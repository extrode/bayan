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
    // Cleanups registered in the enclosing scopes, innermost last.
    private readonly List<Cleanup> _cleanups = new();
    // How many cleanups were active where each loop or switch began: a jump to it runs the rest.
    private readonly Dictionary<JumpTarget, int> _depths = new();

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

    /// <summary>
    /// A <c>defer</c>/<c>errdefer</c>: the locals its body reads and moves, and what was already moved when it was registered.
    /// A <c>defer panicking</c> (<paramref name="Panic"/>) runs at a panic anywhere in its scope, not at the scope's exit.
    /// </summary>
    private sealed record Cleanup(TextSpan Site, bool OnFailure, HashSet<LocalSymbol> Reads,
        List<(LocalSymbol Local, TextSpan Site)> Moves, Dictionary<LocalSymbol, TextSpan> MovedBefore, bool Panic = false);

    private void Report(DiagnosticDescriptor d, TextSpan span, object?[] args, string? label = null, string? help = null, Fix? fix = null)
    {
        if (_reported.Add((d.Code, span))) _c.Report(d, _source, span, args, label, help, fix);
    }

    // ── Statements ───────────────────────────────────────────────────────────────

    private State Block(IEnumerable<BoundStmt> statements, State state)
    {
        var depth = _cleanups.Count;
        foreach (var s in statements) state = Statement(s, state);
        RunCleanups(state, depth, failing: false);
        _cleanups.RemoveRange(depth, _cleanups.Count - depth);
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
            case BoundAssert a:
                return Expr(a.Condition, state);
            case BoundAssign a:
                if (a.Op is null && a.Target is BoundLocal { Local: var l })
                {
                    state = Expr(a.Value, state);
                    // `x = x with { … }` consumes the old `x`: a move, not a copy (revision 0007).
                    if (!IsSelfWith(a.Value, l)) CheckStore(a.Value);
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
                RunCleanups(state, 0, failing: false);
                return State.Unreachable();
            case BoundFail f:
                RunCleanups(Expr(f.Error, state), 0, failing: true);
                return State.Unreachable();
            case BoundBreak b:
                RunCleanups(state, _depths.GetValueOrDefault(b.Target), failing: false);
                Merge(_breaks, b.Target, state);
                return State.Unreachable();
            case BoundContinue c:
                RunCleanups(state, _depths.GetValueOrDefault(c.Target), failing: false);
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
                _depths[w.Target] = _cleanups.Count;
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
                _depths[d.Target] = _cleanups.Count;
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
                _depths[f.Target] = _cleanups.Count;
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
                _depths[sw.Target] = _cleanups.Count;
                var after = State.Unreachable();
                // A guard that ran and failed has made its moves: later sections start from that too.
                var next = state.Clone();
                foreach (var section in sw.Sections)
                {
                    var branch = Bind(section.Pattern, next.Clone());
                    if (section.Guard is not null) next = next.Join(branch = Expr(section.Guard, branch));
                    after = after.Join(Block(section.Body, branch.Clone()));
                }
                if (!sw.Exhaustive) after = after.Join(next);
                return Exit(sw.Target, after);
            }
            case BoundDefer d:
                Statement(d.Body, state.Clone());
                Register(d.Body, d.Syntax.Span, onFailure: false, state);
                return state;
            case BoundErrdefer e:
                Statement(e.Body, state.Clone());
                Register(e.Body, e.Syntax.Span, onFailure: true, state);
                return state;
            // A panic handler runs in place of the scope's cleanups, not at its exit: registered only so a later move of what it reads is BYN2615.
            case BoundPanicDefer p:
                Statement(p.Body, state.Clone());
                Register(p.Body, p.Syntax.Span, onFailure: false, state, panic: true);
                return state;
            case BoundContext c:
                return Statement(c.Body, Expr(c.Allocator, state));
            default:
                return state;
        }
    }

    /// <summary>A pattern's bindings hold a fresh value each time it matches, as a declaration does.</summary>
    private static State Bind(BoundPattern? pattern, State state)
    {
        if (pattern is null) return state;
        foreach (var node in BoundTree.Descendants(pattern))
            if (node is BoundBindPattern { Local: { } b }) state.Moved.Remove(b);
            else if (node is BoundPropertyPattern { Designation: { } d }) state.Moved.Remove(d);
            else if (node is BoundUnionPattern { Designation: { } u }) state.Moved.Remove(u);
        return state;
    }

    /// <summary>Registers a cleanup: the locals <paramref name="body"/> reads (not those it only assigns) and moves.</summary>
    private void Register(BoundStmt body, TextSpan site, bool onFailure, State state, bool panic = false)
    {
        var assigned = new HashSet<BoundNode>(ReferenceEqualityComparer.Instance);
        var reads = new HashSet<LocalSymbol>();
        var moves = new List<(LocalSymbol, TextSpan)>();
        foreach (var n in BoundTree.Descendants(body))
        {
            if (n is BoundAssign { Op: null, Target: BoundLocal target }) assigned.Add(target);
            else if (n is BoundLocal { Local: var l } && !assigned.Contains(n)) reads.Add(l);
            else if (n is BoundMove { Operand: BoundLocal { Local: var m } operand } move && IsMovable(operand) && TypeFacts.IsUnique(m.Type))
                moves.Add((m, move.Syntax.Span));
        }
        _cleanups.Add(new Cleanup(site, onFailure, reads, moves, new(state.Moved), panic));
    }

    /// <summary>
    /// Runs the cleanups from <paramref name="depth"/> up, innermost first, at an exit reached in <paramref name="state"/>:
    /// a local one reads that was moved after it was registered is a use after move, reported at the move.
    /// </summary>
    private void RunCleanups(State state, int depth, bool failing)
    {
        if (!state.Reachable) return;
        var moved = new Dictionary<LocalSymbol, TextSpan>(state.Moved);
        for (var i = _cleanups.Count - 1; i >= depth; i--)
        {
            var c = _cleanups[i];
            if (c.Panic || c.OnFailure && !failing) continue;
            foreach (var l in c.Reads)
                if (moved.TryGetValue(l, out var site) && !(c.MovedBefore.TryGetValue(l, out var before) && before == site))
                    Report(DiagnosticCodes.UseAfterMove, site, [l.Name],
                        label: $"{(c.OnFailure ? "a `defer failing`" : "a `defer`")} on line {_source.GetLinePosition(c.Site.Start).Line} uses it at scope exit",
                        help: $"move `{l.Name}` before the `{(c.OnFailure ? "defer failing" : "defer")}`, or move a `.copy(to: alloc)` of it instead");
            foreach (var (l, site) in c.Moves) moved[l] = site;
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
                if (IsMovable(m.Operand) && m.Operand is BoundLocal { Local: var moved } && TypeFacts.IsUnique(moved.Type))
                {
                    state.Moved[moved] = m.Syntax.Span;
                    foreach (var c in _cleanups)
                        if (c.Panic && state.Reachable && c.Reads.Contains(moved))
                            Report(DiagnosticCodes.PanicHandlerReadsMoved, m.Syntax.Span, [moved.Name],
                                label: $"the `defer panicking` on line {_source.GetLinePosition(c.Site.Start).Line} reads it after a later panic",
                                help: $"move `{moved.Name}` before the `defer panicking`, or read a `let` copied from it there");
                }
                return state;
            case BoundTry t:
                state = Expr(t.Operand, state);
                RunCleanups(state, 0, failing: true);
                return t.Type.IsNever ? State.Unreachable() : state;
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
                var next = state.Clone();
                foreach (var arm in s.Arms)
                {
                    var branch = Bind(arm.Pattern, next.Clone());
                    if (arm.Guard is not null) next = next.Join(branch = Expr(arm.Guard, branch));
                    after = after.Join(Expr(arm.Value, branch.Clone()));
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
                if (c.Variant is not null) RunCleanups(state, 0, failing: true);
                return state.Join(Expr(c.Handler, state.Clone()));
            }
            case BoundJump j:
                return Statement(j.Jump, state);
            case BoundBlockExpr b:
                return Statement(b.Block, state);
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
        if (core is BoundWith w && StripConversions(w.Target) is var target && IsMovable(target))
        {
            var name = _source.GetText(target.Syntax.Span);
            Report(DiagnosticCodes.ImplicitCopy, span, [why], help: $"write `move {name} with {{ … }}` to give up `{name}`");
            return;
        }
        var options = new List<string>();
        Fix? fix = null;
        if (IsMovable(core))
        {
            options.Add($"hand the value over with `move {text}`");
            fix = new Fix(new TextSpan(span.Start, 0), "move ");
        }
        if (core.Type is ListType or MapType or BuiltinType { Kind: BuiltinKind.StrBuf })
        {
            options.Add($"make an independent copy with `{text}.copy(to: alloc)`");
            fix ??= new Fix(new TextSpan(span.End, 0), ".copy(to: alloc)");
        }
        if (core is BoundLocal { Local: ParameterSymbol { Modifier: ParamModifier.None } p })
            options.Add($"declare the parameter `move {p.Name}` to take ownership");
        options.Add("pass it by `ref` to share it instead");
        Report(DiagnosticCodes.ImplicitCopy, span, [why], help: string.Join("; ", options), fix: fix);
    }

    /// <summary>Whether <paramref name="value"/> is <c>local with { … }</c>, possibly under <c>try</c>, rebuilding <paramref name="local"/> itself.</summary>
    internal static bool IsSelfWith(BoundExpr value, LocalSymbol local) => StripConversions(value) switch
    {
        BoundTry t => IsSelfWith(t.Operand, local),
        BoundWith { Target: var target } => StripConversions(target) is BoundLocal { Local: var l } && l == local,
        _ => false,
    };

    /// <summary>A local or <c>move</c> parameter this function owns, which <c>move</c> can hand over.</summary>
    private static bool IsMovable(BoundExpr e) =>
        e is BoundLocal { Local: { Kind: LocalKind.Let or LocalKind.Var or LocalKind.PatternVariable, BorrowsSubject: false } or ParameterSymbol { Modifier: ParamModifier.Move } };

    /// <summary>Whether <paramref name="e"/> reads existing storage (a local, parameter, field, element or <c>.value</c>) rather than making a fresh value.</summary>
    private static bool IsPlace(BoundExpr e) => e switch
    {
        BoundLocal or BoundThis or BoundField or BoundTupleElement or BoundIndex => true,
        // `take()` hands over a fresh value; the place keeps an empty one.
        BoundBuiltinCall { Member: BuiltinMember.ListTake or BuiltinMember.MapTake or BuiltinMember.StrBufTake or BuiltinMember.OptionalTake } => false,
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
