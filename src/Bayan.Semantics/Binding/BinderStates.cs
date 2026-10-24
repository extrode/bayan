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
        // A static method has no `this` to change; one that takes neither `ref this` nor `move this` is BYN2115 (Question 297).
        if (fn.IsStatic)
            Report(DiagnosticCodes.Unsupported, new TextSpan(b.Span.Start, "become".Length), ["`become` in a static method"],
                help: "remove `become`; only an instance method has a `this` to change");
        else if (fn.Decl?.MoveThis is null)
            CheckWritable(new BoundThis(b, owner!), new TextSpan(b.Span.Start, "become".Length), "`become` changes its state");
        if (fn.Decl?.Becomes is not { } becomes)
            Report(DiagnosticCodes.BecomeUndeclared, new TextSpan(b.Span.Start, "become".Length), [state.Name, fn.Name],
                help: $"add `becomes .{state.Name}` to the signature of `{fn.Name}`",
                fix: fn.Decl is { } decl ? new Fix(new TextSpan(AfterParams(decl), 0), $" becomes .{state.Name}") : null);
        else if (becomes.Text != state.Name)
            Report(DiagnosticCodes.BecomeOtherState, b.State.Span, [state.Name, fn.Name, becomes.Text],
                help: $"change the target to `.{becomes.Text}`, or declare `becomes .{state.Name}`");
        var payload = BindArguments(call, state.Fields, null, enforceNames: false, displayName: "." + state.Name);
        // The payload reads the old state; after it, `this` is in the new one.
        ForgetState(_thisFacts);
        LearnState(_thisFacts, new StateType(owner!, [state]));
        return new BoundBecome(b, state, payload);
    }

    /// <summary>
    /// Where a <c>becomes</c> clause goes in <paramref name="decl"/>'s signature: after the <c>)</c> that closes the
    /// parameters and any <c>where</c> and <c>expects</c> clauses, before <c>fails</c> and <c>uses</c> (BYN1065's order).
    /// </summary>
    private int AfterParams(FunctionDecl decl)
    {
        var at = decl.Params.Length > 0 ? decl.Params[^1].Span.End : decl.ParamsStart;
        var bytes = Source.Bytes.Span;
        while (at < bytes.Length && bytes[at] != (byte)')') at++;
        at = Math.Min(at + 1, bytes.Length);
        foreach (var clause in decl.Where) at = Math.Max(at, clause.Span.End);
        foreach (var clause in decl.Requires) at = Math.Max(at, clause.Span.End);
        return at;
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
        var known = value is BoundThis ? (TypeSymbol?)ThisStates() ?? value.Type : value.Type;
        var of = known is StateType ks ? ks.Struct : known;
        if (!of.Equals(wanted.Struct) || Conversions.Implicit(known, wanted) is not null) return false;
        Report(DiagnosticCodes.WrongState, span, [callee, value is BoundThis ? "this" : Source.GetText(span), string.Join(" or .", wanted.States.Select(v => v.Name)), known.DisplayName],
            help: "check the state with `is` or `switch` first");
        return true;
    }

    /// <summary>The states union of a struct with states, or of a state type, through an optional; null for any other type.</summary>
    private UnionSymbol? StatesUnion(TypeSymbol t) => Unwrap(t) switch
    {
        StateType st => _c.StatesOf(st.Struct),
        StructSymbol s => _c.StatesOf(s),
        _ => null,
    };

    /// <summary>The struct of a state type, or the struct itself; null for any other type.</summary>
    private static StructSymbol? StateStruct(TypeSymbol t) => t switch
    {
        StateType st => st.Struct,
        StructSymbol s => s,
        _ => null,
    };

    /// <summary>The states a value of <paramref name="t"/> may be in: those its state type lists, else every state.</summary>
    private static IReadOnlyList<UnionVariantSymbol> PossibleStates(TypeSymbol t, UnionSymbol states) =>
        Unwrap(t) is StateType st ? st.States : states.Variants;

    /// <summary>
    /// <c>o.payment</c>, a payload field: readable when every state <paramref name="target"/> may be in has it with the same type
    /// (spec §3.17), else BYN7002. Null when no state has a field of that name.
    /// </summary>
    private BoundExpr? BindPayloadField(Node syntax, BoundExpr target, UnionSymbol states, Name member)
    {
        var name = member.Text;
        if (states.Variants.FirstOrDefault(v => v.Fields.Any(f => f.Name == name)) is not { } holder) return null;
        var known = target is BoundThis && ThisStates() is { } own ? own.States : PossibleStates(target.Type, states);
        var index = known[0].Fields.FindIndex(f => f.Name == name);
        if (index >= 0 && known.All(v => v.Fields.FindIndex(f => f.Name == name) == index
            && v.Fields.Take(index + 1).Select(f => f.Type).SequenceEqual(known[0].Fields.Take(index + 1).Select(f => f.Type))))
            // Every state holds the field after the same field types, so C reads it through any of them (a common initial sequence).
            return new BoundUnionField(syntax, target, known[0], index);
        if (index >= 0 && known.All(v => v.Fields.Any(f => f.Name == name && f.Type.Equals(known[0].Fields[index].Type))))
        {
            Report(DiagnosticCodes.Unsupported, syntax.Span, [$"reading `{name}` from states that hold it at different places"],
                help: $"match one state with `is` or `switch` before reading `{name}`");
            return new BoundErrorExpr(syntax);
        }
        var what = target is BoundThis ? "this" : Source.GetText(target.Syntax.Span);
        Report(DiagnosticCodes.PayloadNotInState, syntax.Span, [name, holder.Name, what],
            help: $"match `.{holder.Name}` with `is` or `switch` before reading `{name}`");
        return new BoundErrorExpr(syntax);
    }

    /// <summary>
    /// BYN7004: a <c>switch</c> on a value with states must handle each state it may be in, unless a <c>default</c> or an
    /// irrefutable arm covers the rest. A statement's fix adds an empty <c>case .s:</c> section; an arm needs a value, so an
    /// expression gets none (Question 296).
    /// </summary>
    private bool CheckStatesExhaustive(Node s, TypeSymbol type, UnionSymbol states, IReadOnlyList<(BoundPattern? Pattern, BoundExpr? Guard)> arms)
    {
        var covered = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (pattern, guard) in arms)
        {
            if (pattern is null) return true;
            if (guard is not null) continue;
            if (Irrefutable(pattern)) return true;
            Collect(pattern, type, covered);
        }
        var missing = PossibleStates(type, states).Where(v => !covered.Contains("." + v.Name)).ToList();
        if (missing.Count == 0) return true;
        var bytes = Source.Bytes.Span;
        var keyword = s is SwitchExpr e ? e.Value.Span.End + bytes[e.Value.Span.End..].IndexOf("switch"u8) : s.Span.Start;
        var last = s is SwitchStmt { Sections.Length: > 0 } st ? st.Sections[^1] : null;
        var indent = 0;
        if (last is not null)
            while (last.Span.Start - indent > 0 && bytes[last.Span.Start - indent - 1] is (byte)' ' or (byte)'\t') indent++;
        foreach (var v in missing)
            Report(DiagnosticCodes.StateSwitchNotExhaustive, new TextSpan(keyword, "switch".Length), [v.Name],
                help: $"add a `case .{v.Name}` arm",
                fix: last is null ? null : new Fix(new TextSpan(last.Span.End, 0), "\n" + Source.GetText(new TextSpan(last.Span.Start - indent, indent)) + $"case .{v.Name}:"));
        return false;
    }

    // ── What a local is known to be (spec §3.17) ─────────────────────────────────

    /// <summary>
    /// The states locals are known to be in, each with the scope that learned it: after <c>is</c>, in a switch arm, after
    /// a <c>becomes</c> call or an assignment. The latest fact whose scope is still open wins.
    /// </summary>
    private readonly List<(Scope Scope, LocalSymbol Local, StateType Type)> _stateFacts = new();

    /// <summary>Stands for <c>this</c> in <see cref="_stateFacts"/>, so an <c>is</c> or a switch arm narrows it like a local.</summary>
    private readonly LocalSymbol _thisFacts = new("this", null, ErrorType.Instance, LocalKind.Let);

    /// <summary>The states <c>this</c> is known to be in: a fact, else the states its receiver declares; null when any.</summary>
    private StateType? ThisStates() => KnownState(_thisFacts) ?? (_fn is { } fn ? ReceiverStates(fn) : null);

    /// <summary>The fact key and current type of a narrowable subject: a local, or <c>this</c>.</summary>
    private (LocalSymbol Key, TypeSymbol Type)? Subject(BoundExpr e) => e switch
    {
        BoundLocal l => (l.Local, l.Type),
        BoundThis t => (_thisFacts, ThisStates() ?? t.Type),
        _ => null,
    };

    private bool IsOpen(Scope scope)
    {
        for (var s = _scope; s is not null; s = s.Parent)
            if (s == scope) return true;
        return false;
    }

    private StateType? KnownState(LocalSymbol local)
    {
        for (var i = _stateFacts.Count - 1; i >= 0; i--)
            if (_stateFacts[i].Local == local && IsOpen(_stateFacts[i].Scope)) return _stateFacts[i].Type;
        return null;
    }

    private void LearnState(LocalSymbol local, StateType type) => _stateFacts.Add((_scope, local, type));

    /// <summary>The state of <paramref name="local"/> may have changed: every fact about it is dropped, also outside the current scope.</summary>
    private void ForgetState(LocalSymbol local) => _stateFacts.RemoveAll(f => f.Local == local);

    /// <summary>
    /// At the start of a loop, what a <c>var</c> or <c>ref</c> parameter was known to be no longer holds: the loop body may change
    /// it after the reads that come first.
    /// </summary>
    private void ForgetChangingStates() => _stateFacts.RemoveAll(f => f.Local.IsMutable || f.Local is ParameterSymbol { IsByRef: true }
        || f.Local == _thisFacts && _fn is { IsMutating: true });

    /// <summary>Learns, for the code that runs when <paramref name="cond"/> is <paramref name="whenTrue"/>, the states it proves.</summary>
    private List<(Scope, LocalSymbol, StateType)> LearnStates(BoundExpr cond, bool whenTrue)
    {
        var learned = new List<(Scope, LocalSymbol, StateType)>();
        Collect(cond, whenTrue);
        _stateFacts.AddRange(learned);
        return learned;

        void Collect(BoundExpr c, bool when)
        {
            switch (c)
            {
                case BoundIs i when Subject(i.Value) is { } s && Narrow(s.Type, i.Pattern, when) is { } type:
                    learned.Add((_scope, s.Key, type));
                    break;
                case BoundUnary { Op: UnaryOp.Not } u:
                    Collect(u.Operand, !when);
                    break;
                case BoundBinary { Op: BinaryOp.LogicalAnd or BinaryOp.LogicalOr } b when when == (b.Op == BinaryOp.LogicalAnd):
                    Collect(b.Left, when);
                    Collect(b.Right, when);
                    break;
            }
        }
    }

    /// <summary>Drops the facts <see cref="LearnStates"/> added, and those of scopes that have closed.</summary>
    private void UnlearnStates(List<(Scope, LocalSymbol, StateType)> learned)
    {
        foreach (var f in learned) _stateFacts.Remove(f);
        _stateFacts.RemoveAll(f => !IsOpen(f.Scope));
    }

    /// <summary>In a switch arm, the subject local or <c>this</c> is in the states the arm's pattern matches.</summary>
    private void LearnArmState(BoundExpr subject, BoundPattern pattern)
    {
        if (Subject(subject) is { } s && Narrow(s.Type, pattern, true) is { } type) LearnState(s.Key, type);
    }

    /// <summary>
    /// A value of type <paramref name="type"/>, once <paramref name="pattern"/> is known to have matched (or, with
    /// <paramref name="matched"/> false, not to have matched); null when that proves nothing about its state.
    /// </summary>
    private StateType? Narrow(TypeSymbol type, BoundPattern pattern, bool matched)
    {
        if (type is OptionalType || StatesUnion(type) is not { } states) return null;
        var possible = PossibleStates(type, states);
        // A match proves the tag even when a payload subpattern also had to hold; a failed match proves it only for a bare tag test.
        if (StatesOf(pattern, exact: !matched) is not { } tested) return null;
        var left = possible.Where(v => tested.Contains(v) == matched).ToList();
        if (left.Count == 0 || left.Count == possible.Count) return null;
        return new StateType(type is StateType st ? st.Struct : (StructSymbol)type, left);

        HashSet<UnionVariantSymbol>? StatesOf(BoundPattern p, bool exact) => p switch
        {
            BoundUnionPattern up when !exact || up.Subpatterns.All(Irrefutable) => [up.Variant],
            BoundBinaryPattern { IsAnd: false } b when StatesOf(b.Left, exact) is { } l && StatesOf(b.Right, exact) is { } r => [.. l, .. r],
            BoundBinaryPattern { IsAnd: true } b when Irrefutable(b.Right) => StatesOf(b.Left, exact),
            BoundNotPattern n when StatesOf(n.Operand, !exact) is { } inner => states.Variants.Where(v => !inner.Contains(v)).ToHashSet(),
            _ => null,
        };
    }

    /// <summary>
    /// After <c>o.pay(…)</c> or <c>try o.pay(…)</c> as a statement, where <c>pay</c> takes <c>ref this</c> and <c>becomes .paid</c>,
    /// the local <c>o</c> is <c>Order.paid</c>. A call that may fail and is handled in place leaves the state unknown.
    /// </summary>
    private void LearnBecomes(BoundExpr e)
    {
        if ((e is BoundTry t ? t.Operand : e) is BoundCall { Function: { IsMutating: true, Decl.Becomes: { } to, Owner: { } owner } fn, Receiver: BoundLocal l }
            && (!fn.Fails || e is BoundTry) && _c.StatesOf(owner)?.Find(to.Text) is { } state)
            LearnState(l.Local, new StateType(owner, [state]));
    }
}
