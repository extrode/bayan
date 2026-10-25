using System.Diagnostics;
using Bayan.Backend.C;
using Bayan.Cli;
using Bayan.Semantics;
using Bayan.Syntax.Text;
using Xunit.Abstractions;

namespace Bayan.Tests;

/// <summary>T12: the S-expression dump (<c>dump2.byn</c>) and the span walker (<c>spans.byn</c>),
/// exercised in isolation with a stand-in <c>main</c>, before the parsing tasks merge.</summary>
public sealed class SelfhostParserT12Tests(ITestOutputHelper output)
{
    private static string SelfhostDir => Path.Combine(Golden.RepoRoot, "selfhost");

    private static string ParserDir => Path.Combine(SelfhostDir, "parser");

    /// <summary>Builds the parser files plus <paramref name="main"/>, or the compiler's complaint.</summary>
    private static (string? Path, string Error) BuildParser(string main)
    {
        Environment.SetEnvironmentVariable("BAYAN_CACHE", Path.Combine(Golden.RepoRoot, "tmp", "cache"));
        var sources = Directory.GetFiles(ParserDir, "*.byn").Where(f => Path.GetFileName(f) != "main.byn")
            .Order(StringComparer.Ordinal).Select(f => SourceText.FromFile(f))
            .Append(SourceText.FromString(Path.Combine(ParserDir, "t12-test.byn"), main)).ToList();
        var c = Compilation.Create(sources, packageRoot: SelfhostDir);
        if (c.Diagnostics.HasErrors) return (null, Bayan.Syntax.Diagnostics.DiagnosticRenderer.RenderAll(c.Diagnostics.Items, includePath: true));
        var build = Toolchain.Build(CliApp.EmitC(c, BuildMode.Fast), BuildMode.Fast);
        return build.Succeeded ? (build.ExecutablePath, "") : (null, build.Errors);
    }

    private static (int Exit, string Out, string Err) Run(string program)
    {
        var psi = new ProcessStartInfo(program) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        p.WaitForExit();
        return (p.ExitCode, stdout.Result, stderr.Result);
    }

    private string BuildAndRun(string main)
    {
        var (path, error) = BuildParser(main);
        if (path is null)
        {
            var dump = Path.Combine(Golden.RepoRoot, "tmp", "t12-build-error.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(dump)!);
            File.WriteAllText(dump, error);
            Assert.Fail($"selfhost/parser did not build (full error at {dump}):\n{error}");
        }
        var (exit, stdout, stderr) = Run(path!);
        output.WriteLine(stderr);
        return stdout;
    }

    /// <summary>
    /// Builds the module over the source <c>a+b</c> and dumps a binary expression, a named type and a
    /// let-pattern, then runs the span walker over a good tree and a tree with an out-of-range child.
    /// </summary>
    [Fact]
    public void DumpExprTypePatternAndCheckSpans()
    {
        const string main = """
            import lexer
            void main() {
                using let arena = Arena()
                provide allocator: arena {
                    var bytes = List<u8>()
                    bytes.append(0x61)
                    bytes.append(0x2B)
                    bytes.append(0x62)
                    var p = Parser.create(lexer.lex(SourceText.create("<test>", from: move bytes), by: Keywords.build()))
                    let left = Expr.nameExpr(Owned(NameExpr { span: TextSpan { start: 0, length: 1 }, name: TextSpan { start: 0, length: 1 }, typeArgs: List<TypeSyntax>() }))
                    let right = Expr.nameExpr(Owned(NameExpr { span: TextSpan { start: 2, length: 1 }, name: TextSpan { start: 2, length: 1 }, typeArgs: List<TypeSyntax>() }))
                    let bin = Expr.binaryExpr(Owned(BinaryExpr { span: TextSpan { start: 0, length: 3 }, op: .add, left: move left, right: move right }))
                    var sb = StrBuf()
                    dumpExpr(in bin, from: ref p, to: ref sb)
                    print($"expr {sb.finish()}")

                    var path = List<TextSpan>()
                    path.append(TextSpan { start: 0, length: 1 })
                    let ty = TypeSyntax.namedType(Owned(NamedType { span: TextSpan { start: 0, length: 1 }, path: move path, args: List<TypeSyntax>() }))
                    var tb = StrBuf()
                    dumpType(in ty, from: ref p, to: ref tb)
                    print($"type {tb.finish()}")

                    let pat = Pattern.varPattern(Owned(VarPattern { span: TextSpan { start: 0, length: 1 }, name: TextSpan { start: 0, length: 1 }, isLet: true }))
                    var pb = StrBuf()
                    dumpPattern(in pat, from: ref p, to: ref pb)
                    print($"pattern {pb.finish()}")

                    var members = List<Decl>()
                    members.append(Decl.globalStatement(Owned(GlobalStatement {
                        span: TextSpan { start: 0, length: 3 },
                        statement: Stmt.exprStmt(Owned(ExprStmt { span: TextSpan { start: 0, length: 3 }, expr: move bin })),
                    })))
                    let unit = CompilationUnit { span: TextSpan { start: 0, length: 3 }, members: move members }
                    var cb = StrBuf()
                    checkSpans(in unit, until: 3, to: ref cb)
                    print($"spans [{cb.finish()}]")

                    var bad = List<Decl>()
                    var badPath = List<TextSpan>()
                    badPath.append(TextSpan { start: 12, length: 3 })
                    bad.append(Decl.importDecl(Owned(ImportDecl { span: TextSpan { start: 0, length: 10 }, path: move badPath, alias: null })))
                    let badUnit = CompilationUnit { span: TextSpan { start: 0, length: 15 }, members: move bad }
                    var bb = StrBuf()
                    checkSpans(in badUnit, until: 15, to: ref bb)
                    print($"bad [{bb.finish()}]")

                    var big = List<Decl>()
                    big.append(Decl.importDecl(Owned(ImportDecl { span: TextSpan { start: 0, length: 10 }, path: List<TextSpan>(), alias: null })))
                    let bigUnit = CompilationUnit { span: TextSpan { start: 0, length: 10 }, members: move big }
                    var bc = StrBuf()
                    checkSpans(in bigUnit, until: 3, to: ref bc)
                    print($"big [{bc.finish()}]")
                }
            }
            """;
        var stdout = BuildAndRun(main);
        var dump = Path.Combine(Golden.RepoRoot, "tmp", "t12-stdout.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(dump)!);
        File.WriteAllText(dump, stdout);
        Assert.Equal(
            """
            expr (+ a b)
            type a
            pattern (let a)
            spans []
            bad [span out of range: importDecl path 12+3 outside 0+10
            ]
            big [span out of range: compilationUnit 0+10
            span out of range: importDecl 0+10
            ]

            """, stdout);
    }
}
