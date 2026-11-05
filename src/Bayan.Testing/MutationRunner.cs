using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;

namespace Bayan.Testing;

/// <summary>What became of a mutant (spec 013 R1).</summary>
public enum MutantStatus { Killed, Survived, Timeout, NotReached, Marked, NotEmitted }

/// <summary>A mutant's outcome: how many test processes it took and, for a kill or timeout, the test that ended it.</summary>
public sealed record MutantResult(SchemaSite Site, MutantStatus Status, int Runs, int? KilledBy);

/// <summary>The outcome of a run: every site's result, the tests left out because they did not pass unmutated, and the processes started.</summary>
public sealed record MutationResult(IReadOnlyList<MutantResult> Mutants, IReadOnlyList<int> Excluded, int Processes);

/// <summary>
/// Runs the mutants of one <c>--mutate</c> build (spec 013 R1, R3, R8). Mutant 0 runs each test once and records the sites
/// it reaches (<c>BAYAN_SITE_REACH</c>); a mutant then runs only the tests that reach its site, in test order, and stops at
/// the first that fails. A timeout of max(1 s, 5 times the test's mutant-0 time) counts as killed, reported as its own status.
/// </summary>
public static class MutationRunner
{
    private sealed record Outcome(int ExitCode, bool TimedOut, TimeSpan Elapsed);

    /// <param name="executable">The <c>--mutate</c> test binary.</param>
    /// <param name="schema">Its schema (the contents of its <c>sites.json</c>).</param>
    /// <param name="tests">The test indexes to run, as <c>--test &lt;index&gt;</c> takes them.</param>
    /// <param name="workDir">A directory for the reach files, which the caller owns.</param>
    /// <param name="baselineTimeout">The limit for a test's mutant-0 run.</param>
    /// <param name="onMutant">Called as each mutant finishes.</param>
    public static MutationResult Run(string executable, Schema schema, IReadOnlyList<int> tests, string workDir, TimeSpan baselineTimeout,
        Action<MutantResult>? onMutant = null)
    {
        var processes = 0;
        var time = new Dictionary<int, TimeSpan>();
        var reach = new Dictionary<int, HashSet<int>>();
        var excluded = new List<int>();
        foreach (var t in tests)
        {
            var path = Path.Combine(workDir, "reach." + t.ToString(CultureInfo.InvariantCulture));
            File.Delete(path);
            var outcome = Exec(executable, t, 0, path, baselineTimeout);
            processes++;
            // A pending test (exit 4) is left out without a warning; it is not meant to pass yet.
            if (outcome.ExitCode == 4 && !outcome.TimedOut) continue;
            if (outcome.ExitCode != 0 || outcome.TimedOut)
            {
                excluded.Add(t);
                continue;
            }
            time[t] = outcome.Elapsed;
            reach[t] = File.Exists(path)
                ? File.ReadAllLines(path).Select(l => int.Parse(l, NumberStyles.None, CultureInfo.InvariantCulture)).ToHashSet()
                : [];
        }
        var ran = tests.Where(time.ContainsKey).ToList();
        var results = new List<MutantResult>();
        foreach (var site in schema.Sites)
        {
            var result = Mutant(executable, site, ran, reach, time, ref processes);
            results.Add(result);
            onMutant?.Invoke(result);
        }
        return new MutationResult(results, excluded, processes);
    }

    private static MutantResult Mutant(string executable, SchemaSite site, List<int> tests, Dictionary<int, HashSet<int>> reach,
        Dictionary<int, TimeSpan> time, ref int processes)
    {
        if (!site.Emitted) return new MutantResult(site, MutantStatus.NotEmitted, 0, null);
        var runs = 0;
        foreach (var t in tests)
        {
            if (!reach[t].Contains(site.Id)) continue;
            var limit = TimeSpan.FromTicks(Math.Max(TimeSpan.TicksPerSecond, 5 * time[t].Ticks));
            var outcome = Exec(executable, t, site.Id, null, limit);
            runs++;
            processes++;
            if (outcome.TimedOut) return new MutantResult(site, MutantStatus.Timeout, runs, t);
            if (outcome.ExitCode != 0) return new MutantResult(site, MutantStatus.Killed, runs, t);
        }
        // A marked mutant still runs; one a test kills stays killed so the report can warn that the mark is wrong.
        if (site.Unobservable is not null) return new MutantResult(site, MutantStatus.Marked, runs, null);
        return new MutantResult(site, runs == 0 ? MutantStatus.NotReached : MutantStatus.Survived, runs, null);
    }

    /// <summary>
    /// External mode (spec 013 R3): runs <paramref name="command"/> once per emitted site with <c>BAYAN_MUTANT</c> set to its id.
    /// Exit 0 is survived (marked when the site has a mark), any other exit is killed, and a timeout of max(1 s, 5 times the
    /// unmutated run) is killed and reported as its own status. Null when the unmutated run (mutant 0) does not exit 0 within
    /// <paramref name="baselineTimeout"/>, since every mutant would then look killed; no mutant is run then.
    /// </summary>
    public static IReadOnlyList<ExternalResult>? RunExternal(IReadOnlyList<ExternalSite> sites, IReadOnlyList<string> command,
        IReadOnlyDictionary<string, string> environment, TimeSpan baselineTimeout, Action<ExternalResult>? onMutant = null)
    {
        Outcome Run(int mutant, TimeSpan limit)
        {
            var psi = new ProcessStartInfo(command[0]);
            for (var i = 1; i < command.Count; i++) psi.ArgumentList.Add(command[i]);
            foreach (var (k, v) in environment) psi.Environment[k] = v;
            psi.Environment["BAYAN_MUTANT"] = mutant.ToString(CultureInfo.InvariantCulture);
            return Exec(psi, limit);
        }
        Outcome baseline;
        try
        {
            baseline = Run(0, baselineTimeout);
        }
        catch (Win32Exception)
        {
            // The command could not be started (not found, not executable).
            return null;
        }
        if (baseline.TimedOut || baseline.ExitCode != 0) return null;
        var timeout = TimeSpan.FromTicks(Math.Max(TimeSpan.TicksPerSecond, 5 * baseline.Elapsed.Ticks));
        var results = new List<ExternalResult>();
        foreach (var site in sites)
        {
            var result = new ExternalResult(site, MutantStatus.NotEmitted, 0);
            if (site.Emitted)
            {
                var outcome = Run(site.Id, timeout);
                var status = outcome.TimedOut ? MutantStatus.Timeout
                    : outcome.ExitCode != 0 ? MutantStatus.Killed
                    : site.Unobservable is not null ? MutantStatus.Marked
                    : MutantStatus.Survived;
                result = new ExternalResult(site, status, outcome.Elapsed.TotalSeconds);
            }
            results.Add(result);
            onMutant?.Invoke(result);
        }
        return results;
    }

    private static Outcome Exec(string executable, int test, int mutant, string? reachPath, TimeSpan limit)
    {
        var psi = new ProcessStartInfo(executable);
        psi.ArgumentList.Add("--test");
        psi.ArgumentList.Add(test.ToString(CultureInfo.InvariantCulture));
        psi.Environment["BAYAN_MUTANT"] = mutant.ToString(CultureInfo.InvariantCulture);
        if (reachPath is null) psi.Environment.Remove("BAYAN_SITE_REACH");
        else psi.Environment["BAYAN_SITE_REACH"] = reachPath;
        return Exec(psi, limit);
    }

    /// <summary>Reads a stream to its end, keeping nothing: a mutant's output is discarded, however much it prints.</summary>
    internal static Task Drain(Stream stream) => stream.CopyToAsync(Stream.Null);

    private static Outcome Exec(ProcessStartInfo psi, TimeSpan limit)
    {
        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError = true;
        psi.UseShellExecute = false;
        var clock = Stopwatch.StartNew();
        using var p = Process.Start(psi)!;
        _ = Drain(p.StandardOutput.BaseStream);
        _ = Drain(p.StandardError.BaseStream);
        if (!p.WaitForExit(limit))
        {
            p.Kill(entireProcessTree: true);
            p.WaitForExit();
            return new Outcome(-1, true, clock.Elapsed);
        }
        return new Outcome(p.ExitCode, false, clock.Elapsed);
    }
}
