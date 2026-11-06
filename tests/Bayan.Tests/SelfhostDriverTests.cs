using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Reflection;
using Bayan.Cli;
using Bayan.Syntax.Diagnostics;

namespace Bayan.Tests;

/// <summary>
/// 012 T1: the driver's dispatch, usage and global options against <see cref="CliApp.Run"/> (R2), and its refusals (R3).
/// The expected lines come from the C# run in-process; only the version line's parenthesised part is masked (spec Q3).
/// </summary>
public sealed class SelfhostDriverT1Tests
{
    private static string Mask(string text) => Regex.Replace(text, @"^(bayan \S+) \(.*\)$", "$1 (masked)", RegexOptions.Multiline);

    /// <summary><see cref="CliApp.Run(string[], TextWriter, TextWriter)"/> in-process, the oracle of R2 (no terminal), line ends read as LF.</summary>
    private static (int Exit, string Out, string Err) Oracle(string[] args)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var exit = CliApp.Run(args, stdout, stderr);
        return (exit, stdout.ToString().ReplaceLineEndings("\n"), stderr.ToString().ReplaceLineEndings("\n"));
    }

    private static void AssertSameAsCs(params string[] args)
    {
        var expected = Oracle(args);
        var actual = SelfhostDriverHarness.Run(args);
        var label = string.Join(' ', args);
        Assert.Equal(expected.Exit, actual.Exit);
        SelfhostEmitterHarness.AssertSame($"stdout of `{label}`", Mask(expected.Out), Mask(actual.Out));
        SelfhostEmitterHarness.AssertSame($"stderr of `{label}`", expected.Err, actual.Err);
    }

    public static IEnumerable<object[]> Corpus() => new[]
    {
        new string[] { },
        new[] { "version" },
        new[] { "--version" },
        new[] { "help" },
        new[] { "--help" },
        new[] { "-h" },
        new[] { "frobnicate" },
        new[] { "fmt" },
        new[] { "--ascii" },
        new[] { "--ascii", "help" },
        new[] { "--color", "never", "version" },
        new[] { "--color=always", "-h" },
        new[] { "version", "--color", "never" },
        new[] { "--color", "sometimes", "help" },
        new[] { "--color=bad", "help" },
        new[] { "--color" },
        new[] { "--" },
        new[] { "--bogus" },
        new[] { "missing-script.byn" },
        new[] { "--ascii", "fmt", "--color", "never" },
        new[] { "version", Path.Combine(SelfhostDriverHarness.DriverDir, "main.byn"), "--color", "bad" },
    }.Select(a => new object[] { a });

    [Theory]
    [MemberData(nameof(Corpus))]
    public void MatchesTheCSharpCli(string[] args) => AssertSameAsCs(args);

    [Theory]
    [InlineData("doctor")]
    [InlineData("format")]
    [InlineData("audit")]
    [InlineData("features")]
    [InlineData("explain")]
    [InlineData("test")]
    [InlineData("mutate")]
    [InlineData("new")]
    [InlineData("dist")]
    [InlineData("cache")]
    [InlineData("clean")]
    [InlineData("init")]
    public void RefusesWhatItDoesNotPort(string command)
    {
        var (exit, stdout, stderr) = SelfhostDriverHarness.Run(command, "x");
        Assert.Equal(CliApp.ExitUsage, exit);
        Assert.Equal("", stdout);
        // no-oracle: Q917 (the C# never prints the refusal of spec Q4; `CliApp.Run` runs these commands)
        Assert.Equal($"error: `{command}` is not available in the self-hosted compiler; use the C# `bayan`\n", stderr);
    }

    /// <summary>`build` and `run` reach their commands (T5, T6): a missing path fails as the C# does.</summary>
    [Fact]
    public void DispatchesBuildAndRun()
    {
        var missing = Path.Combine(Golden.RepoRoot, "tmp", "t1-driver", "missing.byn");
        AssertSameAsCs("build", missing);
        AssertSameAsCs("run", missing);
    }

    [Fact]
    public void AScriptRunsAndAGlobalOptionBeforeItIsTaken()
    {
        var dir = Path.Combine(Golden.RepoRoot, "tmp", "t1-driver");
        Directory.CreateDirectory(dir);
        var script = Path.Combine(dir, "tool.byn");
        File.WriteAllText(script, "print(\"hi\")\n");
        // the script runs (T6); `--color bad` after it is its own argument (CliApp.cs:97, :328)
        // (the C# program inherits the test process's stdout, so only the exit code is comparable)
        string[] runArgs = ["--ascii", script, "--color", "bad"];
        Assert.Equal(Oracle(runArgs).Exit, SelfhostDriverHarness.Run(runArgs).Exit);
        AssertSameAsCs(dir);
        // a directory named like a script is not one (`File.Exists`)
        Directory.CreateDirectory(Path.Combine(dir, "d.byn"));
        AssertSameAsCs(Path.Combine(dir, "d.byn"));
    }

    private static string StyleLine(Style? out_, Style? err, bool tty) =>
        $"out {Fmt(out_)} err {Fmt(err)} tty {tty.ToString().ToLowerInvariant()}";

    private static string Fmt(Style? s) => $"{(s!.Colors is not null).ToString().ToLowerInvariant()} {(s.Symbols == Symbols.Ascii).ToString().ToLowerInvariant()} {s.Width}";

    /// <summary>
    /// The style state `Run` sets (CliApp.cs:40) with no environment variable set: the colour and `--ascii` come from the C#
    /// <c>TakeGlobalOptions</c> itself, then <c>Decide</c> as `Run` calls it, over an empty environment so the test process's
    /// <c>NO_COLOR</c>, <c>FORCE_COLOR</c>, <c>TERM</c> and locale cannot change the expected line (`Run` takes no environment).
    /// </summary>
    internal static string CsStyle(string[] args)
    {
        var call = new object[] { args, new StringWriter() };
        var taken = typeof(CliApp).GetMethod("TakeGlobalOptions", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, call);
        var (color, ascii) = ((string Color, bool Ascii))taken!;
        var style = TerminalStyle.Decide(color, ascii, false, _ => null, 0);
        return StyleLine(style, style, false);
    }

    internal static string ProbeStyle(IReadOnlyDictionary<string, string> env, string[] args)
    {
        var (exit, stdout, _) = SelfhostDriverHarness.RunProbe(env, ["style", .. args]);
        Assert.Equal(CliApp.ExitOk, exit);
        return stdout.TrimEnd('\n').Split('\n')[^1];
    }

    [Theory]
    [InlineData("--color", "always", "version")]
    [InlineData("--color", "never", "version")]
    [InlineData("--color", "auto", "version")]
    [InlineData("--ascii", "version")]
    [InlineData("--color=always", "--ascii", "version")]
    [InlineData("--ascii", "--color", "never", "version")]
    [InlineData("version")]
    public void DecidesTheStyleAsCliAppRun(params string[] args) =>
        Assert.Equal(CsStyle(args), ProbeStyle(new Dictionary<string, string>(), args));

    [Theory]
    [InlineData("NO_COLOR", "1", "always", false)]
    [InlineData("FORCE_COLOR", "1", "never", false)]
    [InlineData("TERM", "dumb", "always", true)]
    public void AnExplicitFlagDecidesWhateverTheEnvironmentSays(string name, string value, string color, bool ascii)
    {
        // CliApp.Run reads Environment.GetEnvironmentVariable, so the C# side is `Decide` over the same variable, the process
        // environment itself left alone for the other tests. The driver reads no environment (Q919): these are the cases where a flag decides.
        string? Env(string k) => k == name ? value : null;
        var expected = TerminalStyle.Decide(color, ascii, false, Env, 0);
        string[] args = ascii ? ["--ascii", "--color", color, "version"] : ["--color", color, "version"];
        Assert.Equal(StyleLine(expected, expected, false), ProbeStyle(new Dictionary<string, string> { [name] = value }, args));
    }

    [Theory]
    [InlineData("auto", false, true, 80)]
    [InlineData("auto", false, true, 0)]
    [InlineData("auto", true, false, 80)]
    [InlineData("never", true, true, 80)]
    [InlineData("always", false, false, 80)]
    public void DecideMatchesTerminalStyleWithNoEnvironment(string color, bool ascii, bool tty, int width)
    {
        var expected = TerminalStyle.Decide(color, ascii, tty, _ => null, width);
        var actual = SelfhostDriverHarness.RunProbe(new Dictionary<string, string>(), "decide", color, ascii ? "true" : "false", tty ? "true" : "false", width.ToString()).Out;
        Assert.Equal(Fmt(expected) + "\n", actual);
    }

    [Fact]
    public void RefuseLibraryAsCliAppDoes()
    {
        var root = Path.Combine(Golden.RepoRoot, "tmp", "t1-driver-lib");
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        var lib = Path.Combine(root, "cleanlib");
        Directory.CreateDirectory(Path.Combine(lib, "src", "cleanlib"));
        File.WriteAllText(Path.Combine(lib, "package.byn"), "Package {\n    name: \"local/cleanlib\",\n    version: \"0.1.0\",\n    edition: 2027,\n    kind: .lib,\n}\n");
        File.WriteAllText(Path.Combine(lib, "src", "cleanlib", "main.byn"), "public i32 one() => 1\n");
        var plain = Path.Combine(root, "plain");
        Directory.CreateDirectory(plain);
        File.WriteAllText(Path.Combine(plain, "main.byn"), "i32 main() => 0\n");
        foreach (var (path, refused) in new[] { (lib, true), (plain, false) })
        {
            var expected = Oracle(["build", "--emit-c", path]);
            Assert.Equal(refused ? CliApp.ExitUsage : CliApp.ExitOk, expected.Exit);
            var (exit, stdout, stderr) = SelfhostDriverHarness.RunProbe(new Dictionary<string, string>(), "library", path, "build");
            Assert.Equal(0, exit);
            Assert.Equal(refused ? "true\n" : "false\n", stdout);
            SelfhostEmitterHarness.AssertSame($"stderr for {Path.GetFileName(path)}", expected.Err, stderr);
        }
    }

    [Fact]
    public void IsScriptAsCliAppDoes()
    {
        var dir = Path.Combine(Golden.RepoRoot, "tmp", "t1-driver-script");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "a.byn"), "#!/usr/bin/env bayan\nprint(\"x\")\n");
        File.WriteAllText(Path.Combine(dir, "b.byn"), "print(\"x\")\n");
        File.WriteAllText(Path.Combine(dir, "c.byn"), "#");
        File.WriteAllText(Path.Combine(dir, "d.byn"), "#!");
        File.WriteAllText(Path.Combine(dir, "e.byn"), "#x\n");
        var isScript = typeof(CliApp).GetMethod("IsScript", BindingFlags.NonPublic | BindingFlags.Static)!;
        foreach (var path in new[] { "a.byn", "b.byn", "c.byn", "d.byn", "e.byn", "missing.byn", "." }.Select(n => Path.GetFullPath(Path.Combine(dir, n))))
        {
            var expected = ((bool)isScript.Invoke(null, [path])!).ToString().ToLowerInvariant();
            Assert.Equal(expected + "\n", SelfhostDriverHarness.RunProbe(new Dictionary<string, string>(), "isscript", path).Out);
        }
    }

    /// <summary>`bayan format --check` over `selfhost/driver/` with the pinned `bayan`. Skipped on Windows: `bash scripts/pinned-bayan.sh` exits 127 on wp1 (work/todo.md, 012 T28).</summary>
    [Fact]
    public void SourcesAreFormatted()
    {
        if (OperatingSystem.IsWindows()) return;
        var pinned = Run("bash", Path.Combine(Golden.RepoRoot, "scripts", "pinned-bayan.sh"));
        Assert.Equal(0, pinned.Exit);
        var bayan = pinned.Out.Trim().Split('\n')[^1].Trim();
        var (exit, stdout, stderr) = Run(bayan, "format", "--check", SelfhostDriverHarness.DriverDir);
        Assert.True(exit == 0, $"bayan format --check selfhost/driver:\n{stdout}{stderr}");
    }

    private static (int Exit, string Out, string Err) Run(string program, params string[] args)
    {
        var psi = new ProcessStartInfo(program) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, WorkingDirectory = Golden.RepoRoot };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        p.WaitForExit();
        return (p.ExitCode, stdout.Result, stderr.Result);
    }
}

/// <summary>
/// 012 T23: the style oracle of <see cref="SelfhostDriverT1Tests"/> does not read the test process's environment.
/// Runs alone (the collection is not parallel) because it sets <c>FORCE_COLOR</c> and <c>NO_COLOR</c> in the process.
/// </summary>
[Collection(nameof(ToolchainEnvironment))]
public sealed class SelfhostDriverStyleEnvironmentTests
{
    [Theory]
    [InlineData("FORCE_COLOR")]
    [InlineData("NO_COLOR")]
    public void TheStyleOracleIgnoresTheProcessEnvironment(string name)
    {
        var saved = Environment.GetEnvironmentVariable(name);
        Environment.SetEnvironmentVariable(name, "1");
        try
        {
            foreach (var args in new[] { new[] { "version" }, ["--color", "auto", "version"], ["--ascii", "version"] })
                Assert.Equal(SelfhostDriverT1Tests.CsStyle(args), SelfhostDriverT1Tests.ProbeStyle(new Dictionary<string, string>(), args));
        }
        finally { Environment.SetEnvironmentVariable(name, saved); }
    }
}
