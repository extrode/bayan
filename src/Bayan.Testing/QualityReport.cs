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
public sealed record ReportMutant(SchemaSite Site, string Status, int? KilledBy, string? Suggestion);

/// <summary>A file's entry; <see cref="Mutants"/> is for a mutation report. Coverage fields are T10's and are left out here.</summary>
public sealed record ReportFile(string Path, IReadOnlyList<ReportMutant> Mutants);

/// <summary>
/// The one report schema of spec 013 R12 (<c>bayan-quality.json</c>), the mutation half: version, kind, tool, timing, files,
/// totals and per-operator counts. Written by hand (NativeAOT, no reflection). Warnings are terminal-only.
/// </summary>
public sealed record QualityReport(string Kind, string Tool, ReportTiming Timing, IReadOnlyList<ReportFile> Files,
    IReadOnlyDictionary<string, int> Totals, IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> Operators)
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
    public static QualityReport Mutation(string tool, MutationResult result, Schema schema, double buildSeconds, IReadOnlyList<double> mutantSeconds, double totalSeconds)
    {
        var mean = mutantSeconds.Count == 0 ? 0 : mutantSeconds.Average();
        var max = mutantSeconds.Count == 0 ? 0 : mutantSeconds.Max();
        var timing = new ReportTiming(buildSeconds, mean, max, totalSeconds, buildSeconds > 0 ? mean / buildSeconds : 0);
        var byFile = new List<(string Path, List<ReportMutant> Mutants)>();
        var operators = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);
        foreach (var op in Mutator.Operators) operators[op] = Counts.ToDictionary(c => c, _ => 0);
        foreach (var (op, n) in schema.NotEmitted) operators[op]["not_emitted"] = n;
        foreach (var m in result.Mutants)
        {
            if (m.Status == MutantStatus.NotEmitted) continue;
            var status = StatusName(m.Status);
            operators[m.Site.Site.Operator][status]++;
            var entry = new ReportMutant(m.Site, status, m.KilledBy, m.Status == MutantStatus.Survived ? Suggest(m.Site.Site.Operator) : null);
            var i = byFile.FindIndex(f => f.Path == m.Site.Site.File);
            if (i < 0) byFile.Add((m.Site.Site.File, [entry]));
            else byFile[i].Mutants.Add(entry);
        }
        var totals = Counts.ToDictionary(c => c, c => operators.Values.Sum(o => o[c]));
        return new QualityReport("mutation", tool, timing, byFile.Select(f => new ReportFile(f.Path, f.Mutants)).ToList(), totals,
            operators.ToDictionary(o => o.Key, o => (IReadOnlyDictionary<string, int>)o.Value));
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
        using (var w = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            w.WriteStartObject();
            w.WriteNumber("version", 1);
            w.WriteString("kind", Kind);
            w.WriteString("tool", Tool);
            w.WriteStartObject("timing");
            w.WriteNumber("build_s", Timing.BuildSeconds);
            w.WriteNumber("mean_mutant_s", Timing.MeanMutantSeconds);
            w.WriteNumber("max_mutant_s", Timing.MaxMutantSeconds);
            w.WriteNumber("total_s", Timing.TotalSeconds);
            w.WriteNumber("mutant_share_of_build", Timing.MutantShareOfBuild);
            w.WriteEndObject();
            w.WriteStartArray("files");
            foreach (var f in Files)
            {
                w.WriteStartObject();
                w.WriteString("path", f.Path);
                w.WriteStartArray("mutants");
                foreach (var m in f.Mutants)
                {
                    var s = m.Site.Site;
                    w.WriteStartObject();
                    w.WriteStartObject("site");
                    w.WriteNumber("id", m.Site.Id);
                    w.WriteString("file", s.File);
                    w.WriteNumber("line", s.Line);
                    w.WriteNumber("column", s.Column);
                    w.WriteString("operator", s.Operator);
                    w.WriteString("original", s.Original);
                    w.WriteString("mutated", s.Mutated);
                    if (m.Site.Unobservable is not null) w.WriteString("unobservable", m.Site.Unobservable);
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
            WriteCounts(w, "totals", Totals);
            w.WriteStartObject("operators");
            foreach (var (op, counts) in Operators) WriteCounts(w, op, counts);
            w.WriteEndObject();
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.WrittenSpan) + "\n";
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
            if (schema.Sites.Any(s => s.Site.File == mark.File && s.Unobservable == mark.Reason)) continue;
            var at = sources.FirstOrDefault(s => s.Path == mark.File) is { } src ? src.GetLinePosition(mark.Span.Start) : (Line: 0, Column: 0);
            warnings.Add($"warning: {mark.File}:{at.Line}:{at.Column}: the mark covers no mutant (`{mark.Reason}`)");
        }
        return warnings;
    }

    private static string Where(MutationSite s) => $"{s.File}:{s.Line}:{s.Column}";

    private static string Fmt(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>The terminal summary: totals, each survivor with its suggestion, per-operator counts, timing and the warnings.</summary>
    public string Summary(IReadOnlyList<string> warnings)
    {
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"mutation: {Totals["killed"]} killed, {Totals["survived"]} survived, {Totals["timeout"]} timeout, {Totals["marked"]} marked, {Totals["not_reached"]} not reached, {Totals["not_emitted"]} not emitted\n");
        foreach (var m in Files.SelectMany(f => f.Mutants).Where(m => m.Status == "survived"))
        {
            var s = m.Site.Site;
            sb.Append(CultureInfo.InvariantCulture, $"survived: {Where(s)}: {s.Original} → {s.Mutated} [{s.Operator}]\n  suggestion: {m.Suggestion}\n");
        }
        sb.Append("operators:\n");
        foreach (var (op, counts) in Operators)
            sb.Append(CultureInfo.InvariantCulture, $"  {op}: {string.Join(", ", Counts.Select(c => $"{counts[c]} {c.Replace('_', ' ')}"))}\n");
        sb.Append(CultureInfo.InvariantCulture, $"timing: build {Fmt(Timing.BuildSeconds)} s, mean {Fmt(Timing.MeanMutantSeconds)} s and max {Fmt(Timing.MaxMutantSeconds)} s per mutant, total {Fmt(Timing.TotalSeconds)} s, mutant share of build {Fmt(Timing.MutantShareOfBuild * 100)}%\n");
        foreach (var w in warnings) sb.Append(w).Append('\n');
        return sb.ToString();
    }
}
