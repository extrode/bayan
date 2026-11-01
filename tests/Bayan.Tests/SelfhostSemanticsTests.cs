using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using Bayan.Backend.C;
using Bayan.Cli;
using Bayan.Semantics;
using Bayan.Syntax.Diagnostics;
using Bayan.Syntax.Packages;
using Bayan.Syntax.Text;

namespace Bayan.Tests;

/// <summary>
/// <c>selfhost/semantics</c>, the self-hosted checker: T1's skeleton checks, and T26's modes
/// (<c>check</c>, <c>types</c>, <c>--golden</c>) against the C# <see cref="CliApp"/>, <see cref="Compilation"/>
/// and <see cref="Messages"/>. Both sides use the Unicode symbols: <c>TestSetup</c> sets a UTF-8 <c>LANG</c>,
/// and the self-hosted renderer has no ASCII style yet (Q646).
/// </summary>
public sealed class SelfhostSemanticsTests
{
    private static string SelfhostDir => Path.Combine(Golden.RepoRoot, "selfhost");

    private static string SemanticsDir => Path.Combine(SelfhostDir, "semantics");

    /// <summary>
    /// The built <c>bayan-check</c>, or the compiler's complaint. The package root is <c>selfhost/</c>
    /// so that <c>import parser</c> and <c>import lexer</c> resolve (Q430).
    /// </summary>
    private static readonly Lazy<(string? Path, string Error)> BayanCheck = new(() =>
        Build(Directory.GetFiles(SemanticsDir, "*.byn").Order(StringComparer.Ordinal).Select(SourceText.FromFile).ToList()));

    /// <summary><paramref name="sources"/> built as the entry module, or the compiler's complaint.</summary>
    private static (string? Path, string Error) Build(List<SourceText> sources)
    {
        Environment.SetEnvironmentVariable("BAYAN_CACHE", Path.Combine(Golden.RepoRoot, "tmp", "cache"));
        var c = Compilation.Create(sources, packageRoot: SelfhostDir);
        if (c.Diagnostics.HasErrors) return (null, DiagnosticRenderer.RenderAll(c.Diagnostics.Items, includePath: true));
        var build = Toolchain.Build(CliApp.EmitC(c, BuildMode.Fast), BuildMode.Fast);
        return build.Succeeded ? (build.ExecutablePath, "") : (null, build.Errors);
    }

    private static string Built
    {
        get
        {
            var (path, error) = BayanCheck.Value;
            if (path is null) Assert.Fail($"selfhost/semantics did not build:\n{error}");
            return path!;
        }
    }

    /// <summary>A scratch entry module, checked (not built) with the parser as its package's import.</summary>
    private static Compilation CheckScratch(string source) =>
        Compilation.Create([SourceText.FromString("main.byn", source)], packageRoot: SelfhostDir);

    [Fact]
    public void Builds()
    {
        Assert.True(File.Exists(Built));
    }

    /// <summary>The stub with no command: <c>bayan</c>'s usage exit, nothing on stdout, a usage line on stderr.</summary>
    [Fact]
    public void NoCommandIsUsage()
    {
        var expected = CliApp.Run([], new StringWriter(), new StringWriter());
        var (exit, output, error) = SelfhostParserTests.Run(Built);
        Assert.Equal(expected, exit);
        Assert.Equal("", output);
        Assert.StartsWith("usage: bayan-check", error);
    }

    /// <summary>T1 made the parser's types and its two entry points <c>public</c>, so the checker can read them.</summary>
    [Fact]
    public void ParserTypesArePublic()
    {
        var c = CheckScratch("""
            import lexer
            import parser

            i32 main() {
                let bytes = List<u8>()
                let source = lexer.SourceText.create("<probe>", from: move bytes)
                var p = parser.Parser.create(lexer.lex(move source, by: lexer.Keywords.build()))
                let unit: parser.CompilationUnit = parser.parseCompilationUnit(ref p)
                return i32(unit.members.length)
            }
            """);
        Assert.False(c.Diagnostics.HasErrors, DiagnosticRenderer.RenderAll(c.Diagnostics.Items, includePath: true));
    }

    /// <summary>The break-test of <see cref="ParserTypesArePublic"/>: an entry point T1 left private is BYN2005.</summary>
    [Fact]
    public void PrivateParserSymbolIsNotVisible()
    {
        var c = CheckScratch("""
            import lexer
            import parser

            i32 main() {
                let bytes = List<u8>()
                let source = lexer.SourceText.create("<probe>", from: move bytes)
                var p = parser.Parser.create(lexer.lex(move source, by: lexer.Keywords.build()))
                let s = parser.parseBlock(ref p)
                return 0
            }
            """);
        var message = DiagnosticRenderer.RenderAll(c.Diagnostics.Items, includePath: true);
        Assert.Contains(c.Diagnostics.Items, d => d.Code == "BYN2005");
        Assert.Contains("parseBlock", message);
    }

    // ── T26: messages and JSON strings ────────────────────────────────────────────────────────────

    /// <summary>json.byn's message lines against <see cref="Messages"/> with the Unicode symbols.</summary>
    [Fact]
    public void MessagesMatchCli()
    {
        var u = Symbols.Unicode;
        var counts = new[] { (0, 0), (1, 0), (0, 1), (1, 1), (2, 3) };
        var main = $$"""
            i32 main() {
                print("== capLine")
                print(capLine("{{u.Ellipsis}}", by: 1))
                print(capLine("{{u.Ellipsis}}", by: 17))
                print("== buildFailed")
            {{string.Join("\n", counts.Select(n => $"    print(buildFailed(\"{u.Cross}\", by: {n.Item1}, as: {n.Item2}))"))}}
                print("== buildExplain")
                print(buildExplain("BYN2001"))
                print("== checkSummary")
                print(checkSummary("{{u.Dot}}", for: "shop", by: "0.25s"))
                return 0
            }
            """;
        var actual = SelfhostSemanticsHarness.RunSections(main);
        SelfhostSemanticsHarness.AssertSection(actual, "capLine",
            [Messages.DiagCapped.Text(u.Ellipsis, 1), Messages.DiagCapped.Text(u.Ellipsis, 17)]);
        SelfhostSemanticsHarness.AssertSection(actual, "buildFailed", counts.Select(n => Messages.BuildFailed.Text(u.Cross,
            (n.Item1 == 1 ? Messages.ErrorsOne : Messages.ErrorsMany).Text(n.Item1),
            (n.Item2 == 1 ? Messages.WarningsOne : Messages.WarningsMany).Text(n.Item2))).ToList());
        SelfhostSemanticsHarness.AssertSection(actual, "buildExplain", [Messages.BuildExplain.Text("BYN2001")]);
        SelfhostSemanticsHarness.AssertSection(actual, "checkSummary", [Messages.CheckSummary.Text(u.Dot, "shop", Messages.Seconds.Text("0.25"))]);
    }

    /// <summary>The strings <see cref="JsonStringsMatchUtf8JsonWriter"/> escapes: quotes, short escapes, C0, DEL, C1, U+2028, non-BMP.</summary>
    private static readonly string[] JsonInputs =
        ["plain", "q\"b\\s", "\b\t\n\f\r", "\u0001\u001f", "\u007f\u0085\u009f", "\u00e9\ufffd", "\u2028\u2029", "\U0001F600x", "List<i64>?"];

    /// <summary>typed.byn's <c>t26JsonString</c> against <see cref="Utf8JsonWriter"/> with <c>UnsafeRelaxedJsonEscaping</c>.</summary>
    [Fact]
    public void JsonStringsMatchUtf8JsonWriter()
    {
        var main = """
            i32 main(ReadOnlySpan<string> args) {
                print("== json")
                for a in args { print(t26JsonString(a)) }
                return 0
            }
            """;
        var actual = SelfhostSemanticsHarness.RunSections(main, JsonInputs);
        SelfhostSemanticsHarness.AssertSection(actual, "json", JsonInputs.Select(s =>
        {
            var buffer = new System.Buffers.ArrayBufferWriter<byte>();
            using (var w = new Utf8JsonWriter(buffer, new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
                w.WriteStringValue(s);
            return System.Text.Encoding.UTF8.GetString(buffer.WrittenSpan);
        }).ToList());
    }

    // ── T26: check and types against CliApp ───────────────────────────────────────────────────────

    /// <summary>A fresh scratch directory under <c>tmp/</c> holding <paramref name="files"/> (relative path, text).</summary>
    private static string Fixture(string name, params (string Path, string Text)[] files)
    {
        var dir = Path.Combine(Golden.RepoRoot, "tmp", "t26-tests", name);
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        Directory.CreateDirectory(dir);
        foreach (var (path, text) in files)
        {
            var full = Path.Combine(dir, path);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, text);
        }
        return dir;
    }

    /// <summary>The <c>in 0.25s</c> of a check summary, which no two runs share.</summary>
    private static string Untimed(string text) => Regex.Replace(text, @" in [0-9]+\.[0-9]{2}s$", " in <time>", RegexOptions.Multiline);

    /// <summary>
    /// <paramref name="actual"/> against <paramref name="expected"/>, line by line, failing with
    /// <paramref name="input"/>, the stream and the first differing line.
    /// </summary>
    internal static void AssertSameLines(string input, string stream, string expected, string actual)
    {
        var e = expected.Replace("\r\n", "\n").Split('\n');
        var a = actual.Replace("\r\n", "\n").Split('\n');
        for (var i = 0; i < Math.Max(e.Length, a.Length); i++)
        {
            var el = i < e.Length ? e[i] : "<end>";
            var al = i < a.Length ? a[i] : "<end>";
            if (el != al) Assert.Fail($"{input}: {stream} line {i + 1}\n expected: {el}\n   actual: {al}");
        }
    }

    /// <summary><c>bayan-check args</c> against <c>CliApp.Run(args)</c>: exit, stderr and stdout.</summary>
    private static string SameAsCli(string input, params string[] args) => SameAsCli(input, args, args);

    /// <summary><c>bayan-check self</c> against <c>CliApp.Run(cli)</c>, for a Bayan-only flag such as <c>--spans</c>.</summary>
    private static string SameAsCli(string input, string[] cli, string[] self)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var exit = CliApp.Run(cli, stdout, stderr);
        var (actualExit, actualOut, actualErr) = SelfhostParserTests.Run(Built, self);
        AssertSameLines(input, "stderr", Untimed(stderr.ToString()), Untimed(actualErr));
        AssertSameLines(input, "stdout", Untimed(stdout.ToString()), Untimed(actualOut));
        Assert.True(exit == actualExit, $"{input}: exit {actualExit}, expected {exit}");
        return actualOut;
    }

    private const string Hello = "void main() {\n    print(\"hi\")\n}\n";

    [Fact]
    public void CheckCleanFile()
    {
        var dir = Fixture("clean", ("hello.byn", Hello));
        SameAsCli("clean", "check", Path.Combine(dir, "hello.byn"));
        // `--spans` is bayan-check's alone; every span in range prints nothing more than `bayan check`.
        SameAsCli("clean --spans", ["check", Path.Combine(dir, "hello.byn")], ["check", "--spans", Path.Combine(dir, "hello.byn")]);
    }

    /// <summary>A <c>main.byn</c> is named after its directory, and a directory after itself.</summary>
    [Fact]
    public void CheckNamesMainAndDirectory()
    {
        var dir = Fixture("named", ("shop/main.byn", Hello));
        SameAsCli("main.byn", "check", Path.Combine(dir, "shop", "main.byn"));
        SameAsCli("directory", "check", Path.Combine(dir, "shop") + "/");
    }

    [Fact]
    public void CheckErrorFooter()
    {
        var dir = Fixture("broken", ("broken.byn", "void main( {\n"));
        SameAsCli("broken", "check", Path.Combine(dir, "broken.byn"));
        SameAsCli("broken types", "types", Path.Combine(dir, "broken.byn"));
    }

    /// <summary>Over 20 errors: the cap line, and every error with <c>--all</c>.</summary>
    [Fact]
    public void CheckCapsErrors()
    {
        var source = string.Concat(Enumerable.Range(0, 25).Select(i => $"void f{i}() {{ let x = }}\n")) + Hello;
        var dir = Fixture("many", ("many.byn", source));
        SameAsCli("many", "check", Path.Combine(dir, "many.byn"));
        SameAsCli("many --all", "check", "--all", Path.Combine(dir, "many.byn"));
    }

    [Fact]
    public void CheckProjects()
    {
        var bad = Fixture("bad-manifest", ("package.byn", "Package {\n    name \"local/shop\",\n}\n"), ("src/app/main.byn", Hello));
        SameAsCli("bad manifest", "check", bad);
        var noApp = Fixture("no-app", ("package.byn", "Package {\n    name: \"local/shop\",\n    version: \"0.1.0\",\n    edition: 2027,\n    kind: .app,\n}\n"));
        SameAsCli("no src/app", "check", noApp);
        var lib = Fixture("lib", ("package.byn", "Package {\n    name: \"local/tools\",\n    version: \"0.1.0\",\n    edition: 2027,\n    kind: .lib,\n}\n"),
            ("src/lib/tools.byn", "pub i64 twice(i64 x) => x * 2\n"));
        SameAsCli("lib", "check", lib);
    }

    [Fact]
    public void CheckBadArguments()
    {
        var dir = Fixture("args", ("hello.byn", Hello));
        SameAsCli("unexpected", "check", "--bogus", Path.Combine(dir, "hello.byn"));
        SameAsCli("not found", "check", Path.Combine(dir, "missing.byn"));
    }

    /// <summary><c>types</c> prints valid JSON, the same bytes as <c>bayan types</c>.</summary>
    [Fact]
    public void TypesIsCliJson()
    {
        var source = "i64 apply((i64 -> i64) f, to i64 x) => f(x)\n\nvoid main() {\n    let k = 3\n    print($\"{apply((x) => x + k, to: 2)}\")\n}\n";
        var dir = Fixture("types", ("lambda.byn", source));
        var output = SameAsCli("types", "types", Path.Combine(dir, "lambda.byn"));
        using var json = JsonDocument.Parse(output);
        Assert.Equal(TypedDumpVersion, json.RootElement.GetProperty("version").GetInt32());
        Assert.True(json.RootElement.GetProperty("functions").GetArrayLength() > 1);
    }

    private const int TypedDumpVersion = Bayan.Semantics.Binding.TypedDump.Version;

    /// <summary>A typed case in a directory of its own, so the CLI checks it alone.</summary>
    private static string Isolated(string name)
    {
        var dir = Fixture("typed-" + Path.GetFileNameWithoutExtension(name), (Path.GetFileName(name), File.ReadAllText(Path.Combine(Golden.CasesRoot, name))));
        return Path.Combine(dir, Path.GetFileName(name));
    }

    public static TheoryData<string> TypedCases => Golden.Cases("typed");

    [Theory(Skip = "T28 enables typed parity: state-types needs the binder's typestate (BYN2202), Q649")]
    [MemberData(nameof(TypedCases))]
    public void TypesParity(string name) => SameAsCli(name, "types", Isolated(name));

    /// <summary>Typed cases that already agree, kept running until <see cref="TypesParity"/> is enabled.</summary>
    [Theory]
    [InlineData("typed/rune.byn")]
    [InlineData("typed/lambda-narrowed-capture.byn")]
    [InlineData("typed/function-types.byn")]
    public void TypesAgree(string name) => SameAsCli(name, "types", Isolated(name));

    // ── T26: golden mode against DiagTests ────────────────────────────────────────────────────────

    /// <summary>
    /// <c>--golden</c> on a diag case against <see cref="DiagTests"/>' rendering: the case's directory as
    /// the package, its manifest's capabilities and name, tests for <c>*.test.byn</c>, paths in packages.
    /// </summary>
    private static void GoldenSame(string name)
    {
        var path = Path.Combine(Golden.CasesRoot, name);
        var dir = Path.GetDirectoryName(path)!;
        var manifest = File.Exists(Path.Combine(dir, "package.byn")) ? Manifest.Read(Path.Combine(dir, "package.byn")) : null;
        var tests = path.EndsWith(".test.byn", StringComparison.Ordinal);
        var c = Compilation.Create([Golden.Load(path)], dir, new CompilationOptions(IncludeTests: tests,
            Capabilities: manifest?.Capabilities, PackageName: manifest?.Name));
        var package = Path.GetFileName(dir) != "diag";
        string[] args = ["--golden", "--package", dir, .. tests ? new[] { "--tests" } : [], .. package ? new[] { "--include-path" } : [], path];
        var (exit, output, error) = SelfhostParserTests.Run(Built, args);
        Assert.True(exit == 0 && error.Length == 0, $"{name}: exit {exit}\n{error}");
        AssertSameLines(name, "golden", DiagnosticRenderer.RenderAll(c.Diagnostics.Items, includePath: package), output);
    }

    public static TheoryData<string> DiagCases => Golden.Cases("diag");

    [Theory(Skip = "T28 enables diag parity: the binder, regions (T25/T37) and stand-ins are not all real yet, Q649")]
    [MemberData(nameof(DiagCases))]
    public void GoldenParity(string name) => GoldenSame(name);

    /// <summary>Diag cases that already agree: plain cases, a <c>*.test.byn</c>, packages, one with a manifest.</summary>
    [Theory]
    [InlineData("diag/goto.byn")]
    [InlineData("diag/missing-argument.byn")]
    [InlineData("diag/cases-type-mismatch.test.byn")]
    [InlineData("diag/import-cycle/main.byn")]
    [InlineData("diag/capability-exceeded/main.byn")]
    public void GoldenAgrees(string name) => GoldenSame(name);

    /// <summary><see cref="AssertSameLines"/> names the input, the stream and the first differing line.</summary>
    [Fact]
    public void ComparisonNamesInputAndLine()
    {
        var e = Assert.ThrowsAny<Exception>(() => AssertSameLines("case.byn", "stdout", "a\nb\nc", "a\nB\nc"));
        Assert.Contains("case.byn: stdout line 2", e.Message);
        Assert.Contains("expected: b", e.Message);
        Assert.Contains("actual: B", e.Message);
    }
}
