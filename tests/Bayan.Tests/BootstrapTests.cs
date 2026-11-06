using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Bayan.Backend.C;
using Bayan.Cli;

namespace Bayan.Tests;

/// <summary>
/// 012 T6: the three-stage bootstrap (R9, R11). Stage 1 is <c>selfhost/driver</c> built by <see cref="CliApp.Run"/> in-process
/// in <c>safe</c> (Q9); stage 2 is built by stage 1 and stage 3 by stage 2, each with the one invocation
/// <c>build --package selfhost -o tmp/bootstrap/stageN/bayan selfhost/driver</c> from the repo root (Q2). Each stage's C is
/// its <c>build --emit-c --mode &lt;m&gt; --package selfhost selfhost/driver</c>, kept as <c>stageN/driver.&lt;m&gt;.c</c> and
/// compared as bytes. <c>bootstrap/bayan.c</c> is C1 in <c>safe</c>; <c>bootstrap/bayan-rt.{h,c}</c> are
/// <c>src/runtime/</c>; <c>BAYAN_UPDATE=1</c> rewrites all three. The working directory is process-wide, so this runs alone.
/// </summary>
[Collection(nameof(ToolchainEnvironment))]
[Trait("Law", "L027")]
public sealed class BootstrapTests
{
    private static bool Full => Environment.GetEnvironmentVariable("BAYAN_LAW_TIER") == "full";

    private static int StageCount => Full ? 3 : 2;

    private static readonly string[] Modes = ["safe", "debug"];

    private static string StagesDir => Path.Combine(Golden.RepoRoot, "tmp", "bootstrap");

    private static string BootstrapDir => Path.Combine(Golden.RepoRoot, "bootstrap");

    /// <summary>Stage <paramref name="n"/>'s executable relative to the repo root, named as the C# names one (Targets.cs:29-32).</summary>
    private static string Exe(int n) => Path.Combine("tmp", "bootstrap", $"stage{n}", Targets.ExecutableName("bayan", Targets.Host ?? ""));

    private static string[] BuildArgs(int n) => ["build", "--package", "selfhost", "-o", Exe(n), "selfhost/driver"];

    private static string[] EmitArgs(string mode) => ["build", "--emit-c", "--mode", mode, "--package", "selfhost", "selfhost/driver"];

    private static readonly Lazy<bool> Built = new(BuildStages);

    private static (int Exit, byte[] Out, string Err) Process(string program, string cwd, params string[] args)
    {
        var psi = new ProcessStartInfo(program) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, WorkingDirectory = cwd };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = System.Diagnostics.Process.Start(psi)!;
        var stdout = new MemoryStream();
        var copy = p.StandardOutput.BaseStream.CopyToAsync(stdout);
        var stderr = p.StandardError.ReadToEndAsync();
        p.WaitForExit();
        copy.Wait();
        return (p.ExitCode, stdout.ToArray(), stderr.Result);
    }

    /// <summary>Stage <paramref name="n"/>'s C in <paramref name="mode"/>, written to <c>stageN/driver.&lt;mode&gt;.c</c>.</summary>
    private static byte[] Emit(int n, string mode)
    {
        var (exit, c, err) = Process(Path.Combine(Golden.RepoRoot, Exe(n)), Golden.RepoRoot, EmitArgs(mode));
        Assert.True(exit == 0, $"stage {n} --emit-c --mode {mode}: exit {exit}\n{err}");
        File.WriteAllBytes(Path.Combine(StagesDir, $"stage{n}", $"driver.{mode}.c"), c);
        return c;
    }

    private static bool BuildStages()
    {
        for (var n = 1; n <= StageCount; n++)
        {
            var dir = Path.Combine(StagesDir, $"stage{n}");
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
            Directory.CreateDirectory(dir);
        }
        Environment.SetEnvironmentVariable("BAYAN_CACHE", Path.Combine(Golden.RepoRoot, "tmp", "cache"));
        var times = new StringBuilder();
        var clock = Stopwatch.StartNew();
        var saved = Environment.CurrentDirectory;
        int exit;
        var stderr = new StringWriter();
        try
        {
            Environment.CurrentDirectory = Golden.RepoRoot;
            exit = CliApp.Run(BuildArgs(1), new StringWriter(), stderr);
        }
        finally
        {
            Environment.CurrentDirectory = saved;
        }
        Assert.True(exit == 0, $"stage 1 (C#): exit {exit}\n{stderr}");
        times.Append($"stage1 build {clock.Elapsed.TotalSeconds:F2}s\n");
        for (var n = 1; n <= StageCount; n++)
        {
            if (n > 1)
            {
                clock.Restart();
                var (code, _, err) = Process(Path.Combine(Golden.RepoRoot, Exe(n - 1)), Golden.RepoRoot, BuildArgs(n));
                Assert.True(code == 0, $"stage {n} (built by stage {n - 1}): exit {code}\n{err}");
                times.Append($"stage{n} build {clock.Elapsed.TotalSeconds:F2}s\n");
            }
            foreach (var mode in Modes)
            {
                clock.Restart();
                Emit(n, mode);
                times.Append($"stage{n} emit {mode} {clock.Elapsed.TotalSeconds:F2}s\n");
            }
        }
        File.WriteAllText(Path.Combine(StagesDir, "times.txt"), times.ToString());
        return true;
    }

    /// <summary>Stage 2's absolute path, building the stages first when no test has (shared with <c>SelfhostDriverGoldenTests</c>).</summary>
    internal static string Stage2Executable()
    {
        Assert.True(Built.Value);
        return Path.Combine(Golden.RepoRoot, Exe(2));
    }

    private static byte[] C(int n, string mode)
    {
        Assert.True(Built.Value);
        return File.ReadAllBytes(Path.Combine(StagesDir, $"stage{n}", $"driver.{mode}.c"));
    }

    /// <summary>Null when equal, else the first differing line of the two as a counterexample.</summary>
    private static string? FirstDifference(string label, byte[] expected, byte[] actual)
    {
        if (expected.AsSpan().SequenceEqual(actual)) return null;
        var a = Encoding.UTF8.GetString(expected).Split('\n');
        var b = Encoding.UTF8.GetString(actual).Split('\n');
        var i = 0;
        while (i < a.Length && i < b.Length && a[i] == b[i]) i++;
        return $"{label}, line {i + 1}\n  first:  {(i < a.Length ? a[i] : "<end>")}\n  second: {(i < b.Length ? b[i] : "<end>")}";
    }

    /// <summary>C1 == C2 in <c>safe</c> and <c>debug</c>, and C1 == C3 with <c>BAYAN_LAW_TIER=full</c> (R9, Q8).</summary>
    [Fact]
    public void StagesReachAFixpoint()
    {
        foreach (var mode in Modes)
        {
            var c1 = C(1, mode);
            Assert.Null(FirstDifference($"{mode}: stage1 vs stage2", c1, C(2, mode)));
            if (Full) Assert.Null(FirstDifference($"{mode}: stage1 vs stage3", c1, C(3, mode)));
        }
    }

    [Fact]
    public void CSharpEmitEqualsStage1()
    {
        var saved = Environment.CurrentDirectory;
        try
        {
            Environment.CurrentDirectory = Golden.RepoRoot;
            foreach (var mode in Modes)
            {
                var stdout = new StringWriter();
                var stderr = new StringWriter();
                var exit = CliApp.Run(EmitArgs(mode), stdout, stderr);
                Assert.True(exit == 0, $"C# --emit-c --mode {mode}: exit {exit}\n{stderr}");
                Assert.Null(FirstDifference($"{mode}: C# vs stage1", Encoding.UTF8.GetBytes(stdout.ToString()), C(1, mode)));
            }
        }
        finally
        {
            Environment.CurrentDirectory = saved;
        }
    }

    /// <summary>C1 emitted twice is the same, and names neither the repo root (either separator) nor a rooted `#line` file (K6).</summary>
    [Fact]
    public void C1IsReproducibleAndHasNoAbsolutePath()
    {
        var root = Golden.RepoRoot.TrimEnd('/', '\\');
        foreach (var mode in Modes)
        {
            var first = C(1, mode);
            var (exit, again, err) = Process(Path.Combine(Golden.RepoRoot, Exe(1)), Golden.RepoRoot, EmitArgs(mode));
            Assert.True(exit == 0, err);
            Assert.Null(FirstDifference($"{mode}: stage1 first vs second emit", first, again));
            var text = Encoding.UTF8.GetString(first);
            Assert.DoesNotContain(root.Replace('\\', '/'), text);
            Assert.DoesNotContain(root.Replace('/', '\\'), text);
            Assert.DoesNotContain(root.Replace('/', '\\').Replace("\\", "\\\\"), text);
            var names = Regex.Matches(text, "^#line \\d+ \"([^\"]*)\"$", RegexOptions.Multiline).Select(m => m.Groups[1].Value).Distinct().ToList();
            Assert.NotEmpty(names);
            foreach (var name in names)
                Assert.False(Path.IsPathRooted(name) || Regex.IsMatch(name, @"^([A-Za-z]:|[/\\])"), $"{mode}: rooted #line name {name}");
        }
    }

    /// <summary>`bootstrap/bayan.c` is C1 in `safe` (R11); `BAYAN_UPDATE=1` rewrites it.</summary>
    [Fact]
    public void BootstrapCIsC1Safe()
    {
        var c1 = C(1, "safe");
        var path = Path.Combine(BootstrapDir, "bayan.c");
        if (Golden.UpdateMode)
        {
            Directory.CreateDirectory(BootstrapDir);
            File.WriteAllBytes(path, c1);
        }
        Assert.True(File.Exists(path), $"{path} is missing; run BAYAN_UPDATE=1 dotnet test --filter FullyQualifiedName~BootstrapTests");
        Assert.Null(FirstDifference("bootstrap/bayan.c vs stage1 driver.safe.c", File.ReadAllBytes(path), c1));
    }

    /// <summary>`bootstrap/bayan-rt.{h,c}` are byte for byte `src/runtime/bayan-rt.{h,c}` (Q6); `BAYAN_UPDATE=1` copies them.</summary>
    [Theory]
    [InlineData("bayan-rt.h")]
    [InlineData("bayan-rt.c")]
    public void RuntimeCopiesAreTheRuntime(string name)
    {
        var source = File.ReadAllBytes(Path.Combine(Golden.RepoRoot, "src", "runtime", name));
        var copy = Path.Combine(BootstrapDir, name);
        if (Golden.UpdateMode)
        {
            Directory.CreateDirectory(BootstrapDir);
            File.WriteAllBytes(copy, source);
        }
        Assert.True(File.Exists(copy), $"{copy} is missing; run BAYAN_UPDATE=1 dotnet test --filter FullyQualifiedName~BootstrapTests");
        Assert.True(source.AsSpan().SequenceEqual(File.ReadAllBytes(copy)), $"bootstrap/{name} differs from src/runtime/{name}");
    }

    /// <summary>
    /// The `bootstrap/README.md` command builds `bootstrap/bayan.c` with the host `cc` (no Zig, K5, Q6) and the result's
    /// `version` prints what stage 1's does. Skipped on Windows: the CI image has no `cc`.
    /// </summary>
    [Fact]
    public void ReadmeCommandBuildsWithCc()
    {
        if (OperatingSystem.IsWindows()) return;
        var command = File.ReadLines(Path.Combine(BootstrapDir, "README.md")).Single(l => l.StartsWith("cc ", StringComparison.Ordinal));
        Assert.DoesNotContain("zig", command);
        var dir = Path.Combine(StagesDir, "readme");
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        Directory.CreateDirectory(dir);
        var words = command.Split(' ');
        var args = words[1..].Select(w => w.StartsWith("bootstrap/", StringComparison.Ordinal) ? Path.Combine(Golden.RepoRoot, w) : w).ToArray();
        var (exit, _, err) = Process("cc", dir, args);
        Assert.True(exit == 0, $"{command}: exit {exit}\n{err}");
        Assert.True(Built.Value);
        var expected = Process(Path.Combine(Golden.RepoRoot, Exe(1)), dir, "version");
        var actual = Process(Path.Combine(dir, "a.out"), dir, "version");
        Assert.Equal(0, actual.Exit);
        Assert.Equal(Encoding.UTF8.GetString(expected.Out), Encoding.UTF8.GetString(actual.Out));
        Assert.Equal(expected.Err, actual.Err);
    }
}
