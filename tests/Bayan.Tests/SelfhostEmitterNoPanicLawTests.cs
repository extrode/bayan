using System.Diagnostics;
using System.Reflection;
using System.Text;
using Bayan.Backend.C;
using Bayan.Semantics;
using Bayan.Syntax.Text;
using Xunit.Abstractions;

namespace Bayan.Tests;

/// <summary>
/// Law L025 (no emitter panic): <c>bayan-emit --many</c> over the generator's valid and faulty programs on the tier's seeds, a share of
/// corrupted copies of them (the corruption step of <see cref="SelfhostSemanticsNoPanicLawTests"/>, called by reflection so the two laws share one generator,
/// Q826) and the flat <c>run/</c> inputs finishes with exit 0, prints no <c>panic:</c>, and prints <c>exit n</c> only for n = 1, in each of the
/// three modes. A seed the C# checker rejects must print <c>exit 1</c> and one it accepts must not (the rejected side L024 does not cover).
/// </summary>
public sealed class SelfhostEmitterNoPanicLawTests(ITestOutputHelper output)
{
    private static readonly string[] Modes = Enum.GetValues<BuildMode>().Select(m => m.ToString().ToLowerInvariant()).ToArray();

    /// <summary>The bound on one <c>bayan-emit --many</c> run (a mode and a batch), in milliseconds.</summary>
    internal const int RunBoundMs = 120_000;

    private static bool Full => Environment.GetEnvironmentVariable("BAYAN_LAW_TIER") == "full";

    /// <summary>A little under the corrupted inputs the C# checker accepts, measured 2026-11-04: 51 of 1,000 (quick), 533 of 10,000 (full).</summary>
    private const int FloorQuick = 45, FloorFull = 500;

    private const ulong CorruptionSeed = 0x9E3779B97F4A7C15;

    private static string Root => Path.Combine(Golden.RepoRoot, "tmp", "l025");

    /// <summary>The corrupted copies of <paramref name="bases"/>: the first <paramref name="count"/> of the 010 no-panic test's random corpus (its size follows <c>BAYAN_LAW_TIER</c>, 1,000 or 10,000, and so does the count taken).</summary>
    internal static byte[][] Corrupted(byte[][] bases, int count)
    {
        var generate = typeof(SelfhostSemanticsNoPanicLawTests).GetMethod("GenerateRandomCorpus", BindingFlags.NonPublic | BindingFlags.Static)!;
        var all = (byte[][])generate.Invoke(null, [bases, CorruptionSeed])!;
        return all.Take(count).ToArray();
    }

    /// <summary><c>bayan-emit --mode m --many dir</c> with <see cref="RunBoundMs"/> to finish: a run that does not is killed and fails the test naming the batch and mode.</summary>
    internal static (int Exit, string Out, string Err) RunBounded(string batch, string mode, string dir, int boundMs = RunBoundMs)
    {
        if (SelfhostSemanticsHarness.SnapshotDir is not null) return SelfhostEmitterHarness.Run("--mode", mode, "--many", dir);
        var psi = new ProcessStartInfo(SelfhostEmitterHarness.Build()) { RedirectStandardOutput = true, StandardOutputEncoding = Encoding.UTF8, RedirectStandardError = true, StandardErrorEncoding = Encoding.UTF8, UseShellExecute = false };
        foreach (var a in new[] { "--mode", mode, "--many", dir }) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndLfAsync();
        var done = p.WaitForExit(boundMs);
        if (!done) p.Kill(entireProcessTree: true);
        p.WaitForExit();
        Assert.True(done, $"{batch} ({mode}): bayan-emit --many did not finish within {boundMs} ms and was killed");
        return (p.ExitCode, stdout.Result, stderr.Result);
    }

    /// <summary>Writes <paramref name="inputs"/> to <c>tmp/l025/&lt;corpus&gt;/</c> and checks the three modes; returns the <c>--many</c> stdout of the last mode.</summary>
    private string RunCorpus(string corpus, IReadOnlyList<(string Name, byte[] Bytes)> inputs, bool checkRejected, int floor = 0)
    {
        var dir = Path.Combine(Root, corpus);
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        Directory.CreateDirectory(dir);
        foreach (var (name, bytes) in inputs) File.WriteAllBytes(Path.Combine(dir, name), bytes);
        var rejected = checkRejected
            ? inputs.Where(i => Compilation.Create([SourceText.FromFile(Path.Combine(dir, i.Name))], dir).Diagnostics.HasErrors).Select(i => i.Name).ToHashSet()
            : null;
        if (floor > 0)
        {
            var reaching = inputs.Count(i => !Compilation.Create([SourceText.FromFile(Path.Combine(dir, i.Name))], dir).Diagnostics.HasErrors);
            output.WriteLine($"{corpus}: {reaching} of {inputs.Count} inputs pass the C# checker and reach emission (floor {floor})");
            Assert.True(reaching >= floor, $"{corpus}: only {reaching} of {inputs.Count} inputs reach emission, floor {floor}; the corruption no longer exercises the emitter");
        }
        var stdout = "";
        foreach (var mode in Modes)
        {
            var clock = Stopwatch.StartNew();
            var (exit, o, e) = RunBounded(corpus, mode, dir);
            var lastHeader = o.Split('\n').LastOrDefault(l => l.StartsWith("== ", StringComparison.Ordinal)) ?? "(none)";
            Assert.True(exit == 0, $"{corpus} ({mode}): exit {exit} after {lastHeader}\n{e}");
            Assert.DoesNotContain("panic:", e);
            var exits = o.Split('\n').Where(l => l.StartsWith("exit ", StringComparison.Ordinal)).Distinct().ToList();
            Assert.True(exits.All(l => l == "exit 1"), $"{corpus} ({mode}): {string.Join(", ", exits)}");
            if (rejected is not null)
            {
                var sections = SelfhostEmitterEquivalenceLawTests.Sections(o, inputs.Select(i => i.Name).Order(StringComparer.Ordinal).ToList());
                foreach (var (name, _) in inputs)
                    Assert.True(sections[name].StartsWith("exit 1\n", StringComparison.Ordinal) == rejected.Contains(name),
                        $"{corpus}/{name} ({mode}): the C# checker {(rejected.Contains(name) ? "rejects" : "accepts")} it, bayan-emit printed\n{sections[name].Split('\n')[0]}");
            }
            output.WriteLine($"{corpus} ({mode}): {inputs.Count} inputs in {clock.ElapsedMilliseconds} ms");
            stdout = o;
        }
        return stdout;
    }

    /// <summary>Law L025: no corpus input makes <c>bayan-emit</c> panic, trap or exit outside {0, 1}.</summary>
    [Fact]
    [Trait("Law", "L025")]
    public void BayanEmitNeverPanicsOnSeedsCorruptionsAndRunInputs()
    {
        Assert.Equal(Enum.GetValues<BuildMode>().Length, Modes.Length);
        var seeds = SelfhostEmitterEquivalenceLawTests.TierSeeds();
        var programs = SelfhostSemanticsGenerator.GenerateValid(seeds).Concat(SelfhostSemanticsGenerator.GenerateFaulty(seeds)).ToList();
        var bytes = programs.Select(p => (p.Name, Bytes: Encoding.UTF8.GetBytes(p.Source))).ToList();
        RunCorpus("seeds", bytes, checkRejected: true);

        var fixedPrograms = SelfhostSemanticsGeneratorTests.Corpus(SelfhostSemanticsGenerator.Seeds).Select(p => Encoding.UTF8.GetBytes(p.Source)).ToArray();
        var corrupted = Corrupted(fixedPrograms, Full ? 10_000 : 1_000);
        RunCorpus("corrupted", corrupted.Select((b, i) => ($"{i:D5}.byn", b)).ToList(), checkRejected: false, floor: Full ? FloorFull : FloorQuick);

        var run = Directory.GetFiles(Path.Combine(Golden.CasesRoot, "run"), "*.byn").Order(StringComparer.Ordinal)
            .Select(f => (Name: Path.GetFileName(f), Bytes: File.ReadAllBytes(f))).ToList();
        RunCorpus("run", run, checkRejected: false);
    }

    /// <summary>A run past its bound is killed and fails naming the batch and mode.</summary>
    [Fact]
    public void ARunPastItsBoundIsKilledAndNamed()
    {
        var dir = Path.Combine(Root, "bound");
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        Directory.CreateDirectory(dir);
        for (var i = 0; i < 2000; i++) File.WriteAllText(Path.Combine(dir, $"{i:D4}.byn"), "void main() {\n    print(\"x\")\n}\n");
        var failure = Assert.ThrowsAny<Exception>(() => RunBounded("bound", "safe", dir, boundMs: 1));
        Assert.Contains("bound (safe)", failure.Message);
        Assert.Contains("killed", failure.Message);
    }

    /// <summary>The check the law makes can fail: <c>bayan-emit --many</c> on a missing directory exits 2, the usage error the law excludes for file input.</summary>
    [Fact]
    public void AnExitOutsideZeroAndOneIsNoticed()
    {
        var (exit, _, _) = SelfhostEmitterHarness.Run("--many", Path.Combine(Root, "no-such-dir"));
        Assert.Equal(2, exit);
    }
}
