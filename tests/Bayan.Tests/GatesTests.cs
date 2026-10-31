using Bayan.Cli;
using Bayan.Testing;

namespace Bayan.Tests;

/// <summary>Spec 013 T12: the manifest minimums (R13).</summary>
[Collection(nameof(ToolchainEnvironment))]
public sealed class GatesTests
{
    private static readonly IReadOnlyList<ReportErrorPath> NoPaths = [];

    private static QualityReport Cov(int[] covered, int[] missed, int[] unknown, ReportBranch[] branches, ReportErrorPath[] paths) =>
        new("coverage", "t", new ReportTiming(0, 0, 0, 0, 0), [new ReportFile("a.byn", [], new FileCoverage(covered, missed, unknown, branches, paths))],
            new Dictionary<string, int>(), new Dictionary<string, IReadOnlyDictionary<string, int>>(), new CoverageTotals(new Tally(0, 0), new Tally(0, 0), new Tally(0, 0)));

    private static QualityReport Mut(int killed, int timeout, int survived, int notReached) =>
        new("mutation", "t", new ReportTiming(0, 0, 0, 0, 0),
            [new ReportFile("a.byn", Enumerable.Range(0, survived).Select(i => new ReportMutant(new ExternalSite(i, "a.byn", 3 + i, 5, "arithmetic", "a + b", "a - b", true, null), "survived", null, null)).ToList())],
            new Dictionary<string, int> { ["killed"] = killed, ["timeout"] = timeout, ["survived"] = survived, ["not_reached"] = notReached, ["marked"] = 0, ["not_emitted"] = 0 },
            new Dictionary<string, IReadOnlyDictionary<string, int>>());

    [Fact]
    public void LinesAreBelowEqualAndAboveTheMinimum()
    {
        var r = Cov([1, 2, 3], [4], [], [], []);
        var below = Assert.Single(Gates.Coverage(r, 80, null, null));
        Assert.Equal(("lines", 75.0, 80), (below.Name, below.Percent, below.Min));
        Assert.Equal(["uncovered line: a.byn:4"], below.Missing);
        Assert.Empty(Gates.Coverage(r, 75, null, null));
        Assert.Empty(Gates.Coverage(r, 70, null, null));
    }

    [Fact]
    public void BranchesAreBelowEqualAndAboveTheMinimum()
    {
        var r = Cov([], [], [], [new(7, 9, true, "hit"), new(7, 9, false, "missed")], []);
        var below = Assert.Single(Gates.Coverage(r, null, 51, null));
        Assert.Equal(["uncovered branch: a.byn:7:9 never false"], below.Missing);
        Assert.Empty(Gates.Coverage(r, null, 50, null));
        Assert.Empty(Gates.Coverage(r, null, 49, null));
    }

    [Fact]
    public void ErrorPathsAreBelowEqualAndAboveTheMinimum()
    {
        var r = Cov([], [], [], [], [new("f", "E.a", true, false, []), new("f", "E.b", false, false, [])]);
        var below = Assert.Single(Gates.Coverage(r, null, null, 51));
        Assert.Equal("uncovered error path: f E.b (a.byn)", Assert.Single(below.Missing));
        Assert.Empty(Gates.Coverage(r, null, null, 50));
        Assert.Empty(Gates.Coverage(r, null, null, 10));
    }

    [Fact]
    public void AnAbsentMinimumAndAnEmptyMeasureNeverFail()
    {
        Assert.Empty(Gates.Coverage(Cov([], [1], [], [new(1, 1, true, "missed")], [new("f", "E.a", false, false, [])]), null, null, null));
        Assert.Empty(Gates.Coverage(Cov([], [], [], [], []), 100, 100, 100));
        Assert.Empty(Gates.Mutation(Mut(0, 0, 3, 0), null));
        Assert.Empty(Gates.Mutation(Mut(0, 0, 0, 0), 100));
    }

    [Fact]
    public void UnknownCountsAreLeftOutAndNotNamed()
    {
        var r = Cov([1], [], [2, 3], [new(1, 1, true, "hit"), new(1, 1, false, "unknown")], [new("f", "E.a", true, false, []), new("f", "E.b", false, true, [])]);
        Assert.Empty(Gates.Coverage(r, 100, 100, 100));
        var m = Cov([1], [4], [2, 3], [], []);
        Assert.Equal(["uncovered line: a.byn:4"], Assert.Single(Gates.Coverage(m, 100, null, null)).Missing);
    }

    [Fact]
    public void TheMutationScoreIsKilledAndTimeoutOverThoseAndSurvivors()
    {
        var r = Mut(2, 1, 1, 100);
        Assert.Empty(Gates.Mutation(r, 75));
        var below = Assert.Single(Gates.Mutation(r, 76));
        Assert.Equal(75.0, below.Percent);
        Assert.Equal("survived mutant: a.byn:3:5 arithmetic `a + b` -> `a - b`", Assert.Single(below.Missing));
    }

    private static string Project(string name, string tests)
    {
        var dir = Path.Combine(Golden.RepoRoot, "tmp", "gates", name);
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        Directory.CreateDirectory(dir);
        var app = Path.Combine(dir, "tally");
        Assert.Equal(CliApp.ExitOk, CliApp.Run(["new", app, "--vcs", "none"], new StringWriter(), new StringWriter()));
        var manifest = Path.Combine(app, "package.byn");
        var text = File.ReadAllText(manifest);
        File.WriteAllText(manifest, text[..text.LastIndexOf('}')].TrimEnd().TrimEnd(',') + $",\n    test: Tests {{ {tests} }}\n}}\n");
        return app;
    }

    [Fact]
    public void MutateBelowTheMinimumFailsTheTestRunAndNamesTheSurvivor()
    {
        if (OperatingSystem.IsWindows()) return;
        var app = Project("mutate-below", "mutation: 100");
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var exit = CliApp.Run(["test", "--mutate", "--run", "true", app], stdout, stderr);
        Assert.Equal(CliApp.ExitTestFailure, exit);
        Assert.Contains("bayan: mutation 0% < 100%", stderr.ToString());
        Assert.Matches(@"survived mutant: \S+:\d+:\d+ \w+ `.*` -> `.*`", stderr.ToString());
    }

    [Fact]
    public void MutateAtTheMinimumExitsZero()
    {
        if (OperatingSystem.IsWindows()) return;
        var app = Project("mutate-at", "mutation: 0");
        var exit = CliApp.Run(["test", "--mutate", "--run", "true", app], new StringWriter(), new StringWriter());
        Assert.Equal(CliApp.ExitOk, exit);
    }
}
