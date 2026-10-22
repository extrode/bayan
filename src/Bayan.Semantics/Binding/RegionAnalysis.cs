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
    /// <summary>Storage freed when the block at <see cref="Depth"/> (1 = the function body) ends.</summary>
    private sealed record Region(int Depth, string Name);

    /// <summary>
    /// What a value points into: <see cref="Regions"/> bound its lifetime; <see cref="Owners"/>
    /// are locals whose storage it borrows directly, which must not change structurally.
    /// </summary>
    private sealed record Borrow(ImmutableHashSet<Region> Regions, ImmutableHashSet<LocalSymbol> Owners)
    {
        public static readonly Borrow None = new([], []);

        public bool IsEmpty => Regions.IsEmpty && Owners.IsEmpty;

        public Borrow Union(Borrow other) =>
            other.IsEmpty ? this : IsEmpty ? other : new(Regions.Union(other.Regions), Owners.Union(other.Owners));
    }

    private sealed record Loan(LocalSymbol Owner, LocalSymbol? Holder, int Created, int LoopEnd);

    private sealed record Mutation(LocalSymbol Target, TextSpan Span);

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
    private readonly Stack<Borrow> _contexts = new();
    private readonly HashSet<(int, int)> _reported = new();
    private int _depth;
    private bool _reporting;

    private RegionAnalysis(Compilation c, FunctionSymbol fn)
    {
        _c = c;
        _fn = fn;
        _source = fn.File!.Source;
    }

    public static void Analyze(Compilation c, FunctionSymbol fn)
    {
        if (fn.File is null || fn.Body is null) return;
        new RegionAnalysis(c, fn).Run();
    }

    private void Run()
    {
        foreach (var p in _fn.Parameters)
        {
            // An arena parameter is a handle to the caller's arena: what it allocates outlives the call.
            var callerOwned = p.Modifier is Syntax.Ast.ParamModifier.Ref or Syntax.Ast.ParamModifier.Out || p.Type == BuiltinType.Arena;
            _depthOf[p] = callerOwned ? 0 : 1;
            _declaredAt[p] = -1;
            // What a parameter points to belongs to the caller; the marker keeps that known after unions.
            if (MayBorrow(p.Type, [])) _locals[p] = new Borrow([new Region(0, "the caller's data")], []);
        }
        // The first pass collects what each local may borrow (a loop can store into a local
        // after its first use); the second reports against the complete picture.
        Stmt(_fn.Body!);
        _reporting = true;
        Stmt(_fn.Body!);
        CheckMutations();
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
                    Store(d.Local, Expr(d.Initializer), d.Initializer.Syntax.Span);
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
                Stmt(w.Body);
                break;
            case BoundDoWhile w:
                Stmt(w.Body);
                Expr(w.Condition);
                break;
            case BoundFor f:
            {
                var iterable = Expr(f.Iterable);
                // The loop reads the collection on every iteration: it holds a loan on it.
                if (_reporting)
                {
                    var owners = iterable.Owners;
                    if (RootLocal(f.Iterable) is { } root && (f.Iterable.Type is ListType or MapType or ArrayType || f.UserIteration is not null)) owners = owners.Add(root);
                    foreach (var owner in owners) _loans.Add(new Loan(owner, null, f.Syntax.Span.Start, f.Syntax.Span.End));
                }
                _depth++;
                Declare(f.Variable, f.Syntax.Span.Start);
                // A `dyn ref` variable points into an array or list, like `dyn ref I d = xs[i]`; any other is a copy.
                var item = f.Variable.Type is DynRefType && f.Iterable.Type is ListType or ArrayType ? Lend(f.Iterable, iterable) : iterable with { Owners = [] };
                Store(f.Variable, item, f.Iterable.Syntax.Span);
                Stmt(f.Body);
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

    private void Declare(LocalSymbol local, int position)
    {
        _depthOf[local] = _depth;
        _declaredAt[local] = position;
    }

    // ── Expressions ──────────────────────────────────────────────────────────────

    private Borrow Expr(BoundExpr e)
    {
        var result = Compute(e);
        return MayBorrow(e.Type, []) ? result : Borrow.None;
    }

    private Borrow Compute(BoundExpr e)
    {
        switch (e)
        {
            case BoundLocal l:
                return _locals.GetValueOrDefault(l.Local, Borrow.None);
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
                return c.Kind is ConversionKind.ArrayToSpan or ConversionKind.ListToSpan or ConversionKind.ToDynRef ? Lend(c.Operand, operand) : operand;
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
                return s.Target.Type is ListType or ArrayType ? Lend(s.Target, target) : target;
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
            default:
            {
                var result = Borrow.None;
                foreach (var child in BoundTree.Children(e))
                    if (child is BoundExpr ce) result = result.Union(Expr(ce));
                return result;
            }
        }
    }

    private Borrow Builtin(BoundBuiltinCall b)
    {
        var receiver = Expr(b.Receiver);
        var args = b.Arguments.Select(Expr).ToArray();
        switch (b.Member)
        {
            case BuiltinMember.ListAsSpan or BuiltinMember.ArrayAsSpan or BuiltinMember.StrBufAsString:
                return Lend(b.Receiver, receiver);
            case BuiltinMember.SpanCopy or BuiltinMember.StringCopy or BuiltinMember.ListCopy or BuiltinMember.MapCopy or BuiltinMember.StrBufCopy:
                return AllocatedFrom(b.Arguments[0]);
            case BuiltinMember.IoReadAll or BuiltinMember.IntToHex:
                // Fresh text from `context.allocator`, like `Io.readText`.
                return _contexts.Count > 0 ? _contexts.Peek() : Borrow.None;
            case BuiltinMember.ListAdd:
                StoreInto(b.Receiver, args[0], b.Arguments[0].Syntax.Span);
                Mutate(b.Receiver, b.Syntax.Span);
                return Borrow.None;
            case BuiltinMember.MapIndexSet or BuiltinMember.MapSlot:
                StoreInto(b.Receiver, args[0], b.Arguments[0].Syntax.Span);
                Mutate(b.Receiver, b.Syntax.Span);
                return receiver with { Owners = [] };
            case BuiltinMember.ListRemoveLast:
                Mutate(b.Receiver, b.Syntax.Span);
                return receiver with { Owners = [] };
            case BuiltinMember.MapIndex:
                return receiver with { Owners = [] };
            case BuiltinMember.MapGetOr:
                return (receiver with { Owners = [] }).Union(args[1]);
            case BuiltinMember.MapLength or BuiltinMember.MapContains:
                return Borrow.None;
            case BuiltinMember.MapRemove or BuiltinMember.MapClear or BuiltinMember.ListRemoveRange:
                Mutate(b.Receiver, b.Syntax.Span);
                return Borrow.None;
            case BuiltinMember.ListClear or BuiltinMember.StrBufClear or BuiltinMember.ArenaReset:
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
        for (var i = 0; i < c.Arguments.Length && i < fn.Parameters.Count; i++)
        {
            if (fn.Parameters[i].Modifier is not (Syntax.Ast.ParamModifier.Ref or Syntax.Ast.ParamModifier.Out)) continue;
            if (RootLocal(c.Arguments[i]) is not { } root) continue;
            // Writing an element through `ref xs[i]` does not reallocate `xs`; only what the callee can reach may change.
            if (MayBorrow(c.Arguments[i].Type, [])) Mutate(c.Arguments[i], c.Arguments[i].Syntax.Span);
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
        // Rule 4: a returned borrow derives from every argument. `Io` and decoded `string` results are fresh copies and borrow none.
        var result = fn.Builtin is BuiltinFunction.IoReadFile or BuiltinFunction.IoReadText or BuiltinFunction.IoListDir or BuiltinFunction.IoRun or BuiltinFunction.IoRunBatch
            or BuiltinFunction.StringFrom or BuiltinFunction.StringTryFrom or BuiltinFunction.StringInvalidUtf8At
            ? Borrow.None
            : args.Aggregate(receiver, (acc, a) => acc.Union(a));
        // A method returning a span or `dyn ref` may lend its own fields' storage: the result borrows the receiver.
        if (c.Receiver is not null && c.Type is SpanType or DynRefType) result = result.Union(Lend(c.Receiver, receiver));
        // Allocated from `context.allocator`: a list, an `Owned`, a growable `StrBuf`, or the text `Io.readText`, `Io.run` and `string.from` return (Q29).
        if (_contexts.Count > 0 && (ContainsList(c.Type, []) || fn.Builtin is BuiltinFunction.IoReadText or BuiltinFunction.IoRun or BuiltinFunction.IoRunBatch or BuiltinFunction.StrBufGrow
                or BuiltinFunction.StringFrom or BuiltinFunction.StringTryFrom))
            result = result.Union(_contexts.Peek());
        return result;
    }

    /// <summary>A borrow of <paramref name="source"/>'s own storage (a span of an array or list, a <c>StrBuf</c>'s text).</summary>
    private Borrow Lend(BoundExpr source, Borrow sourceBorrow)
    {
        if (source is BoundStaticArray) return sourceBorrow;
        // Inside a method returning a span or `dyn ref`, its own fields are the caller's data; the call site lends the receiver (Q75).
        // Either way it is a loan on that field: changing it structurally invalidates the borrow.
        if (ThisField(source) is { } field) sourceBorrow = sourceBorrow.Union(new Borrow([], [field]));
        if (_fn.ReturnType is SpanType or DynRefType && IsThisRooted(source)) return sourceBorrow.Union(new Borrow([new Region(0, "the caller's data")], []));
        if (RootLocal(source) is { } root)
        {
            var depth = _depthOf.GetValueOrDefault(root, 0);
            var regions = depth > 0 ? sourceBorrow.Regions.Add(new Region(depth, $"`{root.Name}`")) : sourceBorrow.Regions;
            return new Borrow(regions, sourceBorrow.Owners.Add(root));
        }
        return sourceBorrow.Union(new Borrow([new Region(_depth, $"the temporary on line {Line(source.Syntax.Span)}")], []));
    }

    /// <summary>Memory from the arena (or other allocator) <paramref name="allocator"/>.</summary>
    private Borrow AllocatedFrom(BoundExpr allocator)
    {
        var borrow = Expr(allocator);
        if (RootLocal(allocator) is not { } root) return borrow;
        var depth = _depthOf.GetValueOrDefault(root, 0);
        var regions = depth > 0 ? borrow.Regions.Add(new Region(depth, $"arena `{root.Name}`")) : borrow.Regions;
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
                _ => null,
            };
            if (local is null) continue;
            Declare(local, node.Syntax.Span.Start);
            Store(local, value, node.Syntax.Span);
        }
    }

    // ── Stores and escapes ───────────────────────────────────────────────────────

    private void Store(LocalSymbol local, Borrow value, TextSpan span)
    {
        if (value.IsEmpty) return;
        _locals[local] = _locals.GetValueOrDefault(local, Borrow.None).Union(value);
        if (!_reporting) return;
        // The loan starts once the value is stored: a mutating call that produces it (`b.pick()` on `ref this`) is not a conflict.
        foreach (var owner in value.Owners)
            if (owner != local) _loans.Add(new Loan(owner, local, span.End, 0));
        CheckOutlives(value, _depthOf.GetValueOrDefault(local, 0), local.Name, span);
    }

    /// <summary>A store into a local or a part of one; through <c>this</c> or a <c>ref</c> parameter it outlives the function.</summary>
    private void StoreInto(BoundExpr target, Borrow value, TextSpan span)
    {
        if (StoreRoot(target) is { } root)
        {
            Store(root, value, span);
            return;
        }
        if (!_reporting) return;
        // A store through a span lands where the span points: it must live as long as the
        // longest-lived storage the span may point into (the caller's, when that is unknown).
        if (target is BoundIndex { Target.Type: SpanType } i && RootLocal(i.Target) is { } view)
        {
            var sources = _locals.GetValueOrDefault(view, Borrow.None).Regions;
            CheckOutlives(value, sources.IsEmpty ? 0 : sources.Min(r => r.Depth), view.Name, span);
            return;
        }
        CheckOutlives(value, 0, "this", span);
    }

    private void CheckOutlives(Borrow value, int depth, string holder, TextSpan span)
    {
        foreach (var region in Ordered(value.Regions))
        {
            if (region.Depth <= depth) continue;
            Report(DiagnosticCodes.BorrowOutlivesSource, span, [region.Name, holder],
                label: $"{region.Name} is freed at the end of its block",
                help: "copy the data with `.copy(to: alloc)` into an allocator that lives long enough");
            return;
        }
    }

    private void CheckReturn(Borrow value, TextSpan span)
    {
        if (!_reporting) return;
        foreach (var region in Ordered(value.Regions))
        {
            if (region.Depth <= 0) continue;
            Report(DiagnosticCodes.BorrowReturned, span, [region.Name, _fn.Name],
                help: "let the caller pass the buffer or allocator in, and borrow or allocate from that");
            return;
        }
    }

    // ── Structural modification while borrowed ───────────────────────────────────

    private void Mutate(BoundExpr target, TextSpan span)
    {
        if (!_reporting) return;
        if (RootLocal(target) is { } root) _mutations.Add(new Mutation(root, span));
        else if (ThisField(target) is { } field) _mutations.Add(new Mutation(field, span));
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
                var end = loan.Holder is null ? loan.LoopEnd : LastUse(loan.Holder);
                if (end <= m.Span.End) continue;
                var who = loan.Holder is null ? "the `for` loop" : $"`{loan.Holder.Name}`";
                var label = loan.Holder is null
                    ? $"the loop over `{m.Target.Name}` is still running"
                    : $"`{loan.Holder.Name}` is used again on line {Line(new TextSpan(end, 0))}";
                var help = loan.Holder is null
                    ? $"collect the changes in another list and apply them to `{m.Target.Name}` after the loop"
                    : $"finish using {who} first, or copy what it needs before changing `{m.Target.Name}`";
                Report(DiagnosticCodes.ModifiedWhileBorrowed, m.Span, [m.Target.Name, who], label: label, help: help);
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

    /// <summary>Whether <paramref name="e"/> names storage of <c>this</c> (<c>this</c>, <c>f</c>, <c>f[i]</c> of an array or list field).</summary>
    private static bool IsThisRooted(BoundExpr e) => e switch
    {
        BoundThis => true,
        BoundField f => IsThisRooted(f.Receiver),
        BoundTupleElement t => IsThisRooted(t.Receiver),
        BoundIndex i when i.Target.Type is ArrayType or ListType => IsThisRooted(i.Target),
        _ => false,
    };

    /// <summary>The stand-in for the field of <c>this</c> whose storage <paramref name="e"/> names (<c>f</c>, <c>f.g</c>, <c>f[i]</c>), or null.</summary>
    private LocalSymbol? ThisField(BoundExpr e) => e switch
    {
        BoundField { Receiver: BoundThis } f => _thisFields.TryGetValue(f.Field, out var local)
            ? local
            : _thisFields[f.Field] = new LocalSymbol(f.Field.Name, null, f.Field.Type, LocalKind.Parameter),
        BoundField f => ThisField(f.Receiver),
        BoundTupleElement t => ThisField(t.Receiver),
        BoundIndex i when i.Target.Type is ArrayType or ListType => ThisField(i.Target),
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
        SpanType or ListType or MapType or OwnedType or DynRefType => true,
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

    private void Report(DiagnosticDescriptor d, TextSpan span, object?[] args, string? label = null, string? help = null)
    {
        if (!_reporting || !_reported.Add((span.Start, span.End))) return;
        _c.Report(d, _source, span, args, label, help);
    }
}
