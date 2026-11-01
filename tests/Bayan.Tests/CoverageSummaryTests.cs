using Bayan.Cli;

namespace Bayan.Tests;

/// <summary>Spec 013 T13: the default one-line coverage summary of a Debug <c>bayan test</c> (R17).</summary>
[Collection(nameof(ToolchainEnvironment))]
public sealed class CoverageSummaryTests
{
    private const string Main = """
        i64 f(i64 n) {
            if n < 0 {
                return 0
            }
            return n
        }

        test "f" {
            assert(f(1) == 1)
        }

        """;

    private static string Project(string name, bool summary = true)
    {
        var dir = Path.Combine(Golden.RepoRoot, "tmp", "coverage-summary", name);
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        Directory.CreateDirectory(dir);
        var app = Path.Combine(dir, "tally");
        Assert.Equal(CliApp.ExitOk, Run("new", app, "--vcs", "none").Exit);
        File.Delete(Path.Combine(app, "tests", "app.test.byn"));
        File.WriteAllText(Path.Combine(app, "src", "app", "main.byn"), Main);
        if (!summary)
        {
            var manifest = Path.Combine(app, "package.byn");
            var text = File.ReadAllText(manifest);
            var at = text.LastIndexOf('}');
            File.WriteAllText(manifest, text[..at].TrimEnd().TrimEnd(',') + ",\n    test: Tests { summary: false }\n}\n");
        }
        return app;
    }

    private static (int Exit, string Out, string Err) Run(params string[] args)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var exit = CliApp.Run(args, stdout, stderr);
        return (exit, stdout.ToString().ReplaceLineEndings("\n"), stderr.ToString().ReplaceLineEndings("\n"));
    }

    private static string Percentages(string text) =>
        text.Split('\n').Single(l => l.StartsWith("coverage: lines", StringComparison.Ordinal));

    private static string GeneratedC(string app, string mode) =>
        File.ReadAllText(Directory.EnumerateFiles(Path.Combine(app, "out", mode), "main.c", SearchOption.AllDirectories).Single());

    [Fact]
    public void DebugPrintsOneSummaryLineThatMatchesTheCoverageRun()
    {
        var app = Project("debug");
        var plain = Run("test", "--mode", "debug", app);
        Assert.True(plain.Exit == CliApp.ExitOk, plain.Out + plain.Err);
        Assert.Matches(@"coverage: lines \d+/\d+ \(\d+(\.\d+)?%\), branches \d+/\d+ \(\d+(\.\d+)?%\), error paths \d+/\d+ \(\d+(\.\d+)?%\)", plain.Out);
        Assert.Single(plain.Out.Split('\n'), l => l.StartsWith("coverage:", StringComparison.Ordinal));
        Assert.False(File.Exists(Path.Combine(app, "out", "test", "bayan-quality.json")));
        var full = Run("test", "--mode", "debug", "--coverage", app);
        Assert.Equal(Percentages(full.Out), Percentages(plain.Out));
    }

    [Fact]
    public void CoverageInDebugStillWritesTheReport()
    {
        var app = Project("report");
        var r = Run("test", "--mode", "debug", "--coverage", app);
        Assert.True(r.Exit == CliApp.ExitOk, r.Out + r.Err);
        Assert.True(File.Exists(Path.Combine(app, "out", "test", "bayan-quality.json")));
    }

    [Fact]
    public void SummaryFalseBuildsWithoutProbes()
    {
        var app = Project("off", summary: false);
        var r = Run("test", "--mode", "debug", app);
        Assert.True(r.Exit == CliApp.ExitOk, r.Out + r.Err);
        Assert.DoesNotContain("coverage:", r.Out);
        Assert.DoesNotContain("byn_cov[", GeneratedC(app, "debug"));
    }

    [Fact]
    public void SafeHasNoSummaryAndItsCIsTheProbeFreeC()
    {
        var app = Project("safe");
        var r = Run("test", app);
        Assert.True(r.Exit == CliApp.ExitOk, r.Out + r.Err);
        Assert.DoesNotContain("coverage:", r.Out);
        var safe = GeneratedC(app, "safe");
        Assert.DoesNotContain("byn_cov[", safe);
        var off = Project("safe-off", summary: false);
        Run("test", off);
        Assert.Equal(GeneratedC(off, "safe"), safe);
    }
}
