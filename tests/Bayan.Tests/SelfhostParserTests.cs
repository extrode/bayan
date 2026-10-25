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

/// <summary><c>selfhost/parser</c>, built by the bootstrap compiler and run against the C# parser.</summary>
public sealed class SelfhostParserTests(ITestOutputHelper output)
{
    private static string SelfhostDir => Path.Combine(Golden.RepoRoot, "selfhost");

    private static string ParserDir => Path.Combine(SelfhostDir, "parser");

    /// <summary>
    /// The built <c>bayan-parse</c>, or the compiler's complaint. The package root is <c>selfhost/</c> so that
    /// <c>import lexer</c> resolves; <see cref="Compilation.FromPath"/> would use <c>selfhost/parser/</c> (Q430).
    /// </summary>
    private static readonly Lazy<(string? Path, string Error)> BayanParse = new(() =>
        BuildParser(Directory.GetFiles(ParserDir, "*.byn").Order(StringComparer.Ordinal).Select(f => SourceText.FromFile(f)).ToList()));

    /// <summary><paramref name="sources"/> built as the parser module, or the compiler's complaint.</summary>
    private static (string? Path, string Error) BuildParser(List<SourceText> sources)
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
            var (path, error) = BayanParse.Value;
            if (path is null) Assert.Fail($"selfhost/parser did not build:\n{error}");
            return path!;
        }
    }

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

    /// <summary><see cref="Run"/> with stdout kept as bytes, so invalid UTF-8 is compared as printed.</summary>
    private static (int Exit, byte[] Out, string Err) RunBytes(string program, params string[] args) => RunBytes(program, null, args);

    /// <summary><see cref="RunBytes(string, string[])"/> with <paramref name="input"/> fed to stdin.</summary>
    private static (int Exit, byte[] Out, string Err) RunBytes(string program, byte[]? input, params string[] args)
    {
        var psi = new ProcessStartInfo(program) { RedirectStandardInput = input is not null, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var stdout = new MemoryStream();
        var copy = p.StandardOutput.BaseStream.CopyToAsync(stdout);
        var stderr = p.StandardError.ReadToEndAsync();
        if (input is not null)
        {
            p.StandardInput.BaseStream.Write(input);
            p.StandardInput.Close();
        }
        p.WaitForExit();
        copy.Wait();
        return (p.ExitCode, stdout.ToArray(), stderr.Result);
    }

    /// <summary>The stubbed program builds through <c>import lexer</c> and prints the C# dump of an empty file.</summary>
    [Fact]
    public void EmptyFileDumpsAnEmptyCompilationUnit()
    {
        var path = Path.Combine(Golden.RepoRoot, "tmp", "selfhost-parser", "empty.byn");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, []);
        var (exit, stdout, stderr) = Run(Built, path);
        output.WriteLine(stderr);
        Assert.Equal(0, exit);
        Assert.Equal("CompilationUnit\n", stdout);
        Assert.Equal(AstDump.Dump(SyntaxTree.Parse(SourceText.FromFile(path))), stdout);
    }

    /// <summary>
    /// The node shapes of <c>nodes.byn</c> (Q435): <c>FunctionDecl</c> → <c>BlockStmt</c> → <c>BinaryExpr</c> of two
    /// <c>LiteralExpr</c>, built under <c>provide allocator: arena</c> with a <c>List&lt;Stmt&gt;</c>, <c>none</c>
    /// children and field defaults, then read back through <c>.value</c>. The parser's files stand in for the
    /// module, with this <c>main</c> in place of <c>main.byn</c>.
    /// </summary>
    [Fact]
    public void NodeShapesBuildAndReadBack()
    {
        const string program = """
            string exprText(in Expr e) {
                switch e {
                    case .literalExpr(let n):
                        return n.value.text
                    case .binaryExpr(let n):
                        return $"({exprText(in n.value.left)} {n.value.op} {exprText(in n.value.right)})"
                    case .none:
                        return "none"
                    default:
                        return "?"
                }
            }

            void main() {
                using let arena = Arena()
                provide allocator: arena {
                    let one = Expr.literalExpr(Owned(LiteralExpr { span: TextSpan { start: 26, length: 1 }, kind: .integer, text: "1", suffix: null }))
                    let two = Expr.literalExpr(Owned(LiteralExpr { span: TextSpan { start: 30, length: 1 }, kind: .integer, text: "2", suffix: null }))
                    var statements = List<Stmt>()
                    statements.append(Stmt.returnStmt(Owned(ReturnStmt {
                        span: TextSpan { start: 19, length: 12 },
                        value: Expr.binaryExpr(Owned(BinaryExpr { span: TextSpan { start: 26, length: 5 }, op: .add, left: move one, right: move two })),
                    })))
                    statements.append(Stmt.returnStmt(Owned(ReturnStmt { span: TextSpan { start: 32, length: 6 }, value: .none })))
                    var path = List<TextSpan>()
                    path.append(TextSpan { start: 0, length: 3 })
                    var members = List<Decl>()
                    members.append(Decl.functionDecl(Owned(FunctionDecl {
                        span: TextSpan { start: 0, length: 40 },
                        modifiers: modifierPublic | modifierStatic,
                        returnType: TypeSyntax.namedType(Owned(NamedType { span: TextSpan { start: 0, length: 3 }, path: move path, args: List<TypeSyntax>() })),
                        name: TextSpan { start: 4, length: 1 },
                        params: List<Param>(),
                        failsKeyword: null,
                        body: Stmt.blockStmt(Owned(BlockStmt { span: TextSpan { start: 8, length: 32 }, statements: move statements })),
                        expressionBody: .none,
                    })))
                    let unit = CompilationUnit { span: TextSpan { start: 0, length: 40 }, members: move members }
                    for d in unit.members {
                        switch d {
                            case .functionDecl(let f):
                                print($"function {f.value.span.start}..{f.value.span.end} static {(f.value.modifiers & modifierStatic) != 0} params {f.value.params.length} typeParams {f.value.typeParams.length}")
                                switch f.value.returnType {
                                    case .namedType(let t):
                                        print($"returns path {t.value.path.length} args {t.value.args.length}")
                                    default:
                                        print("returns ?")
                                }
                                switch f.value.body {
                                    case .blockStmt(let b):
                                        print($"block {b.value.span.start} statements {b.value.statements.length}")
                                        for s in b.value.statements {
                                            switch s {
                                                case .returnStmt(let r):
                                                    print($"return {exprText(in r.value.value)}")
                                                default:
                                                    print("statement ?")
                                            }
                                        }
                                    default:
                                        print("body ?")
                                }
                                print($"expressionBody {exprText(in f.value.expressionBody)}")
                            default:
                                print("decl ?")
                        }
                    }
                }
            }
            """;
        var sources = Directory.GetFiles(ParserDir, "*.byn").Where(f => Path.GetFileName(f) != "main.byn").Order(StringComparer.Ordinal)
            .Select(f => SourceText.FromFile(f)).Append(SourceText.FromString(Path.Combine(ParserDir, "nodes-test.byn"), program)).ToList();
        var (path, error) = BuildParser(sources);
        if (path is null) Assert.Fail($"the node shapes did not build:\n{error}");
        var (exit, stdout, stderr) = Run(path!);
        output.WriteLine(stderr);
        Assert.Equal(0, exit);
        Assert.Equal(
            """
            function 0..40 static true params 0 typeParams 0
            returns path 1 args 0
            block 8 statements 2
            return (1 add 2)
            return none
            expressionBody none

            """, stdout);
    }
}
