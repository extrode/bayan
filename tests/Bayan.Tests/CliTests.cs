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
        Assert.Matches($"^bayan {System.Text.RegularExpressions.Regex.Escape(CliApp.Version)} \\(commit ([0-9a-f]{{12}}|unknown)\\)\\r?\\n$", stdout);
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
        Assert.Equal(CliApp.ExitUsage, exit);
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
        Assert.Equal(CliApp.ExitUsage, exit);
        Assert.Contains("not found", stderr);
    }

    [Fact]
    public void TypesPrintsEachBindingsType()
    {
        var dir = Path.Combine(Golden.RepoRoot, "tmp", "cli-types");
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, "main.byn");
        File.WriteAllText(file, "var n = 0;\nn += 1;\nprint($\"{n}\");\n");
        var (exit, stdout, stderr) = Run("types", file);
        Assert.True(exit == CliApp.ExitOk, stderr);
        Assert.Contains("\"path\": \"main.byn\"", stdout);
        Assert.Contains("\"name\": \"n\"", stdout);
        Assert.Contains("\"type\": \"i32\"", stdout);
    }

    [Fact]
    public void TypesReportsErrors()
    {
        var (exit, stdout, stderr) = Run("types", Path.Combine(Golden.CasesRoot, "diag", "valid-calls.byn"));
        Assert.Equal(CliApp.ExitFailure, exit);
        Assert.Empty(stdout);
        Assert.Contains("error BYN", stderr);
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

    private static string Program(string name, string source)
    {
        Environment.SetEnvironmentVariable("BAYAN_CACHE", Path.Combine(Golden.RepoRoot, "tmp", "cache"));
        var dir = Path.Combine(Golden.RepoRoot, "tmp", name);
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "main.byn");
        File.WriteAllText(path, source);
        return path;
    }

    [Fact]
    public void RunOfAProgramThatFailsToCompileExitsOne()
    {
        var (exit, _, stderr) = Run("run", Program("cli-run-error", "i32 x = \"no\";\n"));
        Assert.Equal(CliApp.ExitFailure, exit);
        Assert.Contains("error BYN", stderr);
    }

    [Fact]
    public void RunPassesExitZeroThrough()
    {
        var (exit, _, stderr) = Run("run", Program("cli-run-ok", "void main() {\n    print(\"fine\");\n}\n"));
        Assert.True(exit == CliApp.ExitOk, stderr);
    }

    [Fact]
    public void RunPassesTheProgramsOwnExitCodeThrough()
    {
        var (exit, _, _) = Run("run", Program("cli-run-panic", "panic(\"stop\");\n"));
        Assert.Equal(134, exit);
    }

    [Fact]
    public void RunPassesArgumentsAfterTheSeparator()
    {
        // The child inherits stdout, so it prints and also writes what it printed to a file.
        var written = Path.Combine(Golden.RepoRoot, "tmp", "cli-run-args.txt");
        if (File.Exists(written)) File.Delete(written);
        var path = Program("cli-run-args", $$"""
            void main(ReadOnlySpan<string> args) fails IoError {
                let bytes = stackalloc u8[64];
                var sb = StrBuf(bytes);
                for (let a in args) {
                    print(a);
                    sb.append($"{a}\n");
                }
                try Io.writeFile("{{written.Replace('\\', '/')}}", sb.asString().bytes);
            }

            """);
        var (exit, _, stderr) = Run("run", path, "--", "a", "b c");
        Assert.True(exit == CliApp.ExitOk, stderr);
        Assert.Equal("a\nb c\n", File.ReadAllText(written));
    }

    [Fact]
    public void IoRunSpawnsTheBayanBinary()
    {
        var path = Program("cli-io-run-version", """
            i32 main(ReadOnlySpan<string> args) fails IoError {
                string[1] version = ["--version"];
                let r = try Io.run(args[0], version.asSpan());
                return r.exitCode == 0 && r.stdout.startsWith("bayan ") && r.stderr.isEmpty ? 0 : 1;
            }

            """);
        var (exit, _, stderr) = Run("run", path, "--", LexDiffTests.Bayan);
        Assert.True(exit == CliApp.ExitOk, stderr);
    }

    [Fact]
    public void RunCannotTellAFailingMainFromACompileError()
    {
        var path = Program("cli-run-main-fails", """
            error enum E {
                /// It went wrong.
                wrong,
            }

            void main() fails E {
                return .wrong;
            }

            """);
        var (exit, _, stderr) = Run("run", "--diagnostics", "json", path);
        Assert.Equal(CliApp.ExitFailure, exit);
        Assert.DoesNotContain("{\"code\"", stderr);
    }

    [Fact]
    public void DiagnosticsJsonPrintsOneObjectPerLine()
    {
        var path = Program("cli-json", "i32 x = \"no\";\nvar y = 1;\n");
        var (exit, stdout, stderr) = Run("check", "--diagnostics", "json", path);
        Assert.Equal(CliApp.ExitFailure, exit);
        Assert.Empty(stdout);
        var lines = stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.NotEmpty(lines);
        foreach (var line in lines)
        {
            using var doc = System.Text.Json.JsonDocument.Parse(line);
            Assert.StartsWith("BYN", doc.RootElement.GetProperty("code").GetString());
            Assert.Equal(path, doc.RootElement.GetProperty("file").GetString());
        }
    }

    [Theory]
    [InlineData("build")]
    [InlineData("run")]
    [InlineData("test")]
    public void DiagnosticsJsonIsAcceptedByEveryBuildingCommand(string command)
    {
        var path = Program("cli-json-" + command, "i32 x = \"no\";\n");
        var (exit, _, stderr) = Run(command, "--diagnostics", "json", path);
        Assert.Equal(CliApp.ExitFailure, exit);
        Assert.StartsWith("{\"code\":\"BYN", stderr);
    }

    [Fact]
    public void UnknownDiagnosticsFormatIsUsageError()
    {
        var (exit, _, stderr) = Run("check", "--diagnostics", "xml", Example("hello"));
        Assert.Equal(CliApp.ExitUsage, exit);
        Assert.Contains("unknown diagnostics format `xml`", stderr);
    }

    [Fact]
    public void HelpListsTheExitCodes()
    {
        var (_, stdout, _) = Run("--help");
        Assert.Contains("3  test failures", stdout);
        Assert.Contains("4  toolchain problems", stdout);
        Assert.Contains("cannot tell a build failure from a program that exits 1", stdout);
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
        Assert.Equal(CliApp.ExitTestFailure, exit);
        Assert.Equal("""
            running 4 tests
              ok     passes  (T)
              PANIC  asserts  (T)
                     before
                     panic: assertion failed: 1 + 1 == 3
                       1 + 1 is 2
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

    [Fact]
    public void TodoReportsPending()
    {
        var dir = TestSuite("cli-test-todo", "test \"one\" {\n    assert(true);\n}\n\ntest \"later\" {\n    todo(\"x\");\n}\n");
        var (exit, stdout, _) = Run("test", "--json", dir);
        Assert.Equal(CliApp.ExitOk, exit);
        Assert.Contains("\"status\": \"pending\"", stdout);
        Assert.Contains("\"output\": \"pending: x\\n  at main.test.byn:6\"", stdout);
        Assert.Contains("\"pending\": 1", stdout);
        Assert.Contains("\"passed\": 1", stdout);
        Assert.Contains("\"failed\": 0", stdout);
        var (_, human, _) = Run("test", dir);
        Assert.Contains("PEND ", human);
        Assert.Contains("2 tests: 1 passed, 1 pending", human);
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
    public void TallyLiteChecksWithoutWarnings()
    {
        var (exit, stdout, stderr) = Run("check", Example("tally-lite"));
        Assert.Equal(CliApp.ExitOk, exit);
        Assert.Equal("", stderr);
        Assert.StartsWith("checked tally-lite in ", stdout[(stdout.IndexOf(' ') + 1)..]);
    }

    [Fact]
    public void TallyLiteTestsPass()
    {
        Environment.SetEnvironmentVariable("BAYAN_CACHE", Path.Combine(Golden.RepoRoot, "tmp", "cache"));
        var (exit, stdout, stderr) = Run("test", Path.Combine(Golden.RepoRoot, "examples", "tally-lite"));
        Assert.True(exit == CliApp.ExitOk, stdout + stderr);
        Assert.Contains("8 tests: 8 passed", stdout);
    }
}

/// <summary>Changes process-wide environment variables, so it runs alone.</summary>
[CollectionDefinition(nameof(ToolchainEnvironment), DisableParallelization = true)]
public sealed class ToolchainEnvironment;

[Collection(nameof(ToolchainEnvironment))]
public sealed class CliToolchainTests
{
    private static (int Exit, string Err) RunWith(string variable, string value, params string[] args)
    {
        var saved = Environment.GetEnvironmentVariable(variable);
        Environment.SetEnvironmentVariable(variable, value);
        try
        {
            var stderr = new StringWriter();
            var exit = CliApp.Run(args, new StringWriter(), stderr);
            return (exit, stderr.ToString());
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, saved);
        }
    }

    private static string Hello => Path.Combine(Golden.RepoRoot, "examples", "hello", "main.byn");

    [Theory]
    [InlineData("build")]
    [InlineData("run")]
    [InlineData("test")]
    public void AMissingCompilerIsAToolchainProblem(string command)
    {
        var (exit, stderr) = RunWith("BAYAN_CC", Path.Combine(Golden.RepoRoot, "tmp", "no-such-cc"), command, Hello);
        Assert.Equal(CliApp.ExitToolchain, exit);
        Assert.Contains("cannot run the C compiler", stderr);
    }

    [Fact]
    public void AnUnwritableCacheIsAToolchainProblem()
    {
        var blocker = Path.Combine(Golden.RepoRoot, "tmp", "cache-is-a-file");
        Directory.CreateDirectory(Path.GetDirectoryName(blocker)!);
        File.WriteAllText(blocker, "");
        var (exit, stderr) = RunWith("BAYAN_CACHE", blocker, "run", Hello);
        Assert.Equal(CliApp.ExitToolchain, exit);
        Assert.Contains("cannot write the build output or cache", stderr);
    }
}
