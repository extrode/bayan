using System.Diagnostics;
using System.Text.Json.Nodes;
using Xunit.Abstractions;

namespace Bayan.Tests;

/// <summary>
/// Spec 013 R14: a <c>BAYAN_SNAPSHOT</c> recording of selfhost tests replays through <c>scripts/selfhost/replay.py</c>
/// without <c>dotnet test</c>; mutant 0 matches every recorded run and a mutant the tests kill does not.
/// </summary>
public sealed class SelfhostSnapshotTests(ITestOutputHelper output)
{
    /// <summary>The recorded tests: <c>bayan-parse</c>'s CLI modes, one of them a timing.</summary>
    private const string Filter = "FullyQualifiedName~SelfhostParserT13Tests";

    /// <summary>The file whose sites the schemata build carries: the CLI those tests drive (a whole-module schemata
    /// build re-binds the module once per site, over ten minutes for the parser).</summary>
    private const string Scope = "selfhost/parser/main.byn";

    [Fact]
    public void ReplayMatchesMutantZeroAndFailsOnAKilledMutant()
    {
        var dir = Path.Combine(Golden.RepoRoot, "tmp", "selfhost-snapshot");
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        var clock = Stopwatch.StartNew();
        var dotnet = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var a in new[] { "test", typeof(SelfhostSnapshotTests).Assembly.Location, "--filter", Filter }) dotnet.ArgumentList.Add(a);
        dotnet.Environment["BAYAN_SNAPSHOT"] = dir;
        dotnet.Environment["BAYAN_SNAPSHOT_FILES"] = Scope;
        var (recordExit, recordOut) = Exec(dotnet, TimeSpan.FromMinutes(10));
        Assert.True(recordExit == 0, $"recording exited {recordExit}:\n{recordOut}");
        output.WriteLine($"record {clock.Elapsed.TotalSeconds:F1} s");

        var runs = File.ReadAllLines(Path.Combine(dir, "runs.jsonl")).Select(l => JsonNode.Parse(l)!).ToList();
        Assert.NotEmpty(runs);
        var time = Assert.Single(runs, r => r["args"]!.AsArray().Any(a => (string?)a == "--time"));
        Assert.DoesNotContain("stdout", time["compare"]!.AsArray().Select(c => (string?)c));
        var build = Assert.Single(JsonNode.Parse(File.ReadAllText(Path.Combine(dir, "manifest.json")))!["builds"]!.AsArray());
        Assert.All(runs, r => Assert.Equal((string?)build!["binary"], (string?)r["program"]));

        clock.Restart();
        var (exit, text) = Replay(dir, 0);
        output.WriteLine($"replay of mutant 0: {clock.Elapsed.TotalSeconds:F2} s, {runs.Count} runs: {text}");
        Assert.True(exit == 0, text);

        var sites = JsonNode.Parse(File.ReadAllText((string)build!["sites"]!))!["sites"]!.AsArray();
        Assert.NotEmpty(sites);
        Assert.All(sites, s => Assert.Equal(Path.GetFullPath(Scope, Golden.RepoRoot), Path.GetFullPath((string)s!["file"]!, Golden.RepoRoot)));
        foreach (var site in sites.Take(50))
        {
            var (mutantExit, mutantText) = Replay(dir, (int)site!["id"]!);
            if (mutantExit is 0 or -1) continue;
            output.WriteLine($"mutant {site["id"]} ({site["file"]}:{site["line"]}): {mutantText}");
            Assert.Equal(1, mutantExit);
            Assert.Contains("replay: run ", mutantText);
            return;
        }
        Assert.Fail("none of the first 50 mutants was killed by the recorded runs");
    }

    private static (int Exit, string Text) Replay(string dir, int mutant)
    {
        var psi = new ProcessStartInfo("python3") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var a in new[] { "-I", Path.Combine(Golden.RepoRoot, "scripts", "selfhost", "replay.py"), dir }) psi.ArgumentList.Add(a);
        psi.Environment["BAYAN_MUTANT"] = mutant.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return Exec(psi, TimeSpan.FromSeconds(10));
    }

    /// <summary>Runs <paramref name="psi"/>; the exit code (-1 after a kill at <paramref name="limit"/>) and stdout plus stderr.</summary>
    private static (int Exit, string Text) Exec(ProcessStartInfo psi, TimeSpan limit)
    {
        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        if (!p.WaitForExit(limit))
        {
            p.Kill(entireProcessTree: true);
            p.WaitForExit();
            return (-1, "timed out");
        }
        return (p.ExitCode, stdout.Result + stderr.Result);
    }
}
