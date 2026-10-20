using System.Diagnostics;
using System.Text.RegularExpressions;
using Bayan.Backend.C;
using Bayan.Cli;
using Bayan.Semantics;
using Bayan.Syntax.Lexing;

namespace Bayan.Tests;

/// <summary><c>selfhost/lexer</c>, built by the bootstrap compiler and run against the <c>lex/</c> goldens.</summary>
public sealed class SelfhostLexerTests
{
    public static TheoryData<string> Cases => Golden.Cases("lex");

    private static string LexerDir => Path.Combine(Golden.RepoRoot, "selfhost", "lexer");

    /// <summary>The built <c>bayan-lex</c>, or the compiler's complaint.</summary>
    private static readonly Lazy<(string? Path, string Error)> BayanLex = new(() =>
    {
        Environment.SetEnvironmentVariable("BAYAN_CACHE", Path.Combine(Golden.RepoRoot, "tmp", "cache"));
        var c = Compilation.FromPath(Path.Combine(LexerDir, "main.byn"));
        if (c.Diagnostics.HasErrors) return (null, string.Join("\n", c.Diagnostics.Items));
        var build = Toolchain.Build(CliApp.EmitC(c), BuildMode.Fast);
        return build.Succeeded ? (build.ExecutablePath, "") : (null, build.Errors);
    });

    private static string Built
    {
        get
        {
            var (path, error) = BayanLex.Value;
            if (path is null) Assert.Fail($"selfhost/lexer/main.byn did not build:\n{error}");
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

    [Theory]
    [MemberData(nameof(Cases))]
    public void LexGolden(string name)
    {
        var input = Path.Combine(Golden.CasesRoot, name);
        var (_, stdout, _) = Run(Built, input);
        if (Golden.Compare(Path.ChangeExtension(input, ".tokens"), stdout, update: false).Failure is { } failure) Assert.Fail(failure);
    }

    [Fact]
    public void LexDiffFindsNoDifferences()
    {
        var (exit, stdout, stderr) = Run(LexDiffTests.LexDiff.Value, "--left", Built, "--golden", ".tokens", Path.Combine(Golden.CasesRoot, "lex"));
        Assert.True(exit == 0, stdout + stderr);
        Assert.Matches(@"^\d+ files, 0 differ\r?\n$", stderr);
    }

    /// <summary>
    /// The dump prints a Bayan <c>TokenKind</c> by upper-casing the first byte of its camelCase name,
    /// so the C# names must be exactly that form of the Bayan members, in the same order.
    /// </summary>
    [Fact]
    public void TokenKindNamesAreTheCamelCaseNamesUpperCased()
    {
        var source = File.ReadAllText(Path.Combine(LexerDir, "tokens.byn"));
        var body = Regex.Match(source, @"^enum TokenKind \{\n(.*?)^\}", RegexOptions.Multiline | RegexOptions.Singleline).Groups[1].Value;
        var bayan = Regex.Matches(body, @"^\s*([a-z]\w*),", RegexOptions.Multiline).Select(m => m.Groups[1].Value).ToArray();
        var csharp = Enum.GetNames<TokenKind>();
        foreach (var name in csharp)
            Assert.True(char.IsAsciiLetterUpper(name[0]), name);
        Assert.Equal(csharp, bayan.Select(n => char.ToUpperInvariant(n[0]) + n[1..]));
    }
}
