using System.Globalization;

namespace Bayan.Testing;

/// <summary>A measure below its manifest minimum: its percentage, the minimum, and what is missing, one line each.</summary>
public sealed record GateFailure(string Name, double Percent, int Min, IReadOnlyList<string> Missing);

/// <summary>Spec 013 R13: a report checked against the package's minimum scores; each failure names what is missing.</summary>
public static class Gates
{
    /// <summary>
    /// The measures of a coverage report below their minimum percentages. Unknown lines, branches and error paths (a test timed
    /// out) are left out of the percentage and not named. A measure with nothing to cover passes.
    /// </summary>
    public static IReadOnlyList<GateFailure> Coverage(QualityReport report, int? minLines, int? minBranches, int? minErrorPaths)
    {
        var files = report.Files.Select(f => (f.Path, C: f.Coverage!)).ToList();
        var failures = new List<GateFailure>();
        Add(failures, "lines", minLines, files.Sum(f => f.C.CoveredLines.Count),
            files.SelectMany(f => f.C.MissedLines.Select(l => $"uncovered line: {f.Path}:{l}")).ToList());
        Add(failures, "branches", minBranches, files.Sum(f => f.C.Branches.Count(b => b.Status == "hit")),
            files.SelectMany(f => f.C.Branches.Where(b => b.Status == "missed").Select(b => $"uncovered branch: {f.Path}:{b.Line}:{b.Column} never {(b.Outcome ? "true" : "false")}")).ToList());
        Add(failures, "error paths", minErrorPaths, files.Sum(f => f.C.ErrorPaths.Count(e => e.Hit)),
            files.SelectMany(f => f.C.ErrorPaths.Where(e => !e.Hit && !e.Unknown).Select(e => $"uncovered error path: {e.Function} {e.Variant} ({f.Path})")).ToList());
        return failures;
    }

    /// <summary>
    /// The mutation score, (killed + timeout) / (killed + timeout + survived), against the minimum percentage. Not-reached,
    /// marked and not-emitted mutants are left out. Each survivor is named as <c>file:line:column operator `original` -> `mutated`</c>.
    /// </summary>
    public static IReadOnlyList<GateFailure> Mutation(QualityReport report, int? minMutation)
    {
        var failures = new List<GateFailure>();
        Add(failures, "mutation", minMutation, report.Totals["killed"] + report.Totals["timeout"],
            report.Files.SelectMany(f => f.Mutants).Where(m => m.Status == "survived")
                .Select(m => $"survived mutant: {m.Site.File}:{m.Site.Line}:{m.Site.Column} {m.Site.Operator} `{m.Site.Original}` -> `{m.Site.Mutated}`").ToList(),
            report.Totals["survived"]);
        return failures;
    }

    /// <summary>Adds a failure when <paramref name="covered"/> of covered + missing is below <paramref name="min"/>; the missing count is <paramref name="missing"/>'s length unless <paramref name="missingCount"/> is given.</summary>
    private static void Add(List<GateFailure> failures, string name, int? min, int covered, IReadOnlyList<string> missing, int? missingCount = null)
    {
        var total = covered + (missingCount ?? missing.Count);
        if (min is not { } m || (long)covered * 100 >= (long)m * total) return;
        failures.Add(new GateFailure(name, 100.0 * covered / total, m, missing));
    }

    /// <summary>The percentage as the gate line prints it.</summary>
    public static string Fmt(double percent) => percent.ToString("0.###", CultureInfo.InvariantCulture);
}
