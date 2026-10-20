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
    private int _nextLocalId;

    public Binder(Compilation c, FileContext file, FunctionSymbol? fn, StructSymbol? container)
    {
        _c = c;
        _file = file;
        _fn = fn;
        _struct = container;
        _types = new TypeResolver(c, file, container);
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

    private void Report(DiagnosticDescriptor d, TextSpan span, object?[] args, string? label = null, string? help = null, Fix? fix = null) =>
        _c.Report(d, Source, span, args, label, help, fix);

    private void PushScope() => _scope = new Scope(_scope);

    private void PopScope() => _scope = _scope.Parent!;

    private LocalSymbol DeclareLocal(Name name, TypeSymbol type, LocalKind kind)
    {
        if (kind != LocalKind.Temp) Naming.CheckValue(_c, Source, name);
        var local = new LocalSymbol(name.Text, new Location(Source, name.Span), type, kind) { Id = _nextLocalId++ };
        AddLocal(name, local);
        return local;
    }

    private void AddLocal(Name name, LocalSymbol local)
    {
        _fn?.Locals.Add(local);
        if (name.Text == "_") return;
        if (_scope.Lookup(name.Text) is { } existing)
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
        if (decl.Body is { } body)
        {
            _fn!.Body = BindBlock(body);
        }
        else if (decl.ExpressionBody is { } e)
        {
            _fn!.Body = new BoundBlock(e, [BindExpressionBody(e)]);
        }
        else
        {
            // The parser already reported the missing body.
            _fn!.Body = new BoundBlock(decl, []);
        }
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
        foreach (var g in statements) bound.Add(BindStatement(g.Statement));
        _fn!.InfersErrors = true;
        _fn.Body = new BoundBlock(statements[0], bound.ToArray());
    }

    public void BindTest(TestDecl decl)
    {
        _fn!.InfersErrors = true;
        _fn.Body = BindBlock(decl.Body);
    }

    public BoundExpr BindFieldDefault(FieldSymbol field)
    {
        var value = BindConverted(field.Decl.Default!, field.Type);
        return value;
    }

    public void BindModuleConstant(ConstSymbol c)
    {
        var decl = c.Decl;
        var declared = decl.Type is { } t ? _types.Resolve(t) : null;
        if (decl.Init is null)
        {
            Report(DiagnosticCodes.MissingInitializer, decl.Name.Span, ["const", decl.Name.Text]);
            c.Type = declared ?? ErrorType.Instance;
            return;
        }
        var value = declared is null ? BindExpr(decl.Init) : BindConverted(decl.Init, declared);
        c.Type = declared ?? DefaultType(value, decl.Init);
        if (declared is null) value = Convert(value, c.Type, decl.Init.Span);
        if (value.Constant is null && !value.Type.IsError)
            Report(DiagnosticCodes.NotConstant, decl.Init.Span, ["a `const`"]);
        c.Value = value.Constant;
        c.Bound = value;
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
            Report(DiagnosticCodes.InvalidExitCode, arg.Value.Span, [], help: "0 means success, and exit codes above 255 do not survive the operating system");
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
                var then = BindBody(i.Then);
                var els = i.Else is { } e ? BindBody(e) : null;
                return new BoundIf(i, cond, then, els);
            }
            case UnlessStmt u:
            {
                var cond = BindCondition(u.Condition);
                return new BoundIf(u, Negate(u.Condition, cond), BindBody(u.Body), null);
            }
            case GuardedStmt g:
            {
                // The condition is evaluated first (spec §7.3); pattern variables it binds stay in scope.
                var cond = BindCondition(g.Condition);
                var body = BindStatement(g.Statement);
                return new BoundIf(g, g.Unless ? Negate(g.Condition, cond) : cond, body, null);
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
            case DeferStmt d:
            {
                _deferDepth++;
                var body = BindBody(d.Body);
                _deferDepth--;
                return new BoundDefer(d, body);
            }
            case ErrdeferStmt e:
                return BindErrdefer(e);
            case ContextStmt c:
                return BindContext(c);
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

    private BoundStmt BindLabeled(LabeledStmt l)
    {
        Naming.CheckValue(_c, Source, l.Label);
        if (_targets.Any(t => t.Label == l.Label.Text))
            Report(DiagnosticCodes.DuplicateDeclaration, l.Label.Span, [l.Label.Text], label: "an enclosing statement has this label");
        var target = new JumpTarget(l.Label.Text, l.Statement is not SwitchStmt);
        if (l.Statement is not (WhileStmt or DoWhileStmt or ForStmt or SwitchStmt))
        {
            Report(DiagnosticCodes.LabelNotOnLoop, l.Label.Span, [l.Label.Text]);
            return BindStatement(l.Statement);
        }
        return BindStatement(l.Statement, target);
    }

    private BoundStmt BindLocalDecl(LocalDecl d)
    {
        var declared = d.Type is { } t ? _types.Resolve(t) : null;
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
        local.IsUsing = d.Using;
        if (kind == LocalKind.Let && !d.Using && Source.GetText(new TextSpan(d.Span.Start, 3)) == "let")
            local.KeywordSpan = new TextSpan(d.Span.Start, 3);
        if (kind == LocalKind.Const) local.Constant = init?.Constant;
        return new BoundLocalDecl(d, local, init);
    }

    private void CheckDisposable(LocalDecl d, TypeSymbol type)
    {
        if (type == BuiltinType.Arena || type is ListType || type.IsError) return;
        Report(DiagnosticCodes.NotDisposable, d.Name.Span, [type.DisplayName],
            help: "`using` is for values that own memory: `Arena` and `List<T>`");
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

    private BoundStmt BindFor(ForStmt f, JumpTarget target)
    {
        PushScope();
        var iterable = BindExpr(f.Iterable);
        TypeSymbol element = iterable.Type switch
        {
            RangeType r => r.Element,
            SpanType s => s.Element,
            ListType l => l.Element,
            Symbols.ArrayType a => a.Element,
            PrimitiveType { Kind: PrimitiveKind.String } => PrimitiveType.Char,
            ErrorType => ErrorType.Instance,
            _ => NotIterable(f.Iterable, iterable.Type),
        };
        if (f.Type is { } t)
        {
            var declared = _types.Resolve(t);
            if (!declared.IsError && !element.IsError && !declared.Equals(element) && Conversions.Implicit(element, declared) is null)
                Report(DiagnosticCodes.TypeMismatch, t.Span, [element.DisplayName, declared.DisplayName]);
            else if (!declared.IsError) element = declared;
        }
        var variable = DeclareLocal(f.Variable, element, f.Kind == BindingKind.Var ? LocalKind.Var : LocalKind.ForVariable);
        _targets.Add(target);
        var body = BindBody(f.Body);
        _targets.RemoveAt(_targets.Count - 1);
        PopScope();
        return new BoundFor(f, variable, iterable, body, target);
    }

    private TypeSymbol NotIterable(Expr syntax, TypeSymbol type)
    {
        Report(DiagnosticCodes.NotIterable, syntax.Span, [type.DisplayName],
            help: "`for` iterates over ranges (`0..n`), spans, lists, arrays and strings");
        return ErrorType.Instance;
    }

    private BoundStmt BindReturn(ReturnStmt r)
    {
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

    private BoundStmt BindErrdefer(ErrdeferStmt e)
    {
        if (_fn is not { Fails: true } && _fn?.Kind is not (FunctionKind.Main or FunctionKind.Test))
            Report(DiagnosticCodes.ErrdeferWithoutFails, e.Span, [],
                help: "an `errdefer` runs only when the function fails; use `defer` here");
        PushScope();
        LocalSymbol? err = null;
        if (e.Error is { } name)
            err = DeclareLocal(name, _fn is null ? ErrorType.Instance : ErrorUnionType.Of(_fn.ErrorSets), LocalKind.Let);
        _deferDepth++;
        var body = BindBody(e.Body);
        _deferDepth--;
        PopScope();
        return new BoundErrdefer(e, err, body);
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
        var value = BindExpr(s.Value);
        var sections = new List<BoundSwitchSection>();
        _targets.Add(target);
        var sawDefault = false;
        foreach (var section in s.Sections)
        {
            PushScope();
            BoundPattern? pattern = null;
            if (section.Label.Pattern is { } p) pattern = BindPattern(p, value.Type);
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
