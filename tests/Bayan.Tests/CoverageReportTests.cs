using System.Text.Json;
using Bayan.Cli;

namespace Bayan.Tests;

/// <summary>Spec 013 T10: <c>bayan test --coverage</c> report (R9, R12).</summary>
[Collection(nameof(ToolchainEnvironment))]
public sealed class CoverageReportTests
{
    private static string Project(string name, string main)
    {
        var dir = Path.Combine(Golden.RepoRoot, "tmp", "coverage-report", name);
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        Directory.CreateDirectory(dir);
        var app = Path.Combine(dir, "tally");
        Assert.Equal(CliApp.ExitOk, Run("new", app, "--vcs", "none").Exit);
        File.Delete(Path.Combine(app, "tests", "app.test.byn"));
        File.WriteAllText(Path.Combine(app, "src", "app", "main.byn"), main);
        return app;
    }

    private static (int Exit, string Out, string Err) Run(params string[] args)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var exit = CliApp.Run(args, stdout, stderr);
        return (exit, stdout.ToString().ReplaceLineEndings("\n"), stderr.ToString().ReplaceLineEndings("\n"));
    }

    private const string Main = """
        union error E {
            a
            b
        }

        i64 f(i64 n) fails E {
            fail .a if n < 0
            fail .b if n > 100
            return n
        }

        test "negative" {
            let r = f(-1) catch switch {
                E.a => 1,
                E.b => 2
            }
            assert(r == 1)
        }

        i64 unused() {
            return 7
        }

        print("x")

        """;

    [Fact]
    public void ARunBelowTheManifestMinimumFailsAndNamesTheBranch()
    {
        var app = Project("gate", Main);
        var manifest = Path.Combine(app, "package.byn");
        var text = File.ReadAllText(manifest);
        var at = text.LastIndexOf('}');
        File.WriteAllText(manifest, text[..at].TrimEnd().TrimEnd(',') + ",\n    test: Tests { branches: 100 }\n}\n");
        var r = Run("test", "--coverage", app);
        Assert.Equal(CliApp.ExitTestFailure, r.Exit);
        Assert.Matches(@"bayan: branches \d+(\.\d+)?% < 100%", r.Err);
        Assert.Matches(@"uncovered branch: \S*main\.byn:\d+:\d+ never (true|false)", r.Err);
    }

    [Fact]
    public void AnUntriggeredErrorVariantIsNamed()
    {
        var app = Project("untriggered", Main);
        var r = Run("test", "--coverage", app);
        Assert.True(r.Exit == CliApp.ExitOk, r.Out + r.Err);
        Assert.Contains("uncovered error path:", r.Out);
        Assert.Contains("f E.b", r.Out);
        Assert.DoesNotContain("f E.a", r.Out);
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(app, "out", "test", "bayan-quality.json")));
        Assert.Equal("coverage", json.RootElement.GetProperty("kind").GetString());
        var file = json.RootElement.GetProperty("files").EnumerateArray().Single(f => f.GetProperty("path").GetString()!.EndsWith("main.byn", StringComparison.Ordinal));
        var paths = file.GetProperty("errorPaths").EnumerateArray().ToDictionary(e => e.GetProperty("variant").GetString()!, e => e.GetProperty("hit").GetBoolean());
        Assert.True(paths["E.a"]);
        Assert.False(paths["E.b"]);
        Assert.True(json.RootElement.GetProperty("totals").GetProperty("lines").GetProperty("total").GetInt32() > 0);
        var listing = File.ReadAllText(Directory.EnumerateFiles(Path.Combine(app, "out", "test"), "main.byn.cov.txt", SearchOption.AllDirectories).Single());
        var rows = listing.Split('\n');
        Assert.Contains("+     fail .a if n < 0", rows);
        Assert.Contains("-     return 7", rows);
        Assert.Contains("-     return n", rows);
        Assert.Contains(file.GetProperty("lines").GetProperty("missed").EnumerateArray(), l => rows[l.GetInt32() - 1] == "-     return 7");
        Assert.Contains("-     fail .b if n > 100", rows);
        var missed = file.GetProperty("branches").GetProperty("missed").EnumerateArray().Where(b => rows[b.GetProperty("line").GetInt32() - 1].Contains("n > 100", StringComparison.Ordinal));
        Assert.Equal([false, true], missed.Select(b => b.GetProperty("outcome").GetBoolean()).Order());
    }

    [Fact]
    public void ATimedOutTestMakesItsLinesUnknown()
    {
        var app = Project("timeout", Main + "\ntest \"spin\" {\n    while true {\n    }\n}\n");
        var saved = Environment.GetEnvironmentVariable("BAYAN_TEST_TIMEOUT_MS");
        Environment.SetEnvironmentVariable("BAYAN_TEST_TIMEOUT_MS", "2000");
        try
        {
            Run("test", "--coverage", app);
        }
        finally
        {
            Environment.SetEnvironmentVariable("BAYAN_TEST_TIMEOUT_MS", saved);
        }
        var json = File.ReadAllText(Path.Combine(app, "out", "test", "bayan-quality.json"));
        using var doc = JsonDocument.Parse(json);
        var file = doc.RootElement.GetProperty("files").EnumerateArray().Single(f => f.GetProperty("path").GetString()!.EndsWith("main.byn", StringComparison.Ordinal));
        var rows = File.ReadAllText(Directory.EnumerateFiles(Path.Combine(app, "out", "test"), "main.byn.cov.txt", SearchOption.AllDirectories).Single()).Split('\n');
        Assert.Contains("~     return 7", rows);
        Assert.DoesNotContain(rows, r => r.StartsWith("- ", StringComparison.Ordinal));
        Assert.Equal(0, file.GetProperty("lines").GetProperty("missed").GetArrayLength());
        Assert.True(file.GetProperty("lines").GetProperty("unknown").GetArrayLength() > 0);
        Assert.True(file.GetProperty("errorPaths").EnumerateArray().Single(e => e.GetProperty("variant").GetString() == "E.b").GetProperty("unknown").GetBoolean());
    }

    [Fact]
    public void JsonCoverageStdoutIsOneJsonDocument()
    {
        var app = Project("json", Main);
        var r = Run("test", "--json", "--coverage", app);
        Assert.True(r.Exit == CliApp.ExitOk, r.Out + r.Err);
        using var json = JsonDocument.Parse(r.Out);
        Assert.Equal(JsonValueKind.Object, json.RootElement.ValueKind);
    }

    [Fact]
    public void StdModuleLinesAreNeitherReportedNorGated()
    {
        var app = Project("std", """
            import std.json

            json.JsonValue one() fails json.JsonError uses alloc {
                return (try json.Json.parse("1")).trustUnchecked() because "literal"
            }

            test "one" {
                let v = try one()
                assert(v is .number(_, _))
            }

            """);
        var manifest = Path.Combine(app, "package.byn");
        var text = File.ReadAllText(manifest);
        var at = text.LastIndexOf('}');
        File.WriteAllText(manifest, text[..at].TrimEnd().TrimEnd(',') + ",\n    test: Tests { lines: 50 }\n}\n");
        var r = Run("test", "--coverage", app);
        Assert.True(r.Exit == CliApp.ExitOk, r.Out + r.Err);
        Assert.DoesNotContain("<std>", r.Out);
        Assert.DoesNotContain("<std>", File.ReadAllText(Path.Combine(app, "out", "test", "bayan-quality.json")));
    }

    [Fact]
    public void ASubsetRunSkipsTheCoverageGateAndPrintsNoPercentages()
    {
        var app = Project("subset", Main + "\ntest \"other\" {\n    let r = f(5) catch switch {\n        E.a => 1,\n        E.b => 2\n    }\n    assert(r == 5)\n}\n");
        var manifest = Path.Combine(app, "package.byn");
        var text = File.ReadAllText(manifest);
        var at = text.LastIndexOf('}');
        File.WriteAllText(manifest, text[..at].TrimEnd().TrimEnd(',') + ",\n    test: Tests { lines: 100, branches: 100, errorPaths: 100 }\n}\n");
        var r = Run("test", "--coverage", "--only", "negative", app);
        Assert.True(r.Exit == CliApp.ExitOk, r.Out + r.Err);
        Assert.DoesNotContain("%", r.Out + r.Err);
        var rows = File.ReadAllText(Directory.EnumerateFiles(Path.Combine(app, "out", "test"), "main.byn.cov.txt", SearchOption.AllDirectories).Single()).Split('\n');
        Assert.Contains("~     return 7", rows);
    }

    [Fact]
    public void AnEmptyOrTruncatedCounterFileMakesCountersUnknown()
    {
        var dir = Path.Combine(Golden.RepoRoot, "tmp", "coverage-report", "counters");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "BAYAN_COVERAGE.0");
        foreach (var bad in new[] { "", "bayan-coverage 1\n3", "bayan-coverage 1\n3 x\n", "bayan-c" })
        {
            File.WriteAllText(path, bad);
            var hits = new Dictionary<int, ulong>();
            Assert.False(CliApp.MergeCounters(path, hits), bad);
            Assert.Empty(hits);
        }
        File.WriteAllText(path, "bayan-coverage 1\n3 2\n");
        var good = new Dictionary<int, ulong> { [3] = 1 };
        Assert.True(CliApp.MergeCounters(path, good));
        Assert.Equal(3UL, good[3]);
        Assert.True(CliApp.MergeCounters(Path.Combine(dir, "missing"), good));
    }

    [Fact]
    public void ListingNamesReplaceOnlyParentSegmentsAndStayUnderOutTest()
    {
        Assert.Equal("a..b.byn", CliApp.ListingName("a..b.byn"));
        Assert.NotEqual(CliApp.ListingName("a..b.byn"), CliApp.ListingName("a_b.byn"));
        Assert.Equal(Path.Combine("_", "_", "lib", "x.byn"), CliApp.ListingName(Path.Combine("..", "..", "lib", "x.byn")));
        Assert.Equal("x.byn", CliApp.ListingName(Path.Combine(Path.GetTempPath(), "elsewhere", "x.byn")));
    }
}
