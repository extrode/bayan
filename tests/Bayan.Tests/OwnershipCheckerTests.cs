using System.Diagnostics;
using Bayan.Backend.C;
using Bayan.Cli;
using Bayan.Semantics;
using Bayan.Syntax.Diagnostics;
using Bayan.Syntax.Text;

namespace Bayan.Tests;

/// <summary>
/// The debug-build ownership checker (spec 003): safe and fast builds carry none of it, and
/// <see cref="CompilationOptions.SkipOwnershipCheck"/> lets a test compile a program the static check rejects.
/// </summary>
public sealed class OwnershipCheckerTests
{
    public static TheoryData<string> RunCases => Golden.Cases("run");

    [Theory]
    [MemberData(nameof(RunCases))]
    public void SafeAndFastCHaveNoChecker(string name)
    {
        var path = Path.Combine(Golden.CasesRoot, name);
        var c = Compilation.Create([Golden.Load(path)], Path.GetDirectoryName(path)!);
        Assert.False(c.Diagnostics.HasErrors);
        Assert.DoesNotContain("byn_own", CliApp.EmitC(c, BuildMode.Safe));
        Assert.DoesNotContain("byn_own", CliApp.EmitC(c, BuildMode.Fast));
    }

    private const string UseAfterMove = """
        void main() {
            var a = List<i64>();
            var b = move a;
            a.add(1);
            print($"{b.length}");
        }

        """;

    private const string SecondOwner = """
        void main() {
            var a = List<i64>();
            a.add(1);
            var b = a;
            b.add(2);
            a.add(3);
        }

        """;

    private const string ReadAfterMove = """
        void main() {
            var a = List<i64>();
            a.add(1);
            var b = move a;
            print($"{a.length} {b.length}");
        }

        """;

    [Fact]
    public void DebugBuildCatchesASecondOwner() =>
        AssertPanics(SecondOwner, "panic: second owner of List<i64>: owned since c.byn:3\n  at c.byn:4\n");

    [Fact]
    public void DebugBuildCatchesAReadOfAMovedLocal() =>
        AssertPanics(ReadAfterMove, "panic: use of moved List<i64>: moved at c.byn:4\n  at c.byn:5\n");

    private const string DoubleMove = """
        void main() {
            var a = List<i64>();
            a.add(1);
            let b = move a;
            let c = move a;
            print($"{b.length} {c.length}");
        }

        """;

    [Fact]
    public void DebugBuildCatchesASecondMoveOfAMovedLocal() =>
        AssertPanics(DoubleMove, "panic: use of moved List<i64>: moved at c.byn:4\n  at c.byn:5\n");

    [Fact]
    public async Task DebugBuildOfSwitchMoveRuns()
    {
        var path = Path.Combine(Golden.CasesRoot, "run/switch-move.byn");
        var c = Compilation.Create([Golden.Load(path)], Path.GetDirectoryName(path)!);
        Assert.False(c.Diagnostics.HasErrors);
        var build = Toolchain.Build(CliApp.EmitC(c, BuildMode.Debug), BuildMode.Debug);
        Assert.True(build.Succeeded, build.Errors);
        var psi = new ProcessStartInfo(build.ExecutablePath!) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        using var p = Process.Start(psi)!;
        var stderrTask = p.StandardError.ReadToEndAsync();
        var actual = p.StandardOutput.ReadToEnd();
        await stderrTask;
        await p.WaitForExitAsync();
        Assert.Equal(0, p.ExitCode);
        Assert.Equal(File.ReadAllText(Path.ChangeExtension(path, ".stdout")).Replace("\r\n", "\n"), actual.Replace("\r\n", "\n"));
    }

    [Fact]
    public void DebugCMovesASwitchSubjectOnce()
    {
        var path = Path.Combine(Golden.CasesRoot, "run/switch-move.byn");
        var c = Compilation.Create([Golden.Load(path)], Path.GetDirectoryName(path)!);
        var text = File.ReadAllLines(path);
        var lines = Enumerable.Range(0, text.Length).Where(i => text[i].TrimStart().StartsWith("switch (move ") || text[i].Contains(") switch {")).Select(i => i + 1).ToList();
        Assert.Equal(4, lines.Count);
        var emitted = CliApp.EmitC(c, BuildMode.Debug);
        foreach (var line in lines)
            Assert.Equal(1, emitted.Split('\n').Count(l => l.Contains("bynh_chk_move_") && l.Contains($", {line})") && !l.StartsWith("static")));
    }

    [Theory]
    [InlineData(SecondOwner)]
    [InlineData(ReadAfterMove)]
    public void SafeAndFastBuildsOfRejectedProgramsHaveNoChecker(string program)
    {
        var c = Compile(program, skip: true);
        Assert.DoesNotContain("byn_own", CliApp.EmitC(c, BuildMode.Safe));
        Assert.DoesNotContain("byn_own", CliApp.EmitC(c, BuildMode.Fast));
        Assert.Contains("byn_own", CliApp.EmitC(c, BuildMode.Debug));
    }

    private static void AssertPanics(string program, string stderr)
    {
        var build = Toolchain.Build(CliApp.EmitC(Compile(program, skip: true), BuildMode.Debug), BuildMode.Debug);
        Assert.True(build.Succeeded, build.Errors);
        var psi = new ProcessStartInfo(build.ExecutablePath!) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        using var p = Process.Start(psi)!;
        var stdoutTask = p.StandardOutput.ReadToEndAsync();
        var actual = p.StandardError.ReadToEnd();
        stdoutTask.Wait();
        p.WaitForExit();
        Assert.Equal(134, p.ExitCode);
        Assert.Equal(stderr, actual.Replace("\r\n", "\n"));
    }

    private static Compilation Compile(string program, bool skip)
    {
        var source = new SourceText("c.byn", System.Text.Encoding.UTF8.GetBytes(program));
        var c = Compilation.Create([source], ".", new CompilationOptions(SkipOwnershipCheck: skip));
        Assert.False(c.Diagnostics.HasErrors, DiagnosticRenderer.RenderAll(c.Diagnostics.Items, includePath: true));
        return c;
    }

    [Fact]
    public void SkipOwnershipCheckSilencesTheStaticCheck()
    {
        Assert.Empty(OwnershipErrors(skip: true));
        Assert.Contains("BYN2781", OwnershipErrors(skip: false));
    }

    private static List<string> OwnershipErrors(bool skip)
    {
        var source = new SourceText("c.byn", System.Text.Encoding.UTF8.GetBytes(UseAfterMove));
        var c = Compilation.Create([source], ".", new CompilationOptions(SkipOwnershipCheck: skip));
        return c.Diagnostics.Items.Where(d => d.IsError && d.Code.StartsWith("BYN278", StringComparison.Ordinal)).Select(d => d.Code).ToList();
    }
}
