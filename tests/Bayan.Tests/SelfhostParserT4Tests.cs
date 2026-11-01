using System.Diagnostics;
using System.Text;
using Bayan.Backend.C;
using Bayan.Cli;
using Bayan.Semantics;
using Bayan.Syntax.Ast;
using Bayan.Syntax.Diagnostics;
using Bayan.Syntax.Parsing;
using Bayan.Syntax.Text;
using Xunit.Abstractions;

namespace Bayan.Tests;

/// <summary>
/// 009 T4, <c>selfhost/parser/decls.byn</c> on its own: each case calls one declaration parser directly, from
/// a <c>main</c> that stands in for <c>main.byn</c>, and compares a span, a name, a count and every diagnostic
/// (code, span, message, label, help, fix) with the C# parser. The inputs avoid what the sibling stubs and the
/// <c>T4</c> stand-ins do not parse yet (Q445): types other than plain names outside parameters, expressions,
/// blocks, attribute arguments, payloads and fields. A function's span would end before its unparsed body, so a
/// function case compares its name's span instead.
/// </summary>
public sealed class SelfhostParserT4Tests(ITestOutputHelper output)
{
    private static string SelfhostDir => Path.Combine(Golden.RepoRoot, "selfhost");

    private static string ParserDir => Path.Combine(SelfhostDir, "parser");

    private const string Unit = "let d = parseCompilationUnit(ref p)\nsummary(ref p, at: d.span, for: TextSpan { start: 0, length: 0 }, by: d.members.length)";

    private const string Function = "let d = parseFunction(ref p, at: 0, by: modifierNone)\nsummary(ref p, at: d.name, for: d.name, by: d.params.length)";

    private const string Struct = "let d = parseStruct(ref p, at: 0, by: modifierNone)\nsummary(ref p, at: d.span, for: d.name, by: d.states.length)";

    /// <summary>The source, and the Bayan statements that parse it into <c>d</c> and print its summary.</summary>
    private static readonly (string Source, string Call)[] Cases =
    [
        ("import a.b as c\nimport d\n", Unit),
        ("}\nimport a\n", Unit),
        ("public public import x\n", Unit),
        ("readonly static struct S {}\n", Unit),
        ("public\n", Unit),
        ("delegate void Handler(i64 a, string b);\n", Unit),
        ("delegate T F<T>(T x);\n", Unit),
        ("@allow import x\n", Unit),
        ("@allow because \"\" import x\n", Unit),
        ("[Inline]\nimport x\n", Unit),
        ("f(i64 a, by i64 b) {}", Function),
        ("f(i64 a, ref this) {}", Function),
        ("f(this) {}", Function),
        ("f(this, i64 a) {}", Function),
        ("f(ref this is .open or .shut, i64 a) {}", Function),
        ("f(i64 x is .small) {}", Function),
        ("f(i64... xs) {}", Function),
        ("f(rest i64 xs, i64 y) {}", Function),
        ("f() fails uses alloc, net where T : I {}", Function),
        ("f() fails becomes .open {}", Function),
        ("f() fails noalloc {}", Function),
        ("f<T, U>() fails {}", Function),
        ("struct unique S<T> : I, J where T : K {}", Struct),
        ("struct S {\n    state open\n    state shut\n}\n", Struct),
        ("struct S {\n    state open // why\n}\n", Struct),
        ("struct S {\n    states {\n        a\n        b\n    }\n}\n", Struct),
        ("struct S {\n    states { a }\n    states { b }\n}\n", Struct),
        ("struct S {\n    states { a }\n    state b\n}\n", Struct),
        ("struct S {", Struct),
    ];

    private const string Prelude = """
        void summary(ref Parser p, at TextSpan span, for TextSpan name, by i64 count) {
            print($"{p.lexed.source.getText(name)} {span.start}+{span.length} {count}")
            for d in p.lexed.diagnostics {
                let fix = d.hasFix ? $" fix {d.fixSpan.start}+{d.fixSpan.length} [{d.fixReplacement}]" : ""
                print($"{d.code} {d.span.start}+{d.span.length} {d.message} | {d.label} | {d.help}{fix}")
            }
        }

        Parser makeParser(string text, by Keywords keywords) {
            var bytes = List<u8>()
            for b in text.bytes { bytes.append(b) }
            return Parser.create(lexer.lex(SourceText.create("t4.byn", from: move bytes), by: keywords))
        }

        """;

    private static string Quote(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n") + "\"";

    private static string Program()
    {
        var sb = new StringBuilder("import lexer\n\n").Append(Prelude).Append("void main() {\n    let keywords = Keywords.build()\n    using let arena = Arena()\n    provide allocator: arena {\n");
        foreach (var (source, call) in Cases)
        {
            sb.Append("        {\n            var p = makeParser(").Append(Quote(source)).Append(", by: keywords)\n");
            foreach (var line in call.Split('\n')) sb.Append("            ").Append(line).Append('\n');
            sb.Append("        }\n");
        }
        return sb.Append("    }\n}\n").ToString();
    }

    /// <summary>The C# parser's summary of <paramref name="source"/>, in the form <c>summary</c> prints.</summary>
    private static string Expected(string source, string call)
    {
        var tree = SyntaxTree.Parse(SourceText.FromString("t4.byn", source));
        var (span, name, count) = call switch
        {
            Unit => (tree.Root.Span, "", tree.Root.Members.Length),
            Function when tree.Root.Members[0] is FunctionDecl f => (f.Name.Span, f.Name.Text, f.Params.Length),
            Struct when tree.Root.Members[0] is StructDecl s => (s.Span, s.Name.Text, s.States.Length),
            _ => throw new InvalidOperationException(source),
        };
        var sb = new StringBuilder($"{name} {span.Start}+{span.Length} {count}\n");
        foreach (var d in tree.Diagnostics)
        {
            var fix = d.Fix is { } f ? $" fix {f.Span.Start}+{f.Span.Length} [{f.Replacement}]" : "";
            sb.Append($"{d.Code} {d.Span.Start}+{d.Span.Length} {d.Message} | {d.Label} | {d.Help}{fix}\n");
        }
        return sb.ToString();
    }

    private static (int Exit, string Out, string Err) Run(string program)
    {
        var psi = new ProcessStartInfo(program) { RedirectStandardOutput = true, StandardOutputEncoding = System.Text.Encoding.UTF8, RedirectStandardError = true, StandardErrorEncoding = System.Text.Encoding.UTF8, UseShellExecute = false };
        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        p.WaitForExit();
        return (p.ExitCode, stdout.Result, stderr.Result);
    }

    [Fact]
    public void DeclarationsMatchTheCSharpParser()
    {
        var sources = Directory.GetFiles(ParserDir, "*.byn").Where(f => Path.GetFileName(f) != "main.byn").Order(StringComparer.Ordinal)
            .Select(f => SourceText.FromFile(f)).Append(SourceText.FromString(Path.Combine(ParserDir, "t4-test.byn"), Program())).ToList();
        Environment.SetEnvironmentVariable("BAYAN_CACHE", Path.Combine(Golden.RepoRoot, "tmp", "cache"));
        var c = Compilation.Create(sources, packageRoot: SelfhostDir);
        if (c.Diagnostics.HasErrors) Assert.Fail($"decls.byn did not build:\n{DiagnosticRenderer.RenderAll(c.Diagnostics.Items, includePath: true)}");
        var build = Toolchain.Build(CliApp.EmitC(c, BuildMode.Fast), BuildMode.Fast);
        if (!build.Succeeded) Assert.Fail(build.Errors);

        var (exit, stdout, stderr) = Run(build.ExecutablePath!);
        output.WriteLine(stderr);
        Assert.Equal(0, exit);
        var expected = string.Concat(Cases.Select(k => Expected(k.Source, k.Call)));
        var dir = Path.Combine(Golden.RepoRoot, "tmp", "selfhost-parser");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "t4-expected.txt"), expected);
        File.WriteAllText(Path.Combine(dir, "t4-actual.txt"), stdout);
        Assert.Equal(expected, stdout);
    }
}
