using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Bayan.Semantics;
using Bayan.Semantics.Binding;
using Bayan.Semantics.Symbols;
using Bayan.Syntax.Ast;
using Bayan.Syntax.Text;

namespace Bayan.Testing;

/// <summary>A mutant of a schemata build: its id in this binary, whether it is in the schema (it re-bound cleanly), and the reason of the <c>@unobservable</c> mark that covers it, if any.</summary>
public sealed record SchemaSite(int Id, MutationSite Site, bool Emitted, string? Unobservable = null);

/// <summary>
/// The mutants of one <c>--mutate</c> build (spec 013 R2): every site with its id, the candidates not emitted per operator
/// (the mutator's and those that failed to re-bind), and the switch the lowerer puts at each node a mutant replaces.
/// </summary>
public sealed record Schema(IReadOnlyList<SchemaSite> Sites, IReadOnlyDictionary<string, int> NotEmitted,
    IReadOnlyDictionary<BoundNode, BoundMutantSwitch> Switches)
{
    /// <summary>The site map, <c>&lt;binary&gt;.sites.json</c>.</summary>
    public string SitesJson()
    {
        var buffer = new System.Buffers.ArrayBufferWriter<byte>();
        using (var w = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true, NewLine = "\n", Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            w.WriteStartObject();
            w.WriteNumber("version", 1);
            w.WriteStartArray("sites");
            foreach (var s in Sites)
            {
                w.WriteStartObject();
                w.WriteNumber("id", s.Id);
                w.WriteString("file", s.Site.File);
                w.WriteNumber("line", s.Site.Line);
                w.WriteNumber("column", s.Site.Column);
                w.WriteString("operator", s.Site.Operator);
                w.WriteString("original", s.Site.Original);
                w.WriteString("mutated", s.Site.Mutated);
                if (!s.Emitted) w.WriteBoolean("emitted", false);
                if (s.Unobservable is not null) w.WriteString("unobservable", s.Unobservable);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.WrittenSpan) + "\n";
    }
}

/// <summary>
/// Builds the <see cref="Schema"/> of a compilation: ids 1..N in the mutator's site order, each mutant's file re-bound and
/// ownership-checked with the mutation applied (a failure is not emitted), and one switch per site at every bound node
/// of it, so the instances of a generic share their template's site.
/// </summary>
public static class Schemata
{
    /// <summary>Where a <c>--mutate</c> build writes its site map.</summary>
    public static string SitesPath(string binary) => binary + ".sites.json";

    public static Schema Build(Compilation c, IReadOnlySet<string>? skipFiles = null)
    {
        var found = Mutator.Find(c, skipFiles);
        var notEmitted = new Dictionary<string, int>(found.NotEmitted, StringComparer.Ordinal);
        var sites = new List<SchemaSite>();
        foreach (var site in found.Sites)
        {
            var emitted = Rebinds(c, site);
            if (!emitted) notEmitted[site.Operator]++;
            sites.Add(new SchemaSite(sites.Count + 1, site, emitted, MarkOf(c, site)));
        }
        var bySyntax = new Dictionary<Node, List<SchemaSite>>(ReferenceEqualityComparer.Instance);
        foreach (var s in sites.Where(s => s.Emitted))
        {
            if (!bySyntax.TryGetValue(s.Site.OriginalNode.Syntax, out var list)) bySyntax[s.Site.OriginalNode.Syntax] = list = new();
            list.Add(s);
        }
        var switches = new Dictionary<BoundNode, BoundMutantSwitch>(ReferenceEqualityComparer.Instance);
        foreach (var module in c.Modules)
            foreach (var fn in module.AllFunctions)
            {
                if (fn.Body is null || fn.Kind == FunctionKind.Test || fn.IsGeneric) continue;
                foreach (var node in BoundTree.Descendants(fn.Body))
                {
                    if (switches.ContainsKey(node) || !bySyntax.TryGetValue(node.Syntax, out var candidates)) continue;
                    var arms = new List<BoundMutant>();
                    foreach (var s in candidates)
                    {
                        if (s.Site.OriginalNode.GetType() != node.GetType()) continue;
                        var mutant = ReferenceEquals(node, s.Site.OriginalNode) ? s.Site.MutatedNode : Transfer(s.Site, node);
                        if (mutant is not null) arms.Add(new BoundMutant(s.Id, mutant));
                    }
                    if (arms.Count > 0) switches[node] = new BoundMutantSwitch(node.Syntax, node, arms.ToArray());
                }
            }
        return new Schema(sites, notEmitted, switches);
    }

    /// <summary>The reason of the first <c>@unobservable</c> mark whose statement holds the site and whose operator argument, if any, is the site's.</summary>
    private static string? MarkOf(Compilation c, MutationSite site)
    {
        if (c.Unobservable.Count == 0) return null;
        var start = Offset(site.Function.File!.Source, site.Line, site.Column);
        var end = start + Encoding.UTF8.GetByteCount(site.Original);
        foreach (var m in c.Unobservable)
            if (m.File == site.File && (m.Operator is null || m.Operator == site.Operator) && m.Span.Start <= start && end <= m.Span.Start + m.Span.Length)
                return m.Reason;
        return null;
    }

    /// <summary>
    /// Whether the program still compiles, ownership included, with the mutation applied to its source (user decision
    /// 2026-10-31: the mutator cannot see every error a mutant causes).
    /// </summary>
    private static bool Rebinds(Compilation c, MutationSite site)
    {
        var source = site.Function.File!.Source;
        var start = Offset(source, site.Line, site.Column);
        var length = Encoding.UTF8.GetByteCount(site.Original);
        if (source.GetText(new TextSpan(start, length)) != site.Original)
            throw new InvalidOperationException($"internal error: mutation site {site.File}:{site.Line}:{site.Column} is not `{site.Original}`");
        // `x = 1 if c` less its statement is ` if c`, which does not parse: the guarded statement goes whole.
        if (site.Operator == Mutator.Statement && BoundTree.Descendants(site.Function.Body!)
                .FirstOrDefault(n => n.Syntax is GuardedStmt g && ReferenceEquals(g.Statement, site.OriginalNode.Syntax))?.Syntax is { } guarded)
            (start, length) = (guarded.Span.Start, guarded.Span.Length);
        var bytes = source.Bytes.Span;
        var text = new byte[bytes.Length - length + Encoding.UTF8.GetByteCount(site.Mutated)];
        bytes[..start].CopyTo(text);
        var end = start + Encoding.UTF8.GetBytes(site.Mutated, text.AsSpan(start));
        bytes[(start + length)..].CopyTo(text.AsSpan(end));
        return !c.WithFile(new SourceText(source.Path, text)).Diagnostics.HasErrors;
    }

    /// <summary>The byte offset of a one-based line and column, the column counting Unicode scalars.</summary>
    private static int Offset(SourceText source, int line, int column)
    {
        var offset = source.GetLineSpan(line - 1).Start;
        var bytes = source.Bytes.Span;
        for (var col = 1; col < column; col++)
            do offset++; while (offset < bytes.Length && (bytes[offset] & 0xC0) == 0x80);
        return offset;
    }

    /// <summary>The mutant of <paramref name="site"/> at <paramref name="node"/>, a generic instance's twin of its original node; null where its type differs.</summary>
    private static BoundNode? Transfer(MutationSite site, BoundNode node)
    {
        var sameType = node is BoundExpr e && site.OriginalNode is BoundExpr o && e.Type == o.Type;
        return (site.Operator, site.MutatedNode, node) switch
        {
            (Mutator.Negation, _, BoundUnary u) => u.Operand,
            (Mutator.Condition, _, BoundExpr cond) => new BoundUnary(cond.Syntax, UnaryOp.Not, cond, PrimitiveType.Bool),
            (Mutator.Statement, _, BoundStmt s) => new BoundBlock(s.Syntax, []),
            (Mutator.Bool or Mutator.Literal, BoundLiteral m, BoundExpr l) when sameType => new BoundLiteral(l.Syntax, l.Type, m.Value) { Constant = m.Value },
            (Mutator.EmptyReturn, _, BoundExpr value) when sameType => new BoundDefault(value.Syntax, value.Type),
            (Mutator.Variant, BoundErrorVariant m, BoundErrorVariant v) => new BoundErrorVariant(v.Syntax, m.Variant) { Payload = v.Payload },
            (_, BoundBinary m, BoundBinary b) => b with { Op = m.Op, Constant = null },
            (_, BoundAssign m, BoundAssign a) => a with { Op = m.Op },
            _ => null,
        };
    }
}
