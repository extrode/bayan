using Bayan.Cli;

namespace Bayan.Tests;

public sealed class CliTests
{
    private static (int Exit, string Out, string Err) Run(params string[] args)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var exit = CliApp.Run(args, stdout, stderr);
        return (exit, stdout.ToString(), stderr.ToString());
    }

    [Fact]
    public void VersionPrintsVersion()
    {
        var (exit, stdout, _) = Run("--version");
        Assert.Equal(CliApp.ExitOk, exit);
        Assert.Equal($"bayan {CliApp.Version}{Environment.NewLine}", stdout);
    }

    [Fact]
    public void NoArgumentsIsUsageError()
    {
        var (exit, stdout, stderr) = Run();
        Assert.Equal(CliApp.ExitUsage, exit);
        Assert.Empty(stdout);
        Assert.Contains("usage: bayan", stderr);
    }

    [Fact]
    public void UnknownCommandIsUsageError()
    {
        var (exit, _, stderr) = Run("frobnicate");
        Assert.Equal(CliApp.ExitUsage, exit);
        Assert.Contains("unknown command `frobnicate`", stderr);
    }

    [Fact]
    public void ExplainPrintsTheDocCaseInsensitively()
    {
        var (exit, stdout, _) = Run("explain", "byn0003");
        Assert.Equal(CliApp.ExitOk, exit);
        Assert.StartsWith("# BYN0003:", stdout);
    }

    [Theory]
    [InlineData("BYN9999")]
    [InlineData("BYN00")]
    [InlineData("../BYN0001")]
    public void ExplainUnknownCodeFails(string code)
    {
        var (exit, stdout, stderr) = Run("explain", code);
        Assert.Equal(CliApp.ExitFailure, exit);
        Assert.Empty(stdout);
        Assert.Contains("no explanation", stderr);
    }

    private static string Example(string name) => Path.Combine(Golden.RepoRoot, "examples", name, "main.byn");

    [Fact]
    public void CheckAcceptsAValidProgram()
    {
        var (exit, _, stderr) = Run("check", Example("hello"));
        Assert.Equal(CliApp.ExitOk, exit);
        Assert.Empty(stderr);
    }

    [Fact]
    public void CheckReportsErrors()
    {
        var (exit, _, stderr) = Run("check", Path.Combine(Golden.CasesRoot, "diag", "valid-calls.byn"));
        Assert.Equal(CliApp.ExitFailure, exit);
        Assert.Contains("error BYN", stderr);
    }

    [Fact]
    public void CheckOfAMissingPathFails()
    {
        var (exit, _, stderr) = Run("check", "no-such-file.byn");
        Assert.Equal(CliApp.ExitFailure, exit);
        Assert.Contains("not found", stderr);
    }

    [Fact]
    public void BuildEmitsC()
    {
        var (exit, stdout, _) = Run("build", "--emit-c", Example("hello"));
        Assert.Equal(CliApp.ExitOk, exit);
        Assert.Contains("#include \"bayan-rt.h\"", stdout);
        Assert.Contains("int main(int argc, char** argv)", stdout);
    }

    [Fact]
    public void BuildRejectsAnUnknownMode()
    {
        var (exit, _, stderr) = Run("build", "--mode", "turbo", Example("hello"));
        Assert.Equal(CliApp.ExitUsage, exit);
        Assert.Contains("unknown mode `turbo`", stderr);
    }

    [Fact]
    public void BuildWritesAnExecutable()
    {
        Environment.SetEnvironmentVariable("BAYAN_CACHE", Path.Combine(Golden.RepoRoot, "tmp", "cache"));
        var output = Path.Combine(Golden.RepoRoot, "tmp", "cli-build-hello" + (OperatingSystem.IsWindows() ? ".exe" : ""));
        if (File.Exists(output)) File.Delete(output);
        var (exit, _, stderr) = Run("build", "-o", output, Example("hello"));
        Assert.True(exit == CliApp.ExitOk, stderr);
        Assert.True(File.Exists(output));
    }

    [Fact]
    public void RunReturnsTheProgramsExitCode()
    {
        Environment.SetEnvironmentVariable("BAYAN_CACHE", Path.Combine(Golden.RepoRoot, "tmp", "cache"));
        var dir = Path.Combine(Golden.RepoRoot, "tmp", "cli-run");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "main.byn"), "panic(\"stop\");\n");
        var (exit, _, _) = Run("run", Path.Combine(dir, "main.byn"));
        Assert.Equal(134, exit);
    }
}
