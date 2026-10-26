using Bayan.Backend.C;
using Bayan.Cli;
using Bayan.Semantics;
using Bayan.Syntax.Diagnostics;
using Bayan.Syntax.Text;

namespace Bayan.Tests;

/// <summary>
/// <c>selfhost/semantics</c>, the self-hosted checker's skeleton: <c>ctx.byn</c>'s shared types, the
/// header-only pass files and a <c>main</c> that returns 0. Later tasks add the passes' bodies.
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

    [Fact]
    public void MainReturnsZero()
    {
        var (exit, output, error) = SelfhostParserTests.Run(Built);
        Assert.Equal(0, exit);
        Assert.Equal("", output);
        Assert.Equal("", error);
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
}
