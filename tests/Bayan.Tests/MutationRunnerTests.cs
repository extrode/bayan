using System.Diagnostics;
using Bayan.Backend.C;
using Bayan.Cli;
using Bayan.Lowering;
using Bayan.Semantics;
using Bayan.Syntax.Text;
using Bayan.Testing;

namespace Bayan.Tests;

/// <summary>Spec 013 T6: the mutation runner's statuses, and that each mutant runs only the tests that reach it and stops at the first failure.</summary>
public sealed class MutationRunnerTests
{
    private const string Fixture = """
        i64 big(i64 n) {
            if n > 5 { return 1 }
            return 0
        }

        i64 inc(i64 n) {
            return n + 1
        }

        i64 count() {
            var i = 0
            while i < 3 {
                i += 1
            }
            return i
        }

        i64 hidden(i64 n) {
            var y = n
            @unobservable because "no test can see it"
            y += 0
            return y
        }

        i64 unused(i64 n) {
            return n * 2
        }

        test "big" {
            assert big(9) == 1
            assert big(2) == 0
        }

        test "inc" {
            assert inc(1) == 2
        }

        test "inc again" {
            assert inc(2) == 3
        }

        test "count" {
            assert count() == 3
        }

        test "hidden" {
            assert hidden(4) == 4
        }

        """;

    private sealed record Run(Schema Schema, MutationResult Result, string[] Log);

    private static readonly Lazy<Run?> Shared = new(Execute, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>Builds the fixture once and runs every mutant through a wrapper that logs <c>mutant test</c> per process.</summary>
    private static Run? Execute()
    {
        if (OperatingSystem.IsWindows()) return null;
        var dir = Path.Combine(Golden.RepoRoot, "tmp", "mutation-runner");
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        Directory.CreateDirectory(dir);
        var c = Compilation.Create([SourceText.FromString(Path.Combine(dir, "lib.test.byn"), Fixture)], dir,
            new CompilationOptions(IncludeTests: true, Library: true));
        Assert.False(c.Diagnostics.HasErrors, string.Join("\n", c.Diagnostics.Items.Select(d => d.Message)));
        var schema = Schemata.Build(c);
        var build = Toolchain.Build(CEmitter.Emit(Lowerer.Lower(c, schema.Switches), false), BuildMode.Safe);
        Assert.True(build.Succeeded, build.Errors);
        var log = Path.Combine(dir, "log.txt");
        var wrapper = Path.Combine(dir, "wrapper.sh");
        File.WriteAllText(wrapper, $"#!/bin/sh\necho \"$BAYAN_MUTANT $2\" >> '{log}'\nexec '{build.ExecutablePath}' \"$@\"\n");
        File.SetUnixFileMode(wrapper, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var result = MutationRunner.Run(wrapper, schema, Enumerable.Range(0, 5).ToList(), dir, TimeSpan.FromSeconds(60));
        return new Run(schema, result, File.ReadAllLines(log));
    }

    private static MutantResult Mutant(Run run, string operatorName, string original, string mutated) =>
        run.Result.Mutants.Single(m => m.Site.Site.Operator == operatorName && m.Site.Site.Original == original && m.Site.Site.Mutated == mutated);

    private static int Processes(Run run, int mutant) => run.Log.Count(l => l.StartsWith(mutant + " ", StringComparison.Ordinal));

    [Fact]
    public void TheWorkDirectoryIsTouchedSoAnotherProcessDoesNotSweepItMidRun()
    {
        var dir = Path.Combine(Golden.RepoRoot, "tmp", "mutation-runner-touch");
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        Directory.CreateDirectory(dir);
        var c = Compilation.Create([SourceText.FromString(Path.Combine(dir, "lib.test.byn"), "i64 inc(i64 n) {\n    return n + 1\n}\n")], dir,
            new CompilationOptions(IncludeTests: true, Library: true));
        var schema = Schemata.Build(c);
        Assert.NotEmpty(schema.Sites);
        var old = DateTime.UtcNow.AddHours(-3);
        Directory.SetLastWriteTimeUtc(dir, old);
        MutationRunner.Run("none", schema, [], dir, TimeSpan.FromSeconds(1));
        Assert.True(Directory.GetLastWriteTimeUtc(dir) > DateTime.UtcNow.AddMinutes(-5), Directory.GetLastWriteTimeUtc(dir).ToString("o"));
    }

    [Fact]
    public void StatusesAreKilledSurvivedTimeoutNotReachedMarked()
    {
        if (Shared.Value is not { } run) return;
        Assert.Equal(MutantStatus.Killed, Mutant(run, Mutator.Arithmetic, "n + 1", "n - 1").Status);
        Assert.Equal(MutantStatus.Survived, Mutant(run, Mutator.Boundary, "n > 5", "n >= 5").Status);
        Assert.Equal(MutantStatus.Timeout, Mutant(run, Mutator.Statement, "i += 1", "").Status);
        var unused = run.Result.Mutants.Where(m => m.Site.Site.Function.Name == "unused").ToList();
        Assert.NotEmpty(unused);
        Assert.All(unused, m => Assert.Equal(MutantStatus.NotReached, m.Status));
        var hidden = run.Result.Mutants.Where(m => m.Site.Site.Function.Name == "hidden" && m.Site.Site.Line is 21).ToList();
        Assert.NotEmpty(hidden);
        Assert.All(hidden, m => Assert.Contains(m.Status, new[] { MutantStatus.Marked, MutantStatus.Killed, MutantStatus.Timeout }));
        Assert.Contains(hidden, m => m.Runs > 0);
    }

    [Fact]
    public void AMutantRunsOnlyTheTestsThatReachItAndStopsAtTheFirstFailure()
    {
        if (Shared.Value is not { } run) return;
        // `inc` is reached by tests 1 and 2: the kill by test 1 ends the mutant.
        var killed = Mutant(run, Mutator.Arithmetic, "n + 1", "n - 1");
        Assert.Equal(1, killed.Runs);
        Assert.Equal(["%d 1".Replace("%d", killed.Site.Id.ToString())], run.Log.Where(l => l.StartsWith(killed.Site.Id + " ", StringComparison.Ordinal)).ToArray());
        // `big` is reached by test 0 only.
        var survived = Mutant(run, Mutator.Boundary, "n > 5", "n >= 5");
        Assert.Equal(["%d 0".Replace("%d", survived.Site.Id.ToString())], run.Log.Where(l => l.StartsWith(survived.Site.Id + " ", StringComparison.Ordinal)).ToArray());
        // Mutants not run start no process; the total is mutant 0 once per test plus every counted run.
        foreach (var m in run.Result.Mutants.Where(m => m.Status is MutantStatus.NotReached or MutantStatus.NotEmitted))
            Assert.Equal(0, Processes(run, m.Site.Id));
        Assert.All(run.Result.Mutants, m => Assert.Equal(m.Runs, Processes(run, m.Site.Id)));
        Assert.Equal(5, Processes(run, 0));
        Assert.Equal(5 + run.Result.Mutants.Sum(m => m.Runs), run.Log.Length);
        Assert.Equal(run.Log.Length, run.Result.Processes);
    }

    [Fact]
    public void ATimedOutMutantCountsAsKilledAndIsNamed()
    {
        if (Shared.Value is not { } run) return;
        var spin = Mutant(run, Mutator.Statement, "i += 1", "");
        Assert.Equal(3, spin.KilledBy);
        Assert.Equal(1, spin.Runs);
        Assert.Contains(run.Result.Mutants, m => m.Status == MutantStatus.Timeout);
    }

    [Fact]
    public void SitesJsonCarriesTheMark()
    {
        if (Shared.Value is not { } run) return;
        Assert.Contains("\"unobservable\": \"no test can see it\"", run.Schema.SitesJson());
    }
}
