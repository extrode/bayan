using Bayan.Semantics.Symbols;
using Bayan.Syntax.Ast;
using Bayan.Syntax.Diagnostics;
using Bayan.Syntax.Text;

namespace Bayan.Semantics.Binding;

/// <summary>
/// Ownership of unique values (spec §3.11, §9.3), run once every body is bound: an existing
/// unique value may not be stored without <c>move</c> or <c>copy</c> (BYN2780), and a moved local
/// may not be used on any path until it is assigned again (BYN2781). Generic templates are
/// checked with their type parameters taken as unique; instances are not checked again.
/// </summary>
internal sealed class OwnershipAnalysis
{
    private readonly Compilation _c;
    private readonly FunctionSymbol _fn;
    private readonly SourceText _source;
    private readonly Dictionary<JumpTarget, State> _breaks = new();
    private readonly Dictionary<JumpTarget, State> _continues = new();
    private readonly HashSet<(string, TextSpan)> _reported = new();

    private OwnershipAnalysis(Compilation c, FunctionSymbol fn)
    {
        _c = c;
        _fn = fn;
        _source = fn.File!.Source;
    }

    public static void Analyze(Compilation c, FunctionSymbol fn)
    {
        if (fn.File is null || fn.Body is null) return;
        var a = new OwnershipAnalysis(c, fn);
        a.Block(fn.Body.Statements, new State());
    }

    /// <summary>Reachability plus the locals moved on some path, with where.</summary>
    private sealed class State
    {
        public bool Reachable = true;
        public Dictionary<LocalSymbol, TextSpan> Moved = new();

        public State Clone() => new() { Reachable = Reachable, Moved = new(Moved) };

        public static State Unreachable() => new() { Reachable = false };

        /// <summary>Paths meeting: moved if moved on any reachable path.</summary>
        public State Join(State other)
        {
            if (!Reachable) return other.Clone();
            if (!other.Reachable) return Clone();
            var r = Clone();
            foreach (var (l, site) in other.Moved) r.Moved.TryAdd(l, site);
            return r;
        }

        public bool SameAs(State other) =>
            Reachable == other.Reachable && Moved.Count == other.Moved.Count && Moved.Keys.All(other.Moved.ContainsKey);
    }

    private void Report(DiagnosticDescriptor d, TextSpan span, object?[] args, string? label = null, string? help = null, Fix? fix = null)
    {
        if (_reported.Add((d.Code, span))) _c.Report(d, _source, span, args, label, help, fix);
    }

    // ── Statements ───────────────────────────────────────────────────────────────

    private State Block(IEnumerable<BoundStmt> statements, State state)
    {
        foreach (var s in statements) state = Statement(s, state);
        return state;
    }

    private State Statement(BoundStmt s, State state)
    {
        switch (s)
        {
            case BoundBlock b:
                return Block(b.Statements, state);
            case BoundLocalDecl d:
                if (d.Initializer is not null)
                {
                    state = Expr(d.Initializer, state);
                    CheckStore(d.Initializer);
                }
                state.Moved.Remove(d.Local);
                return state;
            case BoundExprStmt e:
                return Expr(e.Expr, state);
            case BoundAssign a:
                if (a.Op is null && a.Target is BoundLocal { Local: var l })
                {
                    state = Expr(a.Value, state);
                    CheckStore(a.Value);
                    state.Moved.Remove(l);
                    return state;
                }
                state = Expr(a.Value, Expr(a.Target, state));
                CheckStore(a.Value);
                return state;
            case BoundReturn r:
                if (r.Value is not null)
                {
                    state = Expr(r.Value, state);
                    // A local or `move` parameter returned is going out of scope: it moves.
                    if (!IsMovable(StripConversions(r.Value))) CheckStore(r.Value);
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
                var otherwise = condition == true ? State.Unreachable() : i.Else is null ? state.Clone() : Statement(i.Else, state.Clone());
                return then.Join(otherwise);
            }
            case BoundWhile w:
            {
                // A move in the body reaches the next iteration: repeat until the entry state is stable.
                var entry = state;
                while (true)
                {
                    var head = Expr(w.Condition, entry.Clone());
                    var body = Statement(w.Body, w.Condition.Constant?.Value is false ? State.Unreachable() : head.Clone());
                    if (_continues.Remove(w.Target, out var cont)) body = body.Join(cont);
                    var next = state.Join(body);
                    if (next.SameAs(entry))
                        return Exit(w.Target, w.Condition.Constant?.Value is true ? State.Unreachable() : head);
                    entry = next;
                }
            }
            case BoundDoWhile d:
            {
                var entry = state;
                while (true)
                {
                    var body = Statement(d.Body, entry.Clone());
                    if (_continues.Remove(d.Target, out var cont)) body = body.Join(cont);
                    var afterCondition = Expr(d.Condition, body);
                    var next = state.Join(afterCondition);
                    if (next.SameAs(entry))
                        return Exit(d.Target, d.Condition.Constant?.Value is true ? State.Unreachable() : afterCondition);
                    entry = next;
                }
            }
            case BoundFor f:
            {
                state = Expr(f.Iterable, state);
                var entry = state;
                while (true)
                {
                    var body = Statement(f.Body, entry.Clone());
                    if (_continues.Remove(f.Target, out var cont)) body = body.Join(cont);
                    var next = state.Join(body);
                    if (next.SameAs(entry)) return Exit(f.Target, entry);
                    entry = next;
                }
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
            case BoundContext c:
                return Statement(c.Body, Expr(c.Allocator, state));
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
                if (state.Reachable && state.Moved.TryGetValue(l, out var site))
                    Report(DiagnosticCodes.UseAfterMove, e.Syntax.Span, [l.Name],
                        label: $"moved on line {_source.GetLinePosition(site.Start).Line}",
                        help: $"assign `{l.Name}` a new value before using it again, or move a `.copy(to: alloc)` of it instead");
                return state;
            case BoundMove m:
                state = Expr(m.Operand, state);
                if (IsMovable(m.Operand) && m.Operand is BoundLocal { Local: var moved } && TypeFacts.IsUnique(moved.Type)) state.Moved[moved] = m.Syntax.Span;
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
                    after = after.Join(Expr(arm.Value, branch));
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
                    var modifier = i < call.Function.Parameters.Count ? call.Function.Parameters[i].Modifier : ParamModifier.None;
                    if (modifier == ParamModifier.Out && arg is BoundLocal { Local: var l })
                    {
                        outs.Add(l);
                        continue;
                    }
                    state = Expr(arg, state);
                    if (modifier == ParamModifier.Move || (i == 0 && call.Function.Builtin is BuiltinFunction.OwnedNew or BuiltinFunction.UntrustedNew or BuiltinFunction.Trust))
                        CheckStore(arg);
                }
                foreach (var l in outs) state.Moved.Remove(l);
                return call.Type.IsNever ? State.Unreachable() : state;
            }
            case BoundCatch c:
            {
                state = Expr(c.Operand, state);
                return state.Join(Expr(c.Handler, state.Clone()));
            }
            case BoundJump j:
                return Statement(j.Jump, state);
            case BoundIs i:
                return Expr(i.Value, state);
            default:
            {
                foreach (var child in BoundTree.Children(e))
                    if (child is BoundExpr ce) state = Expr(ce, state);
                foreach (var stored in StoredOperands(e)) CheckStore(stored);
                return e.Type.IsNever && e is not BoundErrorExpr ? State.Unreachable() : state;
            }
        }
    }

    /// <summary>Operands that <paramref name="e"/> stores into a new location: elements, fields, payloads, inserted values.</summary>
    private static IEnumerable<BoundExpr> StoredOperands(BoundExpr e) => e switch
    {
        BoundCollection c => c.Elements,
        BoundStructLiteral s => s.Fields,
        BoundUnionNew u => u.Payload,
        BoundTuple t => t.Elements,
        BoundWith w => w.Updates.Select(u => u.Value),
        BoundBuiltinCall { Member: BuiltinMember.ListAdd } b => [b.Arguments[0]],
        _ => [],
    };

    // ── Implicit copies (BYN2780) ────────────────────────────────────────────────

    /// <summary><paramref name="value"/> is stored somewhere new: it must not be an existing unique value.</summary>
    private void CheckStore(BoundExpr value)
    {
        if (!IsPlace(value) || TypeFacts.WhyUnique(value.Type) is not { } why) return;
        var core = StripConversions(value);
        var span = core.Syntax.Span;
        var text = _source.GetText(span);
        var options = new List<string>();
        Fix? fix = null;
        if (IsMovable(core))
        {
            options.Add($"`move {text}` hands the value over");
            fix = new Fix(new TextSpan(span.Start, 0), "move ");
        }
        if (core.Type is ListType or MapType or BuiltinType { Kind: BuiltinKind.StrBuf })
        {
            options.Add($"`{text}.copy(to: alloc)` makes an independent copy");
            fix ??= new Fix(new TextSpan(span.End, 0), ".copy(to: alloc)");
        }
        if (core is BoundLocal { Local: ParameterSymbol { Modifier: ParamModifier.None } p })
            options.Add($"declaring the parameter `move {p.Name}` takes ownership");
        options.Add("passing it by `ref` shares it instead");
        Report(DiagnosticCodes.ImplicitCopy, span, [why], help: string.Join("; ", options), fix: fix);
    }

    /// <summary>A local or <c>move</c> parameter this function owns, which <c>move</c> can hand over.</summary>
    private static bool IsMovable(BoundExpr e) =>
        e is BoundLocal { Local: { Kind: LocalKind.Let or LocalKind.Var, BorrowsSubject: false } or ParameterSymbol { Modifier: ParamModifier.Move } };

    /// <summary>Whether <paramref name="e"/> reads existing storage (a local, parameter, field, element or <c>.value</c>) rather than making a fresh value.</summary>
    private static bool IsPlace(BoundExpr e) => e switch
    {
        BoundLocal or BoundThis or BoundField or BoundTupleElement or BoundIndex => true,
        BoundBuiltinCall { Member: BuiltinMember.OwnedValue or BuiltinMember.ResultValue or BuiltinMember.MapIndex
            or BuiltinMember.MapIndexSet or BuiltinMember.MapSlot or BuiltinMember.MapGetOr } => true,
        BoundConversion c => IsPlace(c.Operand),
        BoundConditional c => IsPlace(c.WhenTrue) || IsPlace(c.WhenFalse),
        BoundCoalesce c => IsPlace(c.Left) || IsPlace(c.Right),
        BoundSwitchExpr s => s.Arms.Any(a => IsPlace(a.Value)),
        // A `with` copies only the fields it does not replace.
        BoundWith { Target.Type: StructSymbol s } w => IsPlace(w.Target)
            && s.Fields.Any(f => !w.Updates.Any(u => u.Field == f) && TypeFacts.IsUnique(f.Type)),
        BoundTry t => IsPlace(t.Operand),
        BoundCatch c => IsPlace(c.Operand) || IsPlace(c.Handler),
        _ => false,
    };

    private static BoundExpr StripConversions(BoundExpr e) => e is BoundConversion c ? StripConversions(c.Operand) : e;
}
