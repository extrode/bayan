using System.Collections.Immutable;
using Bayan.Semantics.Symbols;
using Bayan.Syntax.Diagnostics;
using Bayan.Syntax.Text;

namespace Bayan.Semantics.Binding;

/// <summary>
/// Region rules (spec §9.4), the conservative v0.1 version. A borrow may not be stored anywhere
/// that outlives the storage it points into (rule 3), a call's result borrows from every argument
/// (rule 4), and a value may not be structurally modified while a borrow into it is still used
/// (rules 5 and 6). The analysis is flow-insensitive: a local borrows from everything ever stored
/// in it, and a borrow stays alive until the holder's last use (to the end of any loop that uses it).
/// </summary>
internal sealed class RegionAnalysis
{
    /// <summary>Storage freed when the block at <see cref="Depth"/> (1 = the function body) ends; <see cref="Source"/> marks a parameter's (or <c>this</c>'s) data in a summary.</summary>
    private sealed record Region(int Depth, string Name, int Source = NoSource);

    private const int NoSource = int.MinValue;

    /// <summary>The index standing for <c>this</c> in a store summary.</summary>
    private const int This = -1;

    /// <summary>
    /// What a value points into: <see cref="Regions"/> bound its lifetime; <see cref="Owners"/>
    /// are locals whose storage it borrows directly, which must not change structurally. <see cref="Lends"/> holds where
    /// each owner's own storage was first lent into this value, for loans that start inside the expression storing them.
    /// </summary>
    private sealed record Borrow(ImmutableHashSet<Region> Regions, ImmutableHashSet<LocalSymbol> Owners, ImmutableDictionary<LocalSymbol, int>? Lends = null)
    {
        public static readonly Borrow None = new([], []);

        public bool IsEmpty => Regions.IsEmpty && Owners.IsEmpty;

        public Borrow Union(Borrow other) =>
            other.IsEmpty ? this : IsEmpty ? other : new(Regions.Union(other.Regions), Owners.Union(other.Owners), Merge(Lends, other.Lends));

        /// <summary>This borrow plus a loan on <paramref name="owner"/>'s own storage, lent at <paramref name="at"/>.</summary>
        public Borrow Lent(LocalSymbol owner, int at) =>
            new(Regions, Owners.Add(owner), Merge(Lends, ImmutableDictionary<LocalSymbol, int>.Empty.Add(owner, at)));

        private static ImmutableDictionary<LocalSymbol, int>? Merge(ImmutableDictionary<LocalSymbol, int>? a, ImmutableDictionary<LocalSymbol, int>? b)
        {
            if (a is null) return b;
            if (b is null) return a;
            foreach (var (owner, at) in b)
                a = a.SetItem(owner, a.TryGetValue(owner, out var other) ? Math.Min(at, other) : at);
            return a;
        }
    }

    private sealed record Loan(LocalSymbol Owner, LocalSymbol? Holder, int Created, int LoopEnd);

    /// <summary><paramref name="Through"/>: the writable <c>dyn ref</c> local the change goes through, if any.</summary>
    private sealed record Mutation(LocalSymbol Target, TextSpan Span, bool Moved = false, LocalSymbol? Through = null);

    private readonly Compilation _c;
    private readonly FunctionSymbol _fn;
    private readonly SourceText _source;
    /// <summary>Stand for the storage of <c>this</c>'s fields: a borrow into one is a loan on it.</summary>
    private readonly Dictionary<FieldSymbol, LocalSymbol> _thisFields = new();
    private readonly Dictionary<LocalSymbol, Borrow> _locals = new();
    private readonly Dictionary<LocalSymbol, int> _depthOf = new();
    private readonly Dictionary<LocalSymbol, int> _declaredAt = new();
    private readonly List<Loan> _loans = new();
    private readonly List<Mutation> _mutations = new();
    /// <summary>For a writable <c>dyn ref</c> local, whether any value it was set to may hold storage a write through it can change.</summary>
    private readonly Dictionary<LocalSymbol, bool> _referentMayBorrow = new();
    /// <summary>A by-value loop variable over a list, array, span or map, with the iterable and its borrow: its lists share the element's storage.</summary>
    private readonly Dictionary<LocalSymbol, (BoundExpr Iterable, Borrow Borrow)> _loopItems = new();
    private readonly Stack<Borrow> _contexts = new();
    private readonly HashSet<(int, int)> _reported = new();
    /// <summary>The regions standing for <c>rest</c> parameters, with the parameter's name.</summary>
    private readonly Dictionary<Region, string> _restTails = new();
    private int _depth;
    /// <summary>The depth of the innermost loop's iteration scope, or 0 outside a loop.</summary>
    private int _iteration;
    /// <summary>The regions of capturing lambdas' environments, which a call result never borrows.</summary>
    private readonly HashSet<Region> _environments = new();
    private bool _reporting;
    /// <summary>Set while computing <see cref="_fn"/>'s store summary: nothing is reported, and stores into a target are recorded in <see cref="_pairs"/>.</summary>
    private readonly bool _summary;
    private readonly Dictionary<(int Source, int Target), TextSpan> _pairs = new();
    /// <summary>The parameters (or <see cref="This"/>) the result may borrow.</summary>
    private readonly HashSet<int> _returns = new();
    /// <summary>The summary's target parameters, with their index.</summary>
    private readonly Dictionary<LocalSymbol, int> _targets = new();

    private RegionAnalysis(Compilation c, FunctionSymbol fn, bool summary = false)
    {
        _c = c;
        _fn = fn;
        _source = fn.File!.Source;
        _summary = summary;
    }

    public static void Analyze(Compilation c, FunctionSymbol fn)
    {
        if (fn.File is null || fn.Body is null) return;
        new RegionAnalysis(c, fn).Run();
    }

    /// <summary>
    /// Q745: each template's store and return summaries, one least fixpoint from none, recomputed in declaration order until no summary grows.
    /// </summary>
    public static void Summarize(Compilation c)
    {
        var functions = c.Modules.SelectMany(m => m.AllFunctions)
            .Where(f => f.File is not null && f.Body is not null && !f.IsInstance && (HasTargets(f) || MayHold(f.ReturnType))).ToList();
        for (var changed = true; changed;)
        {
            changed = false;
            foreach (var fn in functions)
            {
                var analysis = new RegionAnalysis(c, fn, summary: true);
                analysis.Run();
                if (analysis._pairs.Count != (c.StoreSummaries.TryGetValue(fn, out var known) ? known.Count : 0))
                {
                    c.StoreSummaries[fn] = analysis._pairs;
                    changed = true;
                }
                if (analysis._returns.Count != (c.ReturnSummaries.TryGetValue(fn, out var returned) ? returned.Count : 0))
                {
                    c.ReturnSummaries[fn] = analysis._returns;
                    changed = true;
                }
            }
        }
    }

    /// <summary>Whether <paramref name="fn"/> has a summary target: a <c>ref</c>, <c>out</c> or writable <c>dyn ref</c> parameter, or a <c>ref this</c> receiver, that can hold a borrow.</summary>
    private static bool HasTargets(FunctionSymbol fn) =>
        fn.Parameters.Any(IsTarget) || fn.IsMutating && fn.HasThis && (fn.Owner is null || MayHold(fn.Owner));

    private static bool IsTarget(ParameterSymbol p) =>
        (p.Modifier is Syntax.Ast.ParamModifier.Ref or Syntax.Ast.ParamModifier.Out || p.Type is DynRefType { ReadOnly: false }) && MayHold(p.Type);

    /// <summary>Whether a value of <paramref name="t"/> may borrow, counting a type parameter as one that may (a summary covers every instance).</summary>
    private static bool MayHold(TypeSymbol t) => MayBorrow(t, []) || TypeParameterType.Occurs(t);

    private void Run()
    {
        foreach (var p in _fn.Parameters)
        {
            // An arena parameter is a handle to the caller's arena: what it allocates outlives the call.
            var callerOwned = p.Modifier is Syntax.Ast.ParamModifier.Ref or Syntax.Ast.ParamModifier.Out || p.Type == BuiltinType.Arena;
            _depthOf[p] = callerOwned ? 0 : 1;
            _declaredAt[p] = -1;
            // What a parameter points to belongs to the caller; the marker keeps that known after unions.
            if (_summary)
            {
                var index = _fn.Parameters.IndexOf(p);
                if (IsTarget(p)) _targets[p] = index;
                if (MayHold(p.Type)) _locals[p] = new Borrow([new Region(0, "the caller's data"), new Region(0, "the caller's data", index)], []);
            }
            else if (MayBorrow(p.Type, [])) _locals[p] = new Borrow([new Region(0, "the caller's data")], []);
            // A `rest` tail is borrowed for the call only (J62): it lives in the caller's frame for this statement.
            if (p.Syntax?.Rest == true && p.Type is SpanType)
            {
                var tail = new Region(1, $"`{p.Name}`");
                _restTails[tail] = p.Name;
                _locals[p] = new Borrow([tail], []);
            }
        }
        // The first pass collects what each local may borrow (a loop can store into a local
        // after its first use); the second reports against the complete picture.
        Stmt(_fn.Body!);
        _reporting = true;
        Stmt(_fn.Body!);
        if (!_summary) CheckMutations();
    }

    // ── Statements ───────────────────────────────────────────────────────────────

    private void Stmt(BoundStmt s)
    {
        switch (s)
        {
            case BoundBlock b:
                _depth++;
                foreach (var st in b.Statements) Stmt(st);
                _depth--;
                break;
            case BoundLocalDecl d:
                Declare(d.Local, d.Syntax.Span.Start);
                if (d.Initializer is not null)
                {
                    Store(d.Local, Expr(d.Initializer), d.Initializer.Syntax.Span);
                    Refer(d.Local, d.Initializer);
                }
                break;
            case BoundExprStmt e:
                Expr(e.Expr);
                break;
            case BoundAssert a:
                Expr(a.Condition);
                break;
            case BoundAssign a:
            {
                var value = Expr(a.Value);
                Expr(a.Target);
                StoreInto(a.Target, value, a.Value.Syntax.Span);
                if (a.Target is BoundLocal target) Refer(target.Local, a.Value);
                break;
            }
            case BoundReturn r when r.Value is not null:
                CheckReturn(Expr(r.Value), r.Value.Syntax.Span);
                break;
            case BoundFail f:
                Expr(f.Error);
                break;
            case BoundIf i:
                Expr(i.Condition);
                Stmt(i.Then);
                if (i.Else is not null) Stmt(i.Else);
                break;
            case BoundWhile w:
                Expr(w.Condition);
                Iterate(_depth + 1, w.Body);
                break;
            case BoundDoWhile w:
                Iterate(_depth + 1, w.Body);
                Expr(w.Condition);
                break;
            case BoundFor f:
            {
                var iterable = Expr(f.Iterable);
                // The loop reads the collection on every iteration: it holds a loan on it.
                if (_reporting)
                {
                    var owners = iterable.Owners;
                    if ((RootLocal(f.Iterable) ?? ThisField(f.Iterable)) is { } root && (f.Iterable.Type is ListType or MapType or ArrayType || f.UserIteration is not null)) owners = owners.Add(root);
                    foreach (var owner in owners) _loans.Add(new Loan(owner, null, f.Syntax.Span.Start, f.Syntax.Span.End));
                }
                _depth++;
                Declare(f.Variable, f.Syntax.Span.Start);
                // A `dyn ref` variable points into an array or list, like `dyn ref I d = xs[i]`, or where a span points; any other is a copy.
                var item = f.Variable.Type is not DynRefType ? iterable with { Owners = [] }
                    : f.Iterable.Type is ListType or ArrayType ? Lend(f.Iterable, iterable, f.Iterable.Syntax.Span.End)
                    : f.Iterable.Type is SpanType ? iterable
                    : iterable with { Owners = [] };
                Store(f.Variable, item, f.Iterable.Syntax.Span);
                // Q745: the item is a shallow copy the loop never frees, and `BorrowsSubject` refuses changing it (BYN2783).
                if (f.UserIteration is null && f.Variable is { Type: not DynRefType, BorrowsSubject: true } && f.Iterable.Type is ListType or ArrayType or SpanType or MapType)
                    _loopItems[f.Variable] = (f.Iterable, iterable);
                if (f.Variable.Type is DynRefType)
                    _referentMayBorrow[f.Variable] = f.Iterable.Type switch
                    {
                        ListType l => MayBorrow(l.Element, []),
                        ArrayType a => MayBorrow(a.Element, []),
                        SpanType sp => MayBorrow(sp.Element, []),
                        _ => true,
                    };
                Iterate(_depth, f.Body);
                _depth--;
                break;
            }
            case BoundSwitch sw:
            {
                var value = Expr(sw.Value);
                foreach (var section in sw.Sections)
                {
                    _depth++;
                    if (section.Pattern is not null) BindPattern(section.Pattern, value);
                    if (section.Guard is not null) Expr(section.Guard);
                    foreach (var st in section.Body) Stmt(st);
                    _depth--;
                }
                break;
            }
            case BoundDefer d:
                Stmt(d.Body);
                break;
            case BoundErrdefer e:
                if (e.Error is not null) Declare(e.Error, e.Syntax.Span.Start);
                Stmt(e.Body);
                break;
            case BoundPanicDefer p:
                if (p.Panic is not null) Declare(p.Panic, p.Syntax.Span.Start);
                Stmt(p.Body);
                break;
            case BoundContext c:
                _contexts.Push(AllocatedFrom(c.Allocator));
                Stmt(c.Body);
                _contexts.Pop();
                break;
        }
    }

    private void Iterate(int depth, BoundStmt body)
    {
        var outer = _iteration;
        _iteration = depth;
        Stmt(body);
        _iteration = outer;
    }

    private void Declare(LocalSymbol local, int position)
    {
        _depthOf[local] = _depth;
        _declaredAt[local] = position;
    }

    // ── Expressions ──────────────────────────────────────────────────────────────

    private Borrow Expr(BoundExpr e)
    {
        var result = Compute(e);
        return MayBorrow(e.Type, []) || _summary && TypeParameterType.Occurs(e.Type) ? result : Borrow.None;
    }

    private Borrow Compute(BoundExpr e)
    {
        switch (e)
        {
            case BoundLocal l:
                return _locals.GetValueOrDefault(l.Local, Borrow.None);
            case BoundThis when _summary:
                return new Borrow([new Region(0, "the caller's data", This)], []);
            case BoundLiteral or BoundDefault or BoundThis or BoundErrorVariant or BoundEnumMember or BoundEnumValues or BoundStaticArray:
                return Borrow.None;
            case BoundConversion c:
            {
                // A `dyn ref` to a span element points where the span points: it borrows what the span borrows.
                if (c.Kind is ConversionKind.ToDynRef && c.Operand is BoundIndex { Target.Type: SpanType } element)
                {
                    var span = Expr(element.Target);
                    Expr(element.Index);
                    return span;
                }
                var operand = Expr(c.Operand);
                return c.Kind is ConversionKind.ArrayToSpan or ConversionKind.ListToSpan or ConversionKind.ToDynRef ? Lend(c.Operand, operand, c.Syntax.Span.End) : operand;
            }
            case BoundMove m:
            {
                // The new owner may grow or free the storage: a move ends every borrow into it.
                var operand = Expr(m.Operand);
                Mutate(m.Operand, TextSpan.FromBounds(m.Syntax.Span.Start, Math.Max(m.Syntax.Span.End, m.Operand.Syntax.Span.End)), moved: true);
                return operand;
            }
            case BoundBuiltinCall b:
                return Builtin(b);
            case BoundCall c:
                return Call(c);
            case BoundSlice s:
            {
                var target = Expr(s.Target);
                if (s.Start is not null) Expr(s.Start);
                if (s.End is not null) Expr(s.End);
                return s.Target.Type is ListType or ArrayType ? Lend(s.Target, target, s.Syntax.Span.End) : target;
            }
            case BoundIndex i:
            {
                // The element is a copy: it may point where the collection's data points, but not into the collection.
                var target = Expr(i.Target);
                Expr(i.Index);
                return target with { Owners = [] };
            }
            case BoundSpread s:
                // Like an index: the elements are copies of what the sequence holds.
                return Expr(s.Operand) with { Owners = [] };
            case BoundStackalloc s:
                return new Borrow([new Region(_depth, $"the `stackalloc` buffer on line {Line(s.Syntax.Span)}")], []);
            case BoundCollection c:
            {
                var elements = Borrow.None;
                foreach (var el in c.Elements) elements = elements.Union(Expr(el));
                return c.Type switch
                {
                    SpanType => elements.Union(new Borrow([new Region(_depth, $"the array literal on line {Line(c.Syntax.Span)}")], [])),
                    ListType or MapType when _contexts.Count > 0 => elements.Union(_contexts.Peek()),
                    _ => elements,
                };
            }
            case BoundInterpolated i:
            {
                // The text is a copy formatted into `context.allocator`: it borrows that, not its holes (Q43).
                foreach (var part in i.Parts)
                    if (part.Value is not null) Expr(part.Value);
                return _contexts.Count > 0 ? _contexts.Peek() : Borrow.None;
            }
            case BoundJump j:
                Stmt(j.Jump);
                return Borrow.None;
            case BoundBlockExpr b:
                Stmt(b.Block);
                return Borrow.None;
            case BoundIs i:
                BindPattern(i.Pattern, Expr(i.Value));
                return Borrow.None;
            case BoundSwitchExpr s:
            {
                var value = Expr(s.Value);
                var result = Borrow.None;
                foreach (var arm in s.Arms)
                {
                    BindPattern(arm.Pattern, value);
                    if (arm.Guard is not null) Expr(arm.Guard);
                    result = result.Union(Expr(arm.Value));
                }
                return result;
            }
            case BoundCatch c:
            {
                var operand = Expr(c.Operand);
                if (c.Binding is not null) Declare(c.Binding, c.Syntax.Span.Start);
                return operand.Union(Expr(c.Handler));
            }
            case BoundLambda l:
            {
                // A lambda borrows what its captures borrow, and its captures live in this frame,
                // overwritten by each iteration of the loop that creates it.
                var captures = Borrow.None;
                foreach (var capture in l.Captures) captures = captures.Union(Expr(capture));
                if (l.Captures.Length == 0) return captures;
                var environment = new Region(Math.Max(1, _iteration), $"the lambda on line {Line(l.Syntax.Span)}");
                _environments.Add(environment);
                return captures.Union(new Borrow([environment], []));
            }
            default:
            {
                var result = Borrow.None;
                foreach (var child in BoundTree.Children(e))
                    if (child is BoundExpr ce) result = result.Union(Expr(ce));
                return result;
            }
        }
    }

    /// <summary>
    /// The builtins' store summaries (Q745): the argument each stores into its receiver. Every other builtin stores none:
    /// a <c>StrBuf</c> append or insert copies the text, <c>copy(to:)</c> copies into the arena, formatting copies.
    /// </summary>
    private static readonly Dictionary<BuiltinMember, int> BuiltinStores = new()
    {
        [BuiltinMember.ListAdd] = 0,
        [BuiltinMember.ListReplace] = 1,
        [BuiltinMember.MapIndexSet] = 0,
        [BuiltinMember.MapSlot] = 0,
    };

    private Borrow Builtin(BoundBuiltinCall b)
    {
        var receiver = Expr(b.Receiver);
        var args = b.Arguments.Select(Expr).ToArray();
        if (BuiltinStores.TryGetValue(b.Member, out var stored) && stored < args.Length) StoreInto(b.Receiver, args[stored], b.Arguments[stored].Syntax.Span);
        switch (b.Member)
        {
            // `finish()` points into the buffer's storage, which outlives the local: a loan on it, but not its scope.
            case BuiltinMember.StrBufAsString when b.Syntax is Syntax.Ast.CallExpr && RootLocal(b.Receiver) is { } buffer:
                return receiver.Lent(buffer, b.Syntax.Span.End);
            case BuiltinMember.ListAsSpan or BuiltinMember.ArrayAsSpan or BuiltinMember.StrBufAsString or BuiltinMember.ListMutableSpan or BuiltinMember.ArrayMutableSpan:
                return Lend(b.Receiver, receiver, b.Syntax.Span.End);
            case BuiltinMember.SpanCopy or BuiltinMember.StringCopy or BuiltinMember.ListCopy or BuiltinMember.MapCopy or BuiltinMember.StrBufCopy
                when b.Arguments.Length > 0:
                return AllocatedFrom(b.Arguments[0]);
            case BuiltinMember.IoReadAll or BuiltinMember.IoReadAllBytes or BuiltinMember.IntToString or BuiltinMember.StringCopy or BuiltinMember.StringLowercased or BuiltinMember.StringUppercased
                or BuiltinMember.StringReplaced or BuiltinMember.StringRepeated or BuiltinMember.StringJoined or BuiltinMember.Utf16Copy
                or BuiltinMember.StringPadded or BuiltinMember.BytesToString or BuiltinMember.SpanCopy or BuiltinMember.ListCopy:
                // Fresh text from `context.allocator`, like `Io.readText`.
                return _contexts.Count > 0 ? _contexts.Peek() : Borrow.None;
            case BuiltinMember.ListReplace or BuiltinMember.ListAdd:
                Mutate(b.Receiver, b.Syntax.Span);
                return Borrow.None;
            case BuiltinMember.MapIndexSet or BuiltinMember.MapSlot:
                Mutate(b.Receiver, b.Syntax.Span);
                return receiver with { Owners = [] };
            case BuiltinMember.ListTake or BuiltinMember.MapTake or BuiltinMember.StrBufTake or BuiltinMember.OptionalTake:
                Mutate(b.Receiver, b.Syntax.Span, moved: true);
                return receiver;
            case BuiltinMember.ListRemoveLast:
                Mutate(b.Receiver, b.Syntax.Span);
                return receiver with { Owners = [] };
            case BuiltinMember.MapIndex or BuiltinMember.MapTryGet or BuiltinMember.ListTryGet:
                return receiver with { Owners = [] };
            // An interned `Symbol`'s text lives until exit, like a literal.
            case BuiltinMember.MapLength or BuiltinMember.MapContains or BuiltinMember.SymbolText:
                return Borrow.None;
            case BuiltinMember.MapRemove or BuiltinMember.MapClear or BuiltinMember.ListRemoveRange or BuiltinMember.ListRemoveThrough or BuiltinMember.ListRemoveAt
                or BuiltinMember.ListRemoveAtRange or BuiltinMember.ListSort:
                Mutate(b.Receiver, b.Syntax.Span);
                return Borrow.None;
            case BuiltinMember.ListClear or BuiltinMember.StrBufClear or BuiltinMember.StrBufInsert or BuiltinMember.ArenaReset:
                Mutate(b.Receiver, b.Syntax.Span);
                return Borrow.None;
            default:
                return args.Aggregate(receiver, (acc, a) => acc.Union(a));
        }
    }

    private Borrow Call(BoundCall c)
    {
        var fn = c.Function;
        var receiver = c.Receiver is null ? Borrow.None : Expr(c.Receiver);
        var args = c.Arguments.Select(Expr).ToArray();
        // A method that writes `this` modifies its receiver, like a write to one of its fields (Q78).
        if (c.Receiver is not null && fn.IsMutating) Mutate(c.Receiver, c.Syntax.Span);
        // Through a writable `dyn ref` it modifies every owner the `dyn ref` borrows.
        if (c.Receiver is { Type: DynRefType { ReadOnly: false } } && fn.IsMutating) MutateOwners(c.Receiver, receiver, c.Syntax.Span);
        // A `ref this` call on `this` changes the fields its body changes structurally.
        if (c.Receiver is BoundThis && fn.IsMutating && _reporting)
            foreach (var field in ChangedFields(fn))
                _mutations.Add(new Mutation(FieldLocal(field), c.Syntax.Span));
        for (var i = 0; i < c.Arguments.Length && i < fn.Parameters.Count; i++)
        {
            // An existing writable `dyn ref` passed on lets the callee modify every owner it borrows.
            if (fn.Parameters[i].Type is DynRefType { ReadOnly: false } && c.Arguments[i].Type is DynRefType { ReadOnly: false }
                && c.Arguments[i] is not BoundConversion { Kind: ConversionKind.ToDynRef })
                MutateOwners(c.Arguments[i], args[i], c.Arguments[i].Syntax.Span);
            if (WrittenArgument(fn.Parameters[i], c.Arguments[i]) is not { } written) continue;
            // Writing an element through `ref xs[i]` does not reallocate `xs`; only what the callee can reach may change.
            if (MayBorrow(written.Type, [])) Mutate(written, c.Arguments[i].Syntax.Span);
            // `ref this` hands over the whole receiver: the callee may change any of its fields.
            if (written is BoundThis && MayBorrow(written.Type, []) && _reporting && _fn.Owner is not null)
                foreach (var field in _fn.Owner.Fields)
                    _mutations.Add(new Mutation(FieldLocal(field), c.Arguments[i].Syntax.Span));
            // Only a `ref` or `out` argument is passed by `ref` (BYN3005).
            if (written != c.Arguments[i] || RootLocal(written) is not { } root) continue;
            for (var j = 0; j < c.Arguments.Length; j++)
            {
                // A by-value copy of an element (`xs[0]` as an `i64`) cannot alias what the callee changes.
                var byRef = j < fn.Parameters.Count && fn.Parameters[j].Modifier is Syntax.Ast.ParamModifier.Ref or Syntax.Ast.ParamModifier.Out;
                if (j == i || !(RootLocal(c.Arguments[j]) == root && (byRef || MayBorrow(c.Arguments[j].Type, [])) || args[j].Owners.Contains(root))) continue;
                Report(DiagnosticCodes.RefAndValue, c.Arguments[j].Syntax.Span, [root.Name],
                    label: $"`{root.Name}` is also passed by `ref`",
                    help: "copy the value into a local first, or make two calls");
                break;
            }
        }
        // What the callee may store from one argument (or its receiver) into another, by its summary.
        foreach (var ((s, t), site) in Stores(fn, c))
        {
            var target = t == This ? fn.IsMutating ? c.Receiver : null
                : t < c.Arguments.Length && t < fn.Parameters.Count ? WrittenArgument(fn.Parameters[t], c.Arguments[t]) : null;
            if (target is null || !MayBorrow(target.Type, [])) continue;
            var source = s == This ? receiver : s < args.Length ? args[s] : Borrow.None;
            RelatedSpan? note = site is { } at && (Compilation.TemplateOf(fn) ?? fn) is { File: { } file } callee && file.Source == _source
                ? new RelatedSpan(at, $"`{callee.Name}` stores `{ParameterName(callee, s)}` into `{ParameterName(callee, t)}` here")
                : null;
            StoreInto(target, source, c.Syntax.Span, note);
        }
        // A callee with a body borrows what its return summary names; a builtin what its table entry names; any other, every argument (rule 4).
        // A function value's environment is not one, since no function type can be returned (BYN2822); what its captures borrow is (spec 007 R10).
        HashSet<int>? returned = fn.Builtin is not BuiltinFunction.None ? BuiltinResults.TryGetValue(fn.Builtin, out var named) ? named : null
            : IsBroad(fn) || (Compilation.TemplateOf(fn) ?? fn).Body is null ? null
            : _c.ReturnSummaries.GetValueOrDefault(Compilation.TemplateOf(fn) ?? fn) ?? [];
        // An arena argument the result may borrow lends its memory, as `copy(to:)` does.
        var result = args.Select((a, i) => i < fn.Parameters.Count && fn.Parameters[i].Type is FunctionType ? a with { Regions = a.Regions.Except(_environments) }
                : returned is not null && i < c.Arguments.Length && c.Arguments[i].Type is BuiltinType { Kind: BuiltinKind.Arena } ? AllocatedFrom(c.Arguments[i], a) : a)
            .Where((_, i) => returned is null || i >= fn.Parameters.Count || returned.Contains(i))
            .Aggregate(returned is null || returned.Contains(This) ? receiver : Borrow.None, (acc, a) => acc.Union(a));
        // A method returning a span or `dyn ref` may lend its own fields' storage: the result borrows the receiver.
        if (c.Receiver is not null && c.Type is SpanType or DynRefType) result = result.Union(Lend(c.Receiver, receiver, c.Syntax.Span.End));
        // So may a function returning one: the result lives no longer than any argument passed by `ref` (`in` is by value),
        // and borrows the storage of those with storage to lend.
        if (c.Type is SpanType or DynRefType)
            for (var i = 0; i < c.Arguments.Length && i < fn.Parameters.Count; i++)
                if (fn.Parameters[i].Modifier is Syntax.Ast.ParamModifier.Ref or Syntax.Ast.ParamModifier.Out)
                {
                    var lent = Lend(c.Arguments[i], args[i], c.Syntax.Span.End);
                    result = result.Union(MayLend(c.Arguments[i].Type, []) ? lent : new Borrow(lent.Regions, []));
                }
        // Allocated from `context.allocator`: a list, an `Owned`, a growable `StrBuf`, or the text `Io.readText`, `Io.run` and `Utf8.decode` return (Q29).
        if (_contexts.Count > 0 && (ContainsList(c.Type, []) || fn.Builtin is BuiltinFunction.IoReadText or BuiltinFunction.IoRun or BuiltinFunction.IoRunBatch or BuiltinFunction.StrBufGrow or BuiltinFunction.StrBufOver
                or BuiltinFunction.Utf8Decode or BuiltinFunction.Utf8TryDecode or BuiltinFunction.Utf8DecodeReplace or BuiltinFunction.HexEncode
                or BuiltinFunction.PathRelative or BuiltinFunction.PathFileName or BuiltinFunction.PathDirectoryName or BuiltinFunction.PathCombine or BuiltinFunction.PathCombineSpan
                or BuiltinFunction.PathFullPath or BuiltinFunction.EnvGet))
            result = result.Union(_contexts.Peek());
        return result;
    }

    /// <summary>
    /// The (source, target) pairs a call of <paramref name="fn"/> may store, in a fixed order, with where its body does so. A function value,
    /// an interface method or an <c>extern</c> function has no body to summarise: every argument and the receiver may go into every written one.
    /// </summary>
    private IEnumerable<((int Source, int Target) Pair, TextSpan? Site)> Stores(FunctionSymbol fn, BoundCall c)
    {
        if (IsBroad(fn))
        {
            var targets = Enumerable.Range(0, Math.Min(c.Arguments.Length, fn.Parameters.Count))
                .Where(i => WrittenArgument(fn.Parameters[i], c.Arguments[i]) is not null).ToList();
            if (fn.IsMutating && c.Receiver is not null) targets.Add(This);
            var sources = Enumerable.Range(0, c.Arguments.Length).Append(This).ToList();
            return targets.SelectMany(t => sources.Where(s => s != t).Select(s => ((s, t), (TextSpan?)null)));
        }
        return _c.StoreSummaries.TryGetValue(Compilation.TemplateOf(fn) ?? fn, out var pairs)
            ? pairs.OrderBy(p => p.Key.Target).ThenBy(p => p.Key.Source).Select(p => (p.Key, (TextSpan?)p.Value)).ToList()
            : [];
    }

    /// <summary>A function value, an interface method or an <c>extern</c> function: no body to summarise.</summary>
    private static bool IsBroad(FunctionSymbol fn) =>
        fn.Kind is FunctionKind.Invoke || fn.IsAbstract || fn.Decl?.Modifiers.HasFlag(Syntax.Ast.Modifiers.Extern) == true;

    /// <summary>
    /// The builtin functions' return summaries: the arguments each result borrows. <c>Io</c> results and decoded or encoded text are fresh copies,
    /// and an interned <c>Symbol</c> lives until exit, so they borrow none. Every other builtin's result borrows every argument (rule 4).
    /// </summary>
    private static readonly Dictionary<BuiltinFunction, HashSet<int>> BuiltinResults = new()
    {
        [BuiltinFunction.IoReadFile] = [],
        [BuiltinFunction.IoReadText] = [],
        [BuiltinFunction.IoListDir] = [],
        [BuiltinFunction.IoRun] = [],
        [BuiltinFunction.IoRunBatch] = [],
        [BuiltinFunction.Utf8Decode] = [],
        [BuiltinFunction.Utf8TryDecode] = [],
        [BuiltinFunction.Utf8DecodeReplace] = [],
        [BuiltinFunction.StringInvalidUtf8At] = [],
        [BuiltinFunction.HexEncode] = [],
        [BuiltinFunction.HexDecode] = [],
        [BuiltinFunction.SymbolIntern] = [],
        [BuiltinFunction.PathRelative] = [],
        [BuiltinFunction.PathFileName] = [],
        [BuiltinFunction.PathDirectoryName] = [],
        [BuiltinFunction.PathCombine] = [],
        [BuiltinFunction.PathCombineSpan] = [],
        [BuiltinFunction.PathFullPath] = [],
        [BuiltinFunction.EnvGet] = [],
    };

    private static string ParameterName(FunctionSymbol fn, int index) => index == This ? "this" : fn.Parameters[index].Name;

    /// <summary>A borrow of <paramref name="source"/>'s own storage (a span of an array or list, a <c>StrBuf</c>'s text), taken at <paramref name="at"/>.</summary>
    private Borrow Lend(BoundExpr source, Borrow sourceBorrow, int at)
    {
        if (source is BoundStaticArray) return sourceBorrow;
        // Inside a method returning a span or `dyn ref`, its own fields are the caller's data; the call site lends the receiver (Q75).
        // Either way it is a loan on that field: changing it structurally invalidates the borrow.
        if (ThisField(source) is { } field) sourceBorrow = sourceBorrow.Lent(field, at);
        if (_fn.ReturnType is SpanType or DynRefType && IsThisRooted(source)) return sourceBorrow.Union(new Borrow([new Region(0, "the caller's data")], []));
        // In any other method they outlive the whole body, but cannot be returned.
        if (IsThisRooted(source)) return sourceBorrow.Union(new Borrow([new Region(1, $"the temporary on line {Line(source.Syntax.Span)}")], []));
        if (RootLocal(source) is { } root)
        {
            // A list in a loop item is the iterable's: lend what the iterable lends (a span already names where it points).
            if (source.Type is ListType && _loopItems.TryGetValue(root, out var loop))
                return loop.Iterable.Type is SpanType ? sourceBorrow.Union(loop.Borrow) : Lend(loop.Iterable, sourceBorrow.Union(loop.Borrow), at);
            var depth = _depthOf.GetValueOrDefault(root, 0);
            var regions = depth > 0 ? sourceBorrow.Regions.Add(new Region(depth, $"`{root.Name}`")) : sourceBorrow.Regions;
            return (sourceBorrow with { Regions = regions }).Lent(root, at);
        }
        return sourceBorrow.Union(new Borrow([new Region(_depth, $"the temporary on line {Line(source.Syntax.Span)}")], []));
    }

    /// <summary>Memory from the arena (or other allocator) <paramref name="allocator"/>.</summary>
    private Borrow AllocatedFrom(BoundExpr allocator) => AllocatedFrom(allocator, Expr(allocator));

    private Borrow AllocatedFrom(BoundExpr allocator, Borrow borrow)
    {
        if (RootLocal(allocator) is not { } root) return borrow;
        var depth = _depthOf.GetValueOrDefault(root, 0);
        var regions = depth > 0 ? borrow.Regions.Add(new Region(depth, $"arena `{root.Name}`")) : borrow.Regions;
        // In a summary, memory from an allocator parameter borrows that parameter.
        if (_summary && root is ParameterSymbol p && _fn.Parameters.IndexOf(p) is >= 0 and var index) regions = regions.Add(new Region(0, "the caller's data", index));
        return new Borrow(regions, borrow.Owners.Add(root));
    }

    private void BindPattern(BoundPattern pattern, Borrow value)
    {
        foreach (var node in BoundTree.Descendants(pattern))
        {
            var local = node switch
            {
                BoundBindPattern b => b.Local,
                BoundPropertyPattern p => p.Designation,
                BoundUnionPattern u => u.Designation,
                _ => null,
            };
            if (local is null) continue;
            Declare(local, node.Syntax.Span.Start);
            Store(local, value, node.Syntax.Span);
        }
    }

    // ── Stores and escapes ───────────────────────────────────────────────────────

    private void Store(LocalSymbol local, Borrow value, TextSpan span, RelatedSpan? note = null)
    {
        if (value.IsEmpty) return;
        _locals[local] = _locals.GetValueOrDefault(local, Borrow.None).Union(value);
        if (_summary && _targets.TryGetValue(local, out var target)) Record(target, value, span);
        if (!_reporting) return;
        // The loan starts where the stored value's borrow of the owner was lent (`first(w.items.asSpan(), w.grow())`
        // conflicts), or once the value is stored: a mutating call that produces it (`b.pick()` on `ref this`) is not a conflict.
        foreach (var owner in value.Owners)
        {
            if (owner == local) continue;
            var created = span.End;
            if (value.Lends is { } lends && lends.TryGetValue(owner, out var at) && span.Start <= at && at < created) created = at;
            _loans.Add(new Loan(owner, local, created, 0));
        }
        CheckOutlives(value, _depthOf.GetValueOrDefault(local, 0), local.Name, span, note);
    }

    /// <summary>Adds a pair to the summary for each parameter (or <c>this</c>) whose data <paramref name="value"/> may hold.</summary>
    private void Record(int target, Borrow value, TextSpan span)
    {
        foreach (var region in value.Regions)
            if (region.Source != NoSource && region.Source != target) _pairs.TryAdd((region.Source, target), span);
    }

    /// <summary>A store into a local or a part of one; through <c>this</c> or a <c>ref</c> parameter it outlives the function.</summary>
    private void StoreInto(BoundExpr target, Borrow value, TextSpan span, RelatedSpan? note = null)
    {
        if (StoreRoot(target) is { } root)
        {
            Store(root, value, span, note);
            return;
        }
        if (_summary && _fn.IsMutating && IsThisRooted(target)) Record(This, value, span);
        if (!_reporting) return;
        // A store through a span lands where the span points: it must live as long as the
        // longest-lived storage the span may point into (the caller's, when that is unknown).
        if (target is BoundIndex { Target.Type: SpanType } i && RootLocal(i.Target) is { } view)
        {
            var sources = _locals.GetValueOrDefault(view, Borrow.None).Regions;
            CheckOutlives(value, sources.IsEmpty ? 0 : sources.Min(r => r.Depth), view.Name, span, note);
            return;
        }
        CheckOutlives(value, 0, "this", span, note);
    }

    private void CheckOutlives(Borrow value, int depth, string holder, TextSpan span, RelatedSpan? note = null)
    {
        foreach (var region in Ordered(value.Regions))
        {
            if (region.Depth <= depth) continue;
            if (RestEscapes(region, span)) return;
            Report(DiagnosticCodes.BorrowOutlivesSource, span, [region.Name, holder],
                label: $"{region.Name} is freed at the end of its block",
                help: "copy the data with `.copy(to: alloc)` into an allocator that lives long enough", note: note);
            return;
        }
    }

    private void CheckReturn(Borrow value, TextSpan span)
    {
        if (_summary)
            foreach (var region in value.Regions)
                if (region.Source != NoSource) _returns.Add(region.Source);
        if (!_reporting) return;
        foreach (var region in Ordered(value.Regions))
        {
            if (region.Depth <= 0) continue;
            if (RestEscapes(region, span)) return;
            Report(DiagnosticCodes.BorrowReturned, span, [region.Name, _fn.Name],
                help: "let the caller pass the buffer or allocator in, and borrow or allocate from that");
            return;
        }
    }

    private bool RestEscapes(Region region, TextSpan span)
    {
        if (!_restTails.TryGetValue(region, out var name)) return false;
        Report(DiagnosticCodes.RestEscapes, span, [name], help: $"copy it first: `{name}.copy()`");
        return true;
    }

    // ── Structural modification while borrowed ───────────────────────────────────

    private void Mutate(BoundExpr target, TextSpan span, bool moved = false)
    {
        if (!_reporting) return;
        if (RootLocal(target) is { } root) _mutations.Add(new Mutation(root, span, moved));
        else if (ThisField(target) is { } field) _mutations.Add(new Mutation(field, span, moved));
    }

    private void MutateOwners(BoundExpr through, Borrow borrow, TextSpan span)
    {
        // As for a `ref` argument: a write through it reallocates nothing unless the value it points to may hold such storage.
        if (!_reporting || through is BoundLocal l && !_referentMayBorrow.GetValueOrDefault(l.Local, true)) return;
        foreach (var owner in borrow.Owners) _mutations.Add(new Mutation(owner, span, Through: (through as BoundLocal)?.Local));
    }

    private void Refer(LocalSymbol local, BoundExpr value)
    {
        if (local.Type is not DynRefType) return;
        var mayBorrow = value switch
        {
            BoundConversion { Kind: ConversionKind.ToDynRef } c => MayBorrow(c.Operand.Type, []),
            BoundLocal l => _referentMayBorrow.GetValueOrDefault(l.Local, true),
            _ => true,
        };
        _referentMayBorrow[local] = _referentMayBorrow.GetValueOrDefault(local) || mayBorrow;
    }

    private void CheckMutations()
    {
        if (_mutations.Count == 0 || _loans.Count == 0) return;
        var uses = new Dictionary<LocalSymbol, List<int>>();
        var loops = new List<TextSpan>();
        foreach (var node in BoundTree.Descendants(_fn.Body!))
        {
            if (node is BoundLocal l)
            {
                if (!uses.TryGetValue(l.Local, out var list)) uses[l.Local] = list = new List<int>();
                list.Add(l.Syntax.Span.Start);
            }
            else if (node is BoundWhile or BoundDoWhile or BoundFor)
            {
                loops.Add(node.Syntax.Span);
            }
        }
        int LastUse(LocalSymbol holder)
        {
            var declared = _declaredAt.GetValueOrDefault(holder, -1);
            var end = -1;
            foreach (var use in uses.GetValueOrDefault(holder, []))
            {
                end = Math.Max(end, use);
                foreach (var loop in loops)
                    if (loop.Start <= use && use < loop.End && !(loop.Start <= declared && declared < loop.End))
                        end = Math.Max(end, loop.End);
            }
            return end;
        }
        foreach (var m in _mutations)
        {
            foreach (var loan in _loans)
            {
                if (loan.Owner != m.Target || loan.Created >= m.Span.End) continue;
                // Writing an element in place through the loop's own `dyn ref` variable leaves the loop's collection as it is.
                if (loan.Holder is null && m.Through is { } through && _declaredAt.GetValueOrDefault(through, -1) == loan.Created) continue;
                var end = loan.Holder is null ? loan.LoopEnd : LastUse(loan.Holder);
                if (end <= m.Span.End) continue;
                var who = loan.Holder is null ? "the `for` loop" : $"`{loan.Holder.Name}`";
                var label = loan.Holder is null
                    ? $"the loop over `{m.Target.Name}` is still running"
                    : $"`{loan.Holder.Name}` is used again on line {Line(new TextSpan(end, 0))}";
                var help = m.Moved ? $"finish using {who} first, or copy what it needs before moving `{m.Target.Name}`"
                    : loan.Holder is null
                    ? $"collect the changes in another list and apply them to `{m.Target.Name}` after the loop"
                    : $"finish using {who} first, or copy what it needs before changing `{m.Target.Name}`";
                Report(m.Moved ? DiagnosticCodes.MovedWhileBorrowed : DiagnosticCodes.ModifiedWhileBorrowed, m.Span, [m.Target.Name, who], label: label, help: help);
                break;
            }
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────

    /// <summary>The local whose storage <paramref name="e"/> names (<c>x</c>, <c>x.f</c>, <c>x[i]</c> of an array or list, <c>x.item1</c>), or null.</summary>
    private static LocalSymbol? RootLocal(BoundExpr e) => e switch
    {
        BoundLocal l => l.Local,
        BoundField f => RootLocal(f.Receiver),
        BoundTupleElement t => RootLocal(t.Receiver),
        BoundIndex i when i.Target.Type is ArrayType or ListType => RootLocal(i.Target),
        _ => null,
    };

    /// <summary>Whether <paramref name="e"/> names storage of <c>this</c> (<c>this</c>, <c>f</c>, <c>f[i]</c> of an array or list field, <c>f[k]</c> or <c>f.slot(k)</c> of a map field).</summary>
    private static bool IsThisRooted(BoundExpr e) => e switch
    {
        BoundThis => true,
        BoundField f => IsThisRooted(f.Receiver),
        BoundTupleElement t => IsThisRooted(t.Receiver),
        BoundIndex i when i.Target.Type is ArrayType or ListType => IsThisRooted(i.Target),
        BoundBuiltinCall { Member: BuiltinMember.MapIndexSet or BuiltinMember.MapSlot } b => IsThisRooted(b.Receiver),
        _ => false,
    };

    /// <summary>The stand-in for the field of <c>this</c> whose storage <paramref name="e"/> names (<c>f</c>, <c>f.g</c>, <c>f[i]</c>), or null.</summary>
    private LocalSymbol? ThisField(BoundExpr e) => ThisFieldSymbol(e) is { } field ? FieldLocal(field) : null;

    private LocalSymbol FieldLocal(FieldSymbol field) =>
        _thisFields.TryGetValue(field, out var local) ? local : _thisFields[field] = new LocalSymbol(field.Name, null, field.Type, LocalKind.Parameter);

    private static FieldSymbol? ThisFieldSymbol(BoundExpr e) => e switch
    {
        BoundField { Receiver: BoundThis } f => f.Field,
        BoundField f => ThisFieldSymbol(f.Receiver),
        BoundTupleElement t => ThisFieldSymbol(t.Receiver),
        BoundIndex i when i.Target.Type is ArrayType or ListType => ThisFieldSymbol(i.Target),
        _ => null,
    };

    /// <summary>
    /// The fields of <c>this</c> that the <c>ref this</c> method <paramref name="fn"/> changes structurally, as <see cref="Mutate"/>
    /// sees it: a growing or shrinking builtin, a <c>ref this</c> call or a <c>ref</c> argument on one, a <c>ref this</c> call on <c>this</c>,
    /// or <c>this</c> itself passed by <c>ref</c>. Computed once per method; methods that call each other on <c>this</c> share one set.
    /// </summary>
    private HashSet<FieldSymbol> ChangedFields(FunctionSymbol fn)
    {
        if (_c.ChangedFields.TryGetValue(fn, out var known)) return known;
        // Tarjan's strongly connected components over `ref this` calls on `this`.
        var index = new Dictionary<FunctionSymbol, int>();
        var low = new Dictionary<FunctionSymbol, int>();
        var own = new Dictionary<FunctionSymbol, HashSet<FieldSymbol>>();
        var stack = new Stack<FunctionSymbol>();
        void Visit(FunctionSymbol f)
        {
            index[f] = low[f] = index.Count;
            stack.Push(f);
            var (fields, callees) = DirectChanges(f);
            own[f] = fields;
            foreach (var callee in callees)
            {
                if (_c.ChangedFields.TryGetValue(callee, out var done))
                {
                    fields.UnionWith(done);
                    continue;
                }
                if (!index.ContainsKey(callee))
                {
                    Visit(callee);
                    low[f] = Math.Min(low[f], low[callee]);
                    if (_c.ChangedFields.TryGetValue(callee, out done)) fields.UnionWith(done);
                }
                else if (stack.Contains(callee))
                {
                    low[f] = Math.Min(low[f], index[callee]);
                }
            }
            if (low[f] != index[f]) return;
            var component = new List<FunctionSymbol>();
            var all = new HashSet<FieldSymbol>();
            FunctionSymbol member;
            do
            {
                member = stack.Pop();
                component.Add(member);
                all.UnionWith(own[member]);
            } while (member != f);
            foreach (var m in component) _c.ChangedFields[m] = all;
        }
        Visit(fn);
        return _c.ChangedFields[fn];
    }

    /// <summary>The fields <paramref name="fn"/>'s own body changes, and the <c>ref this</c> methods it calls on <c>this</c>.</summary>
    private static (HashSet<FieldSymbol> Fields, List<FunctionSymbol> Callees) DirectChanges(FunctionSymbol fn)
    {
        var fields = new HashSet<FieldSymbol>();
        var callees = new List<FunctionSymbol>();
        if (fn.Body is null)
        {
            // Unknown: it may change any of them.
            if (fn.Owner is not null) fields.UnionWith(fn.Owner.Fields);
            return (fields, callees);
        }
        foreach (var node in BoundTree.Descendants(fn.Body))
        {
            switch (node)
            {
                case BoundBuiltinCall b when b.Member is BuiltinMember.ListAdd or BuiltinMember.MapIndexSet or BuiltinMember.MapSlot or BuiltinMember.ListRemoveLast
                        or BuiltinMember.MapRemove or BuiltinMember.MapClear or BuiltinMember.ListRemoveRange or BuiltinMember.ListRemoveThrough or BuiltinMember.ListRemoveAt or BuiltinMember.ListRemoveAtRange
                        or BuiltinMember.ListReplace or BuiltinMember.ListSort or BuiltinMember.ListClear or BuiltinMember.StrBufClear or BuiltinMember.StrBufInsert or BuiltinMember.ArenaReset
                        or BuiltinMember.ListTake or BuiltinMember.MapTake or BuiltinMember.StrBufTake or BuiltinMember.OptionalTake
                    && ThisFieldSymbol(b.Receiver) is { } field:
                    fields.Add(field);
                    break;
                case BoundCall c:
                    if (c.Function.IsMutating && c.Receiver is BoundThis) callees.Add(c.Function);
                    else if (c.Function.IsMutating && c.Receiver is not null && ThisFieldSymbol(c.Receiver) is { } receiver) fields.Add(receiver);
                    for (var i = 0; i < c.Arguments.Length && i < c.Function.Parameters.Count; i++)
                    {
                        if (WrittenArgument(c.Function.Parameters[i], c.Arguments[i]) is not { } written || !MayBorrow(written.Type, [])) continue;
                        if (ThisFieldSymbol(written) is { } argument) fields.Add(argument);
                        else if (written is BoundThis && fn.Owner is not null) fields.UnionWith(fn.Owner.Fields);
                    }
                    break;
            }
        }
        return (fields, callees);
    }

    /// <summary>The place the callee may write through <paramref name="parameter"/>: a <c>ref</c> or <c>out</c> argument, or the value a writable <c>dyn ref</c> parameter borrows.</summary>
    private static BoundExpr? WrittenArgument(ParameterSymbol parameter, BoundExpr argument) => parameter switch
    {
        { Modifier: Syntax.Ast.ParamModifier.Ref or Syntax.Ast.ParamModifier.Out } => argument,
        { Type: DynRefType { ReadOnly: false } } when argument is BoundConversion { Kind: ConversionKind.ToDynRef } c => c.Operand,
        _ => null,
    };

    /// <summary>The local a store into <paramref name="e"/> lands in, including through a span or list element.</summary>
    private static LocalSymbol? StoreRoot(BoundExpr e) => e switch
    {
        BoundLocal l => l.Local,
        BoundField f => StoreRoot(f.Receiver),
        BoundTupleElement t => StoreRoot(t.Receiver),
        // A span's elements live wherever it points (often the caller's frame): not in the span local.
        BoundIndex { Target.Type: SpanType } => null,
        BoundIndex i => StoreRoot(i.Target),
        BoundBuiltinCall { Member: BuiltinMember.MapIndexSet or BuiltinMember.MapSlot } b => StoreRoot(b.Receiver),
        _ => null,
    };

    /// <summary>Whether a value of type <paramref name="t"/> can point into memory it doesn't own.</summary>
    private static bool MayBorrow(TypeSymbol t, HashSet<TypeSymbol> seen) => t switch
    {
        PrimitiveType p => p.IsString,
        SpanType or ListType or MapType or OwnedType or DynRefType or TextViewType or FunctionType => true,
        BuiltinType b => b.Kind is BuiltinKind.StrBuf or BuiltinKind.ProcessResult,
        UntrustedType u => MayBorrow(u.Inner, seen),
        OptionalType o => MayBorrow(o.Inner, seen),
        ResultType r => MayBorrow(r.Value, seen),
        ArrayType a => MayBorrow(a.Element, seen),
        TupleType tt => tt.Fields.Any(f => MayBorrow(f.Type, seen)),
        StructSymbol s => seen.Add(s) && s.Fields.Any(f => MayBorrow(f.Type, seen)),
        UnionSymbol u => seen.Add(u) && u.Variants.Any(v => v.Fields.Any(f => MayBorrow(f.Type, seen))),
        _ => false,
    };

    /// <summary>Whether a <c>ref</c> to a <paramref name="t"/> reaches storage a span or <c>dyn ref</c> can point into.</summary>
    private static bool MayLend(TypeSymbol t, HashSet<TypeSymbol> seen) => t switch
    {
        ArrayType or TypeParameterType => true,
        _ when TypeFacts.IsUnique(t) => true,
        OptionalType o => MayLend(o.Inner, seen),
        TupleType tt => tt.Fields.Any(f => MayLend(f.Type, seen)),
        StructSymbol s => seen.Add(s) && s.Fields.Any(f => MayLend(f.Type, seen)),
        UnionSymbol u => seen.Add(u) && u.Variants.Any(v => v.Fields.Any(f => MayLend(f.Type, seen))),
        _ => MayBorrow(t, seen),
    };

    private static bool ContainsList(TypeSymbol t, HashSet<TypeSymbol> seen) => t switch
    {
        ListType or MapType or OwnedType => true,
        UntrustedType u => ContainsList(u.Inner, seen),
        OptionalType o => ContainsList(o.Inner, seen),
        ResultType r => ContainsList(r.Value, seen),
        ArrayType a => ContainsList(a.Element, seen),
        TupleType tt => tt.Fields.Any(f => ContainsList(f.Type, seen)),
        StructSymbol s => seen.Add(s) && s.Fields.Any(f => ContainsList(f.Type, seen)),
        UnionSymbol u => seen.Add(u) && u.Variants.Any(v => v.Fields.Any(f => ContainsList(f.Type, seen))),
        _ => false,
    };

    /// <summary>Shortest-lived first, so diagnostics name the same region on every run.</summary>
    private static IEnumerable<Region> Ordered(ImmutableHashSet<Region> regions) =>
        regions.OrderByDescending(r => r.Depth).ThenBy(r => r.Name, StringComparer.Ordinal);

    private int Line(TextSpan span) => _source.GetLinePosition(span.Start).Line;

    private void Report(DiagnosticDescriptor d, TextSpan span, object?[] args, string? label = null, string? help = null, RelatedSpan? note = null)
    {
        if (!_reporting || _summary || !_reported.Add((span.Start, span.End))) return;
        if (note is null) _c.Report(d, _source, span, args, label, help);
        else _c.ReportHeld([new Diagnostic(d.Severity, d.Code, _source, span, d.Format(args), label, help, Related: [note])]);
    }
}
