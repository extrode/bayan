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
/// 009 T5, <c>selfhost/parser/decls2.byn</c> on its own: each case calls one declaration parser directly, from
/// a <c>main</c> that stands in for <c>main.byn</c>, and compares the node's name, span and case count and every
/// diagnostic (code, span, message, label, help, fix) with the C# parser. The inputs avoid types, expressions,
/// blocks, attributes and docs, which the sibling files and the <c>T5</c> shims do not parse yet (Q450).
/// </summary>
public sealed class SelfhostParserT5Tests(ITestOutputHelper output)
{
    private static string SelfhostDir => Path.Combine(Golden.RepoRoot, "selfhost");

    private static string ParserDir => Path.Combine(SelfhostDir, "parser");

    /// <summary>The source, and the Bayan statements that parse it into <c>d</c> and print its summary.</summary>
    private static readonly (string Source, string Call)[] Cases =
    [
        ("enum Color {\n    red\n    green\n    blue\n}\n", "let d = parseEnum(ref p, at: 0, by: modifierNone)\nsummary(ref p, at: d.span, for: d.name, by: d.members.length)"),
        ("enum Color { red, green, blue }", "let d = parseEnum(ref p, at: 0, by: modifierNone)\nsummary(ref p, at: d.span, for: d.name, by: d.members.length)"),
        ("enum E {\n    a,\n    b\n}\n", "let d = parseEnum(ref p, at: 0, by: modifierNone)\nsummary(ref p, at: d.span, for: d.name, by: d.members.length)"),
        ("enum E { a, /* x */ b }", "let d = parseEnum(ref p, at: 0, by: modifierNone)\nsummary(ref p, at: d.span, for: d.name, by: d.members.length)"),
        ("enum E { a", "let d = parseEnum(ref p, at: 0, by: modifierNone)\nsummary(ref p, at: d.span, for: d.name, by: d.members.length)"),
        ("union Mode { read, write }", "let d = parseUnion(ref p, at: 0, by: modifierNone)\nsummary(ref p, at: d.span, for: d.name, by: d.variants.length)"),
        ("union error E { bad, worse }", ErrorEnum),
        ("union error E { bad, }", ErrorEnum),
        ("error union E { bad }", ErrorEnum),
        ("extension Foo\n", "let d = parseExtension(ref p, at: 0)\nsummary(ref p, at: d.span, for: d.name, by: d.members.length)"),
        ("extension Foo { }", "let d = parseExtension(ref p, at: 0)\nsummary(ref p, at: d.span, for: d.name, by: d.members.length)"),
        ("interface I { type Item }", "let d = parseInterface(ref p, at: 0, by: modifierNone)\nsummary(ref p, at: d.span, for: d.name, by: d.members.length)"),
        ("interface I {\n    5\n}", "let d = parseInterface(ref p, at: 0, by: modifierNone)\nsummary(ref p, at: d.span, for: d.name, by: d.members.length)"),
        ("effect io = net, fs", "let d = parseEffect(ref p, at: 0, by: modifierNone)\nsummary(ref p, at: d.span, for: d.name, by: d.effects.length)"),
    ];

    private const string ErrorEnum = """
        let d = parseErrorUnion(ref p, at: 0, by: modifierNone, through: List<AttributeSyntax>())
        switch d {
            case .errorEnumDecl(let n):
                summary(ref p, at: n.value.span, for: n.value.name, by: n.value.variants.length)
            default:
                print("not an error enum")
        }
        """;

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
            return Parser.create(lexer.lex(SourceText.create("t5.byn", from: move bytes), by: keywords))
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
    private static string Expected(string source)
    {
        var tree = SyntaxTree.Parse(SourceText.FromString("t5.byn", source));
        var (span, name, count) = tree.Root.Members[0] switch
        {
            EnumDecl d => (d.Span, d.Name, d.Members.Length),
            UnionDecl d => (d.Span, d.Name, d.Variants.Length),
            ErrorEnumDecl d => (d.Span, d.Name, d.Variants.Length),
            ExtensionDecl d => (d.Span, d.Name, d.Members.Length),
            InterfaceDecl d => (d.Span, d.Name, d.Members.Length),
            EffectDecl d => (d.Span, d.Name, d.Effects.Length),
            var d => throw new InvalidOperationException(d.GetType().Name),
        };
        var sb = new StringBuilder($"{name.Text} {span.Start}+{span.Length} {count}\n");
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
        var stdout = p.StandardOutput.ReadToEndLfAsync();
        var stderr = p.StandardError.ReadToEndLfAsync();
        p.WaitForExit();
        return (p.ExitCode, stdout.Result, stderr.Result);
    }

    [Fact]
    public void DeclarationsMatchTheCSharpParser()
    {
        var sources = Directory.GetFiles(ParserDir, "*.byn").Where(f => Path.GetFileName(f) != "main.byn").Order(StringComparer.Ordinal)
            .Select(f => SourceText.FromFile(f)).Append(SourceText.FromString(Path.Combine(ParserDir, "t5-test.byn"), Program())).ToList();
        Environment.SetEnvironmentVariable("BAYAN_CACHE", Path.Combine(Golden.RepoRoot, "tmp", "cache"));
        var c = Compilation.Create(sources, packageRoot: SelfhostDir);
        if (c.Diagnostics.HasErrors) Assert.Fail($"decls2.byn did not build:\n{DiagnosticRenderer.RenderAll(c.Diagnostics.Items, includePath: true)}");
        var build = Toolchain.Build(CliApp.EmitC(c, BuildMode.Fast), BuildMode.Fast);
        if (!build.Succeeded) Assert.Fail(build.Errors);

        var (exit, stdout, stderr) = Run(build.ExecutablePath!);
        output.WriteLine(stderr);
        Assert.Equal(0, exit);
        var expected = string.Concat(Cases.Select(k => Expected(k.Source)));
        var dir = Path.Combine(Golden.RepoRoot, "tmp", "selfhost-parser");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "t5-expected.txt"), expected);
        File.WriteAllText(Path.Combine(dir, "t5-actual.txt"), stdout);
        Assert.Equal(expected, stdout);
    }
}
