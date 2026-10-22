using Bayan.Semantics.Symbols;
using Bayan.Syntax.Ast;
using Bayan.Syntax.Diagnostics;

namespace Bayan.Semantics.Binding;

/// <summary>
/// BYN2310: a <c>Span&lt;T&gt;</c> parameter that the function only reads should be a
/// <c>ReadOnlySpan&lt;T&gt;</c>, so callers can pass strings and read-only views.
/// </summary>
internal static class SpanParameterLint
{
    public static void Analyze(Compilation c, FunctionSymbol fn)
    {
        if (fn.File is null || fn.Body is null) return;
        var candidates = fn.Parameters
            .Where(p => p.Modifier == ParamModifier.None && p.Type is SpanType { ReadOnly: false } && p.Syntax?.Type is NamedType)
            .ToHashSet<LocalSymbol>();
        if (candidates.Count == 0) return;
        foreach (var node in BoundTree.Descendants(fn.Body))
        {
            switch (node)
            {
                case BoundAssign a:
                    Written(a.Target);
                    break;
                case BoundCall call:
                    if (call.Receiver is not null) Written(call.Receiver);
                    for (var i = 0; i < call.Arguments.Length && i < call.Function.Parameters.Count; i++)
                        if (call.Function.Parameters[i].Modifier is not ParamModifier.None) Written(call.Arguments[i]);
                    break;
                case BoundBuiltinCall b when b.Member is not (BuiltinMember.SpanLength or BuiltinMember.SpanIsEmpty or BuiltinMember.SpanCopy):
                    Written(b.Receiver);
                    break;
            }
            foreach (var child in BoundTree.Children(node))
                if (child is BoundLocal l && candidates.Contains(l.Local) && !ReadOnlyUse(node, l))
                    candidates.Remove(l.Local);
        }
        foreach (var p in fn.Parameters)
        {
            if (!candidates.Contains(p)) continue;
            var type = (NamedType)p.Syntax!.Type;
            var element = ((SpanType)p.Type).Element.DisplayName;
            c.Report(DiagnosticCodes.SpanParameterNeverWritten, fn.File.Source, type.Span, [p.Name, element],
                help: "take a `ReadOnlySpan`; it also accepts strings and read-only views",
                fix: new Fix(type.Path[^1].Span, "ReadOnlySpan"));
        }

        void Written(BoundExpr target)
        {
            if (Root(target) is { } root) candidates.Remove(root);
        }
    }

    /// <summary>Uses that only read the span's elements: indexing, length, iteration, copying, viewing read-only.</summary>
    private static bool ReadOnlyUse(BoundNode parent, BoundLocal use) => parent switch
    {
        BoundIndex i => i.Target == use,
        BoundBuiltinCall { Member: BuiltinMember.SpanLength or BuiltinMember.SpanIsEmpty or BuiltinMember.SpanCopy } => true,
        BoundFor f => f.Iterable == use,
        BoundConversion { Type: SpanType { ReadOnly: true } } => true,
        BoundCall c => Array.IndexOf(c.Arguments, use) is var i and >= 0 && i < c.Function.Parameters.Count
            && c.Function.Parameters[i] is { Modifier: ParamModifier.None, Type: SpanType { ReadOnly: true } },
        _ => false,
    };

    /// <summary>The span parameter an lvalue path goes through (<c>p[i]</c>, <c>p[i].f</c>, <c>p[a..b][i]</c>).</summary>
    private static LocalSymbol? Root(BoundExpr e) => e switch
    {
        BoundLocal l => l.Local,
        BoundField f => Root(f.Receiver),
        BoundTupleElement t => Root(t.Receiver),
        BoundIndex i => Root(i.Target),
        BoundSlice s => Root(s.Target),
        _ => null,
    };
}
