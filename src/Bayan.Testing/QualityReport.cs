using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Bayan.Semantics;
using Bayan.Syntax.Text;

namespace Bayan.Testing;

/// <summary>Seconds, as the report's <c>timing</c> object names them (plan "Data / interfaces").</summary>
public sealed record ReportTiming(double BuildSeconds, double MeanMutantSeconds, double MaxMutantSeconds, double TotalSeconds, double MutantShareOfBuild);

/// <summary>One mutant in the report; <see cref="Status"/> is killed, survived, timeout, marked or not_reached.</summary>
public sealed record ReportMutant(ExternalSite Site, string Status, int? KilledBy, string? Suggestion);

/// <summary>One outcome of one condition; <see cref="Status"/> is hit, missed or unknown (a test timed out).</summary>
public sealed record ReportBranch(int Line, int Column, bool Outcome, string Status);

/// <summary>One <c>fails</c> variant of a function; <see cref="Unknown"/> when no test hit it and a test timed out. <see cref="Origins"/> are the hit <c>fail</c>, <c>try</c> or <c>catch</c> sites.</summary>
public sealed record ReportErrorPath(string Function, string Variant, bool Hit, bool Unknown, IReadOnlyList<string> Origins);

/// <summary>A file's coverage: the lines (1-based) with a counter, by outcome, every branch outcome and every error path.</summary>
public sealed record FileCoverage(IReadOnlyList<int> CoveredLines, IReadOnlyList<int> MissedLines, IReadOnlyList<int> UnknownLines,
    IReadOnlyList<ReportBranch> Branches, IReadOnlyList<ReportErrorPath> ErrorPaths);

/// <summary>Covered and total counts of one measure.</summary>
public sealed record Tally(int Covered, int Total);

/// <summary>A coverage report's totals over every file.</summary>
public sealed record CoverageTotals(Tally Lines, Tally Branches, Tally ErrorPaths);

/// <summary>A file's entry; <see cref="Mutants"/> is for a mutation report, <see cref="Coverage"/> for a coverage report.</summary>
public sealed record ReportFile(string Path, IReadOnlyList<ReportMutant> Mutants, FileCoverage? Coverage = null);

/// <summary>
/// The one report schema of spec 013 R12 (<c>bayan-quality.json</c>), the mutation half: version, kind, tool, timing, files,
/// totals and per-operator counts. Written by hand (NativeAOT, no reflection). Warnings are terminal-only.
/// </summary>
public sealed record QualityReport(string Kind, string Tool, ReportTiming Timing, IReadOnlyList<ReportFile> Files,
    IReadOnlyDictionary<string, int> Totals, IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> Operators,
    CoverageTotals? CoverageTotals = null)
{
    public static readonly string[] Counts = ["killed", "survived", "timeout", "marked", "not_reached", "not_emitted"];

    /// <summary>The sentence that says what test is missing, one per operator.</summary>
    public static string Suggest(string op) => op switch
    {
        Mutator.Boundary => "add a case at the boundary value",
        Mutator.Relational => "add a case where the two operands are equal and one where they differ",
        Mutator.Logical => "add a case where exactly one operand holds",
        Mutator.Condition => "add a case where the condition is false and one where it is true, and assert the difference",
        Mutator.Bool => "add a case that depends on this value",
        Mutator.Arithmetic => "add a case whose result differs under the changed operator",
        Mutator.Literal => "add a case that depends on this exact number",
        Mutator.Negation => "add a case with a non-zero operand and assert the sign",
        Mutator.Statement => "assert the effect of this statement",
        Mutator.EmptyReturn => "assert the returned value is not the empty value",
        Mutator.Variant => "add a case that asserts which error variant is returned",
        _ => "add a case that fails under this change",
    };

    /// <summary>Builds the mutation report of <paramref name="result"/>; <paramref name="mutantSeconds"/> is each run mutant's wall time, in order.</summary>
    public static QualityReport Mutation(string tool, MutationResult result, Schema schema, double buildSeconds, IReadOnlyList<double> mutantSeconds, double totalSeconds) =>
        Mutation(tool, result.Mutants.Select(m => (ExternalSite.From(m.Site), m.Status, m.KilledBy)).ToList(), schema.NotEmitted, buildSeconds, mutantSeconds, totalSeconds);

    /// <summary>Builds the mutation report of an external-mode run (R3); the candidates not emitted are the results with that status.</summary>
    public static QualityReport Mutation(string tool, IReadOnlyList<ExternalResult> results, double buildSeconds, double totalSeconds)
    {
        var notEmitted = results.Where(r => r.Status == MutantStatus.NotEmitted).GroupBy(r => r.Site.Operator).ToDictionary(g => g.Key, g => g.Count());
        return Mutation(tool, results.Select(r => (r.Site, r.Status, (int?)null)).ToList(), notEmitted, buildSeconds,
            results.Where(r => r.Status != MutantStatus.NotEmitted).Select(r => r.Seconds).ToList(), totalSeconds);
    }

    private static QualityReport Mutation(string tool, IReadOnlyList<(ExternalSite Site, MutantStatus Status, int? KilledBy)> mutants,
        IReadOnlyDictionary<string, int> notEmitted, double buildSeconds, IReadOnlyList<double> mutantSeconds, double totalSeconds)
    {
        var mean = mutantSeconds.Count == 0 ? 0 : mutantSeconds.Average();
        var max = mutantSeconds.Count == 0 ? 0 : mutantSeconds.Max();
        var timing = new ReportTiming(buildSeconds, mean, max, totalSeconds, buildSeconds > 0 ? mean / buildSeconds : 0);
        var byFile = new List<(string Path, List<ReportMutant> Mutants)>();
        var operators = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);
        foreach (var op in Mutator.Operators) operators[op] = Counts.ToDictionary(c => c, _ => 0);
        foreach (var (op, n) in notEmitted)
        {
            if (!operators.ContainsKey(op)) operators[op] = Counts.ToDictionary(c => c, _ => 0);
            operators[op]["not_emitted"] = n;
        }
        foreach (var (site, mutantStatus, killedBy) in mutants)
        {
            if (mutantStatus == MutantStatus.NotEmitted) continue;
            var status = StatusName(mutantStatus);
            if (!operators.ContainsKey(site.Operator)) operators[site.Operator] = Counts.ToDictionary(c => c, _ => 0);
            operators[site.Operator][status]++;
            var entry = new ReportMutant(site, status, killedBy, mutantStatus == MutantStatus.Survived ? Suggest(site.Operator) : null);
            var i = byFile.FindIndex(f => f.Path == site.File);
            if (i < 0) byFile.Add((site.File, [entry]));
            else byFile[i].Mutants.Add(entry);
        }
        var totals = Counts.ToDictionary(c => c, c => operators.Values.Sum(o => o[c]));
        return new QualityReport("mutation", tool, timing, byFile.Select(f => new ReportFile(f.Path, f.Mutants)).ToList(), totals,
            operators.ToDictionary(o => o.Key, o => (IReadOnlyDictionary<string, int>)o.Value));
    }

    /// <summary>
    /// Builds the coverage report of a <c>--coverage</c> run: <paramref name="hits"/> is the counter hits merged over every
    /// test; when <paramref name="timedOut"/> a test was killed, so a counter nobody hit is unknown, never missed (R9).
    /// </summary>
    public static QualityReport Coverage(string tool, CoverageMap map, IReadOnlyDictionary<int, ulong> hits, bool timedOut, double buildSeconds, double totalSeconds)
    {
        var unhit = timedOut ? "unknown" : "missed";
        string Status(CoverageCounter c) => hits.ContainsKey(c.Id) ? "hit" : unhit;
        var files = new List<ReportFile>();
        // std modules (`<std>/...`) are the compiler's own source: they are no part of the package's coverage or its gate
        foreach (var group in map.Counters.Where(c => !c.File.StartsWith("<std>/", StringComparison.Ordinal)).GroupBy(c => c.File))
        {
            var lines = new List<int>[3] { [], [], [] };
            foreach (var byLine in group.Where(c => c.Kind == CoverageInstrumenter.Line).GroupBy(c => c.Line).OrderBy(g => g.Key))
            {
                var statuses = byLine.Select(Status).ToList();
                lines[statuses.All(s => s == "hit") ? 0 : timedOut ? 2 : 1].Add(byLine.Key);
            }
            var branches = group.Where(c => c.Kind == CoverageInstrumenter.Branch)
                .Select(c => new ReportBranch(c.Line, c.Column, c.Outcome == true, Status(c))).ToList();
            var errors = group.Where(c => c.Kind == CoverageInstrumenter.ErrorPath).GroupBy(c => (c.Function!, c.Variant!))
                .Select(g => new ReportErrorPath(g.Key.Item1, g.Key.Item2, g.Any(c => hits.ContainsKey(c.Id)), timedOut && !g.Any(c => hits.ContainsKey(c.Id)),
                    g.Where(c => hits.ContainsKey(c.Id)).Select(c => $"{c.Origin} {c.Line}:{c.Column}").ToList())).ToList();
            files.Add(new ReportFile(group.Key, [], new FileCoverage(lines[0], lines[1], lines[2], branches, errors)));
        }
        var cov = files.Select(f => f.Coverage!).ToList();
        var totals = new CoverageTotals(
            new Tally(cov.Sum(f => f.CoveredLines.Count), cov.Sum(f => f.CoveredLines.Count + f.MissedLines.Count + f.UnknownLines.Count)),
            new Tally(cov.Sum(f => f.Branches.Count(b => b.Status == "hit")), cov.Sum(f => f.Branches.Count)),
            new Tally(cov.Sum(f => f.ErrorPaths.Count(e => e.Hit)), cov.Sum(f => f.ErrorPaths.Count)));
        return new QualityReport("coverage", tool, new ReportTiming(buildSeconds, 0, 0, totalSeconds, 0), files, new Dictionary<string, int>(),
            new Dictionary<string, IReadOnlyDictionary<string, int>>(), totals);
    }

    private static string StatusName(MutantStatus s) => s switch
    {
        MutantStatus.Killed => "killed",
        MutantStatus.Survived => "survived",
        MutantStatus.Timeout => "timeout",
        MutantStatus.Marked => "marked",
        MutantStatus.NotReached => "not_reached",
        _ => "not_emitted",
    };

    /// <summary>The report as <c>bayan-quality.json</c>.</summary>
    public string Json()
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var w = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true, NewLine = "\n", Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            w.WriteStartObject();
            w.WriteNumber("version", 1);
            w.WriteString("kind", Kind);
            w.WriteString("tool", Tool);
            w.WriteStartObject("timing");
            w.WriteNumber("build_s", Timing.BuildSeconds);
            if (CoverageTotals is null)
            {
                w.WriteNumber("mean_mutant_s", Timing.MeanMutantSeconds);
                w.WriteNumber("max_mutant_s", Timing.MaxMutantSeconds);
            }
            w.WriteNumber("total_s", Timing.TotalSeconds);
            if (CoverageTotals is null) w.WriteNumber("mutant_share_of_build", Timing.MutantShareOfBuild);
            w.WriteEndObject();
            w.WriteStartArray("files");
            foreach (var f in Files)
            {
                w.WriteStartObject();
                w.WriteString("path", f.Path);
                if (f.Coverage is { } cov)
                {
                    WriteCoverage(w, cov);
                    w.WriteEndObject();
                    continue;
                }
                w.WriteStartArray("mutants");
                foreach (var m in f.Mutants)
                {
                    var s = m.Site;
                    w.WriteStartObject();
                    w.WriteStartObject("site");
                    w.WriteNumber("id", s.Id);
                    w.WriteString("file", s.File);
                    w.WriteNumber("line", s.Line);
                    w.WriteNumber("column", s.Column);
                    w.WriteString("operator", s.Operator);
                    w.WriteString("original", s.Original);
                    w.WriteString("mutated", s.Mutated);
                    if (s.Unobservable is not null) w.WriteString("unobservable", s.Unobservable);
                    w.WriteEndObject();
                    w.WriteString("status", m.Status);
                    if (m.KilledBy is { } k) w.WriteNumber("killedBy", k);
                    if (m.Suggestion is not null) w.WriteString("suggestion", m.Suggestion);
                    w.WriteEndObject();
                }
                w.WriteEndArray();
                w.WriteEndObject();
            }
            w.WriteEndArray();
            if (CoverageTotals is { } t)
            {
                w.WriteStartObject("totals");
                foreach (var (name, tally) in new[] { ("lines", t.Lines), ("branches", t.Branches), ("errorPaths", t.ErrorPaths) })
                {
                    w.WriteStartObject(name);
                    w.WriteNumber("covered", tally.Covered);
                    w.WriteNumber("total", tally.Total);
                    w.WriteEndObject();
                }
                w.WriteEndObject();
            }
            else
            {
                WriteCounts(w, "totals", Totals);
                w.WriteStartObject("operators");
                foreach (var (op, counts) in Operators) WriteCounts(w, op, counts);
                w.WriteEndObject();
            }
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.WrittenSpan) + "\n";
    }

    private static void WriteCoverage(Utf8JsonWriter w, FileCoverage f)
    {
        w.WriteStartObject("lines");
        w.WriteNumber("covered", f.CoveredLines.Count);
        w.WriteNumber("total", f.CoveredLines.Count + f.MissedLines.Count + f.UnknownLines.Count);
        w.WriteStartArray("missed");
        foreach (var l in f.MissedLines) w.WriteNumberValue(l);
        w.WriteEndArray();
        w.WriteStartArray("unknown");
        foreach (var l in f.UnknownLines) w.WriteNumberValue(l);
        w.WriteEndArray();
        w.WriteEndObject();
        w.WriteStartObject("branches");
        w.WriteNumber("covered", f.Branches.Count(b => b.Status == "hit"));
        w.WriteNumber("total", f.Branches.Count);
        foreach (var status in new[] { "missed", "unknown" })
        {
            w.WriteStartArray(status);
            foreach (var b in f.Branches.Where(b => b.Status == status))
            {
                w.WriteStartObject();
                w.WriteNumber("line", b.Line);
                w.WriteNumber("column", b.Column);
                w.WriteBoolean("outcome", b.Outcome);
                w.WriteEndObject();
            }
            w.WriteEndArray();
        }
        w.WriteEndObject();
        w.WriteStartArray("errorPaths");
        foreach (var e in f.ErrorPaths)
        {
            w.WriteStartObject();
            w.WriteString("function", e.Function);
            w.WriteString("variant", e.Variant);
            w.WriteBoolean("hit", e.Hit);
            if (e.Unknown) w.WriteBoolean("unknown", true);
            w.WriteStartArray("origins");
            foreach (var o in e.Origins) w.WriteStringValue(o);
            w.WriteEndArray();
            w.WriteEndObject();
        }
        w.WriteEndArray();
    }

    private static void WriteCounts(Utf8JsonWriter w, string name, IReadOnlyDictionary<string, int> counts)
    {
        w.WriteStartObject(name);
        foreach (var c in Counts) w.WriteNumber(c, counts[c]);
        w.WriteEndObject();
    }

    /// <summary>The marks that cover no mutant, and the marked mutants a test killed (R7): each a one-line warning.</summary>
    public static IReadOnlyList<string> Warnings(MutationResult result, Schema schema, Compilation c, IReadOnlyList<SourceText> sources)
    {
        var warnings = new List<string>();
        foreach (var m in result.Mutants.Where(m => m.Site.Unobservable is not null && m.Status is MutantStatus.Killed or MutantStatus.Timeout))
            warnings.Add($"warning: {Where(m.Site.Site)}: the mark is wrong: a test sees this ({m.Site.Site.Original} → {m.Site.Site.Mutated}, `{m.Site.Unobservable}`)");
        foreach (var mark in c.Unobservable)
        {
            var source = sources.FirstOrDefault(s => s.Path == mark.File);
            if (source is not null && schema.Sites.Any(s => InMark(mark, s.Site, source))) continue;
            var at = source is not null ? source.GetLinePosition(mark.Span.Start) : (Line: 0, Column: 0);
            warnings.Add($"warning: {mark.File}:{at.Line}:{at.Column}: the mark covers no mutant (`{mark.Reason}`)");
        }
        return warnings;
    }

    /// <summary>Whether the site lies inside the mark's statement and the mark's operator argument, if any, is the site's.</summary>
    private static bool InMark(UnobservableMark mark, MutationSite site, SourceText source)
    {
        if (site.File != mark.File || (mark.Operator is not null && mark.Operator != site.Operator)) return false;
        var offset = source.GetLineSpan(site.Line - 1).Start;
        var bytes = source.Bytes.Span;
        for (var col = 1; col < site.Column; col++)
            do offset++; while (offset < bytes.Length && (bytes[offset] & 0xC0) == 0x80);
        return mark.Span.Start <= offset && offset + Encoding.UTF8.GetByteCount(site.Original) <= mark.Span.Start + mark.Span.Length;
    }

    private static string Where(MutationSite s) => $"{s.File}:{s.Line}:{s.Column}";

    private static string Where(ExternalSite s) => $"{s.File}:{s.Line}:{s.Column}";

    private static string Fmt(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>The terminal summary: totals, each survivor with its suggestion, per-operator counts, timing and the warnings.</summary>
    public string Summary(IReadOnlyList<string> warnings)
    {
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"mutation: {Totals["killed"]} killed, {Totals["survived"]} survived, {Totals["timeout"]} timeout, {Totals["marked"]} marked, {Totals["not_reached"]} not reached, {Totals["not_emitted"]} not emitted\n");
        foreach (var m in Files.SelectMany(f => f.Mutants).Where(m => m.Status == "survived"))
        {
            var s = m.Site;
            sb.Append(CultureInfo.InvariantCulture, $"survived: {Where(s)}: {s.Original} → {s.Mutated} [{s.Operator}]\n  suggestion: {m.Suggestion}\n");
        }
        sb.Append("operators:\n");
        foreach (var (op, counts) in Operators)
            sb.Append(CultureInfo.InvariantCulture, $"  {op}: {string.Join(", ", Counts.Select(c => $"{counts[c]} {c.Replace('_', ' ')}"))}\n");
        sb.Append(CultureInfo.InvariantCulture, $"timing: build {Fmt(Timing.BuildSeconds)} s, mean {Fmt(Timing.MeanMutantSeconds)} s and max {Fmt(Timing.MaxMutantSeconds)} s per mutant, total {Fmt(Timing.TotalSeconds)} s, mutant share of build {Fmt(Timing.MutantShareOfBuild * 100)}%\n");
        foreach (var w in warnings) sb.Append(w).Append('\n');
        return sb.ToString();
    }

    /// <summary>The per-line listing of a coverage file (<c>&lt;file&gt;.cov.txt</c>): a gutter of <c>+</c> hit, <c>-</c> missed, <c>~</c> unknown, blank for no counter, then the source text.</summary>
    public static string Listing(FileCoverage f, IReadOnlyList<string> sourceLines)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < sourceLines.Count; i++)
        {
            var n = i + 1;
            var gutter = f.CoveredLines.Contains(n) ? '+' : f.MissedLines.Contains(n) ? '-' : f.UnknownLines.Contains(n) ? '~' : ' ';
            sb.Append(CultureInfo.InvariantCulture, $"{gutter} {sourceLines[i]}\n");
        }
        return sb.ToString();
    }

    private static string Percent(Tally t) => t.Total == 0 ? "100%" : Fmt(100.0 * t.Covered / t.Total) + "%";

    /// <summary>The coverage terminal summary: totals, then each uncovered branch outcome and error path by name, and what is unknown.</summary>
    public string CoverageSummary()
    {
        var t = CoverageTotals!;
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"coverage: lines {t.Lines.Covered}/{t.Lines.Total} ({Percent(t.Lines)}), branches {t.Branches.Covered}/{t.Branches.Total} ({Percent(t.Branches)}), error paths {t.ErrorPaths.Covered}/{t.ErrorPaths.Total} ({Percent(t.ErrorPaths)})\n");
        foreach (var f in Files)
        {
            var c = f.Coverage!;
            var lines = c.CoveredLines.Count + c.MissedLines.Count + c.UnknownLines.Count;
            sb.Append(CultureInfo.InvariantCulture, $"  {f.Path}: lines {c.CoveredLines.Count}/{lines}, branches {c.Branches.Count(b => b.Status == "hit")}/{c.Branches.Count}, error paths {c.ErrorPaths.Count(e => e.Hit)}/{c.ErrorPaths.Count}\n");
            foreach (var b in c.Branches.Where(b => b.Status != "hit"))
                sb.Append(CultureInfo.InvariantCulture, $"{(b.Status == "missed" ? "uncovered" : "unknown")} branch: {f.Path}:{b.Line}:{b.Column} never {(b.Outcome ? "true" : "false")}\n");
            foreach (var e in c.ErrorPaths.Where(e => !e.Hit))
                sb.Append(CultureInfo.InvariantCulture, $"{(e.Unknown ? "unknown" : "uncovered")} error path: {f.Path}: {e.Function} {e.Variant}\n");
        }
        return sb.ToString();
    }
}
