using System.Diagnostics;
using Bayan.Backend.C;
using Bayan.Semantics;
using Bayan.Syntax.Diagnostics;
using Bayan.Syntax.Text;

namespace Bayan.Tests;

/// <summary>
/// 012 T3: the C compiler interface of the driver (<c>selfhost/driver/cc.byn</c>) against the C# toolchain. Both sides run as
/// processes with a <c>PATH</c> holding fake compilers, so the C# <c>FindCompiler</c> and <c>Compile</c> are the oracle: the
/// fake <c>zig</c>/<c>cc</c> records its argv and creates the output named after <c>-o</c>. The scratch directory (Q1) is the
/// only difference masked: <c>-ffile-prefix-map=&lt;dir&gt;=.</c>, <c>-I &lt;dir&gt;</c> and the absolute <c>main.c</c>/<c>bayan-rt.c</c>.
/// The probe build (a <c>main</c> over <c>cc.byn</c>) is local to this file: <see cref="SelfhostDriverHarness"/> has no entry for it.
/// </summary>
[Collection(nameof(ToolchainEnvironment))]
public sealed class SelfhostDriverT3Tests
{
    private const string ProbeMain = """
        i32 main(ReadOnlySpan<string> args) {
            if args[0] == "triple" {
                Io.stdout.write($"{targetTriple(args[1]) ?? "null"} {targetExecutableName("tally", for: args[1])} {targetIsKnown(args[1])}\n")
                return 0
            }
            if args[0] == "refusal" {
                var compiler = List<string>()
                for i in 2..args.length { compiler.append(args[i]) }
                Io.stdout.write($"{ccRefusal(in compiler, for: args[1])}\n")
                return 0
            }
            var cc = ZigOrCc {}
            // `target ?? Targets.Host` of Toolchain.Build: an empty argument is no `--target`.
            let target = args[5] == "" ? (targetHost() ?? "") : args[5]
            let r = cc.compile(args[1], from: args[2], to: args[3], by: args[4], for: target)
            if r.succeeded { return 0 }
            ccReportToolchain(in r)
            return 4
        }
        """;

    private static readonly Lazy<(string? Path, string Error)> Built = new(BuildNow);

    private static (string? Path, string Error) BuildNow()
    {
        var sources = Directory.GetFiles(SelfhostDriverHarness.DriverDir, "*.byn").Order(StringComparer.Ordinal)
            .Select(p => Path.GetFileName(p) == "main.byn"
                ? SourceText.FromString(p, File.ReadAllText(p).Replace("i32 main(ReadOnlySpan<string> args) {", "i32 driverMain(ReadOnlySpan<string> args) {"))
                : SourceText.FromFile(p)).ToList();
        sources.Add(SourceText.FromString(Path.Combine(SelfhostDriverHarness.DriverDir, "t3probe.byn"), ProbeMain + "\n"));
        Environment.SetEnvironmentVariable("BAYAN_CACHE", Path.Combine(Golden.RepoRoot, "tmp", "cache"));
        var c = Compilation.Create(sources, packageRoot: SelfhostSemanticsHarness.SelfhostDir);
        if (c.Diagnostics.HasErrors) return (null, DiagnosticRenderer.RenderAll(c.Diagnostics.Items, includePath: true));
        var build = SelfhostSemanticsHarness.BuildFast(c, new HashSet<string>(StringComparer.Ordinal));
        return build.Succeeded ? (build.ExecutablePath, "") : (null, build.Errors);
    }

    private static (int Exit, string Out, string Err) Start(string program, string path, IEnumerable<string> args)
    {
        var psi = new ProcessStartInfo(program) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        psi.Environment["PATH"] = path;
        psi.Environment["BAYAN_CACHE"] = Path.Combine(Golden.RepoRoot, "tmp", "t3", "cache-" + Guid.NewGuid().ToString("N"));
        psi.Environment.Remove("BAYAN_CC");
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        p.WaitForExit();
        return (p.ExitCode, stdout.Result.ReplaceLineEndings("\n"), stderr.Result.ReplaceLineEndings("\n"));
    }

    private static string Probe()
    {
        var (path, error) = Built.Value;
        if (path is null) Assert.Fail($"selfhost/driver with the T3 probe main did not build:\n{error}");
        return path!;
    }

    private static string Cli => Path.Combine(Golden.RepoRoot, "src", "Bayan.Cli", "bin", "Debug", "net10.0", "bayan");

    private static string Hello => Path.Combine(Golden.RepoRoot, "examples", "hello", "main.byn");

    private static string Fresh(string name)
    {
        var dir = Path.Combine(Golden.RepoRoot, "tmp", "t3", name);
        TestDirs.Delete(dir);
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void Script(string path, string body)
    {
        File.WriteAllText(path, "#!/bin/sh\n" + body);
        // The fake compilers are shell scripts: unix only.
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    /// <summary>A fake compiler: records its own name and its argv beside itself and creates the file after <c>-o</c>.</summary>
    private const string Recording = """
        printf '%s\n' "${0##*/}" "$@" > "${0%/*}/argv"
        prev=
        for a in "$@"; do
          if [ "$prev" = "-o" ]; then : > "$a"; fi
          prev=$a
        done
        exit 0
        """;

    /// <summary>A rejection whose text has no final newline: the report adds one.</summary>
    private const string RejectingBare = """
        printf 'no newline' >&2
        exit 1
        """;

    private const string Rejecting = """
        echo "main.c:2:5: error: injected" >&2
        echo "stdout note"
        exit 1
        """;

    /// <summary>A directory of fake compilers on a <c>PATH</c> of its own; <paramref name="names"/> are the ones present.</summary>
    private static string Bin(string name, string body, params string[] names)
    {
        var dir = Fresh(name);
        foreach (var n in names) Script(Path.Combine(dir, n), body);
        return dir;
    }

    private static string Masked(string argv, string scratch)
    {
        var lines = argv.TrimEnd('\n').Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i].StartsWith("-ffile-prefix-map=", StringComparison.Ordinal)) lines[i] = "-ffile-prefix-map=<dir>=.";
            else if (i > 0 && lines[i - 1] == "-I") lines[i] = "<dir>";
            else if (i > 0 && lines[i - 1] == "-o") lines[i] = Path.GetFileName(lines[i]);
            else if (lines[i] == Path.Combine(scratch, "main.c")) lines[i] = "main.c";
            else if (lines[i] == Path.Combine(scratch, "bayan-rt.c")) lines[i] = "bayan-rt.c";
        }
        return string.Join("\n", lines);
    }

    /// <summary>The C# side: <c>bayan build</c> under <paramref name="bin"/>; the Bayan side: the probe with the same <c>PATH</c>.</summary>
    private static ((int Exit, string Err) Cs, (int Exit, string Err) Byn, string CsArgv, string BynArgv) Both(string bin, string mode, string target, string name)
    {
        var scratch = Fresh(name);
        var output = Path.Combine(scratch, Targets.ExecutableName("program", target == "" ? "linux" : target));
        var csOut = Path.Combine(scratch, "cs-out");
        var argv = Path.Combine(bin, "argv");
        File.Delete(argv);
        var csArgs = new List<string> { "build", Hello, "-o", csOut, "--mode", mode };
        if (target != "") csArgs.AddRange(["--target", target]);
        var cs = Start(Cli, bin, csArgs);
        var csArgv = File.Exists(argv) ? File.ReadAllText(argv) : "";
        File.Delete(argv);
        var byn = Start(Probe(), bin, ["compile", scratch, "int main(void){return 0;}", output, mode, target]);
        var bynArgv = File.Exists(argv) ? File.ReadAllText(argv) : "";
        if (cs.Exit == 0) Assert.True(File.Exists(csOut));
        if (byn.Exit == 0) Assert.True(File.Exists(output));
        Assert.Equal(cs.Exit == 0, byn.Exit == 0);
        // The C# names the compiler by the path `FindOnPath` found; `Io.run` searches `PATH` itself, so the driver names it as probed.
        return ((cs.Exit, cs.Err.Replace(bin + "/", "")), (byn.Exit, byn.Err), Masked(csArgv, scratch), Masked(bynArgv, scratch));
    }

    public static TheoryData<string, string> Matrix()
    {
        var data = new TheoryData<string, string>();
        foreach (var mode in new[] { "debug", "safe", "fast" })
        {
            data.Add(mode, "");
            foreach (var target in Targets.All.Where(t => t != Targets.Host)) data.Add(mode, target);
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(Matrix))]
    public void ArgumentListAsToolchain(string mode, string target)
    {
        var bin = Bin("bin-zig", Recording, "zig", "cc");
        var r = Both(bin, mode, target, $"args-{mode}-{target}");
        Assert.Equal(0, r.Cs.Exit);
        Assert.NotEqual("", r.CsArgv);
        SelfhostEmitterHarness.AssertSame($"argv of {mode} {target}", r.CsArgv, r.BynArgv);
        Assert.Equal(r.Cs.Err, r.Byn.Err);
        Assert.Equal(0, r.Byn.Exit);
    }

    [Fact]
    public void ArgumentListWithCcOnly()
    {
        var bin = Bin("bin-cc", Recording, "cc");
        var r = Both(bin, "safe", "", "args-cc");
        Assert.Equal(0, r.Cs.Exit);
        Assert.NotEqual("", r.CsArgv);
        SelfhostEmitterHarness.AssertSame("argv with cc only", r.CsArgv, r.BynArgv);
    }

    [Fact]
    public void ClangIsTriedAfterCc()
    {
        var bin = Bin("bin-clang", Recording, "clang", "gcc");
        var r = Both(bin, "debug", "", "args-clang");
        Assert.Equal(0, r.Cs.Exit);
        SelfhostEmitterHarness.AssertSame("argv with clang and gcc", r.CsArgv, r.BynArgv);
    }

    [Theory]
    [InlineData("cc", "clang")]
    [InlineData("clang", "gcc")]
    [InlineData("cc", "gcc")]
    public void TheFirstOfCcClangGccIsChosen(string first, string second)
    {
        var bin = Bin($"bin-{first}-{second}", Recording, first, second);
        var r = Both(bin, "safe", "", $"order-{first}-{second}");
        Assert.Equal(0, r.Cs.Exit);
        Assert.StartsWith(first + "\n", r.CsArgv);
        SelfhostEmitterHarness.AssertSame($"compiler chosen from {first} and {second}", r.CsArgv, r.BynArgv);
    }

    [Fact]
    public void GccIsTheLastResort()
    {
        var bin = Bin("bin-gcc", Recording, "gcc");
        var r = Both(bin, "fast", "", "args-gcc");
        Assert.Equal(0, r.Cs.Exit);
        SelfhostEmitterHarness.AssertSame("argv with gcc only", r.CsArgv, r.BynArgv);
    }

    [Theory]
    [InlineData("linux-x64")]
    [InlineData("windows-x64")]
    public void CrossBuildWithoutZigAsToolchain(string target)
    {
        var host = Targets.Host;
        var bin = Bin("bin-refuse", Recording, "cc");
        var r = Both(bin, "safe", target == host ? "wasm32" : target, $"refuse-{target}");
        Assert.Equal(4, r.Cs.Exit);
        Assert.Equal(4, r.Byn.Exit);
        Assert.StartsWith("bayan: ", r.Cs.Err);
        SelfhostEmitterHarness.AssertSame($"stderr of {target}", r.Cs.Err, r.Byn.Err);
    }

    /// <summary>The unknown-target refusal cannot come through <c>bayan build</c> (it rejects the name first), so the oracle is <c>Toolchain.Build</c>.</summary>
    [Fact]
    public void UnknownTargetAsToolchain()
    {
        var zig = Bin("bin-unknown", Recording, "zig");
        var saved = Environment.GetEnvironmentVariable("BAYAN_CC");
        Environment.SetEnvironmentVariable("BAYAN_CC", Path.Combine(zig, "zig") + " cc");
        NativeBuild expected;
        try { expected = Toolchain.Build("int main(void){return 0;}", BuildMode.Safe, null, "not-a-target"); }
        finally { Environment.SetEnvironmentVariable("BAYAN_CC", saved); }
        Assert.StartsWith("unknown target `not-a-target`; targets are linux-x64, ", expected.Errors);
        var (exit, _, err) = Start(Probe(), zig, ["compile", Fresh("unknown"), "int main(void){return 0;}", "program", "safe", "not-a-target"]);
        Assert.Equal(4, exit);
        SelfhostEmitterHarness.AssertSame("stderr of an unknown target", $"bayan: {expected.Errors}\n", err);
    }

    [Fact]
    public void NoCompilerIsExitFour()
    {
        var empty = Fresh("bin-none");
        var r = Both(empty, "safe", "", "none");
        Assert.Equal(4, r.Cs.Exit);
        Assert.Equal("bayan: no C compiler found: install zig (https://ziglang.org) or set BAYAN_CC\n", r.Cs.Err);
        Assert.Equal(4, r.Byn.Exit);
        SelfhostEmitterHarness.AssertSame("stderr without a compiler", r.Cs.Err, r.Byn.Err);
        var (exit, _, err) = Start(Probe(), "", ["compile", Fresh("none-empty-path"), "int main(void){return 0;}", "program", "safe", ""]);
        Assert.Equal(4, exit);
        Assert.Equal(r.Cs.Err, err);
    }

    [Fact]
    public void ACompilerFailureIsReportedAsCliApp()
    {
        var bin = Bin("bin-reject", Rejecting, "cc");
        var r = Both(bin, "safe", "", "rejected");
        Assert.Equal(4, r.Cs.Exit);
        Assert.StartsWith("error BYN4001: internal compiler error", r.Cs.Err);
        Assert.Contains("main.c:2:5: error: injected\n", r.Cs.Err);
        Assert.Equal(4, r.Byn.Exit);
        SelfhostEmitterHarness.AssertSame("stderr of a rejected build", r.Cs.Err, r.Byn.Err);
    }

    /// <summary>
    /// The C# finds a non-executable <c>cc</c> (<c>File.Exists</c>) and fails to start it; the driver finds a program by starting it,
    /// so the same message needs a <c>cc</c> that starts once (the probe) and is gone for the build (Q954). The OS message is cut.
    /// </summary>
    [Fact]
    public void ACompilerThatCannotBeStartedIsReportedAsToolchain()
    {
        var plain = Fresh("bin-no-exec");
        File.WriteAllText(Path.Combine(plain, "cc"), "#!/bin/sh\nexit 0\n");
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(Path.Combine(plain, "cc"), UnixFileMode.UserRead | UnixFileMode.UserWrite);
        var cs = Start(Cli, plain, ["build", Hello, "-o", Path.Combine(Fresh("no-exec"), "cs-out")]);
        Assert.Equal(4, cs.Exit);
        var expected = cs.Err.Replace(plain + "/", "");
        Assert.StartsWith("bayan: cannot run the C compiler `cc`: ", expected);
        var vanishing = Bin("bin-vanishing", "/bin/rm -- \"$0\"\nexit 0\n", "cc");
        var (exit, _, err) = Start(Probe(), vanishing, ["compile", Fresh("vanishing"), "int main(void){return 0;}", "program", "safe", ""]);
        Assert.Equal(4, exit);
        Assert.Equal(expected[..(expected.IndexOf("`:", StringComparison.Ordinal) + 1)] + "\n", err);
    }

    [Fact]
    public void ACompilerThatLeavesNoExecutableIsAFailure()
    {
        // exit 0 and no output: the C# `!File.Exists(exe)` branch
        var bin = Bin("bin-nothing", "exit 0\n", "cc");
        var r = Both(bin, "safe", "", "nothing");
        Assert.Equal(4, r.Cs.Exit);
        Assert.Contains("C compiler failed (exit 0):", r.Cs.Err);
        SelfhostEmitterHarness.AssertSame("stderr of a compiler that builds nothing", r.Cs.Err, r.Byn.Err);
    }

    [Fact]
    public void AnUnwritableScratchDirectoryIsAToolchainProblem()
    {
        // no-oracle: Q955 the C# writes into a cache directory it creates; no C# path writes into a missing directory
        var bin = Bin("bin-unwritable", Recording, "cc");
        var missing = Path.Combine(Fresh("unwritable"), "no-such-dir");
        var (exit, _, err) = Start(Probe(), bin, ["compile", missing, "int main(void){return 0;}", "program", "safe", ""]);
        Assert.Equal(4, exit);
        Assert.Equal($"bayan: cannot write the build output ({missing})\n", err);
    }

    [Fact]
    public void ARejectionWithoutAFinalNewlineGetsOne()
    {
        var bin = Bin("bin-bare", RejectingBare, "cc");
        var r = Both(bin, "safe", "", "bare");
        Assert.Equal(4, r.Cs.Exit);
        Assert.EndsWith("C compiler failed (exit 1):\nno newline\n", r.Cs.Err);
        SelfhostEmitterHarness.AssertSame("stderr of a rejection without a newline", r.Cs.Err, r.Byn.Err);
    }

    [Theory]
    [InlineData("linux-x64")]
    [InlineData("linux-arm64")]
    [InlineData("macos-x64")]
    [InlineData("macos-arm64")]
    [InlineData("windows-x64")]
    [InlineData("windows-arm64")]
    [InlineData("wasm32")]
    [InlineData("not-a-target")]
    public void TargetsAsCSharp(string name)
    {
        var expected = $"{Targets.Triple(name) ?? "null"} {Targets.ExecutableName("tally", name)} {Targets.IsKnown(name)}\n".Replace("True", "true").Replace("False", "false");
        var (exit, stdout, _) = Start(Probe(), Fresh("bin-triple"), ["triple", name]);
        Assert.Equal(0, exit);
        SelfhostEmitterHarness.AssertSame($"triple and executable of {name}", expected, stdout);
    }

    [Theory]
    [InlineData("linux-x64", "notzig cc")]
    [InlineData("linux-x64", "zig")]
    [InlineData("linux-x64", "zig other")]
    [InlineData("not-a-target", "zig cc")]
    public void RefusalsAsToolchain(string target, string compiler)
    {
        var saved = Environment.GetEnvironmentVariable("BAYAN_CC");
        Environment.SetEnvironmentVariable("BAYAN_CC", compiler);
        NativeBuild expected;
        try { expected = Toolchain.Build("int main(void){return 0;}", BuildMode.Safe, null, target == Targets.Host ? "wasm32" : target); }
        finally { Environment.SetEnvironmentVariable("BAYAN_CC", saved); }
        Assert.False(expected.Succeeded);
        var (exit, stdout, _) = Start(Probe(), Fresh("bin-refusal"), ["refusal", target == Targets.Host ? "wasm32" : target, .. compiler.Split(' ')]);
        Assert.Equal(0, exit);
        SelfhostEmitterHarness.AssertSame($"refusal for {target} with {compiler}", expected.Errors + "\n", stdout);
    }

    /// <summary>A path to <c>zig.exe</c> is zig to the C# (Toolchain.cs:192), so there is no refusal and the build goes on to fail at the start.</summary>
    [Fact]
    public void ZigIsNamedWithoutItsExtensionAsToolchain()
    {
        var saved = Environment.GetEnvironmentVariable("BAYAN_CC");
        Environment.SetEnvironmentVariable("BAYAN_CC", "/nonexistent/zig.exe cc");
        NativeBuild expected;
        try { expected = Toolchain.Build("int main(void){return 0;}", BuildMode.Safe, null, "linux-x64"); }
        finally { Environment.SetEnvironmentVariable("BAYAN_CC", saved); }
        Assert.StartsWith("cannot run the C compiler `/nonexistent/zig.exe cc`", expected.Errors);
        var (exit, stdout, _) = Start(Probe(), Fresh("bin-zigexe"), ["refusal", "linux-x64", "/nonexistent/zig.exe", "cc"]);
        Assert.Equal(0, exit);
        Assert.Equal("\n", stdout);
    }

    /// <summary>Q954: <c>targetHost()</c> is a stub returning <c>""</c>, so the driver treats the host's own name as a cross build; the C# builds it with plain <c>cc</c>. Fails when the stub is ported.</summary>
    [Fact]
    public void TheHostNameIsACrossBuildWhileTargetHostIsAStub()
    {
        if (Targets.Host is not { } host) { Assert.Fail("this machine has no Bayan target, so the host-name case cannot run"); return; }
        var bin = Bin("bin-host-name", Recording, "cc");
        var scratch = Fresh("host-name");
        var cs = Start(Cli, bin, ["build", Hello, "-o", Path.Combine(scratch, "cs-out"), "--target", host]);
        Assert.Equal(0, cs.Exit);
        var (exit, _, err) = Start(Probe(), bin, ["compile", scratch, "int main(void){return 0;}", Path.Combine(scratch, "out"), "safe", host]);
        Assert.Equal(4, exit);
        Assert.StartsWith($"bayan: building for {host} on this machine needs zig", err);
    }
}
