using System.Text.RegularExpressions;
using Bayan.Backend.C;
using Bayan.Cli;
using Bayan.Syntax.Diagnostics;

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

    /// <summary>Runs <paramref name="body"/> in a UTF-8, dark, 16-colour environment with <paramref name="vars"/> on top.</summary>
    internal static void WithTerminalEnv(Action body, params (string Name, string? Value)[] vars)
    {
        (string Name, string? Value)[] all =
        [
            ("NO_COLOR", null), ("FORCE_COLOR", null), ("TERM", null), ("COLORTERM", null), ("BAYAN_THEME", null),
            ("COLORFGBG", null), ("LC_ALL", null), ("LC_CTYPE", null), ("LANG", "en_US.UTF-8"), .. vars,
        ];
        var saved = all.Select(v => (v.Name, Value: Environment.GetEnvironmentVariable(v.Name))).Distinct().ToArray();
        foreach (var (name, value) in all) Environment.SetEnvironmentVariable(name, value);
        try
        {
            body();
        }
        finally
        {
            foreach (var (name, value) in saved.Reverse()) Environment.SetEnvironmentVariable(name, value);
        }
    }

    private static string Fresh(string name)
    {
        var dir = Path.Combine(Golden.RepoRoot, "tmp", "cli-env", name);
        TestDirs.Delete(dir);
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string Broken()
    {
        var dir = Fresh("broken");
        var file = Path.Combine(dir, "main.byn");
        File.WriteAllText(file, "void main() {\n    let x = 1\n    x = 2\n}\n");
        return file;
    }

    [GeneratedRegex("\u001b\\[[0-9;]*m")]
    private static partial Regex Escape();

    // ── Color ────────────────────────────────────────────────────────────────────

    [Fact]
    public void ColorFollowsTheTerminalNoColorAndTheFlag()
    {
        var file = Broken();
        WithTerminalEnv(() =>
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
        WithTerminalEnv(() =>
        {
            Assert.DoesNotContain("\u001b[", Run(["check", file], new CliApp.Terminal(true, true)).Err);
            Assert.Contains("\u001b[", Run(["check", file, "--color", "always"], new CliApp.Terminal(true, true)).Err);
        }, ("NO_COLOR", "1"));
    }

    [Fact]
    public void ForceColorTurnsColorOnWhenPiped()
    {
        var file = Broken();
        WithTerminalEnv(() =>
        {
            Assert.Contains("\u001b[1m\u001b[31merror BYN", Run(["check", file]).Err, StringComparison.Ordinal);
            Assert.DoesNotContain("\u001b[", Run(["check", file, "--color", "never"]).Err, StringComparison.Ordinal);
            var json = Run(["check", file, "--diagnostics", "json"]).Err;
            Assert.DoesNotContain("\u001b[", json, StringComparison.Ordinal);
            WithTerminalEnv(() => Assert.Equal(json, Run(["check", file, "--diagnostics", "json"]).Err));
        }, ("FORCE_COLOR", "1"));
        WithTerminalEnv(() => Assert.DoesNotContain("\u001b[", Run(["check", file]).Err, StringComparison.Ordinal), ("FORCE_COLOR", "0"));
        WithTerminalEnv(() => Assert.DoesNotContain("\u001b[", Run(["check", file], new CliApp.Terminal(true, true)).Err, StringComparison.Ordinal), ("FORCE_COLOR", "1"), ("NO_COLOR", "1"));
    }

    [Fact]
    public void ADumbTerminalGetsNoColorAndAsciiSymbols()
    {
        var file = Broken();
        WithTerminalEnv(() =>
        {
            var err = Run(["check", file], new CliApp.Terminal(true, true)).Err;
            Assert.DoesNotContain("\u001b[", err, StringComparison.Ordinal);
            Assert.Contains("  3 |     x = 2", err, StringComparison.Ordinal);
            Assert.DoesNotContain("│", err, StringComparison.Ordinal);
        }, ("TERM", "dumb"));
    }

    [Fact]
    public void TruecolorFollowsTheTheme()
    {
        var file = Broken();
        const string dark = "\u001b[38;2;255;138;128merror BYN", light = "\u001b[38;2;180;35;24merror BYN";
        string Err() => Run(["check", file], new CliApp.Terminal(true, true)).Err;
        WithTerminalEnv(() => Assert.Contains(dark, Err(), StringComparison.Ordinal), ("COLORTERM", "truecolor"));
        WithTerminalEnv(() => Assert.Contains(dark, Err(), StringComparison.Ordinal), ("COLORTERM", "24bit"), ("BAYAN_THEME", "dark"));
        WithTerminalEnv(() => Assert.Contains(light, Err(), StringComparison.Ordinal), ("COLORTERM", "truecolor"), ("BAYAN_THEME", "light"));
        WithTerminalEnv(() => Assert.Contains(dark, Err(), StringComparison.Ordinal), ("COLORTERM", "truecolor"), ("COLORFGBG", "15;0"));
        WithTerminalEnv(() => Assert.Contains(light, Err(), StringComparison.Ordinal), ("COLORTERM", "truecolor"), ("COLORFGBG", "0;15"));
        WithTerminalEnv(() => Assert.Contains(dark, Err(), StringComparison.Ordinal), ("COLORTERM", "truecolor"), ("COLORFGBG", "0;15"), ("BAYAN_THEME", "dark"));
        WithTerminalEnv(() => Assert.Contains("\u001b[1m\u001b[31merror BYN", Err(), StringComparison.Ordinal), ("COLORFGBG", "0;15"));
    }

    [Fact]
    public void ANonUtf8LocaleOrAsciiFlagUsesAsciiSymbols()
    {
        var file = Broken();
        WithTerminalEnv(() =>
        {
            Assert.Contains("  3 │     x = 2", Run(["check", file]).Err, StringComparison.Ordinal);
            var ascii = Run(["--ascii", "check", file], new CliApp.Terminal(true, true)).Err;
            Assert.Contains("\u001b[", ascii, StringComparison.Ordinal);
            Assert.DoesNotContain("│", Escape().Replace(ascii, ""), StringComparison.Ordinal);
            Assert.Contains("  3 |     x = 2", Run(["check", file, "--ascii"]).Err, StringComparison.Ordinal);
        });
        if (OperatingSystem.IsWindows())
        {
            // No locale variable decides there: the console code page does.
            TestSetup.SetConsoleCodePage(437);
            try
            {
                WithTerminalEnv(() => Assert.Contains("  3 |     x = 2", Run(["check", file]).Err, StringComparison.Ordinal));
            }
            finally
            {
                TestSetup.SetConsoleCodePage(65001);
            }
            WithTerminalEnv(() => Assert.Contains("  3 │     x = 2", Run(["check", file]).Err, StringComparison.Ordinal));
            return;
        }
        WithTerminalEnv(() => Assert.Contains("  3 |     x = 2", Run(["check", file]).Err, StringComparison.Ordinal), ("LANG", "C"));
        WithTerminalEnv(() => Assert.Contains("  3 │     x = 2", Run(["check", file]).Err, StringComparison.Ordinal), ("LANG", "C"), ("LC_ALL", "en_US.utf8"));
        WithTerminalEnv(() => Assert.Contains("  3 |     x = 2", Run(["check", file]).Err, StringComparison.Ordinal), ("LC_CTYPE", "POSIX"));
        WithTerminalEnv(() => Assert.Contains("  3 │     x = 2", Run(["check", file]).Err, StringComparison.Ordinal), ("LANG", null));
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
        File.WriteAllText(Path.Combine(dir, "main.test.byn"), "test \"passes\" {\n    assert(true)\n}\n\ntest \"breaks\" {\n    assert(false)\n}\n");
        WithTerminalEnv(() =>
        {
            var (exit, stdout, _) = Run(["test", dir], new CliApp.Terminal(true, true));
            Assert.Equal(CliApp.ExitTestFailure, exit);
            Assert.Contains("  \u001b[1m\u001b[31m✗\u001b[0m color-tests: breaks\n", stdout);
            Assert.Contains("\u001b[32m◆\u001b[0m 1 passed · \u001b[31m✗\u001b[0m 1 failed · 0 pending · ", stdout);
            Assert.DoesNotContain("\u001b[", Run(["test", dir]).Out);
        });
    }

    [Fact]
    public void AScriptKeepsItsColorArgument()
    {
        var dir = Fresh("script-args");
        var script = Path.Combine(dir, "tool.byn");
        File.WriteAllText(script, "#!/usr/bin/env bayan\n#:io threaded\n\nprint(\"hi\")\n");
        Assert.Equal(CliApp.ExitOk, Run([script, "--color", "sometimes"]).Exit);
        Assert.Equal(CliApp.ExitOk, Run(["--color", "never", script, "--color", "sometimes"]).Exit);
        Assert.Equal(CliApp.ExitOk, Run(["--ascii", script, "--color", "sometimes"]).Exit);
    }

    // ── Doctor ───────────────────────────────────────────────────────────────────

    [Fact]
    public void DoctorReportsAHealthyToolchain()
    {
        var (exit, stdout, _) = Run(["doctor"]);
        Assert.True(exit == CliApp.ExitOk, stdout);
        Assert.StartsWith($"◆ bayan       {CliApp.Version} (commit ", stdout);
        Assert.Contains($"◆ host        {Targets.Host}\n", stdout);
        Assert.Matches("◆ C compiler  .+ \\(.+\\)\n", stdout);
        Assert.Contains($"◆ cache       {Toolchain.CacheDirectory} (writable, ", stdout);
        Assert.DoesNotContain("✗", stdout);
        Assert.DoesNotContain("help:", stdout);
    }

    [Fact]
    public void DoctorExplainsAMissingCompiler()
    {
        With("BAYAN_CC", Path.Combine(Golden.RepoRoot, "tmp", "no-such-cc"), () =>
        {
            var (exit, stdout, _) = Run(["doctor"]);
            Assert.Equal(CliApp.ExitToolchain, exit);
            Assert.Contains("✗ C compiler: BAYAN_CC is `", stdout);
            Assert.Contains("  help: point BAYAN_CC at a C compiler, or unset it to use zig\n", stdout);
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
            Assert.Contains($"✗ cache: {blocker} isn't writable", stdout);
            Assert.Contains("  help: set BAYAN_CACHE to a writable directory\n", stdout);
        });
    }

    private static readonly Doctor.Check[] s_checks =
    [
        new(Doctor.Level.Ok, "bayan", "0.1.0"),
        new(Doctor.Level.Note, "zig", "not found", "install zig"),
        new(Doctor.Level.Fail, "cache", "isn't writable", "set BAYAN_CACHE"),
    ];

    private static string Render(Style style)
    {
        var writer = new StringWriter();
        Doctor.Render(s_checks, style, writer);
        return writer.ToString().ReplaceLineEndings("\n");
    }

    [Fact]
    public void DoctorRendersPlain()
    {
        Assert.Equal("◆ bayan  0.1.0\n◆ zig    not found\n  help: install zig\n✗ cache: isn't writable\n  help: set BAYAN_CACHE\n", Render(Style.Plain));
    }

    [Fact]
    public void DoctorRendersAscii()
    {
        Assert.Equal("* bayan  0.1.0\n* zig    not found\n  help: install zig\nx cache: isn't writable\n  help: set BAYAN_CACHE\n", Render(Style.Plain with { Symbols = Symbols.Ascii }));
    }

    [Fact]
    public void DoctorRendersColour()
    {
        var p = Palette.Ansi16;
        var expected = $"{p.Success}◆{Palette.Reset} bayan  0.1.0\n{p.Note}◆{Palette.Reset} zig    not found\n{p.Help}  help: install zig{Palette.Reset}\n"
            + $"{p.Error}✗ cache: isn't writable{Palette.Reset}\n{p.Help}  help: set BAYAN_CACHE{Palette.Reset}\n";
        Assert.Equal(expected, Render(Style.Plain with { Colors = p }));
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
        File.WriteAllText(Path.Combine(app, "src", "money", "money.byn"), "public i64 cents(i64 units) {\n    return units * 100\n}\n\ntest \"cents\" {\n    assert(cents(2) == 200)\n}\n");
        File.WriteAllText(Path.Combine(app, "src", "app", "main.byn"), "import money\n\nstring greeting() {\n    return \"Hello, world!\"\n}\n\ntest \"in main\" {\n    assert(money.cents(1) == 100)\n}\n\nprint(greeting())\n");
        Assert.Equal(0, Git(app, "init", "--quiet", "-b", "work").Code);
        Assert.Equal(0, Git(app, "add", "-A").Code);
        Assert.Equal(0, Git(app, "commit", "--quiet", "-m", "start").Code);
        return app;
    }

    /// <summary>The names of the tests that passed, from <c>-v</c> output, sorted.</summary>
    private static string[] Ran(string stdout) =>
        [.. PassLine().Matches(stdout).Select(m => m.Groups[1].Value).Order(StringComparer.Ordinal)];

    [GeneratedRegex("^  [*◆] [^:]+: (.+?)(?<! \\(pending\\))$", RegexOptions.Multiline)]
    private static partial Regex PassLine();

    [Fact]
    public void PendingLinesAreNotCountedAsRun() =>
        Assert.Equal(["one"], Ran("  ◆ m: one\n  ◆ m: later (pending)\n  * m: soon (pending)\n"));

    [Fact]
    public void TestChangedRunsOnlyModulesWithChangedFiles()
    {
        if (Toolchain.FindOnPath("git") is null) return;
        var app = ChangedProject("changed");
        var all = Run(["test", "-v", app]);
        Assert.Equal(CliApp.ExitOk, all.Exit);
        Assert.Equal(["cents", "greets the world", "in main"], Ran(all.Out));

        var none = Run(["test", app, "--changed"]);
        Assert.Equal(CliApp.ExitOk, none.Exit);
        Assert.StartsWith("running 0 tests in 0 modules (3 in unchanged modules skipped)\n", none.Out);

        File.AppendAllText(Path.Combine(app, "src", "money", "money.byn"), "\n");
        Assert.Equal(["cents"], Ran(Run(["test", "-v", app, "--changed"]).Out));

        File.WriteAllText(Path.Combine(app, "tests", "extra.test.byn"), "test \"extra\" {\n    assert(true)\n}\n");
        Assert.Equal(["cents", "extra", "greets the world", "in main"], Ran(Run(["test", "-v", app, "--changed"]).Out));

        Assert.Equal(0, Git(app, "add", "-A").Code);
        Assert.Equal(0, Git(app, "commit", "--quiet", "-m", "more").Code);
        Assert.StartsWith("running 0 tests", Run(["test", app, "--changed"]).Out);
        Assert.Equal(["cents", "extra", "greets the world", "in main"], Ran(Run(["test", "-v", app, "--base", "HEAD~1"]).Out));

        File.AppendAllText(Path.Combine(app, "package.byn"), "\n");
        Assert.Equal(4, Ran(Run(["test", "-v", app, "--changed"]).Out).Length);

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
        File.WriteAllText(Path.Combine(loose, "main.byn"), "void main() {\n}\n\ntest \"t\" {\n    assert(true)\n}\n");
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
        Assert.Equal(["cents"], Ran(Run(["test", "-v", link, "--changed"]).Out));
    }

    [Fact]
    public void TestChangedSeesNonAsciiFileNames()
    {
        if (Toolchain.FindOnPath("git") is null) return;
        var app = ChangedProject("changed-non-ascii");
        var cafe = Path.Combine(app, "src", "money", "café.byn");
        File.WriteAllText(cafe, "public i64 euros(i64 units) {\n    return units\n}\n");
        Assert.Equal(["cents"], Ran(Run(["test", "-v", app, "--changed"]).Out));

        Assert.Equal(0, Git(app, "add", "-A").Code);
        Assert.Equal(0, Git(app, "commit", "--quiet", "-m", "café").Code);
        File.AppendAllText(cafe, "\n");
        Assert.Equal(["cents"], Ran(Run(["test", "-v", app, "--changed"]).Out));
    }

    // ── test --only ──────────────────────────────────────────────────────────────

    [Fact]
    public void TestOnlySelectsBySubstringAndUnion()
    {
        if (Toolchain.FindOnPath("git") is null) return;
        var app = ChangedProject("only");
        WithTerminalEnv(() =>
        {
            var cents = Run(["test", "-v", app, "--only", "cent"]);
            Assert.Equal(CliApp.ExitOk, cents.Exit);
            Assert.StartsWith("running 1 test in 1 module\n", cents.Out);
            Assert.Equal(["cents"], Ran(cents.Out));
            Assert.Equal(["cents", "in main"], Ran(Run(["test", "-v", app, "--only", "cents", "--only", "main"]).Out));
            Assert.Equal(["greets the world", "in main"], Ran(Run(["test", "-v", app, "--only", " "]).Out));
        });
    }

    [Fact]
    public void TestOnlyWithChangedSelectsTheIntersection()
    {
        if (Toolchain.FindOnPath("git") is null) return;
        var app = ChangedProject("only-changed");
        File.AppendAllText(Path.Combine(app, "src", "money", "money.byn"), "\n");
        Assert.Equal(["cents"], Ran(Run(["test", "-v", app, "--changed", "--only", "cents", "--only", "main"]).Out));
        var none = Run(["test", app, "--changed", "--only", "main"]);
        Assert.Equal(CliApp.ExitOk, none.Exit);
        Assert.StartsWith("running 0 tests in 0 modules", none.Out);
    }

    [Fact]
    public void TestOnlyWithNoMatchIsAUsageError()
    {
        if (Toolchain.FindOnPath("git") is null) return;
        var app = ChangedProject("only-none");
        var (exit, stdout, err) = Run(["test", app, "--only", "nothing", "--only", "else"]);
        Assert.Equal(CliApp.ExitUsage, exit);
        Assert.Equal("", stdout);
        Assert.Equal("bayan: no test name contains `nothing`, `else`\n", err);

        var changed = Run(["test", app, "--only", "nothing", "--changed"]);
        Assert.Equal(CliApp.ExitUsage, changed.Exit);
        Assert.Equal("", changed.Out);
        Assert.Equal("bayan: no test name contains `nothing`\n", changed.Err);
    }

    // ── test timeouts ────────────────────────────────────────────────────────────

    [Fact]
    public void ATestThatRunsTooLongIsReportedAsTimedOut()
    {
        var file = Path.Combine(Fresh("timeout"), "main.byn");
        File.WriteAllText(file, "void main() {\n}\n\ntest \"loops\" {\n    while true {\n    }\n}\n");
        With("BAYAN_TEST_TIMEOUT_MS", "300", () =>
        {
            var (exit, stdout, _) = Run(["test", file]);
            Assert.Equal(CliApp.ExitTestFailure, exit);
            Assert.Contains(" timeout: loops\n      timed out after 0 s\n", stdout);
            Assert.Contains(" 0 passed · ", stdout);
            Assert.Contains(" 1 failed · ", stdout);
        });
    }
}
