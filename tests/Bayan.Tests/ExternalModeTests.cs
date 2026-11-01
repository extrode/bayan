using Bayan.Cli;
using Bayan.Semantics;
using Bayan.Syntax.Text;
using Bayan.Testing;

namespace Bayan.Tests;

/// <summary>Spec 013 T8: <c>bayan mutate --binary</c> and <c>bayan test --mutate --run</c> run a command once per site with <c>BAYAN_MUTANT</c> set.</summary>
public sealed class ExternalModeTests
{
    private const string Sites = """
        { "version": 1, "sites": [
          { "id": 1, "file": "a.byn", "line": 1, "column": 1, "operator": "arithmetic", "original": "a + b", "mutated": "a - b" },
          { "id": 2, "file": "a.byn", "line": 2, "column": 1, "operator": "arithmetic", "original": "a * b", "mutated": "a / b" },
          { "id": 3, "file": "a.byn", "line": 3, "column": 1, "operator": "bool", "original": "true", "mutated": "false", "emitted": false },
          { "id": 4, "file": "a.byn", "line": 4, "column": 1, "operator": "arithmetic", "original": "a + 0", "mutated": "a - 0", "unobservable": "no test can see it" },
          { "id": 5, "file": "a.byn", "line": 5, "column": 1, "operator": "statement", "original": "i += 1", "mutated": "" }
        ] }
        """;

    private sealed record Outcome(int Exit, string Out, string Err, string Dir);

    private static string Fresh(string name)
    {
        var dir = Path.Combine(Golden.RepoRoot, "tmp", name);
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static Outcome Mutate(string name, string command, string sites = Sites)
    {
        var dir = Fresh(name);
        var binary = Path.Combine(dir, "prog");
        File.WriteAllText(binary, "");
        File.WriteAllText(binary + ".sites.json", sites);
        return Cli(dir, "mutate", "--binary", binary, "--sites", binary + ".sites.json", "--", "/bin/sh", "-c", command);
    }

    private static Outcome Cli(string dir, params string[] args)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var exit = CliApp.Run(args, stdout, stderr);
        return new Outcome(exit, stdout.ToString(), stderr.ToString(), dir);
    }

    [Fact]
    public void ExitCodesAndTimeoutsDecideTheStatus()
    {
        if (OperatingSystem.IsWindows()) return;
        var r = Mutate("external-mixed", "case $BAYAN_MUTANT in 2) exit 1;; 5) sleep 30;; esac");
        Assert.Equal(0, r.Exit);
        Assert.Contains("a.byn:1:1 arithmetic `a + b` -> `a - b`: survived", r.Out);
        Assert.Contains("a.byn:2:1 arithmetic `a * b` -> `a / b`: killed", r.Out);
        Assert.Contains("a.byn:3:1 bool `true` -> `false`: not emitted", r.Out);
        Assert.Contains("a.byn:4:1 arithmetic `a + 0` -> `a - 0`: marked", r.Out);
        Assert.Contains("a.byn:5:1 statement `i += 1` -> ``: timeout (counted as killed)", r.Out);
        Assert.Contains("mutation: 1 killed, 1 survived, 1 timeout, 1 marked, 0 not reached, 1 not emitted", r.Out);
        Assert.Contains("suggestion:", r.Out);
        var json = File.ReadAllText(Path.Combine(r.Dir, "bayan-quality.json"));
        Assert.Contains("\"kind\": \"mutation\"", json);
        Assert.Contains("\"tool\": \"bayan mutate\"", json);
        Assert.Contains("\"survived\": 1", json);
    }

    [Fact]
    public void EachEmittedSiteRunsOnceWithItsId()
    {
        if (OperatingSystem.IsWindows()) return;
        var dir = Fresh("external-log");
        var log = Path.Combine(dir, "log.txt");
        var binary = Path.Combine(dir, "prog");
        File.WriteAllText(binary, "");
        File.WriteAllText(binary + ".sites.json", Sites);
        var r = Cli(dir, "mutate", "--binary", binary, "--sites", binary + ".sites.json", "--", "/bin/sh", "-c", $"echo $BAYAN_MUTANT >> '{log}'; [ \"$BAYAN_MUTANT\" != 5 ] || exit 1");
        Assert.Equal(0, r.Exit);
        Assert.Equal(["0", "1", "2", "4", "5"], File.ReadAllLines(log));
    }

    [Fact]
    public void AllSurvivedIsReportedAsSuch()
    {
        if (OperatingSystem.IsWindows()) return;
        var r = Mutate("external-survived", "exit 0");
        Assert.Equal(0, r.Exit);
        Assert.Contains("mutation: 0 killed, 3 survived, 0 timeout, 1 marked, 0 not reached, 1 not emitted", r.Out);
        Assert.Equal(3, r.Out.Split('\n').Count(l => l.StartsWith("survived:", StringComparison.Ordinal)));
    }

    [Fact]
    public void ACommandThatFailsUnmutatedExitsOneAndRunsNoMutant()
    {
        if (OperatingSystem.IsWindows()) return;
        var dir = Fresh("external-baseline");
        var log = Path.Combine(dir, "log.txt");
        var binary = Path.Combine(dir, "prog");
        File.WriteAllText(binary, "");
        File.WriteAllText(binary + ".sites.json", Sites);
        var r = Cli(dir, "mutate", "--binary", binary, "--sites", binary + ".sites.json", "--", "/bin/sh", "-c", $"echo $BAYAN_MUTANT >> '{log}'; exit 3");
        Assert.Equal(1, r.Exit);
        Assert.Contains("unmutated", r.Err);
        Assert.Equal(["0"], File.ReadAllLines(log));
        Assert.DoesNotContain("a.byn", r.Out);
        Assert.False(File.Exists(Path.Combine(dir, "bayan-quality.json")));
    }

    [Fact]
    public void MissingBinarySitesOrCommandIsAUsageError()
    {
        var dir = Fresh("external-usage");
        var binary = Path.Combine(dir, "prog");
        File.WriteAllText(binary, "");
        File.WriteAllText(binary + ".sites.json", Sites);
        var sites = binary + ".sites.json";
        foreach (var args in new[]
        {
            new[] { "mutate", "--sites", sites, "--", "true" },
            new[] { "mutate", "--binary", binary, "--", "true" },
            new[] { "mutate", "--binary", binary, "--sites", sites },
            new[] { "mutate", "--binary", binary, "--sites", sites, "--" },
        })
        {
            var r = Cli(dir, args);
            Assert.Equal(2, r.Exit);
            Assert.Contains("usage: bayan mutate", r.Err);
        }
    }

    [Fact]
    public void ASiteMapThatIsNotJsonOrHasTheWrongShapeIsAUsageError()
    {
        foreach (var (name, sites) in new[]
        {
            ("external-notjson", "not json"),
            ("external-shape-sites", "{ \"version\": 1, \"sites\": 3 }"),
            ("external-shape-field", "{ \"version\": 1, \"sites\": [ { \"id\": \"one\" } ] }"),
            ("external-shape-missing", "{ \"version\": 1, \"sites\": [ { \"id\": 1 } ] }"),
            ("external-shape-null-file", "{ \"version\": 1, \"sites\": [ { \"id\": 1, \"file\": null, \"line\": 1, \"column\": 1, \"operator\": \"bool\", \"original\": \"a\", \"mutated\": \"b\" } ] }"),
            ("external-shape-null-operator", "{ \"version\": 1, \"sites\": [ { \"id\": 1, \"file\": \"a.byn\", \"line\": 1, \"column\": 1, \"operator\": null, \"original\": \"a\", \"mutated\": \"b\" } ] }"),
            ("external-shape-version", "{ \"version\": 1.5, \"sites\": [] }"),
        })
        {
            var r = Mutate(name, "exit 0", sites);
            Assert.Equal(2, r.Exit);
            Assert.Contains("is not a version 1 site map", r.Err);
        }
    }

    [Fact]
    public void ACommandThatCannotStartReportsTheBaselineFailure()
    {
        var dir = Fresh("external-nosuchcmd");
        var binary = Path.Combine(dir, "prog");
        File.WriteAllText(binary, "");
        File.WriteAllText(binary + ".sites.json", Sites);
        var r = Cli(dir, "mutate", "--binary", binary, "--sites", binary + ".sites.json", "--", "nosuchcmd-xyz");
        Assert.Equal(1, r.Exit);
        Assert.Contains("does not exit 0 on the unmutated binary", r.Err);
    }

    [Fact]
    public void APendingTestIsLeftOutOfMutateWithoutAWarning()
    {
        var dir = Fresh("external-pending");
        File.WriteAllText(Path.Combine(dir, "a.test.byn"), """
            i64 inc(i64 n) {
                return n + 1
            }

            test "inc" {
                assert inc(1) == 2
            }

            test "later" {
                todo("x")
            }

            """);
        var r = Cli(dir, "test", "--mutate", dir);
        Assert.True(r.Exit == 0, r.Err + r.Out);
        Assert.DoesNotContain("does not pass unmutated", r.Err);
        Assert.Contains(" killed, ", r.Out);
    }

    [Fact]
    public void MutateWithCoverageOrJsonIsAUsageError()
    {
        var dir = Fresh("external-mutate-flags");
        File.WriteAllText(Path.Combine(dir, "a.test.byn"), "test \"one\" {\n    assert(true)\n}\n");
        foreach (var flag in new[] { "--coverage", "--json" })
        {
            var r = Cli(dir, "test", "--mutate", flag, dir);
            Assert.Equal(CliApp.ExitUsage, r.Exit);
            Assert.Contains($"unexpected argument `{flag}`", r.Err);
        }
    }

    [Fact]
    public void TheSiteMapReaderRoundTripsSitesJson()
    {
        var dir = Fresh("external-roundtrip");
        const string source = """
            i64 inc(i64 n) {
                var y = n
                @unobservable because "no test can see it"
                y += 0
                return n + 1
            }

            test "inc" {
                assert inc(1) == 2
            }

            """;
        var c = Compilation.Create([SourceText.FromString(Path.Combine(dir, "lib.test.byn"), source)], dir, new CompilationOptions(IncludeTests: true, Library: true));
        Assert.False(c.Diagnostics.HasErrors);
        var schema = Schemata.Build(c);
        var read = ExternalSite.ReadAll(schema.SitesJson());
        Assert.NotNull(read);
        Assert.NotEmpty(read);
        Assert.Equal(schema.Sites.Select(ExternalSite.From), read);
        Assert.Contains(read, s => s.Unobservable == "no test can see it");
        Assert.Null(ExternalSite.ReadAll("{ \"version\": 2, \"sites\": [] }"));
    }

    [Fact]
    public void TestMutateRunRunsTheCommandThroughTheRealCli()
    {
        if (OperatingSystem.IsWindows()) return;
        var dir = Fresh("external-e2e");
        File.WriteAllText(Path.Combine(dir, "a.test.byn"), """
            i64 inc(i64 n) {
                return n + 1
            }

            test "inc" {
                assert inc(1) == 2
            }

            """);
        var r = Cli(dir, "test", "--mutate", "--run", "\"$BAYAN_BINARY\" --test 0 >/dev/null", dir);
        Assert.True(r.Exit == 0, r.Err + r.Out);
        Assert.Contains("arithmetic `n + 1` -> `n - 1`: killed", r.Out);
        Assert.Contains("mutation: 4 killed, 0 survived", r.Out);
        var json = File.ReadAllText(Path.Combine(dir, "out", "test", "bayan-quality.json"));
        Assert.Contains("\"tool\": \"bayan test --mutate --run\"", json);
    }
}
