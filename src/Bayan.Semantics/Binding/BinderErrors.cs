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

    /// <summary>A value bound where it is built (a <c>let</c>, an argument, a <c>return</c>, a field): BYN2705 for a bare case
    /// with a payload. Patterns, <c>==</c> and <c>catch</c> filters bind the bare case without it.</summary>
    private BoundExpr Built(BoundExpr value, Expr syntax)
    {
        CheckPayloadGiven(value, syntax);
        return value;
    }

    // ── Error-case patterns ──────────────────────────────────────────────────────

    /// <summary>The <c>c</c> of <c>.timeout c</c>, mapped to its case, so <c>c.after</c> reads the payload.</summary>
    private readonly Dictionary<LocalSymbol, ErrorVariantSymbol> _errorNarrowed = new();

    /// <summary><c>CarrierError.timeout</c> in a pattern: the case of <paramref name="set"/> named <paramref name="name"/>.</summary>
    private BoundExpr ErrorCase(Pattern p, ErrorSetSymbol set, Name name)
    {
        if (set.Find(name.Text) is { } variant) return new BoundErrorVariant(p, variant);
        Report(DiagnosticCodes.UnknownVariant, name.Span, [name.Text, set.DisplayName],
            help: Suggest(name.Text, set.Variants.Select(v => v.Name)) is { } close ? $"did you mean `{close}`?" : null);
        return new BoundErrorExpr(p);
    }

    /// <summary>
    /// <c>.timeout(let after)</c> or <c>.timeout e</c> on an error (J29): a test of the case <paramref name="bound"/> names, then
    /// each element on its payload field, or <paramref name="binding"/> bound to the whole error.
    /// </summary>
    private BoundPattern BindErrorCasePattern(Pattern p, TypeSymbol input, BoundExpr bound, Pattern[] elements, Name? binding)
    {
        var target = Unwrap(input);
        ErrorVariantSymbol? variant = null;
        if (bound is BoundErrorVariant ev)
        {
            if (target is ErrorUnionType u && u.Sets.Count > 0 && (!u.Sets.Contains(ev.Variant.Set) || u.Excluded.Contains(ev.Variant))
                || target is ErrorSetSymbol s && s != ev.Variant.Set)
                Report(DiagnosticCodes.PatternMismatch, p.Span, [$"`{ev.Variant.Set.DisplayName}.{ev.Variant.Name}`", target.DisplayName]);
            else variant = ev.Variant;
        }
        if (binding is not null)
        {
            var local = DeclareLocal(binding, variant is null ? ErrorType.Instance : target, LocalKind.PatternVariable);
            if (variant is null) return new BoundBindPattern(p, input, null);
            _errorNarrowed[local] = variant;
            return new BoundBinaryPattern(p, input, true, new BoundErrorPattern(p, input, variant, []), new BoundBindPattern(p, input, local));
        }
        if (variant is { Fields.Count: 0 })
            Report(DiagnosticCodes.VariantHasNoPayload, p.Span, [variant.Name], help: $"match it with `.{variant.Name}`");
        else if (variant is not null && elements.Length != variant.Fields.Count)
            Report(DiagnosticCodes.VariantPatternArity, p.Span, [variant.Name, variant.Fields.Count, elements.Length],
                help: $"list every field: `.{variant.Name}({string.Join(", ", variant.Fields.Select(f => "let " + f.Name))})`, or `_` to skip one");
        var ok = variant is not null && variant.Fields.Count == elements.Length && elements.Length > 0;
        var subs = new BoundPattern[elements.Length];
        for (var i = 0; i < subs.Length; i++)
            subs[i] = BindPattern(elements[i], ok ? variant!.Fields[i].Type : ErrorType.Instance);
        return ok ? new BoundErrorPattern(p, input, variant!, subs) : new BoundBindPattern(p, input, null);
    }
}
