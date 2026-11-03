using Bayan.Backend.C;
using Bayan.Cli;
using Bayan.Semantics;

namespace Bayan.Tests;

/// <summary>
/// 011 T12 parity: <c>bayan-emit</c> against the C# compiler on the golden inputs (R1, R5). Error inputs compare
/// stderr and exit code with <c>bayan build --emit-c</c> today; the emission rows are skipped until T13 replaces
/// the stand-ins of T2-T11.
/// </summary>
public sealed class SelfhostEmitterParityTests
{
    private const string SkipUntilT13 = "bayan-emit prints C only once T13 has integrated T2-T11";

    public static TheoryData<string> RunCases => Golden.Cases("run");

    public static TheoryData<string> RunTestCases => Golden.Cases("run", ".test.byn");

    public static TheoryData<string> Examples
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var dir in Directory.GetDirectories(Path.Combine(Golden.RepoRoot, "examples")).Order(StringComparer.Ordinal))
                if (File.Exists(Path.Combine(dir, "main.byn"))) data.Add(Path.GetFileName(dir));
            return data;
        }
    }

    /// <summary>Every <c>run/</c> input in its mode, compiled alone with its directory as package root (RunTests.cs:80-82).</summary>
    [Theory(Skip = SkipUntilT13)]
    [MemberData(nameof(RunCases))]
    public void RunInputsEmitTheSameC(string name)
    {
        var input = Path.Combine(Golden.CasesRoot, name);
        var mode = SelfhostEmitterHarness.ModeOf(input);
        var c = Compilation.Create([Golden.Load(input)], Path.GetDirectoryName(input)!);
        if (c.Diagnostics.HasErrors) return;
        Toolchain.TryParseMode(mode, out var buildMode);
        var (exit, output, error) = SelfhostEmitterHarness.Run("--mode", mode, "--package", Path.GetDirectoryName(input)!, input);
        Assert.True(exit == 0, $"{name}: exit {exit}\n{error}");
        SelfhostEmitterHarness.AssertSame(name, CliApp.EmitC(c, buildMode), output);
    }

    /// <summary>Every <c>*.test.byn</c> with tests included.</summary>
    [Theory(Skip = SkipUntilT13)]
    [MemberData(nameof(RunTestCases))]
    public void RunTestInputsEmitTheSameC(string name)
    {
        var input = Path.Combine(Golden.CasesRoot, name);
        var c = Compilation.Create([Golden.Load(input)], Path.GetDirectoryName(input)!, new CompilationOptions(IncludeTests: true));
        if (c.Diagnostics.HasErrors) return;
        var (exit, output, error) = SelfhostEmitterHarness.Run("--package", Path.GetDirectoryName(input)!, "--tests", input);
        Assert.True(exit == 0, $"{name}: exit {exit}\n{error}");
        SelfhostEmitterHarness.AssertSame(name, CliApp.EmitC(c, BuildMode.Safe), output);
    }

    /// <summary>The inputs <c>CliTests.BuildEmitsC</c> and <c>CacheTests</c> pass to <c>--emit-c</c>, over every example project.</summary>
    [Theory(Skip = SkipUntilT13)]
    [MemberData(nameof(Examples))]
    public void ExamplesEmitTheSameC(string name)
    {
        var path = Path.Combine(Golden.RepoRoot, "examples", name, "main.byn");
        SelfhostEmitterHarness.SameAsCli(name, ["build", "--emit-c", path], [path]);
    }
}

/// <summary>The T12 rows that run today: <c>bayan-emit</c>'s error inputs and CLI shape against the C# (the guard's harness filter).</summary>
public sealed class SelfhostEmitterHarnessTests
{
    public static TheoryData<string> DiagCases => Golden.Cases("diag");

    public static TheoryData<string> CorpusCases
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var kind in new[] { "lex", "parse", "typed", "diag-json" })
                foreach (var name in Golden.Cases(kind)) data.Add(name);
            return data;
        }
    }

    public static TheoryData<string> ManifestCases => Golden.Cases("manifest");

    private static void SameOnFailure(string name, string copy, params string[] options)
    {
        if (CliApp.Run(["check", copy], new StringWriter(), new StringWriter()) == CliApp.ExitOk) return;
        SelfhostEmitterHarness.SameAsCli(name, ["build", "--emit-c", copy, .. options], [copy, .. options]);
    }

    /// <summary>
    /// A diag input the C# compiler rejects: <c>CliApp.Run(["build", "--emit-c", copy])</c> against <c>bayan-emit copy</c>, stderr and exit
    /// (R1 errors). An input the C# accepts reaches the emitter proper and belongs to the skipped rows.
    /// </summary>
    [Theory]
    [MemberData(nameof(DiagCases))]
    public void ErrorInputsAgree(string name) => SameOnFailure(name, SelfhostEmitterHarness.Isolate(name));

    /// <summary>The lexer, parser, typed and JSON-diagnostic goldens that the C# rejects, with the same comparison.</summary>
    [Theory]
    [MemberData(nameof(CorpusCases))]
    public void CorpusErrorInputsAgree(string name) => SameOnFailure(name, SelfhostEmitterHarness.Isolate(name));

    /// <summary><c>--diagnostics json</c> and <c>--all</c> on every diag input.</summary>
    [Theory]
    [MemberData(nameof(DiagCases))]
    public void DiagnosticOptionsAgree(string name)
    {
        var copy = SelfhostEmitterHarness.Isolate(name);
        SameOnFailure($"{name} --diagnostics json", copy, "--diagnostics", "json");
        SameOnFailure($"{name} --all", copy, "--all");
    }

    /// <summary>A manifest golden as the <c>package.byn</c> of a project with an entry module: its diagnostics are reported before anything compiles.</summary>
    [Theory]
    [MemberData(nameof(ManifestCases))]
    public void ManifestErrorsAgree(string name)
    {
        var dir = Path.Combine(Golden.RepoRoot, "tmp", "t12-tests", "manifest-" + Path.GetFileNameWithoutExtension(name));
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        Directory.CreateDirectory(Path.Combine(dir, "src", "app"));
        File.Copy(Path.Combine(Golden.CasesRoot, name), Path.Combine(dir, "package.byn"));
        File.WriteAllText(Path.Combine(dir, "src", "app", "main.byn"), "void main() {\n    print(\"a\")\n}\n");
        SameOnFailure(name, dir);
    }

    /// <summary>A project without its entry module, an app and a library; a script with an error; the cap on reported errors.</summary>
    [Fact]
    public void ProjectAndScriptErrorsAgree()
    {
        var root = Path.Combine(Golden.RepoRoot, "tmp", "t12-tests", "projects");
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        foreach (var (dir, manifest) in new[] { ("app", "name: app\nkind: app\n"), ("lib", "name: lib\nkind: lib\n") })
        {
            Directory.CreateDirectory(Path.Combine(root, dir));
            File.WriteAllText(Path.Combine(root, dir, "package.byn"), manifest);
            SelfhostEmitterHarness.SameAsCli(dir, ["build", "--emit-c", Path.Combine(root, dir)], [Path.Combine(root, dir)]);
        }
        var script = Path.Combine(root, "script.byn");
        File.WriteAllText(script, "#!/usr/bin/env bayan\nlet x: i32 = \"s\"\n");
        SelfhostEmitterHarness.SameAsCli("script", ["build", "--emit-c", script], [script]);
        var many = Path.Combine(root, "many.byn");
        File.WriteAllText(many, string.Concat(Enumerable.Range(0, 30).Select(i => $"let a{i}: i32 = \"s\"\n")) + "\u00e9 \ttab\r\nlet y = 1\n");
        SelfhostEmitterHarness.SameAsCli("many errors", ["build", "--emit-c", many], [many]);
        SelfhostEmitterHarness.SameAsCli("many errors --all", ["build", "--emit-c", many, "--all"], [many, "--all"]);
        SelfhostEmitterHarness.SameAsCli("many errors --diagnostics text", ["build", "--emit-c", many, "--diagnostics", "text"], [many, "--diagnostics", "text"]);
        SelfhostEmitterHarness.SameAsCli("bad format", ["build", "--emit-c", many, "--diagnostics", "xml"], [many, "--diagnostics", "xml"]);
        SelfhostEmitterHarness.SameAsCli("second path", ["build", "--emit-c", many, script], [many, script]);
        SelfhostEmitterHarness.SameAsCli("no path", ["build", "--emit-c"], []);
        SelfhostEmitterHarness.SameAsCli("-- args", ["build", "--emit-c", many, "--", "x"], [many, "--", "x"]);
    }

    /// <summary><c>--time</c> and <c>--mode</c> on an input the C# rejects fail as <c>build --emit-c</c> does.</summary>
    [Fact]
    public void ModeAndTimeFailLikeBuildOnAnErrorInput()
    {
        var copy = SelfhostEmitterHarness.Isolate("diag/goto.byn");
        SelfhostEmitterHarness.SameAsCli("--mode fast", ["build", "--mode", "fast", "--emit-c", copy], ["--mode", "fast", copy]);
        SelfhostEmitterHarness.SameAsCli("--time", ["build", "--emit-c", copy], ["--time", copy]);
    }

    /// <summary>A flag with no value, or a stray argument beside <c>--many</c>, <c>--time</c> or <c>--package</c>, is usage (exit 2).</summary>
    [Fact]
    public void ModeFlagsWantTheirArguments()
    {
        // no-oracle: Q814
        foreach (var args in new[] { new[] { "--many", "/tmp", "x" }, ["--time", "/tmp", "x"], ["--package", "/tmp"], ["--package", "/tmp", "a", "b"] })
        {
            var (exit, output, error) = SelfhostEmitterHarness.Run(args);
            Assert.True(exit == 2 && output.Length == 0 && error.StartsWith("usage: bayan-emit"), $"{string.Join(' ', args)}: exit {exit}\n{error}");
        }
        var missing = SelfhostEmitterHarness.Run("--many", "/nonexistent-bayan-dir");
        Assert.True(missing.Exit == 2 && missing.Err.StartsWith("usage: bayan-emit"), $"--many missing: exit {missing.Exit}");
        var gone = SelfhostEmitterHarness.Run("--package", "/tmp", "/nonexistent-bayan-dir/a.byn");
        Assert.True(gone.Exit == 1 && gone.Err.StartsWith("bayan: not found:"), $"--package missing file: exit {gone.Exit}\n{gone.Err}");
        // `--many` over a directory with one clean and one erroneous input prints `== <name>` for each and `exit 1` after the second.
        var many = SelfhostEmitterHarness.Isolate("diag/goto.byn");
        var (manyExit, manyOut, _) = SelfhostEmitterHarness.Run("--many", Path.GetDirectoryName(many)!);
        Assert.True(manyExit == 0 && manyOut.StartsWith("== goto.byn\n") && manyOut.TrimEnd().EndsWith("exit 1"), $"--many: exit {manyExit}\n{manyOut}");
    }

    /// <summary><c>--color</c> is consumed as <c>bayan</c> does, before the command.</summary>
    [Fact]
    public void GlobalOptionsFailLikeBuildOnAnErrorInput()
    {
        var copy = SelfhostEmitterHarness.Isolate("diag/goto.byn");
        SelfhostEmitterHarness.SameAsCli("--color never", ["--color", "never", "build", "--emit-c", copy], ["--color", "never", copy]);
        SelfhostEmitterHarness.SameAsCli("--color=never", ["--color=never", "build", "--emit-c", copy], ["--color=never", copy]);
        SelfhostEmitterHarness.SameAsCli("--color=blue", ["--color=blue", "build", "--emit-c", copy], ["--color=blue", copy]);
    }

    /// <summary>
    /// A clean input exits 0 on every path the C# exits 0 on (<c>build --emit-c</c>, a compilation without errors); the C text itself
    /// is compared in the rows skipped until T13, since it passes through the T2-T11 stand-ins.
    /// </summary>
    [Fact]
    public void CleanInputsExitLikeBuild()
    {
        var input = SelfhostEmitterHarness.Isolate("run/arithmetic.byn");
        var dir = Path.GetDirectoryName(input)!;
        Assert.Equal(CliApp.ExitOk, CliApp.Run(["build", "--emit-c", input], new StringWriter(), new StringWriter()));
        foreach (var args in new[] { new[] { input }, ["--mode", "debug", input], ["--package", dir, input], ["--time", input], ["--tests", input], ["--many", dir] })
        {
            var (exit, _, error) = SelfhostEmitterHarness.Run(args);
            Assert.True(exit == CliApp.ExitOk, $"{string.Join(' ', args)}: exit {exit}\n{error}");
        }
    }

    /// <summary>
    /// An input the C# rejects fails under <c>--package</c>, <c>--tests</c> and <c>--many</c> as <c>build --emit-c</c> and
    /// <c>test</c> do: stderr (first differing line), stdout and exit code.
    /// </summary>
    [Fact]
    public void PackageAndTestsFailLikeBuildOnAnErrorInput()
    {
        var copy = SelfhostEmitterHarness.Isolate("diag/goto.byn");
        var dir = Path.GetDirectoryName(copy)!;
        SelfhostEmitterHarness.SameAsCli("--package", ["build", "--emit-c", copy], ["--package", dir, copy], namedBy: dir);
        SelfhostEmitterHarness.SameAsCli("--tests", ["test", copy], ["--tests", copy]);
        var tests = SelfhostEmitterHarness.Isolate("diag/cases-type-mismatch.test.byn");
        SelfhostEmitterHarness.SameAsCli("test.byn --tests", ["test", tests], ["--tests", tests]);
        SelfhostEmitterHarness.SameAsCli("test.byn --package --tests", ["test", tests], ["--package", Path.GetDirectoryName(tests)!, "--tests", tests], namedBy: Path.GetDirectoryName(tests)!);
    }

    /// <summary>A flag without its value is an unexpected argument, an unknown mode and a missing input are the usage errors of <c>build --emit-c</c>.</summary>
    [Fact]
    public void BadArgumentsFailLikeBuild()
    {
        var copy = SelfhostEmitterHarness.Isolate("diag/goto.byn");
        foreach (var flag in new[] { "--mode", "--color", "--package", "--many", "--time" })
            SelfhostEmitterHarness.SameAsCli(flag, ["build", "--emit-c", copy, flag], [copy, flag]);
        SelfhostEmitterHarness.SameAsCli("--mode turbo", ["build", "--mode", "turbo", "--emit-c", copy], ["--mode", "turbo", copy]);
        var missing = Path.Combine(Path.GetDirectoryName(copy)!, "missing.byn");
        SelfhostEmitterHarness.SameAsCli("missing", ["build", "--emit-c", missing], [missing]);
        SelfhostEmitterHarness.SameAsCli("missing --time", ["build", "--emit-c", missing], ["--time", missing]);
    }

    /// <summary>A warning alone does not stop emission: exit 0 under every path, as <c>build --emit-c</c>; <c>--ascii</c> and a valid <c>--color</c> are consumed.</summary>
    [Fact]
    public void WarningsDoNotStopEmission()
    {
        var dir = Path.Combine(Golden.RepoRoot, "tmp", "t12-tests", "warning");
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "a.byn");
        File.WriteAllText(path, "void main() {\n    var x = 1\n    print($\"{x}\")\n}\n");
        Assert.Equal(CliApp.ExitOk, CliApp.Run(["build", "--emit-c", path], new StringWriter(), new StringWriter()));
        foreach (var args in new[] { new[] { "--ascii", path }, ["--color=always", path], ["--package", dir, path] })
        {
            var (exit, _, error) = SelfhostEmitterHarness.Run(args);
            Assert.True(exit == CliApp.ExitOk, $"{string.Join(' ', args)}: exit {exit}\n{error}");
        }
    }

    /// <summary><c>--many</c> skips a directory entry that is not a <c>.byn</c> file.</summary>
    [Fact]
    public void ManySkipsOtherFiles()
    {
        var input = SelfhostEmitterHarness.Isolate("run/arithmetic.byn");
        var dir = Path.GetDirectoryName(input)!;
        File.WriteAllText(Path.Combine(dir, "notes.txt"), "not a source\n");
        var (exit, output, _) = SelfhostEmitterHarness.Run("--many", dir);
        Assert.True(exit == 0 && output.StartsWith("== arithmetic.byn\n") && !output.Contains("notes.txt"), $"--many: exit {exit}\n{output}");
    }
}
