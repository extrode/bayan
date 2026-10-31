using Bayan.Semantics;
using Bayan.Syntax.Text;
using Bayan.Testing;

namespace Bayan.Tests;

/// <summary>Spec 013 T7: the mutation report's JSON, terminal summary, per-operator totals, timing and warnings, over a hand-made result list.</summary>
public sealed class QualityReportTests
{
    private const string Path = "lib.test.byn";

    private const string Fixture = """
        i64 big(i64 n) {
            if n > 5 { return 1 }
            return 0
        }

        i64 hidden(i64 n) {
            var y = n
            @unobservable because "no test can see it"
            y += 0
            @unobservable because "nothing to mutate"
            var z = y
            return z
        }

        """;

    private static readonly SourceText Source = SourceText.FromString(Path, Fixture);

    private static (Compilation C, Schema Schema) Build()
    {
        var c = Compilation.Create([Source], ".", new CompilationOptions(Library: true));
        Assert.False(c.Diagnostics.HasErrors, string.Join("\n", c.Diagnostics.Items.Select(d => d.Message)));
        return (c, Schemata.Build(c));
    }

    private static MutationResult Results(Schema schema, Func<SchemaSite, MutantResult> status) =>
        new(schema.Sites.Select(status).ToList(), [], 0);

    private static MutantResult Outcome(SchemaSite s) => s.Site.Operator switch
    {
        Mutator.Boundary => new MutantResult(s, MutantStatus.Survived, 2, null),
        Mutator.Relational => new MutantResult(s, MutantStatus.Killed, 1, 0),
        _ when !s.Emitted => new MutantResult(s, MutantStatus.NotEmitted, 0, null),
        _ when s.Unobservable is not null => new MutantResult(s, MutantStatus.Marked, 0, null),
        _ => new MutantResult(s, MutantStatus.NotReached, 0, null),
    };

    private static QualityReport Report(Schema schema, MutationResult result) =>
        QualityReport.Mutation("bayan test --mutate", result, schema, 20, [0.5, 1.5], 25);

    [Fact]
    public void JsonMatchesTheGolden()
    {
        var (_, schema) = Build();
        var json = Report(schema, Results(schema, Outcome)).Json();
        var golden = System.IO.Path.Combine(Golden.RepoRoot, "tests", "cases", "quality", "mutation-report.json");
        if (Environment.GetEnvironmentVariable("BAYAN_UPDATE_GOLDEN") == "1")
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(golden)!);
            File.WriteAllText(golden, json);
        }
        Assert.Equal(File.ReadAllText(golden), json);
    }

    [Fact]
    public void JsonHasTheSchemaKeys()
    {
        var (_, schema) = Build();
        using var doc = System.Text.Json.JsonDocument.Parse(Report(schema, Results(schema, Outcome)).Json());
        var root = doc.RootElement;
        Assert.Equal(1, root.GetProperty("version").GetInt32());
        Assert.Equal("mutation", root.GetProperty("kind").GetString());
        Assert.Equal(["build_s", "mean_mutant_s", "max_mutant_s", "total_s", "mutant_share_of_build"],
            root.GetProperty("timing").EnumerateObject().Select(p => p.Name));
        var mutant = root.GetProperty("files")[0].GetProperty("mutants").EnumerateArray().First(m => m.GetProperty("status").GetString() == "survived");
        Assert.True(mutant.TryGetProperty("suggestion", out _));
        Assert.False(mutant.TryGetProperty("killedBy", out _));
    }

    [Fact]
    public void SummaryNamesEachSurvivorWithSuggestion()
    {
        var (_, schema) = Build();
        var summary = Report(schema, Results(schema, Outcome)).Summary([]);
        Assert.Contains("survived: lib.test.byn:2:8: n > 5 → n >= 5 [boundary]\n  suggestion: add a case at the boundary value\n", summary);
    }

    [Fact]
    public void PerOperatorTotalsAddUp()
    {
        var (_, schema) = Build();
        var result = Results(schema, Outcome);
        var report = Report(schema, result);
        foreach (var op in Mutator.Operators)
        {
            var sites = result.Mutants.Where(m => m.Site.Site.Operator == op && m.Status != MutantStatus.NotEmitted).ToList();
            Assert.Equal(sites.Count(m => m.Status == MutantStatus.Survived), report.Operators[op]["survived"]);
            Assert.Equal(sites.Count(m => m.Status == MutantStatus.Killed), report.Operators[op]["killed"]);
            Assert.Equal(sites.Count(m => m.Status == MutantStatus.NotReached), report.Operators[op]["not_reached"]);
        }
        Assert.Equal(Mutator.Operators.Sum(o => report.Operators[o]["survived"]), report.Totals["survived"]);
        Assert.True(report.Totals["survived"] > 0 && report.Totals["killed"] > 0 && report.Totals["marked"] > 0);
        Assert.Contains("  boundary: 0 killed, 1 survived,", report.Summary([]));
    }

    [Fact]
    public void TimingIsMeanMaxTotalAndShareOfBuild()
    {
        var (_, schema) = Build();
        var report = Report(schema, Results(schema, Outcome));
        Assert.Equal(new ReportTiming(20, 1, 1.5, 25, 0.05), report.Timing);
        Assert.Contains("timing: build 20 s, mean 1 s and max 1.5 s per mutant, total 25 s, mutant share of build 5%\n", report.Summary([]));
    }

    [Fact]
    public void AKilledMarkedMutantIsAWarning()
    {
        var (c, schema) = Build();
        var result = Results(schema, s => s.Unobservable is not null && s.Emitted ? new MutantResult(s, MutantStatus.Killed, 1, 3) : Outcome(s));
        var warnings = QualityReport.Warnings(result, schema, c, [Source]);
        Assert.Contains(warnings, w => w.StartsWith("warning: lib.test.byn:9:", StringComparison.Ordinal) && w.Contains("the mark is wrong: a test sees this"));
        Assert.DoesNotContain(QualityReport.Warnings(Results(schema, Outcome), schema, c, [Source]), w => w.Contains("the mark is wrong"));
    }

    [Fact]
    public void AMarkWithNoMutantIsAWarning()
    {
        var (c, schema) = Build();
        var warnings = QualityReport.Warnings(Results(schema, Outcome), schema, c, [Source]);
        var w = Assert.Single(warnings);
        Assert.Equal("warning: lib.test.byn:10:5: the mark covers no mutant (`nothing to mutate`)", w);
        Assert.Contains(w, Report(schema, Results(schema, Outcome)).Summary(warnings));
    }
}
