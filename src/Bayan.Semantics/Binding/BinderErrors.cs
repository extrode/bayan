using Bayan.Semantics.Symbols;
using Bayan.Syntax.Ast;
using Bayan.Syntax.Diagnostics;

namespace Bayan.Semantics.Binding;

internal sealed partial class Binder
{
    // ── Error-case payloads ──────────────────────────────────────────────────────

    /// <summary>
    /// <c>.timeout(after)</c>, target-typed by an error set, or <c>CarrierError.timeout(after)</c>: the case built with its
    /// payload. Null when <paramref name="c"/> does not call an error case, so it binds as any other call.
    /// </summary>
    private BoundExpr? BindErrorNew(CallExpr c, TypeSymbol? expected)
    {
        BoundExpr callee;
        switch (c.Callee)
        {
            case TargetMemberExpr t when (expected is OptionalType o ? o.Inner : expected is ResultType r ? r.Error : expected) is ErrorSetSymbol or ErrorUnionType:
                callee = BindTargetMember(t, expected);
                break;
            case MemberExpr { NullConditional: false, Target: NameExpr { TypeArgs.Length: 0 } n } m
                when _scope.Lookup(n.Name.Text) is null && Module.Types.TryGetValue(n.Name.Text, out var type) && type is ErrorSetSymbol:
                callee = BindMember(m);
                break;
            default:
                return null;
        }
        if (callee is BoundErrorVariant ev) return BindPayload(c, ev);
        BindArgumentsForRecovery(c);
        return new BoundErrorExpr(c);
    }

    /// <summary>The arguments of <paramref name="c"/> as the payload of <paramref name="variant"/>; a case without one takes none (BYN2706).</summary>
    private BoundExpr BindPayload(CallExpr c, BoundErrorVariant variant)
    {
        var v = variant.Variant;
        if (v.Fields.Count == 0)
        {
            Report(DiagnosticCodes.VariantHasNoPayload, c.Span, [v.Name], help: $"write `.{v.Name}` without parentheses");
            BindArgumentsForRecovery(c);
            return new BoundErrorExpr(c);
        }
        return variant with { Syntax = c, Payload = BindArguments(c, v.Fields, null, enforceNames: false, v.Name) };
    }

    /// <summary>BYN2705 for a case with a payload written bare where it is built, as in <c>fail .timeout</c>.</summary>
    private bool CheckPayloadGiven(BoundExpr error, Expr syntax)
    {
        if (error is not BoundErrorVariant { Payload.Length: 0, Variant: { Fields.Count: > 0 } v }) return true;
        Report(DiagnosticCodes.VariantNeedsPayload, syntax.Span, [v.Name],
            help: $"pass it: `.{v.Name}({string.Join(", ", v.Fields.Select(f => f.Name))})`");
        return false;
    }
}
