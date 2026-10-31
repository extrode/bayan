using System.Globalization;

namespace Bayan.Testing;

/// <summary>Spec 013 R13: a report checked against the package's minimum scores; each failure names what is missing.</summary>
public static class Gates
{
    /// <summary>The failures of a coverage report against the minimum line, branch and error-path percentages; empty when it passes.</summary>
    public static IReadOnlyList<string> Coverage(QualityReport report, int? minLines, int? minBranches, int? minErrorPaths)
    {
        var t = report.CoverageTotals!;
        var failures = new List<string>();
        if (Below(t.Lines, minLines))
        {
            failures.Add(Header("lines", t.Lines, minLines!.Value));
            foreach (var f in report.Files)
                foreach (var (lines, status) in new[] { (f.Coverage!.MissedLines, "uncovered"), (f.Coverage.UnknownLines, "unknown") })
                    failures.AddRange(lines.Select(l => $"{status} line: {f.Path}:{l}"));
        }
        if (Below(t.Branches, minBranches))
        {
            failures.Add(Header("branches", t.Branches, minBranches!.Value));
            foreach (var f in report.Files)
                failures.AddRange(f.Coverage!.Branches.Where(b => b.Status != "hit")
                    .Select(b => $"{(b.Status == "missed" ? "uncovered" : "unknown")} branch: {f.Path}:{b.Line}:{b.Column} never {(b.Outcome ? "true" : "false")}"));
        }
        if (Below(t.ErrorPaths, minErrorPaths))
        {
            failures.Add(Header("error paths", t.ErrorPaths, minErrorPaths!.Value));
            foreach (var f in report.Files)
                failures.AddRange(f.Coverage!.ErrorPaths.Where(e => !e.Hit)
                    .Select(e => $"{(e.Unknown ? "unknown" : "uncovered")} error path: {f.Path}: {e.Function} {e.Variant}"));
        }
        return failures;
    }

    /// <summary>The failures of a mutation report against the minimum score (killed and timeout over killed, timeout, survived and not reached; marked and not emitted are left out); empty when it passes.</summary>
    public static IReadOnlyList<string> Mutation(QualityReport report, int? minMutation)
    {
        var caught = report.Totals["killed"] + report.Totals["timeout"];
        var tally = new Tally(caught, caught + report.Totals["survived"] + report.Totals["not_reached"]);
        if (!Below(tally, minMutation)) return [];
        var failures = new List<string> { Header("mutation score", tally, minMutation!.Value) };
        failures.AddRange(report.Files.SelectMany(f => f.Mutants).Where(m => m.Status is "survived" or "not_reached")
            .Select(m => $"{(m.Status == "survived" ? "survived" : "not reached")} mutant: {m.Site.File}:{m.Site.Line}:{m.Site.Column}: {m.Site.Original} → {m.Site.Mutated} [{m.Site.Operator}]"));
        return failures;
    }

    private static bool Below(Tally t, int? min) => min is { } m && (long)t.Covered * 100 < (long)m * t.Total;

    private static string Header(string name, Tally t, int min) =>
        string.Create(CultureInfo.InvariantCulture, $"bayan: {name} {t.Covered}/{t.Total} ({100.0 * t.Covered / t.Total:0.###}%) is below the minimum {min}%");
}
