using Bayan.Semantics.Symbols;
using Bayan.Syntax.Ast;
using Bayan.Syntax.Diagnostics;
using Bayan.Syntax.Text;

namespace Bayan.Semantics.Binding;

/// <summary>Typestate (spec §3.17): <c>become</c>, and the state a receiver or argument must be in.</summary>
internal sealed partial class Binder
{
    /// <summary><c>become .s(…)</c>: the state must be one of the struct's, and the one the signature's <c>becomes</c> declares (BYN7003).</summary>
    private BoundStmt BindBecome(BecomeStmt b)
    {
        var call = new CallExpr(b.Span, new NameExpr(b.State.Span, b.State, []), b.Args);
        var owner = _fn?.Owner;
        if ((owner is null ? null : _c.StatesOf(owner)?.Find(b.State.Text)) is not { } state)
        {
            var name = owner?.Name ?? _fn?.Name ?? "?";
            Report(DiagnosticCodes.UnknownState, b.State.Span, [name, b.State.Text], help: $"check the states declared in `{name}`");
            BindArgumentsForRecovery(call);
            return new BoundErrorStmt(b);
        }
        var fn = _fn!;
        if (fn.Decl?.Becomes is not { } becomes)
            Report(DiagnosticCodes.BecomeUndeclared, new TextSpan(b.Span.Start, "become".Length), [state.Name, fn.Name],
                help: $"add `becomes .{state.Name}` to the signature of `{fn.Name}`",
                fix: fn.Decl is { } decl ? new Fix(new TextSpan(AfterParams(decl), 0), $" becomes .{state.Name}") : null);
        else if (becomes.Text != state.Name)
            Report(DiagnosticCodes.BecomeOtherState, b.State.Span, [state.Name, fn.Name, becomes.Text],
                help: $"change the target to `.{becomes.Text}`, or declare `becomes .{state.Name}`");
        var payload = BindArguments(call, state.Fields, null, enforceNames: false, displayName: "." + state.Name);
        return new BoundBecome(b, state, payload);
    }

    /// <summary>Just past the <c>)</c> that closes <paramref name="decl"/>'s parameters, where a <c>becomes</c> clause goes.</summary>
    private int AfterParams(FunctionDecl decl)
    {
        var at = decl.Params.Length > 0 ? decl.Params[^1].Span.End : decl.ParamsStart;
        var bytes = Source.Bytes.Span;
        while (at < bytes.Length && bytes[at] != (byte)')') at++;
        return Math.Min(at + 1, bytes.Length);
    }

    /// <summary>The states <paramref name="fn"/>'s receiver must be in (<c>this is .a or .b</c>), or null when any will do.</summary>
    private StateType? ReceiverStates(FunctionSymbol fn)
    {
        if (fn.Decl is not { ReceiverStates.Length: > 0 } decl || fn.Owner is not { } owner || _c.StatesOf(owner) is not { } states) return null;
        var found = decl.ReceiverStates.Select(n => states.Find(n.Text)).OfType<UnionVariantSymbol>().ToList();
        return found.Count == 0 ? null : new StateType(owner, found);
    }

    /// <summary>
    /// BYN7001: <paramref name="value"/>, passed to <paramref name="callee"/> where <paramref name="wanted"/> is needed, is of the same
    /// struct but not known to be in one of its states. Inside a method, <c>this</c> is in the states its receiver declares.
    /// </summary>
    private bool ReportWrongState(string callee, BoundExpr value, StateType wanted, TextSpan span)
    {
        var known = value is BoundThis && _fn is { } fn && ReceiverStates(fn) is { } own ? own : value.Type;
        var of = known is StateType ks ? ks.Struct : known;
        if (!of.Equals(wanted.Struct) || Conversions.Implicit(known, wanted) is not null) return false;
        Report(DiagnosticCodes.WrongState, span, [callee, value is BoundThis ? "this" : Source.GetText(span), string.Join(" or .", wanted.States.Select(v => v.Name)), known.DisplayName],
            help: "check the state with `is` or `switch` first");
        return true;
    }
}
