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
        var psi = new ProcessStartInfo(program) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        p.WaitForExit();
        return (p.ExitCode, stdout.Result, stderr.Result);
    }

    /// <summary>
    /// <c>tryAssignOp</c> and <c>parseGoto</c> are self-contained (they run through no sibling stub
    /// that changes the tree), so they can be exercised in isolation before the wave merges.
    /// </summary>
    [Fact]
    public void TryAssignOpAndParseGotoBuildAndRun()
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

            void main() {
                using let arena = Arena()
                provide allocator: arena {
                    print($"plusEquals {probe("+=")}")
                    print($"equals {probe("=")}")
                    print($"shl {probe("<<=")}")
                    print($"plus {probe("+")}")
                    print($"goto {gotoKind("goto")}")
                    print($"gotoCase {gotoKind("goto case")}")
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

            """, stdout);
    }
}
