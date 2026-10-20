using Bayan.Semantics.Symbols;

namespace Bayan.Semantics.Binding;

/// <summary>Which error sets a body can fail with: <c>try</c>, partial <c>catch</c>, and <c>return .variant</c>.</summary>
internal static class ErrorFlow
{
    public static IEnumerable<ErrorSetSymbol> Propagated(BoundBlock body)
    {
        var sets = new List<ErrorSetSymbol>();
        foreach (var node in BoundTree.Descendants(body))
        {
            switch (node)
            {
                case BoundTry { Operand: BoundCall call }:
                    sets.AddRange(call.Function.ErrorSets);
                    break;
                case BoundCatch { Operand: BoundCall call, Variant: { } v }:
                    sets.AddRange(Uncaught(call.Function, v));
                    break;
                case BoundFail { Error.Type: var t }:
                    if (t is ErrorSetSymbol s) sets.Add(s);
                    else if (t is ErrorUnionType u) sets.AddRange(u.Sets);
                    break;
            }
        }
        return sets.Distinct();
    }

    /// <summary>The sets still propagated after <c>catch (Set.variant)</c> handles one variant.</summary>
    public static IEnumerable<ErrorSetSymbol> Uncaught(FunctionSymbol callee, ErrorVariantSymbol caught) =>
        callee.ErrorSets.Where(s => !(s == caught.Set && s.Variants.Count == 1));
}
