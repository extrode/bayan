using System.Diagnostics;
using System.Text;
using Bayan.Backend.C;
using Bayan.Cli;
using Bayan.Semantics;
using Bayan.Syntax.Diagnostics;
using Bayan.Syntax.Text;
using Xunit.Abstractions;

namespace Bayan.Tests;

/// <summary>
/// Law L024 (emitter equivalence): the C that <c>bayan-emit</c> prints is the C that <c>CliApp.EmitC</c> returns for the
/// same input and mode. Corpora: the generator's well-typed programs on the fixed seeds, each in <c>debug</c>, <c>safe</c>
/// and <c>fast</c> (D10, R6), and the flat <c>run/</c> inputs in the mode their <c>// mode:</c> line names (the <c>*.test.byn</c>
/// ones as <c>bayan test -v</c> builds them). Each corpus is one directory per mode and one <c>bayan-emit --many</c> process, whose
/// <c>== name</c> sections are compared with the C# text of the same input (<c>Compilation.Create([source], dir)</c>, the
/// <c>--package</c> compile of RunTests.cs:80-82).
/// </summary>
public sealed class SelfhostEmitterEquivalenceLawTests(ITestOutputHelper output)
{
    private static readonly string[] Modes = Enum.GetValues<BuildMode>().Select(m => m.ToString().ToLowerInvariant()).ToArray();

    /// <summary>The seeds of the quick tier (Q823): the generator's eight fixed seeds, then a xorshift64 chain from the first. <c>BAYAN_LAW_TIER=full</c> runs <see cref="FullSeeds"/>.</summary>
    internal const int QuickSeeds = 256;

    internal const int FullSeeds = 1024;

    private static string Root => Path.Combine(Golden.RepoRoot, "tmp", "l024");

    /// <summary>The seeds of the tier (<c>BAYAN_LAW_TIER</c>): the fixed ones, then the chain; the chain is the same prefix at either size.</summary>
    internal static ulong[] TierSeeds()
    {
        var count = Environment.GetEnvironmentVariable("BAYAN_LAW_TIER") == "full" ? FullSeeds : QuickSeeds;
        var seeds = SelfhostSemanticsGenerator.Seeds.ToList();
        var state = seeds[0];
        while (seeds.Count < count)
        {
            state ^= state << 13;
            state ^= state >> 7;
            state ^= state << 17;
            seeds.Add(state);
        }
        return seeds.ToArray();
    }

    /// <summary>The R6 switch (Q825): a test-only change to the C# backend's text, named by <c>BAYAN_L024_BREAK</c> (<c>cast</c> or <c>fields</c>).</summary>
    private static Func<string, string>? EnvBreak => Environment.GetEnvironmentVariable("BAYAN_L024_BREAK") switch
    {
        "cast" => DropOneCast,
        "fields" => SwapTwoFieldLines,
        _ => null,
    };

    /// <summary>Drops the first <c>(int64_t)</c> cast of the text.</summary>
    internal static string DropOneCast(string c)
    {
        var at = c.IndexOf("(int64_t)", StringComparison.Ordinal);
        return at < 0 ? c : c.Remove(at, "(int64_t)".Length);
    }

    /// <summary>Swaps the first two adjacent, different field lines of a <c>struct … {</c> block.</summary>
    internal static string SwapTwoFieldLines(string c)
    {
        var lines = c.Split('\n');
        for (var i = 1; i + 1 < lines.Length; i++)
        {
            if (!lines[i - 1].StartsWith("struct ", StringComparison.Ordinal) || !lines[i - 1].EndsWith('{')) continue;
            if (!lines[i].StartsWith("    ", StringComparison.Ordinal) || !lines[i].EndsWith(';')) continue;
            if (!lines[i + 1].StartsWith("    ", StringComparison.Ordinal) || !lines[i + 1].EndsWith(';') || lines[i] == lines[i + 1]) continue;
            (lines[i], lines[i + 1]) = (lines[i + 1], lines[i]);
            return string.Join('\n', lines);
        }
        return c;
    }

    /// <summary>One input of a corpus: its file name in the corpus directory and the C# text for each mode (null when the C# rejects it).</summary>
    internal sealed record Input(string Name, string Source);

    /// <summary>The C# side for <paramref name="input"/> written to <paramref name="dir"/>: <c>EmitC</c>, or the test run's C for a <c>*.test.byn</c>.</summary>
    internal static string? Expected(string dir, Input input, string mode)
    {
        var path = Path.Combine(dir, input.Name);
        Toolchain.TryParseMode(mode, out var buildMode);
        if (input.Name.EndsWith(".test.byn", StringComparison.Ordinal))
            return SelfhostEmitterHarness.TestRunC(path, Path.Combine(Root, "work", Path.GetFileNameWithoutExtension(input.Name), mode));
        var c = Compilation.Create([SourceText.FromFile(path)], dir);
        return c.Diagnostics.HasErrors ? null : CliApp.EmitC(c, buildMode);
    }

    /// <summary>Splits <c>--many</c> stdout into the section after each <c>== name</c> header, in <paramref name="names"/> order.</summary>
    internal static Dictionary<string, string> Sections(string stdout, IReadOnlyList<string> names)
    {
        var text = "\n" + stdout;
        var starts = new List<int>();
        var at = 0;
        foreach (var name in names)
        {
            var found = text.IndexOf($"\n== {name}\n", at, StringComparison.Ordinal);
            Assert.True(found >= 0, $"bayan-emit --many printed no section for {name}");
            starts.Add(found);
            at = found + 1;
        }
        var sections = new Dictionary<string, string>();
        for (var i = 0; i < names.Count; i++)
        {
            var body = starts[i] + $"\n== {names[i]}\n".Length;
            var end = i + 1 < names.Count ? starts[i + 1] + 1 : text.Length;
            sections[names[i]] = text[body..end];
        }
        return sections;
    }

    /// <summary>
    /// Writes <paramref name="inputs"/> to <c>tmp/l024/&lt;corpus&gt;/</c>, runs <c>bayan-emit --mode m --many</c> on it and compares each
    /// section with the C# text, mutated by <paramref name="mutate"/> if given. Returns the first difference as
    /// <c>input (mode): line n …</c>, or null; the count of compared inputs is added to <paramref name="compared"/>.
    /// </summary>
    internal static string? FirstDifference(string corpus, IReadOnlyList<Input> inputs, string mode, Func<string, string>? mutate, ref int compared)
    {
        var dir = Path.Combine(Root, corpus);
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        Directory.CreateDirectory(dir);
        foreach (var input in inputs) File.WriteAllText(Path.Combine(dir, input.Name), input.Source);
        var expected = inputs.Select(i => (Input: i, C: Expected(dir, i, mode))).Where(p => p.C is not null).ToList();
        // A file the C# rejects is not in the law (it needs bayan check to accept it); take it out so --many prints only compared sections.
        foreach (var input in inputs.Where(i => expected.All(p => p.Input != i))) File.Delete(Path.Combine(dir, input.Name));
        var names = expected.Select(p => p.Input.Name).Order(StringComparer.Ordinal).ToList();
        var (exit, stdout, stderr) = SelfhostEmitterHarness.Run("--mode", mode, "--many", dir);
        Assert.True(exit == 0, $"{corpus} ({mode}): bayan-emit --many exited {exit}\n{stderr}");
        Assert.DoesNotContain("panic:", stderr);
        var sections = Sections(stdout, names);
        foreach (var (input, c) in expected.OrderBy(p => p.Input.Name, StringComparer.Ordinal))
        {
            compared++;
            var want = (mutate ?? (x => x))(c!).Replace("\r\n", "\n");
            var got = sections[input.Name];
            if (got == want) continue;
            var e = want.Split('\n');
            var a = got.Split('\n');
            for (var n = 0; n < Math.Max(e.Length, a.Length); n++)
            {
                var el = n < e.Length ? e[n] : "<end>";
                var al = n < a.Length ? a[n] : "<end>";
                if (el != al) return $"{corpus}/{input.Name} ({mode}): line {n + 1} differs\nC#:    {el}\nBayan: {al}";
            }
        }
        return null;
    }

    /// <summary>The generator's well-typed programs for <paramref name="seeds"/>, as <c>Name</c> files of one module each.</summary>
    internal static List<Input> SeedInputs(ulong[] seeds) =>
        SelfhostSemanticsGenerator.GenerateValid(seeds).Select(p => new Input(p.Name, p.Source)).ToList();

    /// <summary>The flat <c>run/</c> inputs whose <c>// mode:</c> is <paramref name="mode"/>; the <c>*.test.byn</c> ones only in <c>safe</c>, as <c>bayan test</c> builds them.</summary>
    internal static List<Input> RunInputs(string mode) =>
        Directory.GetFiles(Path.Combine(Golden.CasesRoot, "run"), "*.byn").Order(StringComparer.Ordinal)
            .Where(f => f.EndsWith(".test.byn", StringComparison.Ordinal) ? mode == "safe" : SelfhostEmitterHarness.ModeOf(f) == mode)
            .Select(f => new Input(Path.GetFileName(f), File.ReadAllText(f))).ToList();

    /// <summary>Law L024: every corpus input prints the C# backend's C in all three modes.</summary>
    [Fact]
    [Trait("Law", "L024")]
    public void BayanEmitEqualsTheCSharpBackendOnSeedsAndRunInputs()
    {
        Assert.Equal(Enum.GetValues<BuildMode>().Length, Modes.Length);
        var clock = Stopwatch.StartNew();
        var seeds = TierSeeds();
        var programs = SeedInputs(seeds);
        Assert.Equal(programs.Count, programs.Select(p => p.Name).Distinct().Count());
        var compared = 0;
        foreach (var mode in Modes)
        {
            var before = compared;
            Assert.Null(FirstDifference($"seeds-{mode}", programs, mode, EnvBreak, ref compared));
            output.WriteLine($"seeds ({mode}): {compared - before} of {programs.Count} compared");
            before = compared;
            Assert.Null(FirstDifference($"run-{mode}", RunInputs(mode), mode, EnvBreak, ref compared));
            output.WriteLine($"run ({mode}): {compared - before} compared");
        }
        output.WriteLine($"L024: {seeds.Length} seeds, {compared} comparisons in {clock.Elapsed.TotalSeconds:F1} s");
    }

    /// <summary>R6 break-test (Q825): the same comparison with one deliberate change to the C# text fails on at least one seed, for each change.</summary>
    [Theory]
    [InlineData("cast")]
    [InlineData("fields")]
    public void ADeliberateChangeToTheCSharpTextFailsAtLeastOneSeed(string change)
    {
        Func<string, string> mutate = change == "cast" ? DropOneCast : SwapTwoFieldLines;
        var programs = SeedInputs(SelfhostSemanticsGenerator.Seeds);
        var failed = new List<string>();
        foreach (var mode in Modes)
        {
            var compared = 0;
            if (FirstDifference($"break-{change}-{mode}", programs, mode, mutate, ref compared) is { } difference) failed.Add(difference);
        }
        Assert.True(failed.Count > 0, $"no seed noticed '{change}' in any mode");
        output.WriteLine(failed[0]);
    }

    /// <summary>The mutators change the text they are given, and leave a text without their pattern alone.</summary>
    [Fact]
    public void TheMutatorsChangeOnlyWhatTheyName()
    {
        Assert.Equal("x = 1;", DropOneCast("x = (int64_t)1;"));
        Assert.Equal("x", DropOneCast("x"));
        Assert.Equal("struct S {\n    b;\n    a;\n};", SwapTwoFieldLines("struct S {\n    a;\n    b;\n};"));
        Assert.Equal("a;\nb;", SwapTwoFieldLines("a;\nb;"));
    }

    /// <summary>A text-only check of <see cref="Sections"/>, the splitter both laws rely on.</summary>
    [Fact]
    public void SectionsSplitOnTheNamedHeaders()
    {
        var s = Sections("== a.byn\nint a;\n== b.byn\nint b;\n", ["a.byn", "b.byn"]);
        Assert.Equal("int a;\n", s["a.byn"]);
        Assert.Equal("int b;\n", s["b.byn"]);
    }

    /// <summary>
    /// R7 (Q827): the line-start table gives the lines the C# <c>GetLinePosition</c> does. A fixture with statements on the first line, in
    /// the middle, split over several lines and ending on the file's last byte (no trailing newline), emitted in the three modes.
    /// </summary>
    [Fact]
    public void LineNumbersInLineDirectivesAndRuntimeCallsMatchTheCSharp()
    {
        var dir = Path.Combine(Root, "lines");
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        Directory.CreateDirectory(dir);
        const string source = "i64 half(i64 n) => n / 2\n\nvoid main() {\n    var a: i64 = 7\n    var b: i64 = (a\n        + 1)\n    let c = i32(\n        b)\n\n    print($\"{c / 2} {half(a * b)}\")\n}\n\nnever stop() {\n    panic(\"stop\")\n}\n\nnever stop2(i64 n) {\n    let z = stop()\n}\n\ni64 last() => 7";
        var path = Path.Combine(dir, "main.byn");
        File.WriteAllText(path, source);
        foreach (var mode in Modes)
        {
            var expected = Expected(dir, new Input("main.byn", source), mode)!;
            Toolchain.TryParseMode(mode, out _);
            var (exit, actual, error) = SelfhostEmitterHarness.Run("--mode", mode, "--package", dir, path);
            Assert.True(exit == 0, $"{mode}: exit {exit}\n{error}");
            // The C# text names the first line, a middle one, the last line and, on it, a node (the `7`) that starts at the file's last byte.
            var lines = System.Text.RegularExpressions.Regex.Matches(expected, @"#line (\d+) ").Select(m => int.Parse(m.Groups[1].Value)).ToHashSet();
            var total = source.Split('\n').Length;
            Assert.Equal(21, total);
            Assert.Contains(1, lines);
            Assert.Contains(total / 2, lines);
            Assert.Contains(total, lines);
            Assert.Contains("byn_unreachable(", expected);
            SelfhostEmitterHarness.AssertSame($"lines ({mode})", expected, actual);
        }
    }

    // A frame built by hand gets its line starts from `lineStartsOf`, as T2, T10 and T11 build theirs. args: the file to check.
    private const string HandFrameMain = """
        import semantics
        import emitter.cprint
        import emitter.ltree

        type CFunc = cprint.CFunc

        i32 main(ReadOnlySpan<string> args) {
            var rest = List<string>()
            for i in 0..args.length { rest.append(args[i]) }
            let symbols = semantics.t26Symbols(ascii: false)
            let o = semantics.t26ParseBuildOptions(in rest, allowSpans: false) ?? return 2
            var reported = List<semantics.T26Reported>()
            var ok = false
            var c = semantics.t26Compile(in o, by: in symbols, to: ref reported, through: ref ok)
            var l = LCtx {}
            let p = lower(ref c, to: ref l)
            var sb = StrBuf()
            for i in 0..p.functions.length {
                let f = CFunc { fn: p.functions[i].symbol, file: "x", lineStarts: cprint.lineStartsOf(in c, for: c.functions[p.functions[i].symbol].file) }
                sb.append($"{c.functions[f.fn].name} {cprint.lineOf(in c, by: in f, at: semantics.nodeSpan(in c, for: p.functions[i].body))}\n")
            }
            Io.stdout.write($"{sb.finish()}")
            return 0
        }
        """;

    /// <summary>
    /// <c>lineOf</c> on a frame built by hand with <c>lineStartsOf</c> gives the line of <c>LineOf</c> for each function body.
    /// </summary>
    [Fact]
    public void LineOfOnAFrameBuiltByHandMatchesTheCSharp()
    {
        var dir = Path.Combine(Root, "hand");
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        Directory.CreateDirectory(dir);
        const string source = "i64 half(i64 n) => n / 2\n\nvoid main() {\n    var a: i64 = 7\n    print($\"{half(a)}\")\n}";
        var path = Path.Combine(dir, "main.byn");
        File.WriteAllText(path, source);
        var emitter = SelfhostEmitterHarness.EmitterDir;
        var sources = Directory.GetFiles(emitter, "*.byn")
            .Where(f => Path.GetFileName(f) != "main.byn")
            .Order(StringComparer.Ordinal)
            .Select(SourceText.FromFile)
            .ToList();
        sources.Add(SourceText.FromString(Path.Combine(emitter, "main.byn"), HandFrameMain));
        Environment.SetEnvironmentVariable("BAYAN_CACHE", Path.Combine(Golden.RepoRoot, "tmp", "cache"));
        var built = Compilation.Create(sources, packageRoot: SelfhostSemanticsHarness.SelfhostDir);
        Assert.False(built.Diagnostics.HasErrors, DiagnosticRenderer.RenderAll(built.Diagnostics.Items, includePath: true));
        var build = SelfhostSemanticsHarness.BuildFast(built, new HashSet<string>(StringComparer.Ordinal) { sources[^1].Path });
        Assert.True(build.Succeeded, build.Errors);
        var (exit, actual, error) = SelfhostParserTests.Run(build.ExecutablePath!, [path]);
        Assert.True(exit == 0, $"exit {exit}\n{error}");
        var compilation = Compilation.Create([SourceText.FromFile(path)], dir);
        var expected = compilation.Modules.SelectMany(m => m.AllFunctions)
            .Where(fn => fn.Body is not null && fn.File is not null)
            .ToDictionary(fn => fn.Name, fn => fn.File!.Source.GetLinePosition(fn.Body!.Syntax.Span.Start).Line);
        var seen = 0;
        foreach (var row in actual.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = row.Split(' ');
            if (!expected.TryGetValue(parts[0], out var line)) continue;
            Assert.Equal($"{parts[0]} {line}", row);
            seen++;
        }
        Assert.True(seen >= 2, actual);
    }
}
