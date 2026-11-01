using System.Text.Json;

namespace Bayan.Testing;

/// <summary>A site as <c>&lt;binary&gt;.sites.json</c> carries it: the identity fields, its id in that binary, and whether it was emitted.</summary>
public sealed record ExternalSite(int Id, string File, int Line, int Column, string Operator, string Original, string Mutated, bool Emitted, string? Unobservable)
{
    public static ExternalSite From(SchemaSite s) =>
        new(s.Id, s.Site.File, s.Site.Line, s.Site.Column, s.Site.Operator, s.Site.Original, s.Site.Mutated, s.Emitted, s.Unobservable);

    /// <summary>Reads a site map; null when it is not one this version understands.</summary>
    public static IReadOnlyList<ExternalSite>? ReadAll(string json)
    {
        try
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
        catch (Exception e) when (e is JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        {
            return null;
        }
    }
}

/// <summary>A site's outcome under an external command, with the wall time of its run (0 when not run).</summary>
public sealed record ExternalResult(ExternalSite Site, MutantStatus Status, double Seconds);
