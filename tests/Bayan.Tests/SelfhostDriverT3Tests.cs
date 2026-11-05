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
            if args[0] == "host" {
                Io.stdout.write($"{targetHost() ?? "none"}\n")
                return 0
            }
            if args[0] == "hostfrom" {
                Io.stdout.write($"{targetHostFrom(args[1], for: args[2]) ?? "none"}\n")
                return 0
            }
            if args[0] == "findpath" {
                Io.stdout.write($"{ccFindOnPath(args[2], for: args[1]) ?? "none"}\n")
                return 0
            }
            if args[0] == "arguments" {
                // No target (null) and `cc` alone: what `compile` would pass for the host.
                var compiler = List<string>()
                compiler.append("cc")
                for a in ccArguments(in compiler, for: args[1], to: args[2], by: args[3], through: null) { Io.stdout.write($"{a}\n") }
                return 0
            }
            if args[0] == "refusal" {
                var compiler = List<string>()
                for i in 2..args.length { compiler.append(args[i]) }
                // `-` is no target at all (null): the host has none and `--target` was not given.
                Io.stdout.write($"{ccRefusal(in compiler, for: args[1] == "-" ? null : args[1])}\n")
                return 0
            }
            var cc = ZigOrCc {}
            // An empty argument is no `--target`: null, which `compile` resolves to the host as `target ?? Targets.Host` of Toolchain.Build.
            let r = cc.compile(args[1], from: args[2], to: args[3], by: args[4], for: args[5] == "" ? null : args[5])
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

    private static (int Exit, string Out, string Err) Start(string program, string path, IEnumerable<string> args, string? workDir = null)
    {
        var psi = new ProcessStartInfo(program) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        if (workDir is not null) psi.WorkingDirectory = workDir;
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

    private static string Configuration =>
        Path.GetFileName(Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory)))!;

    private static string Cli => Path.Combine(Golden.RepoRoot, "src", "Bayan.Cli", "bin", Configuration, "net10.0", OperatingSystem.IsWindows() ? "bayan.exe" : "bayan");

    /// <summary>The file name <c>FindOnPath</c> finds for <paramref name="name"/> on this OS (Toolchain.cs:153: <c>.exe</c> first on Windows).</summary>
    private static string Tool(string name) => OperatingSystem.IsWindows() ? name + ".exe" : name;

    private static string Hello => Path.Combine(Golden.RepoRoot, "examples", "hello", "main.byn");

    private static string Fresh(string name)
    {
        var dir = Path.Combine(Golden.RepoRoot, "tmp", "t3", name);
        TestDirs.Delete(dir);
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>
    /// A fake compiler named <paramref name="path"/>: a <c>/bin/sh</c> script on Unix; on Windows (no <c>/bin/sh</c>, and <c>Io.run</c>
    /// refuses <c>.cmd</c>/<c>.bat</c>) a copy of <see cref="FakeExe"/> as <c>path.exe</c>, with the behaviour of <paramref name="body"/> in a <c>.mode</c> file.
    /// </summary>
    private static void Script(string path, string body)
    {
        if (OperatingSystem.IsWindows())
        {
            File.Copy(FakeExe.Value, path + ".exe", overwrite: true);
            File.WriteAllText(path + ".mode", body == Recording ? "record" : body == Rejecting ? "reject" : body == RejectingBare ? "bare" : "nothing");
            return;
        }
        File.WriteAllText(path, "#!/bin/sh\n" + body);
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    /// <summary>The fake compiler as C, for Windows: the same four behaviours as the scripts, chosen by <c>&lt;name&gt;.mode</c> beside it.</summary>
    private const string FakeSource = """
        #include <stdio.h>
        #include <string.h>
        int main(int argc, char **argv) {
            char dir[1024], name[256], file[1400], mode[32] = "nothing";
            const char *self = argv[0], *slash = strrchr(self, '\\'), *fwd = strrchr(self, '/');
            if (fwd > slash) slash = fwd;
            snprintf(dir, sizeof dir, "%.*s", slash ? (int)(slash - self) : 1, slash ? self : ".");
            snprintf(name, sizeof name, "%s", slash ? slash + 1 : self);
            size_t n = strlen(name);
            if (n > 4 && strcmp(name + n - 4, ".exe") == 0) name[n - 4] = 0;
            snprintf(file, sizeof file, "%s/%s.mode", dir, name);
            FILE *m = fopen(file, "r");
            if (m) { if (fscanf(m, "%31s", mode) != 1) strcpy(mode, "nothing"); fclose(m); }
            if (strcmp(mode, "reject") == 0) { fputs("main.c:2:5: error: injected\n", stderr); puts("stdout note"); return 1; }
            if (strcmp(mode, "bare") == 0) { fputs("no newline", stderr); return 1; }
            if (strcmp(mode, "record") != 0) return 0;
            snprintf(file, sizeof file, "%s/argv", dir);
            FILE *f = fopen(file, "w");
            if (!f) return 1;
            fprintf(f, "%s\n", name);
            for (int i = 1; i < argc; i++) fprintf(f, "%s\n", argv[i]);
            fclose(f);
            for (int i = 2; i < argc; i++)
                if (strcmp(argv[i - 1], "-o") == 0) { FILE *o = fopen(argv[i], "w"); if (o) fclose(o); }
            return 0;
        }
        """;

    private static readonly Lazy<string> FakeExe = new(() =>
    {
        var dir = Path.Combine(Golden.RepoRoot, "tmp", "t3", "fake-cc");
        Directory.CreateDirectory(dir);
        var source = Path.Combine(dir, "fake.c");
        var exe = Path.Combine(dir, "fake.exe");
        File.WriteAllText(source, FakeSource);
        var compiler = Toolchain.FindCompiler() ?? throw new InvalidOperationException("no C compiler to build the fake compiler");
        var psi = new ProcessStartInfo(compiler[0]) { RedirectStandardError = true, UseShellExecute = false };
        foreach (var a in compiler[1..]) psi.ArgumentList.Add(a);
        foreach (var a in new[] { source, "-o", exe }) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var err = p.StandardError.ReadToEnd();
        p.WaitForExit();
        Assert.True(p.ExitCode == 0 && File.Exists(exe), $"the fake compiler did not build:\n{err}");
        return exe;
    });

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
            else if (lines[i] == Path.Combine(scratch, "main.c")) lines[i] = "main.c";
            else if (lines[i] == Path.Combine(scratch, "bayan-rt.c")) lines[i] = "bayan-rt.c";
        }
        return string.Join("\n", lines);
    }

    /// <summary>The C# side: <c>bayan build</c> under <paramref name="bin"/>; the Bayan side: the probe with the same <c>PATH</c>.</summary>
    private static ((int Exit, string Err) Cs, (int Exit, string Err) Byn, string CsArgv, string BynArgv) Both(string bin, string mode, string target, string name)
    {
        var scratch = Fresh(name);
        // The C# compiles `-o program` in its work directory; the port gets the same name and runs in the scratch directory.
        var output = Targets.ExecutableName("program", target == "" ? Targets.Host ?? "" : target);
        var csOut = Path.Combine(scratch, "cs-out");
        var argv = Path.Combine(bin, "argv");
        File.Delete(argv);
        var csArgs = new List<string> { "build", Hello, "-o", csOut, "--mode", mode };
        if (target != "") csArgs.AddRange(["--target", target]);
        var cs = Start(Cli, bin, csArgs, scratch);
        var csArgv = File.Exists(argv) ? File.ReadAllText(argv).ReplaceLineEndings("\n") : "";
        File.Delete(argv);
        var byn = Start(Probe(), bin, ["compile", scratch, "int main(void){return 0;}", output, mode, target], scratch);
        var bynArgv = File.Exists(argv) ? File.ReadAllText(argv).ReplaceLineEndings("\n") : "";
        if (cs.Exit == 0) Assert.True(File.Exists(csOut));
        if (byn.Exit == 0) Assert.True(File.Exists(Path.Combine(scratch, output)));
        Assert.Equal(cs.Exit == 0, byn.Exit == 0);
        return ((cs.Exit, cs.Err), (byn.Exit, byn.Err), Masked(csArgv, scratch), Masked(bynArgv, scratch));
    }

    public static TheoryData<string, string> Matrix()
    {
        var data = new TheoryData<string, string>();
        foreach (var mode in new[] { "debug", "safe", "fast" })
        {
            data.Add(mode, "");
            foreach (var target in Targets.All) data.Add(mode, target);
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
        Environment.SetEnvironmentVariable("BAYAN_CC", Path.Combine(zig, Tool("zig")) + " cc");
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

    /// <summary>
    /// No target (null) for <c>ccArguments</c>: the C# never has it on a host with a target (<c>Build</c> resolves it), where it is the
    /// host's own argument list, so the oracle is <c>bayan build</c> without <c>--target</c>.
    /// </summary>
    [Theory]
    [InlineData("debug")]
    [InlineData("safe")]
    public void ArgumentsWithoutATargetAreTheHostsAsToolchain(string mode)
    {
        var bin = Bin("bin-null-args", Recording, "cc");
        var scratch = Fresh($"null-args-{mode}");
        var argv = Path.Combine(bin, "argv");
        File.Delete(argv);
        Assert.Equal(0, Start(Cli, bin, ["build", Hello, "-o", Path.Combine(scratch, "cs-out"), "--mode", mode], scratch).Exit);
        var expected = Masked(File.ReadAllText(argv).ReplaceLineEndings("\n"), scratch).Split('\n')[1..];
        var (exit, stdout, _) = Start(Probe(), bin, ["arguments", scratch, Targets.ExecutableName("program", Targets.Host ?? ""), mode], scratch);
        Assert.Equal(0, exit);
        SelfhostEmitterHarness.AssertSame($"arguments of {mode} without a target", string.Join("\n", expected), Masked(stdout, scratch));
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

    /// <summary>A <c>cc</c> that exists and cannot be started: the C# <c>Win32Exception</c> text (Toolchain.cs:215-217) without the OS message.</summary>
    [Fact]
    public void ACompilerThatCannotBeStartedIsReportedAsToolchain()
    {
        var plain = Fresh("bin-no-exec");
        File.WriteAllText(Path.Combine(plain, Tool("cc")), "#!/bin/sh\nexit 0\n");
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(Path.Combine(plain, "cc"), UnixFileMode.UserRead | UnixFileMode.UserWrite);
        var cs = Start(Cli, plain, ["build", Hello, "-o", Path.Combine(Fresh("no-exec"), "cs-out")]);
        Assert.Equal(4, cs.Exit);
        Assert.StartsWith($"bayan: cannot run the C compiler `{Path.Combine(plain, Tool("cc"))}`: ", cs.Err);
        var (exit, _, err) = Start(Probe(), plain, ["compile", Fresh("no-exec-byn"), "int main(void){return 0;}", "program", "safe", ""]);
        Assert.Equal(4, exit);
        Assert.Equal(cs.Err[..(cs.Err.IndexOf("`:", StringComparison.Ordinal) + 1)] + "\n", err);
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

    /// <summary>
    /// No target at all (null) on a host that has one: <c>Build</c> never passes it (<c>target ?? Targets.Host</c>), so the oracle is the
    /// private <c>BuildCached</c>, where it is a cross build (Toolchain.cs:184) of an unknown target (:190), whatever the compiler.
    /// </summary>
    [Theory]
    [InlineData("zig cc")]
    [InlineData("cc")]
    public void NoTargetIsAnUnknownCrossBuildAsToolchain(string compiler)
    {
        if (Targets.Host is null) { Assert.Fail("this machine has no Bayan target, so a null target is not a cross build here"); return; }
        var buildCached = typeof(Toolchain).GetMethod("BuildCached", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(buildCached);
        var saved = Environment.GetEnvironmentVariable("BAYAN_CC");
        Environment.SetEnvironmentVariable("BAYAN_CC", compiler);
        NativeBuild expected;
        try { expected = (NativeBuild)buildCached!.Invoke(null, ["int main(void){return 0;}", BuildMode.Safe, null, null, false])!; }
        finally { Environment.SetEnvironmentVariable("BAYAN_CC", saved); }
        Assert.StartsWith("unknown target ``; targets are linux-x64, ", expected.Errors);
        var (exit, stdout, _) = Start(Probe(), Fresh("bin-null-target"), ["refusal", "-", .. compiler.Split(' ')]);
        Assert.Equal(0, exit);
        SelfhostEmitterHarness.AssertSame($"refusal for no target with {compiler}", expected.Errors + "\n", stdout);
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

    /// <summary>The host's own name is no cross build: plain <c>cc</c>, no <c>-target</c> (Toolchain.cs:184).</summary>
    [Fact]
    public void TheHostNameBuildsWithPlainCc()
    {
        if (Targets.Host is not { } host) { Assert.Fail("this machine has no Bayan target, so the host-name case cannot run"); return; }
        var bin = Bin("bin-host-name", Recording, "cc");
        var r = Both(bin, "safe", host, "host-name");
        Assert.Equal(0, r.Cs.Exit);
        Assert.DoesNotContain("-target", r.CsArgv);
        SelfhostEmitterHarness.AssertSame("argv of the host name", r.CsArgv, r.BynArgv);
    }

    [Fact]
    public void TargetHostAsTargetsHost()
    {
        var (exit, stdout, _) = Start(Probe(), Fresh("bin-host"), ["host"]);
        Assert.Equal(0, exit);
        Assert.Equal((Targets.Host ?? "none") + "\n", stdout);
    }

    /// <summary>Every name of the table is what its <c>Target.os</c> and <c>Target.arch</c> spell; no os is no host.</summary>
    [Fact]
    public void TargetHostFromIsTheTableName()
    {
        foreach (var name in Targets.All)
        {
            var parts = name == "wasm32" ? new[] { "wasi", "wasm32" } : name.Split('-');
            var (exit, stdout, _) = Start(Probe(), Fresh("bin-hostfrom"), ["hostfrom", parts[0], parts[1]]);
            Assert.Equal(0, exit);
            Assert.Equal(name + "\n", stdout);
        }
        Assert.Equal("none\n", Start(Probe(), Fresh("bin-hostfrom"), ["hostfrom", "", ""]).Out);
    }

    /// <summary>
    /// <c>FindOnPath</c> as the C#, seen through the compiler it picks: a directory called <c>cc</c> and an empty entry come first
    /// and are skipped, and the first directory holding the file wins.
    /// </summary>
    [Fact]
    public void FindOnPathSkipsDirectoriesAndEmptyEntriesAsToolchain()
    {
        var root = Fresh("bin-path");
        var first = Path.Combine(root, "first");
        Directory.CreateDirectory(Path.Combine(first, Tool("cc")));
        var second = Path.Combine(root, "second");
        Directory.CreateDirectory(second);
        Script(Path.Combine(second, "cc"), Recording);
        var third = Path.Combine(root, "third");
        Directory.CreateDirectory(third);
        Script(Path.Combine(third, "cc"), Recording);
        var path = string.Join(Path.PathSeparator, first, "", second, third);
        var csScratch = Fresh("path-cs");
        var cs = Start(Cli, path, ["build", Hello, "-o", Path.Combine(csScratch, "out")], csScratch);
        Assert.Equal(0, cs.Exit);
        Assert.True(File.Exists(Path.Combine(second, "argv")));
        Assert.False(File.Exists(Path.Combine(third, "argv")));
        var byn = Start(Probe(), path, ["compile", Fresh("path-byn"), "int main(void){return 0;}", "program", "safe", ""], Fresh("path-byn-cwd"));
        Assert.Equal(0, byn.Exit);
        Assert.StartsWith("cc\n", File.ReadAllText(Path.Combine(second, "argv")).ReplaceLineEndings("\n"));
        var found = Start(Probe(), path, ["findpath", OperatingSystem.IsWindows() ? "windows" : "linux", "cc"]);
        Assert.Equal(Path.Combine(second, Tool("cc")) + "\n", found.Out);
    }

    /// <summary>
    /// The Windows lookup: <c>;</c> separates the directories and the extensions are <c>.exe</c>, <c>.cmd</c>, <c>.bat</c> in that order
    /// (Toolchain.cs:153). No C# path reaches this on this machine's OS, so the expected paths are typed.
    /// </summary>
    [Fact]
    public void FindOnPathOnWindowsTriesTheExtensions()
    {
        // no-oracle: Q1008 the C# reads OperatingSystem.IsWindows(), false here
        var root = Fresh("bin-path-windows");
        var a = Path.Combine(root, "a");
        var b = Path.Combine(root, "b");
        Directory.CreateDirectory(a);
        Directory.CreateDirectory(b);
        File.WriteAllText(Path.Combine(a, "tool.bat"), "");
        File.WriteAllText(Path.Combine(a, "tool.cmd"), "");
        File.WriteAllText(Path.Combine(b, "tool.exe"), "");
        var path = $"{b};{a};";
        Assert.Equal(Path.Combine(b, "tool.exe") + "\n", Start(Probe(), path, ["findpath", "windows", "tool"]).Out);
        Assert.Equal(Path.Combine(a, "tool.cmd") + "\n", Start(Probe(), a + ";" + b, ["findpath", "windows", "tool"]).Out);
        Assert.Equal("none\n", Start(Probe(), path, ["findpath", "windows", "missing"]).Out);
        Assert.Equal("none\n", Start(Probe(), path, ["findpath", "linux", "tool"]).Out);
    }
}
