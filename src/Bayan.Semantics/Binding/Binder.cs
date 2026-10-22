using Bayan.Semantics.Symbols;
using Bayan.Syntax.Ast;
using Bayan.Syntax.Diagnostics;
using Bayan.Syntax.Text;

namespace Bayan.Semantics.Binding;

/// <summary>
/// Binds one body (function, method, getter, constructor, main, test, constant or field default)
/// into the typed tree, reporting semantic errors as it goes.
/// </summary>
internal sealed partial class Binder
{
    private readonly Compilation _c;
    private readonly FileContext _file;
    private readonly FunctionSymbol? _fn;
    private readonly StructSymbol? _struct;
    private readonly TypeResolver _types;
    private Scope _scope = new(null);
    private readonly List<JumpTarget> _targets = new();
    private int _deferDepth;
    private bool _inPanicHandler;
    /// <summary>The body's first statement: the one place <c>Panic.onPanic</c> may be set.</summary>
    private Stmt? _firstStatement;
    private bool _onPanicSet;
    private int _nextLocalId;

    // Spec R4: a first pass holds its diagnostics and records the first integer type each untyped
    // `var` literal meets; when one is not `i32`, the body is bound again with those types seeded.
    private DiagnosticBag? _held;
    private readonly IReadOnlyDictionary<LocalDecl, TypeSymbol>? _seeds;
    private readonly Dictionary<LocalSymbol, LocalDecl> _pending = new();
    private readonly Dictionary<LocalDecl, TypeSymbol> _constraints = new();
    private readonly Dictionary<LocalDecl, LocalDecl> _peers = new();
    /// <summary>Let locals and parameters a null check proved present, each valid while its scope is open (spec §7.2).</summary>
    private readonly List<(Scope Scope, LocalSymbol Local)> _nonNull = new();

    public Binder(Compilation c, FileContext file, FunctionSymbol? fn, StructSymbol? container,
        IReadOnlyDictionary<string, TypeSymbol>? typeArgs = null, IReadOnlyDictionary<LocalDecl, TypeSymbol>? seeds = null)
    {
        _c = c;
        _file = file;
        _fn = fn;
        _struct = container;
        _types = new TypeResolver(c, file, container, typeArgs ?? fn?.TypeMap ?? container?.TypeMap);
        _seeds = seeds;
    }

    /// <summary>Makes this binding a first pass: diagnostics are held until <see cref="Release"/>.</summary>
    public Binder Hold()
    {
        _held = new DiagnosticBag();
        return this;
    }

    /// <summary>
    /// Ends a first pass. Returns the local types to bind the body again with, dropping what the
    /// pass reported except instantiation errors (the cached instance does not report them again);
    /// or null, when the pass stands and its diagnostics are reported.
    /// </summary>
    public IReadOnlyDictionary<LocalDecl, TypeSymbol>? Release()
    {
        var held = _held!;
        _held = null;
        // A group of pending `var`s that met each other takes the first integer constraint any of them recorded;
        // a group with a member whose own first use is a float or string stays `i32` (Q74).
        var groups = new Dictionary<LocalDecl, TypeSymbol>();
        foreach (var (decl, type) in _constraints)
            if (type.IsInteger) groups.TryAdd(FindPeer(decl), type);
        foreach (var (decl, type) in _constraints)
            if (!type.IsInteger) groups.Remove(FindPeer(decl));
        var seeds = new Dictionary<LocalDecl, TypeSymbol>();
        foreach (var decl in _pending.Values)
            if (groups.TryGetValue(FindPeer(decl), out var type)) seeds[decl] = type;
        if (seeds.Values.Any(t => t.IsInteger && t != PrimitiveType.I32))
        {
            _c.ReportHeld(held.Items.Where(d => d.Label == Compilation.InstantiationLabel));
            return seeds;
        }
        _c.ReportHeld(held.Items);
        return null;
    }

    private SourceText Source => _file.Source;

    private ModuleSymbol Module => _file.Module;

    private sealed class Scope(Scope? parent)
    {
        public Scope? Parent { get; } = parent;

        public Dictionary<string, LocalSymbol> Locals { get; } = new(StringComparer.Ordinal);

        public LocalSymbol? Lookup(string name)
        {
            for (var s = this; s is not null; s = s.Parent)
                if (s.Locals.TryGetValue(name, out var l)) return l;
            return null;
        }
    }

    private void Report(DiagnosticDescriptor d, TextSpan span, object?[] args, string? label = null, string? help = null, Fix? fix = null)
    {
        if (_held is not null) _held.Report(d, Source, span, args, label, help, fix);
        else _c.Report(d, Source, span, args, label, help, fix);
    }

    private void CheckValueName(Name name) => Holding(() => { Naming.CheckValue(_c, Source, name); return 0; });

    private TypeSymbol ResolveType(TypeSyntax t) => Holding(() => _types.Resolve(t));

    /// <summary>Runs a helper that reports through the compilation; a first pass holds those reports too.</summary>
    private T Holding<T>(Func<T> work)
    {
        if (_held is null) return work();
        var outer = _c.ReportInto(_held);
        try { return work(); }
        finally { _c.ReportInto(outer); }
    }

    private void PushScope() => _scope = new Scope(_scope);

    private void PopScope() => _scope = _scope.Parent!;

    private LocalSymbol DeclareLocal(Name name, TypeSymbol type, LocalKind kind)
    {
        if (kind != LocalKind.Temp) CheckValueName(name);
        var local = new LocalSymbol(name.Text, new Location(Source, name.Span), type, kind) { Id = _nextLocalId++ };
        AddLocal(name, local);
        return local;
    }

    private void AddLocal(Name name, LocalSymbol local)
    {
        _fn?.Locals.Add(local);
        if (name.Text == "_") return;
        // A `let` may take the name of another local in the same scope to give it a new type (HANDOFF §4).
        if (local.Kind == LocalKind.Let && _scope.Locals.TryGetValue(name.Text, out var same))
        {
            if (same.Type.Equals(local.Type) && !local.Type.IsError)
                Report(DiagnosticCodes.SameTypeShadowing, name.Span, [name.Text, local.Type.DisplayName],
                    label: same.Location is { } at ? $"`{name.Text}` is declared on line {at.Line}" : null,
                    help: "use the first one, or rename this one to say how it differs");
        }
        else if (_scope.Lookup(name.Text) is { } existing)
        {
            var where = existing.Location is { } l ? $"line {l.Line}" : "here";
            Report(DiagnosticCodes.Shadowing, name.Span, [name.Text], label: $"already declared on {where}",
                help: "pick a different name; Bayan does not let a local hide another one");
        }
        _scope.Locals[name.Text] = local;
    }

    // ── Entry points ─────────────────────────────────────────────────────────────

    private void DeclareParameters()
    {
        foreach (var p in _fn!.Parameters)
        {
            p.Id = _nextLocalId++;
            _fn.Locals.Add(p);
            _scope.Locals[p.Name] = p;
        }
    }

    public void BindFunctionBody(FunctionDecl decl)
    {
        DeclareParameters();
        BindParameterDefaults();
        var checks = BindRequires(decl);
        if (decl.Body is { } body)
        {
            _firstStatement = body.Statements.FirstOrDefault();
            var block = BindBlock(body);
            _fn!.Body = checks.Length == 0 ? block : new BoundBlock(body, [.. checks, block]);
        }
        else if (decl.ExpressionBody is { } e)
        {
            _fn!.Body = new BoundBlock(e, [.. checks, BindExpressionBody(e)]);
        }
        else
        {
            // The parser already reported the missing body.
            _fn!.Body = new BoundBlock(decl, []);
        }
    }

    /// <summary>Binds the <c>requires</c> clauses into entry checks that panic in every build mode.</summary>
    private BoundStmt[] BindRequires(FunctionDecl decl)
    {
        var conditions = new List<BoundExpr>();
        var checks = new List<BoundStmt>();
        foreach (var r in decl.Requires)
        {
            var condition = BindExpr(r, PrimitiveType.Bool);
            if (!condition.Type.IsBool)
            {
                if (!condition.Type.IsError)
                    Report(DiagnosticCodes.RequiresNotBool, r.Span, [condition.Type.DisplayName],
                        help: "write a condition the arguments must meet, such as `count > 0`");
                continue;
            }
            conditions.Add(condition);
            var message = Literal(r, PrimitiveType.String, "contract violated: " + Source.GetText(r.Span));
            var panic = new BoundCall(r, _c.Builtins.Panic, null, [message], PrimitiveType.Never);
            checks.Add(new BoundIf(r, new BoundUnary(r, UnaryOp.Not, condition, PrimitiveType.Bool), new BoundExprStmt(r, panic), null));
        }
        _fn!.Requires = conditions.ToArray();
        return checks.ToArray();
    }

    private void BindParameterDefaults()
    {
        foreach (var p in _fn!.Parameters)
            _c.ParameterDefault(_fn, p);
    }

    public BoundExpr BindParameterDefault(ParameterSymbol p)
    {
        var d = p.Syntax!.Default!;
        var value = BindConverted(d, p.Type);
        if (value.Constant is null && value is not BoundErrorExpr && value is not BoundDefault)
            Report(DiagnosticCodes.NotConstant, d.Span, ["a parameter default"]);
        return value;
    }

    private BoundStmt BindExpressionBody(Expr e)
    {
        if (_fn!.ReturnType.IsVoid)
            return new BoundExprStmt(e, BindStatementExpression(e));
        return new BoundReturn(e, BindConverted(e, _fn.ReturnType));
    }

    public void BindGetter(PropertyDecl decl)
    {
        _fn!.Body = new BoundBlock(decl.Body, [new BoundReturn(decl.Body, BindConverted(decl.Body, _fn.ReturnType))]);
    }

    public void BindConstructor(ConstructorDecl decl)
    {
        DeclareParameters();
        BindParameterDefaults();
        _fn!.Body = BindBlock(decl.Body);
    }

    public void BindMain(List<GlobalStatement> statements)
    {
        var bound = new List<BoundStmt>();
        _firstStatement = statements[0].Statement;
        foreach (var g in statements) bound.Add(BindStatement(g.Statement));
        _fn!.InfersErrors = true;
        _fn.Body = new BoundBlock(statements[0], bound.ToArray());
    }

    /// <summary>A test body; a <c>[Cases]</c> case first binds each parameter as a <c>let</c> of its value.</summary>
    public void BindTest(TestDecl decl, Expr[]? values)
    {
        _fn!.InfersErrors = true;
        if (values is null)
        {
            _fn.Body = BindBlock(decl.Body);
            return;
        }
        var statements = new List<BoundStmt>();
        for (var i = 0; i < decl.Params.Length; i++)
        {
            var p = decl.Params[i];
            var type = ResolveType(p.Type);
            var value = BindConverted(values[i], type);
            statements.Add(new BoundLocalDecl(p, DeclareLocal(p.Name, type, LocalKind.Let), value));
        }
        statements.Add(BindBlock(decl.Body));
        _fn.Body = new BoundBlock(decl.Body, statements.ToArray());
    }

    public BoundExpr BindFieldDefault(FieldSymbol field)
    {
        var value = BindConverted(field.Decl.Default!, field.Type);
        return value;
    }

    /// <summary>The arguments of each member of a data enum, matched like call arguments; each must be a constant (BYN2723).</summary>
    public void BindEnumArguments(EnumSymbol e)
    {
        var ps = e.Fields.Select(f => new ParameterSymbol(f.Name, f.Location, f.Type, ParamModifier.None, f.Index)).ToList();
        foreach (var d in e.Decl.Members)
        {
            if (e.Find(d.Name.Text) is not { Values.Length: 0 } member) continue;
            if (d.Args is null && e.Decl.Params is null) continue;
            var name = $"{e.Name}.{member.Name}";
            var args = BindArguments(new CallExpr(d.Span, new NameExpr(d.Name.Span, d.Name, []), d.Args ?? []), ps, null, enforceNames: false, displayName: name);
            member.Values = new ConstantValue?[args.Length];
            for (var i = 0; i < args.Length; i++)
            {
                var arg = args[i];
                if (arg.Type.IsError || ps[i].Type.IsError) continue;
                if (arg is BoundEnumMember em) member.Values[i] = new ConstantValue((Int128)em.Member.Ordinal);
                else if (arg.Constant is { } value) member.Values[i] = value;
                else Report(DiagnosticCodes.EnumArgumentNotConstant, arg.Syntax.Span, [ps[i].Name, name],
                    label: "computed at run time", help: "use literals and `const`s only; enum data is static");
            }
        }
    }

    public void BindModuleConstant(ConstSymbol c)
    {
        var decl = c.Decl;
        var declared = decl.Type is { } t ? ResolveType(t) : null;
        if (decl.Init is null)
        {
            Report(DiagnosticCodes.MissingInitializer, decl.Name.Span, ["const", decl.Name.Text]);
            c.Type = declared ?? ErrorType.Instance;
            return;
        }
        var value = declared is null ? BindExpr(decl.Init) : BindConverted(decl.Init, declared);
        c.Type = declared ?? DefaultType(value, decl.Init);
        if (declared is null) value = Convert(value, c.Type, decl.Init.Span);
        if (c.Type is Symbols.ArrayType or ListType or SpanType || decl.Init is CollectionExpr)
        {
            BindStaticArray(c, value);
            return;
        }
        if (value.Constant is null && !value.Type.IsError)
            Report(DiagnosticCodes.NotConstant, decl.Init.Span, ["a `const`"]);
        c.Value = value.Constant;
        c.Bound = value;
    }

    /// <summary>A module-level <c>const T[N]</c> whose elements are literal numbers, <c>bool</c>s, <c>char</c>s or <c>string</c>s becomes static data (BYN2229 otherwise).</summary>
    private void BindStaticArray(ConstSymbol c, BoundExpr value)
    {
        c.Bound = value;
        if (value.Type.IsError) return;
        var decl = c.Decl;
        var help = $"write `const T[N] {decl.Name.Text} = [ … ];` with `T` a number type, `bool`, `char` or `string`, and only literals or constants inside";
        if (c.Type is not Symbols.ArrayType { Element: PrimitiveType { IsNumeric: true } or PrimitiveType { Kind: PrimitiveKind.Bool or PrimitiveKind.Char or PrimitiveKind.String } })
        {
            Report(DiagnosticCodes.InvalidConstArray, decl.Type?.Span ?? decl.Init!.Span, [decl.Name.Text], help: help);
            return;
        }
        if (value is not BoundCollection collection)
        {
            Report(DiagnosticCodes.InvalidConstArray, decl.Init!.Span, [decl.Name.Text], help: help);
            return;
        }
        if (collection.Elements.Any(e => e.Type.IsError)) return;
        if (collection.Elements.FirstOrDefault(e => e.Constant is null) is { } bad)
        {
            Report(DiagnosticCodes.InvalidConstArray, bad.Syntax.Span, [decl.Name.Text], label: "not a constant", help: help);
            return;
        }
        c.IsStaticArray = true;
        c.Elements = collection.Elements.Select(e => e.Constant!).ToArray();
    }

    /// <summary>The <c>N</c> of <c>T[N]</c>: a non-negative integer constant, or -1 after an error.</summary>
    public long BindArraySize(Expr size)
    {
        var value = BindConverted(size, PrimitiveType.I64);
        if (value.Type.IsError) return -1;
        if (value.Constant is not { Value: Int128 n })
        {
            Report(DiagnosticCodes.NotConstant, size.Span, ["an array length"]);
            return -1;
        }
        if (n < 0)
        {
            Report(DiagnosticCodes.LiteralOutOfRange, size.Span, [n.ToString(), "an array length"]);
            return -1;
        }
        return (long)n;
    }

    /// <summary>The value of <c>[ExitCode(n)]</c>: one integer constant from 1 to 255, else BYN2772 and 1.</summary>
    public int BindExitCode(AttributeSyntax attribute)
    {
        if (attribute.Args is not [{ Name: null, Modifier: ArgModifier.None } arg])
        {
            Report(DiagnosticCodes.InvalidExitCode, attribute.Span, []);
            return 1;
        }
        var value = BindExpr(arg.Value);
        if (value.Type.IsError) return 1;
        if (!value.Type.IsInteger || value.Constant is not { Value: Int128 n })
        {
            Report(DiagnosticCodes.InvalidExitCode, arg.Value.Span, []);
            return 1;
        }
        if (n < 1 || n > 255)
        {
            Report(DiagnosticCodes.InvalidExitCode, arg.Value.Span, [], help: "pick a code from 1 to 255; 0 means success, and exit codes above 255 do not survive the operating system");
            return 1;
        }
        return (int)n;
    }

    // ── Statements ───────────────────────────────────────────────────────────────

    private BoundBlock BindBlock(BlockStmt block)
    {
        PushScope();
        var statements = new BoundStmt[block.Statements.Length];
        for (var i = 0; i < statements.Length; i++) statements[i] = BindStatement(block.Statements[i]);
        PopScope();
        return new BoundBlock(block, statements);
    }

    /// <summary>A loop or <c>if</c> body: a block, or one statement in its own scope.</summary>
    private BoundStmt BindBody(Stmt body)
    {
        if (body is BlockStmt b) return BindBlock(b);
        PushScope();
        var s = BindStatement(body);
        PopScope();
        if (body is LocalDecl)
            Report(DiagnosticCodes.DeclarationAsBody, body.Span, [], help: "wrap it in `{ … }` if the variable is used inside");
        return s;
    }

    private BoundStmt BindStatement(Stmt stmt, JumpTarget? labelTarget = null)
    {
        switch (stmt)
        {
            case BlockStmt b:
                return BindBlock(b);
            case LocalDecl d:
                return BindLocalDecl(d);
            case ExprStmt e:
                return new BoundExprStmt(e, BindStatementExpression(e.Expr));
            case AssignStmt { Op: AssignOp.Assign, Target: MemberExpr { Target: NameExpr { Name.Text: "Panic" }, Member.Text: "onPanic" } } a
                when _scope.Lookup("Panic") is null:
                return BindOnPanic(a);
            case AssignStmt a:
                return BindAssign(a);
            case IncDecStmt i:
                return BindIncDec(i);
            case ReturnStmt r:
                return BindReturn(r);
            case BreakStmt b:
                return BindBreak(b);
            case ContinueStmt c:
                return BindContinue(c);
            case IfStmt i:
            {
                var cond = BindCondition(i.Condition);
                var then = Narrowed(cond, true, () => BindBody(i.Then));
                var els = i.Else is { } e ? Narrowed(cond, false, () => BindBody(e)) : null;
                return NarrowAfter(new BoundIf(i, cond, then, els) { Never = i.Likelihood == Likelihood.Never });
            }
            case UnlessStmt u:
            {
                var cond = Negate(u.Condition, BindCondition(u.Condition));
                return NarrowAfter(new BoundIf(u, cond, Narrowed(cond, true, () => BindBody(u.Body)), null));
            }
            case GuardedStmt g:
            {
                // The condition is evaluated first (spec §7.3); pattern variables it binds stay in scope.
                var cond = BindCondition(g.Condition);
                if (g.Unless) cond = Negate(g.Condition, cond);
                var body = Narrowed(cond, true, () => BindStatement(g.Statement));
                return NarrowAfter(new BoundIf(g, cond, body, null));
            }
            case WhileStmt w:
            {
                var target = labelTarget ?? new JumpTarget(null, true);
                PushScope();
                var cond = BindCondition(w.Condition);
                _targets.Add(target);
                var body = BindBody(w.Body);
                _targets.RemoveAt(_targets.Count - 1);
                PopScope();
                return new BoundWhile(w, cond, body, target);
            }
            case WhileLetStmt w:
                return BindWhileLet(w, labelTarget ?? new JumpTarget(null, true));
            case DoWhileStmt d:
            {
                var target = labelTarget ?? new JumpTarget(null, true);
                _targets.Add(target);
                var body = BindBody(d.Body);
                _targets.RemoveAt(_targets.Count - 1);
                var cond = BindCondition(d.Condition);
                return new BoundDoWhile(d, body, cond, target);
            }
            case ForStmt f:
                return BindFor(f, labelTarget ?? new JumpTarget(null, true));
            case LabeledStmt l:
                return BindLabeled(l);
            case SwitchStmt s:
                return BindSwitchStatement(s, labelTarget ?? new JumpTarget(null, false));
            case DeferStmt { Kind: DeferKind.Always } d:
            {
                _deferDepth++;
                var body = BindBody(d.Body);
                _deferDepth--;
                return new BoundDefer(d, body);
            }
            case DeferStmt { Kind: DeferKind.Failing } d:
                return BindErrdefer(d);
            case DeferStmt d:
                return BindPanicDefer(d);
            case ContextStmt c:
                return BindContext(c);
            case AssertStmt a:
                return BindAssert(a);
            case EmptyStmt:
                return new BoundBlock(stmt, []);
            case ErrorStmt:
                return new BoundErrorStmt(stmt);
            default:
                Report(DiagnosticCodes.Unsupported, stmt.Span, ["this statement"]);
                return new BoundErrorStmt(stmt);
        }
    }

    private static BoundExpr Negate(Expr syntax, BoundExpr cond) =>
        cond.Constant is { Value: bool b }
            ? new BoundLiteral(syntax, PrimitiveType.Bool, new ConstantValue(!b)) { Constant = new ConstantValue(!b) }
            : new BoundUnary(syntax, UnaryOp.Not, cond, PrimitiveType.Bool);

    private BoundExpr BindCondition(Expr e) => BindConverted(e, PrimitiveType.Bool);

    private BoundStmt BindAssert(AssertStmt a)
    {
        var condition = BindExpr(a.Condition, PrimitiveType.Bool);
        if (condition.Type.IsError || Conversions.Implicit(condition.Type, PrimitiveType.Bool) is not null)
            return new BoundAssert(a, Convert(condition, PrimitiveType.Bool, a.Condition.Span));
        Report(DiagnosticCodes.AssertNotBool, a.Condition.Span, [condition.Type.DisplayName], label: "this is not a `bool`",
            help: "check the value against what you expect, e.g. `assert count == 3;`");
        return new BoundErrorStmt(a);
    }

    // ── Null narrowing (spec §7.2) ───────────────────────────────────────────────

    private bool IsNonNull(LocalSymbol local)
    {
        foreach (var (scope, l) in _nonNull)
        {
            if (l != local) continue;
            for (var s = _scope; s is not null; s = s.Parent)
                if (s == scope) return true;
        }
        return false;
    }

    /// <summary>Binds code that runs only when <paramref name="cond"/> is <paramref name="whenTrue"/>.</summary>
    private T Narrowed<T>(BoundExpr cond, bool whenTrue, Func<T> bind)
    {
        var mark = _nonNull.Count;
        foreach (var l in NonNullWhen(cond, whenTrue)) _nonNull.Add((_scope, l));
        var result = bind();
        _nonNull.RemoveRange(mark, _nonNull.Count - mark);
        return result;
    }

    /// <summary>When one branch always exits, the rest of the block runs only under the other.</summary>
    private BoundIf NarrowAfter(BoundIf i)
    {
        var thenExits = Exits(i.Then);
        var elseExits = i.Else is { } e && Exits(e);
        if (thenExits != elseExits)
            foreach (var l in NonNullWhen(i.Condition, elseExits)) _nonNull.Add((_scope, l));
        return i;
    }

    private static bool Exits(BoundStmt s) => s switch
    {
        BoundReturn or BoundBreak or BoundContinue => true,
        BoundBlock b => b.Statements.Length > 0 && Exits(b.Statements[^1]),
        BoundExprStmt e => e.Expr.Type.IsNever,
        BoundIf { Else: { } e } i => Exits(i.Then) && Exits(e),
        _ => false,
    };

    /// <summary>The let locals and parameters that <paramref name="cond"/> being <paramref name="whenTrue"/> proves present; <c>var</c>s can change, so they never narrow.</summary>
    private static List<LocalSymbol> NonNullWhen(BoundExpr cond, bool whenTrue)
    {
        var found = new List<LocalSymbol>();
        Collect(cond, whenTrue);
        return found;

        void Collect(BoundExpr c, bool when)
        {
            switch (c)
            {
                case BoundIs { Value: BoundLocal { Local: { Type: OptionalType } l } } i
                    when l.Kind == LocalKind.Let || l is ParameterSymbol { Modifier: ParamModifier.None or ParamModifier.In }:
                    if (i.Pattern is BoundNotPattern { Operand: BoundConstantPattern { Value: BoundDefault } } ? when
                        : i.Pattern is BoundConstantPattern { Value: BoundDefault } && !when)
                        found.Add(l);
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

    private BoundStmt BindLabeled(LabeledStmt l)
    {
        CheckValueName(l.Label);
        if (_targets.Any(t => t.Label == l.Label.Text))
            Report(DiagnosticCodes.DuplicateDeclaration, l.Label.Span, [l.Label.Text], label: "an enclosing statement has this label");
        var target = new JumpTarget(l.Label.Text, l.Statement is not SwitchStmt);
        if (l.Statement is not (WhileStmt or WhileLetStmt or DoWhileStmt or ForStmt or SwitchStmt))
        {
            Report(DiagnosticCodes.LabelNotOnLoop, l.Label.Span, [l.Label.Text]);
            return BindStatement(l.Statement);
        }
        return BindStatement(l.Statement, target);
    }

    private BoundStmt BindLocalDecl(LocalDecl d)
    {
        var declared = d.Type is { } t ? ResolveType(t) : _seeds?.GetValueOrDefault(d) is { IsInteger: true } seed ? seed : null;
        BoundExpr? init = null;
        if (d.Init is { } e)
        {
            init = declared is null ? BindExpr(e) : BindConverted(e, declared);
        }
        else if (d.Kind != BindingKind.Var)
        {
            Report(DiagnosticCodes.MissingInitializer, d.Name.Span, [d.Kind == BindingKind.Let ? "let" : "const", d.Name.Text],
                help: d.Kind == BindingKind.Let ? "give it a value, or declare it with `var` and assign it on every path before use" : null);
        }
        else if (declared is null)
        {
            Report(DiagnosticCodes.CannotInferType, d.Name.Span, [d.Name.Text], help: $"write the type: `var i64 {d.Name.Text};`");
        }

        TypeSymbol type;
        if (declared is not null) type = declared;
        else if (init is not null)
        {
            type = DefaultType(init, d.Init!);
            init = Convert(init, type, d.Init!.Span);
        }
        else type = ErrorType.Instance;

        var kind = d.Kind switch { BindingKind.Let => LocalKind.Let, BindingKind.Const => LocalKind.Const, _ => LocalKind.Var };
        if (d.Kind == BindingKind.Const && init is not null && init.Constant is null && !init.Type.IsError)
            Report(DiagnosticCodes.NotConstant, d.Init!.Span, ["a `const`"]);
        if (d.Using) CheckDisposable(d, type);
        var local = DeclareLocal(d.Name, type, kind);
        if (_held is not null && kind == LocalKind.Var && declared is null && type == PrimitiveType.I32 && IsUntypedLiteral(d.Init!))
            _pending[local] = d;
        local.IsUsing = d.Using;
        if (kind is LocalKind.Let or LocalKind.Var && !d.Using && Source.GetText(new TextSpan(d.Span.Start, 3)) == (kind == LocalKind.Let ? "let" : "var"))
            local.KeywordSpan = new TextSpan(d.Span.Start, 3);
        if (kind == LocalKind.Const) local.Constant = init?.Constant;
        return new BoundLocalDecl(d, local, init);
    }

    private void CheckDisposable(LocalDecl d, TypeSymbol type)
    {
        if (type == BuiltinType.Arena || type is ListType || type.IsError) return;
        Report(DiagnosticCodes.NotDisposable, d.Name.Span, [type.DisplayName],
            help: "use `using` only for values that own memory: `Arena` and `List<T>`");
    }

    /// <summary>The type a binding gets from its initializer when none is written.</summary>
    private TypeSymbol DefaultType(BoundExpr init, Expr syntax)
    {
        switch (init.Type)
        {
            case NullType:
                Report(DiagnosticCodes.CannotInferType, syntax.Span, ["null"], help: "write the type: `i64? x = null;`");
                return ErrorType.Instance;
            case PrimitiveType { Kind: PrimitiveKind.Void }:
                Report(DiagnosticCodes.VoidValue, syntax.Span, []);
                return ErrorType.Instance;
            case PrimitiveType { Kind: PrimitiveKind.Never }:
                return PrimitiveType.Never;
            default:
                return init.Type;
        }
    }

    /// <summary><c>while let x = e { … }</c> is <c>while (e is { } x) { … }</c>.</summary>
    private BoundStmt BindWhileLet(WhileLetStmt w, JumpTarget target)
    {
        PushScope();
        var value = BindExpr(w.Value);
        if (value.Type is not (OptionalType or ErrorType))
            Report(DiagnosticCodes.WhileLetNotOptional, w.Value.Span, [value.Type.DisplayName],
                help: $"loop with `while` and a condition, or bind it once: `let {w.Variable.Text} = …;`");
        var cond = new BoundIs(w, value, BindPresent(w.Variable, value.Type));
        _targets.Add(target);
        var body = BindBody(w.Body);
        _targets.RemoveAt(_targets.Count - 1);
        PopScope();
        return new BoundWhile(w, cond, body, target);
    }

    /// <summary>The pattern <c>{ } x</c>: on an optional, matches a present value and binds it unwrapped.</summary>
    private BoundPropertyPattern BindPresent(Name name, TypeSymbol input)
    {
        var local = DeclareLocal(name, Unwrap(input), LocalKind.PatternVariable);
        local.BorrowsSubject = true;
        return new BoundPropertyPattern(name, input, Unwrap(input), [], local);
    }

    private BoundStmt BindFor(ForStmt f, JumpTarget target)
    {
        PushScope();
        var iterable = BindExpr(f.Iterable);
        if (f.Try) return BindForTry(f, iterable, target);
        TypeSymbol element = iterable.Type switch
        {
            RangeType r => r.Element,
            SpanType s => s.Element,
            ListType l => l.Element,
            MapType m => m.Entry,
            Symbols.ArrayType a => a.Element,
            PrimitiveType { Kind: PrimitiveKind.String } => PrimitiveType.Char,
            ErrorType => ErrorType.Instance,
            _ => NotIterable(f, iterable.Type),
        };
        element = DeclaredElement(f, element);
        var variable = DeclareLocal(f.Variable, element, f.Kind == BindingKind.Var ? LocalKind.Var : LocalKind.ForVariable);
        variable.BorrowsSubject = true;
        var body = BindForBody(f, variable, target);
        PopScope();
        return new BoundFor(f, variable, iterable, body, target);
    }

    /// <summary>The element type, or the type written in <c>for T x in e</c> when the element converts to it.</summary>
    private TypeSymbol DeclaredElement(ForStmt f, TypeSymbol element)
    {
        if (f.Type is not { } t) return element;
        var declared = ResolveType(t);
        if (!declared.IsError && !element.IsError && !declared.Equals(element) && Conversions.Implicit(element, declared) is null)
            Report(DiagnosticCodes.TypeMismatch, t.Span, [element.DisplayName, declared.DisplayName]);
        else if (!declared.IsError) element = declared;
        return element;
    }

    /// <summary>The loop body; for <c>for (k, v) in e</c>, preceded by one binding per element of the tuple <paramref name="variable"/>.</summary>
    private BoundStmt BindForBody(ForStmt f, LocalSymbol variable, JumpTarget target)
    {
        var prelude = new List<BoundStmt>();
        if (f.Names.Length > 0)
        {
            if (variable.Type is TupleType tt && tt.Fields.Count == f.Names.Length)
            {
                for (var i = 0; i < f.Names.Length; i++)
                {
                    var name = f.Names[i];
                    var element = DeclareLocal(name, tt.Fields[i].Type, LocalKind.ForVariable);
                    element.BorrowsSubject = true;
                    prelude.Add(new BoundLocalDecl(name, element, new BoundTupleElement(name, new BoundLocal(name, variable), i, element.Type)));
                }
            }
            else
            {
                if (!variable.Type.IsError)
                    Report(DiagnosticCodes.PatternMismatch, f.Variable.Span, [$"{f.Names.Length} names", variable.Type.DisplayName],
                        help: "name one loop variable, or iterate over pairs such as a map's entries");
                foreach (var name in f.Names) DeclareLocal(name, ErrorType.Instance, LocalKind.ForVariable);
            }
        }
        _targets.Add(target);
        var body = BindBody(f.Body);
        _targets.RemoveAt(_targets.Count - 1);
        return prelude.Count == 0 ? body : new BoundBlock(f.Body, [.. prelude, body]);
    }

    /// <summary>
    /// <c>for try x in e { … }</c> over a value whose <c>next()</c> returns <c>T?</c> and fails: the loop owns the value
    /// and runs <c>while (try seq.next() is { } x) { … }</c>, so a failing step propagates like any <c>try</c>.
    /// </summary>
    private BoundStmt BindForTry(ForStmt f, BoundExpr iterable, JumpTarget target)
    {
        if (FallibleNext(iterable.Type) is null)
        {
            if (!iterable.Type.IsError)
                Report(DiagnosticCodes.NotIterable, f.Iterable.Span, [iterable.Type.DisplayName],
                    help: "give `for try` a value with a failing `next()` that returns `T?`; write `for x in …` for a sequence that cannot fail");
            foreach (var name in f.Names.Length > 0 ? f.Names : [f.Variable]) DeclareLocal(name, ErrorType.Instance, LocalKind.ForVariable);
            BindBody(f.Body);
            PopScope();
            return new BoundErrorStmt(f);
        }
        var span = f.Iterable.Span;
        var sequence = DeclareLocal(new Name(span, $"<sequence {_nextLocalId}>"), iterable.Type, LocalKind.Temp);
        var next = new TryExpr(span, new CallExpr(span, new MemberExpr(span, new NameExpr(span, new Name(span, sequence.Name), []), new Name(span, "next"), false), []));
        var step = BindExpr(next);
        if (f.Type is { } t && ResolveType(t) is { IsError: false } declared && !Unwrap(step.Type).IsError && !declared.Equals(Unwrap(step.Type)))
            Report(DiagnosticCodes.TypeMismatch, t.Span, [Unwrap(step.Type).DisplayName, declared.DisplayName]);
        var pattern = BindPresent(f.Variable, step.Type);
        var body = BindForBody(f, pattern.Designation!, target);
        PopScope();
        return new BoundBlock(f, [new BoundLocalDecl(f, sequence, iterable), new BoundWhile(f, new BoundIs(f, step, pattern), body, target)]);
    }

    /// <summary>The <c>T? next() fails …</c> method that <c>for try</c> steps with, if <paramref name="type"/> has one.</summary>
    private static FunctionSymbol? FallibleNext(TypeSymbol type) =>
        type is StructSymbol s
            ? s.Methods.FirstOrDefault(m => m is { Name: "next", IsStatic: false, Fails: true, Parameters.Count: 0, ReturnType: OptionalType })
            : null;

    private TypeSymbol NotIterable(ForStmt f, TypeSymbol type)
    {
        if (FallibleNext(type) is null)
            Report(DiagnosticCodes.NotIterable, f.Iterable.Span, [type.DisplayName],
                help: "use `for` over ranges (`0..n`), spans, lists, arrays and strings");
        else
            Report(DiagnosticCodes.NotIterable, f.Iterable.Span, [type.DisplayName], label: "each step can fail",
                help: "write `for try`", fix: new Fix(new TextSpan(f.Span.Start + "for".Length, 0), " try"));
        return ErrorType.Instance;
    }

    private BoundStmt BindReturn(ReturnStmt r)
    {
        if (InPanicHandler(r.Span, "return")) return new BoundErrorStmt(r);
        if (_deferDepth > 0)
        {
            Report(DiagnosticCodes.JumpOutOfDefer, r.Span, ["return"]);
            return new BoundErrorStmt(r);
        }
        var fn = _fn;
        if (fn is null) return new BoundErrorStmt(r);
        var returnType = fn.ReturnType;
        if (r.Value is null)
        {
            if (!returnType.IsVoid && !returnType.IsError)
                Report(DiagnosticCodes.MissingReturnValue, r.Span, [returnType.DisplayName]);
            return new BoundReturn(r, null);
        }
        if (fn.Fails && TryBindFailure(r.Value) is { } failure)
            return new BoundFail(r, failure);
        if (returnType.IsVoid)
        {
            var v = BindExpr(r.Value);
            if (v.Type.IsNever) return new BoundExprStmt(r, v);
            if (!v.Type.IsError)
                Report(DiagnosticCodes.ReturnValueInVoid, r.Value.Span, [fn.Kind == FunctionKind.Main ? "main" : fn.Name],
                    help: fn.Kind == FunctionKind.Main ? null : $"declare a return type instead of `void`, or remove the value");
            return new BoundReturn(r, null);
        }
        return new BoundReturn(r, BindConverted(r.Value, returnType));
    }

    /// <summary>In a failing function, <c>return .variant</c> or <c>return Set.variant</c> fails with that error.</summary>
    private BoundExpr? TryBindFailure(Expr value)
    {
        var fn = _fn!;
        switch (value)
        {
            case TargetMemberExpr t when fn.ReturnType is not ErrorSetSymbol:
            {
                var matches = fn.ErrorSets.Select(s => s.Find(t.Member.Text)).Where(v => v is not null).ToList();
                if (matches.Count == 1) return new BoundErrorVariant(t, matches[0]!);
                if (matches.Count > 1)
                {
                    Report(DiagnosticCodes.AmbiguousVariant, t.Span, [t.Member.Text, string.Join("`, `", matches.Select(m => m!.Set.Name))]);
                    return new BoundErrorExpr(t);
                }
                if (fn.InfersErrors && fn.ErrorSets.Count == 0 && fn.ReturnType is PrimitiveType { Kind: PrimitiveKind.Void })
                {
                    Report(DiagnosticCodes.UnknownVariant, t.Span, [t.Member.Text, "the inferred error set"],
                        help: "name the set: `return MyError.x;`");
                    return new BoundErrorExpr(t);
                }
                return null;
            }
            case MemberExpr m when fn.ReturnType is not ErrorSetSymbol:
            {
                var bound = BindExprOrType(m);
                if (bound is BoundErrorVariant variant)
                {
                    CheckCanFailWith(variant.Variant.Set, m.Span);
                    return variant;
                }
                return null;
            }
            default:
                return null;
        }
    }

    private void CheckCanFailWith(ErrorSetSymbol set, TextSpan span)
    {
        var fn = _fn!;
        if (fn.ErrorSets.Contains(set)) return;
        if (fn.InfersErrors) return;
        Report(DiagnosticCodes.UndeclaredError, span, [set.Name, fn.Name],
            help: $"add it to the signature: `fails {string.Join(", ", fn.ErrorSets.Select(s => s.Name).Append(set.Name))}`");
    }

    private JumpTarget? FindTarget(Name? label, bool loopOnly, TextSpan span, string keyword)
    {
        if (InPanicHandler(span, keyword)) return null;
        if (_deferDepth > 0)
        {
            Report(DiagnosticCodes.JumpOutOfDefer, span, [keyword]);
            return null;
        }
        for (var i = _targets.Count - 1; i >= 0; i--)
        {
            var t = _targets[i];
            if (label is not null)
            {
                if (t.Label != label.Text) continue;
                if (loopOnly && !t.IsLoop)
                {
                    Report(DiagnosticCodes.ContinueSwitch, label.Span, [label.Text]);
                    return null;
                }
                return t;
            }
            if (!loopOnly || t.IsLoop) return t;
        }
        if (label is not null)
            Report(DiagnosticCodes.UnknownLabel, label.Span, [label.Text]);
        else
            Report(DiagnosticCodes.JumpOutsideLoop, span, [keyword]);
        return null;
    }

    private BoundStmt BindBreak(BreakStmt b) =>
        FindTarget(b.Label, false, b.Span, "break") is { } t ? new BoundBreak(b, t) : new BoundErrorStmt(b);

    private BoundStmt BindContinue(ContinueStmt c) =>
        FindTarget(c.Label, true, c.Span, "continue") is { } t ? new BoundContinue(c, t) : new BoundErrorStmt(c);

    private BoundStmt BindErrdefer(DeferStmt e)
    {
        if (_fn is not { Fails: true } && _fn?.Kind is not (FunctionKind.Main or FunctionKind.Test))
            Report(DiagnosticCodes.ErrdeferWithoutFails, e.Span, [],
                help: "remove `failing` to run it on every exit, or add a `fails` clause to the function");
        PushScope();
        LocalSymbol? err = null;
        if (e.Binding is { } name)
        {
            err = DeclareLocal(name, _fn is null ? ErrorType.Instance : ErrorUnionType.Of(_fn.ErrorSets), LocalKind.Let);
            err.IsCatch = true;
        }
        _deferDepth++;
        var body = BindBody(e.Body);
        _deferDepth--;
        PopScope();
        return new BoundErrdefer(e, err, body);
    }

    /// <summary><c>defer panicking [(p)] body</c>: <c>return</c>, <c>break</c>, <c>continue</c> and <c>try</c> in the body are BYN2612; <see cref="PanicHandlerCheck"/> checks the rest.</summary>
    private BoundStmt BindPanicDefer(DeferStmt d)
    {
        PushScope();
        LocalSymbol? panic = d.Binding is { } name ? DeclareLocal(name, BuiltinType.Panic, LocalKind.Let) : null;
        var (deferDepth, inHandler) = (_deferDepth, _inPanicHandler);
        (_deferDepth, _inPanicHandler) = (0, true);
        var body = BindBody(d.Body);
        (_deferDepth, _inPanicHandler) = (deferDepth, inHandler);
        PopScope();
        return new BoundPanicDefer(d, panic, body);
    }

    /// <summary>
    /// <c>Panic.onPanic = f;</c> with <c>f</c> a named <c>void f(Panic p)</c>: only as the first statement of the program's
    /// entry function, once (BYN2614). <see cref="PanicHandlerCheck"/> checks <c>f</c>'s body.
    /// </summary>
    private BoundStmt BindOnPanic(AssignStmt a)
    {
        var value = BindExpr(a.Value);
        FunctionSymbol? handler = null;
        if (value is BoundMethodGroup { Receiver: null, Function: { } f } && f.Kind == FunctionKind.Function && !f.IsGeneric && !f.Fails
            && f.ReturnType.IsVoid && f.Parameters is [{ Modifier: ParamModifier.None, Type: BuiltinType { Kind: BuiltinKind.Panic } }])
            handler = f;
        else if (!value.Type.IsError || value is BoundMethodGroup)
            Report(DiagnosticCodes.TypeMismatch, a.Value.Span,
                [value is BoundMethodGroup g ? $"{g.Function.ReturnType.DisplayName} {g.Function.Name}({string.Join(", ", g.Function.Parameters.Select(p => p.Type.DisplayName))})" : value.Type.DisplayName,
                    "void f(Panic p)"],
                help: "name a function declared `void f(Panic p)`");
        var entry = !_c.Options.Library && _fn is not null && _fn == _c.EntryModule.Main;
        string? misplaced = null;
        if (!entry) misplaced = _c.Options.Library ? "libraries cannot set it" : $"`{_fn?.Name}` is not the program's entry function";
        else if (_onPanicSet) misplaced = "set again here";
        else if (a != _firstStatement) misplaced = "not the first statement of `main`";
        if (misplaced is not null)
        {
            Report(DiagnosticCodes.OnPanicPlacement, a.Span, [], label: misplaced,
                help: _c.Options.Library ? "set `Panic.onPanic` in the program that uses this library" : "set it once, as the first statement of `main`");
            return new BoundErrorStmt(a);
        }
        _onPanicSet = true;
        if (handler is null) return new BoundErrorStmt(a);
        _c.OnPanic = handler;
        return new BoundSetOnPanic(a, handler);
    }

    /// <summary>BYN2612 for <paramref name="keyword"/> inside a <c>defer panicking</c> body; true when reported.</summary>
    private bool InPanicHandler(TextSpan span, string keyword)
    {
        if (!_inPanicHandler) return false;
        Report(DiagnosticCodes.PanicHandlerRestricted, span, [$"`{keyword}`"], help: "remove it; panic handlers can't recover");
        return true;
    }

    private BoundStmt BindContext(ContextStmt c)
    {
        BoundExpr? allocator = null;
        foreach (var v in c.Values)
        {
            if (v.Name.Text != "allocator")
            {
                Report(DiagnosticCodes.Unsupported, v.Name.Span, [$"the context value `{v.Name.Text}`"]);
                continue;
            }
            allocator = BindConverted(v.Value, BuiltinType.Arena);
        }
        if (allocator is null)
        {
            Report(DiagnosticCodes.MissingArgument, c.Span, ["allocator", "context"]);
            allocator = new BoundErrorExpr(c);
        }
        return new BoundContext(c, allocator, BindBlock(c.Body));
    }

    private BoundStmt BindSwitchStatement(SwitchStmt s, JumpTarget target)
    {
        var value = BindSwitchSubject(s.Value, out var owns);
        var sections = new List<BoundSwitchSection>();
        _targets.Add(target);
        var sawDefault = false;
        foreach (var section in s.Sections)
        {
            PushScope();
            BoundPattern? pattern = null;
            if (section.Label.Pattern is { } p)
            {
                _patternOwns = owns;
                pattern = BindPattern(p, value.Type);
                _patternOwns = false;
                if (owns) CheckOwningOverlap(pattern);
                if (owns && section.Label.Guard is { } lg) GuardBindings(pattern, lg.Span);
            }
            else
            {
                if (sawDefault) Report(DiagnosticCodes.DuplicateDefault, section.Label.Span, []);
                sawDefault = true;
            }
            var guard = section.Label.Guard is { } g ? BindCondition(g) : null;
            var body = section.Statements.Select(st => BindStatement(st)).ToArray();
            PopScope();
            sections.Add(new BoundSwitchSection(section, pattern, guard, body));
        }
        _targets.RemoveAt(_targets.Count - 1);
        var arms = sections.Select(x => (x.Pattern, x.Guard)).ToList();
        var exhaustive = sawDefault || CheckExhaustive(s.Value.Span, value.Type, arms, statement: true);
        return new BoundSwitch(s, value, sections.ToArray(), target) { Exhaustive = exhaustive };
    }
}
