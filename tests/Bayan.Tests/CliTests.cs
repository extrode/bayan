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

    private static string TestSuite(string name, string tests)
    {
        Environment.SetEnvironmentVariable("BAYAN_CACHE", Path.Combine(Golden.RepoRoot, "tmp", "cache"));
        var dir = Path.Combine(Golden.RepoRoot, "tmp", name);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "main.byn"), "void main() {\n    print(\"main\");\n}\n");
        File.WriteAllText(Path.Combine(dir, "main.test.byn"), tests);
        return dir;
    }

    private static string WithoutTimes(string text) =>
        System.Text.RegularExpressions.Regex.Replace(text, @"\(\d+\.\d ms\)", "(T)");

    [Fact]
    public void TestReportsPassFailAndPanicPerTest()
    {
        var dir = TestSuite("cli-test-mixed", """
            error enum E {
                /// It went wrong.
                wrong,
            }

            void fail() fails E {
                return .wrong;
            }

            test "passes" {
                assert(1 + 1 == 2);
            }

            test "asserts" {
                print("before");
                assert(1 + 1 == 3);
            }

            test "fails" {
                try fail();
            }

            test "panics" {
                panic("boom");
            }

            """);
        var (exit, stdout, _) = Run("test", dir);
        Assert.Equal(1, exit);
        Assert.Equal("""
            running 4 tests
              ok     passes  (T)
              PANIC  asserts  (T)
                     before
                     panic: assertion failed: 1 + 1 == 3
                       at main.test.byn:16
              FAIL   fails  (T)
                     error: It went wrong.
              PANIC  panics  (T)
                     panic: boom
                       at main.test.byn:24

            4 tests: 1 passed, 1 failed, 2 panicked  (T)

            """, WithoutTimes(stdout).ReplaceLineEndings("\n"));
    }

    [Fact]
    public void TestExitsZeroWhenEveryTestPasses()
    {
        var dir = TestSuite("cli-test-pass", "test \"one\" {\n    assert(true);\n}\n");
        var (exit, stdout, _) = Run("test", dir);
        Assert.Equal(0, exit);
        Assert.Contains("1 test: 1 passed", stdout);
        Assert.DoesNotContain("main", stdout.Replace("running", ""));
    }

    private static string BuildAndRun(string example)
    {
        Environment.SetEnvironmentVariable("BAYAN_CACHE", Path.Combine(Golden.RepoRoot, "tmp", "cache"));
        var output = Path.Combine(Golden.RepoRoot, "tmp", "cli-example-" + example + (OperatingSystem.IsWindows() ? ".exe" : ""));
        var (exit, _, stderr) = Run("build", "-o", output, Example(example));
        Assert.True(exit == CliApp.ExitOk, stderr);
        var psi = new System.Diagnostics.ProcessStartInfo(output) { RedirectStandardOutput = true, UseShellExecute = false };
        using var p = System.Diagnostics.Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        Assert.Equal(0, p.ExitCode);
        return stdout.ReplaceLineEndings("\n");
    }

    [Fact]
    public void HelloPrintsHelloWorld() => Assert.Equal("Hello, world!\n", BuildAndRun("hello"));

    [Fact]
    public void TallyLitePrintsTheSettlement() => Assert.Equal(
        "dina pays alice 91.82 MYR\nchen pays alice 59.12 MYR\nbob pays alice 1.28 MYR\n",
        BuildAndRun("tally-lite"));

    [Fact]
    public void TallyLiteTestsPass()
    {
        Environment.SetEnvironmentVariable("BAYAN_CACHE", Path.Combine(Golden.RepoRoot, "tmp", "cache"));
        var (exit, stdout, stderr) = Run("test", Path.Combine(Golden.RepoRoot, "examples", "tally-lite"));
        Assert.True(exit == CliApp.ExitOk, stdout + stderr);
        Assert.Contains("8 tests: 8 passed", stdout);
    }
}
