using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
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
    /// <summary>
    /// This class's own directory; <see cref="SelfhostSemanticsCorpusParityTests"/> writes under another one, so the
    /// two classes, which xUnit runs in parallel, never write or delete the same path.
    /// </summary>
    private static string CorpusDir => Path.Combine(Golden.RepoRoot, "tmp", "semantics-corpus", "generator");

    /// <summary>
    /// The C# checker's diagnostics for <paramref name="source"/>, tests excluded as <c>bayan check</c> compiles
    /// (CliApp.cs:274 passes no <c>includeTests</c>), so both oracles see the same declarations.
    /// </summary>
    internal static IReadOnlyList<Diagnostic> Check(string name, string source) =>
        Check(SourceText.FromString(Path.Combine(CorpusDir, name), source));

    internal static IReadOnlyList<Diagnostic> Check(SourceText source) =>
        Compilation.Create([source], CorpusDir, new CompilationOptions(IncludeTests: false)).Diagnostics.Items;

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

    /// <summary>
    /// R6 regression pin: the SHA-256 of every generated program (name, newline, source, NUL), so any change to the
    /// generator or to the lambda and interface generators it calls shows up. Regenerate it deliberately (Q651).
    /// </summary>
    [Fact]
    public void GeneratedCorpusIsPinned()
    {
        var sb = new StringBuilder();
        foreach (var p in Corpus(Seeds)) sb.Append(p.Name).Append('\n').Append(p.Source).Append('\0');
        Assert.Equal(PinnedCorpus, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()))));
    }

    private const string PinnedCorpus = "A38F8E5995F343C0EEDF3062FE05E6119377DCF5114301DF67504A53436290DF";

    /// <summary>
    /// Writes each corpus program alone to <c>&lt;root&gt;/seeds/&lt;name&gt;/main.byn</c>: <c>bayan check</c> on a file
    /// reads its whole directory (CliApp.cs:274, Compilation.cs:117-121), so one directory per program. Each test
    /// class passes its own <paramref name="root"/>.
    /// </summary>
    internal static List<(GeneratedProgram Program, string Path)> WriteCorpus(string root)
    {
        var dir = Path.Combine(root, "seeds");
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        var written = new List<(GeneratedProgram, string)>();
        foreach (var p in Corpus(Seeds))
        {
            var programDir = Path.Combine(dir, Path.GetFileNameWithoutExtension(p.Name));
            Directory.CreateDirectory(programDir);
            var path = Path.Combine(programDir, "main.byn");
            File.WriteAllText(path, p.Source);
            written.Add((p, path));
        }
        return written;
    }

    /// <summary><c>bayan check</c> in-process: exit code, stdout without the timed success summary (CliApp.cs:436), and stderr.</summary>
    internal static (int Exit, string Out, string Err) CliCheck(string path)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var exit = CliApp.Run(["check", path], stdout, stderr);
        return (exit, WithoutSummary(stdout.ToString()), stderr.ToString());
    }

    internal static string WithoutSummary(string text) =>
        string.Join('\n', text.Split('\n').Where(l => !l.StartsWith("◆ checked ", StringComparison.Ordinal)));

    /// <summary>
    /// R6 through the CLI: <c>bayan check</c> on each written corpus file sees that program alone. A valid program
    /// exits 0 with no error line; a faulty one exits nonzero with exactly one error, the code the in-process
    /// checker gives, located on one of the injected lines of that file.
    /// </summary>
    [Fact]
    public void CliCheckSeesEachCorpusProgramAlone()
    {
        var clock = Stopwatch.StartNew();
        var written = WriteCorpus(CorpusDir);
        foreach (var (p, path) in written)
        {
            var (exit, _, err) = CliCheck(path);
            var errorLines = err.Split('\n').Where(l => l.StartsWith("error BYN", StringComparison.Ordinal)).ToList();
            if (p.Fault is null)
            {
                Assert.True(exit == 0 && errorLines.Count == 0, $"{path}: exit {exit}\n{err}");
                continue;
            }
            var expected = Check(p.Name, p.Source).Single(d => d.IsError);
            Assert.True(exit != 0 && errorLines.Count == 1 && errorLines[0].StartsWith($"error {expected.Code}:", StringComparison.Ordinal),
                $"{path}: exit {exit}, expected one {expected.Code}\n{err}");
            var at = err.Split('\n').Single(l => l.TrimStart().StartsWith($"--> {path}:", StringComparison.Ordinal));
            var line = int.Parse(at[(at.IndexOf($"{path}:", StringComparison.Ordinal) + path.Length + 1)..].Split(':')[0]);
            Assert.True(line >= p.FirstLine && line <= p.LastLine, $"{path}: error at line {line}, fault at {p.FirstLine}-{p.LastLine}\n{err}");
        }
        output.WriteLine($"{written.Count} corpus files checked through CliApp in {clock.ElapsedMilliseconds} ms");
    }

    /// <summary>R7 input: <see cref="OneMegabyte"/> is exactly 1 MiB, well typed, and written to <c>tmp/semantics-corpus/generator/big.byn</c>.</summary>
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
    /// <summary>This class's own directory, apart from <see cref="SelfhostSemanticsGeneratorTests"/>'s (they run in parallel).</summary>
    private static string CorpusDir => Path.Combine(Golden.RepoRoot, "tmp", "semantics-corpus", "parity");

    private static string BayanCheck =>
        SelfhostSemanticsHarness.Build(File.ReadAllText(Path.Combine(SelfhostSemanticsHarness.SemanticsDir, "main.byn")));

    /// <summary>
    /// R6: every valid and faulty program, each alone under <c>tmp/semantics-corpus/parity/seeds/&lt;name&gt;/</c>, gets the same
    /// exit code, stdout (minus the timed <c>◆ checked</c> summary) and stderr from <c>bayan-check</c> and <c>bayan check</c>.
    /// </summary>
    [Fact]
    public void GeneratedSeedsAgree()
    {
        foreach (var (_, path) in SelfhostSemanticsGeneratorTests.WriteCorpus(CorpusDir))
        {
            var expected = SelfhostSemanticsGeneratorTests.CliCheck(path);
            var (exit, stdout, stderr) = SelfhostParserTests.Run(BayanCheck, path);
            var actualOut = SelfhostSemanticsGeneratorTests.WithoutSummary(stdout);
            Assert.True(exit == expected.Exit && actualOut == expected.Out && stderr == expected.Err,
                $"{path}: bayan-check exited {exit}, bayan check {expected.Exit}\n--- bayan-check stdout\n{actualOut}\n--- bayan check stdout\n{expected.Out}"
                + $"\n--- bayan-check stderr\n{stderr}\n--- bayan check stderr\n{expected.Err}");
        }
    }

    /// <summary>R7: on <see cref="OneMegabyte"/>, the median of 5 <c>bayan-check</c> runs is at most 10 times <c>bayan check</c>'s.</summary>
    [Fact]
    public void OneMegabyteWithinTenTimes()
    {
        if (Environment.GetEnvironmentVariable("BAYAN_SKIP_TIMING") == "1") return;
        Directory.CreateDirectory(CorpusDir);
        var path = Path.Combine(CorpusDir, "big.byn");
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
        var csharp = Median(() => Assert.Equal(0, SelfhostSemanticsGeneratorTests.CliCheck(path).Exit));
        var selfhost = Median(() => Assert.Equal(0, SelfhostParserTests.Run(program, path).Exit));
        output.WriteLine($"bayan check {csharp:F0} ms, bayan-check {selfhost:F0} ms, ratio {selfhost / csharp:F2}");
        Assert.True(selfhost <= 10 * csharp, $"bayan-check {selfhost:F0} ms is more than 10x bayan check {csharp:F0} ms");
    }
}
