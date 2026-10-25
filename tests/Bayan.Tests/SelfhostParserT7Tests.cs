using System.Diagnostics;
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
/// <c>selfhost/parser/stmts.byn</c> (009 T7) against the C# parser, before the other tasks merge. The parser's files
/// stand in for the module without <c>main.byn</c> and <c>exprs.byn</c>; this file's program adds a <c>parseExpression</c>
/// for names, <c>not x</c> and <c>(x)</c>, and a <c>main</c> that parses the first block of a file and prints its
/// statement shapes with spans, then the diagnostics with their fixes. The inputs use only T7's statements.
/// </summary>
public sealed class SelfhostParserT7Tests(ITestOutputHelper output)
{
    private static string SelfhostDir => Path.Combine(Golden.RepoRoot, "selfhost");

    private static string ParserDir => Path.Combine(SelfhostDir, "parser");

    private const string Program = """
        import lexer

        Expr parseExpression(ref Parser p) {
            let start = p.current().start
            if p.kind() == .notKeyword {
                let _ = p.advance()
                let operand = parseExpression(ref p)
                return Expr.unaryExpr(Owned(UnaryExpr { span: p.spanFrom(start), op: .notKeyword, operand: move operand }))
            }
            if p.kind() == .openParen {
                let _ = p.advance()
                let inner = parseExpression(ref p)
                let _ = p.expect(.closeParen)
                return Expr.parenExpr(Owned(ParenExpr { span: p.spanFrom(start), inner: move inner }))
            }
            let name = p.expect(.identifier)
            return Expr.nameExpr(Owned(NameExpr { span: p.spanFrom(start), name: spanOf(name), typeArgs: List<TypeSyntax>() }))
        }

        string likelihoodText(Likelihood l) => l switch {
            .none => "",
            .unlikely => " unlikely",
            .neverKeyword => " never",
        }

        string shape(in Stmt s, from ref Parser p) {
            switch s {
                case .blockStmt(let n):
                    var text = $"block {n.value.span.start}..{n.value.span.end} <"
                    for st in n.value.statements { text = $"{text} {shape(in st, from: ref p)}" }
                    return $"{text} >"
                case .ifStmt(let n):
                    return $"if {n.value.span.start}..{n.value.span.end}{likelihoodText(n.value.likelihood)} {shape(in n.value.then, from: ref p)} else {shape(in n.value.elseKeyword, from: ref p)}"
                case .unlessStmt(let n):
                    return $"unless {n.value.span.start}..{n.value.span.end} {shape(in n.value.body, from: ref p)}"
                case .whileStmt(let n):
                    return $"while {n.value.span.start}..{n.value.span.end} {shape(in n.value.body, from: ref p)}"
                case .doWhileStmt(let n):
                    return $"do {n.value.span.start}..{n.value.span.end} {shape(in n.value.body, from: ref p)}"
                case .whileLetStmt(let n):
                    return $"whilelet {n.value.span.start}..{n.value.span.end} {p.lexed.source.getText(n.value.variable)} {shape(in n.value.body, from: ref p)}"
                case .forStmt(let n):
                    return $"for {n.value.span.start}..{n.value.span.end} {n.value.kind} {n.value.names.length > 0 ? "" : p.lexed.source.getText(n.value.variable)} names {n.value.names.length} {shape(in n.value.body, from: ref p)}"
                case .labeledStmt(let n):
                    return $"label {n.value.span.start}..{n.value.span.end} {p.lexed.source.getText(n.value.label)} {shape(in n.value.statement, from: ref p)}"
                case .errorStmt(let n):
                    return $"error {n.value.span.start}..{n.value.span.end}"
                case .none:
                    return "none"
                default:
                    return "?"
            }
        }

        i32 main(ReadOnlySpan<string> args) {
            let keywords = Keywords.build()
            var bytes = Io.readFile(args[0]) catch return 4
            var p = Parser.create(lexer.lex(SourceText.create(args[0], from: move bytes), by: keywords))
            while p.kind() != .openBrace && !p.atEnd() { let _ = p.advance() }
            let block = parseBlock(ref p)
            print(shape(in block, from: ref p))
            for d in p.lexed.diagnostics {
                if d.code.isEmpty { continue }
                let fix = d.hasFix ? $" fix {d.fixSpan.start}+{d.fixSpan.length} '{d.fixReplacement}'" : ""
                print($"{d.code} {d.span.start}+{d.span.length} {d.message}{fix}")
            }
            return 0
        }
        """;

    private static readonly Lazy<(string? Path, string Error)> Built = new(() =>
    {
        Environment.SetEnvironmentVariable("BAYAN_CACHE", Path.Combine(Golden.RepoRoot, "tmp", "cache"));
        var sources = Directory.GetFiles(ParserDir, "*.byn").Where(f => Path.GetFileName(f) is not ("main.byn" or "exprs.byn"))
            .Order(StringComparer.Ordinal).Select(f => SourceText.FromFile(f))
            .Append(SourceText.FromString(Path.Combine(ParserDir, "t7-test.byn"), Program)).ToList();
        var c = Compilation.Create(sources, packageRoot: SelfhostDir);
        if (c.Diagnostics.HasErrors) return (null, DiagnosticRenderer.RenderAll(c.Diagnostics.Items, includePath: true));
        var stmts = c.Diagnostics.Items.Where(d => d.Source?.Path.EndsWith("stmts.byn") == true).ToList();
        if (stmts.Count > 0) return (null, DiagnosticRenderer.RenderAll(stmts, includePath: true));
        var build = Toolchain.Build(CliApp.EmitC(c, BuildMode.Fast), BuildMode.Fast);
        return build.Succeeded ? (build.ExecutablePath, "") : (null, build.Errors);
    });

    /// <summary>The C# shape and diagnostics of the first function's body, printed as <see cref="Program"/> does.</summary>
    private static string Expected(SourceText source)
    {
        var tree = SyntaxTree.Parse(source);
        var body = ((FunctionDecl)tree.Root.Members[0]).Body!;
        var lines = new List<string> { Shape(body) };
        foreach (var d in tree.Diagnostics)
        {
            var fix = d.Fix is { } f ? $" fix {f.Span.Start}+{f.Span.Length} '{f.Replacement}'" : "";
            lines.Add($"{d.Code} {d.Span.Start}+{d.Span.Length} {d.Message}{fix}");
        }
        return string.Join("\n", lines) + "\n";
    }

    private static string Range(Node n) => $"{n.Span.Start}..{n.Span.End}";

    private static string Shape(Stmt? s) => s switch
    {
        null => "none",
        BlockStmt b => $"block {Range(b)} <{string.Concat(b.Statements.Select(st => " " + Shape(st)))} >",
        IfStmt i => $"if {Range(i)}{i.Likelihood switch { Likelihood.Unlikely => " unlikely", Likelihood.Never => " never", _ => "" }} {Shape(i.Then)} else {Shape(i.Else)}",
        UnlessStmt u => $"unless {Range(u)} {Shape(u.Body)}",
        WhileStmt w => $"while {Range(w)} {Shape(w.Body)}",
        DoWhileStmt d => $"do {Range(d)} {Shape(d.Body)}",
        WhileLetStmt w => $"whilelet {Range(w)} {w.Variable.Text} {Shape(w.Body)}",
        ForStmt f => $"for {Range(f)} {(f.Kind == BindingKind.Var ? "varKeyword" : "letKeyword")} {(f.Names.Length > 0 ? "" : f.Variable.Text)} names {f.Names.Length} {Shape(f.Body)}",
        LabeledStmt l => $"label {Range(l)} {l.Label.Text} {Shape(l.Statement)}",
        ErrorStmt e => $"error {Range(e)}",
        _ => "?",
    };

    private static (int Exit, string Out, string Err) Run(string program, params string[] args)
    {
        var psi = new ProcessStartInfo(program) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        p.WaitForExit();
        return (p.ExitCode, stdout.Result, stderr.Result);
    }

    /// <summary>
    /// Statement inputs, each the body of <c>void f()</c>: blocks, <c>if</c> with likelihood and <c>else</c>, <c>unless</c> with
    /// its input forms, <c>while</c>, <c>do while</c>, <c>while let</c>, <c>for</c> and its input forms, labels, and the
    /// BYN1017/BYN1018 fix merging of <c>ParseBody</c>. A stray <c>else</c> is left out: what follows it depends on
    /// <c>recoverIfNeeded</c> (T3), still a stub here.
    /// </summary>
    [Theory]
    [InlineData("if x {\n}")]
    [InlineData("if (x) {\n}")]
    [InlineData("if (x) while y {\n}")]
    [InlineData("if (x) while y {\n} else while z {\n}")]
    [InlineData("if x {\n}\nelse {\n}")]
    [InlineData("if x {\n} else if y {\n} else {\n}")]
    [InlineData("if never x {\n}")]
    [InlineData("unless x {\n}")]
    [InlineData("unless not x {\n}")]
    [InlineData("unless (not x) while y {\n}")]
    [InlineData("unless (x) while y {\n}")]
    [InlineData("unless x {\n} else {\n}")]
    [InlineData("unless not x {\n} else while y {\n}")]
    [InlineData("unless never x {\n}")]
    [InlineData("while x {\n}")]
    [InlineData("while (x) {\n}")]
    [InlineData("while (x)\n    while y {\n    }")]
    [InlineData("do {\n} while x")]
    [InlineData("do {\n} while (x)")]
    [InlineData("do while x {\n} while y")]
    [InlineData("while let v = x {\n}")]
    [InlineData("for v in xs {\n}")]
    [InlineData("for (let v in xs) {\n}")]
    [InlineData("foreach (var v in xs) {\n}")]
    [InlineData("for (v in xs) while y {\n}")]
    [InlineData("for (a, b) in xs {\n}")]
    [InlineData("for (v) in xs {\n}")]
    [InlineData("for (let i = 0; i < n; i++) {\n}")]
    [InlineData("outer: while x {\n}")]
    [InlineData("{\n    if x {\n    }\n}")]
    public void StatementsMatchTheCSharpParser(string body)
    {
        var (path, error) = Built.Value;
        if (path is null) Assert.Fail($"stmts.byn did not build:\n{error}");
        var input = Path.Combine(Golden.RepoRoot, "tmp", "selfhost-parser-t7", $"{Convert.ToHexString(System.Text.Encoding.UTF8.GetBytes(body))[..Math.Min(48, body.Length * 2)]}-{body.GetHashCode():x8}.byn");
        Directory.CreateDirectory(Path.GetDirectoryName(input)!);
        var text = $"void f() {{\n    {body.Replace("\n", "\n    ")}\n}}\n";
        File.WriteAllText(input, text);
        var (exit, stdout, stderr) = Run(path!, input);
        output.WriteLine(stderr);
        Assert.Equal(0, exit);
        Assert.Equal(Expected(SourceText.FromFile(input)), stdout);
    }
}
