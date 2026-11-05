using Bayan.Semantics;
using Bayan.Semantics.Binding;
using Bayan.Semantics.Symbols;
using Bayan.Syntax.Ast;
using Bayan.Syntax.Text;
using OptionalType = Bayan.Semantics.Symbols.OptionalType;

namespace Bayan.Lowering;

/// <summary>Turns a bound, error-free compilation into a <see cref="LoweredProgram"/>.</summary>
public static class Lowerer
{
    /// <summary>
    /// Lowers <paramref name="c"/>. A <c>--mutate</c> build passes <paramref name="mutants"/>, the mutant switch of each
    /// bound node a mutant replaces (keyed by reference); a <c>--coverage</c> build passes <paramref name="coverage"/>, its
    /// probes. Without them the program is the plain one.
    /// </summary>
    public static LoweredProgram Lower(Compilation c, IReadOnlyDictionary<BoundNode, BoundMutantSwitch>? mutants = null, CoverageProbes? coverage = null)
    {
        if (c.Diagnostics.HasErrors) throw new InvalidOperationException("cannot lower a compilation with errors");
        var functions = new List<LoweredFunction>();
        var tests = new List<(TestSymbol, LoweredFunction)>();
        foreach (var module in c.Modules)
        {
            foreach (var fn in module.AllFunctions)
            {
                if (fn.Body is null || fn.Kind == FunctionKind.Test || fn.IsGeneric) continue;
                functions.Add(new FunctionLowerer(fn, c.Builtins, mutants, coverage).Lower());
            }
            foreach (var t in module.Tests)
                tests.Add((t, new FunctionLowerer(t.Function, c.Builtins, mutants, coverage).Lower()));
        }
        return new LoweredProgram(c, functions, c.EntryModule.Main, tests, mutants is not null, coverage);
    }
}

internal sealed class FunctionLowerer(FunctionSymbol fn, Builtins builtins, IReadOnlyDictionary<BoundNode, BoundMutantSwitch>? mutants = null,
    CoverageProbes? coverage = null)
{
    private readonly FunctionSymbol _fn = fn;
    private readonly Builtins _builtins = builtins;
    private readonly IReadOnlyDictionary<BoundNode, BoundMutantSwitch>? _mutants = mutants;
    /// <summary>The nodes whose mutant switch is being lowered: inside it they lower as written (a mutant may contain its original).</summary>
    private readonly HashSet<BoundNode> _plain = new(ReferenceEqualityComparer.Instance);
    private readonly CoverageProbes? _coverage = coverage;
    /// <summary>The conditions whose branch probe is being lowered: inside it they lower as written.</summary>
    private readonly HashSet<BoundNode> _probing = new(ReferenceEqualityComparer.Instance);
    private readonly List<LocalSymbol> _locals = new();
    private readonly HashSet<LocalSymbol> _seen = new();
    private readonly List<Scope> _scopes = new();
    private readonly Dictionary<JumpTarget, (string Break, string? Continue, int Depth)> _targets = new();
    private List<BoundStmt> _out = new();
    private int _labels;
    private int _contextSlots;
    private int _panicSlots;
    /// <summary>While an <c>assert</c> condition is lowered: the sub-expressions it shows, each with its stored value once lowered.</summary>
    private Dictionary<BoundExpr, BoundExpr?>? _shown;
    /// <summary>The shown sub-expressions that may not run (a lazy side), each with the flag set when it does.</summary>
    private Dictionary<BoundExpr, LocalSymbol>? _ran;

    private sealed class Scope
    {
        public List<Cleanup> Cleanups { get; } = new();
    }

    private abstract record Cleanup;

    private sealed record DeferCleanup(BoundStmt Body) : Cleanup;

    private sealed record ErrdeferCleanup(LocalSymbol? Error, BoundStmt Body) : Cleanup;

    private sealed record RestoreContext(Node Syntax, int Slot) : Cleanup;

    private sealed record FreeArena(Node Syntax, LocalSymbol Arena) : Cleanup;

    private sealed record PopPanicHandler(Node Syntax, int Slot) : Cleanup;

    public LoweredFunction Lower()
    {
        var body = _fn.Body!;
        _scopes.Add(new Scope());
        if (_fn.Kind == FunctionKind.Constructor)
        {
            var s = _fn.Owner!;
            var defaults = s.Fields.Select(f => f.Default ?? new BoundDefault(body.Syntax, f.Type)).ToArray();
            Emit(new BoundAssign(body.Syntax, new BoundThis(body.Syntax, s), new BoundStructLiteral(body.Syntax, s, defaults), null));
        }
        foreach (var s in body.Statements) Statement(s);
        EmitCleanups(0, null);
        _scopes.RemoveAt(0);
        if (_fn.Kind == FunctionKind.Constructor) Emit(new LReturn(body.Syntax, null));
        return new LoweredFunction(_fn, _locals, _contextSlots, _panicSlots, new BoundBlock(body.Syntax, _out.ToArray()));
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────

    private void Emit(BoundStmt s) => _out.Add(s);

    private void Note(LocalSymbol l)
    {
        // A lambda reads its captures through its environment, not as locals of its own.
        if (l is ParameterSymbol || _fn.Captures.Contains(l)) return;
        if (_seen.Add(l)) _locals.Add(l);
    }

    private LocalSymbol NewTemp(TypeSymbol type)
    {
        var t = new LocalSymbol("t", null, type, LocalKind.Temp);
        Note(t);
        return t;
    }

    private string NewLabel(string kind) => $"{kind}_{++_labels}";

    /// <summary>Runs <paramref name="action"/> with a fresh output buffer and returns what it emitted.</summary>
    private List<BoundStmt> Capture(Action action)
    {
        var saved = _out;
        _out = new List<BoundStmt>();
        try
        {
            action();
            return _out;
        }
        finally
        {
            _out = saved;
        }
    }

    private (BoundExpr Value, List<BoundStmt> Pre) CaptureExpr(BoundExpr e, bool lvalue = false)
    {
        BoundExpr value = null!;
        var pre = Capture(() => value = lvalue ? Lvalue(e) : Expr(e));
        return (value, pre);
    }

    private static BoundBlock Block(Node syntax, IEnumerable<BoundStmt> statements) => new(syntax, statements.ToArray());

    private static BoundLiteral Bool(Node syntax, bool value) => new(syntax, PrimitiveType.Bool, new ConstantValue(value)) { Constant = new ConstantValue(value) };

    private static BoundLiteral I64(Node syntax, long value) =>
        new(syntax, PrimitiveType.I64, new ConstantValue((Int128)value)) { Constant = new ConstantValue((Int128)value) };

    private static BoundExpr Not(BoundExpr e) => new BoundUnary(e.Syntax, UnaryOp.Not, e, PrimitiveType.Bool);

    private static BoundExpr And(BoundExpr a, BoundExpr b) =>
        a is BoundLiteral { Value.Value: true } ? b : b is BoundLiteral { Value.Value: true } ? a : new BoundBinary(a.Syntax, BinaryOp.LogicalAnd, a, b, PrimitiveType.Bool);

    private static BoundExpr Or(BoundExpr a, BoundExpr b) => new BoundBinary(a.Syntax, BinaryOp.LogicalOr, a, b, PrimitiveType.Bool);

    private static BoundExpr Local(Node syntax, LocalSymbol l) => new BoundLocal(syntax, l);

    private static bool IsNever(BoundExpr e) => e is LNever;

    /// <summary>Cheap and free of effects: safe to evaluate more than once or later.</summary>
    private static bool IsTrivial(BoundExpr e) => e switch
    {
        BoundErrorVariant { Payload.Length: > 0 } v => v.Payload.All(IsTrivial),
        BoundLiteral or BoundSymbolLiteral or BoundHostTarget or BoundDefault or BoundLocal or BoundThis or BoundErrorVariant or BoundEnumMember or BoundEnumValues or BoundStaticArray or LVoid => true,
        BoundField f => IsTrivial(f.Receiver),
        BoundTupleElement t => IsTrivial(t.Receiver),
        BoundMove m => IsTrivial(m.Operand),
        LOptionalValue v => IsTrivial(v.Optional),
        LUnionTag t => IsTrivial(t.Union),
        LUnionField f => IsTrivial(f.Union),
        LErrorCode c => IsTrivial(c.Error),
        LErrorField f => IsTrivial(f.Error),
        _ => false,
    };

    /// <summary>Trivial and free of <c>move</c>: a move records its slot, so it must be evaluated exactly once.</summary>
    private static bool IsRepeatable(BoundExpr e) => e switch
    {
        BoundMove => false,
        BoundField f => IsRepeatable(f.Receiver),
        BoundTupleElement t => IsRepeatable(t.Receiver),
        LOptionalValue v => IsRepeatable(v.Optional),
        LUnionTag t => IsRepeatable(t.Union),
        LUnionField f => IsRepeatable(f.Union),
        LErrorCode c => IsRepeatable(c.Error),
        LErrorField f => IsRepeatable(f.Error),
        BoundErrorVariant v => v.Payload.All(IsRepeatable),
        _ => IsTrivial(e),
    };

    private static bool IsLvalue(BoundExpr e) => e switch
    {
        BoundLocal or BoundThis or BoundIndex or LIndexUnchecked or BoundStaticArray => true,
        BoundBuiltinCall { Member: BuiltinMember.MapIndexSet or BuiltinMember.MapSlot } => true,
        BoundField f => IsLvalue(f.Receiver),
        BoundTupleElement t => IsLvalue(t.Receiver),
        LOptionalValue v => IsLvalue(v.Optional),
        _ => false,
    };

    private static bool HasEffects(BoundExpr e) => e switch
    {
        BoundCall or BoundBuiltinCall or LAssignExpr or LSequence or LNextChar => true,
        _ => !IsTrivial(e) && BoundTree.Children(e).OfType<BoundExpr>().Any(HasEffects) || e is LOptionalValue v && HasEffects(v.Optional),
    };

    private BoundExpr Spill(BoundExpr e)
    {
        if (IsNever(e) || e is LVoid) return e;
        var t = NewTemp(e.Type);
        EmitAssign(Local(e.Syntax, t), e);
        return Local(e.Syntax, t);
    }

    /// <summary>
    /// <c>become .s(…)</c>: stores the payload in the state's part of <c>this</c>, then the tag. The payload is evaluated into
    /// temporaries first, since it may read the old state's fields from the same storage.
    /// </summary>
    private void Become(BoundBecome b)
    {
        if (Operands(b.Payload) is not { } payload) return;
        var values = payload.Select(v => v.Constant is null ? Spill(v) : v).ToArray();
        var self = new BoundThis(b.Syntax, _fn.Owner!);
        for (var i = 0; i < values.Length; i++) EmitAssign(new LUnionField(b.Syntax, self, b.State, i), values[i]);
        var tag = new ConstantValue((Int128)b.State.Tag);
        EmitAssign(new LUnionTag(b.Syntax, self), new BoundLiteral(b.Syntax, PrimitiveType.I32, tag) { Constant = tag });
    }

    /// <summary>Stores <paramref name="value"/> in <paramref name="target"/>, or just evaluates it when it does not complete.</summary>
    private void EmitAssign(BoundExpr target, BoundExpr value)
    {
        if (IsNever(value)) return;
        if (value.Type.IsNever || value is LVoid)
        {
            EmitEffect(value);
            return;
        }
        Emit(new BoundAssign(value.Syntax, target, value, null));
    }

    private void EmitEffect(BoundExpr value)
    {
        if (IsNever(value) || value is LVoid || IsTrivial(value)) return;
        Emit(new BoundExprStmt(value.Syntax, value));
    }

    /// <summary>
    /// Lowers operands so they are evaluated left to right: when a later operand needs statements
    /// first, or two operands have effects (C leaves their order unspecified), earlier operands
    /// are stored in temporaries. Operands in <paramref name="lvalues"/> stay addressable.
    /// </summary>
    private BoundExpr[]? Operands(IReadOnlyList<BoundExpr> operands, Func<int, bool>? lvalues = null)
    {
        var n = operands.Count;
        var values = new BoundExpr[n];
        var pres = new List<BoundStmt>[n];
        for (var i = 0; i < n; i++)
            (values[i], pres[i]) = CaptureExpr(operands[i], lvalues?.Invoke(i) ?? false);
        var barrier = -1;
        for (var i = 0; i < n; i++)
            if (pres[i].Count > 0) barrier = i;
        var effects = Enumerable.Range(0, n).Where(i => HasEffects(values[i])).ToList();
        if (effects.Count >= 2) barrier = Math.Max(barrier, effects[^1]);
        // An operand moving a local a later operand reads happens first, as its own statement, so the read sees the local moved.
        var movedFirst = Enumerable.Range(0, n).Select(i => Enumerable.Range(i + 1, n - i - 1).Any(j =>
            Any(values[j], x => x is BoundLocal r && Moves(values[i], r.Local))
            || pres[j].Any(s => Any(s, x => x is BoundLocal r && Moves(values[i], r.Local))))).ToArray();
        for (var i = 0; i < n; i++)
        {
            _out.AddRange(pres[i]);
            if (IsNever(values[i])) return null;
            if ((i < barrier && !IsTrivial(values[i]) || movedFirst[i]) && !(lvalues?.Invoke(i) ?? false) && values[i] is not LVoid)
                values[i] = Spill(values[i]);
        }
        return values;
    }

    /// <summary>Whether <paramref name="node"/> or a node inside it passes <paramref name="test"/>.</summary>
    private static bool Any(BoundNode node, Func<BoundNode, bool> test) => test(node) || node switch
    {
        BoundLocal => false,
        LOptionalHas h => Any(h.Optional, test),
        LOptionalValue v => Any(v.Optional, test),
        LUnionTag t => Any(t.Union, test),
        LUnionField f => Any(f.Union, test),
        LErrorCode c => Any(c.Error, test),
        LErrorField f => Any(f.Error, test),
        LAssignExpr a => Any(a.Target, test) || Any(a.Value, test),
        LSequence s => s.Effects.Any(e => Any(e, test)) || Any(s.Value, test),
        LFailingCall f => Any(f.Call, test),
        _ => BoundTree.Children(node).Any(c => Any(c, test)),
    };

    /// <summary>Whether <paramref name="node"/> moves local <paramref name="l"/>.</summary>
    private static bool Moves(BoundNode node, LocalSymbol l) => Any(node, x => x is BoundMove { Operand: BoundLocal r } && r.Local == l);

    // ── Cleanup ──────────────────────────────────────────────────────────────────

    /// <summary>A guarded defer body (<c>defer stmt if c</c>) with its condition read now, at registration (J20).</summary>
    private BoundStmt CaptureGuard(BoundStmt body) =>
        body is BoundIf { Syntax: GuardedStmt, Else: null } g ? g with { Condition = Spill(Expr(g.Condition)) } : body;

    /// <summary>Emits the cleanups of scopes <paramref name="depth"/> and deeper, innermost first; errdefers only when failing with <paramref name="error"/>.</summary>
    private void EmitCleanups(int depth, LocalSymbol? error)
    {
        for (var i = _scopes.Count - 1; i >= depth; i--)
        {
            var cleanups = _scopes[i].Cleanups;
            for (var j = cleanups.Count - 1; j >= 0; j--)
                EmitCleanup(i, cleanups[j], error);
        }
    }

    private void EmitCleanup(int scopeIndex, Cleanup cleanup, LocalSymbol? error)
    {
        switch (cleanup)
        {
            case DeferCleanup d:
                LowerOutside(scopeIndex, d.Body);
                break;
            case ErrdeferCleanup e when error is not null:
                if (e.Error is { } binding)
                {
                    Note(binding);
                    Emit(new BoundAssign(e.Body.Syntax, Local(e.Body.Syntax, binding), Local(e.Body.Syntax, error), null));
                }
                LowerOutside(scopeIndex, e.Body);
                break;
            case RestoreContext r:
                Emit(new LSetContext(r.Syntax, null, r.Slot));
                break;
            case FreeArena f:
                Emit(new LFreeArena(f.Syntax, Local(f.Syntax, f.Arena)));
                break;
            case PopPanicHandler p:
                Emit(new LPopPanicHandler(p.Syntax, p.Slot));
                break;
        }
    }

    /// <summary>Lowers a cleanup body as if its own scope and everything inside it had already ended.</summary>
    private void LowerOutside(int scopeIndex, BoundStmt body)
    {
        var saved = _scopes.GetRange(scopeIndex, _scopes.Count - scopeIndex);
        _scopes.RemoveRange(scopeIndex, _scopes.Count - scopeIndex);
        try
        {
            Statement(body);
        }
        finally
        {
            _scopes.AddRange(saved);
        }
    }

    // ── Statements ───────────────────────────────────────────────────────────────

    private void Statement(BoundStmt s)
    {
        if (_coverage?.Statements.GetValueOrDefault(s) is { } probe && !_plain.Contains(s))
            Emit(new BoundExprStmt(probe.Syntax, new LCount(probe.Syntax, probe.Id)));
        if (MutantSwitch(s) is { } mutants)
        {
            MutantStatement(mutants);
            return;
        }
        switch (s)
        {
            case BoundBlock b:
                _scopes.Add(new Scope());
                foreach (var inner in b.Statements) Statement(inner);
                EmitCleanups(_scopes.Count - 1, null);
                _scopes.RemoveAt(_scopes.Count - 1);
                break;
            case BoundLocalDecl d:
                Note(d.Local);
                if (d.Initializer is not null) EmitAssign(Local(d.Syntax, d.Local), Expr(d.Initializer));
                if (d.Local.IsUsing && d.Local.Type is BuiltinType { Kind: BuiltinKind.Arena })
                    _scopes[^1].Cleanups.Add(new FreeArena(d.Syntax, d.Local));
                break;
            case BoundExprStmt e:
                EmitEffect(Expr(e.Expr));
                break;
            case BoundAssert a:
                Assert(a);
                break;
            case BoundSetOnPanic o:
                Emit(o);
                break;
            case BoundAssign a:
                Assign(a);
                break;
            case BoundReturn r:
                Return(r);
                break;
            case BoundFail f:
            {
                var code = Expr(f.Error);
                if (IsNever(code)) break;
                var error = NewTemp(f.Error.Type);
                EmitAssign(Local(f.Syntax, error), code);
                EmitCleanups(0, error);
                CountError(f, error);
                Emit(new LReturnError(f.Syntax, Local(f.Syntax, error)));
                break;
            }
            case BoundBreak b:
            {
                var (brk, _, depth) = _targets[b.Target];
                EmitCleanups(depth, null);
                Emit(new LGoto(b.Syntax, brk));
                break;
            }
            case BoundContinue c:
            {
                var (_, cont, depth) = _targets[c.Target];
                EmitCleanups(depth, null);
                Emit(new LGoto(c.Syntax, cont!));
                break;
            }
            case BoundIf i:
                If(i);
                break;
            case BoundWhile w:
                While(w);
                break;
            case BoundDoWhile d:
                DoWhile(d);
                break;
            case BoundFor f:
                For(f);
                break;
            case BoundSwitch sw:
                Switch(sw);
                break;
            case BoundDefer d:
                _scopes[^1].Cleanups.Add(new DeferCleanup(CaptureGuard(d.Body)));
                break;
            case BoundErrdefer e:
                _scopes[^1].Cleanups.Add(new ErrdeferCleanup(e.Error, CaptureGuard(e.Body)));
                break;
            case BoundPanicDefer p:
            {
                // The handler runs while a panic is in progress, in place of every cleanup: none of the scopes' cleanups apply in it.
                if (p.Panic is not null) Note(p.Panic);
                var slot = _panicSlots++;
                var body = Capture(() => LowerOutside(0, p.Body));
                Emit(new LPushPanicHandler(p.Syntax, slot, p.Panic, Block(p.Body.Syntax, body)));
                _scopes[^1].Cleanups.Add(new PopPanicHandler(p.Syntax, slot));
                break;
            }
            case BoundContext c:
                Provide(c);
                break;
            case BoundBecome b:
                Become(b);
                break;
            default:
                throw new InvalidOperationException($"cannot lower {s.GetType().Name}");
        }
    }

    /// <summary><c>provide alloc: a { … }</c>: sets the allocator for the body and restores the outer one on every exit.</summary>
    private void Provide(BoundContext c)
    {
        var arena = Expr(c.Allocator);
        if (IsNever(arena)) return;
        var slot = _contextSlots++;
        Emit(new LSetContext(c.Syntax, arena, slot));
        _scopes.Add(new Scope());
        _scopes[^1].Cleanups.Add(new RestoreContext(c.Syntax, slot));
        foreach (var inner in c.Body.Statements) Statement(inner);
        EmitCleanups(_scopes.Count - 1, null);
        _scopes.RemoveAt(_scopes.Count - 1);
    }

    private void Assign(BoundAssign a)
    {
        if (a.Op is null)
        {
            var parts = Operands([a.Target, a.Value], i => i == 0);
            if (parts is null) return;
            // C leaves `*at(xs, i) = f(&xs)` unsequenced: the value is computed first, so a call
            // that grows the list cannot leave the store pointing at freed storage.
            var assigned = IsTrivial(parts[1]) ? parts[1] : Spill(parts[1]);
            EmitAssign(parts[0], assigned);
            return;
        }
        // `t op= v`: the target is evaluated once, so its index (if any) must be stable.
        var target = Lvalue(a.Target);
        if (target is BoundIndex { Index: var index } bi && !IsTrivial(index))
            target = bi with { Index = Spill(index) };
        else if (target is BoundBuiltinCall { Member: BuiltinMember.MapIndexSet or BuiltinMember.MapSlot, Arguments: [var key, ..] } mb && !IsTrivial(key))
            target = mb with { Arguments = [Spill(key), .. mb.Arguments[1..]] };
        // The old value is read before `v` runs, which may change or move the target.
        // `map[k] op= v` reads through the panicking index: only `slot` inserts a missing key.
        var current = target is BoundBuiltinCall { Member: BuiltinMember.MapIndexSet } read
            ? Spill(read with { Member = BuiltinMember.MapIndex })
            : IsTrivial(a.Value) ? target : Spill(target);
        if (a.Op == BinaryOp.Coalesce)
        {
            // `t ??= v` is `t = t ?? v`: `v` runs only when `t` is null.
            var optional = (OptionalType)target.Type;
            var coalesced = Coalesce(new BoundCoalesce(a.Syntax, current, new BoundConversion(a.Syntax, a.Value, optional, ConversionKind.ToOptional), optional));
            if (!IsNever(coalesced)) EmitAssign(target, coalesced);
            return;
        }
        var value = Expr(a.Value);
        if (IsNever(value)) return;
        if (!IsTrivial(value)) value = Spill(value);
        EmitAssign(target, new BoundBinary(a.Syntax, a.Op.Value, current, value, target.Type));
    }

    private void Return(BoundReturn r)
    {
        BoundExpr? value = null;
        if (r.Value is not null)
        {
            value = Expr(r.Value);
            if (IsNever(value)) return;
            if (value.Type.IsVoid || value is LVoid)
            {
                EmitEffect(value);
                value = null;
            }
            // Cleanups run after the value is read: a `defer` that changes a local must not change the result.
            else if (value is not (BoundLiteral or BoundDefault or BoundErrorVariant { Payload.Length: 0 } or BoundEnumMember) && _scopes.Any(s => s.Cleanups.Count > 0))
            {
                value = Spill(value);
            }
        }
        EmitCleanups(0, null);
        Emit(new LReturn(r.Syntax, value));
    }

    private void If(BoundIf i)
    {
        var condition = Expr(i.Condition);
        if (IsNever(condition)) return;
        var then = Capture(() =>
        {
            if (!i.Never)
            {
                Statement(i.Then);
                return;
            }
            // The panic is a cleanup of the block, so a `return`, `break`, `continue` or failure inside it still panics.
            _scopes.Add(new Scope());
            _scopes[^1].Cleanups.Add(new DeferCleanup(new BoundExprStmt(i.Syntax, new BoundCall(i.Syntax, _builtins.Panic, null,
                [new BoundLiteral(i.Syntax, PrimitiveType.String, new ConstantValue("`if never` condition was true"))], PrimitiveType.Never))));
            Statement(i.Then);
            EmitCleanups(_scopes.Count - 1, null);
            _scopes.RemoveAt(_scopes.Count - 1);
        });
        var otherwise = i.Else is null ? null : Capture(() => Statement(i.Else));
        Emit(new BoundIf(i.Syntax, condition, Block(i.Then.Syntax, then), otherwise is null ? null : Block(i.Else!.Syntax, otherwise)));
    }

    /// <summary>Lowers <paramref name="body"/> as a loop body with fresh break/continue labels.</summary>
    private (string Break, string Continue) EnterLoop(JumpTarget target)
    {
        var brk = NewLabel("break");
        var cont = NewLabel("continue");
        _targets[target] = (brk, cont, _scopes.Count);
        return (brk, cont);
    }

    /// <summary><c>if (!condition) goto label;</c> with the condition's statements first.</summary>
    private void ExitUnless(BoundExpr condition, string label)
    {
        var c = Expr(condition);
        if (IsNever(c)) return;
        Emit(new BoundIf(condition.Syntax, Not(c), Block(condition.Syntax, [new LGoto(condition.Syntax, label)]), null));
    }

    private void While(BoundWhile w)
    {
        var (brk, cont) = EnterLoop(w.Target);
        var body = Capture(() =>
        {
            ExitUnless(w.Condition, brk);
            Statement(w.Body);
            Emit(new LLabel(w.Syntax, cont));
        });
        Emit(new LLoop(w.Syntax, Block(w.Syntax, body)));
        Emit(new LLabel(w.Syntax, brk));
    }

    private void DoWhile(BoundDoWhile d)
    {
        var (brk, cont) = EnterLoop(d.Target);
        var body = Capture(() =>
        {
            Statement(d.Body);
            Emit(new LLabel(d.Syntax, cont));
            ExitUnless(d.Condition, brk);
        });
        Emit(new LLoop(d.Syntax, Block(d.Syntax, body)));
        Emit(new LLabel(d.Syntax, brk));
    }

    private void For(BoundFor f)
    {
        Note(f.Variable);
        var syntax = f.Syntax;
        var variable = Local(syntax, f.Variable);
        if (f.Iterable is BoundRange range)
        {
            var bounds = Operands([range.Start, range.End]);
            if (bounds is null) return;
            var i = NewTemp(range.Start.Type);
            var end = NewTemp(range.End.Type);
            EmitAssign(Local(syntax, i), bounds[0]);
            EmitAssign(Local(syntax, end), bounds[1]);
            var (brk, cont) = EnterLoop(f.Target);
            var cmp = range.Inclusive ? BinaryOp.Le : BinaryOp.Lt;
            var body = Capture(() =>
            {
                Emit(new BoundIf(syntax, Not(new BoundBinary(syntax, cmp, Local(syntax, i), Local(syntax, end), PrimitiveType.Bool)),
                    Block(syntax, [new LGoto(syntax, brk)]), null));
                EmitAssign(variable, Local(syntax, i));
                Statement(f.Body);
                Emit(new LLabel(syntax, cont));
                if (range.Inclusive)
                    Emit(new BoundIf(syntax, new BoundBinary(syntax, BinaryOp.Eq, Local(syntax, i), Local(syntax, end), PrimitiveType.Bool),
                        Block(syntax, [new LGoto(syntax, brk)]), null));
                Emit(new BoundAssign(syntax, Local(syntax, i),
                    new BoundBinary(syntax, BinaryOp.AddWrap, Local(syntax, i), One(syntax, i.Type), i.Type), null));
            });
            Emit(new LLoop(syntax, Block(syntax, body)));
            Emit(new LLabel(syntax, brk));
            return;
        }

        if (f.UserIteration is { } user)
        {
            UserFor(f, variable, user);
            return;
        }

        var iterable = Expr(f.Iterable);
        if (IsNever(iterable)) return;
        if (iterable.Type.IsString)
        {
            var text = Spill(iterable);
            var pos = NewTemp(PrimitiveType.I64);
            EmitAssign(Local(syntax, pos), I64(syntax, 0));
            var (brk, cont) = EnterLoop(f.Target);
            var body = Capture(() =>
            {
                var length = new BoundBuiltinCall(syntax, BuiltinMember.StringLength, text, [], PrimitiveType.I64);
                Emit(new BoundIf(syntax, Not(new BoundBinary(syntax, BinaryOp.Lt, Local(syntax, pos), length, PrimitiveType.Bool)),
                    Block(syntax, [new LGoto(syntax, brk)]), null));
                EmitAssign(variable, new LNextChar(syntax, text, pos));
                Statement(f.Body);
                Emit(new LLabel(syntax, cont));
            });
            Emit(new LLoop(syntax, Block(syntax, body)));
            Emit(new LLabel(syntax, brk));
            return;
        }

        if (iterable.Type is TextViewType view)
        {
            // The view is a copy the cursor functions advance; `parts` counts its limit down in it.
            var copy = Spill(iterable);
            var pos = NewTemp(PrimitiveType.I64);
            EmitAssign(Local(syntax, pos), I64(syntax, 0));
            var (brk, cont) = EnterLoop(f.Target);
            var body = Capture(() =>
            {
                var more = new BoundBuiltinCall(syntax, BuiltinMember.TextViewMore, copy, [Local(syntax, pos)], PrimitiveType.Bool);
                Emit(new BoundIf(syntax, Not(more), Block(syntax, [new LGoto(syntax, brk)]), null));
                EmitAssign(variable, new BoundBuiltinCall(syntax, BuiltinMember.TextViewNext, copy, [Local(syntax, pos)], view.Element));
                Statement(f.Body);
                Emit(new LLabel(syntax, cont));
            });
            Emit(new LLoop(syntax, Block(syntax, body)));
            Emit(new LLabel(syntax, brk));
            return;
        }

        if (iterable.Type is MapType)
        {
            // Entries in insertion order; removed entries stay as tombstones until the next rehash.
            var map = Spill(iterable);
            var slot = NewTemp(PrimitiveType.I64);
            EmitAssign(Local(syntax, slot), I64(syntax, 0));
            var (brk, cont) = EnterLoop(f.Target);
            var body = Capture(() =>
            {
                var count = new BoundBuiltinCall(syntax, BuiltinMember.MapSlotCount, map, [], PrimitiveType.I64);
                Emit(new BoundIf(syntax, Not(new BoundBinary(syntax, BinaryOp.Lt, Local(syntax, slot), count, PrimitiveType.Bool)),
                    Block(syntax, [new LGoto(syntax, brk)]), null));
                var live = new BoundBuiltinCall(syntax, BuiltinMember.MapSlotLive, map, [Local(syntax, slot)], PrimitiveType.Bool);
                Emit(new BoundIf(syntax, Not(live), Block(syntax, [new LGoto(syntax, cont)]), null));
                EmitAssign(variable, new BoundBuiltinCall(syntax, BuiltinMember.MapSlotEntry, map, [Local(syntax, slot)], f.Variable.Type));
                Statement(f.Body);
                Emit(new LLabel(syntax, cont));
                Emit(new BoundAssign(syntax, Local(syntax, slot),
                    new BoundBinary(syntax, BinaryOp.AddWrap, Local(syntax, slot), I64(syntax, 1), PrimitiveType.I64), null));
            });
            Emit(new LLoop(syntax, Block(syntax, body)));
            Emit(new LLabel(syntax, brk));
            return;
        }

        // Spans and lists are small headers: iterate over a copy. Arrays are iterated in place.
        var sequence = iterable.Type is Semantics.Symbols.ArrayType && IsLvalue(iterable) ? iterable : Spill(iterable);
        var (lengthMember, element) = iterable.Type switch
        {
            SpanType sp => (BuiltinMember.SpanLength, sp.Element),
            ListType l => (BuiltinMember.ListLength, l.Element),
            _ => (BuiltinMember.ArrayLength, ((Semantics.Symbols.ArrayType)iterable.Type).Element),
        };
        var index = NewTemp(PrimitiveType.I64);
        EmitAssign(Local(syntax, index), I64(syntax, 0));
        var (b, c) = EnterLoop(f.Target);
        var loopBody = Capture(() =>
        {
            var length = new BoundBuiltinCall(syntax, lengthMember, sequence, [], PrimitiveType.I64);
            Emit(new BoundIf(syntax, Not(new BoundBinary(syntax, BinaryOp.Lt, Local(syntax, index), length, PrimitiveType.Bool)),
                Block(syntax, [new LGoto(syntax, b)]), null));
            EmitAssign(variable, ForItem(f, new LIndexUnchecked(syntax, sequence, Local(syntax, index), element)));
            Statement(f.Body);
            Emit(new LLabel(syntax, c));
            Emit(new BoundAssign(syntax, Local(syntax, index),
                new BoundBinary(syntax, BinaryOp.AddWrap, Local(syntax, index), I64(syntax, 1), PrimitiveType.I64), null));
        });
        Emit(new LLoop(syntax, Block(syntax, loopBody)));
        Emit(new LLabel(syntax, b));
    }

    /// <summary>
    /// <c>for</c> over an <c>IIndexable</c>: <c>count</c> is read once, then <c>get(i)</c> for each index below it.
    /// Over an <c>IEnumerator</c>: <c>moveNext()</c> until it returns false, reading <c>current</c> after each true.
    /// A place is walked in place; an rvalue enumerator goes to the binder's spill local, any other rvalue to a temporary.
    /// </summary>
    private void UserFor(BoundFor f, BoundExpr variable, UserIteration user)
    {
        var syntax = f.Syntax;
        BoundExpr subject;
        if (user.Spill is { } spill)
        {
            Note(spill);
            var value = Expr(f.Iterable);
            if (IsNever(value)) return;
            subject = Local(syntax, spill);
            EmitAssign(subject, value);
        }
        else
        {
            subject = IsLvalueShape(f.Iterable) ? Lvalue(f.Iterable) : Expr(f.Iterable);
            if (IsNever(subject)) return;
            subject = Stable(subject);
            if (!IsLvalue(subject)) subject = Spill(subject);
        }
        BoundExpr Item(BoundExpr item) => ForItem(f, item);
        // A getter's witness may be a field: read it.
        BoundExpr Call(Symbol witness, BoundExpr[] arguments) => witness is FieldSymbol field
            ? new BoundField(syntax, subject, field)
            : new BoundCall(syntax, (FunctionSymbol)witness, subject, arguments, ((FunctionSymbol)witness).ReturnType);
        if (user.Kind == IterationKind.Indexable)
        {
            var count = NewTemp(PrimitiveType.I64);
            EmitAssign(Local(syntax, count), Call(user.Advance, []));
            var index = NewTemp(PrimitiveType.I64);
            EmitAssign(Local(syntax, index), I64(syntax, 0));
            var (brk, cont) = EnterLoop(f.Target);
            var body = Capture(() =>
            {
                Emit(new BoundIf(syntax, Not(new BoundBinary(syntax, BinaryOp.Lt, Local(syntax, index), Local(syntax, count), PrimitiveType.Bool)),
                    Block(syntax, [new LGoto(syntax, brk)]), null));
                EmitAssign(variable, Item(Call(user.Item, [Local(syntax, index)])));
                Statement(f.Body);
                Emit(new LLabel(syntax, cont));
                Emit(new BoundAssign(syntax, Local(syntax, index),
                    new BoundBinary(syntax, BinaryOp.AddWrap, Local(syntax, index), I64(syntax, 1), PrimitiveType.I64), null));
            });
            Emit(new LLoop(syntax, Block(syntax, body)));
            Emit(new LLabel(syntax, brk));
            return;
        }
        var (b, c) = EnterLoop(f.Target);
        var loopBody = Capture(() =>
        {
            Emit(new BoundIf(syntax, Not(Call(user.Advance, [])),
                Block(syntax, [new LGoto(syntax, b)]), null));
            EmitAssign(variable, Item(Call(user.Item, [])));
            Statement(f.Body);
            Emit(new LLabel(syntax, c));
        });
        Emit(new LLoop(syntax, Block(syntax, loopBody)));
        Emit(new LLabel(syntax, b));
    }

    /// <summary>
    /// <c>for T x in e</c> with <c>T</c> other than the element, e.g. <c>dyn ref I</c>: the item is held in a temporary and converted.
    /// A mutable <c>dyn ref</c> to an array or List element points at the element itself, so mutations through it persist.
    /// </summary>
    private BoundExpr ForItem(BoundFor f, BoundExpr item)
    {
        if (item.Type.Equals(f.Variable.Type) || Conversions.Implicit(item.Type, f.Variable.Type) is not { } kind) return item;
        if (item is LIndexUnchecked && f.Variable.Type is DynRefType { ReadOnly: false })
            return new BoundConversion(f.Syntax, item, f.Variable.Type, kind);
        return Expr(new BoundConversion(f.Syntax, Spill(item), f.Variable.Type, kind));
    }

    private static BoundLiteral One(Node syntax, TypeSymbol type) =>
        new(syntax, type, new ConstantValue((Int128)1)) { Constant = new ConstantValue((Int128)1) };

    private void Switch(BoundSwitch sw)
    {
        var value = Expr(sw.Value);
        if (IsNever(value)) return;
        if (!IsRepeatable(value)) value = Spill(value);
        var end = NewLabel("switch_end");
        _targets[sw.Target] = (end, null, _scopes.Count);
        BoundSwitchSection? fallback = null;
        foreach (var section in sw.Sections)
        {
            if (section.Pattern is null)
            {
                fallback = section;
                continue;
            }
            var body = Capture(() =>
            {
                Claim(section.Pattern);
                _scopes.Add(new Scope());
                foreach (var s in section.Body) Statement(s);
                EmitCleanups(_scopes.Count - 1, null);
                _scopes.RemoveAt(_scopes.Count - 1);
                Emit(new LGoto(section.Syntax, end));
            });
            var match = Match(section.Pattern, value);
            if (section.Guard is null)
            {
                Emit(new BoundIf(section.Syntax, match, Block(section.Syntax, body), null));
                continue;
            }
            var (guard, pre) = CaptureExpr(section.Guard);
            if (pre.Count == 0)
            {
                Emit(new BoundIf(section.Syntax, And(match, guard), Block(section.Syntax, body), null));
            }
            else
            {
                pre.Add(new BoundIf(section.Syntax, guard, Block(section.Syntax, body), null));
                Emit(new BoundIf(section.Syntax, match, Block(section.Syntax, pre), null));
            }
        }
        if (fallback is not null)
        {
            _scopes.Add(new Scope());
            foreach (var s in fallback.Body) Statement(s);
            EmitCleanups(_scopes.Count - 1, null);
            _scopes.RemoveAt(_scopes.Count - 1);
        }
        Emit(new LLabel(sw.Syntax, end));
    }

    // ── Expressions ──────────────────────────────────────────────────────────────

    /// <summary>Lowers an expression that must stay addressable (assignment target, <c>ref</c> argument, mutating receiver).</summary>
    private BoundExpr Lvalue(BoundExpr e)
    {
        switch (e)
        {
            case BoundLocal l:
                Note(l.Local);
                return l;
            case BoundThis or BoundStaticArray:
                return e;
            case BoundField f:
                return f with { Receiver = IsLvalueShape(f.Receiver) ? Lvalue(f.Receiver) : Spill(Expr(f.Receiver)) };
            case BoundTupleElement t:
                return t with { Receiver = IsLvalueShape(t.Receiver) ? Lvalue(t.Receiver) : Spill(Expr(t.Receiver)) };
            case BoundIndex i:
            {
                var arrayTarget = i.Target.Type is Semantics.Symbols.ArrayType;
                var parts = Operands([i.Target, i.Index], k => k == 0 && arrayTarget && IsLvalueShape(i.Target));
                if (parts is null) return new LNever(e.Syntax, e.Type);
                var target = arrayTarget && !IsLvalue(parts[0]) ? Spill(parts[0]) : parts[0];
                return i with { Target = target, Index = parts[1] };
            }
            case BoundBuiltinCall { Member: BuiltinMember.MapIndexSet or BuiltinMember.MapSlot } b:
                return BuiltinCall(b);
            default:
                return Spill(Expr(e));
        }
    }

    private static bool IsLvalueShape(BoundExpr e) => e switch
    {
        BoundLocal or BoundThis or BoundIndex or BoundStaticArray => true,
        BoundBuiltinCall { Member: BuiltinMember.MapIndexSet or BuiltinMember.MapSlot } => true,
        BoundField f => IsLvalueShape(f.Receiver),
        BoundTupleElement t => IsLvalueShape(t.Receiver),
        _ => false,
    };

    private BoundExpr Expr(BoundExpr e)
    {
        if (_shown is null || !_shown.ContainsKey(e)) return LowerExpr(e);
        var value = LowerExpr(e);
        if (value.Constant is null) value = Spill(value);
        if (_ran!.TryGetValue(e, out var ran) && !IsNever(value)) EmitAssign(Local(e.Syntax, ran), Bool(e.Syntax, true));
        return _shown[e] = value;
    }

    private BoundExpr LowerExpr(BoundExpr e)
    {
        if (MutantSwitch(e) is { } mutants) return MutantExpr(mutants);
        if (_coverage?.Branches.GetValueOrDefault(e) is { } branch && _probing.Add(e)) return BranchProbe(e, branch);
        switch (e)
        {
            case BoundErrorVariant { Payload.Length: > 0 } v:
            {
                var payload = Operands(v.Payload);
                return payload is null ? new LNever(e.Syntax, e.Type) : v with { Payload = payload };
            }
            case BoundLiteral or BoundSymbolLiteral or BoundHostTarget or BoundDefault or BoundThis or BoundErrorVariant or BoundEnumMember or BoundEnumValues or BoundStaticArray:
                return e;
            case BoundLocal l:
                Note(l.Local);
                return e;
            case BoundField f:
            {
                var receiver = Expr(f.Receiver);
                return IsNever(receiver) ? new LNever(e.Syntax, e.Type) : f with { Receiver = receiver };
            }
            case BoundTupleElement t:
            {
                var receiver = Expr(t.Receiver);
                return IsNever(receiver) ? new LNever(e.Syntax, e.Type) : t with { Receiver = receiver };
            }
            case BoundUnionField u:
            {
                var receiver = Expr(u.Receiver);
                return IsNever(receiver) ? new LNever(e.Syntax, e.Type) : new LUnionField(e.Syntax, receiver, u.Variant, u.Index);
            }
            case BoundErrorField f:
            {
                var receiver = Expr(f.Receiver);
                return IsNever(receiver) ? new LNever(e.Syntax, e.Type) : new LErrorField(e.Syntax, receiver, f.Variant, f.Index);
            }
            case BoundCall c:
                return Call(c);
            case BoundBuiltinCall b:
                return BuiltinCall(b);
            case BoundTryFrom t:
                return TryFrom(t);
            case BoundUnary u:
            {
                var operand = Expr(u.Operand);
                return IsNever(operand) ? new LNever(e.Syntax, e.Type) : u with { Operand = operand };
            }
            case BoundBinary { Op: BinaryOp.LogicalAnd or BinaryOp.LogicalOr } b:
                return ShortCircuit(b);
            case BoundBinary b:
            {
                var parts = Operands([b.Left, b.Right]);
                return parts is null ? new LNever(e.Syntax, e.Type) : b with { Left = parts[0], Right = parts[1] };
            }
            case BoundConversion c:
            {
                var lvalue = c.Kind is ConversionKind.ArrayToSpan or ConversionKind.ToDynRef;
                var operand = lvalue ? Lvalue(c.Operand) : Expr(c.Operand);
                if (IsNever(operand)) return new LNever(e.Syntax, e.Type);
                if (c.Kind == ConversionKind.FromNever) return operand;
                if (c.Kind == ConversionKind.Narrow) return new LOptionalValue(c.Syntax, operand, c.Type);
                if (c.Kind is ConversionKind.ListToSpan && !IsTrivial(operand)) operand = Spill(operand);
                return c with { Operand = operand };
            }
            case BoundConditional c:
                return Conditional(c);
            case BoundCoalesce c:
                return Coalesce(c);
            case BoundStructLiteral s:
            {
                // The fields, then a state literal's payload, in source order.
                var operands = Operands([.. s.Fields, .. s.Payload]);
                return operands is null ? new LNever(e.Syntax, e.Type) : s with { Fields = operands[..s.Fields.Length], Payload = operands[s.Fields.Length..] };
            }
            case BoundUnionNew u:
            {
                var payload = Operands(u.Payload);
                return payload is null ? new LNever(e.Syntax, e.Type) : u with { Payload = payload };
            }
            case BoundWith w:
            {
                var target = Expr(w.Target);
                if (IsNever(target)) return target;
                var copy = NewTemp(w.Type);
                EmitAssign(Local(w.Syntax, copy), target);
                foreach (var (field, value) in w.Updates)
                {
                    var v = Expr(value);
                    if (IsNever(v)) return new LNever(e.Syntax, e.Type);
                    EmitAssign(new BoundField(w.Syntax, Local(w.Syntax, copy), field), v);
                }
                return Local(w.Syntax, copy);
            }
            case BoundTuple t:
            {
                var elements = Operands(t.Elements);
                return elements is null ? new LNever(e.Syntax, e.Type) : t with { Elements = elements };
            }
            case BoundRange r:
            {
                // A range held as a value is its layout tuple; `for` over a range literal does not come here.
                var bounds = Operands([r.Start, r.End]);
                return bounds is null ? new LNever(e.Syntax, e.Type) : new BoundTuple(r.Syntax, ((RangeType)r.Type).Layout, [.. bounds, Bool(r.Syntax, r.Inclusive)]);
            }
            case BoundCollection c:
                return Collection(c);
            case BoundIndex i:
            {
                var arrayTarget = i.Target.Type is Semantics.Symbols.ArrayType;
                var parts = Operands([i.Target, i.Index], k => k == 0 && arrayTarget && IsLvalueShape(i.Target));
                if (parts is null) return new LNever(e.Syntax, e.Type);
                var target = arrayTarget && !IsLvalue(parts[0]) ? Spill(parts[0]) : parts[0];
                return i with { Target = target, Index = parts[1] };
            }
            case BoundSlice s:
                return Slice(s);
            case BoundIs i:
            {
                var value = Expr(i.Value);
                if (IsNever(value)) return new LNever(e.Syntax, e.Type);
                // `x in a..=b` may compute its ends at run time (a pattern's ends are constants): the value is read first.
                if (i.Pattern is BoundRangePattern { Start.Constant: null } or BoundRangePattern { End.Constant: null })
                {
                    var r = (BoundRangePattern)i.Pattern;
                    if (value.Constant is null) value = Spill(value);
                    var start = Expr(r.Start);
                    if (!IsRepeatable(start)) start = Spill(start);
                    var end = Expr(r.End);
                    if (IsNever(start) || IsNever(end)) return new LNever(e.Syntax, e.Type);
                    return Match(r with { Start = start, End = end }, value);
                }
                if (!IsRepeatable(value)) value = Spill(value);
                return Match(i.Pattern, value);
            }
            case BoundSwitchExpr s:
                return SwitchExpr(s);
            case BoundTry t:
                return Try(t);
            case BoundMove m:
                return new BoundMove(m.Syntax, Expr(m.Operand));
            case BoundCatch c:
                return Catch(c);
            case BoundJump j:
                Statement(j.Jump);
                return new LNever(e.Syntax, e.Type);
            case BoundBlockExpr b:
                Statement(b.Block);
                return b.Type.IsNever ? new LNever(e.Syntax, e.Type) : new LVoid(e.Syntax);
            case BoundStackalloc s:
            {
                var storage = NewTemp(new Semantics.Symbols.ArrayType(s.Element, s.Count));
                Emit(new BoundAssign(s.Syntax, Local(s.Syntax, storage), new BoundDefault(s.Syntax, storage.Type), null));
                return new BoundConversion(s.Syntax, Local(s.Syntax, storage), s.Type, ConversionKind.ArrayToSpan);
            }
            case BoundFunctionRef r:
                return new LFunctionValue(r.Syntax, r.Function, null, r.FunctionType);
            case BoundLambda { Captures.Length: 0 } l:
                return new LFunctionValue(l.Syntax, l.Lambda, null, l.FunctionType);
            case BoundLambda l:
            {
                // The captures are copied, as the declared types of their locals, into a tuple in this frame: the environment.
                var tuple = new Semantics.Symbols.TupleType(l.Captures.Select(c => new TupleField(null, c.Local.Type)).ToArray());
                var env = NewTemp(tuple);
                var values = Operands(l.Captures.Select(c => (BoundExpr)new BoundLocal(c.Syntax, c.Local)).ToArray());
                if (values is null) return new LNever(e.Syntax, e.Type);
                EmitAssign(Local(l.Syntax, env), new BoundTuple(l.Syntax, tuple, values));
                return new LFunctionValue(l.Syntax, l.Lambda, env, l.FunctionType);
            }
            case BoundInterpolated i:
            {
                // Outside an output call the text goes into a temporary growable `StrBuf` (from `context.allocator`).
                var buf = NewTemp(BuiltinType.StrBuf);
                Emit(new BoundAssign(i.Syntax, Local(i.Syntax, buf), new BoundCall(i.Syntax, _builtins.StrBufGrow, null, [], BuiltinType.StrBuf), null));
                if (IsNever(WriteParts(i, WriteSink.StrBuf, Local(i.Syntax, buf), i))) return new LNever(e.Syntax, e.Type);
                return new BoundBuiltinCall(i.Syntax, BuiltinMember.StrBufAsString, Local(i.Syntax, buf), [], PrimitiveType.String);
            }
            default:
                throw new InvalidOperationException($"cannot lower {e.GetType().Name}");
        }
    }

    // ── Mutant schemata ──────────────────────────────────────────────────────────

    private BoundMutantSwitch? MutantSwitch(BoundNode node) =>
        _mutants is not null && !_plain.Contains(node) && _mutants.TryGetValue(node, out var s) ? s : null;

    /// <summary>
    /// <c>(byn_mutant == id ? mutated : original)</c> when no arm needs statements first; otherwise, so only the selected
    /// arm's prelude runs, <c>if (byn_mutant == id) { pre_m; t = m } else { pre_o; t = o }</c>.
    /// </summary>
    private BoundExpr MutantExpr(BoundMutantSwitch s)
    {
        var original = (BoundExpr)s.Original;
        _plain.Add(original);
        try
        {
            var arms = s.Mutants.Select(m => (m.Id, Arm: CaptureExpr((BoundExpr)m.Node))).ToList();
            var plain = CaptureExpr(original);
            if (plain.Pre.Count == 0 && !IsNever(plain.Value) && !original.Type.IsNever
                && arms.All(a => a.Arm.Pre.Count == 0 && !IsNever(a.Arm.Value)))
            {
                var value = plain.Value;
                for (var i = arms.Count - 1; i >= 0; i--)
                    value = new BoundConditional(s.Syntax, new LMutantIs(s.Syntax, arms[i].Id), arms[i].Arm.Value, value, original.Type);
                return value;
            }
            var result = NewTemp(original.Type);
            var target = Local(s.Syntax, result);
            List<BoundStmt> Assign((BoundExpr Value, List<BoundStmt> Pre) arm) =>
                Capture(() => { _out.AddRange(arm.Pre); EmitAssign(target, arm.Value); });
            BoundStmt chain = Block(s.Syntax, Assign(plain));
            for (var i = arms.Count - 1; i >= 0; i--)
                chain = new BoundIf(s.Syntax, new LMutantIs(s.Syntax, arms[i].Id), Block(s.Syntax, Assign(arms[i].Arm)), Block(s.Syntax, [chain]));
            Emit(chain);
            return original.Type.IsNever ? new LNever(s.Syntax, original.Type) : target;
        }
        finally { _plain.Remove(original); }
    }

    /// <summary><c>if (byn_mutant == id) { mutated } else { original }</c>, each arm lowered on its own.</summary>
    private void MutantStatement(BoundMutantSwitch s)
    {
        var original = (BoundStmt)s.Original;
        _plain.Add(original);
        try
        {
            BoundStmt chain = Block(s.Syntax, Capture(() => Statement(original)));
            for (var i = s.Mutants.Length - 1; i >= 0; i--)
            {
                var mutant = (BoundStmt)s.Mutants[i].Node;
                chain = new BoundIf(s.Syntax, new LMutantIs(s.Syntax, s.Mutants[i].Id), Block(s.Syntax, Capture(() => Statement(mutant))), Block(s.Syntax, [chain]));
            }
            Emit(chain);
        }
        finally { _plain.Remove(original); }
    }

    // ── Coverage ─────────────────────────────────────────────────────────────────

    /// <summary><c>(c ? (byn_cov[t]++, true) : (byn_cov[f]++, false))</c>: the condition counts its outcome.</summary>
    private BoundExpr BranchProbe(BoundExpr e, BoundBranchProbe probe)
    {
        try
        {
            var value = LowerExpr(e);
            if (IsNever(value)) return value;
            BoundExpr Outcome(int id, bool outcome) => new LSequence(e.Syntax, [new LCount(e.Syntax, id)], Bool(e.Syntax, outcome));
            return new BoundConditional(e.Syntax, value, Outcome(probe.WhenTrue, true), Outcome(probe.WhenFalse, false), PrimitiveType.Bool);
        }
        finally { _probing.Remove(e); }
    }

    /// <summary>On the error-return edge of <paramref name="origin"/>: the counter of the variant in <paramref name="error"/>.</summary>
    private void CountError(BoundNode origin, LocalSymbol error)
    {
        if (_coverage?.Errors.GetValueOrDefault(origin) is not { } probe) return;
        var syntax = probe.Syntax;
        foreach (var (variant, id) in probe.Variants)
        {
            var code = new ConstantValue((Int128)variant.Code);
            var test = Equal(syntax, new LErrorCode(syntax, Local(syntax, error)), new BoundLiteral(syntax, PrimitiveType.U32, code) { Constant = code });
            Emit(new BoundIf(syntax, test, Block(syntax, [new BoundExprStmt(syntax, new LCount(syntax, id))]), null));
        }
    }

    private BoundExpr ShortCircuit(BoundBinary b)
    {
        var left = Expr(b.Left);
        if (IsNever(left)) return left;
        var (right, pre) = CaptureExpr(b.Right);
        if (pre.Count == 0 && !IsNever(right)) return b with { Left = left, Right = right };
        // `t = left; if (t) { pre; t = right; }` for &&, `if (!t)` for ||.
        var t = NewTemp(PrimitiveType.Bool);
        EmitAssign(Local(b.Syntax, t), left);
        pre.Add(new BoundAssign(b.Syntax, Local(b.Syntax, t), right, null));
        if (IsNever(right)) pre.RemoveAt(pre.Count - 1);
        BoundExpr test = Local(b.Syntax, t);
        if (b.Op == BinaryOp.LogicalOr) test = Not(test);
        Emit(new BoundIf(b.Syntax, test, Block(b.Syntax, pre), null));
        return Local(b.Syntax, t);
    }

    private BoundExpr Conditional(BoundConditional c)
    {
        var condition = Expr(c.Condition);
        if (IsNever(condition)) return condition;
        var (whenTrue, preTrue) = CaptureExpr(c.WhenTrue);
        var (whenFalse, preFalse) = CaptureExpr(c.WhenFalse);
        if (preTrue.Count == 0 && preFalse.Count == 0 && !IsNever(whenTrue) && !IsNever(whenFalse)
            && !whenTrue.Type.IsNever && !whenFalse.Type.IsNever)
            return c with { Condition = condition, WhenTrue = whenTrue, WhenFalse = whenFalse };
        var result = NewTemp(c.Type);
        var target = Local(c.Syntax, result);
        var t = Capture(() => { _out.AddRange(preTrue); EmitAssign(target, whenTrue); });
        var f = Capture(() => { _out.AddRange(preFalse); EmitAssign(target, whenFalse); });
        Emit(new BoundIf(c.Syntax, condition, Block(c.Syntax, t), Block(c.Syntax, f)));
        return c.Type.IsNever ? new LNever(c.Syntax, c.Type) : target;
    }

    private BoundExpr Coalesce(BoundCoalesce c)
    {
        var left = Expr(c.Left);
        if (IsNever(left)) return left;
        if (!IsRepeatable(left)) left = Spill(left);
        var keepsOptional = c.Type is OptionalType;
        BoundExpr present = keepsOptional ? left : new LOptionalValue(c.Syntax, left, ((OptionalType)c.Left.Type).Inner);
        var (right, pre) = CaptureExpr(c.Right);
        if (pre.Count == 0 && !IsNever(right) && !right.Type.IsNever)
            return new BoundConditional(c.Syntax, new LOptionalHas(c.Syntax, left), present, right, c.Type);
        var result = NewTemp(c.Type);
        var target = Local(c.Syntax, result);
        var missing = Capture(() => { _out.AddRange(pre); EmitAssign(target, right); });
        Emit(new BoundIf(c.Syntax, new LOptionalHas(c.Syntax, left),
            Block(c.Syntax, [new BoundAssign(c.Syntax, target, present, null)]), Block(c.Syntax, missing)));
        return target;
    }

    private BoundExpr Collection(BoundCollection c)
    {
        switch (c.Type)
        {
            case ListType l:
            {
                if (c.Elements.Length == 0) return c;
                var list = NewTemp(l);
                EmitAssign(Local(c.Syntax, list), c with { Elements = [] });
                foreach (var element in c.Elements)
                {
                    var v = Expr(element is BoundSpread sp ? sp.Operand : element);
                    if (IsNever(v)) return new LNever(c.Syntax, c.Type);
                    if (element is BoundSpread spread)
                    {
                        AddEach(spread, Spill(v), item => Emit(new BoundExprStmt(c.Syntax,
                            new BoundBuiltinCall(c.Syntax, BuiltinMember.ListAdd, Local(c.Syntax, list), [item], PrimitiveType.Void))));
                        continue;
                    }
                    Emit(new BoundExprStmt(c.Syntax, new BoundBuiltinCall(c.Syntax, BuiltinMember.ListAdd, Local(c.Syntax, list), [v], PrimitiveType.Void)));
                }
                return Local(c.Syntax, list);
            }
            case MapType m:
            {
                if (c.Elements.Length == 0) return c;
                var map = NewTemp(m);
                EmitAssign(Local(c.Syntax, map), c with { Elements = [] });
                for (var i = 0; i < c.Elements.Length; i += 2)
                {
                    var entry = Operands([c.Elements[i], c.Elements[i + 1]]);
                    if (entry is null) return new LNever(c.Syntax, c.Type);
                    Emit(new BoundAssign(c.Syntax,
                        new BoundBuiltinCall(c.Syntax, BuiltinMember.MapIndexSet, Local(c.Syntax, map), [entry[0]], m.ValueType), entry[1], null));
                }
                return Local(c.Syntax, map);
            }
            case SpanType s:
            {
                if (c.Elements.Length == 0) return new BoundDefault(c.Syntax, s);
                var elements = c.Elements.Any(e => e is BoundSpread) ? Expanded(c.Elements) : Operands(c.Elements);
                if (elements is null) return new LNever(c.Syntax, c.Type);
                var array = new Semantics.Symbols.ArrayType(s.Element, elements.Length);
                var storage = NewTemp(array);
                EmitAssign(Local(c.Syntax, storage), new BoundCollection(c.Syntax, array, elements));
                return new BoundConversion(c.Syntax, Local(c.Syntax, storage), s, ConversionKind.ArrayToSpan);
            }
            default:
            {
                var elements = c.Elements.Any(e => e is BoundSpread) ? Expanded(c.Elements) : Operands(c.Elements);
                return elements is null ? new LNever(c.Syntax, c.Type) : c with { Elements = elements };
            }
        }
    }

    /// <summary>
    /// The elements of an array or span literal with each spread fixed array replaced by its elements,
    /// evaluated left to right: every operand that is not trivial is stored first.
    /// </summary>
    private BoundExpr[]? Expanded(BoundExpr[] elements)
    {
        var values = new List<BoundExpr>();
        foreach (var element in elements)
        {
            var v = Expr(element is BoundSpread sp ? sp.Operand : element);
            if (IsNever(v)) return null;
            if (element is BoundSpread spread)
            {
                var source = Spill(v);
                var length = ((Semantics.Symbols.ArrayType)spread.Operand.Type).Length;
                for (var i = 0L; i < length; i++)
                    values.Add(new LIndexUnchecked(spread.Syntax, source, I64(spread.Syntax, i), spread.Type));
                continue;
            }
            values.Add(IsTrivial(v) ? v : Spill(v));
        }
        return values.ToArray();
    }

    /// <summary>Runs <paramref name="add"/> on each element of the list, span or array in <paramref name="source"/>, in order.</summary>
    private void AddEach(BoundSpread spread, BoundExpr source, Action<BoundExpr> add)
    {
        var syntax = spread.Syntax;
        var lengthMember = source.Type switch
        {
            SpanType => BuiltinMember.SpanLength,
            ListType => BuiltinMember.ListLength,
            _ => BuiltinMember.ArrayLength,
        };
        var index = NewTemp(PrimitiveType.I64);
        EmitAssign(Local(syntax, index), I64(syntax, 0));
        var done = NewLabel("break");
        var body = Capture(() =>
        {
            var length = new BoundBuiltinCall(syntax, lengthMember, source, [], PrimitiveType.I64);
            Emit(new BoundIf(syntax, Not(new BoundBinary(syntax, BinaryOp.Lt, Local(syntax, index), length, PrimitiveType.Bool)),
                Block(syntax, [new LGoto(syntax, done)]), null));
            add(new LIndexUnchecked(syntax, source, Local(syntax, index), spread.Type));
            Emit(new BoundAssign(syntax, Local(syntax, index),
                new BoundBinary(syntax, BinaryOp.AddWrap, Local(syntax, index), I64(syntax, 1), PrimitiveType.I64), null));
        });
        Emit(new LLoop(syntax, Block(syntax, body)));
        Emit(new LLabel(syntax, done));
    }

    private BoundExpr Slice(BoundSlice s)
    {
        var operands = new List<BoundExpr> { s.Target };
        if (s.Start is not null) operands.Add(s.Start);
        if (s.End is not null) operands.Add(s.End);
        var arrayTarget = s.Target.Type is Semantics.Symbols.ArrayType;
        var parts = Operands(operands, k => k == 0 && arrayTarget && IsLvalueShape(s.Target));
        if (parts is null) return new LNever(s.Syntax, s.Type);
        var target = arrayTarget && !IsLvalue(parts[0]) ? Spill(parts[0]) : parts[0];
        if (!IsTrivial(target) && !arrayTarget) target = Spill(target);
        var k = 1;
        var start = s.Start is null ? null : parts[k++];
        var end = s.End is null ? null : parts[k];
        return s with { Target = target, Start = start, End = end };
    }

    private BoundExpr Call(BoundCall c)
    {
        switch (c.Function.Builtin)
        {
            case BuiltinFunction.Print when c.Arguments[0] is BoundInterpolated i:
                // The stream is the target, as a log level is.
                return WriteParts(c, WriteSink.Print, null, i, level: c.Arguments[1]);
            case BuiltinFunction.Log when c.Arguments[0] is BoundInterpolated i:
                // The level is the target, evaluated after the text's parts as in the plain-string call.
                return WriteParts(c, WriteSink.Log, null, i, level: c.Arguments[1]);
            case BuiltinFunction.Panic when c.Arguments[0] is BoundInterpolated i:
                Write(c, WriteSink.Panic, null, i);
                return new LNever(c.Syntax, c.Type);
        }
        if (c.Function.Fails) throw new InvalidOperationException($"call of failing `{c.Function.Name}` outside try/catch");
        var lowered = CallParts(c);
        if (lowered is null) return new LNever(c.Syntax, c.Type);
        if (c.Type.IsNever)
        {
            Emit(new BoundExprStmt(c.Syntax, lowered));
            return new LNever(c.Syntax, c.Type);
        }
        if (c.Type.IsVoid)
        {
            Emit(new BoundExprStmt(c.Syntax, lowered));
            return new LVoid(c.Syntax);
        }
        return lowered;
    }

    private static bool Printable(TypeSymbol t) => t.IsInteger || t.IsFloat || t.IsBool || t.IsString || t is EnumSymbol;

    /// <summary>
    /// <c>assert e;</c>: each printable sub-expression of <c>e</c> that is not a literal is stored once in a temporary as it
    /// is lowered, and a failure panics with the statement and the <see cref="PowerAssert"/> diagram of those values. A
    /// side that may not run (the right of <c>&amp;&amp;</c>, <c>||</c>, <c>??</c>, a branch of <c>?:</c>) also sets a flag
    /// when it does, and its values are shown only then.
    /// </summary>
    private void Assert(BoundAssert a)
    {
        if (a.FailsWith is not null)
        {
            AssertFails(a, a.FailsWith);
            return;
        }
        var syntax = (AssertStmt)a.Syntax;
        var source = _fn.File!.Source;
        string Before(int offset) => source.GetText(TextSpan.FromBounds(syntax.Span.Start, offset));
        int After(Node left)
        {
            var rest = source.GetText(TextSpan.FromBounds(left.Span.End, syntax.Condition.Span.End));
            return left.Span.End + rest.Length - rest.TrimStart().Length;
        }
        int Anchor(Node node) => node switch
        {
            ParenExpr p => Anchor(p.Inner),
            MemberExpr m => m.Member.Span.Start,
            CallExpr c => Anchor(c.Callee),
            BinaryExpr b => After(b.Left),
            IsExpr i => After(i.Value),
            IndexExpr i => After(i.Target),
            ConditionalExpr c => After(c.Condition),
            _ => node.Span.Start,
        };
        // The first (outermost) node at a column wins: an implicit conversion shares its operand's syntax.
        var columns = new SortedDictionary<int, BoundExpr>();
        var lazy = new HashSet<BoundExpr>(ReferenceEqualityComparer.Instance);
        foreach (var (e, mayNotRun) in Shown(a.Condition, false))
            if (columns.TryAdd(PowerAssert.Column(Before(Anchor(e.Syntax))), e) && mayNotRun) lazy.Add(e);
        _shown = new Dictionary<BoundExpr, BoundExpr?>(ReferenceEqualityComparer.Instance);
        _ran = new Dictionary<BoundExpr, LocalSymbol>(ReferenceEqualityComparer.Instance);
        foreach (var e in columns.Values)
        {
            _shown[e] = null;
            if (!lazy.Contains(e)) continue;
            var flag = NewTemp(PrimitiveType.Bool);
            EmitAssign(Local(e.Syntax, flag), Bool(e.Syntax, false));
            _ran[e] = flag;
        }
        var condition = Expr(a.Condition);
        var (values, ran) = (_shown, _ran);
        (_shown, _ran) = (null, null);
        if (IsNever(condition)) return;
        var statement = PowerAssert.Flatten(Before(syntax.Condition.Span.End));
        var kept = columns.Where(c => values[c.Value] is { } v && !IsNever(v)).ToList();
        BoundStmt panic;
        if (kept.Count == 0) panic = new LPanic(a.Syntax, statement);
        else
        {
            var flags = kept.Select(c => ran.GetValueOrDefault(c.Value)).ToList();
            BoundExpr Flag(int k) => Local(a.Syntax, flags[k]!);
            BoundExpr? Guard(PowerAssert.Piece p)
            {
                var terms = p.When.Select(Flag).Concat(p.Unless.Select(k => Not(Flag(k))));
                if (p.AnyFrom is { } from) terms = terms.Append(Enumerable.Range(from, kept.Count - from).Select(Flag).Aggregate(Or));
                return terms.Aggregate((BoundExpr?)null, (g, t) => g is null ? t : And(g, t));
            }
            var parts = new List<BoundInterpolationPart> { new(statement, null) };
            var guards = new List<BoundExpr?> { null };
            foreach (var piece in PowerAssert.Diagram(kept.Select(c => c.Key).ToList(), flags.Select(f => f is null).ToList()))
            {
                var guard = Guard(piece);
                if (guard is null && piece.Value < 0 && guards[^1] is null && parts[^1].Value is null)
                    parts[^1] = new(parts[^1].Text + piece.Text, null);
                else
                {
                    parts.Add(piece.Value < 0 ? new(piece.Text, null) : new(null, values[kept[piece.Value].Value]));
                    guards.Add(guard);
                }
            }
            panic = new LWrite(a.Syntax, WriteSink.Panic, null, parts.ToArray(), guards.All(g => g is null) ? null : guards.ToArray());
        }
        Emit(new BoundIf(a.Syntax, Not(condition), Block(a.Syntax, [panic]), null));
    }

    /// <summary>
    /// <c>assert f() fails E[.x]</c>: panics with the statement and a bar under the call, hung with what it returned or
    /// failed with, when the call succeeds or fails with another error.
    /// </summary>
    private void AssertFails(BoundAssert a, ErrorSetSymbol set)
    {
        var syntax = (AssertStmt)a.Syntax;
        var call = (BoundCall)a.Condition;
        if (FailingCall(call) is not var (error, result)) return;
        BoundExpr Is(ErrorVariantSymbol v) =>
            new BoundBinary(a.Syntax, BinaryOp.Eq, Local(a.Syntax, error), new BoundErrorVariant(a.Syntax, v), PrimitiveType.Bool);
        var expected = a.FailsCase is { } only ? Is(only) : set.Variants.Select(Is).Aggregate(Or);
        var source = _fn.File!.Source;
        var statement = PowerAssert.Flatten(source.GetText(syntax.Span));
        var anchor = ((CallExpr)call.Syntax).Callee is MemberExpr m ? m.Member.Span.Start : call.Syntax.Span.Start;
        var gap = new string(' ', PowerAssert.Column(source.GetText(TextSpan.FromBounds(syntax.Span.Start, anchor))));
        var failed = Failed(a.Syntax, error);
        var parts = new List<BoundInterpolationPart>
        {
            new(statement + "\n" + PowerAssert.Indent + gap + PowerAssert.Bar + "\n" + PowerAssert.Indent + gap, null),
            new("returned", null),
        };
        var guards = new List<BoundExpr?> { null, Not(failed) };
        foreach (var v in call.Function.ErrorSets.SelectMany(e => e.Variants))
        {
            parts.Add(new($"failed with {v.Set.DisplayName}.{v.Name}", null));
            guards.Add(Is(v));
        }
        if (Printable(call.Type))
        {
            parts.Add(new(" ", null));
            parts.Add(new(null, result));
            guards.Add(Not(failed));
            guards.Add(Not(failed));
        }
        var panic = new LWrite(a.Syntax, WriteSink.Panic, null, parts.ToArray(), guards.ToArray());
        Emit(new BoundIf(a.Syntax, Not(expected), Block(a.Syntax, [panic]), null));
    }

    /// <summary>The sub-expressions of an <c>assert</c> condition shown on failure, outermost first, each with whether it may not run.</summary>
    private static IEnumerable<(BoundExpr Expr, bool MayNotRun)> Shown(BoundExpr e, bool mayNotRun)
    {
        if (Printable(e.Type) && !IsLiteral(e.Syntax)) yield return (e, mayNotRun);
        IEnumerable<(BoundNode, bool)> children = e switch
        {
            BoundBinary { Op: BinaryOp.LogicalAnd or BinaryOp.LogicalOr } b => [(b.Left, mayNotRun), (b.Right, true)],
            BoundConditional c => [(c.Condition, mayNotRun), (c.WhenTrue, true), (c.WhenFalse, true)],
            BoundCoalesce c => [(c.Left, mayNotRun), (c.Right, true)],
            BoundIs i => [(i.Value, mayNotRun)],
            BoundBinary or BoundUnary or BoundField or BoundTupleElement or BoundUnionField or BoundCall or BoundBuiltinCall or BoundConversion or BoundIndex
                => BoundTree.Children(e).Select(c => (c, mayNotRun)),
            _ => [],
        };
        foreach (var (child, lazy) in children)
            if (child is BoundExpr x)
                foreach (var shown in Shown(x, lazy)) yield return shown;
    }

    private static bool IsLiteral(Node syntax) => syntax switch
    {
        LiteralExpr => true,
        ParenExpr p => IsLiteral(p.Inner),
        UnaryExpr { Op: UnaryOp.Negate or UnaryOp.Plus } u => IsLiteral(u.Operand),
        _ => false,
    };

    /// <summary>Lowers the receiver and arguments of a call; the receiver and <c>ref</c>/<c>out</c> arguments stay addressable.</summary>
    private BoundCall? CallParts(BoundCall c)
    {
        var operands = new List<BoundExpr>();
        // A call through a function value keeps the value as its receiver.
        var hasReceiver = c.Receiver is not null && (c.Function.HasThis || c.Function.Kind == FunctionKind.Invoke);
        if (hasReceiver) operands.Add(c.Receiver!);
        operands.AddRange(c.Arguments);
        var offset = hasReceiver ? 1 : 0;
        bool Addressable(int k)
        {
            if (hasReceiver && k == 0) return IsLvalueShape(c.Receiver!);
            var p = k - offset;
            return p < c.Function.Parameters.Count && c.Function.Parameters[p].IsByRef;
        }
        var parts = Operands(operands, Addressable);
        if (parts is null) return null;
        BoundExpr? receiver = null;
        if (hasReceiver)
        {
            receiver = parts[0];
            // A function value is named twice in C (its code and its environment), so anything but a local is evaluated once first.
            if (!IsLvalue(receiver) || c.Function.Kind == FunctionKind.Invoke && receiver is not BoundLocal) receiver = Spill(receiver);
            // `map[k].f()` with `ref this` changes the stored value; reading through the panicking index first keeps a missing key out.
            if (receiver is BoundBuiltinCall { Member: BuiltinMember.MapIndexSet })
            {
                var place = (BoundBuiltinCall)Stable(receiver);
                Emit(new BoundExprStmt(c.Syntax, place with { Member = BuiltinMember.MapIndex }));
                receiver = place;
            }
        }
        return c with { Receiver = receiver, Arguments = parts[offset..] };
    }

    private BoundExpr BuiltinCall(BoundBuiltinCall b)
    {
        if (b.Member == BuiltinMember.StrBufAppend && b.Arguments[0] is BoundInterpolated i)
        {
            var target = Lvalue(b.Receiver);
            if (IsNever(target)) return target;
            return WriteParts(b, WriteSink.StrBuf, target, i);
        }
        // `x.take()`: move the value out, then leave the type's empty value in the place, evaluated once.
        if (b.Member is BuiltinMember.ListTake or BuiltinMember.MapTake or BuiltinMember.StrBufTake or BuiltinMember.OptionalTake)
        {
            var place = Lvalue(b.Receiver);
            if (IsNever(place)) return place;
            place = Stable(place);
            // `map[k].take()` reads through the panicking index, so a missing key is not inserted.
            var taken = Spill(place is BoundBuiltinCall { Member: BuiltinMember.MapIndexSet } read ? read with { Member = BuiltinMember.MapIndex } : place);
            EmitAssign(place, b.Member == BuiltinMember.StrBufTake
                ? new BoundCall(b.Syntax, _builtins.StrBufGrow, null, [], b.Type)
                : new BoundDefault(b.Syntax, b.Type));
            return taken;
        }
        var mutating = b.Member is BuiltinMember.ListAdd or BuiltinMember.ListClear or BuiltinMember.ListRemoveLast or BuiltinMember.ListRemoveRange or BuiltinMember.ListRemoveThrough or BuiltinMember.ListRemoveAt or BuiltinMember.ListRemoveAtRange
            or BuiltinMember.ListReplace or BuiltinMember.ListSort
            or BuiltinMember.MapIndexSet or BuiltinMember.MapSlot or BuiltinMember.MapRemove or BuiltinMember.MapClear
            or BuiltinMember.StrBufAppend or BuiltinMember.StrBufAppendRepeated or BuiltinMember.StrBufInsert or BuiltinMember.StrBufClear or BuiltinMember.ArrayAsSpan or BuiltinMember.ListMutableSpan or BuiltinMember.ArrayMutableSpan;
        var operands = new List<BoundExpr> { b.Receiver };
        operands.AddRange(b.Arguments);
        // `slot` inserts the value type's default: field initializers for a struct, zero otherwise.
        if (b.Member == BuiltinMember.MapSlot && operands.Count == 2)
            operands.Add(b.Type is StructSymbol s ? new BoundStructLiteral(b.Syntax, s, s.Fields.Select(f => f.Default ?? new BoundDefault(b.Syntax, f.Type)).ToArray()) : new BoundDefault(b.Syntax, b.Type));
        var parts = Operands(operands, k => k == 0 && mutating && IsLvalueShape(b.Receiver));
        if (parts is null) return new LNever(b.Syntax, b.Type);
        var receiver = parts[0];
        if (mutating && !IsLvalue(receiver)) receiver = Spill(receiver);
        else if (!mutating && !IsTrivial(receiver) && b.Member is BuiltinMember.ListAsSpan or BuiltinMember.StrBufAsString) receiver = Spill(receiver);
        var lowered = b with { Receiver = receiver, Arguments = parts[1..] };
        if (b.Type.IsVoid)
        {
            Emit(new BoundExprStmt(b.Syntax, lowered));
            return new LVoid(b.Syntax);
        }
        return lowered;
    }

    /// <summary>
    /// <paramref name="place"/> with every index, key and non-addressable receiver in it stored in a
    /// temporary, innermost first, so reading it and then writing it names the same storage.
    /// </summary>
    private BoundExpr Stable(BoundExpr place) => place switch
    {
        BoundField f => f with { Receiver = Stable(f.Receiver) },
        BoundTupleElement t => t with { Receiver = Stable(t.Receiver) },
        LOptionalValue v => v with { Optional = Stable(v.Optional) },
        // A list's elements live behind its pointer, so a copy of a non-addressable list still names them.
        BoundIndex i => i with { Target = Stable(i.Target), Index = IsTrivial(i.Index) ? i.Index : Spill(i.Index) },
        BoundBuiltinCall { Member: BuiltinMember.MapIndexSet or BuiltinMember.MapSlot } b =>
            b with { Receiver = Stable(b.Receiver), Arguments = b.Arguments.Select(a => IsTrivial(a) ? a : Spill(a)).ToArray() },
        _ => IsTrivial(place) || IsLvalue(place) ? place : Spill(place),
    };

    private BoundExpr Write(BoundCall c, WriteSink sink, BoundExpr? target, BoundInterpolated text) => WriteParts(c, sink, target, text);

    /// <param name="level">A log level or print stream, lowered after the parts and written as the target.</param>
    private BoundExpr WriteParts(BoundExpr call, WriteSink sink, BoundExpr? target, BoundInterpolated text, BoundExpr? level = null)
    {
        // A `Symbol` writes its text (spec 008 R4).
        var values = text.Parts.Where(p => p.Value is not null)
            .Select(p => p.Value!.Type is BuiltinType { Kind: BuiltinKind.Symbol }
                ? new BoundBuiltinCall(p.Value.Syntax, BuiltinMember.SymbolText, p.Value, [], PrimitiveType.String)
                : p.Value).ToList();
        if (level is not null) values.Add(level);
        var lowered = Operands(values);
        if (lowered is null) return new LNever(call.Syntax, call.Type);
        if (level is not null)
        {
            target = lowered[^1];
            if (!IsTrivial(target))
            {
                // Spilling the target evaluates it ahead of the call (the emitter would evaluate it last inside the
                // call), so the parts before it are spilled first to keep them evaluated before it.
                for (var j = 0; j < lowered.Length - 1; j++)
                    if (!IsTrivial(lowered[j])) lowered[j] = Spill(lowered[j]);
                target = Spill(target);
            }
        }
        var k = 0;
        var parts = text.Parts.Select(p => p.Value is null ? p : p with { Value = lowered[k++] }).ToArray();
        Emit(new LWrite(call.Syntax, sink, target, parts));
        return new LVoid(call.Syntax);
    }

    private BoundExpr SwitchExpr(BoundSwitchExpr s)
    {
        var value = Expr(s.Value);
        if (IsNever(value)) return value;
        if (!IsRepeatable(value)) value = Spill(value);
        var result = s.Type.IsNever ? null : NewTemp(s.Type);
        var end = NewLabel("switch_end");
        foreach (var arm in s.Arms)
        {
            var match = Match(arm.Pattern, value);
            var body = Capture(() =>
            {
                Claim(arm.Pattern);
                var v = Expr(arm.Value);
                if (result is not null) EmitAssign(Local(arm.Syntax, result), v);
                else EmitEffect(v);
                Emit(new LGoto(arm.Syntax, end));
            });
            if (arm.Guard is null)
            {
                Emit(new BoundIf(arm.Syntax, match, Block(arm.Syntax, body), null));
                continue;
            }
            var (guard, pre) = CaptureExpr(arm.Guard);
            if (pre.Count == 0)
            {
                Emit(new BoundIf(arm.Syntax, And(match, guard), Block(arm.Syntax, body), null));
            }
            else
            {
                pre.Add(new BoundIf(arm.Syntax, guard, Block(arm.Syntax, body), null));
                Emit(new BoundIf(arm.Syntax, match, Block(arm.Syntax, pre), null));
            }
        }
        Emit(new LPanic(s.Syntax, "no switch arm matched"));
        Emit(new LLabel(s.Syntax, end));
        return result is null ? new LNever(s.Syntax, s.Type) : Local(s.Syntax, result);
    }

    /// <summary><c>u8.tryFrom(x)</c>: <c>x</c> in range ? <c>u8(x)</c> : <c>null</c>; a float out of range or NaN gives <c>null</c>.</summary>
    private BoundExpr TryFrom(BoundTryFrom t)
    {
        var value = Expr(t.Operand);
        if (IsNever(value)) return value;
        if (!IsRepeatable(value)) value = Spill(value);
        var from = (PrimitiveType)value.Type;
        var syntax = t.Syntax;
        BoundExpr? fits = null;
        void Check(BinaryOp op, BoundExpr bound)
        {
            var test = new BoundBinary(syntax, op, value, bound, PrimitiveType.Bool);
            fits = fits is null ? test : And(fits, test);
        }
        if (t.Target.IsInteger && from.IsFloat)
        {
            var (min, max) = t.Target.Range;
            // A value above min - 1 truncates into range; when min - 1 rounds to min in `from` (i64 from f64, i32
            // from f32) no value lies between them, so the bound is min itself, inclusive.
            var below = from.Equals(PrimitiveType.F32) ? (float)((double)min - 1) : (double)min - 1;
            if (below == (double)min) Check(BinaryOp.Ge, new BoundLiteral(syntax, from, new ConstantValue((double)min)));
            else Check(BinaryOp.Gt, new BoundLiteral(syntax, from, new ConstantValue(below)));
            Check(BinaryOp.Lt, new BoundLiteral(syntax, from, new ConstantValue((double)max + 1)));
        }
        else if (t.Target.IsInteger)
        {
            var (min, max) = t.Target.Range;
            var (fromMin, fromMax) = from.Range;
            if (min > fromMin) Check(BinaryOp.Ge, new BoundLiteral(syntax, from, new ConstantValue(min)));
            if (max < fromMax) Check(BinaryOp.Le, new BoundLiteral(syntax, from, new ConstantValue(max)));
        }
        var kind = from.Equals(t.Target) ? (ConversionKind?)null : ConversionKind.NumericCast;
        var converted = kind is null ? value : new BoundConversion(syntax, value, t.Target, kind.Value);
        var some = new BoundConversion(syntax, converted, t.Type, ConversionKind.ToOptional);
        if (fits is null) return some;
        return Expr(new BoundConditional(syntax, fits, some, new BoundDefault(syntax, t.Type), t.Type));
    }

    // ── Failure ──────────────────────────────────────────────────────────────────

    /// <summary>Emits the failing call; returns its error local and its result (or <see cref="LVoid"/>).</summary>
    private (LocalSymbol Error, BoundExpr Result)? FailingCall(BoundExpr operand)
    {
        var call = (BoundCall)operand;
        // A `requires` check takes the built value as `this`; the value, not the void call, is the result.
        if (call.Function is { Name: "requires", Owner: not null })
        {
            var value = Spill(Expr(call.Receiver!));
            if (IsNever(value)) return null;
            var check = NewTemp(ErrorUnionType.Of(call.Function.ErrorSets));
            Emit(new LFailingCall(call.Syntax, check, call with { Receiver = value }, null));
            return (check, value);
        }
        var lowered = CallParts(call);
        if (lowered is null) return null;
        var error = NewTemp(ErrorUnionType.Of(call.Function.ErrorSets));
        var result = call.Type.IsVoid || call.Type.IsNever ? null : NewTemp(call.Type);
        Emit(new LFailingCall(call.Syntax, error, lowered, result));
        return (error, result is null ? new LVoid(call.Syntax) : Local(call.Syntax, result));
    }

    private BoundExpr Failed(Node syntax, LocalSymbol error) =>
        new BoundBinary(syntax, BinaryOp.Ne, new LErrorCode(syntax, Local(syntax, error)), new BoundLiteral(syntax, PrimitiveType.U32, new ConstantValue((Int128)0)), PrimitiveType.Bool);

    /// <summary>Runs the cleanups (errdefers included) and fails the function with <paramref name="error"/>.</summary>
    private List<BoundStmt> Propagate(BoundExpr origin, LocalSymbol error) => Capture(() =>
    {
        var syntax = origin.Syntax;
        EmitCleanups(0, error);
        CountError(origin, error);
        Emit(new LReturnError(syntax, Local(syntax, error)));
    });

    private BoundExpr Try(BoundTry t)
    {
        if (FailingCall(t.Operand) is not var (error, result)) return new LNever(t.Syntax, t.Type);
        Emit(new BoundIf(t.Syntax, Failed(t.Syntax, error), Block(t.Syntax, Propagate(t, error)), null));
        return result;
    }

    private BoundExpr Catch(BoundCatch c)
    {
        if (FailingCall(c.Operand) is not var (error, result)) return new LNever(c.Syntax, c.Type);
        var target = result is BoundLocal r ? r : null;
        var handler = Capture(() =>
        {
            if (c.Binding is { } binding)
            {
                Note(binding);
                Emit(new BoundAssign(c.Syntax, Local(c.Syntax, binding), Local(c.Syntax, error), null));
            }
            var h = Expr(c.Handler);
            if (target is not null) EmitAssign(target, h);
            else EmitEffect(h);
        });
        if (c.Variant is { } variant)
        {
            var other = new BoundBinary(c.Syntax, BinaryOp.Ne, Local(c.Syntax, error),
                new BoundErrorVariant(c.Syntax, variant), PrimitiveType.Bool);
            handler.Insert(0, new BoundIf(c.Syntax, other, Block(c.Syntax, Propagate(c, error)), null));
        }
        Emit(new BoundIf(c.Syntax, Failed(c.Syntax, error), Block(c.Syntax, handler), null));
        return result;
    }

    // ── Patterns ─────────────────────────────────────────────────────────────────

    /// <summary>A condition that tests <paramref name="value"/> (trivial) and binds the pattern's variables as it goes.</summary>
    private BoundExpr Match(BoundPattern p, BoundExpr value)
    {
        var syntax = p.Syntax;
        var optional = value.Type as OptionalType;
        BoundExpr Inner() => new LOptionalValue(syntax, value, optional!.Inner);
        switch (p)
        {
            case BoundBindPattern { Local: null }:
                return Bool(syntax, true);
            case BoundBindPattern { Local: { } local }:
                Note(local);
                return new LSequence(syntax, [new LAssignExpr(syntax, Local(syntax, local), value)], Bool(syntax, true));
            case BoundConstantPattern c when c.Value is BoundDefault && optional is not null:
                return Not(new LOptionalHas(syntax, value));
            case BoundConstantPattern c:
                return optional is not null && c.Value.Type is not OptionalType
                    ? And(new LOptionalHas(syntax, value), Equal(syntax, Inner(), c.Value))
                    : Equal(syntax, value, c.Value);
            case BoundRelationalPattern r:
            {
                var v = optional is not null ? Inner() : value;
                var test = new BoundBinary(syntax, r.Op, v, r.Value, PrimitiveType.Bool);
                return optional is not null ? And(new LOptionalHas(syntax, value), test) : test;
            }
            case BoundRangePattern r:
            {
                var v = optional is not null ? Inner() : value;
                var test = And(new BoundBinary(syntax, BinaryOp.Ge, v, r.Start, PrimitiveType.Bool),
                    new BoundBinary(syntax, r.Inclusive ? BinaryOp.Le : BinaryOp.Lt, v, r.End, PrimitiveType.Bool));
                return optional is not null ? And(new LOptionalHas(syntax, value), test) : test;
            }
            case BoundNotPattern n:
                return Not(Match(n.Operand, value));
            case BoundBinaryPattern b:
                return b.IsAnd ? And(Match(b.Left, value), Match(b.Right, value)) : Or(Match(b.Left, value), Match(b.Right, value));
            case BoundPropertyPattern pp:
            {
                var tests = new List<BoundExpr>();
                var subject = value;
                if (optional is not null)
                {
                    tests.Add(new LOptionalHas(syntax, value));
                    subject = Inner();
                }
                foreach (var sub in pp.Subpatterns)
                {
                    var member = subject;
                    foreach (var step in sub.Path) member = Step(syntax, member, step);
                    tests.Add(Match(sub.Pattern, member));
                }
                if (pp.Designation is { } d)
                {
                    Note(d);
                    tests.Add(new LSequence(syntax, [new LAssignExpr(syntax, Local(syntax, d), subject)], Bool(syntax, true)));
                }
                return tests.Count == 0 ? Bool(syntax, true) : tests.Aggregate(And);
            }
            case BoundUnionPattern up:
            {
                var subject = optional is not null ? Inner() : value;
                var tag = new ConstantValue((Int128)up.Variant.Tag);
                var tests = new List<BoundExpr>();
                if (optional is not null) tests.Add(new LOptionalHas(syntax, value));
                tests.Add(Equal(syntax, new LUnionTag(syntax, subject), new BoundLiteral(syntax, PrimitiveType.I32, tag) { Constant = tag }));
                for (var i = 0; i < up.Subpatterns.Length; i++)
                    tests.Add(Match(up.Subpatterns[i], new LUnionField(syntax, subject, up.Variant, i)));
                if (up.Designation is { } d)
                {
                    Note(d);
                    tests.Add(new LSequence(syntax, [new LAssignExpr(syntax, Local(syntax, d), subject)], Bool(syntax, true)));
                }
                return tests.Aggregate(And);
            }
            case BoundErrorPattern ep:
            {
                var code = new ConstantValue((Int128)ep.Variant.Code);
                var tests = new List<BoundExpr>
                {
                    Equal(syntax, new LErrorCode(syntax, value), new BoundLiteral(syntax, PrimitiveType.U32, code) { Constant = code }),
                };
                for (var i = 0; i < ep.Subpatterns.Length; i++)
                    tests.Add(Match(ep.Subpatterns[i], new LErrorField(syntax, value, ep.Variant, i)));
                return tests.Aggregate(And);
            }
            default:
                throw new InvalidOperationException($"cannot lower pattern {p.GetType().Name}");
        }
    }

    /// <summary>The selected arm's owning bindings take their storage; binding them in <see cref="Match"/> does not, as a later test may fail.</summary>
    private void Claim(BoundPattern p)
    {
        switch (p)
        {
            case BoundBindPattern { Local: { BorrowsSubject: false } local }:
                Emit(new LClaim(p.Syntax, local));
                break;
            case BoundNotPattern n:
                Claim(n.Operand);
                break;
            case BoundBinaryPattern b:
                Claim(b.Left);
                Claim(b.Right);
                break;
            case BoundPropertyPattern pp:
                foreach (var sub in pp.Subpatterns) Claim(sub.Pattern);
                if (pp.Designation is { BorrowsSubject: false } d) Emit(new LClaim(p.Syntax, d));
                break;
            case BoundUnionPattern up:
                foreach (var sub in up.Subpatterns) Claim(sub);
                if (up.Designation is { BorrowsSubject: false } ud) Emit(new LClaim(p.Syntax, ud));
                break;
            case BoundErrorPattern ep:
                foreach (var sub in ep.Subpatterns) Claim(sub);
                break;
        }
    }

    private static BoundExpr Step(Node syntax, BoundExpr receiver, BoundMemberStep step)
    {
        if (step.Field is { } f) return new BoundField(syntax, receiver, f);
        if (step.Property is { } p) return new BoundCall(syntax, p.Getter, receiver, [], p.Type);
        if (step.Builtin is { } b) return new BoundBuiltinCall(syntax, b, receiver, [], step.Type);
        return new BoundTupleElement(syntax, receiver, step.TupleIndex, step.Type);
    }

    private static BoundExpr Equal(Node syntax, BoundExpr a, BoundExpr b) => new BoundBinary(syntax, BinaryOp.Eq, a, b, PrimitiveType.Bool);
}
