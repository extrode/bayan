using System.Diagnostics;
using Bayan.Backend.C;
using Bayan.Cli;
using Bayan.Semantics;

namespace Bayan.Tests;

/// <summary><c>tools/lex-diff</c>, built by the bootstrap compiler and run against <c>bayan lex</c> and the <c>lex/</c> goldens.</summary>
public sealed class LexDiffTests
{
    internal static readonly Lazy<string> LexDiff = new(() =>
    {
        Environment.SetEnvironmentVariable("BAYAN_CACHE", Path.Combine(Golden.RepoRoot, "tmp", "cache"));
        var c = Compilation.FromPath(Path.Combine(Golden.RepoRoot, "tools", "lex-diff", "main.byn"));
        Assert.False(c.Diagnostics.HasErrors, string.Join("\n", c.Diagnostics.Items));
        var build = Toolchain.Build(CliApp.EmitC(c, BuildMode.Safe), BuildMode.Safe);
        Assert.True(build.Succeeded, build.Errors);
        return build.ExecutablePath!;
    });

    /// <summary>The <c>bayan</c> apphost next to the test assembly.</summary>
    internal static string Bayan => Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "bayan.exe" : "bayan");

    private static string LexCases => Path.Combine(Golden.CasesRoot, "lex");

    private static (int Exit, string Out, string Err) Run(params string[] args)
    {
        var psi = new ProcessStartInfo(LexDiff.Value) { RedirectStandardOutput = true, StandardOutputEncoding = System.Text.Encoding.UTF8, RedirectStandardError = true, StandardErrorEncoding = System.Text.Encoding.UTF8, UseShellExecute = false };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEndLfAsync();
        var stderr = p.StandardError.ReadToEndLfAsync();
        p.WaitForExit();
        return (p.ExitCode, stdout.Result, stderr.Result);
    }

    [Fact]
    public void BayanLexMatchesTheGoldens()
    {
        var (exit, stdout, stderr) = Run("--left", Bayan + " lex", "--golden", ".tokens", LexCases);
        Assert.True(exit == 0, stdout + stderr);
        Assert.Empty(stdout);
        Assert.Matches(@"^\d+ files, 0 differ\r?\n$", stderr);
    }

    [Fact]
    public void BayanLexAgreesWithItself()
    {
        var (exit, stdout, stderr) = Run("--left", Bayan + " lex", "--right", Bayan + " lex", LexCases);
        Assert.True(exit == 0, stdout + stderr);
    }

    [Fact]
    public void ATamperedGoldenDiffers()
    {
        var dir = Path.Combine(Golden.RepoRoot, "tmp", "lex-diff-tampered");
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        Directory.CreateDirectory(dir);
        foreach (var file in Directory.GetFiles(LexCases)) File.Copy(file, Path.Combine(dir, Path.GetFileName(file)));
        var golden = Path.Combine(dir, "chars.tokens");
        var lines = File.ReadAllLines(golden);
        lines[2] = "3:1 Tampered";
        File.WriteAllLines(golden, lines);
        var (exit, stdout, stderr) = Run("--left", Bayan + " lex", "--golden", ".tokens", dir);
        Assert.Equal(1, exit);
        Assert.Matches(@"chars\.byn:3: .* \| 3:1 Tampered\r?\n$", stdout);
        Assert.Single(stdout.Trim().Split('\n'));
        Assert.EndsWith("files, 1 differ\n", stderr.ReplaceLineEndings("\n"));
    }

    [Fact]
    public void NoArgumentsIsAUsageError()
    {
        var (exit, stdout, stderr) = Run();
        Assert.Equal(2, exit);
        Assert.Empty(stdout);
        Assert.Contains("usage: lex-diff", stderr);
    }

    [Fact]
    public void ACommandThatCannotStartExitsTwo()
    {
        var (exit, _, stderr) = Run("--left", "bayan-no-such-program", "--golden", ".tokens", LexCases);
        Assert.Equal(2, exit);
        Assert.Contains("cannot run `bayan-no-such-program`", stderr);
    }
}
