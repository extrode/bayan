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
    /// <summary>The recorded tests: <c>bayan-parse</c>'s CLI modes but the timing, tagged <c>Snapshot=skip</c>.</summary>
    private const string Filter = "FullyQualifiedName~SelfhostParserT13Tests&Snapshot!=skip";

    /// <summary>The file whose sites the schemata build carries: the CLI those tests drive (a whole-module schemata
    /// build re-binds the module once per site, over ten minutes for the parser).</summary>
    private const string Scope = "selfhost/parser/main.byn";

    [Fact]
    public void ReplayMatchesMutantZeroAndFailsOnAKilledMutant()
    {
        var clock = Stopwatch.StartNew();
        var (dir, runs) = Record("selfhost-snapshot", Filter, Scope);
        output.WriteLine($"record {clock.Elapsed.TotalSeconds:F1} s");
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

    /// <summary>A test whose class builds its own program (T5's generated main) records against a schemata build:
    /// the manifest lists one build whose sites are all in <c>ctx.byn</c> (the module file with the fewest sites, so the
    /// per-site re-binding stays under a minute), every run used it, and mutant 0 replays.</summary>
    [Fact]
    public void OwnBuildCallerRecordsAgainstASchemataBuild()
    {
        const string scope = "selfhost/semantics/ctx.byn";
        var clock = Stopwatch.StartNew();
        var (dir, runs) = Record("selfhost-snapshot-t5", "FullyQualifiedName~SelfhostSemanticsT5Tests&Snapshot!=skip", scope);
        output.WriteLine($"record {clock.Elapsed.TotalSeconds:F1} s");
        var build = Assert.Single(JsonNode.Parse(File.ReadAllText(Path.Combine(dir, "manifest.json")))!["builds"]!.AsArray());
        var sites = JsonNode.Parse(File.ReadAllText((string)build!["sites"]!))!["sites"]!.AsArray();
        Assert.NotEmpty(sites);
        Assert.All(sites, s => Assert.Equal(Path.GetFullPath(scope, Golden.RepoRoot), Path.GetFullPath((string)s!["file"]!, Golden.RepoRoot)));
        Assert.All(runs, r => Assert.Equal((string?)build["binary"], (string?)r["program"]));
        var (exit, text) = Replay(dir, 0);
        Assert.True(exit == 0, text);
    }

    /// <summary>A program whose two recording runs differ (the shell's pid) fails the recording, naming the command
    /// and the trait that leaves such a test out.</summary>
    [Fact]
    public void DifferingRecordRunsFailTheRecording()
    {
        var dir = Path.Combine(Golden.RepoRoot, "tmp", "selfhost-snapshot-differ");
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        var e = Assert.Throws<InvalidOperationException>(() => SelfhostSemanticsHarness.RunRecorded(dir, "/bin/sh", null, ["-c", "echo $$"]));
        Assert.Contains("/bin/sh -c echo $$", e.Message);
        Assert.Contains("differ in stdout", e.Message);
        Assert.Contains("tag the test [Trait(\"Snapshot\", \"skip\")] if its output is not deterministic", e.Message);
        Assert.False(File.Exists(Path.Combine(dir, "runs.jsonl")));
    }

    /// <summary>Runs the tests matching <paramref name="filter"/> under <c>BAYAN_SNAPSHOT=tmp/&lt;name&gt;</c> with sites only in
    /// <paramref name="scope"/>; the directory and its recorded runs (at least one).</summary>
    private static (string Dir, List<JsonNode> Runs) Record(string name, string filter, string scope)
    {
        var dir = Path.Combine(Golden.RepoRoot, "tmp", name);
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        var dotnet = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var a in new[] { "test", typeof(SelfhostSnapshotTests).Assembly.Location, "--filter", filter }) dotnet.ArgumentList.Add(a);
        dotnet.Environment["BAYAN_SNAPSHOT"] = dir;
        dotnet.Environment["BAYAN_SNAPSHOT_FILES"] = scope;
        var (exit, text) = Exec(dotnet, TimeSpan.FromMinutes(10));
        Assert.True(exit == 0, $"recording exited {exit}:\n{text}");
        var runs = File.ReadAllLines(Path.Combine(dir, "runs.jsonl")).Select(l => JsonNode.Parse(l)!).ToList();
        Assert.NotEmpty(runs);
        return (dir, runs);
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
