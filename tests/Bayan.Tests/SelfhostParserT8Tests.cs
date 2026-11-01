using System.Diagnostics;
using Bayan.Backend.C;
using Bayan.Cli;
using Bayan.Semantics;
using Bayan.Syntax.Diagnostics;
using Bayan.Syntax.Text;
using Xunit.Abstractions;

namespace Bayan.Tests;

/// <summary>Focused checks for the T8 statements-B port (<c>selfhost/parser/stmts2.byn</c>).</summary>
public sealed class SelfhostParserT8Tests(ITestOutputHelper output)
{
    private static string SelfhostDir => Path.Combine(Golden.RepoRoot, "selfhost");

    private static string ParserDir => Path.Combine(SelfhostDir, "parser");

    private static (string? Path, string Error) Build(List<SourceText> sources)
    {
        Environment.SetEnvironmentVariable("BAYAN_CACHE", Path.Combine(Golden.RepoRoot, "tmp", "cache"));
        var c = Compilation.Create(sources, packageRoot: SelfhostDir);
        if (c.Diagnostics.HasErrors) return (null, DiagnosticRenderer.RenderAll(c.Diagnostics.Items, includePath: true));
        var build = Toolchain.Build(CliApp.EmitC(c, BuildMode.Fast), BuildMode.Fast);
        return build.Succeeded ? (build.ExecutablePath, "") : (null, build.Errors);
    }

    private static (int Exit, string Out, string Err) Run(string program, params string[] args)
    {
        var psi = new ProcessStartInfo(program) { RedirectStandardOutput = true, StandardOutputEncoding = System.Text.Encoding.UTF8, RedirectStandardError = true, StandardErrorEncoding = System.Text.Encoding.UTF8, UseShellExecute = false };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        p.WaitForExit();
        return (p.ExitCode, stdout.Result, stderr.Result);
    }

    /// <summary>
    /// The self-contained parts of the port run without any sibling stub: <c>tryAssignOp</c> and
    /// <c>parseGoto</c> use no parse entry point, and <c>exprSpan</c>, <c>patternSpan</c>,
    /// <c>isStatementExpression</c> and <c>checkAssignable</c> take directly built nodes. Everything
    /// else routes through T7/T9/T3 stubs and is checked by T15.
    /// </summary>
    [Fact]
    public void SelfContainedStatementsBuildAndRun()
    {
        const string program = """
            import lexer

            string probe(string src) {
                var bytes = List<u8>()
                for b in src.bytes { bytes.append(b) }
                let source = SourceText.create("t", from: move bytes)
                let keywords = Keywords.build()
                let result = lexer.lex(move source, by: keywords)
                var p = Parser.create(move result)
                let op = tryAssignOp(ref p)
                if (op is { } v) { return $"{v}" }
                return "none"
            }

            string gotoKind(string src) {
                var bytes = List<u8>()
                for b in src.bytes { bytes.append(b) }
                let source = SourceText.create("t", from: move bytes)
                let keywords = Keywords.build()
                let result = lexer.lex(move source, by: keywords)
                var p = Parser.create(move result)
                let stmt = parseGoto(ref p, at: p.current().start)
                switch stmt {
                    case .breakStmt(let b):
                        if b.value.label is null { return "break" }
                        return "breakLabeled"
                    case .errorStmt(_):
                        return "error"
                    default:
                        return "?"
                }
            }

            string spanExpr() {
                let lit = Expr.literalExpr(Owned(LiteralExpr {
                    span: TextSpan { start: 3, length: 5 },
                    kind: .integer,
                    text: "1",
                    suffix: null,
                }))
                let s = exprSpan(in lit)
                return $"expr {s.start} {s.end}"
            }

            string spanPattern() {
                let d = Pattern.discardPattern(Owned(DiscardPattern { span: TextSpan { start: 7, length: 2 } }))
                let s = patternSpan(in d)
                return $"pattern {s.start} {s.end}"
            }

            string stmtExprs() {
                let call = Expr.callExpr(Owned(CallExpr {
                    span: TextSpan { start: 0, length: 1 },
                    callee: Expr.nameExpr(Owned(NameExpr { span: TextSpan { start: 0, length: 1 }, name: TextSpan { start: 0, length: 1 }, typeArgs: List<TypeSyntax>() })),
                    args: List<Argument>(),
                    becauseKeyword: null,
                }))
                let name = Expr.nameExpr(Owned(NameExpr { span: TextSpan { start: 0, length: 1 }, name: TextSpan { start: 0, length: 1 }, typeArgs: List<TypeSyntax>() }))
                let jump = Expr.jumpExpr(Owned(JumpExpr { span: TextSpan { start: 0, length: 1 }, jump: Stmt.breakStmt(Owned(BreakStmt { span: TextSpan { start: 0, length: 1 }, label: null })) }))
                let left = Expr.nameExpr(Owned(NameExpr { span: TextSpan { start: 0, length: 1 }, name: TextSpan { start: 0, length: 1 }, typeArgs: List<TypeSyntax>() }))
                let coal = Expr.binaryExpr(Owned(BinaryExpr { span: TextSpan { start: 0, length: 1 }, op: .coalesce, left: move left, right: move jump }))
                let err = Expr.errorExpr(Owned(ErrorExpr { span: TextSpan { start: 0, length: 1 } }))
                return $"call {isStatementExpression(in call)} name {isStatementExpression(in name)} coal {isStatementExpression(in coal)} err {isStatementExpression(in err)}"
            }

            string assignCheck() {
                var bytes = List<u8>()
                let source = SourceText.create("t", from: move bytes)
                let keywords = Keywords.build()
                let result = lexer.lex(move source, by: keywords)
                var p = Parser.create(move result)
                let before = p.errorCount()
                let n = Expr.nameExpr(Owned(NameExpr { span: TextSpan { start: 0, length: 1 }, name: TextSpan { start: 0, length: 1 }, typeArgs: List<TypeSyntax>() }))
                checkAssignable(ref p, for: in n)
                let afterName = p.errorCount()
                let l = Expr.literalExpr(Owned(LiteralExpr { span: TextSpan { start: 0, length: 1 }, kind: .integer, text: "1", suffix: null }))
                checkAssignable(ref p, for: in l)
                return $"name {afterName - before} literal {p.errorCount() - afterName}"
            }

            void main() {
                using let arena = Arena()
                provide allocator: arena {
                    print($"plusEquals {probe("+=")}")
                    print($"equals {probe("=")}")
                    print($"shl {probe("<<=")}")
                    print($"plus {probe("+")}")
                    print($"goto {gotoKind("goto")}")
                    print($"gotoCase {gotoKind("goto case")}")
                    print(spanExpr())
                    print(spanPattern())
                    print(stmtExprs())
                    print(assignCheck())
                }
            }
            """;
        var sources = Directory.GetFiles(ParserDir, "*.byn").Where(f => Path.GetFileName(f) != "main.byn").Order(StringComparer.Ordinal)
            .Select(f => SourceText.FromFile(f)).Append(SourceText.FromString(Path.Combine(ParserDir, "t8-test.byn"), program)).ToList();
        var (path, error) = Build(sources);
        if (path is null) Assert.Fail($"the T8 statements-B program did not build:\n{error}");
        var (exit, stdout, stderr) = Run(path!);
        output.WriteLine(stderr);
        Assert.Equal(0, exit);
        Assert.Equal(
            """
            plusEquals add
            equals assign
            shl shl
            plus none
            goto break
            gotoCase error
            expr 3 8
            pattern 7 9
            call true name false coal true err true
            name 0 literal 1

            """, stdout);
    }
}
