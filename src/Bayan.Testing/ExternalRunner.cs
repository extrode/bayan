using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace Bayan.Testing;

/// <summary>A site read back from a <c>&lt;binary&gt;.sites.json</c>: the identity fields, its id in that binary, and whether it was emitted.</summary>
public sealed record ExternalSite(int Id, string File, int Line, int Column, string Operator, string Original, string Mutated, bool Emitted, string? Unobservable);

/// <summary>A site's outcome under an external command.</summary>
public sealed record ExternalResult(ExternalSite Site, MutantStatus Status);

/// <summary>
/// External mode (spec 013 R3): runs a user command once per site with <c>BAYAN_MUTANT</c> set to the site's id. Exit 0
/// is survived, any other exit is killed, and a timeout (max(1 s, 5 times the unmutated run)) is killed and reported as its own status.
/// </summary>
public static class ExternalRunner
{
    /// <summary>Reads a site map; null when it is not one this version understands.</summary>
    public static IReadOnlyList<ExternalSite>? ReadSites(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("version", out var v) || v.GetInt32() != 1
            || !root.TryGetProperty("sites", out var sites)) return null;
        var list = new List<ExternalSite>();
        foreach (var s in sites.EnumerateArray())
            list.Add(new ExternalSite(s.GetProperty("id").GetInt32(), s.GetProperty("file").GetString()!, s.GetProperty("line").GetInt32(),
                s.GetProperty("column").GetInt32(), s.GetProperty("operator").GetString()!, s.GetProperty("original").GetString()!,
                s.GetProperty("mutated").GetString()!, !s.TryGetProperty("emitted", out var e) || e.GetBoolean(),
                s.TryGetProperty("unobservable", out var u) ? u.GetString() : null));
        return list;
    }

    /// <summary>
    /// Runs <paramref name="command"/> unmutated (mutant 0) and then once per emitted site. Null when the unmutated run
    /// does not exit 0 within <paramref name="baselineTimeout"/>, since every mutant would then look killed.
    /// </summary>
    public static IReadOnlyList<ExternalResult>? Run(IReadOnlyList<ExternalSite> sites, IReadOnlyList<string> command, IDictionary<string, string> environment,
        TimeSpan baselineTimeout, Action<ExternalResult>? onMutant = null)
    {
        var baseline = Exec(command, environment, 0, baselineTimeout);
        if (baseline.TimedOut || baseline.ExitCode != 0) return null;
        var limit = TimeSpan.FromTicks(Math.Max(TimeSpan.TicksPerSecond, 5 * baseline.Elapsed.Ticks));
        var results = new List<ExternalResult>();
        foreach (var site in sites)
        {
            var status = MutantStatus.NotEmitted;
            if (site.Emitted)
            {
                var outcome = Exec(command, environment, site.Id, limit);
                status = outcome.TimedOut ? MutantStatus.Timeout
                    : outcome.ExitCode != 0 ? MutantStatus.Killed
                    : site.Unobservable is not null ? MutantStatus.Marked
                    : MutantStatus.Survived;
            }
            var result = new ExternalResult(site, status);
            results.Add(result);
            onMutant?.Invoke(result);
        }
        return results;
    }

    private sealed record Outcome(int ExitCode, bool TimedOut, TimeSpan Elapsed);

    private static Outcome Exec(IReadOnlyList<string> command, IDictionary<string, string> environment, int mutant, TimeSpan limit)
    {
        var psi = new ProcessStartInfo(command[0])
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        for (var i = 1; i < command.Count; i++) psi.ArgumentList.Add(command[i]);
        foreach (var (k, v) in environment) psi.Environment[k] = v;
        psi.Environment["BAYAN_MUTANT"] = mutant.ToString(CultureInfo.InvariantCulture);
        var clock = Stopwatch.StartNew();
        using var p = Process.Start(psi)!;
        _ = p.StandardOutput.ReadToEndAsync();
        _ = p.StandardError.ReadToEndAsync();
        if (!p.WaitForExit(limit))
        {
            p.Kill(entireProcessTree: true);
            p.WaitForExit();
            return new Outcome(-1, true, clock.Elapsed);
        }
        return new Outcome(p.ExitCode, false, clock.Elapsed);
    }
}
