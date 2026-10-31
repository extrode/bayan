using System.Diagnostics;
using Bayan.Backend.C;
using Bayan.Cli;
using Bayan.Semantics;
using Bayan.Syntax.Diagnostics;
using Bayan.Syntax.Text;
using Xunit.Abstractions;

namespace Bayan.Tests;

/// <summary>T11: the declaration and statement halves of the tree dump, checked before the parsing tasks merge.</summary>
public sealed class SelfhostParserT11Tests(ITestOutputHelper output)
{
    private static string SelfhostDir => Path.Combine(Golden.RepoRoot, "selfhost");

    private static string ParserDir => Path.Combine(SelfhostDir, "parser");

    private static (string? Path, string Error) BuildParser(List<SourceText> sources)
    {
        Environment.SetEnvironmentVariable("BAYAN_CACHE", Path.Combine(Golden.RepoRoot, "tmp", "cache"));
        var c = Compilation.Create(sources, packageRoot: SelfhostDir);
        if (c.Diagnostics.HasErrors) return (null, DiagnosticRenderer.RenderAll(c.Diagnostics.Items, includePath: true));
        var build = Toolchain.Build(CliApp.EmitC(c, BuildMode.Fast), BuildMode.Fast);
        return build.Succeeded ? (build.ExecutablePath, "") : (null, build.Errors);
    }

    private static (int Exit, string Out, string Err) Run(string program)
    {
        var psi = new ProcessStartInfo(program) { RedirectStandardOutput = true, StandardOutputEncoding = new System.Text.UTF8Encoding(false), RedirectStandardError = true, StandardErrorEncoding = new System.Text.UTF8Encoding(false), UseShellExecute = false };
        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        p.WaitForExit();
        return (p.ExitCode, stdout.Result, stderr.Result);
    }

    /// <summary>
    /// The declarations and statements T11 owns dump with the same spelling as <c>AstDump</c>, without the
    /// S-expressions T12 owns: an <c>Import</c> (with alias), an <c>AssociatedType</c>, and a
    /// <c>GlobalStatement</c> wrapping a <c>Block</c> of an <c>Empty</c> and a label-less <c>Break</c>. The
    /// type/expression/pattern entry points are still stubs, so only name-only and name-less nodes are
    /// exercised; a tree with a type or expression is T15's once <c>dump2.byn</c> lands.
    /// </summary>
    [Fact]
    public void DumpWritesDeclarationsAndStatements()
    {
        const string program = """
            import lexer

            void main() {
                var bytes = List<u8>()
                for b in "import a.b".bytes { bytes.append(b) }
                let source = SourceText.create("t", from: move bytes)
                var p = Parser.create(lexer.lex(move source, by: Keywords.build()))
                var path = List<TextSpan>()
                path.append(TextSpan { start: 7, length: 1 })
                path.append(TextSpan { start: 9, length: 1 })
                var members = List<Decl>()
                members.append(Decl.importDecl(Owned(ImportDecl { span: TextSpan { start: 0, length: 10 }, path: move path, alias: TextSpan { start: 7, length: 1 } })))
                members.append(Decl.associatedTypeDecl(Owned(AssociatedTypeDecl { span: TextSpan { start: 0, length: 0 }, name: TextSpan { start: 9, length: 1 } })))
                var stmts = List<Stmt>()
                stmts.append(Stmt.emptyStmt(Owned(EmptyStmt { span: TextSpan { start: 0, length: 0 } })))
                stmts.append(Stmt.breakStmt(Owned(BreakStmt { span: TextSpan { start: 0, length: 0 }, label: null })))
                members.append(Decl.globalStatement(Owned(GlobalStatement {
                    span: TextSpan { start: 0, length: 0 },
                    statement: Stmt.blockStmt(Owned(BlockStmt { span: TextSpan { start: 0, length: 0 }, statements: move stmts })),
                })))
                let unit = CompilationUnit { span: TextSpan { start: 0, length: 10 }, members: move members }
                var sb = StrBuf()
                dump(in unit, from: ref p, to: ref sb)
                Io.stdout.write(sb.finish())
                print($"[{mods(modifierPublic | modifierReadonly)}]")
                print(bindingKindText(.letKeyword))
                print(assignText(.add))
            }
            """;
        var sources = Directory.GetFiles(ParserDir, "*.byn").Where(f => Path.GetFileName(f) != "main.byn").Order(StringComparer.Ordinal)
            .Select(f => SourceText.FromFile(f)).Append(SourceText.FromString(Path.Combine(ParserDir, "dump-t11-test.byn"), program)).ToList();
        var (path, error) = BuildParser(sources);
        if (path is null) Assert.Fail($"selfhost/parser did not build:\n{error}");
        var (exit, stdout, stderr) = Run(path!);
        output.WriteLine(stderr);
        Assert.Equal(0, exit);
        Assert.Equal(
            """
            CompilationUnit
              Import a.b as a
              AssociatedType b
              Block
                Empty
                Break
            [public readonly ]
            Let
            +=

            """, stdout);
    }
}
