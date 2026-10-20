using System.Text.RegularExpressions;
using Bayan.Backend.C;
using Bayan.Cli;

namespace Bayan.Tests;

/// <summary>Color, <c>bayan doctor</c> and <c>test --changed</c>; these change environment variables, so they run alone.</summary>
[Collection(nameof(ToolchainEnvironment))]
public sealed partial class CliEnvironmentTests
{
    private static (int Exit, string Out, string Err) Run(string[] args, CliApp.Terminal terminal = default)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var exit = CliApp.Run(args, stdout, stderr, terminal);
        return (exit, stdout.ToString().ReplaceLineEndings("\n"), stderr.ToString().ReplaceLineEndings("\n"));
    }

    private static void With(string name, string? value, Action body)
    {
        var saved = Environment.GetEnvironmentVariable(name);
        Environment.SetEnvironmentVariable(name, value);
        try
        {
            body();
        }
        finally
        {
            Environment.SetEnvironmentVariable(name, saved);
        }
    }

    private static string Fresh(string name)
    {
        var dir = Path.Combine(Golden.RepoRoot, "tmp", "cli-env", name);
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string Broken()
    {
        var dir = Fresh("broken");
        var file = Path.Combine(dir, "main.byn");
        File.WriteAllText(file, "void main() {\n    let x = 1;\n    x = 2;\n}\n");
        return file;
    }

    [GeneratedRegex("\u001b\\[[0-9;]*m")]
    private static partial Regex Escape();

    // ── Color ────────────────────────────────────────────────────────────────────

    [Fact]
    public void ColorFollowsTheTerminalNoColorAndTheFlag()
    {
        var file = Broken();
        With("NO_COLOR", null, () =>
        {
            var plain = Run(["check", file]);
            Assert.Equal(CliApp.ExitFailure, plain.Exit);
            Assert.DoesNotContain("\u001b[", plain.Err);
            var terminal = Run(["check", file], new CliApp.Terminal(true, true));
            Assert.Contains("\u001b[1m\u001b[31merror BYN", terminal.Err);
            Assert.Equal(plain.Err, Escape().Replace(terminal.Err, ""));
            Assert.Equal(terminal.Err, Run(["check", "--color", "always", file]).Err);
            Assert.Equal(terminal.Err, Run(["--color=always", "check", file]).Err);
            Assert.Equal(plain.Err, Run(["check", file, "--color", "never"], new CliApp.Terminal(true, true)).Err);
            Assert.Equal(plain.Err, Run(["check", file], new CliApp.Terminal(true, false)).Err);
        });
        With("NO_COLOR", "1", () =>
        {
            Assert.DoesNotContain("\u001b[", Run(["check", file], new CliApp.Terminal(true, true)).Err);
            Assert.Contains("\u001b[", Run(["check", file, "--color", "always"], new CliApp.Terminal(true, true)).Err);
        });
    }

    [Fact]
    public void AnUnknownColorSettingIsAUsageError()
    {
        var (exit, _, err) = Run(["check", Broken(), "--color", "sometimes"]);
        Assert.Equal(CliApp.ExitUsage, exit);
        Assert.Contains("unknown color setting `sometimes`", err);
    }

    [Fact]
    public void TestResultsAreColoredOnATerminal()
    {
        var dir = Fresh("color-tests");
        File.WriteAllText(Path.Combine(dir, "main.byn"), "void main() {\n}\n");
        File.WriteAllText(Path.Combine(dir, "main.test.byn"), "test \"passes\" {\n    assert(true);\n}\n\ntest \"breaks\" {\n    assert(false);\n}\n");
        With("NO_COLOR", null, () =>
        {
            var (exit, stdout, _) = Run(["test", dir], new CliApp.Terminal(true, true));
            Assert.Equal(CliApp.ExitTestFailure, exit);
            Assert.Contains("  \u001b[32mok   \u001b[0m  passes", stdout);
            Assert.Contains("  \u001b[1m\u001b[31mPANIC\u001b[0m  breaks", stdout);
            Assert.DoesNotContain("\u001b[", Run(["test", dir]).Out);
        });
    }

    [Fact]
    public void AScriptKeepsItsColorArgument()
    {
        var dir = Fresh("script-args");
        var script = Path.Combine(dir, "tool.byn");
        File.WriteAllText(script, "#!/usr/bin/env bayan\n#:io threaded\n\nprint(\"hi\");\n");
        Assert.Equal(CliApp.ExitOk, Run([script, "--color", "sometimes"]).Exit);
    }

    // ── Doctor ───────────────────────────────────────────────────────────────────

    [Fact]
    public void DoctorReportsAHealthyToolchain()
    {
        var (exit, stdout, _) = Run(["doctor"]);
        Assert.True(exit == CliApp.ExitOk, stdout);
        Assert.StartsWith($"ok    bayan       {CliApp.Version} (commit ", stdout);
        Assert.Contains($"ok    host        {Targets.Host}\n", stdout);
        Assert.Matches("ok    C compiler  .+ \\(.+\\)\n", stdout);
        Assert.Contains($"ok    cache       {Toolchain.CacheDirectory} (writable, ", stdout);
        Assert.DoesNotContain("FAIL", stdout);
    }

    [Fact]
    public void DoctorExplainsAMissingCompiler()
    {
        With("BAYAN_CC", Path.Combine(Golden.RepoRoot, "tmp", "no-such-cc"), () =>
        {
            var (exit, stdout, _) = Run(["doctor"]);
            Assert.Equal(CliApp.ExitToolchain, exit);
            Assert.Contains("FAIL  C compiler  BAYAN_CC is `", stdout);
            Assert.Contains("fix: point BAYAN_CC at a C compiler, or unset it to use zig\n", stdout);
        });
    }

    [Fact]
    public void DoctorExplainsAnUnwritableCache()
    {
        var blocker = Path.Combine(Fresh("doctor-cache"), "file");
        File.WriteAllText(blocker, "");
        With("BAYAN_CACHE", blocker, () =>
        {
            var (exit, stdout, _) = Run(["doctor"]);
            Assert.Equal(CliApp.ExitToolchain, exit);
            Assert.Contains($"FAIL  cache       {blocker} isn't writable", stdout);
            Assert.Contains("fix: set BAYAN_CACHE to a writable directory\n", stdout);
        });
    }

    // ── test --changed ───────────────────────────────────────────────────────────

    private static (int Code, string Out) Git(string dir, params string[] args) =>
        Scaffold.Git(Toolchain.FindOnPath("git")!, dir, ["-c", "core.hooksPath=" + Path.Combine(dir, ".no-hooks"), "-c", "user.name=test", "-c", "user.email=test@example.com", "-c", "commit.gpgsign=false", .. args]);

    /// <summary>An app whose entry module imports `money`, each with a test, committed to a fresh repository.</summary>
    private static string ChangedProject(string name)
    {
        var app = Path.Combine(Fresh(name), "tally");
        Assert.Equal(CliApp.ExitOk, Run(["new", app, "--vcs", "none"]).Exit);
        Directory.CreateDirectory(Path.Combine(app, "src", "money"));
        File.WriteAllText(Path.Combine(app, "src", "money", "money.byn"), "public i64 cents(i64 units) {\n    return units * 100;\n}\n\ntest \"cents\" {\n    assert(cents(2) == 200);\n}\n");
        File.WriteAllText(Path.Combine(app, "src", "app", "main.byn"), "import money;\n\nstring greeting() {\n    return \"Hello, world!\";\n}\n\ntest \"in main\" {\n    assert(money.cents(1) == 100);\n}\n\nprint(greeting());\n");
        Assert.Equal(0, Git(app, "init", "--quiet", "-b", "work").Code);
        Assert.Equal(0, Git(app, "add", "-A").Code);
        Assert.Equal(0, Git(app, "commit", "--quiet", "-m", "start").Code);
        return app;
    }

    /// <summary>The names of the tests that passed, sorted.</summary>
    private static string[] Ran(string stdout) =>
        [.. PassLine().Matches(stdout).Select(m => m.Groups[1].Value).Order(StringComparer.Ordinal)];

    [GeneratedRegex("^  ok +(.+?)  \\(", RegexOptions.Multiline)]
    private static partial Regex PassLine();

    [Fact]
    public void TestChangedRunsOnlyModulesWithChangedFiles()
    {
        if (Toolchain.FindOnPath("git") is null) return;
        var app = ChangedProject("changed");
        var all = Run(["test", app]);
        Assert.Equal(CliApp.ExitOk, all.Exit);
        Assert.Equal(["cents", "greets the world", "in main"], Ran(all.Out));

        var none = Run(["test", app, "--changed"]);
        Assert.Equal(CliApp.ExitOk, none.Exit);
        Assert.StartsWith("running 0 tests (3 in unchanged modules skipped)\n", none.Out);

        File.AppendAllText(Path.Combine(app, "src", "money", "money.byn"), "\n");
        Assert.Equal(["cents"], Ran(Run(["test", app, "--changed"]).Out));

        File.WriteAllText(Path.Combine(app, "tests", "extra.test.byn"), "test \"extra\" {\n    assert(true);\n}\n");
        Assert.Equal(["cents", "extra", "greets the world", "in main"], Ran(Run(["test", app, "--changed"]).Out));

        Assert.Equal(0, Git(app, "add", "-A").Code);
        Assert.Equal(0, Git(app, "commit", "--quiet", "-m", "more").Code);
        Assert.StartsWith("running 0 tests", Run(["test", app, "--changed"]).Out);
        Assert.Equal(["cents", "extra", "greets the world", "in main"], Ran(Run(["test", app, "--base", "HEAD~1"]).Out));

        File.AppendAllText(Path.Combine(app, "package.byn"), "\n");
        Assert.Equal(4, Ran(Run(["test", app, "--changed"]).Out).Length);

        var json = Run(["test", app, "--changed", "--json"]).Out;
        Assert.Contains("\"skipped\": 0", json);
    }

    [Fact]
    public void TestChangedNeedsAValidBaseAndARepository()
    {
        if (Toolchain.FindOnPath("git") is null) return;
        var app = ChangedProject("changed-errors");
        var (exit, _, err) = Run(["test", app, "--base", "no-such-ref"]);
        Assert.Equal(CliApp.ExitUsage, exit);
        Assert.Contains("can't compare the working tree with `no-such-ref`", err);

        var loose = Fresh("changed-no-repo");
        File.WriteAllText(Path.Combine(loose, "main.byn"), "void main() {\n}\n\ntest \"t\" {\n    assert(true);\n}\n");
        With("GIT_CEILING_DIRECTORIES", Path.GetDirectoryName(loose), () =>
        {
            var r = Run(["test", loose, "--changed"]);
            Assert.Equal(CliApp.ExitUsage, r.Exit);
            Assert.Contains("needs a git repository", r.Err);
        });
    }

    [Fact]
    public void TestChangedWorksThroughASymlinkedPath()
    {
        if (Toolchain.FindOnPath("git") is null || OperatingSystem.IsWindows()) return;
        var app = ChangedProject("changed-symlink");
        var link = Path.Combine(Path.GetDirectoryName(app)!, "link");
        Directory.CreateSymbolicLink(link, app);
        File.AppendAllText(Path.Combine(link, "src", "money", "money.byn"), "\n");
        Assert.Equal(["cents"], Ran(Run(["test", link, "--changed"]).Out));
    }

    [Fact]
    public void TestChangedSeesNonAsciiFileNames()
    {
        if (Toolchain.FindOnPath("git") is null) return;
        var app = ChangedProject("changed-non-ascii");
        var cafe = Path.Combine(app, "src", "money", "café.byn");
        File.WriteAllText(cafe, "public i64 euros(i64 units) {\n    return units;\n}\n");
        Assert.Equal(["cents"], Ran(Run(["test", app, "--changed"]).Out));

        Assert.Equal(0, Git(app, "add", "-A").Code);
        Assert.Equal(0, Git(app, "commit", "--quiet", "-m", "café").Code);
        File.AppendAllText(cafe, "\n");
        Assert.Equal(["cents"], Ran(Run(["test", app, "--changed"]).Out));
    }

    // ── test timeouts ────────────────────────────────────────────────────────────

    [Fact]
    public void ATestThatRunsTooLongIsReportedAsTimedOut()
    {
        var file = Path.Combine(Fresh("timeout"), "main.byn");
        File.WriteAllText(file, "void main() {\n}\n\ntest \"loops\" {\n    while (true) {\n    }\n}\n");
        With("BAYAN_TEST_TIMEOUT_MS", "300", () =>
        {
            var (exit, stdout, _) = Run(["test", file]);
            Assert.Equal(CliApp.ExitTestFailure, exit);
            Assert.Matches("^  TIME   loops  \\(", stdout.Split('\n')[1]);
            Assert.Contains("1 test: 0 passed, 1 timed out  (", stdout);
            Assert.DoesNotContain("panicked", stdout);
        });
    }
}
