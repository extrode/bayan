using Bayan.Cli;

namespace Bayan.Tests;

public sealed class FormatTests
{
    private static (int Exit, string Out, string Err) Run(params string[] args)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var exit = CliApp.Run(args, stdout, stderr);
        return (exit, stdout.ToString(), stderr.ToString());
    }

    /// <summary>A fresh, empty directory under <c>tmp/</c>.</summary>
    private static string Fresh(string name)
    {
        var dir = Path.Combine(Golden.RepoRoot, "tmp", "format", name);
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void AFixIsApplied()
    {
        var file = Path.Combine(Fresh("fix"), "main.byn");
        File.WriteAllText(file, "let a = true;\nunless !a { print(\"y\"); }\n");
        var (exit, stdout, stderr) = Run("format", file);
        Assert.True(exit == CliApp.ExitOk, stderr);
        Assert.Equal("let a = true;\nif a { print(\"y\"); }\n", File.ReadAllText(file));
        Assert.Contains("formatted", stdout);
    }

    [Fact]
    public void TwoFixesOnOneLineBothApply()
    {
        var file = Path.Combine(Fresh("two"), "main.byn");
        File.WriteAllText(file, "let a = true;\nlet b = true;\nif (a) { if (b) { print(\"x\"); } }\n");
        var (exit, _, stderr) = Run("format", file);
        Assert.True(exit == CliApp.ExitOk, stderr);
        Assert.Equal("let a = true;\nlet b = true;\nif a { if b { print(\"x\"); } }\n", File.ReadAllText(file));
    }

    [Fact]
    public void ASecondRunChangesNothing()
    {
        var file = Path.Combine(Fresh("idempotent"), "main.byn");
        File.WriteAllText(file, "let a = true;\nif (a) { print(\"x\"); }\n");
        Run("format", file);
        var once = File.ReadAllText(file);
        Assert.Equal("let a = true;\nif a { print(\"x\"); }\n", once);
        var (exit, stdout, _) = Run("format", file);
        Assert.Equal(CliApp.ExitOk, exit);
        Assert.Empty(stdout);
        Assert.Equal(once, File.ReadAllText(file));
    }

    [Fact]
    public void AFileWithARealErrorIsLeftAloneAndOthersAreFormatted()
    {
        var dir = Fresh("error");
        var broken = Path.Combine(dir, "a.byn");
        var fixable = Path.Combine(dir, "b.byn");
        var brokenText = "let a = true;\nif (a) { print(\"x\"; }\n";
        File.WriteAllText(broken, brokenText);
        File.WriteAllText(fixable, "let a = true;\nif (a) { print(\"x\"); }\n");
        var (exit, _, stderr) = Run("format", dir);
        Assert.Equal(CliApp.ExitFailure, exit);
        Assert.Contains("a.byn", stderr);
        Assert.Equal(brokenText, File.ReadAllText(broken));
        Assert.Equal("let a = true;\nif a { print(\"x\"); }\n", File.ReadAllText(fixable));
    }

    [Fact]
    public void DirectoriesAreFormattedRecursively()
    {
        var dir = Fresh("recursive");
        Directory.CreateDirectory(Path.Combine(dir, "sub", "deep"));
        var file = Path.Combine(dir, "sub", "deep", "main.byn");
        File.WriteAllText(file, "let a = true;\nif (a) { print(\"x\"); }\n");
        var (exit, _, stderr) = Run("format", dir);
        Assert.True(exit == CliApp.ExitOk, stderr);
        Assert.Equal("let a = true;\nif a { print(\"x\"); }\n", File.ReadAllText(file));
    }

    [Fact]
    public void AnInputFormWithoutAFixIsReported()
    {
        var file = Path.Combine(Fresh("nofix"), "main.byn");
        var text = "i32!Foo? f() { return 1; }\n";
        File.WriteAllText(file, text);
        var (exit, _, stderr) = Run("format", file);
        Assert.Equal(CliApp.ExitFailure, exit);
        Assert.Contains("BYN1031", stderr);
        Assert.Contains("main.byn", stderr);
        Assert.Equal(text, File.ReadAllText(file));
    }

    [Fact]
    public void NonConvergenceIsReported()
    {
        var file = Path.Combine(Fresh("passes"), "main.byn");
        File.WriteAllText(file, "let a = true;\nif (a) { print(\"x\"); }\n");
        var passes = Format.MaxPasses;
        Format.MaxPasses = 1;
        try
        {
            var (exit, _, stderr) = Run("format", file);
            Assert.Equal(CliApp.ExitFailure, exit);
            Assert.Contains("still changing after 1 passes", stderr);
        }
        finally { Format.MaxPasses = passes; }
    }

    [Fact]
    public void AnUnreadableFileIsReportedAndTheRestAreFormatted()
    {
        if (OperatingSystem.IsWindows()) return;
        var dir = Fresh("io");
        var locked = Path.Combine(dir, "a.byn");
        var fixable = Path.Combine(dir, "b.byn");
        File.WriteAllText(locked, "let a = true;\n");
        File.WriteAllText(fixable, "let a = true;\nif (a) { print(\"x\"); }\n");
        File.SetUnixFileMode(locked, UnixFileMode.None);
        try
        {
            var (exit, _, stderr) = Run("format", dir);
            Assert.Equal(CliApp.ExitFailure, exit);
            Assert.Contains("a.byn", stderr);
            Assert.Equal("let a = true;\nif a { print(\"x\"); }\n", File.ReadAllText(fixable));
        }
        finally { File.SetUnixFileMode(locked, UnixFileMode.UserRead | UnixFileMode.UserWrite); }
    }

    [Fact]
    public void NoTempFileIsLeftBehind()
    {
        var dir = Fresh("temp");
        File.WriteAllText(Path.Combine(dir, "main.byn"), "let a = true;\nif (a) { print(\"x\"); }\n");
        Run("format", dir);
        Assert.Equal(["main.byn"], Directory.GetFiles(dir).Select(Path.GetFileName));
    }

    [Fact]
    public void IdenticalFixesApplyOnce()
    {
        var at = new Bayan.Syntax.Text.TextSpan(1, 0);
        var fix = new Bayan.Syntax.Diagnostics.Fix(at, "X");
        var result = Format.ApplyFixes("ab"u8.ToArray(), [fix, new Bayan.Syntax.Diagnostics.Fix(at, "X")]);
        Assert.Equal("aXb", System.Text.Encoding.UTF8.GetString(result));
    }
}
