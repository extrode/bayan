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
        Assert.DoesNotContain("the formatter is", stderr);
    }

    [Fact]
    public void FmtIsAnUnknownCommandNamingFormat()
    {
        var (exit, _, stderr) = Run("fmt");
        Assert.Equal(CliApp.ExitUsage, exit);
        Assert.Contains("unknown command `fmt`", stderr);
        Assert.Contains("  format <path>", stderr);
        Assert.Contains("unknown command `fmt`\nthe formatter is `bayan format`\nusage:", stderr.ReplaceLineEndings("\n"));
    }

    [Fact]
    public void FormatCheckOnAFileThatDoesNotParseReportsItsDiagnosticsAndExits1()
    {
        var dir = Fresh("format-check-broken", "let a = true\nunless !a { print(\"y\") }\n");
        File.WriteAllText(Path.Combine(dir, "z.byn"), "let b = (\nlet c = (\n");
        var (exit, stdout, stderr) = Run("format", "--check", dir);
        Assert.Equal(CliApp.ExitFailure, exit);
        Assert.Contains($"would format {Path.Combine(dir, "main.byn")}", stdout);
        Assert.Contains("z.byn:2:1", stderr);
        Assert.Contains("z.byn:3:1", stderr);
    }

    private static string Fresh(string name, string source)
    {
        var dir = Path.Combine(Golden.RepoRoot, "tmp", "cli", name);
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "main.byn"), source);
        return dir;
    }

    [Fact]
    public void PackageResolvesImportsUnderItsRootAndNamesLinesFromIt()
    {
        var root = Path.Combine(Golden.RepoRoot, "tmp", "cli", "package");
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        Directory.CreateDirectory(Path.Combine(root, "util"));
        Directory.CreateDirectory(Path.Combine(root, "app"));
        File.WriteAllText(Path.Combine(root, "util", "util.byn"), "public i32 twice(i32 x) => x * 2\n");
        File.WriteAllText(Path.Combine(root, "app", "main.byn"), "import util\n\nvoid main() {\n    print($\"{util.twice(21)}\")\n}\n");
        var app = Path.Combine(root, "app");
        var (exit, stdout, stderr) = Run("build", "--emit-c", "--mode", "debug", "--package", root, app);
        Assert.True(exit == CliApp.ExitOk, stderr);
        Assert.Contains("#line 4 \"app/main.byn\"", stdout);
        Assert.Contains("#line 1 \"util/util.byn\"", stdout);
        Assert.NotEqual(CliApp.ExitOk, Run("build", "--emit-c", app).Exit);
        var (usage, _, err) = Run("build", app, "--package");
        Assert.Equal(CliApp.ExitUsage, usage);
        Assert.Contains("unexpected argument `--package`", err);
    }

    [Fact]
    public void AuditListsEveryBypassWithItsReason()
    {
        var dir = Fresh("audit", "@allow(BYN2230) because \"kept for the C ABI\"\nvoid toHex() {\n}\n\nunsafe void poke() {\n}\n\n"
            + "void main() {\n    let k = input.trustUnchecked() because \"a fixture\"\n    unsafe because \"raw write\" {\n    }\n}\n\nextern i32 puts(cstr s)\n");
        var (exit, stdout, stderr) = Run("audit", dir);
        Assert.True(exit == CliApp.ExitOk, stderr);
        var main = Path.Combine(dir, "main.byn");
        Assert.Equal(
            $"{main}:1:1  @allow(BYN2230)  because \"kept for the C ABI\"\n{main}:5:1  unsafe void poke()  no reason given\n"
            + $"{main}:9:19  trustUnchecked()  because \"a fixture\"\n{main}:10:5  unsafe  because \"raw write\"\n"
            + $"{main}:14:1  extern i32 puts(cstr s)  no reason given\n◆ 5 bypasses in {dir}\n",
            stdout.ReplaceLineEndings("\n"));
    }

    [Fact]
    public void AuditReportsAnUnreadableFileAndAuditsTheRest()
    {
        if (OperatingSystem.IsWindows()) return;
        var dir = Fresh("audit-file", "unsafe void poke() {\n}\n");
        var locked = Path.Combine(dir, "a.byn");
        File.WriteAllText(locked, "unsafe void peek() {\n}\n");
        File.SetUnixFileMode(locked, UnixFileMode.None);
        try
        {
            if (CanRead(() => File.ReadAllBytes(locked))) return;
            var (exit, stdout, stderr) = Run("audit", dir);
            Assert.Equal(CliApp.ExitFailure, exit);
            Assert.Contains("a.byn", stderr);
            Assert.Contains("unsafe void poke()", stdout);
        }
        finally { File.SetUnixFileMode(locked, UnixFileMode.UserRead | UnixFileMode.UserWrite); }
    }

    [Fact]
    public void AuditReportsAnUnreadableDirectoryAndAuditsTheRest()
    {
        if (OperatingSystem.IsWindows()) return;
        var dir = Fresh("audit-dir", "unsafe void poke() {\n}\n");
        var locked = Directory.CreateDirectory(Path.Combine(dir, "locked")).FullName;
        File.WriteAllText(Path.Combine(locked, "b.byn"), "unsafe void peek() {\n}\n");
        File.SetUnixFileMode(locked, UnixFileMode.None);
        try
        {
            if (CanRead(() => Directory.GetFiles(locked))) return;
            var (exit, stdout, stderr) = Run("audit", dir);
            Assert.Equal(CliApp.ExitFailure, exit);
            Assert.Contains("locked", stderr);
            Assert.Contains("unsafe void poke()", stdout);
        }
        finally { File.SetUnixFileMode(locked, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute); }
    }

    [Fact]
    public async Task AuditWalksSymlinkCyclesOnce()
    {
        if (OperatingSystem.IsWindows()) return;
        var dir = Fresh("audit-cycle", "unsafe void poke() {\n}\n");
        var sub = Directory.CreateDirectory(Path.Combine(dir, "sub")).FullName;
        Directory.CreateSymbolicLink(Path.Combine(sub, "up"), "..");
        Directory.CreateSymbolicLink(Path.Combine(sub, "up2"), "..");
        var result = await Task.Factory.StartNew(() => Run("audit", dir), TaskCreationOptions.LongRunning).WaitAsync(TimeSpan.FromSeconds(120));
        Assert.Equal(1, result.Out.Split("unsafe void poke()").Length - 1);
    }

    [Fact]
    public async Task AuditWalksASymlinkToAParentOfTheRootOnce()
    {
        if (OperatingSystem.IsWindows()) return;
        var dir = Path.Combine(Fresh("audit-parent-link", "unsafe void poke() {\n}\n"), "outer", "dir");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "main.byn"), "unsafe void peek() {\n}\n");
        File.WriteAllText(Path.Combine(dir, "..", "outside.byn"), "unsafe void prod() {\n}\n");
        Directory.CreateSymbolicLink(Path.Combine(dir, "link"), "..");
        var result = await Task.Factory.StartNew(() => Run("audit", dir), TaskCreationOptions.LongRunning).WaitAsync(TimeSpan.FromSeconds(120));
        Assert.Equal(1, result.Out.Split("unsafe void peek()").Length - 1);
        Assert.Equal(1, result.Out.Split("unsafe void prod()").Length - 1);
    }

    [Fact]
    public async Task AuditReportsALoopingFileLinkAndGoesOn()
    {
        if (OperatingSystem.IsWindows()) return;
        var dir = Fresh("audit-loop-link", "void main() {\n}\n");
        File.CreateSymbolicLink(Path.Combine(dir, "loop.byn"), "loop.byn");
        var sub = Directory.CreateDirectory(Path.Combine(dir, "sub")).FullName;
        File.WriteAllText(Path.Combine(sub, "b.byn"), "unsafe void peek() {\n}\n");
        var result = await Task.Factory.StartNew(() => Run("audit", dir), TaskCreationOptions.LongRunning).WaitAsync(TimeSpan.FromSeconds(120));
        Assert.Equal(CliApp.ExitFailure, result.Exit);
        Assert.Contains("unsafe void peek()", result.Out);
        Assert.Contains(Path.Combine(dir, "loop.byn"), result.Err);
    }

    /// <summary>Whether <paramref name="read"/> succeeds despite a mode of none, as it does for root.</summary>
    private static bool CanRead(Action read)
    {
        try
        {
            read();
            return true;
        }
        catch (UnauthorizedAccessException) { return false; }
    }

    [Fact]
    public void AuditNeedsOnePath()
    {
        var (exit, _, stderr) = Run("audit");
        Assert.Equal(CliApp.ExitUsage, exit);
        Assert.Contains("usage: bayan audit <path>", stderr);
    }

    [Fact]
    public void FeaturesUsedListsNodesBuiltinsAndDiagnostics()
    {
        var dir = Fresh("features", "void main() {\n    var s = \"abc\"\n    if s.length > 1 {\n        print(s)\n    }\n}\n");
        var (exit, stdout, stderr) = Run("features", "--used", dir);
        Assert.True(exit == CliApp.ExitOk, stderr);
        using var json = System.Text.Json.JsonDocument.Parse(stdout);
        string[] Items(string name) => json.RootElement.GetProperty(name).EnumerateArray().Select(e => e.GetString()!).ToArray();
        Assert.Equal(1, json.RootElement.GetProperty("version").GetInt32());
        Assert.Contains("IfStmt", Items("nodes"));
        Assert.Contains("FunctionDecl", Items("nodes"));
        Assert.Equal(["Print", "StringLength"], Items("builtins"));
        Assert.Equal(["BYN2311"], Items("diagnostics"));
    }

    [Fact]
    public void FeaturesNeedsUsed()
    {
        var (exit, _, stderr) = Run("features", ".");
        Assert.Equal(CliApp.ExitUsage, exit);
        Assert.Contains("usage: bayan features --used <path>", stderr);
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
        File.WriteAllText(file, "var n = 0\nn += 1\nprint($\"{n}\")\n");
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
        var (exit, _, stderr) = Run("run", Program("cli-run-error", "i32 x = \"no\"\n"));
        Assert.Equal(CliApp.ExitFailure, exit);
        Assert.Contains("error BYN", stderr);
    }

    private static string ManyErrors(string name) =>
        Program(name, string.Concat(Enumerable.Range(0, 25).Select(i => $"i32 x{i} = \"no\"\n")));

    private static int Count(string text, string needle) => text.Split(needle).Length - 1;

    [Fact]
    public void ErrorsPastTwentyAreCappedWithAHint()
    {
        var (_, _, stderr) = Run("check", ManyErrors("cli-cap"));
        Assert.Equal(20, Count(stderr, "error BYN"));
        Assert.Contains("\u2026 and 5 more errors (use --all to show them)", stderr);
    }

    [Fact]
    public void AllShowsEveryError()
    {
        var (_, _, stderr) = Run("check", "--all", ManyErrors("cli-cap-all"));
        Assert.Equal(25, Count(stderr, "error BYN"));
        Assert.DoesNotContain("more errors", stderr);
    }

    [Fact]
    public void JsonIsNeverCapped()
    {
        var (_, _, stderr) = Run("check", "--diagnostics", "json", ManyErrors("cli-cap-json"));
        Assert.Equal(25, Count(stderr, "\"severity\":\"error\""));
        Assert.DoesNotContain("more errors", stderr);
    }

    [Fact]
    public void AsciiCapLineUsesThreeDots()
    {
        var (_, _, stderr) = Run("--ascii", "check", ManyErrors("cli-cap-ascii"));
        Assert.Contains("... and 5 more errors (use --all to show them)", stderr);
        Assert.DoesNotContain("\u2026", stderr);
    }

    [Fact]
    public void RunPassesExitZeroThrough()
    {
        var (exit, _, stderr) = Run("run", Program("cli-run-ok", "void main() {\n    print(\"fine\")\n}\n"));
        Assert.True(exit == CliApp.ExitOk, stderr);
    }

    [Fact]
    public void RunPassesTheProgramsOwnExitCodeThrough()
    {
        var (exit, _, _) = Run("run", Program("cli-run-panic", "panic(\"stop\")\n"));
        Assert.Equal(PanicExit.Code, exit);
    }

    [Fact]
    public void RunPassesArgumentsAfterTheSeparator()
    {
        // The child inherits stdout, so it prints and also writes what it printed to a file.
        var written = Path.Combine(Golden.RepoRoot, "tmp", "cli-run-args.txt");
        if (File.Exists(written)) File.Delete(written);
        var path = Program("cli-run-args", $$"""
            void main(ReadOnlySpan<string> args) fails IoError {
                let bytes = stackalloc u8[64]
                var sb = StrBuf(bytes)
                for a in args {
                    print(a)
                    sb.append($"{a}\n")
                }
                try Io.writeFile("{{written.Replace('\\', '/')}}", sb.finish().bytes)
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
                let version: string[1] = ["--version"]
                let r = try Io.run(args[0], version.span)
                return r.exitCode == 0 && r.stdout.startsWith("bayan ") && r.stderr.isEmpty ? 0 : 1
            }

            """);
        var (exit, _, stderr) = Run("run", path, "--", LexDiffTests.Bayan);
        Assert.True(exit == CliApp.ExitOk, stderr);
    }

    [Fact]
    public void RunCannotTellAFailingMainFromACompileError()
    {
        var path = Program("cli-run-main-fails", """
            union error E {
                /// It went wrong.
                wrong,
            }

            void main() fails E {
                fail .wrong
            }

            """);
        var (exit, _, stderr) = Run("run", "--diagnostics", "json", path);
        Assert.Equal(CliApp.ExitFailure, exit);
        Assert.DoesNotContain("{\"code\"", stderr);
    }

    [Fact]
    public void DiagnosticsJsonPrintsOneObjectPerLine()
    {
        var path = Program("cli-json", "i32 x = \"no\"\nvar y = 1\n");
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
        var path = Program("cli-json-" + command, "i32 x = \"no\"\n");
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
        Assert.Contains("3  test failures, or a coverage or mutation score below its minimum", stdout);
        Assert.Contains("4  toolchain problems", stdout);
        Assert.Contains("cannot tell a build failure from a program that exits 1", stdout);
    }

    [Theory]
    [InlineData("run")]
    [InlineData("types")]
    [InlineData("check")]
    public void VerboseIsOnlyForBuildAndTest(string command)
    {
        var dir = TestSuite("cli-verbose-" + command, "");
        var main = Path.Combine(dir, "main.byn");
        var (exit, _, stderr) = Run(command, main, "-v");
        Assert.Equal(CliApp.ExitUsage, exit);
        Assert.Contains("unexpected argument `-v`", stderr);
        Assert.Equal(CliApp.ExitUsage, Run(command, main, "--verbose").Exit);
        Assert.Equal(CliApp.ExitOk, Run(command, main, "--all").Exit);
    }

    [Fact]
    public void ParseRendersItsDiagnosticsInTheChosenSymbols()
    {
        var dir = Path.Combine(Golden.RepoRoot, "tmp", "cli-parse-ascii");
        Directory.CreateDirectory(dir);
        var bad = Path.Combine(dir, "bad.byn");
        File.WriteAllText(bad, "void main() {\n    let x = \n}\n");
        var (exit, _, stderr) = Run("--ascii", "parse", bad);
        Assert.Equal(CliApp.ExitFailure, exit);
        Assert.Contains("  3 | }", stderr);
        Assert.DoesNotContain("│", stderr);
        Assert.Contains("  3 | }", Run("--ascii", "parse", "--ast", bad).Out);
    }

    private static string TestSuite(string name, string tests)
    {
        Environment.SetEnvironmentVariable("BAYAN_CACHE", Path.Combine(Golden.RepoRoot, "tmp", "cache"));
        var dir = Path.Combine(Golden.RepoRoot, "tmp", name);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "main.byn"), "void main() {\n    print(\"main\")\n}\n");
        File.WriteAllText(Path.Combine(dir, "main.test.byn"), tests);
        return dir;
    }

    private static string WithoutTimes(string text) =>
        System.Text.RegularExpressions.Regex.Replace(text.ReplaceLineEndings("\n"), @"\d+\.\d\ds$", "Ts", System.Text.RegularExpressions.RegexOptions.Multiline); // `$` stops before `\n`, so a `\r` (Windows) would hide the time

    [Fact]
    public void TestReportsPassFailAndPanicPerTest()
    {
        var dir = TestSuite("cli-test-mixed", """
            union error E {
                /// It went wrong.
                wrong,
            }

            void breaks() fails E {
                fail .wrong
            }

            test "passes" {
                assert(1 + 1 == 2)
            }

            test "asserts" {
                print("before")
                assert(1 + 1 == 3)
            }

            test "fails" {
                try breaks()
            }

            test "panics" {
                panic("boom")
            }

            """);
        var (exit, stdout, _) = Run("test", "--ascii", dir);
        Assert.Equal(CliApp.ExitTestFailure, exit);
        Assert.Equal("""
            running 4 tests in 1 module

              x cli-test-mixed: asserts
                  before
                  assert(1 + 1 == 3)
                           |   |
                           |   false
                           2
                  main.test.byn:16

              x cli-test-mixed: fails
                  error: It went wrong.

              x cli-test-mixed: panics
                  boom
                  main.test.byn:24

            * 1 passed - x 3 failed - 0 pending - Ts
              rerun the failures: bayan test --only "asserts" --only "fails" --only "panics"

            """, WithoutTimes(stdout));
    }

    [Fact]
    public void TestExitsZeroWhenEveryTestPasses()
    {
        var dir = TestSuite("cli-test-pass", "test \"one\" {\n    assert(true)\n}\n");
        var (exit, stdout, _) = Run("test", "--ascii", dir);
        Assert.Equal(0, exit);
        Assert.Equal("running 1 test in 1 module\n\n* 1 passed - Ts\n", WithoutTimes(stdout));
        Assert.DoesNotContain("·", stdout, StringComparison.Ordinal);
    }

    [Fact]
    public void TodoReportsPending()
    {
        var dir = TestSuite("cli-test-todo", "test \"one\" {\n    assert(true)\n}\n\ntest \"later\" {\n    todo(\"x\")\n}\n");
        var (exit, json, _) = Run("test", "--json", dir);
        var stdout = OperatingSystem.IsWindows() ? json.Replace("\\r\\n", "\\n") : json; // Windows text-mode stderr ends lines in CRLF (Q742), JSON-escaped here
        Assert.Equal(CliApp.ExitOk, exit);
        Assert.Contains("\"status\": \"pending\"", stdout);
        Assert.Contains("\"output\": \"pending: x\\n  at main.test.byn:6\"", stdout);
        Assert.Contains("\"pending\": 1", stdout);
        Assert.Contains("\"passed\": 1", stdout);
        Assert.Contains("\"failed\": 0", stdout);
        var (humanExit, human, _) = Run("test", "--ascii", dir);
        Assert.Equal(CliApp.ExitOk, humanExit);
        Assert.Equal("running 2 tests in 1 module\n\n* 1 passed - 1 pending - Ts\n", WithoutTimes(human));
    }

    [Fact]
    public void TestVerboseListsPassingAndPendingTests()
    {
        var dir = TestSuite("cli-test-verbose", "test \"one\" {\n    assert(true)\n}\n\ntest \"later\" {\n    todo(\"x\")\n}\n");
        var (exit, stdout, _) = Run("test", "--ascii", "-v", dir);
        Assert.Equal(CliApp.ExitOk, exit);
        Assert.Equal("""
            running 2 tests in 1 module
              * cli-test-verbose: one
              * cli-test-verbose: later (pending)

            * 1 passed - 1 pending - Ts

            """, WithoutTimes(stdout));
        Assert.Equal(WithoutTimes(stdout), WithoutTimes(Run("test", "--ascii", "--verbose", dir).Out));
    }

    [Fact]
    public void TestShowsAssertionValuesAndOneRerunLine()
    {
        var dir = TestSuite("cli-test-values", "i64 cents(i64 units) {\n    return units * 100\n}\n\ntest \"leftover cents\" {\n    assert(cents(2) == 201)\n}\n");
        var (exit, stdout, _) = Run("test", "--ascii", dir);
        Assert.Equal(CliApp.ExitTestFailure, exit);
        Assert.Equal("""
            running 1 test in 1 module

              x cli-test-values: leftover cents
                  assert(cents(2) == 201)
                         |        |
                         |        false
                         200
                  main.test.byn:6

            * 0 passed - x 1 failed - 0 pending - Ts
              rerun the failure: bayan test --only "leftover cents"

            """, WithoutTimes(stdout));
    }

    [Fact]
    public void TheRerunHintQuotesTheNameForAShell()
    {
        const string name = "x $(touch owned) `id` it's \"hi\" \\ bye";
        var dir = TestSuite("cli-test-quoted", "test \"x $(touch owned) `id` it's \\\"hi\\\" \\\\ bye\" {\n    assert(false)\n}\n");
        var (exit, stdout, _) = Run("test", "--ascii", dir);
        Assert.Equal(CliApp.ExitTestFailure, exit);
        const string quoted = "'x $(touch owned) `id` it'\\''s \"hi\" \\ bye'";
        Assert.Contains("  rerun the failure: bayan test --only " + quoted + "\n", stdout.ReplaceLineEndings("\n"));
        if (OperatingSystem.IsWindows()) return;
        var psi = new System.Diagnostics.ProcessStartInfo("/bin/sh") { RedirectStandardOutput = true, StandardOutputEncoding = System.Text.Encoding.UTF8, UseShellExecute = false, WorkingDirectory = dir };
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add("printf '%s' " + quoted);
        using var sh = System.Diagnostics.Process.Start(psi)!;
        var echoed = sh.StandardOutput.ReadToEndLf();
        sh.WaitForExit();
        Assert.Equal(name, echoed);
        Assert.False(File.Exists(Path.Combine(dir, "owned")));
    }

    [Fact]
    public void ControlCharactersInTestNamesAndOutputAreShownEscaped()
    {
        var dir = TestSuite("cli-test-control", """
            test "ok\u{1b}]0;PWNED\u{7}" {
                assert(true)
            }

            test "later\u{1b}[2J" {
                todo("soon")
            }

            test "y\u{1b}]0;PWNED\u{7}\u{1b}[2J" {
                panic("boom\u{1b}[2J\u{9b}31m")
            }

            """);
        var (exit, stdout, _) = Run("test", "--ascii", "-v", dir);
        Assert.Equal(CliApp.ExitTestFailure, exit);
        Assert.DoesNotContain('\u001b', stdout);
        Assert.DoesNotContain('\u0007', stdout);
        Assert.DoesNotContain('\u009b', stdout);
        Assert.Equal("""
            running 3 tests in 1 module
              * cli-test-control: ok\u{1b}]0;PWNED\u{7}
              * cli-test-control: later\u{1b}[2J (pending)

              x cli-test-control: y\u{1b}]0;PWNED\u{7}\u{1b}[2J
                  boom\u{1b}[2J\u{9b}31m
                  main.test.byn:10

            * 1 passed - x 1 failed - 1 pending - Ts
              a failing test's name contains control characters and cannot be selected from a shell

            """, WithoutTimes(stdout));
    }

    [Fact]
    public void BidiControlsInTestNamesAndOutputAreShownEscaped()
    {
        var dir = TestSuite("cli-test-bidi", "test \"a\\u{202e}b\" {\n    panic(\"x\\u{2067}y\")\n}\n\ntest \"plain\" {\n    assert(false)\n}\n");
        var (exit, stdout, _) = Run("test", "--ascii", dir);
        Assert.Equal(CliApp.ExitTestFailure, exit);
        Assert.DoesNotContain('\u202e', stdout);
        Assert.DoesNotContain('\u2067', stdout);
        Assert.Contains("a\\u{202e}b", stdout);
        Assert.Contains("x\\u{2067}y", stdout);
        Assert.Contains("bayan test --only \"plain\"", stdout);
        Assert.DoesNotContain("--only a", stdout);
        Assert.Contains("cannot be selected from a shell", stdout);
    }

    [Fact]
    public void TestsLeftOutByOnlyAreNotSkipped()
    {
        var dir = TestSuite("cli-test-only-json", "test \"one\" {\n    assert(true)\n}\n\ntest \"two\" {\n    assert(true)\n}\n");
        var (exit, stdout, _) = Run("test", "--json", "--only", "one", dir);
        Assert.Equal(CliApp.ExitOk, exit);
        Assert.Contains("\"passed\": 1", stdout);
        Assert.Contains("\"skipped\": 0", stdout);
    }

    [Fact]
    public void MoreThanThreeFailuresGetNoRerunLine()
    {
        var dir = TestSuite("cli-test-many", string.Concat(Enumerable.Range(1, 4).Select(n => $"test \"t{n}\" {{\n    assert(false)\n}}\n\n")));
        var (exit, stdout, _) = Run("test", "--ascii", dir);
        Assert.Equal(CliApp.ExitTestFailure, exit);
        Assert.Contains("* 0 passed - x 4 failed - 0 pending - ", stdout);
        Assert.DoesNotContain("rerun", stdout);
    }

    private static string BuildAndRun(string example)
    {
        Environment.SetEnvironmentVariable("BAYAN_CACHE", Path.Combine(Golden.RepoRoot, "tmp", "cache"));
        var output = Path.Combine(Golden.RepoRoot, "tmp", "cli-example-" + example + (OperatingSystem.IsWindows() ? ".exe" : ""));
        var (exit, _, stderr) = Run("build", "-o", output, Example(example));
        Assert.True(exit == CliApp.ExitOk, stderr);
        var psi = new System.Diagnostics.ProcessStartInfo(output) { RedirectStandardOutput = true, StandardOutputEncoding = System.Text.Encoding.UTF8, UseShellExecute = false };
        using var p = System.Diagnostics.Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEndLf();
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
        Assert.Contains("8 passed · ", stdout);
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

    [Theory]
    [InlineData("build")]
    [InlineData("run")]
    [InlineData("test")]
    public void GeneratedCTheCCompilerRejectsIsAnInternalCompilerError(string command)
    {
        if (OperatingSystem.IsWindows()) return;
        var fake = Path.Combine(Golden.RepoRoot, "tmp", "rejecting-cc.sh");
        Directory.CreateDirectory(Path.GetDirectoryName(fake)!);
        File.WriteAllText(fake, "echo \"main.byn:2:5: error: injected\" >&2\nexit 1\n");
        var (exit, stderr) = RunWith("BAYAN_CC", "/bin/sh " + fake, command, Hello);
        Assert.Equal(CliApp.ExitToolchain, exit);
        Assert.Contains("error BYN4001: internal compiler error", stderr);
        Assert.Contains("main.byn:2:5: error: injected\n", stderr);
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
