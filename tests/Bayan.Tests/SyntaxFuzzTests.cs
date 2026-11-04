using System.Diagnostics;

namespace Bayan.Tests;

/// <summary><c>tools/Bayan.SyntaxFuzz</c>, run with <c>dotnet run</c> from the repository root.</summary>
public sealed class SyntaxFuzzTests
{
    private static (int Exit, string Out, string Err) Run(params string[] args)
    {
        var psi = new ProcessStartInfo("dotnet") { WorkingDirectory = Golden.RepoRoot, RedirectStandardOutput = true, StandardOutputEncoding = System.Text.Encoding.UTF8, RedirectStandardError = true, StandardErrorEncoding = System.Text.Encoding.UTF8, UseShellExecute = false };
        foreach (var a in (string[])["run", "--project", Path.Combine("tools", "Bayan.SyntaxFuzz"), "--", .. args]) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEndLfAsync();
        var stderr = p.StandardError.ReadToEndLfAsync();
        p.WaitForExit();
        return (p.ExitCode, stdout.Result, stderr.Result);
    }

    [Fact]
    public void AnEmptyCasesDirectoryIsAnError()
    {
        var dir = Path.Combine(Golden.RepoRoot, "tmp", "syntax-fuzz-empty");
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        Directory.CreateDirectory(dir);
        var (exit, stdout, stderr) = Run("--cases", dir, "--count", "10");
        Assert.True(exit == 2, stdout + stderr);
        Assert.Empty(stdout);
        Assert.Equal($"no *.byn files under {dir}\n", stderr);
    }
}
