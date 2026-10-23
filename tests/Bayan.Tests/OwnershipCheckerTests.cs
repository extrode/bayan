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
        var options = new CompilationOptions(IncludeTests: name.EndsWith(".test.byn", StringComparison.Ordinal));
        var c = Compilation.Create([Golden.Load(path)], Path.GetDirectoryName(path)!, options);
        Assert.False(c.Diagnostics.HasErrors);
        Assert.DoesNotContain("byn_own", CliApp.EmitC(c, BuildMode.Safe));
        Assert.DoesNotContain("byn_own", CliApp.EmitC(c, BuildMode.Fast));
    }

    private const string UseAfterMove = """
        void main() {
            var a = List<i64>()
            var b = move a
            a.append(1)
            print($"{b.length}")
        }

        """;

    private const string SecondOwner = """
        void main() {
            var a = List<i64>()
            a.append(1)
            var b = a
            b.append(2)
            a.append(3)
        }

        """;

    private const string ReadAfterMove = """
        void main() {
            var a = List<i64>()
            a.append(1)
            var b = move a
            print($"{a.length} {b.length}")
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
            var a = List<i64>()
            a.append(1)
            let b = move a
            let c = move a
            print($"{b.length} {c.length}")
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
        var lines = Enumerable.Range(0, text.Length).Where(i => text[i].TrimStart().StartsWith("switch move ") || text[i].Contains(") switch {")).Select(i => i + 1).ToList();
        Assert.Equal(4, lines.Count);
        var emitted = CliApp.EmitC(c, BuildMode.Debug);
        foreach (var line in lines)
            Assert.Equal(1, emitted.Split('\n').Count(l => l.Contains("bynh_chk_move_") && l.Contains($", {line})") && !l.StartsWith("static")));
    }

    private const string GuardedArm = """
        union Slot {
            full(List<i64> items)
            empty
        }
        void main() {
            var a = List<i64>()
            a.append(1)
            let s = Slot.full(move a)
            switch move s {
                case .full(let x) when x.length > 5:
                    print($"big {x.length}")
                case .full(let y):
                    print($"small {y.length}")
                case .empty:
                    print("empty")
            }
        }

        """;

    private const string GuardedArmExpression = """
        union Slot {
            full(List<i64> items)
            empty
        }
        void main() {
            var a = List<i64>()
            a.append(1)
            let s = Slot.full(move a)
            let n = (move s) switch {
                .full(let x) when x.length > 5 => x.length * 10,
                .full(let y) => y.length,
                .empty => 0,
            }
            print($"{n}")
        }

        """;

    private const string FailedSubpattern = """
        struct P {
            List<i64> items
            i64 n
        }
        void main() {
            var a = List<i64>()
            a.append(1)
            let p = P { items: move a, n: 3 }
            switch move p {
                case { items: let x, n: 5 }:
                    print($"five {x.length}")
                case { items: let y, n: _ }:
                    print($"other {y.length}")
            }
        }

        """;

    // An arm not taken (its guard or a later subpattern failed) does not keep its bindings' storage.
    [Theory]
    [InlineData(GuardedArm)]
    [InlineData(GuardedArmExpression)]
    [InlineData(FailedSubpattern)]
    public async Task DebugBuildReleasesBindingsOfAnArmNotTaken(string program)
    {
        var c = Compile(program, skip: false);
        var (safeCode, safeOut) = await Run(CliApp.EmitC(c, BuildMode.Safe), BuildMode.Safe);
        var (debugCode, debugOut) = await Run(CliApp.EmitC(c, BuildMode.Debug), BuildMode.Debug);
        Assert.Equal(0, safeCode);
        Assert.Equal(0, debugCode);
        Assert.Equal(safeOut, debugOut);
    }

    private const string MoveThenReadInCall = """
        i64 f(move List<i64> x, i64 n) => n + x.length
        void main() {
            var a = List<i64>()
            a.append(1)
            print($"{f(move a, a.length)}")
        }

        """;

    private const string MoveThenReadInLiteral = """
        struct P {
            List<i64> xs
            i64 n
        }
        void main() {
            var a = List<i64>()
            a.append(1)
            let p = P { xs: move a, n: a.length }
            print($"{p.n}")
        }

        """;

    private const string MoveThenReadInNestedCall = """
        i64 f(move List<i64> x, i64 n) => n + x.length
        i64 g(List<i64> x) => x.length
        void main() {
            var a = List<i64>()
            a.append(1)
            print($"{f(move a, g(a))}")
        }

        """;

    private const string NestedMoveThenRead = """
        struct P {
            List<i64> xs
        }
        i64 f(P p, i64 n) => n + p.xs.length
        void main() {
            var a = List<i64>()
            a.append(1)
            print($"{f(P { xs: move a }, a.length)}")
        }

        """;

    // Operands run left to right, so a later operand reads the local an earlier one moved.
    [Theory]
    [InlineData(MoveThenReadInCall, 5)]
    [InlineData(MoveThenReadInLiteral, 8)]
    [InlineData(MoveThenReadInNestedCall, 6)]
    [InlineData(NestedMoveThenRead, 8)]
    public void DebugBuildCatchesAReadAfterAMoveInOneExpression(string program, int line) =>
        AssertPanics(program, $"panic: use of moved List<i64>: moved at c.byn:{line}\n  at c.byn:{line}\n");

    private const string GrowThenMoveReceiver = """
        struct Bag {
            List<i64> items
            public i64 grow(ref this) {
                this.items.append(1)
                return this.items.length
            }
        }
        Bag keep(i64 n, move Bag b) => b
        void main() {
            var b = Bag { items: List<i64>() }
            let d = keep(b.grow(), move b)
            print($"{d.items.length}")
        }

        """;

    private const string GrowInGuard = """
        struct Bag {
            List<i64> items
            public i64 grow(ref this) {
                this.items.append(1)
                return this.items.length
            }
        }
        union Slot {
            full(Bag b)
            empty
        }
        void main() {
            var a = List<i64>()
            a.append(1)
            a.append(2)
            a.append(3)
            let s = Slot.full(Bag { items: move a })
            switch move s {
                case .full(var x) when x.grow() > 5:
                    print("big")
                case .full(let y):
                    print($"small {y.items.length}")
                default:
                    print("empty")
            }
        }

        """;

    // A claim after a call is not made for a receiver the statement moves.
    [Theory]
    [InlineData(GrowThenMoveReceiver, "1\n")]
    public async Task DebugBuildDoesNotClaimStorageItsOwnerGaveUp(string program, string expected)
    {
        var (code, stdout) = await Run(CliApp.EmitC(Compile(program, skip: false), BuildMode.Debug), BuildMode.Debug);
        Assert.Equal(0, code);
        Assert.Equal(expected, stdout);
    }

    // A guard that grows its binding would leave the next arm a stale copy of the subject; the binder rejects it
    // (BYN2791), so the static switch does not let it reach the debug checker, which no longer skips claims in guards.
    [Fact]
    public void StaticSwitchDoesNotSilenceAGuardThatGrowsItsBinding()
    {
        var source = new SourceText("c.byn", System.Text.Encoding.UTF8.GetBytes(GrowInGuard));
        var c = Compilation.Create([source], ".", new CompilationOptions(SkipOwnershipCheck: true));
        Assert.Equal(["BYN2791"], c.Diagnostics.Items.Where(d => d.IsError).Select(d => d.Code));
    }

    private const string GrowThroughRef = """
        void bump(ref List<i64> x) {
            x.append(1)
        }
        void main() {
            var x = List<i64>()
            bump(ref x)
            var y = x
            y.append(2)
        }

        """;

    [Fact]
    public void DebugBuildOwnsStorageGrownThroughARef() =>
        AssertPanics(GrowThroughRef, "panic: second owner of List<i64>: owned since c.byn:6\n  at c.byn:7\n");

    private const string FillThroughOut = """
        void fill(out List<i64> xs) {
            xs = List<i64>()
            xs.append(7)
        }
        void main() {
            var x = List<i64>()
            fill(out x)
            var y = x
            y.append(1)
            print($"{y.length}")
        }

        """;

    [Fact]
    public void DebugBuildOwnsStorageWrittenThroughAnOut() =>
        AssertPanics(FillThroughOut, "panic: second owner of List<i64>: owned since c.byn:7\n  at c.byn:8\n");

    // `bayan build --mode debug --emit-c` output compiles by hand against the runtime, without -DBYN_DEBUG.
    [Fact]
    public void DebugCCompilesStandalone()
    {
        var compiler = Toolchain.FindCompiler();
        Assert.NotNull(compiler);
        var path = Path.Combine(Golden.CasesRoot, "run/switch-move-guard.byn");
        var clean = Compilation.Create([Golden.Load(path)], Path.GetDirectoryName(path)!);
        var (code, stdout, _) = BuildStandalone(compiler, CliApp.EmitC(clean, BuildMode.Debug));
        Assert.Equal(0, code);
        Assert.Equal(File.ReadAllText(Path.ChangeExtension(path, ".stdout")).Replace("\r\n", "\n"), stdout.Replace("\r\n", "\n"));
        var (panicCode, _, stderr) = BuildStandalone(compiler, CliApp.EmitC(Compile(SecondOwner, skip: true), BuildMode.Debug));
        Assert.Equal(134, panicCode);
        Assert.Equal("panic: second owner of List<i64>: owned since c.byn:3\n  at c.byn:4\n", stderr.Replace("\r\n", "\n"));
    }

    private static (int Code, string Stdout, string Stderr) BuildStandalone(string[] compiler, string cSource)
    {
        var dir = Directory.CreateTempSubdirectory("bayan-standalone-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(dir, "out.c"), cSource);
            File.WriteAllText(Path.Combine(dir, "bayan-rt.c"), Toolchain.RuntimeSource);
            File.WriteAllText(Path.Combine(dir, "bayan-rt.h"), Toolchain.RuntimeHeader);
            var exe = Path.Combine(dir, OperatingSystem.IsWindows() ? "out.exe" : "out");
            var cc = new ProcessStartInfo(compiler[0]) { WorkingDirectory = dir, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            foreach (var a in compiler[1..]) cc.ArgumentList.Add(a);
            foreach (var a in new[] { "-std=c11", "out.c", "bayan-rt.c", "-I.", "-o", exe, OperatingSystem.IsWindows() ? "-lshell32" : "-lm" }) cc.ArgumentList.Add(a);
            var (ccCode, ccOut, ccErr) = Exec(cc);
            Assert.True(ccCode == 0, ccOut + ccErr);
            return Exec(new ProcessStartInfo(exe) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false });
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Theory]
    [InlineData(BuildMode.Safe)]
    [InlineData(BuildMode.Fast)]
    public void SafeAndFastExecutablesHaveNoChecker(BuildMode mode)
    {
        var build = Toolchain.Build(CliApp.EmitC(Compile(SecondOwner, skip: true), mode), mode);
        Assert.True(build.Succeeded, build.Errors);
        Assert.DoesNotContain("byn_own_", System.Text.Encoding.Latin1.GetString(File.ReadAllBytes(build.ExecutablePath!)));
    }

    private static (int Code, string Stdout, string Stderr) Exec(ProcessStartInfo psi)
    {
        using var p = Process.Start(psi)!;
        var stderrTask = p.StandardError.ReadToEndAsync();
        var stdout = p.StandardOutput.ReadToEnd();
        var stderr = stderrTask.Result;
        p.WaitForExit();
        return (p.ExitCode, stdout, stderr);
    }

    private static async Task<(int Code, string Stdout)> Run(string cSource, BuildMode mode)
    {
        var build = Toolchain.Build(cSource, mode);
        Assert.True(build.Succeeded, build.Errors);
        var psi = new ProcessStartInfo(build.ExecutablePath!) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        using var p = Process.Start(psi)!;
        var stderrTask = p.StandardError.ReadToEndAsync();
        var stdout = await p.StandardOutput.ReadToEndAsync();
        await stderrTask;
        await p.WaitForExitAsync();
        return (p.ExitCode, stdout.Replace("\r\n", "\n"));
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
