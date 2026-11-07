using System.Diagnostics;
using System.Text.Json.Nodes;
using Bayan.Semantics;
using Bayan.Syntax.Text;
using Xunit.Abstractions;

namespace Bayan.Tests;

/// <summary>
/// Spec 013 R14: a <c>BAYAN_SNAPSHOT</c> recording of selfhost tests replays through <c>scripts/selfhost/replay.py</c>
/// without <c>dotnet test</c>; mutant 0 matches every recorded run and a mutant the tests kill does not.
/// </summary>
[Collection(nameof(SelfhostSnapshot))]
public sealed class SelfhostSnapshotTests(ITestOutputHelper output)
{
    /// <summary>The recorded tests: <c>bayan-parse</c>'s CLI modes but the timing, tagged <c>Snapshot=skip</c>.</summary>
    private const string Filter = "FullyQualifiedName~SelfhostParserT13Tests&Snapshot!=skip";

    /// <summary>The file whose sites the schemata build carries: the CLI those tests drive (a whole-module schemata
    /// build re-binds the module once per site, over ten minutes for the parser).</summary>
    private const string Scope = "selfhost/parser/main.byn";

    [Fact]
    public void ReplayMatchesMutantZeroAndFailsOnAKilledMutant()
    {
        var clock = Stopwatch.StartNew();
        var (dir, runs) = Record("selfhost-snapshot", Filter, Scope);
        output.WriteLine($"record {clock.Elapsed.TotalSeconds:F1} s");
        var build = Assert.Single(JsonNode.Parse(File.ReadAllText(Path.Combine(dir, "manifest.json")))!["builds"]!.AsArray());
        Assert.All(runs, r => Assert.Equal((string?)build!["binary"], (string?)r["program"]));

        clock.Restart();
        var binary = (string)build!["binary"]!;
        var (exit, text) = Replay(dir, 0, binary);
        output.WriteLine($"replay of mutant 0: {clock.Elapsed.TotalSeconds:F2} s, {runs.Count} runs: {text}");
        Assert.True(exit == 0, text);

        var sites = JsonNode.Parse(File.ReadAllText((string)build!["sites"]!))!["sites"]!.AsArray();
        Assert.NotEmpty(sites);
        Assert.All(sites, s => Assert.Equal(Path.GetFullPath(Scope, Golden.RepoRoot), Path.GetFullPath((string)s!["file"]!, Golden.RepoRoot)));
        foreach (var site in sites.Take(50))
        {
            var (mutantExit, mutantText) = Replay(dir, (int)site!["id"]!, binary);
            if (mutantExit is 0 or -1) continue;
            output.WriteLine($"mutant {site["id"]} ({site["file"]}:{site["line"]}): {mutantText}");
            Assert.Equal(1, mutantExit);
            Assert.Contains("replay: run ", mutantText);
            return;
        }
        Assert.Fail("none of the first 50 mutants was killed by the recorded runs");
    }

    /// <summary>A test whose class builds its own program (T5's generated main) records against a schemata build:
    /// the manifest lists one build whose sites are all in <c>ctx.byn</c> (the module file with the fewest sites, so the
    /// per-site re-binding stays under a minute), every run used it, and mutant 0 replays.</summary>
    [Fact]
    public void OwnBuildCallerRecordsAgainstASchemataBuild()
    {
        const string scope = "selfhost/semantics/ctx.byn";
        var clock = Stopwatch.StartNew();
        var (dir, runs) = Record("selfhost-snapshot-t5", "FullyQualifiedName~SelfhostSemanticsT5Tests&Snapshot!=skip", scope);
        output.WriteLine($"record {clock.Elapsed.TotalSeconds:F1} s");
        var build = Assert.Single(JsonNode.Parse(File.ReadAllText(Path.Combine(dir, "manifest.json")))!["builds"]!.AsArray());
        var sites = JsonNode.Parse(File.ReadAllText((string)build!["sites"]!))!["sites"]!.AsArray();
        Assert.NotEmpty(sites);
        Assert.All(sites, s => Assert.Equal(Path.GetFullPath(scope, Golden.RepoRoot), Path.GetFullPath((string)s!["file"]!, Golden.RepoRoot)));
        Assert.All(runs, r => Assert.Equal((string?)build["binary"], (string?)r["program"]));
        var (exit, text) = Replay(dir, 0, null);
        Assert.True(exit == 0, text);
    }

    /// <summary>A program whose two recording runs differ (the shell's pid) fails the recording, naming the command
    /// and the trait that leaves such a test out.</summary>
    [Fact]
    public void DifferingRecordRunsFailTheRecording()
    {
        var dir = Path.Combine(Golden.RepoRoot, "tmp", "selfhost-snapshot-differ");
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        var e = Assert.Throws<InvalidOperationException>(() => SelfhostSemanticsHarness.RunRecorded(dir, SnapshotFixture.DifferProgram, null, SnapshotFixture.DifferArgs));
        Assert.Contains(SnapshotFixture.DifferCommand, e.Message);
        Assert.Contains("differ in stdout", e.Message);
        Assert.Contains("tag the test [Trait(\"Snapshot\", \"skip\")] if its output is not deterministic", e.Message);
        Assert.False(File.Exists(Path.Combine(dir, "runs.jsonl")));
    }

    /// <summary>A recording with two builds replays only the runs of the <c>--binary</c> build (site ids are per build);
    /// without <c>--binary</c> the replay refuses, naming both. Break-test (F1): drop replay.py's <c>--binary</c> filter.</summary>
    [Fact]
    public void ReplayRunsOnlyTheNamedBuild()
    {
        var dir = Path.Combine(Golden.RepoRoot, "tmp", "selfhost-snapshot-two-builds");
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        Directory.CreateDirectory(dir);
        var manifest = new JsonObject
        {
            ["version"] = 1,
            ["builds"] = new JsonArray(
                new JsonObject { ["binary"] = SnapshotFixture.EchoProgram, ["sites"] = "a" },
                new JsonObject { ["binary"] = SnapshotFixture.FalseProgram, ["sites"] = "b" }),
        };
        File.WriteAllText(Path.Combine(dir, "manifest.json"), manifest.ToJsonString());
        static string Run(string program, string[] args, string stdout) => new JsonObject
        {
            ["program"] = program, ["args"] = new JsonArray(args.Select(a => (JsonNode?)a).ToArray()), ["cwd"] = Golden.RepoRoot,
            ["stdin"] = null, ["stdout"] = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(stdout)), ["stderr"] = "", ["exit"] = 0,
        }.ToJsonString();
        File.WriteAllLines(Path.Combine(dir, "runs.jsonl"), [
            Run(SnapshotFixture.EchoProgram, SnapshotFixture.EchoArgs("hi"), SnapshotFixture.EchoStdout("hi")),
            Run(SnapshotFixture.FalseProgram, SnapshotFixture.FalseArgs, ""),
        ]);

        var (exit, text) = Replay(dir, 0, SnapshotFixture.EchoProgram);
        Assert.True(exit == 0, text);
        Assert.Contains("replay: 1 runs match", text);

        (exit, text) = Replay(dir, 0, null);
        Assert.Equal(2, exit);
        Assert.Contains($"{SnapshotFixture.EchoProgram}, {SnapshotFixture.FalseProgram}", text);
    }

    /// <summary>A second recording into a directory replaces the first: this process's first write deletes the earlier
    /// <c>manifest.json</c> and <c>runs.jsonl</c>, later writes add to them. Break-test (F2): drop the deletes in
    /// <c>ResetOnce</c>.</summary>
    [Fact]
    public void ASecondRecordingReplacesTheFirst()
    {
        var dir = Path.Combine(Golden.RepoRoot, "tmp", "selfhost-snapshot-twice");
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "manifest.json"), """{"version": 1, "builds": [{"binary": "/old", "sites": "/old.sites.json"}]}""");
        File.WriteAllText(Path.Combine(dir, "runs.jsonl"), """{"program": "/old", "args": [], "cwd": "/", "stdin": null, "stdout": "", "stderr": "", "exit": 0}""" + "\n");

        SelfhostSemanticsHarness.RecordBuild(dir, SnapshotFixture.EchoProgram, "/echo.sites.json");
        SelfhostSemanticsHarness.RunRecorded(dir, SnapshotFixture.EchoProgram, null, SnapshotFixture.EchoArgs("one"));
        SelfhostSemanticsHarness.RunRecorded(dir, SnapshotFixture.EchoProgram, null, SnapshotFixture.EchoArgs("two"));

        var build = Assert.Single(JsonNode.Parse(File.ReadAllText(Path.Combine(dir, "manifest.json")))!["builds"]!.AsArray());
        Assert.Equal(SnapshotFixture.EchoProgram, (string?)build!["binary"]);
        var runs = File.ReadAllLines(Path.Combine(dir, "runs.jsonl")).Select(l => JsonNode.Parse(l)!).ToList();
        Assert.Equal(["one", "two"], runs.Select(r => (string?)r["args"]!.AsArray().Last()));
    }

    /// <summary><c>BAYAN_SNAPSHOT_FILES</c> entries are trimmed, and one matching no compiled source throws naming it.
    /// Break-tests (F5): drop <c>TrimEntries</c>; drop the throw in <c>SnapshotSkip</c>.</summary>
    [Fact]
    public void SnapshotFilesAreTrimmedAndMustMatchACompiledSource()
    {
        string[] compiled = [Path.Combine(Golden.RepoRoot, "selfhost", "a.byn"), Path.Combine(Golden.RepoRoot, "selfhost", "b.byn")];
        var none = new HashSet<string>(StringComparer.Ordinal);
        Assert.Equal([compiled[1]], SelfhostSemanticsHarness.SnapshotSkip(compiled, none, " selfhost/a.byn , "));
        var e = Assert.Throws<InvalidOperationException>(() => SelfhostSemanticsHarness.SnapshotSkip(compiled, none, "selfhost/a.byn,selfhost/nope.byn"));
        Assert.Contains(Path.Combine(Golden.RepoRoot, "selfhost", "nope.byn") + " matches no compiled source", e.Message);
    }

    /// <summary>Q820: a listed file that exists but is not in this compilation (the guard lists every changed file to
    /// every build) is ignored for this build, so every compiled file is skipped; the compiled paths are the C# compilation's,
    /// as <c>BuildCovered</c> takes them. Break-test: drop <c>!File.Exists(k)</c>.</summary>
    [Fact]
    public void SnapshotFilesOnDiskButNotCompiledAreIgnored()
    {
        var dir = Path.Combine(Golden.RepoRoot, "tmp", "t13-tests", "snapshot-skip");
        Directory.CreateDirectory(dir);
        var main = Path.Combine(dir, "main.byn");
        File.WriteAllText(main, "i64 one() => 1\nprint($\"{one()}\")\n");
        var c = Compilation.Create([SourceText.FromFile(main)], dir);
        Assert.False(c.Diagnostics.HasErrors);
        var compiled = c.Modules.SelectMany(m => m.AllFunctions).Select(f => f.File?.Source.Path).OfType<string>().Distinct().ToList();
        Assert.Contains(main, compiled);
        var none = new HashSet<string>(StringComparer.Ordinal);
        Assert.True(File.Exists(Path.Combine(Golden.RepoRoot, "selfhost", "parser", "decls.byn")));
        var skip = SelfhostSemanticsHarness.SnapshotSkip(compiled, none, "selfhost/parser/decls.byn", root: c.DisplayRoot);
        Assert.Equal(compiled.Order(StringComparer.Ordinal), skip.Order(StringComparer.Ordinal));
    }

    /// <summary>An imported module's file is compiled under a path relative to the compilation's <c>DisplayRoot</c>;
    /// <c>SnapshotSkip</c> resolves it against the root it is given, not the working directory.
    /// Break-test: resolve with <c>Path.GetFullPath(p)</c> again.</summary>
    [Fact]
    public void RelativeModulePathsResolveAgainstTheRoot()
    {
        var root = Path.Combine(Golden.RepoRoot, "selfhost");
        string[] compiled = [Path.Combine(root, "emitter", "a.byn"), "emitter/cprint/common.byn"];
        var none = new HashSet<string>(StringComparer.Ordinal);
        Assert.Equal([compiled[0]], SelfhostSemanticsHarness.SnapshotSkip(compiled, none, "selfhost/emitter/cprint/common.byn", root: root));
    }

    /// <summary>A recording whose <c>dotnet test</c> fails (here every build throws on an unknown
    /// <c>BAYAN_SNAPSHOT_FILES</c> entry) is not a recording: <see cref="Record"/> fails on the exit code.
    /// Break-test (F3): drop <see cref="Record"/>'s exit-code assert.</summary>
    [Fact]
    public void AFailedRecordingFailsTheRecordHelper()
    {
        var e = Assert.Throws<Xunit.Sdk.TrueException>(() => Record("selfhost-snapshot-failed", Filter, "selfhost/parser/nope.byn"));
        Assert.Contains("recording exited 1", e.Message);
    }

    /// <summary>The nested recordings rewrite T13's and T5's fixed input files, so this class is in a collection that
    /// runs alone, after the parallel ones. Break-test (F4): drop the class's <c>[Collection]</c>, or
    /// <c>DisableParallelization</c> from its definition.</summary>
    [Fact]
    public void RunsAloneAfterTheParallelClasses()
    {
        var collection = Assert.Single(typeof(SelfhostSnapshotTests).GetCustomAttributesData(), a => a.AttributeType == typeof(CollectionAttribute));
        Assert.Equal(nameof(SelfhostSnapshot), (string?)collection.ConstructorArguments[0].Value);
        var definition = Assert.Single(typeof(SelfhostSnapshot).GetCustomAttributes(typeof(CollectionDefinitionAttribute), false).Cast<CollectionDefinitionAttribute>());
        Assert.True(definition.DisableParallelization);
    }

    /// <summary>Runs the tests matching <paramref name="filter"/> under <c>BAYAN_SNAPSHOT=tmp/&lt;name&gt;</c> with sites only in
    /// <paramref name="scope"/>; the directory and its recorded runs (at least one).</summary>
    private static (string Dir, List<JsonNode> Runs) Record(string name, string filter, string scope)
    {
        var dir = Path.Combine(Golden.RepoRoot, "tmp", name);
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        var dotnet = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var a in new[] { "test", typeof(SelfhostSnapshotTests).Assembly.Location, "--filter", filter }) dotnet.ArgumentList.Add(a);
        dotnet.Environment["BAYAN_SNAPSHOT"] = dir;
        dotnet.Environment["BAYAN_SNAPSHOT_FILES"] = scope;
        // one mode per run: a guard's coverage run must not reach the recording, or BuildFast refuses both (Q820)
        dotnet.Environment.Remove("BAYAN_COVERAGE");
        dotnet.Environment.Remove("BAYAN_COVERAGE_FILES");
        var (exit, text) = Exec(dotnet, TimeSpan.FromMinutes(10));
        Assert.True(exit == 0, $"recording exited {exit}:\n{text}");
        var runs = File.ReadAllLines(Path.Combine(dir, "runs.jsonl")).Select(l => JsonNode.Parse(l)!).ToList();
        Assert.NotEmpty(runs);
        return (dir, runs);
    }

    /// <summary>Replays <paramref name="dir"/> under <c>BAYAN_MUTANT=</c><paramref name="mutant"/>, with
    /// <c>--binary</c> <paramref name="binary"/> unless null.</summary>
    private static (int Exit, string Text) Replay(string dir, int mutant, string? binary)
    {
        var psi = new ProcessStartInfo("python3") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        psi.ArgumentList.Add("-I");
        psi.ArgumentList.Add(Path.Combine(Golden.RepoRoot, "scripts", "selfhost", "replay.py"));
        if (binary is not null) { psi.ArgumentList.Add("--binary"); psi.ArgumentList.Add(binary); }
        psi.ArgumentList.Add(dir);
        psi.Environment["BAYAN_MUTANT"] = mutant.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return Exec(psi, TimeSpan.FromSeconds(10));
    }

    /// <summary>Runs <paramref name="psi"/>; the exit code (-1 after a kill at <paramref name="limit"/>) and stdout plus stderr.</summary>
    private static (int Exit, string Text) Exec(ProcessStartInfo psi, TimeSpan limit)
    {
        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        if (!p.WaitForExit(limit))
        {
            p.Kill(entireProcessTree: true);
            p.WaitForExit();
            return (-1, "timed out");
        }
        return (p.ExitCode, stdout.Result + stderr.Result);
    }

    /// <summary>Programs and arguments for snapshot test fixtures across platforms.</summary>
    private static class SnapshotFixture
    {
        // cmd.exe's %RANDOM% is seeded from the clock in seconds, so two runs in one second print the same number (wp1).
        internal static readonly string DifferProgram = OperatingSystem.IsWindows() ? "powershell.exe" : "/bin/sh";
        internal static readonly string[] DifferArgs = OperatingSystem.IsWindows() ? ["-NoProfile", "-Command", "$PID"] : ["-c", "echo $$"];
        internal static string DifferCommand => $"{DifferProgram} {string.Join(' ', DifferArgs)}";

        internal static readonly string EchoProgram = OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/echo";
        internal static string[] EchoArgs(params string[] args) => OperatingSystem.IsWindows() ? ["/c", "echo", .. args] : args;
        internal static string EchoStdout(string text) => OperatingSystem.IsWindows() ? text + "\r\n" : text + "\n";

        internal static readonly string FalseProgram = OperatingSystem.IsWindows() ? Path.Combine(Environment.SystemDirectory, "cmd.exe") : "/usr/bin/false";
        internal static readonly string[] FalseArgs = OperatingSystem.IsWindows() ? ["/c", "exit", "1"] : [];
    }
}

/// <summary>Runs nested <c>dotnet test</c> recordings of T13 and T5, which rewrite those classes' fixed input files,
/// so it runs alone, after the parallel classes (spec 013 T14 review F4).</summary>
[CollectionDefinition(nameof(SelfhostSnapshot), DisableParallelization = true)]
public sealed class SelfhostSnapshot;
