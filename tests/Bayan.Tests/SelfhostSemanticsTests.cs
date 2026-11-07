using System.Globalization;
using System.Reflection;
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
/// (<c>bayan-check &lt;path&gt;</c>, <c>--types</c>, <c>--golden</c>) against the C# <see cref="CliApp"/>, <see cref="Compilation"/>
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
        var build = SelfhostSemanticsHarness.BuildFast(c, new HashSet<string>());
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

    /// <summary>014 T9 (Q990): the C#'s check summary on Windows ends in CRLF; both line endings lose their time.</summary>
    [Fact]
    public void UntimedCoversLfAndCrlf()
    {
        Assert.Equal("\u25c6 checked hello in <time>\r\nnext\r\n", Untimed("\u25c6 checked hello in 0.25s\r\nnext\r\n"));
        Assert.Equal("\u25c6 checked hello in <time>\nnext\n", Untimed("\u25c6 checked hello in 12.00s\nnext\n"));
        Assert.Equal("checked in 0.25s later", Untimed("checked in 0.25s later"));
    }

    [Fact]
    public void Builds()
    {
        Assert.True(File.Exists(Built));
    }

    /// <summary>No arguments is <c>bayan check</c> with no path (R1); a mode flag without its operand is usage.</summary>
    [Fact]
    public void NoPathAndBadMode()
    {
        SameAsCli("no path", "check");
        var (exit, output, error) = SelfhostParserTests.Run(Built, "--many");
        Assert.Equal(CliApp.ExitUsage, exit);
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

    /// <summary>The <c>in 0.25s</c> of a check summary, which no two runs share; <c>\r?</c> because the C#'s Windows output ends lines with CRLF and <c>$</c> does not match before <c>\r</c>.</summary>
    private static string Untimed(string text) => Regex.Replace(text, @" in [0-9]+\.[0-9]{2}s(?=\r?$)", " in <time>", RegexOptions.Multiline);

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

    /// <summary><c>bayan-check</c> with <see cref="SelfArgs"/> of <paramref name="cli"/> against <c>CliApp.Run(cli)</c>: exit, stderr and stdout.</summary>
    internal static string SameAsCli(string input, params string[] cli) => SameAsCli(input, cli, SelfArgs(cli));

    /// <summary>
    /// <c>bayan</c>'s arguments as <c>bayan-check</c> takes them: no <c>check</c> word (R1), and <c>types</c> as
    /// <c>--types</c> (R2). The command is the first argument that is neither a global option nor its value.
    /// </summary>
    private static string[] SelfArgs(string[] cli)
    {
        for (var i = 0; i < cli.Length; i++)
        {
            if (cli[i] == "--color")
            {
                i++;
                continue;
            }
            if (cli[i].StartsWith('-')) continue;
            return cli[i] switch
            {
                "check" => [.. cli[..i], .. cli[(i + 1)..]],
                "types" => [.. cli[..i], "--types", .. cli[(i + 1)..]],
                _ => cli,
            };
        }
        return cli;
    }

    /// <summary><c>bayan-check self</c> against <c>CliApp.Run(cli)</c>, for a Bayan-only flag such as <c>--spans</c>.</summary>
    internal static string SameAsCli(string input, string[] cli, string[] self)
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
        SameAsCli("clean --spans", ["check", Path.Combine(dir, "hello.byn")], ["--spans", Path.Combine(dir, "hello.byn")]);
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
        SameAsCli("output", "check", "-o", "x", Path.Combine(dir, "hello.byn"));
    }

    /// <summary>The ported <c>ParseBuildOptions</c> cases: <c>--mode</c>, an unknown mode, and <c>--</c>.</summary>
    [Fact]
    public void CheckBuildOptions()
    {
        var dir = Fixture("options", ("hello.byn", Hello));
        var file = Path.Combine(dir, "hello.byn");
        SameAsCli("mode", "check", "--mode", "fast", file);
        SameAsCli("unknown mode", "check", "--mode", "turbo", file);
        SameAsCli("program args", "check", file, "--", "--bogus");
        SameAsCli("types mode", "types", "--mode", "debug", file);
    }

    /// <summary>
    /// <c>--diagnostics json|text</c> (CliApp.cs:172): JSON lines with a fix, a label, a non-ASCII column, no cap
    /// and a manifest's diagnostics, no footer and no summary; text as the default; a bad or missing format.
    /// </summary>
    [Fact]
    public void CheckDiagnosticsFormats()
    {
        var warn = Path.Combine(Fixture("json-warn", ("warn.byn", "void main() {\n    var x = 1\n    print($\"{x}\")\n}\n")), "warn.byn");
        var wrong = Path.Combine(Fixture("json-wrong", ("wrong.byn", "void main() {\n    let n: i32 = \"\u00e9\u00e9\"\n    print($\"{n}\")\n}\n")), "wrong.byn");
        var many = Path.Combine(Fixture("json-many", ("many.byn", string.Concat(Enumerable.Range(0, 25).Select(i => $"void f{i}() {{ let x = }}\n")) + Hello)), "many.byn");
        var manifest = Fixture("json-manifest", ("package.byn", "Package {\n    name \"local/shop\",\n}\n"), ("src/app/main.byn", Hello));
        foreach (var format in new[] { "json", "text" })
        {
            SameAsCli($"{format} warn", "check", "--diagnostics", format, warn);
            SameAsCli($"{format} wrong", "check", "--diagnostics", format, wrong);
            SameAsCli($"{format} many", "check", "--diagnostics", format, many);
            SameAsCli($"{format} manifest", "check", "--diagnostics", format, manifest);
            SameAsCli($"{format} types", "types", "--diagnostics", format, wrong);
        }
        SameAsCli("unknown format", "check", "--diagnostics", "xml", warn);
        SameAsCli("no format", "check", warn, "--diagnostics");
    }

    /// <summary>CliApp.cs:304 <c>TakeGlobalOptions</c>: <c>--color</c> in its forms, a bad value, and <c>--ascii</c> kept after <c>--</c>.</summary>
    [Fact]
    public void CheckGlobalOptions()
    {
        var file = Path.Combine(Fixture("global", ("hello.byn", Hello)), "hello.byn");
        var broken = Path.Combine(Fixture("global-broken", ("broken.byn", "void main( {\n")), "broken.byn");
        SameAsCli("color never", "--color", "never", "check", file);
        SameAsCli("color=auto", "--color=auto", "check", broken);
        SameAsCli("color after the path", "check", broken, "--color", "never");
        SameAsCli("unknown color", "--color", "pink", "check", file);
        SameAsCli("color without a value", "check", file, "--color");
        SameAsCli("ascii summary", "--ascii", "check", file);
        SameAsCli("ascii after --", "check", broken, "--", "--ascii");
    }

    /// <summary>CliApp.cs:274 and :351: a <c>#!</c> file is compiled alone, any other file with its whole directory.</summary>
    [Fact]
    public void CheckScriptAlone()
    {
        var dir = Fixture("script", ("tool.byn", "#!/usr/bin/env bayan\nprint(\"hi\")\n"), ("other.byn", "void f() {\n    let x: i32 = \"s\"\n}\n"));
        SameAsCli("script", "check", Path.Combine(dir, "tool.byn"));
        SameAsCli("not a script", "check", Path.Combine(dir, "other.byn"));
        SameAsCli("types script", "types", Path.Combine(dir, "tool.byn"));
    }

    /// <summary><c>--codes</c> against <c>DiagnosticCodes</c>: the semantic ranges, the three BYN1 codes the checker reports, and the three second message forms after their codes (Q527).</summary>
    [Fact]
    public void CodesMatchDiagnosticCodes()
    {
        static bool Checker(string code) =>
            int.Parse(code.AsSpan(3), CultureInfo.InvariantCulture) is >= 2000 and <= 8999 || code is "BYN1016" or "BYN1054" or "BYN1056";
        var table = DiagnosticCodes.All.Where(d => Checker(d.Code)).ToList();
        foreach (var name in new[] { "ConversionMember", "BecomeOtherState", "BecomesCallUndeclared" })
        {
            var extra = (DiagnosticDescriptor)typeof(DiagnosticCodes).GetField(name, BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;
            table.Insert(table.FindLastIndex(d => d.Code == extra.Code) + 1, extra);
        }
        var (exit, output, error) = SelfhostParserTests.Run(Built, "--codes");
        Assert.True(exit == 0 && error.Length == 0, $"--codes: exit {exit}\n{error}");
        AssertSameLines("--codes", "stdout", string.Concat(table.Select(d => $"{d.Code}\t{d.MessageFormat}\n")), output);
    }

    /// <summary><c>--many</c>: each <c>*.byn</c> in name order, under <c>== name</c>, as <see cref="DiagTests"/> renders it alone.</summary>
    [Fact]
    public void ManyMatchesCompilation()
    {
        var dir = Fixture("many-dir", ("b.byn", Hello), ("a.byn", "void main( {\n"), ("c.byn", "i32 main() {\n    return \"x\"\n}\n"), ("notes.txt", "x"));
        var expected = string.Concat(new[] { "a.byn", "b.byn", "c.byn" }.Select(name =>
        {
            var c = Compilation.Create([Golden.Load(Path.Combine(dir, name))], dir, new CompilationOptions(IncludeTests: false));
            return $"== {name}\n" + DiagnosticRenderer.RenderAll(c.Diagnostics.Items, includePath: false);
        }));
        var (exit, output, error) = SelfhostParserTests.Run(Built, "--many", dir);
        Assert.True(exit == 0 && error.Length == 0, $"--many: exit {exit}\n{error}");
        AssertSameLines("--many", "stdout", expected, output);
    }

    /// <summary><c>--time</c> prints one nanosecond total, and the parse and check split on stderr.</summary>
    [Fact]
    [Trait("Snapshot", "skip")]
    public void TimePrintsNanoseconds()
    {
        // no-oracle: Q648 (C# `check` has no `--time`; the numbers differ per run)
        var dir = Fixture("time", ("hello.byn", Hello));
        var (exit, output, error) = SelfhostParserTests.Run(Built, "--time", Path.Combine(dir, "hello.byn"));
        Assert.Equal(0, exit);
        Assert.Matches(@"^[0-9]+\n$", output);
        Assert.Matches(@"^parse [0-9]+ check [0-9]+\n$", error);
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

    /// <summary>TypedDump.cs:92 <c>KindOf</c>: a <c>var</c>, a local <c>const</c>, a <c>for</c> variable, a pattern and a <c>catch</c> binding (<c>defer failing (e)</c>; <c>catch (e)</c> is BYN1025).</summary>
    [Fact]
    public void TypesBindingKinds()
    {
        var source = """
            union error ParseError {
                empty
            }

            i64 digit(string s) fails ParseError {
                defer failing (e) print($"failed: {e}")
                fail .empty if s.isEmpty
                return 1
            }

            i64 total(List<i64> xs) {
                const k = 2
                var sum: i64 = 0
                for x in xs { sum += x * k }
                let first: i64? = xs.length > 0 ? xs[0] : null
                if (first is { } f) { sum += f }
                let d = digit("") catch 0
                return sum + d
            }

            void main() {
                var xs = List<i64>()
                xs.append(1)
                print($"{total(xs)}")
            }

            """;
        var dir = Fixture("types-kinds", ("kinds.byn", source));
        var output = SameAsCli("kinds", "types", Path.Combine(dir, "kinds.byn"));
        foreach (var kind in new[] { "var", "const", "for", "pattern", "catch" }) Assert.Contains($"\"kind\": \"{kind}\"", output);
    }

    /// <summary>TypedDump.cs:29-31 across two files of one module: file order first, then a lambda before the function after it.</summary>
    [Fact]
    public void TypesModuleOrder()
    {
        var padding = string.Concat(Enumerable.Repeat("// padding so that main starts after every function of b.byn\n", 4));
        var dir = Fixture("types-order", ("a.byn", padding + "void main() {\n    print($\"{twice(2)} {ten()}\")\n}\n"),
            ("b.byn", "i64 twice(i64 x) {\n    let f: (i64 -> i64) = (y) => y * 2\n    return f(x)\n}\n\ni64 ten() => 10\n"));
        SameAsCli("module order", "types", dir);
    }

    /// <summary>TypedDump.cs:89 <c>PathOf</c>: a project's files relative to its root.</summary>
    [Fact]
    public void TypesProjectPaths()
    {
        var dir = Fixture("types-project", ("package.byn", "Package {\n    name: \"local/shop\",\n    version: \"0.1.0\",\n    edition: 2027,\n    kind: .app,\n}\n"),
            ("src/app/main.byn", "void main() {\n    print($\"{one()}\")\n}\n"), ("src/app/util.byn", "i64 one() => 1\n"));
        SameAsCli("project", "types", dir);
    }

    private const int TypedDumpVersion = Bayan.Semantics.Binding.TypedDump.Version;

    /// <summary>A typed case in a directory of its own, so the CLI checks it alone.</summary>
    internal static string Isolated(string name)
    {
        var dir = Fixture("typed-" + Path.GetFileNameWithoutExtension(name), (Path.GetFileName(name), File.ReadAllText(Path.Combine(Golden.CasesRoot, name))));
        return Path.Combine(dir, Path.GetFileName(name));
    }

    /// <summary>Typed cases that already agree, kept running until <see cref="SelfhostSemanticsParityTests.TypesParity"/> is enabled.</summary>
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
    internal static void GoldenSame(string name)
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

    /// <summary>Diag cases that already agree: plain cases, a <c>*.test.byn</c>, packages, one with a manifest.</summary>
    [Theory]
    [InlineData("diag/goto.byn")]
    [InlineData("diag/missing-argument.byn")]
    [InlineData("diag/cases-type-mismatch.test.byn")]
    [InlineData("diag/import-cycle/main.byn")]
    [InlineData("diag/capability-exceeded/main.byn")]
    public void GoldenAgrees(string name) => GoldenSame(name);

    /// <summary>
    /// Plan :130's CLI comparison: the diag case copied to <c>tmp/t26-tests/cli/&lt;slug&gt;/</c> (a directory case
    /// whole), then <c>CliApp.Run(["check", copy])</c> against <c>bayan-check copy</c>, stderr and exit.
    /// </summary>
    internal static void CheckSame(string name)
    {
        var source = Path.Combine(Golden.CasesRoot, name);
        var caseDir = Path.GetDirectoryName(source)!;
        var dir = Path.Combine(Golden.RepoRoot, "tmp", "t26-tests", "cli", name.Replace('/', '_'));
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        Directory.CreateDirectory(dir);
        if (Path.GetFileName(caseDir) == "diag") File.Copy(source, Path.Combine(dir, Path.GetFileName(source)));
        else
        {
            foreach (var file in Directory.EnumerateFiles(caseDir, "*", SearchOption.AllDirectories))
            {
                var target = Path.Combine(dir, Path.GetRelativePath(caseDir, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target);
            }
        }
        var copy = Path.Combine(dir, Path.GetFileName(source));
        var stderr = new StringWriter();
        var exit = CliApp.Run(["check", copy], new StringWriter(), stderr);
        var (actualExit, _, actualErr) = SelfhostParserTests.Run(Built, copy);
        AssertSameLines(name, "stderr", stderr.ToString(), actualErr);
        Assert.True(exit == actualExit, $"{name}: exit {actualExit}, expected {exit}");
    }

    /// <summary><see cref="AssertSameLines"/> names the input, the stream and the first differing line.</summary>
    [Fact]
    public void ComparisonNamesInputAndLine()
    {
        var e = Assert.ThrowsAny<Exception>(() => AssertSameLines("case.byn", "stdout", "a\nb\nc", "a\nB\nc"));
        Assert.Contains("case.byn: stdout line 2", e.Message);
        Assert.Contains("expected: b", e.Message);
        Assert.Contains("actual: B", e.Message);
    }

    // ── T28: `@unobservable` marks against Compilation.Unobservable ─────────────────────────────────

    private const string UnobservableSource = """
        i32 f(i32 x) {
            @unobservable because "all"
            return x
        }

        i64 count<T>(in T x) {
            @unobservable(boundary) because "first"
            @unobservable(boundary) because "second"
            @unobservable(relational) because "third"
            return 1
        }

        void main() {
            @unobservable(bogus) because "unknown"
            let a = f(1)
            let b = count(a)
            let s = count("s")
            print($"{b} {s}")
        }

        """;

    /// <summary>
    /// <c>c.unobservable</c> after a whole check, against the C# <c>Compilation.Unobservable</c> (Compilation.cs:35-38): binding
    /// order, a null operator (printed <c>*</c>), one mark per (file, span, operator) across a repeated attribute and the
    /// generic instances, and an unknown operator reported (BYN3219) and not recorded.
    /// </summary>
    [Fact]
    public void UnobservableMarksMatchCompilation()
    {
        var dir = Fixture("unobservable", ("marks.byn", UnobservableSource));
        var path = Path.Combine(dir, "marks.byn");
        var c = Compilation.FromPath(path, new CompilationOptions(), wholeDirectory: false);
        var marks = c.Unobservable.Select(m => $"{m.File} {m.Span.Start} {m.Span.Length} {m.Operator ?? "*"} {m.Reason}").ToList();
        var diags = c.Diagnostics.Items.Select(d =>
        {
            var (line, col) = d.Source!.GetLinePosition(d.Span.Start);
            return $"{d.Code} {line}:{col} {d.Message}";
        }).ToList();
        var main = $$"""
            i32 main() {
                let options = CompilationOptions {}
                let c = fromPath("{{path.Replace("\\", "\\\\")}}", by: in options, wholeDirectory: false)
                print("== marks")
                for m in c.unobservable { print($"{m.file} {m.span.start} {m.span.length} {m.op ?? "*"} {m.reason}") }
                print("== diags")
                for d in c.diagnostics {
                    let pos = linePosition(c.files[d.file].text.span, at: d.span.start)
                    print($"{d.code} {pos.line}:{pos.column} {d.message}")
                }
                return 0
            }
            """;
        var actual = SelfhostSemanticsHarness.RunSections(main);
        // The fixture reaches both cases: a null operator, and a (file, span, operator) given twice and bound twice.
        Assert.Contains(c.Unobservable, m => m.Operator is null);
        Assert.Single(c.Unobservable, m => m.Operator == "boundary");
        SelfhostSemanticsHarness.AssertSection(actual, "marks", marks);
        SelfhostSemanticsHarness.AssertSection(actual, "diags", diags);
    }

    // ── T28: `for try` stepping (Binder.cs:1090-1115) against CliApp ────────────────────────────────

    private const string ForTrySource = """
        union error ReadError { broken }

        struct Rows {
            i64 at
            i64 limit

            i64? next(ref this) fails ReadError {
                if at >= limit { return null }
                at += 1
                return at
            }
        }

        i64 copied(i64 limit) fails ReadError {
            var sum = 0
            for try row in (Rows { at: 0, limit: limit }) { sum += row }
            return sum
        }

        i64 inPlace() fails ReadError {
            var rows = Rows { at: 0, limit: 3 }
            var sum = 0
            for try row: i64 in rows {
                sum += row
                break if row == 2
            }
            return sum + rows.at
        }

        i64 fromLet() fails ReadError {
            let rows = Rows { at: 0, limit: 2 }
            var sum = 0
            for try row in rows { sum += row }
            return sum + rows.at
        }

        i64 byRef(ref Rows rows) fails ReadError {
            var sum = 0
            for try row in (rows) { sum += row }
            return sum
        }

        var rows = Rows { at: 0, limit: 2 }
        print($"{try copied(3)} {try inPlace()} {try fromLet()} {try byRef(ref rows)}")

        """;

    /// <summary>
    /// A valid <c>for try</c> whose body reports: the hidden <c>&lt;sequence N&gt;</c> copy and the in-place <c>var</c>, the
    /// step's span at the iterable, and a declared type that is not the step's.
    /// </summary>
    [Fact]
    public void ForTryBodyReports()
    {
        var source = """
            union error ReadError { broken }

            struct Rows {
                i64 at

                i64? next(ref this) fails ReadError {
                    at += 1
                    return at > 2 ? null : at
                }
            }

            void copied() fails ReadError {
                for try row in (Rows { at: 0 }) {
                    let s: string = row
                }
            }

            void inPlace() fails ReadError {
                var rows = Rows { at: 0 }
                for try row: string in rows {
                    let n: i64 = "x"
                }
            }

            try copied()
            try inPlace()

            """;
        var dir = Fixture("for-try-reports", ("reports.byn", source));
        SameAsCli("for try reports", "check", Path.Combine(dir, "reports.byn"));
    }

    /// <summary>The typed dump of the hidden copy (of a temporary and of a <c>let</c>), the in-place <c>var</c> and a <c>ref</c> parameter in parentheses.</summary>
    [Fact]
    public void ForTryTypes()
    {
        var dir = Fixture("for-try-types", ("steps.byn", ForTrySource));
        SameAsCli("for try types", "types", Path.Combine(dir, "steps.byn"));
        SameAsCli("for try check", "check", Path.Combine(dir, "steps.byn"));
    }
}

/// <summary>
/// The whole-corpus parity theories, skipped until T28. The class name keeps them out of the T26 guard's
/// <c>SelfhostSemanticsTests</c> filter (Q649).
/// </summary>
public sealed class SelfhostSemanticsParityTests
{
    public static TheoryData<string> TypedCases => Golden.Cases("typed");

    [Theory]
    [MemberData(nameof(TypedCases))]
    public void TypesParity(string name) => SelfhostSemanticsTests.SameAsCli(name, "types", SelfhostSemanticsTests.Isolated(name));

    public static TheoryData<string> DiagCases => Golden.Cases("diag");

    [Theory]
    [MemberData(nameof(DiagCases))]
    public void GoldenParity(string name) => SelfhostSemanticsTests.GoldenSame(name);

    [Theory]
    [MemberData(nameof(DiagCases))]
    public void CheckParity(string name) => SelfhostSemanticsTests.CheckSame(name);
}
