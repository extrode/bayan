using System.Text.Json.Nodes;
using Bayan.Cli;
using Bayan.Semantics;
using Bayan.Syntax.Text;

namespace Bayan.Tests;

/// <summary>
/// Spec 013 T26: a <c>BAYAN_COVERAGE</c> build of the selfhost harness counts every run of every build into one
/// <c>bayan-quality.json</c> of kind coverage and a <c>&lt;file&gt;.cov.txt</c> listing per file.
/// </summary>
public sealed class SelfhostCoverageTests
{
    private const string Lib = "i64 f(i64 a) {\n    if a == 1 {\n        return 1\n    }\n    return a + 1\n}\n";

    private static string Dir => Path.Combine(Golden.RepoRoot, "tmp", "selfhost-coverage");

    private static string LibPath => Path.Combine(Dir, "src", "lib.byn");

    /// <summary>A file compiled before <c>lib.byn</c> in the second build, so its counters shift <c>lib.byn</c>'s ids.</summary>
    private static string ExtraPath => Path.Combine(Dir, "src", "extra.byn");

    /// <summary><c>lib.byn</c> (after <c>extra.byn</c> when <paramref name="extra"/>) with a generated <c>main.byn</c>
    /// printing <c>f(<paramref name="a"/>)</c>.</summary>
    private static (Compilation C, HashSet<string> Generated) Program(int a, bool extra = false)
    {
        var main = SourceText.FromString(Path.Combine(Dir, "src", "main.byn"), $"print($\"{{f({a})}}\")\n");
        List<SourceText> sources = [SourceText.FromFile(LibPath), main];
        if (extra) sources.Insert(0, SourceText.FromFile(ExtraPath));
        var c = Compilation.Create(sources, Path.Combine(Dir, "src"));
        Assert.False(c.Diagnostics.HasErrors, string.Join("\n", c.Diagnostics.Items.Select(d => d.Message)));
        return (c, new HashSet<string>(StringComparer.Ordinal) { main.Path });
    }

    private static string BuildAndRun(int a, string? files = null, bool extra = false)
    {
        var (c, generated) = Program(a, extra);
        var build = SelfhostSemanticsHarness.BuildFast(c, generated, null, Path.Combine(Dir, "out"), files);
        Assert.True(build.Succeeded, build.Errors);
        var (exit, stdout, stderr) = SelfhostParserTests.Run(build.ExecutablePath!);
        Assert.True(exit == 0 && stderr.Length == 0, $"exited {exit}:\n{stderr}");
        return stdout;
    }

    /// <summary>
    /// A run of <c>f(2)</c> leaves line 3 and the true outcome of line 2's condition missed and the rest covered; a second
    /// build (<c>extra.byn</c> first, so <c>lib.byn</c>'s counter ids differ) run with <c>f(1)</c> is merged into the same
    /// report, which then misses nothing in <c>lib.byn</c>; an unreadable counter file makes what nothing hit unknown. The
    /// generated <c>main.byn</c> has no counters, and a previous recording's report and listings are gone after the first
    /// build. Break-tests: merge by counter id instead of by what it counts; drop the wrapper's <c>BAYAN_COVERAGE=</c>;
    /// drop <c>generated</c> from the skip set; drop the reset's <c>*.cov.txt</c> delete; ignore MergeCounters' result.
    /// </summary>
    [Fact]
    public void ACoveredAndAnUncoveredLineAndBranchLandInTheReport()
    {
        if (OperatingSystem.IsWindows()) return;
        if (Directory.Exists(Dir)) Directory.Delete(Dir, recursive: true);
        Directory.CreateDirectory(Path.Combine(Dir, "src"));
        Directory.CreateDirectory(Path.Combine(Dir, "out", "old"));
        File.WriteAllText(LibPath, Lib);
        File.WriteAllText(ExtraPath, "i64 g() {\n    return 5\n}\n");
        File.WriteAllText(Path.Combine(Dir, "out", "bayan-quality.json"), "{}");
        File.WriteAllText(Path.Combine(Dir, "out", "old", "stale.byn.cov.txt"), "");
        var outDir = Path.Combine(Dir, "out");

        Assert.Equal("3\n", BuildAndRun(2, Path.GetRelativePath(Golden.RepoRoot, LibPath)));
        Assert.False(File.Exists(Path.Combine(outDir, "bayan-quality.json")), "the earlier report survived the first build");
        Assert.False(File.Exists(Path.Combine(outDir, "old", "stale.byn.cov.txt")), "the earlier listing survived the first build");
        SelfhostSemanticsHarness.WriteCoverage(outDir);
        var report = JsonNode.Parse(File.ReadAllText(Path.Combine(outDir, "bayan-quality.json")))!;
        Assert.Equal("coverage", (string?)report["kind"]);
        var file = Assert.Single(report["files"]!.AsArray())!;
        Assert.Equal(LibPath, (string?)file["path"]);
        Assert.Equal([3], file["lines"]!["missed"]!.AsArray().Select(n => (int)n!));
        Assert.Equal((2, 3), ((int)file["lines"]!["covered"]!, (int)file["lines"]!["total"]!));
        var branch = Assert.Single(file["branches"]!["missed"]!.AsArray())!;
        Assert.Equal((2, true), ((int)branch["line"]!, (bool)branch["outcome"]!));
        Assert.Equal((1, 2), ((int)file["branches"]!["covered"]!, (int)file["branches"]!["total"]!));
        var listing = File.ReadAllText(Path.Combine(outDir, CliApp.ListingName(Path.GetRelativePath(Golden.RepoRoot, LibPath)) + ".cov.txt"));
        Assert.Contains("+     if a == 1 {\n-         return 1\n", listing);

        Assert.Equal("1\n", BuildAndRun(1, extra: true));
        SelfhostSemanticsHarness.WriteCoverage(outDir);
        var files = JsonNode.Parse(File.ReadAllText(Path.Combine(outDir, "bayan-quality.json")))!["files"]!.AsArray().ToDictionary(f => (string)f!["path"]!);
        Assert.Equal([ExtraPath, LibPath], files.Keys.Order(StringComparer.Ordinal));
        file = files[LibPath]!;
        Assert.Equal((3, 3), ((int)file["lines"]!["covered"]!, (int)file["lines"]!["total"]!));
        Assert.Equal((2, 2), ((int)file["branches"]!["covered"]!, (int)file["branches"]!["total"]!));
        Assert.Equal([2], files[ExtraPath]!["lines"]!["missed"]!.AsArray().Select(n => (int)n!));

        File.WriteAllText(Path.Combine(outDir, "counters", "1", "BAYAN_COVERAGE.0"), "not a counter file\n");
        SelfhostSemanticsHarness.WriteCoverage(outDir);
        file = JsonNode.Parse(File.ReadAllText(Path.Combine(outDir, "bayan-quality.json")))!["files"]!.AsArray().Single(f => (string)f!["path"]! == ExtraPath)!;
        Assert.Equal((0, 1), (file["lines"]!["missed"]!.AsArray().Count, file["lines"]!["unknown"]!.AsArray().Count));
    }

    /// <summary>A <c>BAYAN_COVERAGE_FILES</c> entry matching no compiled source throws, as <c>BAYAN_SNAPSHOT_FILES</c> does;
    /// <c>BAYAN_SNAPSHOT</c> with <c>BAYAN_COVERAGE</c> is refused. Break-tests: drop the <c>variable</c> argument; drop
    /// the both-set check.</summary>
    [Fact]
    public void AnUnknownFileAndBothModesAreRefused()
    {
        Directory.CreateDirectory(Path.Combine(Dir, "src"));
        if (!File.Exists(LibPath)) File.WriteAllText(LibPath, Lib);
        var (c, generated) = Program(2);
        var out2 = Path.Combine(Golden.RepoRoot, "tmp", "selfhost-coverage-refused");
        var e = Assert.Throws<InvalidOperationException>(() => SelfhostSemanticsHarness.BuildFast(c, generated, null, out2, "tmp/nope.byn"));
        Assert.Equal($"BAYAN_COVERAGE_FILES: {Path.Combine(Golden.RepoRoot, "tmp", "nope.byn")} matches no compiled source", e.Message);
        e = Assert.Throws<InvalidOperationException>(() => SelfhostSemanticsHarness.BuildFast(c, generated, out2, out2, null));
        Assert.Equal("BAYAN_SNAPSHOT and BAYAN_COVERAGE are both set: one mode per run; unset one", e.Message);
    }
}
