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
}
