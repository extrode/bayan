using Bayan.Semantics.Symbols;
using Bayan.Syntax.Ast;
using Bayan.Syntax.Diagnostics;
using Bayan.Syntax.Text;

namespace Bayan.Semantics.Binding;

/// <summary>
/// Checks that need control flow, run once every body is bound and error sets are inferred:
/// definite assignment (locals declared without a value, <c>out</c> parameters), missing
/// returns, unreachable code, that each <c>try</c> propagates only declared errors, and that a
/// function that <c>becomes</c> a state does not return normally without <c>become</c>.
/// </summary>
internal sealed class FlowAnalysis
{
    private readonly Compilation _c;
    private readonly FunctionSymbol _fn;
    private readonly SourceText _source;

    /// <summary>Locals that start unassigned: <c>var x;</c> and <c>out</c> parameters.</summary>
    private readonly HashSet<LocalSymbol> _tracked = new();
    private readonly HashSet<LocalSymbol> _reported = new();
    private readonly Dictionary<JumpTarget, State> _breaks = new();
    private readonly Dictionary<JumpTarget, State> _continues = new();

    private FlowAnalysis(Compilation c, FunctionSymbol fn)
    {
        _c = c;
        _fn = fn;
        _source = fn.File!.Source;
    }

    public static void Analyze(Compilation c, FunctionSymbol fn)
    {
        if (fn.File is null || fn.Body is null) return;
        new FlowAnalysis(c, fn).Run();
    }

    /// <summary>Reachability plus the set of tracked locals assigned on every path.</summary>
    private sealed class State
    {
        public bool Reachable = true;
        public HashSet<LocalSymbol> Assigned = new();
        /// <summary>A <c>become</c> ran on every path.</summary>
        public bool Became;

        public State Clone() => new() { Reachable = Reachable, Assigned = new(Assigned), Became = Became };

        public static State Unreachable() => new() { Reachable = false };

        /// <summary>Paths meeting: assigned only if assigned on every reachable path.</summary>
        public State Join(State other)
        {
            if (!Reachable) return other.Clone();
            if (!other.Reachable) return Clone();
            var r = Clone();
            r.Assigned.IntersectWith(other.Assigned);
            r.Became &= other.Became;
            return r;
        }
    }

    private void Run()
    {
        foreach (var p in _fn.Parameters.Where(p => p.Modifier == ParamModifier.Out)) _tracked.Add(p);
        var end = Block(_fn.Body!.Statements, new State());
        if (!end.Reachable) return;
        var closing = new TextSpan(Math.Max(_fn.Body.Syntax.Span.End - 1, 0), 1);
        var returns = _fn.ReturnType;
        if (!returns.IsVoid && !returns.IsError && _fn.Kind is not (FunctionKind.Main or FunctionKind.Test or FunctionKind.Constructor))
        {
            if (returns.IsNever)
                Report(DiagnosticCodes.NeverReturns, closing, [_fn.Name], help: "end every path with `panic(…)` or a call that never returns");
            else
                Report(DiagnosticCodes.MissingReturn, closing, [_fn.Name, returns.DisplayName],
                    help: "add a `return` (or `panic(…)`) on the paths that reach the end");
            return;
        }
        CheckOutAssigned(end, closing);
        CheckBecame(end, closing);
    }

    private void Report(DiagnosticDescriptor d, TextSpan span, object?[] args, string? label = null, string? help = null, Fix? fix = null) =>
        _c.Report(d, _source, span, args, label, help, fix);

    private void CheckOutAssigned(State state, TextSpan span)
    {
        foreach (var p in _fn.Parameters.Where(p => p.Modifier == ParamModifier.Out && !state.Assigned.Contains(p)))
            Report(DiagnosticCodes.OutNotAssigned, span, [p.Name], help: $"assign `{p.Name}` before every `return`");
    }

    /// <summary>BYN7009: a normal return from a function that <c>becomes .s</c> must follow a <c>become</c>; not after BYN7010.</summary>
    private void CheckBecame(State state, TextSpan span)
    {
        if (state.Became || _fn.Decl?.Becomes is not { } becomes || _fn is { Kind: FunctionKind.Method, IsStatic: false, Decl.RefThis: null }) return;
        Report(DiagnosticCodes.MissingBecome, span, [_fn.Name, becomes.Text], help: $"add `become .{becomes.Text}(…)` before returning, or fail");
    }

    // ── Statements ───────────────────────────────────────────────────────────────

    private State Block(IEnumerable<BoundStmt> statements, State state)
    {
        var warned = false;
        foreach (var s in statements)
        {
            if (!state.Reachable && !warned && s is not (BoundErrorStmt or BoundBlock { Statements.Length: 0 }))
            {
                Report(DiagnosticCodes.UnreachableCode, s.Syntax.Span, []);
                warned = true;
            }
            state = Statement(s, state);
        }
        return state;
    }

    private State Statement(BoundStmt s, State state)
    {
        switch (s)
        {
            case BoundBlock b:
                return Block(b.Statements, state);
            case BoundLocalDecl d:
                if (d.Initializer is null)
                {
                    _tracked.Add(d.Local);
                    return state;
                }
                state = Expr(d.Initializer, state);
                state.Assigned.Add(d.Local);
                return state;
            case BoundExprStmt e:
                return Expr(e.Expr, state);
            case BoundAssert a:
                return Expr(a.Condition, state);
            case BoundAssign a:
                if (a.Op is null && a.Target is BoundLocal { Local: var l })
                {
                    state = Expr(a.Value, state);
                    state.Assigned.Add(l);
                    return state;
                }
                return Expr(a.Value, Expr(a.Target, state));
            case BoundReturn r:
                if (r.Value is not null) state = Expr(r.Value, state);
                if (state.Reachable)
                {
                    CheckOutAssigned(state, r.Syntax.Span);
                    CheckBecame(state, r.Syntax.Span);
                }
                return State.Unreachable();
            case BoundFail f:
                Expr(f.Error, state);
                return State.Unreachable();
            case BoundBreak b:
                Merge(_breaks, b.Target, state);
                return State.Unreachable();
            case BoundContinue c:
                Merge(_continues, c.Target, state);
                return State.Unreachable();
            case BoundIf i:
            {
                state = Expr(i.Condition, state);
                var condition = i.Condition.Constant?.Value as bool?;
                var then = Statement(i.Then, condition == false ? State.Unreachable() : state.Clone());
                if (i.Never) then = State.Unreachable();
                var otherwise = condition == true ? State.Unreachable() : i.Else is null ? state.Clone() : Statement(i.Else, state.Clone());
                return then.Join(otherwise);
            }
            case BoundWhile w:
            {
                state = Expr(w.Condition, state);
                Statement(w.Body, w.Condition.Constant?.Value is false ? State.Unreachable() : state.Clone());
                var after = w.Condition.Constant?.Value is true ? State.Unreachable() : state;
                return Exit(w.Target, after);
            }
            case BoundDoWhile d:
            {
                var body = Statement(d.Body, state.Clone());
                if (_continues.Remove(d.Target, out var cont)) body = body.Join(cont);
                var afterCondition = Expr(d.Condition, body);
                var after = d.Condition.Constant?.Value is true ? State.Unreachable() : afterCondition;
                return Exit(d.Target, after);
            }
            case BoundFor f:
            {
                state = Expr(f.Iterable, state);
                Statement(f.Body, state.Clone());
                return Exit(f.Target, state);
            }
            case BoundSwitch sw:
            {
                state = Expr(sw.Value, state);
                var after = sw.Exhaustive ? State.Unreachable() : state.Clone();
                foreach (var section in sw.Sections)
                {
                    var branch = state.Clone();
                    if (section.Guard is not null) branch = Expr(section.Guard, branch);
                    after = after.Join(Block(section.Body, branch));
                }
                return Exit(sw.Target, after);
            }
            case BoundDefer d:
                Statement(d.Body, state.Clone());
                return state;
            case BoundErrdefer e:
                Statement(e.Body, state.Clone());
                return state;
            case BoundPanicDefer p:
                Statement(p.Body, state.Clone());
                return state;
            case BoundContext c:
                return Statement(c.Body, Expr(c.Allocator, state));
            case BoundBecome b:
                foreach (var a in b.Payload) state = Expr(a, state);
                state.Became = true;
                return state;
            case BoundErrorStmt { Syntax: BecomeStmt }:
                // An unknown state was already reported (BYN7007); count the `become` so BYN7009 does not follow it.
                state.Became = true;
                return state;
            default:
                return state;
        }
    }

    private static void Merge(Dictionary<JumpTarget, State> map, JumpTarget target, State state)
    {
        if (!state.Reachable) return;
        map[target] = map.TryGetValue(target, out var existing) ? existing.Join(state) : state.Clone();
    }

    private State Exit(JumpTarget target, State after)
    {
        _continues.Remove(target);
        return _breaks.Remove(target, out var b) ? after.Join(b) : after;
    }

    // ── Expressions ──────────────────────────────────────────────────────────────

    private State Expr(BoundExpr e, State state)
    {
        switch (e)
        {
            case BoundLocal { Local: var l }:
                if (state.Reachable && _tracked.Contains(l) && !state.Assigned.Contains(l) && _reported.Add(l))
                    Report(DiagnosticCodes.UseBeforeAssign, e.Syntax.Span, [l.Name],
                        label: l is ParameterSymbol ? "an `out` parameter starts unassigned" : $"declared without a value on line {l.Location?.Line}",
                        help: l is ParameterSymbol ? null : "assign it on every path before this, or give the declaration a value");
                return state;
            case BoundBinary { Op: BinaryOp.LogicalAnd or BinaryOp.LogicalOr } b:
            {
                state = Expr(b.Left, state);
                var right = Expr(b.Right, state.Clone());
                return state.Join(right);
            }
            case BoundConditional c:
            {
                state = Expr(c.Condition, state);
                return Expr(c.WhenTrue, state.Clone()).Join(Expr(c.WhenFalse, state.Clone()));
            }
            case BoundCoalesce c:
            {
                state = Expr(c.Left, state);
                return state.Join(Expr(c.Right, state.Clone()));
            }
            case BoundSwitchExpr s:
            {
                state = Expr(s.Value, state);
                var after = State.Unreachable();
                foreach (var arm in s.Arms)
                {
                    var branch = state.Clone();
                    if (arm.Guard is not null) branch = Expr(arm.Guard, branch);
                    branch = Expr(arm.Value, branch);
                    // A block arm of a value-giving `catch switch` must leave, as a `catch` block does (J49).
                    if (arm.Value is BoundBlockExpr { Type.IsNever: true } or BoundConversion { Operand: BoundBlockExpr { Type.IsNever: true } } && branch.Reachable)
                    {
                        Report(DiagnosticCodes.CatchBlockCompletes, new TextSpan(Math.Max(arm.Value.Syntax.Span.End - 1, 0), 1), [],
                            help: "leave the block instead of falling through; the call returns a value");
                        branch = State.Unreachable();
                    }
                    after = after.Join(branch);
                }
                return after;
            }
            case BoundCall call:
            {
                if (call.Receiver is not null) state = Expr(call.Receiver, state);
                var outs = new List<LocalSymbol>();
                for (var i = 0; i < call.Arguments.Length; i++)
                {
                    var arg = call.Arguments[i];
                    if (i < call.Function.Parameters.Count && call.Function.Parameters[i].Modifier == ParamModifier.Out && arg is BoundLocal { Local: var l })
                        outs.Add(l);
                    else
                        state = Expr(arg, state);
                }
                state.Assigned.UnionWith(outs);
                // A `becomes` call on `this` counts as `become` (BYN7003's third form makes the caller declare it; revision 0010).
                if (call is { Receiver: BoundThis, Function.Decl.Becomes: not null }) state.Became = true;
                return call.Type.IsNever ? State.Unreachable() : state;
            }
            case BoundTry t:
                CheckPropagation(t.Syntax.Span, t.Operand, null);
                state = Expr(t.Operand, state);
                return state;
            case BoundCatch c:
            {
                var became = state.Became;
                state = Expr(c.Operand, state);
                if (c.Variant is { } v && c.Operand is BoundCall call)
                {
                    if (!call.Function.ErrorSets.Contains(v.Set) && call.Function.ErrorSets.Count > 0 || call.Function.ExcludedErrors.Contains(v))
                        Report(DiagnosticCodes.UnknownVariant, ((CatchExpr)c.Syntax).Variant!.Span, [v.Name, ErrorUnionType.Of(call.Function.ErrorSets, call.Function.ExcludedErrors).DisplayName]);
                    else CheckPropagation(c.Syntax.Span, call, v);
                }
                // A failed `becomes` call left the state unchanged, so the handler's path has not run `become` (revision 0010 erratum).
                var failed = state.Clone();
                failed.Became = became;
                var handler = Expr(c.Handler, failed);
                if (c.Handler is BoundBlockExpr { Type.IsNever: true } b && handler.Reachable)
                {
                    Report(DiagnosticCodes.CatchBlockCompletes, new TextSpan(Math.Max(b.Syntax.Span.End - 1, 0), 1), [],
                        help: $"leave the block instead of falling through; `{(c.Operand as BoundCall)?.Function.Name}` returns a value");
                    handler = State.Unreachable();
                }
                return state.Join(handler);
            }
            case BoundJump j:
                return Statement(j.Jump, state);
            case BoundBlockExpr b:
                return Block(b.Block.Statements, state);
            case BoundIs { Pattern: BoundRangePattern r } i:
                return Expr(r.End, Expr(r.Start, Expr(i.Value, state)));
            case BoundIs i:
                return Expr(i.Value, state);
            default:
            {
                foreach (var child in BoundTree.Children(e))
                    if (child is BoundExpr ce) state = Expr(ce, state);
                return e.Type.IsNever && e is not BoundErrorExpr ? State.Unreachable() : state;
            }
        }
    }

    /// <summary>A <c>try</c> (or partial <c>catch</c>) passes the callee's errors on; the caller must be able to fail with them.</summary>
    private void CheckPropagation(TextSpan span, BoundExpr operand, ErrorVariantSymbol? caught)
    {
        if (operand is not BoundCall call) return;
        var sets = caught is null ? call.Function.ErrorSets : ErrorFlow.Uncaught(call.Function, caught).ToList();
        if (sets.Count == 0) return;
        if (_fn.InfersErrors || _fn.Kind is FunctionKind.Main or FunctionKind.Test) return;
        if (!_fn.Fails)
        {
            Report(DiagnosticCodes.PropagateFromInfallible, span, [_fn.Name, string.Join("`, `", sets.Select(s => s.DisplayName))],
                help: $"handle the error with `catch`, or declare `fails {string.Join(", ", sets.Select(s => s.DisplayName))}`");
            return;
        }
        foreach (var set in sets.Where(s => !_fn.ErrorSets.Contains(s)))
            Report(DiagnosticCodes.UndeclaredError, span, [set.DisplayName, _fn.Name],
                help: $"add it to the signature: `fails {string.Join(", ", _fn.ErrorSets.Select(s => s.DisplayName).Append(set.DisplayName))}`");
        // A case the caller takes out with `except` must not arrive from the callee.
        foreach (var v in sets.SelectMany(s => s.Variants).Where(v => v != caught && _fn.ExcludedErrors.Contains(v) && !call.Function.ExcludedErrors.Contains(v)))
            Report(DiagnosticCodes.UndeclaredError, span, [$"{v.Set.DisplayName}.{v.Name}", _fn.Name],
                help: $"handle it with `catch ({v.Set.DisplayName}.{v.Name})`, or remove `.{v.Name}` from the `except` list");
    }
}
