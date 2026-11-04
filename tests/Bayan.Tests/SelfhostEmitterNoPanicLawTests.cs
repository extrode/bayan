using System.Diagnostics;
using System.Reflection;
using System.Text;
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
    private static readonly string[] Modes = ["debug", "safe", "fast"];

    private const ulong CorruptionSeed = 0x9E3779B97F4A7C15;

    private static string Root => Path.Combine(Golden.RepoRoot, "tmp", "l025");

    /// <summary>The corrupted copies of <paramref name="bases"/>: the first <paramref name="count"/> of the 010 no-panic test's random corpus (its size follows <c>BAYAN_LAW_TIER</c>, 1,000 or 10,000, and so does the count taken).</summary>
    internal static byte[][] Corrupted(byte[][] bases, int count)
    {
        var generate = typeof(SelfhostSemanticsNoPanicLawTests).GetMethod("GenerateRandomCorpus", BindingFlags.NonPublic | BindingFlags.Static)!;
        var all = (byte[][])generate.Invoke(null, [bases, CorruptionSeed])!;
        return all.Take(count).ToArray();
    }

    /// <summary>Writes <paramref name="inputs"/> to <c>tmp/l025/&lt;corpus&gt;/</c> and checks the three modes; returns the <c>--many</c> stdout of the last mode.</summary>
    private string RunCorpus(string corpus, IReadOnlyList<(string Name, byte[] Bytes)> inputs, bool checkRejected)
    {
        var dir = Path.Combine(Root, corpus);
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        Directory.CreateDirectory(dir);
        foreach (var (name, bytes) in inputs) File.WriteAllBytes(Path.Combine(dir, name), bytes);
        var rejected = checkRejected
            ? inputs.Where(i => Compilation.Create([SourceText.FromFile(Path.Combine(dir, i.Name))], dir).Diagnostics.HasErrors).Select(i => i.Name).ToHashSet()
            : null;
        var stdout = "";
        foreach (var mode in Modes)
        {
            var clock = Stopwatch.StartNew();
            var (exit, o, e) = SelfhostEmitterHarness.Run("--mode", mode, "--many", dir);
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
        var seeds = SelfhostEmitterEquivalenceLawTests.TierSeeds();
        var programs = SelfhostSemanticsGenerator.GenerateValid(seeds).Concat(SelfhostSemanticsGenerator.GenerateFaulty(seeds)).ToList();
        var bytes = programs.Select(p => (p.Name, Bytes: Encoding.UTF8.GetBytes(p.Source))).ToList();
        RunCorpus("seeds", bytes, checkRejected: true);

        var fixedPrograms = SelfhostSemanticsGeneratorTests.Corpus(SelfhostSemanticsGenerator.Seeds).Select(p => Encoding.UTF8.GetBytes(p.Source)).ToArray();
        var corrupted = Corrupted(fixedPrograms, Environment.GetEnvironmentVariable("BAYAN_LAW_TIER") == "full" ? 10_000 : 1_000);
        RunCorpus("corrupted", corrupted.Select((b, i) => ($"{i:D5}.byn", b)).ToList(), checkRejected: false);

        var run = Directory.GetFiles(Path.Combine(Golden.CasesRoot, "run"), "*.byn").Order(StringComparer.Ordinal)
            .Select(f => (Name: Path.GetFileName(f), Bytes: File.ReadAllBytes(f))).ToList();
        RunCorpus("run", run, checkRejected: false);
    }

    /// <summary>The check the law makes can fail: <c>bayan-emit --many</c> on a missing directory exits 2, which the corpus runner rejects.</summary>
    [Fact]
    public void AnExitOutsideZeroAndOneIsNoticed()
    {
        var (exit, _, _) = SelfhostEmitterHarness.Run("--many", Path.Combine(Root, "no-such-dir"));
        Assert.Equal(2, exit);
    }
}
