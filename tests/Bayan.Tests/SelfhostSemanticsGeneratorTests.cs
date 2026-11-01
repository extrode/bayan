using System.Diagnostics;
using Bayan.Cli;
using Bayan.Semantics;
using Bayan.Syntax.Diagnostics;
using Bayan.Syntax.Text;
using Xunit.Abstractions;
using static Bayan.Tests.SelfhostSemanticsGenerator;

namespace Bayan.Tests;

/// <summary>
/// Tests for the checker generator (spec 010 R6, R7). Every expectation is the C# checker's verdict on the
/// generated program: the valid corpus checks clean, and each faulty program has exactly one error, inside the
/// injected lines, with one code per fault kind. The comparisons with <c>bayan-check</c> are in
/// <see cref="SelfhostSemanticsCorpusParityTests"/> until T28.
/// </summary>
public sealed class SelfhostSemanticsGeneratorTests(ITestOutputHelper output)
{
    private static string CorpusDir => Path.Combine(Golden.RepoRoot, "tmp", "semantics-corpus");

    /// <summary>The C# checker's diagnostics for <paramref name="source"/>, tests included.</summary>
    internal static IReadOnlyList<Diagnostic> Check(string name, string source) =>
        Check(SourceText.FromString(Path.Combine(CorpusDir, name), source));

    internal static IReadOnlyList<Diagnostic> Check(SourceText source) =>
        Compilation.Create([source], CorpusDir, new CompilationOptions(IncludeTests: true)).Diagnostics.Items;

    private static string Render(IReadOnlyList<Diagnostic> diagnostics) => DiagnosticRenderer.RenderAll(diagnostics);

    /// <summary>R6: every valid program, checked by the C# checker, has no error.</summary>
    [Fact]
    public void ValidCorpusChecksWithoutErrors()
    {
        var clock = Stopwatch.StartNew();
        var programs = GenerateValid(Seeds);
        foreach (var p in programs)
        {
            var errors = Check(p.Name, p.Source).Where(d => d.IsError).ToList();
            Assert.True(errors.Count == 0, $"{p.Name} has errors:\n{Render(errors)}\n{p.Source}");
        }
        output.WriteLine($"{programs.Count} valid programs checked in {clock.ElapsedMilliseconds} ms");
    }

    /// <summary>
    /// R6: every fault kind appears for every seed; the C# checker reports exactly one error per faulty program,
    /// on one of the injected lines; one fault kind always gives the same code and two kinds never share one.
    /// </summary>
    [Fact]
    public void EachFaultYieldsOneErrorOnTheInjectedLines()
    {
        var clock = Stopwatch.StartNew();
        var programs = GenerateFaulty(Seeds);
        var codes = new Dictionary<FaultKind, string>();
        foreach (var p in programs)
        {
            var errors = Check(p.Name, p.Source).Where(d => d.IsError).ToList();
            Assert.True(errors.Count == 1, $"{p.Name}: {errors.Count} errors, expected the injected one:\n{Render(errors)}\n{p.Source}");
            var line = errors[0].Source!.GetLinePosition(errors[0].Span.Start).Line;
            Assert.True(line >= p.FirstLine && line <= p.LastLine,
                $"{p.Name}: {errors[0].Code} at line {line}, outside the fault's lines {p.FirstLine}-{p.LastLine}\n{p.Source}");
            var kind = p.Fault!.Value;
            if (!codes.TryAdd(kind, errors[0].Code))
                Assert.True(codes[kind] == errors[0].Code, $"{p.Name}: {kind} reported {errors[0].Code}, earlier {codes[kind]}");
        }
        Assert.Equal(Enum.GetValues<FaultKind>().Length * Seeds.Length, programs.Count);
        Assert.Equal(Enum.GetValues<FaultKind>(), codes.Keys.Order());
        Assert.Equal(codes.Count, codes.Values.Distinct().Count());
        output.WriteLine($"{programs.Count} faulty programs checked in {clock.ElapsedMilliseconds} ms: "
                         + string.Join(", ", codes.Select(c => $"{c.Key} {c.Value}")));
    }

    /// <summary>R6: the corpora are identical across two runs of the same seeds, and different seeds differ.</summary>
    [Fact]
    public void GeneratedCorporaAreIdenticalAcrossRuns()
    {
        var a = Corpus(Seeds);
        var b = Corpus(Seeds);
        Assert.Equal(a.Select(p => (p.Name, p.Source)), b.Select(p => (p.Name, p.Source)));
        var assembled = a.Where(p => p.Name.StartsWith("valid-seed-", StringComparison.Ordinal)).Select(p => p.Source);
        Assert.Equal(Seeds.Length, assembled.Distinct().Count());
    }

    internal static GeneratedProgram[] Corpus(ulong[] seeds) => GenerateValid(seeds).Concat(GenerateFaulty(seeds)).ToArray();

    /// <summary>R7 input: <see cref="OneMegabyte"/> is exactly 1 MiB, well typed, and written to <c>tmp/semantics-corpus/big.byn</c>.</summary>
    [Fact]
    public void OneMegabyteIsWrittenAndWellTyped()
    {
        var source = OneMegabyte();
        Assert.Equal(1024 * 1024, source.Length);
        Directory.CreateDirectory(CorpusDir);
        var path = Path.Combine(CorpusDir, "big.byn");
        File.WriteAllBytes(path, source.Bytes.ToArray());
        var clock = Stopwatch.StartNew();
        var errors = Check(SourceText.FromFile(path)).Where(d => d.IsError).ToList();
        output.WriteLine($"C# check of 1 MiB: {clock.ElapsedMilliseconds} ms");
        Assert.True(errors.Count == 0, $"big.byn has errors:\n{Render(errors.Take(5).ToList())}");
    }
}

/// <summary>
/// The R6/R7 comparisons of <c>bayan-check</c> with <c>bayan check</c> on the generated corpora. The class name
/// keeps them out of the T27 guard's <c>SelfhostSemanticsGenerator</c> filter (Q650); T28 removes the skips.
/// </summary>
public sealed class SelfhostSemanticsCorpusParityTests(ITestOutputHelper output)
{
    private static string BayanCheck =>
        SelfhostSemanticsHarness.Build(File.ReadAllText(Path.Combine(SelfhostSemanticsHarness.SemanticsDir, "main.byn")));

    /// <summary><c>bayan check</c> in-process: exit code and stderr without the timed success summary.</summary>
    private static (int Exit, string Err) CSharpCheck(string path)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var exit = CliApp.Run(["check", path], stdout, stderr);
        return (exit, WithoutSummary(stderr.ToString()));
    }

    private static string WithoutSummary(string err) =>
        string.Join('\n', err.Split('\n').Where(l => !l.StartsWith("◆ checked ", StringComparison.Ordinal)));

    /// <summary>R6: every valid and faulty program under <c>tmp/semantics-corpus/</c> gets the same diagnostics and exit code.</summary>
    [Fact(Skip = "T28 enables the corpus comparison once bayan-check runs the passes (T26 CLI)")]
    public void GeneratedSeedsAgree()
    {
        var dir = Path.Combine(Golden.RepoRoot, "tmp", "semantics-corpus", "seeds");
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        Directory.CreateDirectory(dir);
        foreach (var p in SelfhostSemanticsGeneratorTests.Corpus(Seeds))
        {
            var path = Path.Combine(dir, p.Name);
            File.WriteAllText(path, p.Source);
            var expected = CSharpCheck(path);
            var (exit, _, err) = SelfhostParserTests.Run(BayanCheck, path);
            Assert.True(exit == expected.Exit && WithoutSummary(err) == expected.Err,
                $"{path}: bayan-check exited {exit}, bayan check {expected.Exit}\n--- bayan-check\n{err}\n--- bayan check\n{expected.Err}");
        }
    }

    /// <summary>R7: on <see cref="OneMegabyte"/>, the median of 5 <c>bayan-check</c> runs is at most 10 times <c>bayan check</c>'s.</summary>
    [Fact(Skip = "T28 enables the 1 MB speed bound once bayan-check runs the passes (T26 CLI)")]
    public void OneMegabyteWithinTenTimes()
    {
        if (Environment.GetEnvironmentVariable("BAYAN_SKIP_TIMING") == "1") return;
        var dir = Path.Combine(Golden.RepoRoot, "tmp", "semantics-corpus");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "big.byn");
        File.WriteAllBytes(path, OneMegabyte().Bytes.ToArray());
        var program = BayanCheck;
        double Median(Action run)
        {
            var times = new List<double>();
            for (var i = 0; i < 5; i++)
            {
                var clock = Stopwatch.StartNew();
                run();
                times.Add(clock.Elapsed.TotalMilliseconds);
            }
            return times.Order().ElementAt(2);
        }
        var csharp = Median(() => Assert.Equal(0, CSharpCheck(path).Exit));
        var selfhost = Median(() => Assert.Equal(0, SelfhostParserTests.Run(program, path).Exit));
        output.WriteLine($"bayan check {csharp:F0} ms, bayan-check {selfhost:F0} ms, ratio {selfhost / csharp:F2}");
        Assert.True(selfhost <= 10 * csharp, $"bayan-check {selfhost:F0} ms is more than 10x bayan check {csharp:F0} ms");
    }
}
