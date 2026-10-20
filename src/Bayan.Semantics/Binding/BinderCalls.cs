using Bayan.Semantics.Symbols;
using Bayan.Syntax.Ast;
using Bayan.Syntax.Diagnostics;
using Bayan.Syntax.Text;

namespace Bayan.Semantics.Binding;

internal sealed partial class Binder
{
    /// <summary>The call that the enclosing <c>try</c>/<c>catch</c> handles; any other failing call is an error.</summary>
    private CallExpr? _handled;

    private static Expr StripParens(Expr e)
    {
        while (e is ParenExpr p) e = p.Inner;
        return e;
    }

    // ── Calls ────────────────────────────────────────────────────────────────────

    private BoundExpr BindCall(CallExpr c, TypeSymbol? expected)
    {
        var callee = c.Callee is TargetMemberExpr t ? BindTargetMember(t, expected) : BindExprOrType(c.Callee);
        switch (callee)
        {
            case BoundMethodGroup { Function.Builtin: BuiltinFunction.OwnedNew }:
                return BindOwned(c, (expected as OwnedType)?.Inner);
            case BoundMethodGroup { Function.Builtin: BuiltinFunction.UntrustedNew }:
                return BindUntrusted(c, (expected as UntrustedType)?.Inner);
            case BoundMethodGroup { Function.Builtin: BuiltinFunction.Trust }:
                return BindTrust(c);
            case BoundMethodGroup g:
                return BindFunctionCall(c, g.Function, g.Receiver, g.TypeArgs);
            case BoundBuiltinGroup b:
                return BindBuiltinMethodCall(c, b.Receiver, b.Name);
            case BoundTypeRef tr:
                return BindConstruction(c, tr.Referenced);
            case BoundVariantGroup v:
                return new BoundUnionNew(c, v.Variant, BindArguments(c, v.Variant.Fields, null, enforceNames: false, v.Variant.Name));
            case BoundUnionNew { Payload.Length: 0 } unit:
                Report(DiagnosticCodes.VariantHasNoPayload, c.Span, [unit.Variant.Name],
                    help: $"write `{unit.Variant.Union.Name}.{unit.Variant.Name}` without parentheses");
                BindArgumentsForRecovery(c);
                return new BoundErrorExpr(c);
            case BoundErrorExpr:
                BindArgumentsForRecovery(c);
                return new BoundErrorExpr(c);
            default:
                Report(DiagnosticCodes.NotCallable, c.Callee.Span, [callee.Type.DisplayName]);
                BindArgumentsForRecovery(c);
                return new BoundErrorExpr(c);
        }
    }

    private void BindArgumentsForRecovery(CallExpr c)
    {
        foreach (var a in c.Args)
        {
            if (a.Value is InterpolatedExpr || _prebound.Remove(a)) continue;
            BindExprOrType(a.Value);
        }
    }

    /// <summary>Binding the argument of <c>print</c>, which also takes an enum and prints its name.</summary>
    private bool _printArgument;

    /// <summary>Binding an argument of <c>Io.runBatch</c>, a sink where an <c>Untrusted&lt;T&gt;</c> value is BYN2786.</summary>
    private bool _sinkArgument;

    private BoundExpr BindFunctionCall(CallExpr c, FunctionSymbol fn, BoundExpr? receiver, TypeSyntax[]? typeArgs = null)
    {
        if (fn.TypeParameters.Count > 0 || typeArgs is { Length: > 0 })
        {
            if (Instantiate(c, fn, typeArgs ?? []) is not { } instance)
            {
                BindArgumentsForRecovery(c);
                return new BoundErrorExpr(c);
            }
            fn = instance;
        }
        var (outer, outerSink) = (_printArgument, _sinkArgument);
        _printArgument = fn.Builtin == BuiltinFunction.Print;
        _sinkArgument = fn.Builtin == BuiltinFunction.IoRunBatch;
        var args = BindArguments(c, fn.Parameters, fn, enforceNames: fn.Kind != FunctionKind.Builtin);
        (_printArgument, _sinkArgument) = (outer, outerSink);
        if (receiver is not null && fn.HasThis)
            _c.MutationChecks.Add(new MutationCheck(this, fn, receiver));
        if (fn.Fails && !ReferenceEquals(_handled, c))
        {
            Report(DiagnosticCodes.UnhandledFailure, c.Span, [fn.Name],
                help: "propagate the error with `try`, or handle it with `catch`",
                fix: new Fix(new TextSpan(c.Span.Start, 0), "try "));
        }
        return new BoundCall(c, fn, receiver, args, fn.ReturnType);
    }

    /// <summary>Arguments bound early, without an expected type, to infer type arguments from; used in place of binding them again.</summary>
    private readonly Dictionary<Argument, BoundExpr> _prebound = new(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// The instantiation a call of a generic function means: its type arguments are explicit
    /// (<c>pick&lt;i64&gt;(…)</c>) or inferred by unifying parameter types with argument types.
    /// </summary>
    private FunctionSymbol? Instantiate(CallExpr c, FunctionSymbol fn, TypeSyntax[] typeArgs)
    {
        var parameters = fn.TypeParameters;
        if (typeArgs.Length > 0)
        {
            if (typeArgs.Length != parameters.Count)
            {
                Report(DiagnosticCodes.GenericArgumentCount, c.Callee.Span, [fn.Name, parameters.Count, typeArgs.Length],
                    help: parameters.Count == 0 ? $"`{fn.Name}` is not generic; remove the type arguments" : null);
                return null;
            }
            var explicitArgs = typeArgs.Select(t => _types.Resolve(t)).ToArray();
            return explicitArgs.Any(a => a.IsError) ? null : _c.InstantiateFunction(fn, explicitArgs, Source, c.Span);
        }
        var found = new Dictionary<TypeParameterType, TypeSymbol>();
        var conflict = false;
        var positional = 0;
        var sawNamed = false;
        foreach (var arg in c.Args)
        {
            ParameterSymbol? p;
            if (arg.Name is { } n)
            {
                sawNamed = true;
                p = fn.Parameters.FirstOrDefault(x => x.Name == n.Text);
            }
            else
                p = sawNamed || positional >= fn.Parameters.Count ? null : fn.Parameters[positional++];
            if (p is null || !TypeParameterType.Occurs(p.Type) || _prebound.ContainsKey(arg)) continue;
            var value = BindExpr(arg.Value, null);
            _prebound[arg] = value;
            conflict |= !Unify(p.Type, value.Type, found, fn, arg.Value.Span);
        }
        if (conflict) return null;
        var args = new TypeSymbol[parameters.Count];
        for (var i = 0; i < parameters.Count; i++)
        {
            if (!found.TryGetValue(parameters[i], out var t))
            {
                Report(DiagnosticCodes.CannotInferTypeArgument, c.Callee.Span, [parameters[i].Name, fn.Name],
                    help: $"pass an argument it can be read from, or write it out: `{fn.Name}<{string.Join(", ", parameters.Select(x => x.Name))}>(…)`");
                return null;
            }
            args[i] = t;
        }
        return _c.InstantiateFunction(fn, args, Source, c.Span);
    }

    /// <summary>Matches a parameter type against an argument type, binding the type parameters of <paramref name="fn"/>; false on a conflict (reported).</summary>
    private bool Unify(TypeSymbol p, TypeSymbol a, Dictionary<TypeParameterType, TypeSymbol> found, FunctionSymbol fn, TextSpan span)
    {
        if (a.IsError || a.IsNever || a is NullType) return true;
        switch (p)
        {
            case TypeParameterType tp when fn.TypeParameters.Contains(tp):
                if (!found.TryGetValue(tp, out var previous))
                {
                    found[tp] = a;
                    return true;
                }
                if (Conversions.Common(previous, a) is { } common)
                {
                    found[tp] = common;
                    return true;
                }
                Report(DiagnosticCodes.ConflictingTypeArgument, span, [tp.Name, fn.Name, previous.DisplayName, a.DisplayName],
                    help: $"make the arguments agree, or write it out: `{fn.Name}<…>(…)`");
                return false;
            case Symbols.OptionalType po:
                return Unify(po.Inner, a is Symbols.OptionalType ao ? ao.Inner : a, found, fn, span);
            case SpanType ps:
                return a switch
                {
                    SpanType sa => Unify(ps.Element, sa.Element, found, fn, span),
                    ListType la => Unify(ps.Element, la.Element, found, fn, span),
                    Symbols.ArrayType aa => Unify(ps.Element, aa.Element, found, fn, span),
                    _ => true,
                };
            case ListType pl:
                return a is not ListType al || Unify(pl.Element, al.Element, found, fn, span);
            case MapType pm:
                return a is not MapType am || Unify(pm.KeyType, am.KeyType, found, fn, span) && Unify(pm.ValueType, am.ValueType, found, fn, span);
            case Symbols.ArrayType pa:
                return a is not Symbols.ArrayType aa2 || Unify(pa.Element, aa2.Element, found, fn, span);
            case Symbols.TupleType pt:
                return a is not Symbols.TupleType at || at.Fields.Count != pt.Fields.Count
                    || pt.Fields.Zip(at.Fields).All(f => Unify(f.First.Type, f.Second.Type, found, fn, span));
            case ResultType pr:
                return a is not ResultType ar || Unify(pr.Value, ar.Value, found, fn, span);
            case StructSymbol { Template: { } ps2 } pi:
                return a is not StructSymbol ai || ai.Template != ps2
                    || pi.TypeArguments.Zip(ai.TypeArguments).All(x => Unify(x.First, x.Second, found, fn, span));
            case UnionSymbol { Template: { } pu } ui:
                return a is not UnionSymbol au || au.Template != pu
                    || ui.TypeArguments.Zip(au.TypeArguments).All(x => Unify(x.First, x.Second, found, fn, span));
            default:
                return true;
        }
    }

    private BoundExpr[] BindArguments(CallExpr c, IReadOnlyList<ParameterSymbol> ps, FunctionSymbol? fn, bool enforceNames, string? displayName = null)
    {
        var name = displayName ?? fn?.Name ?? "?";
        // An instantiation follows its template's naming rule, so `T a, T b` asks for names at every type.
        var rule = fn?.Template?.Parameters ?? ps;
        var result = new BoundExpr?[ps.Count];
        var sawNamed = false;
        var positional = 0;
        var mismatched = false;
        foreach (var arg in c.Args)
        {
            ParameterSymbol? p;
            if (arg.Name is { } n)
            {
                sawNamed = true;
                p = ps.FirstOrDefault(x => x.Name == n.Text);
                if (p is null)
                {
                    var s = Suggest(n.Text, ps.Select(x => x.Name));
                    Report(DiagnosticCodes.UnknownParameter, n.Span, [name, n.Text],
                        help: s is null ? null : $"did you mean `{s}`?", fix: s is null ? null : new Fix(n.Span, s));
                    BindArgumentValue(arg, null);
                    mismatched = true;
                    continue;
                }
            }
            else
            {
                if (sawNamed)
                {
                    Report(DiagnosticCodes.PositionalAfterNamed, arg.Span, []);
                    BindArgumentValue(arg, null);
                    mismatched = true;
                    continue;
                }
                if (positional >= ps.Count)
                {
                    Report(DiagnosticCodes.TooManyArguments, arg.Span, [name, ps.Count]);
                    BindArgumentValue(arg, null);
                    positional++;
                    continue;
                }
                p = ps[positional++];
                if (enforceNames && MustBeNamed(rule[p.Ordinal], rule, name) is { } reason)
                    Report(DiagnosticCodes.ArgumentMustBeNamed, arg.Value.Span, [p.Name], help: reason,
                        fix: new Fix(new TextSpan(arg.Span.Start, 0), p.Name + ": "));
            }
            if (result[p.Ordinal] is not null)
            {
                Report(DiagnosticCodes.DuplicateArgument, arg.Span, [p.Name]);
                _prebound.Remove(arg);
                continue;
            }
            result[p.Ordinal] = BindArgument(arg, p);
        }
        var missing = new List<string>();
        for (var i = 0; i < ps.Count; i++)
        {
            if (result[i] is not null) continue;
            var p = ps[i];
            if (p.Syntax?.Default is not null && fn is not null)
            {
                result[i] = _c.ParameterDefault(fn, p) ?? new BoundErrorExpr(c);
                continue;
            }
            missing.Add(p.Name);
            result[i] = new BoundErrorExpr(c);
        }
        if (missing.Count > 0 && !mismatched)
            Report(DiagnosticCodes.MissingArgument, c.Span, [string.Join("`, `", missing), name]);
        return result!;
    }

    private static string? MustBeNamed(ParameterSymbol p, IReadOnlyList<ParameterSymbol> ps, string fnName)
    {
        if (p.Type.IsBool) return $"a `bool` argument says nothing at the call site; write `{p.Name}: …`";
        if (!p.Type.IsError && ps.Count(q => q.Type.Equals(p.Type)) > 1)
            return $"`{fnName}` has several `{p.Type.DisplayName}` parameters; name them so they cannot be swapped";
        return null;
    }

    private BoundExpr BindArgument(Argument arg, ParameterSymbol p)
    {
        var wanted = p.Modifier switch { ParamModifier.Ref => ArgModifier.Ref, ParamModifier.Out => ArgModifier.Out, _ => ArgModifier.None };
        // `move x` hands a value to a plain or `move` parameter.
        var given = arg.Modifier == ArgModifier.In || (arg.Modifier == ArgModifier.Move && wanted == ArgModifier.None) ? ArgModifier.None : arg.Modifier;
        if (given != wanted)
        {
            var keyword = wanted == ArgModifier.None ? null : wanted.ToString().ToLowerInvariant();
            Report(DiagnosticCodes.ArgumentModifierMismatch, arg.Span, [p.Name, keyword is null ? "a plain value" : $"`{keyword}`"],
                fix: keyword is null ? null : new Fix(new TextSpan(arg.Value.Span.Start, 0), keyword + " "));
        }
        if (wanted is ArgModifier.Ref or ArgModifier.Out)
        {
            var place = _prebound.Remove(arg, out var early) ? early : BindExpr(arg.Value, p.Type);
            if (place.Type.IsError) return place;
            if (!place.Type.Equals(p.Type))
            {
                Report(DiagnosticCodes.TypeMismatch, arg.Value.Span, [place.Type.DisplayName, p.Type.DisplayName],
                    help: "a `ref` or `out` argument must have exactly the parameter's type");
                return new BoundErrorExpr(arg.Value);
            }
            CheckWritable(place, arg.Value.Span, $"it is passed as `{(wanted == ArgModifier.Ref ? "ref" : "out")}`");
            return place;
        }
        return BindArgumentValue(arg, p.Type);
    }

    private BoundExpr BindArgumentValue(Argument arg, TypeSymbol? type)
    {
        var value = _prebound.Remove(arg, out var early) ? early : BindExpr(arg.Value, type);
        if (arg.Modifier == ArgModifier.Move) value = BindMove(arg, value);
        return type is null ? value : Convert(value, type, arg.Value.Span, argument: true);
    }

    private BoundExpr BindConstruction(CallExpr c, TypeSymbol type)
    {
        switch (type)
        {
            case StructSymbol s when s.Constructors.Count > 0:
            {
                var ctor = s.Constructors[0];
                CheckAccess(ctor.Access, s, s.Module, new Name(c.Callee.Span, s.Name));
                var args = BindArguments(c, ctor.Parameters, ctor, enforceNames: true);
                return new BoundCall(c, ctor, null, args, s);
            }
            case StructSymbol s:
            {
                if (c.Args.Length > 0)
                {
                    Report(DiagnosticCodes.NoConstructor, c.Callee.Span, [s.Name],
                        help: $"set the fields with a struct literal: `{s.Name} {{ {string.Join(", ", s.Fields.Take(2).Select(f => f.Name + " = …"))} }}`");
                    BindArgumentsForRecovery(c);
                    return new BoundErrorExpr(c);
                }
                var missing = s.Fields.Where(f => f.IsRequired || !f.HasDefault).Select(f => f.Name).ToList();
                if (missing.Count > 0)
                {
                    Report(DiagnosticCodes.MissingFields, c.Span, [s.Name, string.Join("`, `", missing)],
                        help: $"set {(missing.Count == 1 ? "it" : "them")}: `{s.Name} {{ {string.Join(", ", missing.Select(m => m + " = …"))} }}`");
                    return new BoundErrorExpr(c);
                }
                return new BoundStructLiteral(c, s, s.Fields.Select(f => _c.FieldDefault(s, f, Source, c.Span) ?? new BoundErrorExpr(c)).ToArray());
            }
            case ListType l:
                if (c.Args.Length > 0)
                {
                    Report(DiagnosticCodes.TooManyArguments, c.Args[0].Span, [l.DisplayName, 0]);
                    BindArgumentsForRecovery(c);
                }
                return new BoundCollection(c, l, []);
            case MapType m:
                if (c.Args.Length > 0)
                {
                    Report(DiagnosticCodes.TooManyArguments, c.Args[0].Span, [m.DisplayName, 0]);
                    BindArgumentsForRecovery(c);
                }
                return new BoundCollection(c, m, []);
            case OwnedType o:
                return BindOwned(c, o.Inner);
            case BuiltinType { Kind: BuiltinKind.Arena }:
                return new BoundCall(c, _c.Builtins.ArenaNew, null, BindArguments(c, _c.Builtins.ArenaNew.Parameters, _c.Builtins.ArenaNew, false, "Arena"), type);
            case BuiltinType { Kind: BuiltinKind.StrBuf } when c.Args.Length == 0:
                return new BoundCall(c, _c.Builtins.StrBufGrow, null, [], type);
            case BuiltinType { Kind: BuiltinKind.StrBuf }:
                return new BoundCall(c, _c.Builtins.StrBufNew, null, BindArguments(c, _c.Builtins.StrBufNew.Parameters, _c.Builtins.StrBufNew, false, "StrBuf"), type);
            case ErrorType:
                BindArgumentsForRecovery(c);
                return new BoundErrorExpr(c);
            default:
                Report(DiagnosticCodes.NotCallable, c.Callee.Span, [type.DisplayName],
                    help: type.IsNumeric || type == PrimitiveType.Char ? $"convert with a cast: `({type.DisplayName})x`" : null);
                BindArgumentsForRecovery(c);
                return new BoundErrorExpr(c);
        }
    }

    /// <summary><c>Owned(value)</c> or <c>Owned&lt;T&gt;(value)</c>: the value's type comes from <paramref name="inner"/>, else from the argument.</summary>
    private BoundExpr BindOwned(CallExpr c, TypeSymbol? inner)
    {
        BoundExpr value;
        if (inner is null && c.Args is [{ Name: null, Modifier: ArgModifier.None } arg])
        {
            value = BindExpr(arg.Value);
            inner = DefaultType(value, arg.Value);
            if (inner.IsError || inner.IsNever) return value;
            value = Convert(value, inner, arg.Value.Span, argument: true);
        }
        else
        {
            var ps = new[] { new ParameterSymbol("value", null, inner ?? ErrorType.Instance, ParamModifier.None, 0) };
            value = BindArguments(c, ps, null, enforceNames: false, "Owned")[0];
            if (inner is null || inner.IsError) return new BoundErrorExpr(c);
        }
        return new BoundCall(c, _c.Builtins.OwnedNew, null, [value], new OwnedType(inner));
    }

    /// <summary><c>Untrusted(value)</c>: the value's type comes from the argument, or from the expected <c>Untrusted&lt;T&gt;</c>.</summary>
    private BoundExpr BindUntrusted(CallExpr c, TypeSymbol? inner)
    {
        BoundExpr value;
        if (inner is null && c.Args is [{ Name: null, Modifier: ArgModifier.None } arg])
        {
            value = BindExpr(arg.Value);
            inner = DefaultType(value, arg.Value);
            if (inner.IsError || inner.IsNever) return value;
            value = Convert(value, inner, arg.Value.Span, argument: true);
        }
        else
        {
            var ps = new[] { new ParameterSymbol("value", null, inner ?? ErrorType.Instance, ParamModifier.None, 0) };
            value = BindArguments(c, ps, null, enforceNames: false, "Untrusted")[0];
            if (inner is null || inner.IsError) return new BoundErrorExpr(c);
        }
        return new BoundCall(c, _c.Builtins.UntrustedNew, null, [value], new UntrustedType(inner));
    }

    /// <summary><c>trust(value, reason: "…")</c>: <c>Untrusted&lt;T&gt;</c> to <c>T</c>, with a non-empty literal reason.</summary>
    private BoundExpr BindTrust(CallExpr c)
    {
        var value = c.Args.Length > 0 && c.Args[0].Name is null ? BindExpr(c.Args[0].Value) : null;
        if (value is null || c.Args.Length != 2 || c.Args[1].Name?.Text != "reason")
        {
            BindArgumentsForRecovery(c);
            Report(DiagnosticCodes.TrustReason, c.Span, [], help: "write `trust(x, reason: \"why this value is safe\")`");
            return new BoundErrorExpr(c);
        }
        var reason = c.Args[1].Value;
        BindExpr(reason);
        if (reason is not LiteralExpr { Kind: LiteralKind.String, Text.Length: > 0 })
            Report(DiagnosticCodes.TrustReason, reason.Span, [], help: "explain why the value is safe, in a string literal such as `reason: \"validated upstream\"`");
        if (value.Type.IsError) return value;
        if (value.Type is not UntrustedType u)
        {
            Convert(value, new UntrustedType(value.Type), c.Args[0].Value.Span, argument: true);
            return new BoundErrorExpr(c);
        }
        return new BoundCall(c, _c.Builtins.Trust, null, [value], u.Inner);
    }

    private BoundExpr BindBuiltinMethodCall(CallExpr c, BoundExpr receiver, string name)
    {
        var m = BuiltinMembers.Method(receiver.Type, name)!;
        var ps = m.Parameters.Select((p, i) => new ParameterSymbol(p.Name, null, p.Type, ParamModifier.None, i)).ToList();
        var args = BindArguments(c, ps, null, enforceNames: false, displayName: name);
        if (m.Mutating) CheckWritable(receiver, receiver.Syntax.Span, $"`{name}` modifies it");
        if (m.Member == BuiltinMember.MapSlot && !IsDefaultable(m.Returns))
            Report(DiagnosticCodes.MapSlotNoDefault, c.Span, [m.Returns.DisplayName],
                help: "read with `map[key]` or `map.getOr(key, fallback)`, and write with `map[key] = value`");
        if (m.Member is BuiltinMember.ListCopy or BuiltinMember.MapCopy)
        {
            var held = m.Member == BuiltinMember.ListCopy
                ? TypeFacts.UniquePart(((ListType)receiver.Type).Element)
                : TypeFacts.UniquePart(((MapType)receiver.Type).KeyType) ?? TypeFacts.UniquePart(((MapType)receiver.Type).ValueType);
            if (held is not null)
                Report(DiagnosticCodes.CopyUniqueElement, c.Span, [receiver.Type.DisplayName, held.DisplayName],
                    help: "build the copy with a loop that copies each element, or share the original by `ref`");
        }
        var returns = m.Returns is SpanType span && receiver is BoundStaticArray ? new SpanType(span.Element, readOnly: true)
            : m.Member == BuiltinMember.IoReadAll ? new ResultType(m.Returns, _c.Builtins.IoError)
            : m.Returns;
        return new BoundBuiltinCall(c, m.Member, receiver, args, returns);
    }

    /// <summary>Whether <c>map.slot</c> can insert a default <paramref name="t"/>: numbers, <c>bool</c>, <c>char</c>, <c>string</c>, optionals and structs whose fields all have defaults.</summary>
    private static bool IsDefaultable(TypeSymbol t) => t switch
    {
        PrimitiveType { Kind: PrimitiveKind.Char } => true,
        OptionalType => true,
        StructSymbol s => s.IsDefaultable,
        _ => t.IsNumeric || t.IsBool || t.IsString,
    };

    // ── Ownership ────────────────────────────────────────────────────────────────

    /// <summary><c>move x</c> (or a <c>move</c> argument): only a local or a <c>move</c> parameter can be moved (spec §9.3).</summary>
    private BoundExpr BindMove(Node syntax, BoundExpr operand)
    {
        if (operand.Type.IsError) return operand;
        switch (operand)
        {
            case BoundLocal { Local: { BorrowsSubject: true } l } when TypeFacts.IsUnique(l.Type):
                Report(DiagnosticCodes.MutateBorrow, operand.Syntax.Span, [l.Name], label: "moving it would take the value out of what it borrows from",
                    help: "move or copy the value it was bound to instead");
                return new BoundMove(syntax, operand);
            case BoundLocal { Local: { Kind: LocalKind.Let or LocalKind.Var } or ParameterSymbol { Modifier: ParamModifier.Move } }:
            case BoundLocal { Local.BorrowsSubject: true }:
                return new BoundMove(syntax, operand);
        }
        var (what, help) = operand switch
        {
            BoundLocal { Local: ParameterSymbol { IsByRef: true } p } => ($"`{p.Modifier.ToString().ToLowerInvariant()}` parameter `{p.Name}`", "the caller still owns it; copy it with `.copy(to: alloc)`"),
            BoundLocal { Local: ParameterSymbol p } => ($"parameter `{p.Name}`", $"a plain parameter is borrowed from the caller; declare it `move {p.Name}` to take ownership"),
            BoundLocal { Local: var l } => ($"`{l.Name}`", "only a local variable or a `move` parameter can be moved"),
            BoundField f => ($"field `{f.Field.Name}`", "its owner keeps it; move the whole value, or copy the field with `.copy(to: alloc)`"),
            BoundIndex or BoundBuiltinCall { Member: BuiltinMember.MapIndex } => ("an element", "the collection keeps it; copy the element with `.copy(to: alloc)`"),
            BoundTupleElement => ("a tuple element", "move the whole tuple instead"),
            BoundThis => ("`this`", "a method borrows `this`"),
            BoundCall or BoundBuiltinCall or BoundStructLiteral or BoundCollection or BoundLiteral => ("a fresh value", "a fresh value needs no `move`; remove it"),
            _ => ("this expression", "only a local variable or a `move` parameter can be moved"),
        };
        Report(DiagnosticCodes.CannotMove, operand.Syntax.Span, [what], help: help);
        return new BoundMove(syntax, operand);
    }

    // ── Failure handling ─────────────────────────────────────────────────────────

    private BoundExpr BindTry(TryExpr t, TypeSymbol? expected)
    {
        if (_deferDepth > 0)
            Report(DiagnosticCodes.TryInDefer, t.Span, [],
                help: "cleanup cannot fail; handle the error here with `catch`");
        var inner = StripParens(t.Operand);
        if (inner is not CallExpr call)
        {
            var v = BindExpr(t.Operand, expected);
            if (!v.Type.IsError) Report(DiagnosticCodes.NothingToHandle, t.Operand.Span, ["try"]);
            return v;
        }
        var saved = _handled;
        _handled = call;
        var operand = BindExpr(call, expected);
        _handled = saved;
        if (operand is not BoundCall { Function.Fails: true })
        {
            if (!operand.Type.IsError)
                Report(DiagnosticCodes.NothingToHandle, t.Operand.Span, ["try"], fix: new Fix(TextSpan.FromBounds(t.Span.Start, t.Operand.Span.Start), ""));
            return operand;
        }
        return new BoundTry(t, operand, operand.Type);
    }

    private BoundExpr BindCatch(CatchExpr c, TypeSymbol? expected)
    {
        var inner = StripParens(c.Operand);
        BoundExpr operand;
        if (inner is CallExpr call)
        {
            var saved = _handled;
            _handled = call;
            operand = BindExpr(call, expected);
            _handled = saved;
        }
        else
        {
            operand = BindExpr(c.Operand, expected);
        }
        if (operand is not BoundCall { Function.Fails: true } bc)
        {
            if (!operand.Type.IsError) Report(DiagnosticCodes.NothingToHandle, c.Operand.Span, ["catch"]);
            PushScope();
            if (c.Binding is { } b0) DeclareLocal(b0, ErrorType.Instance, LocalKind.Let);
            BindExpr(c.Handler);
            PopScope();
            return new BoundErrorExpr(c);
        }
        var sets = _c.ErrorSetsOf(bc.Function);
        var errorType = ErrorUnionType.Of(sets);
        PushScope();
        LocalSymbol? binding = null;
        ErrorVariantSymbol? variant = null;
        if (c.Binding is { } b) binding = DeclareLocal(b, errorType, LocalKind.Let);
        if (c.Variant is { } v)
        {
            var bound = BindExpr(v, errorType);
            if (bound is BoundErrorVariant ev)
            {
                if (sets.Count > 0 && !sets.Contains(ev.Variant.Set))
                    Report(DiagnosticCodes.UnknownVariant, v.Span, [ev.Variant.Name, errorType.DisplayName],
                        help: $"`{bc.Function.Name}` fails with {string.Join(", ", sets.Select(s => "`" + s.Name + "`"))}");
                else variant = ev.Variant;
            }
        }
        var resultType = operand.Type;
        BoundExpr handler;
        if (resultType.IsVoid)
        {
            handler = BindExpr(c.Handler);
            if (!handler.Type.IsVoid && !handler.Type.IsNever && !handler.Type.IsError)
                Report(DiagnosticCodes.TypeMismatch, c.Handler.Span, [handler.Type.DisplayName, "void"],
                    help: $"`{bc.Function.Name}` returns nothing, so the handler cannot produce a value");
        }
        else
        {
            handler = BindExpr(c.Handler, resultType);
            if (!handler.Type.IsNever) handler = Convert(handler, resultType, c.Handler.Span);
        }
        PopScope();
        return new BoundCatch(c, operand, binding, variant, handler, resultType);
    }

    // ── switch expressions ───────────────────────────────────────────────────────

    private BoundExpr BindSwitchExpr(SwitchExpr s, TypeSymbol? expected)
    {
        var value = BindExpr(s.Value);
        var arms = new List<BoundSwitchArm>();
        foreach (var arm in s.Arms)
        {
            PushScope();
            var pattern = BindPattern(arm.Pattern, value.Type);
            var guard = arm.Guard is { } g ? BindCondition(g) : null;
            var result = BindExpr(arm.Value, expected);
            PopScope();
            arms.Add(new BoundSwitchArm(arm, pattern, guard, result));
        }
        TypeSymbol? type = expected;
        if (type is null || arms.Any(a => !a.Value.Type.IsError && Conversions.Implicit(a.Value.Type, type) is null))
        {
            type = null;
            foreach (var a in arms)
            {
                if (a.Value.Type.IsNever || a.Value.Type.IsError) continue;
                type = type is null ? a.Value.Type : Conversions.Common(type, a.Value.Type);
                if (type is null)
                {
                    Report(DiagnosticCodes.BranchTypeMismatch, a.Value.Syntax.Span, [arms[0].Value.Type.DisplayName, a.Value.Type.DisplayName]);
                    return new BoundErrorExpr(s);
                }
            }
            type ??= arms.All(a => a.Value.Type.IsNever) && arms.Count > 0 ? PrimitiveType.Never : ErrorType.Instance;
        }
        if (type is NullType)
        {
            Report(DiagnosticCodes.CannotInferType, s.Span, ["null"]);
            type = ErrorType.Instance;
        }
        for (var i = 0; i < arms.Count; i++)
            arms[i] = arms[i] with { Value = Convert(arms[i].Value, type, ((SwitchArm)arms[i].Syntax).Value.Span) };
        CheckExhaustive(s.Value.Span, value.Type, arms.Select(a => ((BoundPattern?)a.Pattern, a.Guard)).ToList(), statement: false);
        return new BoundSwitchExpr(s, value, arms.ToArray(), type);
    }

    // ── Patterns ─────────────────────────────────────────────────────────────────

    private static TypeSymbol Unwrap(TypeSymbol t) => t is OptionalType o ? o.Inner : t;

    private BoundPattern BindPattern(Pattern p, TypeSymbol input)
    {
        switch (p)
        {
            case DiscardPattern:
                return new BoundBindPattern(p, input, null);
            case VarPattern v:
            {
                var local = DeclareLocal(v.Name, input, v.IsLet ? LocalKind.PatternVariable : LocalKind.Var);
                local.BorrowsSubject = true;
                if (!v.IsLet) local.KeywordSpan = new TextSpan(v.Span.Start, 3);
                return new BoundBindPattern(p, input, local);
            }
            case ConstantPattern c:
                return BindConstantPattern(c, input);
            case RelationalPattern r:
            {
                var target = Unwrap(input);
                var value = BindConverted(r.Value, target);
                if (!target.IsError && !(target.IsNumeric || target == PrimitiveType.Char || target.IsString))
                    Report(DiagnosticCodes.BadBinaryOperands, p.Span, [BinaryText(r.Op), target.DisplayName, value.Type.DisplayName]);
                else RequireConstant(value, r.Value.Span);
                return new BoundRelationalPattern(p, input, r.Op, value);
            }
            case NotPattern n:
                return new BoundNotPattern(p, input, BindPattern(n.Operand, input));
            case BinaryPattern b:
            {
                var left = BindPattern(b.Left, input);
                var right = BindPattern(b.Right, input);
                if (!b.IsAnd && (HasBindings(left) || HasBindings(right)))
                    Report(DiagnosticCodes.Unsupported, p.Span, ["variables inside `or` patterns"]);
                return new BoundBinaryPattern(p, input, b.IsAnd, left, right);
            }
            case PropertyPattern pp:
                return BindPropertyPattern(pp, input);
            case PositionalPattern { Type: null } pos when Unwrap(input) is Symbols.TupleType tt:
            {
                if (pos.Elements.Length != tt.Fields.Count)
                {
                    Report(DiagnosticCodes.PatternMismatch, p.Span, [$"{pos.Elements.Length} elements", tt.DisplayName]);
                    return new BoundBindPattern(p, input, null);
                }
                var subs = new BoundSubpattern[tt.Fields.Count];
                for (var i = 0; i < subs.Length; i++)
                {
                    var step = new BoundMemberStep(null, null, i, null, tt.Fields[i].Type);
                    subs[i] = new BoundSubpattern([step], BindPattern(pos.Elements[i], tt.Fields[i].Type));
                }
                return new BoundPropertyPattern(p, input, tt, subs, null);
            }
            case VariantPattern vp:
                return BindUnionPattern(vp, input, null, vp.Variant.Member, vp.Elements);
            case PositionalPattern { Type: NamedType { Path.Length: > 1, Args.Length: 0 } nt } pos:
            {
                // `Option2.Some(let v)` on an `Option2<i64>` names the generic union without its type arguments.
                var owner = nt.Path.Length == 2 && Module.Types.TryGetValue(nt.Path[0].Text, out var named) && named is UnionSymbol { TypeParameters.Count: > 0 }
                    && Unwrap(input) is UnionSymbol u && (u == named || u.Template == named)
                    ? u
                    : _types.Resolve(new NamedType(TextSpan.FromBounds(nt.Path[0].Span.Start, nt.Path[^2].Span.End), nt.Path[..^1], []));
                if (owner is UnionSymbol or ErrorType)
                    return BindUnionPattern(pos, input, owner, nt.Path[^1], pos.Elements);
                Report(DiagnosticCodes.Unsupported, p.Span, ["deconstruction patterns"]);
                return new BoundBindPattern(p, input, null);
            }
            case PositionalPattern pos:
                Report(DiagnosticCodes.Unsupported, p.Span, [pos.Type is null ? $"positional patterns on `{input.DisplayName}`" : "deconstruction patterns"]);
                return new BoundBindPattern(p, input, null);
            default:
                Report(DiagnosticCodes.Unsupported, p.Span, ["list patterns"]);
                return new BoundBindPattern(p, input, null);
        }
    }

    private static bool HasBindings(BoundPattern p) => p switch
    {
        BoundBindPattern b => b.Local is not null,
        BoundNotPattern n => HasBindings(n.Operand),
        BoundBinaryPattern b => HasBindings(b.Left) || HasBindings(b.Right),
        BoundPropertyPattern pp => pp.Designation is not null || pp.Subpatterns.Any(s => HasBindings(s.Pattern)),
        BoundUnionPattern up => up.Subpatterns.Any(HasBindings),
        _ => false,
    };

    /// <summary>
    /// <c>.Circle(let r)</c> or <c>Shape.Circle(let r)</c>; <paramref name="owner"/> is the written union, if any.
    /// Elements are null for a unit variant written as a constant (<c>.None</c>).
    /// </summary>
    private BoundPattern BindUnionPattern(Pattern p, TypeSymbol input, TypeSymbol? owner, Name name, Pattern[] elements)
    {
        var target = Unwrap(input);
        UnionVariantSymbol? variant = null;
        if (owner is not null && !owner.IsError && !target.IsError && !owner.Equals(target))
            Report(DiagnosticCodes.PatternMismatch, p.Span, [$"`{owner.DisplayName}.{name.Text}`", target.DisplayName]);
        else if ((owner ?? target) is UnionSymbol u)
        {
            variant = u.Find(name.Text);
            if (variant is null)
                Report(DiagnosticCodes.UnknownUnionVariant, name.Span, [name.Text, u.Name],
                    help: Suggest(name.Text, u.Variants.Select(v => v.Name)) is { } close ? $"did you mean `{close}`?" : null);
            else if (variant.Fields.Count == 0)
                Report(DiagnosticCodes.VariantHasNoPayload, p.Span, [variant.Name], help: $"match it with `.{variant.Name}`");
            else if (elements.Length != variant.Fields.Count)
                Report(DiagnosticCodes.VariantPatternArity, p.Span, [variant.Name, variant.Fields.Count, elements.Length],
                    help: $"list every field: `.{variant.Name}({string.Join(", ", variant.Fields.Select(f => "let " + f.Name))})`, or `_` to skip one");
        }
        else if (!target.IsError && (owner ?? target) is not ErrorType)
            Report(DiagnosticCodes.PatternMismatch, p.Span, [$"`.{name.Text}(…)`", target.DisplayName]);
        var ok = variant is not null && variant.Fields.Count == elements.Length && elements.Length > 0;
        var subs = new BoundPattern[elements.Length];
        for (var i = 0; i < subs.Length; i++)
            subs[i] = BindPattern(elements[i], ok ? variant!.Fields[i].Type : ErrorType.Instance);
        return ok ? new BoundUnionPattern(p, input, variant!, subs) : new BoundBindPattern(p, input, null);
    }

    private void RequireConstant(BoundExpr value, TextSpan span)
    {
        if (value.Constant is null && value is not (BoundErrorExpr or BoundErrorVariant or BoundEnumMember or BoundDefault))
            Report(DiagnosticCodes.NotConstant, span, ["a pattern"], help: "bind it with `var x` and compare in a `when` guard");
    }

    private BoundPattern BindConstantPattern(ConstantPattern c, TypeSymbol input)
    {
        var target = Unwrap(input);
        if (c.Value is RangeExpr { Start: not null, End: not null } r)
        {
            var start = BindConverted(r.Start, target);
            var end = BindConverted(r.End, target);
            if (!target.IsError && !(target.IsInteger || target == PrimitiveType.Char))
                Report(DiagnosticCodes.PatternMismatch, c.Span, ["a range", target.DisplayName]);
            RequireConstant(start, r.Start.Span);
            RequireConstant(end, r.End.Span);
            return new BoundRangePattern(c, input, start, end, r.Inclusive);
        }
        if (c.Value is LiteralExpr { Kind: LiteralKind.Null })
        {
            if (input is not OptionalType && !input.IsError)
                Report(DiagnosticCodes.PatternMismatch, c.Span, ["`null`", input.DisplayName]);
            return new BoundConstantPattern(c, input, new BoundDefault(c.Value, input));
        }
        var value = BindExpr(c.Value, target);
        if (value is BoundUnionNew { Payload.Length: 0 } unit && (target.IsError || unit.Type.Equals(target)))
            return new BoundUnionPattern(c, input, unit.Variant, []);
        if (value is BoundErrorVariant ev && target is ErrorUnionType u && u.Sets.Count > 0 && !u.Sets.Contains(ev.Variant.Set))
            Report(DiagnosticCodes.PatternMismatch, c.Span, [$"`{ev.Variant.Set.Name}.{ev.Variant.Name}`", target.DisplayName]);
        else if (!(value is BoundErrorVariant && target is ErrorUnionType))
            value = Convert(value, target, c.Value.Span);
        RequireConstant(value, c.Value.Span);
        return new BoundConstantPattern(c, input, value);
    }

    private BoundPattern BindPropertyPattern(PropertyPattern pp, TypeSymbol input)
    {
        var valueType = Unwrap(input);
        if (pp.Type is { } t)
        {
            var declared = _types.Resolve(t);
            if (!declared.IsError && !valueType.IsError && !declared.Equals(valueType))
                Report(DiagnosticCodes.PatternMismatch, t.Span, [$"`{declared.DisplayName}`", valueType.DisplayName],
                    help: "type patterns apply only to unions and `dyn` values, which arrive in a later version");
        }
        var subs = new List<BoundSubpattern>();
        foreach (var sub in pp.Properties)
        {
            var steps = new List<BoundMemberStep>();
            var current = valueType;
            var ok = true;
            foreach (var name in sub.Path)
            {
                if (ResolveStep(current, name) is not { } step)
                {
                    ok = false;
                    break;
                }
                steps.Add(step);
                current = step.Type;
            }
            var pattern = BindPattern(sub.Pattern, ok ? current : ErrorType.Instance);
            if (ok) subs.Add(new BoundSubpattern(steps.ToArray(), pattern));
        }
        var designation = pp.Designation is { } d ? DeclareLocal(d, valueType, LocalKind.PatternVariable) : null;
        if (designation is not null) designation.BorrowsSubject = true;
        return new BoundPropertyPattern(pp, input, valueType, subs.ToArray(), designation);
    }

    private BoundMemberStep? ResolveStep(TypeSymbol type, Name name)
    {
        switch (type)
        {
            case ErrorType:
                return null;
            case StructSymbol s:
                if (s.FindField(name.Text) is { } f)
                {
                    CheckAccess(f.Access, s, s.Module, name);
                    return new BoundMemberStep(f, null, -1, null, f.Type);
                }
                if (s.FindProperty(name.Text) is { } p)
                {
                    CheckAccess(p.Access, s, s.Module, name);
                    return new BoundMemberStep(null, p, -1, null, p.Type);
                }
                break;
            case Symbols.TupleType tt when tt.IndexOf(name.Text) is var i and >= 0:
                return new BoundMemberStep(null, null, i, null, tt.Fields[i].Type);
            case OptionalType o:
                Report(DiagnosticCodes.MemberOfOptional, name.Span, [name.Text, o.DisplayName],
                    help: "match the value first: `{ } v`");
                return null;
        }
        if (BuiltinMembers.Property(type, name.Text) is { } b)
            return new BoundMemberStep(null, null, -1, b.Member, b.Type);
        Report(DiagnosticCodes.UnknownMember, name.Span, [name.Text, type.DisplayName], help: MemberSuggestion(type, name.Text));
        return null;
    }

    // ── Exhaustiveness ───────────────────────────────────────────────────────────

    /// <summary>
    /// Reports a non-exhaustive <c>switch</c>. Expressions must always be exhaustive; statements
    /// only over finite types (<c>bool</c>, error sets, enums, their optionals). Returns whether every
    /// value is covered.
    /// </summary>
    private bool CheckExhaustive(TextSpan span, TypeSymbol type, IReadOnlyList<(BoundPattern? Pattern, BoundExpr? Guard)> arms, bool statement)
    {
        if (type.IsError) return true;
        var domain = Domain(type);
        var covered = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (pattern, guard) in arms)
        {
            if (pattern is null) return true;
            if (guard is not null) continue;
            if (Irrefutable(pattern)) return true;
            Collect(pattern, type, covered);
        }
        if (domain is not null && domain.All(covered.Contains)) return true;
        if (statement && (domain is null || domain.Contains(Some))) return false;
        var missing = domain?.Where(k => !covered.Contains(k)).Select(k => k == Some ? "a value" : k).ToList();
        Report(DiagnosticCodes.NonExhaustive, span, [type.DisplayName],
            label: missing is { Count: > 0 } ? "not covered: " + string.Join(", ", missing) : null,
            help: statement ? "add the missing cases or a `default:` section" : "add the missing arms, or `_ => …` for the rest");
        return false;
    }

    private const string Some = "\0some";

    /// <summary>Every value of a finite type as a key; null for types with too many values to list.</summary>
    private static List<string>? Domain(TypeSymbol type) => type switch
    {
        PrimitiveType { Kind: PrimitiveKind.Bool } => ["true", "false"],
        ErrorSetSymbol s => s.Variants.Select(v => VariantKey(v, type)).ToList(),
        EnumSymbol e => e.Members.Select(m => "." + m.Name).ToList(),
        UnionSymbol u => u.Variants.Select(v => "." + v.Name).ToList(),
        ErrorUnionType { Sets.Count: > 0 } u => u.Sets.SelectMany(s => s.Variants).Select(v => VariantKey(v, type)).ToList(),
        OptionalType o => Domain(o.Inner) is { } inner ? [.. inner, "null"] : ["null", Some],
        _ => null,
    };

    private static string VariantKey(ErrorVariantSymbol v, TypeSymbol type) =>
        Unwrap(type) is ErrorUnionType { Sets.Count: > 1 } ? $"{v.Set.Name}.{v.Name}" : "." + v.Name;

    private static bool Irrefutable(BoundPattern p) => p switch
    {
        BoundBindPattern => true,
        BoundPropertyPattern pp => pp.InputType is not OptionalType && pp.Subpatterns.All(s => Irrefutable(s.Pattern)),
        BoundBinaryPattern { IsAnd: false } b => Irrefutable(b.Left) || Irrefutable(b.Right),
        BoundBinaryPattern { IsAnd: true } b => Irrefutable(b.Left) && Irrefutable(b.Right),
        _ => false,
    };

    private static void Collect(BoundPattern p, TypeSymbol type, HashSet<string> covered)
    {
        switch (p)
        {
            case BoundConstantPattern { Value: BoundDefault }:
                covered.Add("null");
                break;
            case BoundConstantPattern { Value: BoundErrorVariant ev }:
                covered.Add(VariantKey(ev.Variant, type));
                break;
            case BoundConstantPattern { Value: BoundEnumMember em }:
                covered.Add("." + em.Member.Name);
                break;
            case BoundUnionPattern up when up.Subpatterns.All(Irrefutable):
                covered.Add("." + up.Variant.Name);
                break;
            case BoundConstantPattern { Value.Constant.Value: bool b }:
                covered.Add(b ? "true" : "false");
                break;
            case BoundBinaryPattern { IsAnd: false } bp:
                Collect(bp.Left, type, covered);
                Collect(bp.Right, type, covered);
                break;
            case BoundPropertyPattern { InputType: OptionalType o } pp when pp.Subpatterns.All(s => Irrefutable(s.Pattern)):
                covered.Add(Some);
                if (Domain(o.Inner) is { } all) covered.UnionWith(all);
                break;
            case BoundNotPattern { Operand: BoundConstantPattern { Value: BoundDefault } } when type is OptionalType o2:
                covered.Add(Some);
                if (Domain(o2.Inner) is { } rest) covered.UnionWith(rest);
                break;
        }
    }

    // ── Assignment and mutability ────────────────────────────────────────────────

    private static BinaryOp? CompoundOperator(AssignOp op) => op switch
    {
        AssignOp.Add => BinaryOp.Add,
        AssignOp.Sub => BinaryOp.Sub,
        AssignOp.Mul => BinaryOp.Mul,
        AssignOp.Div => BinaryOp.Div,
        AssignOp.Rem => BinaryOp.Rem,
        AssignOp.And => BinaryOp.BitAnd,
        AssignOp.Or => BinaryOp.BitOr,
        AssignOp.Xor => BinaryOp.BitXor,
        AssignOp.Shl => BinaryOp.Shl,
        AssignOp.Shr => BinaryOp.Shr,
        AssignOp.Coalesce => BinaryOp.Coalesce,
        _ => null,
    };

    private BoundStmt BindAssign(AssignStmt a)
    {
        var target = BindExpr(a.Target);
        var op = CompoundOperator(a.Op);
        if (target.Type.IsError)
        {
            BindExpr(a.Value);
            return new BoundErrorStmt(a);
        }
        target = MapWriteTarget(target);
        CheckWritable(target, a.Target.Span, null);
        if (op is null)
            return new BoundAssign(a, target, BindConverted(a.Value, target.Type), null);
        if (op == BinaryOp.Coalesce)
        {
            if (target.Type is not OptionalType o)
            {
                Report(DiagnosticCodes.CoalesceNotOptional, a.Target.Span, [target.Type.DisplayName]);
                return new BoundErrorStmt(a);
            }
            return new BoundAssign(a, target, BindConverted(a.Value, o.Inner), op);
        }
        var value = BindExpr(a.Value, op is BinaryOp.Shl or BinaryOp.Shr ? PrimitiveType.I32 : target.Type);
        if (value.Type.IsError) return new BoundErrorStmt(a);
        if (op is BinaryOp.Shl or BinaryOp.Shr)
        {
            if (!target.Type.IsInteger || !value.Type.IsInteger)
            {
                Report(DiagnosticCodes.BadBinaryOperands, a.Span, [BinaryText(op.Value), target.Type.DisplayName, value.Type.DisplayName]);
                return new BoundErrorStmt(a);
            }
            return new BoundAssign(a, target, value, op);
        }
        if (target.Type.IsString && op == BinaryOp.Add)
        {
            Report(DiagnosticCodes.StringConcatenation, a.Span, [],
                help: "build text in a `StrBuf`: `buf.append($\"{a}{b}\")`");
            return new BoundErrorStmt(a);
        }
        if (!OperatorApplies(op.Value, target.Type))
        {
            Report(DiagnosticCodes.BadBinaryOperands, a.Span, [BinaryText(op.Value), target.Type.DisplayName, value.Type.DisplayName]);
            return new BoundErrorStmt(a);
        }
        return new BoundAssign(a, target, Convert(value, target.Type, a.Value.Span), op);
    }

    private BoundStmt BindIncDec(IncDecStmt i)
    {
        var target = BindExpr(i.Target);
        if (target.Type.IsError) return new BoundErrorStmt(i);
        if (!target.Type.IsInteger)
        {
            Report(DiagnosticCodes.BadUnaryOperand, i.Span, [i.Increment ? "++" : "--", target.Type.DisplayName]);
            return new BoundErrorStmt(i);
        }
        target = MapWriteTarget(target);
        CheckWritable(target, i.Target.Span, null);
        var one = Literal(i, target.Type, (Int128)1);
        return new BoundAssign(i, target, one, i.Increment ? BinaryOp.Add : BinaryOp.Sub);
    }

    /// <summary><c>map[k]</c> as an assignment target inserts <c>k</c> when missing instead of panicking.</summary>
    private static BoundExpr MapWriteTarget(BoundExpr target) =>
        target is BoundBuiltinCall { Member: BuiltinMember.MapIndex } b ? b with { Member = BuiltinMember.MapIndexSet } : target;

    /// <summary>
    /// Reports when <paramref name="target"/> may not be written. Writes through <c>this</c> make
    /// a method mutating (inferred later); getters may not write.
    /// </summary>
    /// <summary>Whether writing to <paramref name="target"/> writes to <c>this</c> of a method.</summary>
    internal bool WritesThis(BoundExpr target) => _fn is { Kind: FunctionKind.Method, IsStatic: false } && target switch
    {
        BoundThis => true,
        BoundField f => WritesThis(f.Receiver),
        BoundTupleElement t => WritesThis(t.Receiver),
        BoundIndex { Target.Type: not SpanType } i => WritesThis(i.Target),
        BoundBuiltinCall { Member: BuiltinMember.MapIndexSet or BuiltinMember.MapSlot } b => WritesThis(b.Receiver),
        _ => false,
    };

    internal FunctionSymbol? Function => _fn;

    internal bool CheckWritable(BoundExpr target, TextSpan span, string? why)
    {
        if (WrittenLocal(target) is { } written) written.Written = true;
        switch (target)
        {
            case BoundErrorExpr:
                return false;
            case BoundLocal { Local: { BorrowsSubject: true } l } when TypeFacts.IsUnique(l.Type):
                Report(DiagnosticCodes.MutateBorrow, span, [l.Name], label: why,
                    help: "a loop or pattern variable of a unique type borrows from its subject; change the subject itself, or work on a `.copy(to: alloc)`");
                return false;
            case BoundLocal { Local: var l }:
                if (l.IsMutable || l is ParameterSymbol { IsByRef: true }) return true;
                ReportImmutable(l, span, why);
                return false;
            case BoundThis:
                if (_fn is { Kind: FunctionKind.Constructor }) return true;
                if (_fn is { Kind: FunctionKind.Method, IsStatic: false })
                {
                    _fn.IsMutating = true;
                    return true;
                }
                if (_fn is { Kind: FunctionKind.Getter })
                    Report(DiagnosticCodes.GetterMutates, span, [_fn.Name], help: "make it a method if it needs to change the value");
                else
                    Report(DiagnosticCodes.MutateTemporary, span, [], label: why);
                return false;
            case BoundField f:
                if (f.Field.IsReadonly && !(_fn is { Kind: FunctionKind.Constructor } && _fn.Owner == f.Field.Owner && f.Receiver is BoundThis))
                {
                    Report(DiagnosticCodes.ReadonlyField, span, [f.Field.Name], help: "`readonly` fields are set only in a constructor or a struct literal");
                    return false;
                }
                return CheckWritable(f.Receiver, span, why);
            case BoundTupleElement t:
                return CheckWritable(t.Receiver, span, why);
            case BoundIndex { Target.Type: SpanType } i:
                if (i.Target.Type is SpanType { ReadOnly: true })
                {
                    Report(DiagnosticCodes.MutateTemporary, span, [], label: "a `ReadOnlySpan` cannot be written through");
                    return false;
                }
                return true;
            case BoundIndex i:
                return CheckWritable(i.Target, span, why);
            case BoundBuiltinCall { Member: BuiltinMember.MapIndexSet or BuiltinMember.MapSlot } b:
                return CheckWritable(b.Receiver, span, why);
            case BoundStaticArray:
                Report(DiagnosticCodes.MutateTemporary, span, [], label: why is null ? "a `const` never changes" : why + "; a `const` never changes");
                return false;
            case BoundBuiltinCall { Member: BuiltinMember.OwnedValue }:
                Report(DiagnosticCodes.MutateTemporary, span, [], label: why is null ? "an `Owned` value is read-only" : why + "; an `Owned` value is read-only",
                    help: "build a new value and wrap it with `Owned(…)`");
                return false;
            default:
                Report(DiagnosticCodes.MutateTemporary, span, [], label: why);
                return false;
        }
    }

    /// <summary>The local a write lands in, even when the write is rejected (<c>x.f</c>, <c>x[i]</c>, <c>x.t.0</c>).</summary>
    private static LocalSymbol? WrittenLocal(BoundExpr e) => e switch
    {
        BoundLocal l => l.Local,
        BoundField f => WrittenLocal(f.Receiver),
        BoundTupleElement t => WrittenLocal(t.Receiver),
        BoundIndex { Target.Type: not SpanType } i => WrittenLocal(i.Target),
        BoundBuiltinCall { Member: BuiltinMember.MapIndexSet or BuiltinMember.MapSlot } b => WrittenLocal(b.Receiver),
        _ => null,
    };

    private void ReportImmutable(LocalSymbol l, TextSpan span, string? why)
    {
        string? label = null, help = null;
        Fix? fix = null;
        switch (l.Kind)
        {
            case LocalKind.Let:
                label = $"declared with `let` on line {l.Location!.Value.Line}";
                help = "declare it with `var` if it should change";
                if (l.KeywordSpan is { } k) fix = new Fix(k, "var");
                break;
            case LocalKind.Const:
                label = "a `const` never changes";
                break;
            case LocalKind.Parameter:
                label = "parameters are immutable";
                help = $"copy it into a local `var`, or take it by `ref` if the caller's value should change";
                break;
            case LocalKind.ForVariable:
                label = "loop variables are immutable";
                help = "declare it with `for (var …)` to change the copy";
                break;
            case LocalKind.PatternVariable:
                label = "pattern variables are immutable";
                break;
        }
        if (why is not null) label = label is null ? why : why + "; " + label;
        Report(DiagnosticCodes.ImmutableAssignment, span, [l.Name], label: label, help: help, fix: fix);
    }
}
