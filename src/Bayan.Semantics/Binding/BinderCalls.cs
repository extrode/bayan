using Bayan.Semantics.Symbols;
using Bayan.Syntax.Ast;
using Bayan.Syntax.Diagnostics;
using Bayan.Syntax.Text;

namespace Bayan.Semantics.Binding;

internal sealed partial class Binder
{
    /// <summary>The call or checked construction that the enclosing <c>try</c>/<c>catch</c> handles; any other failing one is an error.</summary>
    private Expr? _handled;

    /// <summary>While a <c>try</c> operand is bound: the failing calls it does not handle, reported once it is known whether the operand fails (BYN1054).</summary>
    private List<(Expr Call, string Name)>? _covered;

    /// <summary>The error a <c>catch switch</c> is switching on, while its subject is bound.</summary>
    private LocalSymbol? _caught;

    private static Expr StripParens(Expr e)
    {
        while (e is ParenExpr p) e = p.Inner;
        return e;
    }

    // ── Calls ────────────────────────────────────────────────────────────────────

    private BoundExpr BindCall(CallExpr c, TypeSymbol? expected)
    {
        // A call to a member ending in `Unchecked` bypasses a safety rule and says why (J6); an empty reason is the parser's.
        var calleeName = c.Callee switch { MemberExpr me => me.Member.Text, NameExpr ne => ne.Name.Text, _ => null };
        if (c.Because is null && calleeName is { } m && m.EndsWith("Unchecked", StringComparison.Ordinal))
            Report(DiagnosticCodes.MissingBecause, c.Span, [m + "()"], label: "bypasses a safety rule",
                help: "explain the bypass in a `because` clause");
        if (c.Callee is MemberExpr { Member.Text: "trustUnchecked", NullConditional: false } trusted && c.Args.Length == 0)
            return BindTrustUnchecked(c, trusted);
        if (c.Callee is MemberExpr { Member.Text: "tryFrom", NullConditional: false, Target: NameExpr into }
            && PrimitiveType.ByName.TryGetValue(into.Name.Text, out var target) && target.IsNumeric)
            return BindTryFrom(c, target);
        if (c.Callee is MemberExpr { Member.Text: "tryParse", NullConditional: false, Target: NameExpr parsed }
            && PrimitiveType.ByName.TryGetValue(parsed.Name.Text, out var parseTo) && (parseTo.IsInteger || parseTo.IsFloat))
            return parseTo.IsFloat ? BindFloatTryParse(c, parseTo) : BindTryParse(c, parseTo);
        if (c.Callee is MemberExpr { Member.Text: "parse", NullConditional: false, Target: NameExpr parsedBy }
            && PrimitiveType.ByName.TryGetValue(parsedBy.Name.Text, out var parseAs) && parseAs.IsInteger)
            return BindParse(c, parseAs);
        // `StrBuf.over(span)`: the span first, then `context.allocator` (spec §16.4).
        if (c.Callee is MemberExpr { Member.Text: "over", NullConditional: false, Target: NameExpr { Name.Text: "StrBuf", TypeArgs.Length: 0 } }
            && _scope.Lookup("StrBuf") is null && !Module.Types.ContainsKey("StrBuf"))
            return new BoundCall(c, _c.Builtins.StrBufOver, null, BindArguments(c, _c.Builtins.StrBufOver.Parameters, _c.Builtins.StrBufOver, false, "StrBuf.over"), BuiltinType.StrBuf);
        // `Log.debug(…)` on an undeclared `Log` names the level where the act should be (BYN5007, Question 271).
        if (c.Callee is MemberExpr { NullConditional: false, Target: NameExpr { Name.Text: "Log", TypeArgs.Length: 0 }, Member.Text: var level } logged
            && _c.Builtins.LogLevel.Find(level) is not null && _scope.Lookup("Log") is null && !Module.Types.ContainsKey("Log")
            && !Module.Functions.ContainsKey("Log") && !Module.Constants.ContainsKey("Log") && !_file.Imports.ContainsKey("Log"))
        {
            Report(DiagnosticCodes.LogLevelAsVerb, logged.Span, [level],
                help: $"call `log` and pass the level `as: .{level}`",
                fix: c.Args is [{ Name: null, Modifier: ArgModifier.None } text]
                    ? new Fix(c.Span, $"log({Source.GetText(text.Value.Span)}, as: .{level})") : null);
            BindArgumentsForRecovery(c);
            return new BoundErrorExpr(c);
        }
        var outer = _calleeOf;
        _calleeOf = c;
        var callee = c.Callee is TargetMemberExpr t ? BindTargetMember(t, expected) : BindExprOrType(c.Callee);
        _calleeOf = outer;
        switch (callee)
        {
            case BoundMethodGroup { Function.Builtin: BuiltinFunction.OwnedNew }:
                return BindOwned(c, (expected as OwnedType)?.Inner);
            case BoundMethodGroup { Function.Builtin: BuiltinFunction.UntrustedNew }:
                return BindUntrusted(c, (expected as UntrustedType)?.Inner);
            case BoundMethodGroup { Function.Builtin: BuiltinFunction.Trust }:
                return BindTrust(c);
            // `Utf8.decode(bytes, invalid: .replace)` is the lossy form, which never fails.
            case BoundMethodGroup g when g.Function == _c.Builtins.Utf8Decode && c.Args.Any(a => a.Name?.Text == "invalid"):
                return BindFunctionCall(c, _c.Builtins.Utf8DecodeReplace, null, g.TypeArgs);
            // `Path.combine(paths)`, one argument, is the span form; `Overload` does not see builtins.
            case BoundMethodGroup g when g.Function == _c.Builtins.PathCombine && c.Args.Length == 1:
                return BindFunctionCall(c, _c.Builtins.PathCombineSpan, null, g.TypeArgs);
            case BoundMethodGroup g:
                return BindFunctionCall(c, Overload(g.Function, c), g.Receiver, g.TypeArgs);
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
            // A call through a function value: the value is the receiver of its type's invoke symbol (R9).
            case { Type: FunctionType ft }:
                return BindFunctionCall(c, _c.InvokeSymbol(ft), callee);
            default:
                Report(DiagnosticCodes.NotCallable, c.Callee.Span, [callee.Type.DisplayName]);
                BindArgumentsForRecovery(c);
                return new BoundErrorExpr(c);
        }
    }

    /// <summary>
    /// The overload of <paramref name="fn"/> the call's labels select (J65): the first whose labels fit the named arguments
    /// and whose unlabeled parameters fit the positional ones. Overloads cannot overlap, so at most one fits; with none, <paramref name="fn"/> reports why.
    /// </summary>
    private static FunctionSymbol Overload(FunctionSymbol fn, CallExpr c)
    {
        var overloads = fn.Owner is StructSymbol s ? s.FindMethods(fn.Name).ToList()
            : fn.Module.Functions.TryGetValue(fn.Name, out var fs) && fs.Contains(fn) ? fs : [];
        if (overloads.Count < 2) return fn;
        var named = c.Args.Where(a => a.Name is not null).Select(a => a.Name!.Text).ToHashSet();
        var positional = c.Args.Count(a => a.Name is null);
        return overloads.FirstOrDefault(f => Accepts(f, named, positional)) ?? fn;
    }

    private static bool Accepts(FunctionSymbol f, HashSet<string> named, int positional)
    {
        var ps = f.Parameters;
        if (!named.All(n => ps.Any(p => Label(p) == n))) return false;
        if (ps.Any(p => p.Syntax is { Particle: not null, Default: null } && !named.Contains(Label(p)))) return false;
        var open = ps.Where(p => p.Syntax is { Particle: null } && !named.Contains(p.Name)).ToList();
        return positional >= open.Count(p => p.Syntax is { Default: null, Rest: false })
            && (positional <= open.Count || open.Any(p => p.Syntax!.Rest));
    }

    /// <summary><c>u8.tryFrom(x)</c>: a numeric conversion that gives <c>null</c> instead of trapping when the value does not fit.</summary>
    private BoundExpr BindTryFrom(CallExpr c, PrimitiveType target)
    {
        if (c.Args is not [{ Name: null } arg])
        {
            Report(DiagnosticCodes.TooManyArguments, c.Callee.Span, [$"{target.Name}.tryFrom", 1]);
            BindArgumentsForRecovery(c);
            return new BoundErrorExpr(c);
        }
        var operand = BindExpr(arg.Value);
        if (operand.Type.IsError) return new BoundErrorExpr(c);
        if (!operand.Type.IsNumeric)
        {
            Report(DiagnosticCodes.InvalidCast, arg.Value.Span, [operand.Type.DisplayName, target.DisplayName]);
            return new BoundErrorExpr(c);
        }
        return new BoundTryFrom(c, operand, target, new OptionalType(target));
    }

    /// <summary><c>i64.tryParse(text, as: radix)</c>: an integer read from text in a <c>Radix</c>, <c>.decimal</c> by default, or <c>null</c> (spec §6.2).</summary>
    private BoundExpr BindTryParse(CallExpr c, PrimitiveType target)
    {
        var ps = new[]
        {
            new ParameterSymbol("text", null, PrimitiveType.String, ParamModifier.None, 0),
            new ParameterSymbol("as", null, _c.Builtins.Radix, ParamModifier.None, 1),
        };
        var args = BindArguments(c, ps[..Math.Clamp(c.Args.Length, 1, 2)], null, enforceNames: false, displayName: $"{target.Name}.tryParse");
        if (args.Length == 0 || args.Any(a => a.Type.IsError)) return new BoundErrorExpr(c);
        var radix = args.Length > 1 ? args[1] : new BoundEnumMember(c, _c.Builtins.Radix.Find("decimal")!);
        return new BoundBuiltinCall(c, BuiltinMember.IntTryParse, args[0], [radix], new OptionalType(target));
    }

    /// <summary><c>f64.tryParse(text)</c>: a float read from text in the literal form without <c>_</c>, or <c>null</c> (revision 0015).</summary>
    private BoundExpr BindFloatTryParse(CallExpr c, PrimitiveType target)
    {
        var args = BindArguments(c, [new ParameterSymbol("text", null, PrimitiveType.String, ParamModifier.None, 0)], null, enforceNames: false, displayName: $"{target.Name}.tryParse");
        if (args.Length == 0 || args.Any(a => a.Type.IsError)) return new BoundErrorExpr(c);
        return new BoundBuiltinCall(c, BuiltinMember.FloatTryParse, args[0], [], new OptionalType(target));
    }

    /// <summary><c>i64.parse(text, as: radix)</c>: as <c>tryParse</c>, but fails <c>ParseError</c> with the offset of the bad digit, or <c>overflow</c>.</summary>
    private BoundExpr BindParse(CallExpr c, PrimitiveType target)
    {
        var fn = _c.Builtins.IntParse(target);
        var args = BindArguments(c, fn.Parameters.Take(Math.Clamp(c.Args.Length, 1, 2)).ToArray(), null, enforceNames: false, displayName: $"{target.Name}.parse");
        if (args.Length == 0 || args.Any(a => a.Type.IsError)) return new BoundErrorExpr(c);
        var radix = args.Length > 1 ? args[1] : new BoundEnumMember(c, _c.Builtins.Radix.Find("decimal")!);
        if (!ReferenceEquals(_handled, c))
        {
            if (_covered is not null) _covered.Add((c, $"{target.Name}.parse"));
            else ReportUnhandled(c, $"{target.Name}.parse");
        }
        return new BoundCall(c, fn, null, [args[0], radix], target);
    }

    private void BindArgumentsForRecovery(CallExpr c)
    {
        foreach (var a in c.Args)
        {
            // A lambda or `it` argument has no type to bind with once the call failed.
            if (a.Value is InterpolatedExpr or LambdaExpr || MentionedIt(a.Value) is not null || _prebound.Remove(a)) continue;
            BindExprOrType(a.Value);
        }
    }

    /// <summary>Binding the argument of <c>print</c>, which also takes an enum and prints its name.</summary>
    private bool _printArgument;

    /// <summary>The sink whose argument is being bound (<c>Io.run</c> or <c>Io.runBatch</c>), where an <c>Untrusted&lt;T&gt;</c> value is BYN2786; null elsewhere.</summary>
    private string? _sinkArgument;

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
        _printArgument = fn.Builtin is BuiltinFunction.Print or BuiltinFunction.Log;
        _sinkArgument = fn.Builtin switch
        {
            BuiltinFunction.IoRun => "Io.run",
            BuiltinFunction.IoRunBatch => "Io.runBatch",
            _ => null,
        };
        // An invoke symbol's parameters have no names or particles, so a call through a value is positional (R9).
        var name = fn.Kind == FunctionKind.Invoke ? Source.GetText(c.Callee.Span) : fn.Name;
        var args = BindArguments(c, fn.Parameters, fn, enforceNames: fn.Kind is not (FunctionKind.Builtin or FunctionKind.Invoke), name);
        (_printArgument, _sinkArgument) = (outer, outerSink);
        if (receiver is not null && ReceiverStates(fn) is { } wanted) ReportWrongState(fn.Name, receiver, wanted, receiver.Syntax.Span);
        // A call that `becomes` changes the receiver's state; a statement learns the new state after it (`LearnBecomes`). A call
        // without `becomes` leaves it as it was (revision 0010).
        if (receiver is not null && fn.Decl?.Becomes is { } to)
        {
            if (Subject(receiver) is { } changed) ForgetState(changed.Key);
            if (receiver is BoundThis && _fn is { } caller) CheckBecomesCall(c, caller, fn, to);
            else if (receiver is BoundLocal { Local: ParameterSymbol { Modifier: ParamModifier.Ref } param })
                Report(DiagnosticCodes.BecomesOnRefParameter, c.Span, [fn.Name, param.Name],
                    help: $"take `{param.Name}` by `move` and return the new state as a value");
        }
        // A `ref this` call on `map[key]` changes the stored value in place.
        if (receiver is not null && fn.IsMutating) receiver = MapWriteTarget(receiver);
        // A `dyn ref` writes through the borrow, not into the local holding it; `dyn ref readonly` is BYN2813.
        if (receiver is not null && fn.HasThis && receiver.Type is not DynRefType)
            _c.MutationChecks.Add(new MutationCheck(this, fn, receiver));
        if (fn.Decl is { Requires.Length: > 0 } && args.Any(a => a.Constant is not null))
            _c.RequiresChecks.Add(new RequiresCheck(Source, c, fn, args));
        if (fn.Fails && !ReferenceEquals(_handled, c))
        {
            if (_covered is not null) _covered.Add((c, name));
            else ReportUnhandled(c, name);
        }
        return new BoundCall(c, fn, receiver, args, receiver is null ? fn.ReturnType : ReturnTypeThrough(receiver.Type, fn));
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
                    help: parameters.Count == 0 ? $"remove the type arguments; `{fn.Name}` is not generic" : null);
                return null;
            }
            var explicitArgs = typeArgs.Select(t => ResolveType(t)).ToArray();
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
                p = fn.Parameters.FirstOrDefault(x => Label(x) == n.Text);
            }
            else
                p = sawNamed || positional >= fn.Parameters.Count ? null : fn.Parameters[positional++];
            if (p is null || !TypeParameterType.Occurs(p.Type) || _prebound.ContainsKey(arg)) continue;
            // A lambda or `it` argument never contributes to inference (R12).
            if (arg.Value is LambdaExpr || p.Type is FunctionType { Parameters.Count: 1 } && MentionedIt(arg.Value) is not null) continue;
            var value = p.Type is FunctionType ? BindExprFor(arg.Value, p.Type) : BindExpr(arg.Value, null);
            _prebound[arg] = value;
            // A known-state local passed `ref` or `out` is inferred from its declared type, as `BindArgument` passes it (Q295).
            // A module function named for a function-typed parameter is inferred from its signature (R12).
            var type = p.Modifier is ParamModifier.Ref or ParamModifier.Out && value is BoundLocal known && KnownState(known.Local) is not null
                ? known.Local.Type
                : value is BoundMethodGroup { Receiver: null, Function: { Kind: FunctionKind.Function, Owner: null, IsGeneric: false } named }
                    ? new FunctionType(named.Parameters.Select(x => x.Type).ToArray(), named.ReturnType, named.ErrorSets.ToArray(), [])
                    : value.Type;
            conflict |= !Unify(p.Type, type, found, fn, arg.Value.Span);
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
            case FunctionType pf:
                return a is not FunctionType af || af.Parameters.Count != pf.Parameters.Count
                    || pf.Parameters.Zip(af.Parameters).All(x => Unify(x.First, x.Second, found, fn, span)) && Unify(pf.Return, af.Return, found, fn, span);
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
        // An instantiation follows its template's rule, so a `T` parameter is not a `bool` one at `T = bool`.
        var rule = fn?.Template?.Parameters ?? ps;
        var result = new BoundExpr?[ps.Count];
        var sawNamed = false;
        var positional = 0;
        var mismatched = false;
        // A `rest` tail takes every positional argument from its place on; parameters after it are named only (J64).
        var rest = ps.FirstOrDefault(x => x.Syntax?.Rest == true && x.Type is SpanType);
        var tail = new List<Argument>();
        foreach (var arg in c.Args)
        {
            ParameterSymbol? p;
            if (arg.Name is null && rest is not null && positional >= rest.Ordinal)
            {
                tail.Add(arg);
                continue;
            }
            if (arg.Name is { } n)
            {
                sawNamed = true;
                p = ps.FirstOrDefault(x => Label(x) == n.Text);
                if (p is null)
                {
                    var s = Suggest(n.Text, ps.Select(Label));
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
                // A particle is part of the function's name, so the call writes it (BYN5005).
                if (enforceNames && p.Syntax?.Particle is { } particle)
                    Report(DiagnosticCodes.ArgumentNeedsParticle, arg.Span, [p.Name, particle.Text], label: $"needs `{particle.Text}:`",
                        help: $"add `{particle.Text}:` before the argument", fix: new Fix(new TextSpan(arg.Span.Start, 0), particle.Text + ": "));
                else if (enforceNames && rule[p.Ordinal].Type.IsBool && ImpliedName(arg.Value) != p.Name)
                    Report(DiagnosticCodes.ArgumentMustBeNamed, arg.Value.Span, [p.Name], help: $"write `{p.Name}: …`; a `bool` argument says nothing at the call site",
                        fix: new Fix(new TextSpan(arg.Span.Start, 0), p.Name + ": "));
            }
            if (result[p.Ordinal] is not null)
            {
                Report(DiagnosticCodes.DuplicateArgument, arg.Span, [Label(p)]);
                _prebound.Remove(arg);
                continue;
            }
            result[p.Ordinal] = BindArgument(arg, p, name);
        }
        if (rest is not null && result[rest.Ordinal] is null)
            result[rest.Ordinal] = BindRestTail(c, rest, tail);
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
            if (fn?.Builtin == BuiltinFunction.Log && p.Ordinal == 1)
            {
                result[i] = new BoundEnumMember(c, _c.Builtins.LogLevel.Find("info")!);
                continue;
            }
            if (fn?.Builtin == BuiltinFunction.Print && p.Ordinal == 1)
            {
                result[i] = new BoundEnumMember(c, _c.Builtins.Stream.Find("stdout")!);
                continue;
            }
            missing.Add(Label(p));
            result[i] = new BoundErrorExpr(c);
        }
        if (missing.Count > 0 && !mismatched)
            Report(DiagnosticCodes.MissingArgument, c.Span, [string.Join("`, `", missing), name]);
        return result!;
    }

    /// <summary>
    /// The arguments of a <c>rest</c> tail as a span literal, which lowers to stack slots in the caller (J62).
    /// A lone <c>..x</c> passes the span of collection <c>x</c>; on anything else it stays a range (J63).
    /// </summary>
    private BoundExpr BindRestTail(CallExpr c, ParameterSymbol p, List<Argument> tail)
    {
        if (tail is [{ Name: null, Modifier: ArgModifier.None, Value: RangeExpr { Start: null, End: { } operand, Inclusive: false } range }])
        {
            var collection = BindExpr(operand);
            if (collection.Type.IsError) return collection;
            return collection.Type is ListType or SpanType or Symbols.ArrayType
                ? Convert(collection, p.Type, operand.Span, argument: true) : BindRange(range, null);
        }
        var element = ((SpanType)p.Type).Element;
        return new BoundCollection(c, p.Type, tail.Select(a => BindArgumentValue(a, element)).ToArray());
    }

    /// <summary>The label a call writes for <paramref name="p"/>: its particle (<c>to:</c>), else its name.</summary>
    private static string Label(ParameterSymbol p) => p.Syntax?.Particle?.Text ?? p.Name;

    // `verbose` and `opts.verbose` name the argument for a `bool` parameter `verbose`.
    private static string? ImpliedName(Expr e) => e switch
    {
        NameExpr { TypeArgs.Length: 0 } n => n.Name.Text,
        MemberExpr { NullConditional: false } m => m.Member.Text,
        _ => null,
    };

    private BoundExpr BindArgument(Argument arg, ParameterSymbol p, string callee)
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
            // A local whose state is known is passed as its declared type. A `ref` callee keeps the state (revision 0010); an `out`
            // callee supplies a new value, so its state is no longer known.
            if (place is BoundLocal known && KnownState(known.Local) is not null)
            {
                place = new BoundLocal(known.Syntax, known.Local);
                if (wanted == ArgModifier.Out) ForgetState(known.Local);
            }
            else if (place is BoundThis && wanted == ArgModifier.Out) ForgetState(_thisFacts);
            if (early is null) Constrain(place, p.Type);
            if (!place.Type.Equals(p.Type))
            {
                Report(DiagnosticCodes.TypeMismatch, arg.Value.Span, [place.Type.DisplayName, p.Type.DisplayName],
                    help: "give a `ref` or `out` argument exactly the parameter's type");
                return new BoundErrorExpr(arg.Value);
            }
            // An `out` argument writes a whole value: a `ref` parameter or `this` keeps its state only when its type names one (BYN7012).
            if (wanted == ArgModifier.Out && (place is BoundLocal { Local: ParameterSymbol { Modifier: ParamModifier.Ref } } || place is BoundThis && _fn is { IsMutating: true })
                && (place.Type is StateType { States.Count: > 1 } || place.Type is StructSymbol s && _c.StatesOf(s) is not null))
                Report(DiagnosticCodes.AssignmentChangesState, arg.Span, [place is BoundLocal l ? l.Local.Name : "this"],
                    help: "assign a value in the same state, or move the transition into a `becomes` method");
            CheckWritable(place, arg.Value.Span, $"it is passed as `{(wanted == ArgModifier.Ref ? "ref" : "out")}`");
            return place;
        }
        return BindArgumentValue(arg, p.Type, callee);
    }

    private BoundExpr BindArgumentValue(Argument arg, TypeSymbol? type, string? callee = null)
    {
        var value = _prebound.Remove(arg, out var early) ? early
            : type is FunctionType { Parameters.Count: 1 } ft && arg.Value is not LambdaExpr && MentionedIt(arg.Value) is { } it ? BindIt(arg.Value, it, ft)
            : Built(BindExprFor(arg.Value, type), arg.Value);
        if (arg.Modifier == ArgModifier.Move) value = BindMove(arg, value);
        if (callee is not null && type is StateType wanted && ReportWrongState(callee, value, wanted, arg.Value.Span)) return new BoundErrorExpr(arg.Value);
        // A type argument inferred from the value is not a typed use of it.
        return type is null ? value : Convert(value, type, arg.Value.Span, argument: true, constrains: early is null);
    }

    /// <summary>
    /// An argument that mentions <c>it</c> where a one-parameter function type is expected: a lambda whose parameter is
    /// <c>it</c>. Inside another, or inside a <c>requires</c> rule where <c>it</c> is the checked value, it is BYN2029 (R7, Q361).
    /// </summary>
    private BoundExpr BindIt(Expr e, Name it, FunctionType type)
    {
        if (_itDepth > 0)
        {
            // Outside any lambda, the `it` in scope is a `requires` rule's checked value.
            Report(DiagnosticCodes.NestedIt, it.Span, [], help: _lambdas.Count == 0 ? "name the lambda's parameter: `x => …`" : "name the inner lambda's parameter: `x => …`");
            return new BoundErrorExpr(e);
        }
        _itDepth++;
        var lambda = BindLambda(e, [it], e, null, type);
        _itDepth--;
        return lambda;
    }

    /// <summary>The first <c>it</c> the syntax of <paramref name="e"/> mentions outside a lambda nested in it.</summary>
    private static Name? MentionedIt(Expr? e)
    {
        return e switch
        {
            NameExpr { Name: { Text: "it" } n, TypeArgs.Length: 0 } => n,
            ParenExpr p => MentionedIt(p.Inner),
            MemberExpr m => MentionedIt(m.Target),
            CallExpr c => MentionedIt(c.Callee) ?? First(c.Args.Select(a => a.Value)),
            IndexExpr i => MentionedIt(i.Target) ?? MentionedIt(i.Index),
            UnaryExpr u => MentionedIt(u.Operand),
            BinaryExpr b => MentionedIt(b.Left) ?? MentionedIt(b.Right),
            RangeExpr r => MentionedIt(r.Start) ?? MentionedIt(r.End),
            ConditionalExpr c => MentionedIt(c.Condition) ?? MentionedIt(c.WhenTrue) ?? MentionedIt(c.WhenFalse),
            CastExpr c => MentionedIt(c.Operand),
            IsExpr i => MentionedIt(i.Value),
            ExceptExpr x => MentionedIt(x.Value),
            InExpr i => MentionedIt(i.Value) ?? MentionedIt(i.Range),
            SwitchExpr s => MentionedIt(s.Value) ?? First(s.Arms.SelectMany(a => new[] { a.Guard, a.Value })),
            TryExpr t => MentionedIt(t.Operand),
            MoveExpr m => MentionedIt(m.Operand),
            CatchExpr c => MentionedIt(c.Operand) ?? MentionedIt(c.Variant) ?? MentionedIt(c.Handler),
            StructLiteralExpr s => First(s.Fields.Select(f => f.Value)),
            WithExpr w => MentionedIt(w.Target) ?? First(w.Fields.Select(f => f.Value)),
            SpreadExpr s => MentionedIt(s.Operand),
            MapEntryExpr m => MentionedIt(m.Key) ?? MentionedIt(m.Value),
            CollectionExpr c => First(c.Elements),
            TupleExpr t => First(t.Elements.Select(x => x.Value)),
            InterpolatedExpr i => First(i.Parts.OfType<InterpolationHole>().Select(h => h.Expr)),
            _ => null,
        };

        static Name? First(IEnumerable<Expr?> es) => es.Select(MentionedIt).FirstOrDefault(n => n is not null);
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
                return Checked(c, new BoundCall(c, ctor, null, args, s));
            }
            case StructSymbol s when _c.Distinct.Contains(s):
            {
                // `Email(raw)` wraps the value; with `requires`, the wrapper is checked like a literal.
                var value = s.Fields[0];
                var ps = new[] { new ParameterSymbol(value.Name, value.Location, value.Type, ParamModifier.None, 0) };
                var args = BindArguments(c, ps, null, enforceNames: false, displayName: s.Name);
                return Checked(c, new BoundStructLiteral(c, s, args));
            }
            case StructSymbol s when _c.Positional.Contains(s.Decl):
            {
                // `Point(3, 4)` is `Point { x: 3, y: 4 }`: every field in declaration order, checked like the literal.
                var ps = s.Fields.Select((f, i) => new ParameterSymbol(f.Name, f.Location, f.Type, ParamModifier.None, i)).ToArray();
                var args = BindArguments(c, ps, null, enforceNames: false, displayName: s.Name);
                return Checked(c, new BoundStructLiteral(c, s, args));
            }
            case StructSymbol s:
            {
                if (_c.StatesOf(s) is { } states)
                {
                    Report(DiagnosticCodes.StateLiteralNeedsState, c.Callee.Span, [s.Name, states.Variants[0].Name],
                        help: "name the state the value starts in");
                    BindArgumentsForRecovery(c);
                    return new BoundErrorExpr(c);
                }
                if (c.Args.Length > 0)
                {
                    Report(DiagnosticCodes.NoConstructor, c.Callee.Span, [s.Name],
                        help: $"set the fields with a struct literal: `{s.Name} {{ {string.Join(", ", s.Fields.Take(2).Select(f => f.Name + ": …"))} }}`");
                    BindArgumentsForRecovery(c);
                    return new BoundErrorExpr(c);
                }
                var missing = s.Fields.Where(f => f.IsRequired || !f.HasDefault).Select(f => f.Name).ToList();
                if (missing.Count > 0)
                {
                    Report(DiagnosticCodes.MissingFields, c.Span, [s.Name, string.Join("`, `", missing)],
                        help: $"set {(missing.Count == 1 ? "it" : "them")}: `{s.Name} {{ {string.Join(", ", missing.Select(m => m + ": …"))} }}`");
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
            // `StrBuf(capacity: n)` grows from `context.allocator` like `StrBuf()`, starting with room for n bytes.
            case BuiltinType { Kind: BuiltinKind.StrBuf } when c.Args is [{ Name.Text: "capacity" }]:
                return new BoundCall(c, _c.Builtins.StrBufGrow, null,
                    BindArguments(c, [new ParameterSymbol("capacity", null, PrimitiveType.I64, ParamModifier.None, 0)], null, false, "StrBuf"), type);
            case BuiltinType { Kind: BuiltinKind.StrBuf }:
                return new BoundCall(c, _c.Builtins.StrBufNew, null, BindArguments(c, _c.Builtins.StrBufNew.Parameters, _c.Builtins.StrBufNew, false, "StrBuf"), type);
            case ErrorType:
                BindArgumentsForRecovery(c);
                return new BoundErrorExpr(c);
            case PrimitiveType p when (p.IsNumeric || p == PrimitiveType.Char) && c.Args is [{ Name: null } arg]:
                return BindConversion(c, p, arg.Value);
            case PrimitiveType { IsString: true } when c.Args is [{ Name: null }, ..]:
                return BindIntegerText(c);
            default:
                Report(DiagnosticCodes.NotCallable, c.Callee.Span, [type.DisplayName],
                    help: type.IsNumeric || type == PrimitiveType.Char ? $"convert one value: `{type.DisplayName}(x)`" : null);
                BindArgumentsForRecovery(c);
                return new BoundErrorExpr(c);
        }
    }

    /// <summary><c>string(n, as: radix)</c>: an integer as text in a <c>Radix</c>, <c>.decimal</c> by default (spec §3.2, §6.2).</summary>
    private BoundExpr BindIntegerText(CallExpr c)
    {
        var value = BindExpr(c.Args[0].Value);
        if (value.Type.IsError) return new BoundErrorExpr(c);
        if (value.Type is SpanType { Element: PrimitiveType { Kind: PrimitiveKind.U8 } } && c.Args.Length == 1)
            return new BoundBuiltinCall(c, BuiltinMember.BytesToString, value, [], PrimitiveType.String);
        if (!value.Type.IsInteger)
        {
            // `string(x)` as the `Display` conversion of every type is step 5.4a's later chunk.
            Report(DiagnosticCodes.Unsupported, c.Callee.Span, [$"`string(x)` of a `{value.Type.DisplayName}`"]);
            BindArgumentsForRecovery(c);
            return new BoundErrorExpr(c);
        }
        var ps = new[]
        {
            new ParameterSymbol("value", null, value.Type, ParamModifier.None, 0),
            new ParameterSymbol("as", null, _c.Builtins.Radix, ParamModifier.None, 1),
        };
        _prebound[c.Args[0]] = value;
        var args = BindArguments(c, ps[..Math.Min(c.Args.Length, 2)], null, enforceNames: false, displayName: "string");
        var radix = args.Length > 1 ? args[1] : new BoundEnumMember(c, _c.Builtins.Radix.Find("decimal")!);
        return new BoundBuiltinCall(c, BuiltinMember.IntToString, args[0], [radix], PrimitiveType.String);
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
            Report(DiagnosticCodes.TrustReason, c.Span, [], help: "write `x.trustUnchecked() because \"why this value is safe\"`");
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

    /// <summary>
    /// <c>x.trustUnchecked() because "…"</c>: <c>Untrusted&lt;T&gt;</c> to <c>T</c>, the canonical form of <c>trust</c>
    /// (BYN1062); a missing or empty reason is BYN8001 (<see cref="BindCall"/>, the parser).
    /// </summary>
    private BoundExpr BindTrustUnchecked(CallExpr c, MemberExpr callee)
    {
        var value = BindExpr(callee.Target);
        if (value.Type.IsError) return value;
        if (value.Type is not UntrustedType u)
        {
            Convert(value, new UntrustedType(value.Type), callee.Target.Span, argument: true);
            return new BoundErrorExpr(c);
        }
        return new BoundCall(c, _c.Builtins.Trust, null, [value], u.Inner);
    }

    private BoundExpr BindBuiltinMethodCall(CallExpr c, BoundExpr receiver, string name)
    {
        var m = BuiltinMembers.Method(receiver.Type, name)!;
        if (m.Member == BuiltinMember.StringParts && c.Args.Length > 0)
        {
            var sep = BindExpr(c.Args[0].Value);
            _prebound[c.Args[0]] = sep;
            if (sep.Type == PrimitiveType.Char)
                m = m with { Member = BuiltinMember.StringPartsRune, Parameters = [("by", PrimitiveType.Char), m.Parameters[1]] };
        }
        // `buf.append(x)` formats any `Display` value straight into the buffer, as the hole `$"{x}"` would.
        if (m.Member == BuiltinMember.StrBufAppend && c.Args is [{ Value: not InterpolatedExpr } appended])
        {
            var value = BindExpr(appended.Value);
            if (!value.Type.IsError && !value.Type.IsString)
            {
                if (WhyNotFormattable(value.Type, []) is { } why)
                    Report(DiagnosticCodes.NotFormattable, appended.Value.Span, [value.Type.DisplayName], label: why == "" ? null : why,
                        help: value.Type is StructSymbol s ? $"add `formatInto(ref StrBuf)` to `{s.Name}`, or use `{{x, as: .debug}}`" : null);
                value = new BoundInterpolated(appended.Value, [new BoundInterpolationPart(null, value)]);
            }
            _prebound[appended] = value;
        }
        // `remove(at:)` takes an index or a range; a range literal or a held `Range<i64>` picks the range form.
        BoundExpr? removedAt = null;
        if (m.Member == BuiltinMember.ListRemoveRange && c.Args is [{ Name.Text: "at", Value: not RangeExpr } at])
            _prebound[at] = removedAt = BindExpr(at.Value);
        m = m.Member switch
        {
            BuiltinMember.ListRemoveRange when c.Args is [{ Name.Text: "at", Value: RangeExpr }] || removedAt?.Type is RangeType =>
                m with { Member = BuiltinMember.ListRemoveAtRange, Parameters = [("at", new RangeType(PrimitiveType.I64))] },
            BuiltinMember.ListRemoveRange when c.Args is [{ Name.Text: "at" }] =>
                m with { Member = BuiltinMember.ListRemoveAt, Parameters = [("at", PrimitiveType.I64)] },
            BuiltinMember.ListRemoveRange when c.Args is [_, { Name.Text: "through" }] =>
                m with { Member = BuiltinMember.ListRemoveThrough, Parameters = [m.Parameters[0], ("through", PrimitiveType.I64)] },
            BuiltinMember.StringTrimmed when c.Args is [{ Name.Text: "by" }] =>
                new(BuiltinMember.StringTrimmedBy, [("by", new SpanType(PrimitiveType.Char, readOnly: true))], PrimitiveType.String),
            BuiltinMember.StringTrimmed => m with { Parameters = [("from", _c.Builtins.Edge)] },
            BuiltinMember.StringEquals => m with { Parameters = [m.Parameters[0], ("as", _c.Builtins.Comparison)] },
            _ => m,
        };
        if (m.Member is BuiltinMember.ListTake or BuiltinMember.MapTake or BuiltinMember.StrBufTake or BuiltinMember.OptionalTake)
            receiver = MapWriteTarget(receiver);
        var ps = m.Parameters.Take(Math.Max(c.Args.Length, m.Parameters.Length - m.Optional))
            .Select((p, i) => new ParameterSymbol(p.Name, null, p.Type, ParamModifier.None, i)).ToList();
        var args = BindArguments(c, ps, null, enforceNames: false, displayName: name);
        if (m.Mutating) CheckWritable(receiver, receiver.Syntax.Span, $"`{name}` modifies it");
        if (m.Member == BuiltinMember.MapSlot && !IsDefaultable(m.Returns))
            Report(DiagnosticCodes.MapSlotNoDefault, c.Span, [m.Returns.DisplayName],
                help: "read with `map[key]` or `map.tryGet(at: key) ?? fallback`, and write with `map[key] = value`");
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

    /// <summary>Whether <c>map.slot</c> can insert a default <paramref name="t"/>: numbers, <c>bool</c>, <c>rune</c>, <c>string</c>, <c>Symbol</c>, optionals and structs whose fields all have defaults.</summary>
    private static bool IsDefaultable(TypeSymbol t) => t switch
    {
        PrimitiveType { Kind: PrimitiveKind.Char } or BuiltinType { Kind: BuiltinKind.Symbol } => true,
        OptionalType => true,
        StructSymbol s => s.IsDefaultable,
        _ => t.IsNumeric || t.IsBool || t.IsString,
    };

    // ── Ownership ────────────────────────────────────────────────────────────────

    /// <summary><c>move x</c> (or a <c>move</c> argument): only a local or a <c>move</c> parameter can be moved (spec §9.3).</summary>
    private BoundExpr BindMove(Node syntax, BoundExpr operand)
    {
        if (operand.Type.IsError) return operand;
        // a narrowed local moves the underlying optional binding, still typed as the narrowed value
        if (operand is BoundConversion { Kind: ConversionKind.Narrow, Operand: BoundLocal } narrowed)
            return new BoundConversion(narrowed.Syntax, BindMove(syntax, narrowed.Operand), narrowed.Type, ConversionKind.Narrow);
        if (CheckGuardWrite(operand, syntax.Span, "it is moved")) return new BoundMove(syntax, operand);
        switch (operand)
        {
            case BoundLocal { Local: { BorrowsSubject: true } l } when TypeFacts.IsUnique(l.Type):
                Report(DiagnosticCodes.MutateBorrow, operand.Syntax.Span, [l.Name], label: "moving it would take the value out of what it borrows from",
                    help: "move or copy the value it was bound to instead");
                return new BoundMove(syntax, operand);
            case BoundLocal { Local: { Kind: LocalKind.Let or LocalKind.Var or LocalKind.PatternVariable } or ParameterSymbol { Modifier: ParamModifier.Move } }:
            case BoundLocal { Local.BorrowsSubject: true }:
                return new BoundMove(syntax, operand);
        }
        var (what, help) = operand switch
        {
            BoundLocal { Local: ParameterSymbol { IsByRef: true } p } => ($"`{p.Modifier.ToString().ToLowerInvariant()}` parameter `{p.Name}`", "copy it with `.copy(to: alloc)`; the caller still owns it"),
            BoundLocal { Local: ParameterSymbol p } => ($"parameter `{p.Name}`", $"declare it `move {p.Name}` to take ownership; a plain parameter is borrowed from the caller"),
            BoundLocal { Local: var l } => ($"`{l.Name}`", "move a local variable or a `move` parameter instead"),
            BoundField f when BuiltinMembers.Method(f.Type, "take") is not null => ($"field `{f.Field.Name}`", "take it with `.take()`, or copy it with `.copy(to: alloc)`; its owner keeps it"),
            BoundField f => ($"field `{f.Field.Name}`", "move the whole value, or copy the field with `.copy(to: alloc)`; its owner keeps it"),
            BoundIndex or BoundBuiltinCall { Member: BuiltinMember.MapIndex } when BuiltinMembers.Method(operand.Type, "take") is not null =>
                ("an element", "take it with `.take()`, or copy it with `.copy(to: alloc)`; its owner keeps it"),
            BoundIndex or BoundBuiltinCall { Member: BuiltinMember.MapIndex } => ("an element", "copy the element with `.copy(to: alloc)`; the collection keeps it"),
            BoundTupleElement => ("a tuple element", "move the whole tuple instead"),
            BoundThis => ("`this`", "move a `.copy(to: alloc)` of it instead; a method borrows `this`"),
            BoundCall or BoundBuiltinCall or BoundStructLiteral or BoundCollection or BoundLiteral or BoundLambda => ("a fresh value", "remove the `move`; a fresh value needs none"),
            _ => ("this expression", "move a local variable or a `move` parameter instead"),
        };
        Report(DiagnosticCodes.CannotMove, operand.Syntax.Span, [what], help: help);
        return new BoundMove(syntax, operand);
    }

    /// <summary>Whether the patterns being bound are on a <c>switch move x</c> subject, so their bindings own their parts (spec §9.3).</summary>
    private bool _patternOwns;

    /// <summary>Locals bound by <c>.case name</c>, and the variant each is known to hold.</summary>
    private readonly Dictionary<LocalSymbol, UnionVariantSymbol> _narrowed = new();

    /// <summary>A switch subject; <c>move x</c> moves a local or <c>move</c> parameter into the switch, and <paramref name="owns"/> says its bindings own their parts.</summary>
    private BoundExpr BindSwitchSubject(Expr value, out bool owns)
    {
        owns = false;
        var inner = value;
        while (inner is ParenExpr p) inner = p.Inner;
        if (inner is not MoveExpr m) return BindExpr(value);
        var operand = BindExpr(m.Operand);
        if (operand.Type.IsError) return operand;
        if (operand is BoundConversion { Kind: ConversionKind.Narrow, Operand: BoundLocal underlying }) operand = underlying;
        if (operand is BoundLocal { Local: { Kind: LocalKind.Let or LocalKind.Var or LocalKind.PatternVariable } or ParameterSymbol { Modifier: ParamModifier.Move } } local)
        {
            owns = !local.Local.BorrowsSubject;
            return BindMove(m, operand);
        }
        var what = operand switch
        {
            BoundLocal { Local: ParameterSymbol p } => $"parameter `{p.Name}`",
            BoundLocal { Local: var l } => $"`{l.Name}`",
            BoundField f => $"field `{f.Field.Name}`",
            BoundIndex or BoundBuiltinCall { Member: BuiltinMember.MapIndex } => "an element",
            BoundTupleElement => "a tuple element",
            BoundThis => "`this`",
            BoundCall or BoundBuiltinCall or BoundStructLiteral or BoundCollection or BoundLiteral => "a fresh value",
            _ => "this expression",
        };
        Report(DiagnosticCodes.SwitchMoveSubject, m.Operand.Span, [what],
            help: "drop `move` so the bindings borrow, declare the parameter `move`, or `.take()` a collection field into a local first");
        return operand;
    }

    // ── Failure handling ─────────────────────────────────────────────────────────

    private void ReportUnhandled(Expr c, string name)
    {
        if (c is StructLiteralExpr)
            Report(DiagnosticCodes.LiteralNeedsTry, c.Span, [name],
                help: $"add `try` before the literal and handle the `ValidationError<{name}>`",
                fix: new Fix(new TextSpan(c.Span.Start, 0), "try "));
        else
            Report(DiagnosticCodes.UnhandledFailure, c.Span, [name],
                help: "propagate the error with `try`, or handle it with `catch`",
                fix: new Fix(new TextSpan(c.Span.Start, 0), "try "));
    }

    /// <summary>Whether a <c>try</c> or <c>catch</c> can handle <paramref name="e"/> itself: a call, or a construction that may run a check.</summary>
    private static bool Handles(Expr e) => e is CallExpr or StructLiteralExpr or WithExpr;

    /// <summary>
    /// A value just built, by a literal, a constructor, <c>X(raw)</c> or <c>with</c>: of a struct with <c>requires</c>, it
    /// runs the struct's check and can fail with <c>ValidationError</c>. <paramref name="changed"/> lists the fields a
    /// <c>with</c> sets; when no rule reads one, the value needs no check (J19). Built from constants, the rules are
    /// decided here (J38).
    /// </summary>
    private BoundExpr Checked(Expr syntax, BoundExpr value, IReadOnlyCollection<FieldSymbol>? changed = null)
    {
        // A state literal is checked as its struct and keeps its state type.
        if ((value.Type is StateType state ? state.Struct : value.Type) is not StructSymbol s || !_c.Checks.TryGetValue(s, out var checks)) return value;
        if (changed is not null && !checks.Rules.Any(r => (r.Field is { } f && changed.Contains(f)) || r.Reads.Any(changed.Contains)))
            return value;
        if (value is BoundStructLiteral literal && CheckConstant(syntax, literal, checks) is { } decided)
            return decided ? value : new BoundErrorExpr(syntax);
        if (!ReferenceEquals(_handled, syntax))
        {
            if (_covered is not null) _covered.Add((syntax, s.Name));
            else ReportUnhandled(syntax, s.Name);
        }
        return new BoundCall(syntax, checks.Check, value, [], value.Type);
    }

    /// <summary>
    /// J38: a literal whose fields are all constants is checked now. True when every rule holds, false after reporting
    /// each broken one (BYN2321), null when a rule is left to run time. A struct rule reading a field whose own rule
    /// failed is skipped (J43).
    /// </summary>
    private bool? CheckConstant(Expr syntax, BoundStructLiteral literal, StructChecks checks)
    {
        if (literal.Fields.Any(f => f.Constant is null)) return null;
        var fields = literal.Struct.Fields.ToDictionary(f => f, f => literal.Fields[f.Index].Constant!);
        var results = checks.Rules.Select(r => ConstantFolding.Evaluate(r.Condition, new Dictionary<LocalSymbol, ConstantValue>(), fields)).ToList();
        if (results.Any(r => r?.Value is not bool)) return null;
        var failed = new HashSet<FieldSymbol>();
        for (var i = 0; i < checks.Rules.Count; i++)
        {
            var rule = checks.Rules[i];
            if (results[i]!.Value is true || rule.Reads.Any(failed.Contains)) continue;
            if (rule.Field is { } field) failed.Add(field);
            var at = syntax.Span;
            if (rule.Field is { } own && literal.Fields[own.Index].Syntax.Span is var v && v.Start >= at.Start && v.End <= at.End && v != at) at = v;
            var text = literal.Struct.File.Source.GetText(rule.Syntax.Condition.Span);
            Report(DiagnosticCodes.RequiresViolated, at, [Source.GetText(at), text, literal.Struct.Name],
                label: "fails the contract", help: $"pass a value that meets `requires {text}`");
        }
        return results.All(r => r!.Value is true);
    }

    private BoundExpr BindTry(TryExpr t, TypeSymbol? expected)
    {
        if (!InPanicHandler(t.Span, "try") && _deferDepth > 0)
            Report(DiagnosticCodes.TryInDefer, t.Span, [],
                help: "handle the error here with `catch`; cleanup cannot fail");
        var inner = StripParens(t.Operand);
        var (savedCovered, covered) = (_covered, new List<(Expr Call, string Name)>());
        _covered = covered;
        BoundExpr operand;
        if (!Handles(inner))
        {
            operand = BindExpr(t.Operand, expected);
        }
        else
        {
            var saved = _handled;
            _handled = inner;
            operand = BindExpr(inner, expected);
            _handled = saved;
        }
        _covered = savedCovered;
        // `try string(bytes)` decoded before round 3; `string(x)` only formats now (J82).
        if (operand is BoundBuiltinCall { Member: BuiltinMember.BytesToString } && inner is CallExpr { Callee: NameExpr conversion })
        {
            Report(DiagnosticCodes.RenamedMember, conversion.Span, ["try string(bytes)", "try Utf8.decode(bytes)"], label: "the old name",
                help: "write `Utf8.decode`", fix: new Fix(conversion.Span, "Utf8.decode"));
            return new BoundErrorExpr(t);
        }
        if (Handles(inner) && operand is BoundCall { Function.Fails: true })
        {
            foreach (var (c, name) in covered) ReportUnhandled(c, name);
            return new BoundTry(t, operand, operand.Type);
        }
        if (covered.Count > 0)
        {
            ReportTryCoversMore(t, covered[0].Call);
            foreach (var (c, name) in covered.Skip(1)) ReportUnhandled(c, name);
        }
        else if (!operand.Type.IsError)
        {
            Report(DiagnosticCodes.NothingToHandle, t.Operand.Span, ["try"],
                fix: inner is CallExpr ? new Fix(TextSpan.FromBounds(t.Span.Start, t.Operand.Span.Start), "") : null);
        }
        return operand;
    }

    /// <summary>BYN1054: <c>try</c> covers an expression that cannot fail; the fix moves it onto the failing call, in parentheses (§18).</summary>
    private void ReportTryCoversMore(TryExpr t, Expr failing)
    {
        var text = Source.GetText(t.Operand.Span);
        var call = Source.GetText(failing.Span);
        var at = failing.Span.Start - t.Operand.Span.Start;
        var fixedText = text[..at] + "(try " + call + ")" + text[(at + call.Length)..];
        Report(DiagnosticCodes.TryCoversMore, t.Operand.Span, [text, call], label: "cannot fail",
            help: $"wrap the part that fails in parentheses: `(try {call})`", fix: new Fix(t.Span, fixedText));
    }

    private BoundExpr BindCatch(CatchExpr c, TypeSymbol? expected)
    {
        var inner = StripParens(c.Operand);
        BoundExpr operand;
        if (Handles(inner))
        {
            var saved = _handled;
            _handled = inner;
            operand = BindExpr(inner, expected);
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
            if (c.Binding is { } b0) DeclareLocal(b0, ErrorType.Instance, LocalKind.Let).IsCatch = true;
            BindExpr(c.Handler);
            PopScope();
            return new BoundErrorExpr(c);
        }
        var sets = _c.ErrorSetsOf(bc.Function);
        var errorType = ErrorUnionType.Of(sets, sets.Count == 0 ? null : bc.Function.ExcludedErrors);
        PushScope();
        LocalSymbol? binding = null;
        ErrorVariantSymbol? variant = null;
        if (c.Binding is { } b)
        {
            binding = DeclareLocal(b, errorType, LocalKind.Let);
            binding.IsCatch = true;
        }
        else if (c.Handler is SwitchExpr { Value: CaughtErrorExpr })
        {
            binding = new LocalSymbol("t", null, errorType, LocalKind.Temp) { Id = _nextLocalId++, IsCatch = true };
            _fn?.Locals.Add(binding);
        }
        var savedCaught = _caught;
        _caught = binding;
        if (c.Variant is { } v)
        {
            var bound = BindExpr(v, errorType);
            if (bound is BoundErrorVariant ev)
            {
                if (sets.Count > 0 && !sets.Contains(ev.Variant.Set))
                    Report(DiagnosticCodes.UnknownVariant, v.Span, [ev.Variant.Name, errorType.DisplayName],
                        help: $"use a variant of {string.Join(", ", sets.Select(s => "`" + s.DisplayNameAmong(sets) + "`"))}, the sets `{bc.Function.Name}` fails with");
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
                    help: $"drop the handler's value; `{bc.Function.Name}` returns nothing, so the handler cannot produce one");
        }
        else if (c.Handler is BlockExpr block)
        {
            handler = new BoundBlockExpr(block, BindBlock(block.Block), PrimitiveType.Never);
        }
        else
        {
            handler = BindExpr(c.Handler, resultType);
            if (!handler.Type.IsNever) handler = Convert(handler, resultType, c.Handler.Span);
        }
        _caught = savedCaught;
        PopScope();
        return new BoundCatch(c, operand, binding, variant, handler, resultType);
    }

    // ── switch expressions ───────────────────────────────────────────────────────

    private BoundExpr BindSwitchExpr(SwitchExpr s, TypeSymbol? expected)
    {
        var value = BindSwitchSubject(s.Value, out var owns);
        var arms = new List<BoundSwitchArm>();
        foreach (var arm in s.Arms)
        {
            PushScope();
            _patternOwns = owns;
            var pattern = BindPattern(arm.Pattern, value.Type);
            _patternOwns = false;
            if (owns) CheckOwningOverlap(pattern);
            if (owns && arm.Guard is { } ag) GuardBindings(pattern, ag.Span);
            if (arm.Guard is null) LearnArmState(value, pattern);
            var guard = arm.Guard is { } g ? BindCondition(g) : null;
            // A block arm of a value-giving `catch switch` has no value to give, so it must leave (J49, BYN2611).
            var result = arm.Value is BlockExpr block && s.Value is CaughtErrorExpr && expected is { IsVoid: false }
                ? new BoundBlockExpr(block, BindBlock(block.Block), PrimitiveType.Never)
                : BindExpr(arm.Value, expected);
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
        var cases = arms.Select(a => ((BoundPattern?)a.Pattern, a.Guard)).ToList();
        if (value.Type is not OptionalType && StatesUnion(value.Type) is { } states) CheckStatesExhaustive(s, Subject(value)?.Type ?? value.Type, states, cases);
        else CheckExhaustive(s.Value.Span, value.Type, cases, statement: false);
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
                local.BorrowsSubject = !_patternOwns;
                if (!v.IsLet) local.KeywordSpan = new TextSpan(v.Span.Start, 3);
                return new BoundBindPattern(p, input, local);
            }
            case ConstantPattern c:
                return BindConstantPattern(c, input);
            case DeclarationPattern d:
            {
                var declared = ResolveType(d.Type);
                if (declared is ErrorSetSymbol set) return BindErrorSetPattern(p, input, set, d.Name);
                if (!declared.IsError)
                    Report(DiagnosticCodes.PatternMismatch, d.Type.Span, [$"`{declared.DisplayName}`", Unwrap(input).DisplayName],
                        help: "match by type only on an error set, in a `catch switch`");
                DeclareLocal(d.Name, ErrorType.Instance, LocalKind.PatternVariable);
                return new BoundBindPattern(p, input, null);
            }
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
            case VariantPattern vp when Unwrap(input) is ErrorSetSymbol or ErrorUnionType:
                return BindErrorCasePattern(vp, input, BindTargetMember(vp.Variant, Unwrap(input)), vp.Elements, vp.Binding);
            case PositionalPattern { Type: NamedType { Path.Length: 2, Args.Length: 0 } nt } pos
                when Unwrap(input) is ErrorSetSymbol or ErrorUnionType && Module.Types.TryGetValue(nt.Path[0].Text, out var named) && named is ErrorSetSymbol set:
                return BindErrorCasePattern(pos, input, ErrorCase(pos, set, nt.Path[1]), pos.Elements, null);
            case VariantPattern vp:
                return BindUnionPattern(vp, input, null, vp.Variant.Member, vp.Elements, vp.Binding);
            case PositionalPattern { Type: NamedType { Path.Length: > 1, Args.Length: 0 } nt } pos:
            {
                // `Option2.Some(let v)` on an `Option2<i64>` names the generic union without its type arguments.
                var owner = nt.Path.Length == 2 && Module.Types.TryGetValue(nt.Path[0].Text, out var named) && named is UnionSymbol { TypeParameters.Count: > 0 }
                    && Unwrap(input) is UnionSymbol u && (u == named || u.Template == named)
                    ? u
                    : ResolveType(new NamedType(TextSpan.FromBounds(nt.Path[0].Span.Start, nt.Path[^2].Span.End), nt.Path[..^1], []));
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

    /// <summary>In an owning switch, a pattern may not bind a value and a part of it, both sides of an `and`, or the same field
    /// twice: each would own the same storage (L006). Only bindings of a unique type own storage.</summary>
    private void CheckOwningOverlap(BoundPattern p)
    {
        switch (p)
        {
            case BoundBinaryPattern b when b.IsAnd && Overlap(OwningPaths(b.Left), OwningPaths(b.Right)):
            case BoundPropertyPattern { Designation: { } d } pp when TypeFacts.IsUnique(d.Type) && pp.Subpatterns.Any(s => OwningPaths(s.Pattern).Any()):
                Report(DiagnosticCodes.OwningPatternOverlap, p.Syntax.Span, [Source.GetText(p.Syntax.Span)],
                    help: "bind the whole value or its parts, not both");
                break;
            case BoundPropertyPattern pp when RepeatsField(pp):
                Report(DiagnosticCodes.OwningPatternOverlap, p.Syntax.Span, [Source.GetText(p.Syntax.Span)],
                    help: "bind each field once");
                break;
            case BoundBinaryPattern b:
                CheckOwningOverlap(b.Left);
                CheckOwningOverlap(b.Right);
                break;
            case BoundNotPattern n:
                CheckOwningOverlap(n.Operand);
                break;
            case BoundPropertyPattern pp:
                foreach (var s in pp.Subpatterns) CheckOwningOverlap(s.Pattern);
                break;
            case BoundUnionPattern up:
                foreach (var s in up.Subpatterns) CheckOwningOverlap(s);
                break;
        }
    }

    /// <summary>Whether bindings of two subpatterns reach the same storage (one binding's path is a prefix of the other's).</summary>
    private static bool RepeatsField(BoundPropertyPattern pp)
    {
        var paths = pp.Subpatterns.Select(s => OwningPaths(s.Pattern).Select(q => s.Path.Concat(q).ToArray()).ToList()).ToList();
        for (var i = 0; i < paths.Count; i++)
            for (var j = i + 1; j < paths.Count; j++)
                if (Overlap(paths[i], paths[j])) return true;
        return false;
    }

    /// <summary>Whether a path of <paramref name="a"/> is a prefix of (or equal to) a path of <paramref name="b"/>, or the other way round.</summary>
    private static bool Overlap(IEnumerable<object[]> a, IEnumerable<object[]> b) =>
        a.Any(x => b.Any(y =>
        {
            var n = Math.Min(x.Length, y.Length);
            return x.Take(n).SequenceEqual(y.Take(n));
        }));

    /// <summary>The paths from <paramref name="p"/>'s input to each binding of a unique type: member steps, and (variant, index) for union elements.</summary>
    private static IEnumerable<object[]> OwningPaths(BoundPattern p) => p switch
    {
        BoundBindPattern { Local: { } l } when TypeFacts.IsUnique(l.Type) => [[]],
        BoundNotPattern n => OwningPaths(n.Operand),
        BoundBinaryPattern b => OwningPaths(b.Left).Concat(OwningPaths(b.Right)),
        BoundPropertyPattern pp => (pp.Designation is { } d && TypeFacts.IsUnique(d.Type) ? [[]] : Enumerable.Empty<object[]>())
            .Concat(pp.Subpatterns.SelectMany(s => OwningPaths(s.Pattern).Select(q => s.Path.Concat(q).ToArray()))),
        BoundUnionPattern up => (up.Designation is { } d && TypeFacts.IsUnique(d.Type) ? [[]] : Enumerable.Empty<object[]>())
            .Concat(up.Subpatterns.SelectMany((s, i) => OwningPaths(s).Select(q => q.Prepend((up.Variant, i)).ToArray()))),
        _ => [],
    };

    /// <summary>Marks the bindings of an owning switch arm read-only inside its <c>when</c> guard (BYN2791).</summary>
    private static void GuardBindings(BoundPattern p, TextSpan guard)
    {
        switch (p)
        {
            case BoundBindPattern { Local: { } l }:
                l.GuardSpan = guard;
                break;
            case BoundNotPattern n:
                GuardBindings(n.Operand, guard);
                break;
            case BoundBinaryPattern b:
                GuardBindings(b.Left, guard);
                GuardBindings(b.Right, guard);
                break;
            case BoundPropertyPattern pp:
                if (pp.Designation is { } d) d.GuardSpan = guard;
                foreach (var s in pp.Subpatterns) GuardBindings(s.Pattern, guard);
                break;
            case BoundUnionPattern up:
                if (up.Designation is { } ud) ud.GuardSpan = guard;
                foreach (var s in up.Subpatterns) GuardBindings(s, guard);
                break;
        }
    }

    /// <summary>Reports a write at <paramref name="span"/> to an owning switch binding, or a part of it, inside its arm's guard.</summary>
    internal bool CheckGuardWrite(BoundExpr target, TextSpan span, string? why)
    {
        if (WrittenLocal(target) is not { GuardSpan: { } g } l || span.Start < g.Start || span.End > g.End) return false;
        Report(DiagnosticCodes.OwningBindingMutatedInGuard, span, [l.Name], label: why,
            help: "read it in the guard and change it in the arm's body");
        return true;
    }

    private static bool HasBindings(BoundPattern p) => p switch
    {
        BoundBindPattern b => b.Local is not null,
        BoundNotPattern n => HasBindings(n.Operand),
        BoundBinaryPattern b => HasBindings(b.Left) || HasBindings(b.Right),
        BoundPropertyPattern pp => pp.Designation is not null || pp.Subpatterns.Any(s => HasBindings(s.Pattern)),
        BoundUnionPattern up => up.Designation is not null || up.Subpatterns.Any(HasBindings),
        _ => false,
    };

    /// <summary>
    /// <c>.Circle(let r)</c> or <c>Shape.Circle(let r)</c>; <paramref name="owner"/> is the written union, if any.
    /// Elements are null for a unit variant written as a constant (<c>.None</c>). <paramref name="binding"/> is the
    /// <c>c</c> of <c>.Circle c</c>: the whole value, narrowed to the variant.
    /// </summary>
    private BoundPattern BindUnionPattern(Pattern p, TypeSymbol input, TypeSymbol? owner, Name name, Pattern[] elements, Name? binding = null)
    {
        var target = Unwrap(input);
        UnionVariantSymbol? variant = null;
        if (StatesUnion(input) is { } states)
        {
            // A struct with states matches like its states union; `Order.paid(…)` names the struct as the owner.
            var of = StateStruct(target)!;
            if (owner is not null && StateStruct(owner) is { } ownerStruct && ownerStruct.Equals(of)) owner = states;
            target = states;
            if ((owner is null || owner.Equals(states)) && states.Find(name.Text) is null)
            {
                Report(DiagnosticCodes.UnknownState, name.Span, [of.Name, name.Text], help: $"check the states declared in `{of.Name}`");
                owner = ErrorType.Instance;
            }
        }
        if (owner is not null && !owner.IsError && !target.IsError && !owner.Equals(target))
            Report(DiagnosticCodes.PatternMismatch, p.Span, [$"`{owner.DisplayName}.{name.Text}`", target.DisplayName]);
        else if ((owner ?? target) is UnionSymbol u)
        {
            variant = u.Find(name.Text);
            if (variant is null)
                Report(DiagnosticCodes.UnknownUnionVariant, name.Span, [name.Text, u.Name],
                    help: Suggest(name.Text, u.Variants.Select(v => v.Name)) is { } close ? $"did you mean `{close}`?" : null);
            else if (binding is not null)
                return BindWholeVariant(p, input, target, variant, binding);
            else if (variant.Fields.Count == 0)
                Report(DiagnosticCodes.VariantHasNoPayload, p.Span, [variant.Name], help: $"match it with `.{variant.Name}`");
            else if (elements.Length != variant.Fields.Count)
                Report(DiagnosticCodes.VariantPatternArity, p.Span, [variant.Name, variant.Fields.Count, elements.Length],
                    help: $"list every field: `.{variant.Name}({string.Join(", ", variant.Fields.Select(f => "let " + f.Name))})`, or `_` to skip one");
        }
        else if (!target.IsError && (owner ?? target) is not ErrorType)
            Report(DiagnosticCodes.PatternMismatch, p.Span, [$"`.{name.Text}(…)`", target.DisplayName]);
        if (binding is not null) DeclareLocal(binding, ErrorType.Instance, LocalKind.PatternVariable);
        var ok = variant is not null && variant.Fields.Count == elements.Length && elements.Length > 0;
        var subs = new BoundPattern[elements.Length];
        for (var i = 0; i < subs.Length; i++)
            subs[i] = BindPattern(elements[i], ok ? variant!.Fields[i].Type : ErrorType.Instance);
        return ok ? new BoundUnionPattern(p, input, variant!, subs) : new BoundBindPattern(p, input, null);
    }

    /// <summary><c>.percentOff p</c>: a tag test that binds the whole value as <c>p</c>, whose payload fields read as <c>p.rate</c>.</summary>
    private BoundUnionPattern BindWholeVariant(Pattern p, TypeSymbol input, TypeSymbol union, UnionVariantSymbol variant, Name binding)
    {
        // A state binds as its state type, `Order.paid`, whose payload fields read without a further check.
        var states = union is UnionSymbol u && StateStruct(Unwrap(input)) is { } s && _c.StatesOf(s) == u;
        var local = DeclareLocal(binding, states ? new StateType(StateStruct(Unwrap(input))!, [variant]) : union, LocalKind.PatternVariable);
        local.BorrowsSubject = !_patternOwns;
        if (!states) _narrowed[local] = variant;
        return new BoundUnionPattern(p, input, variant, [], local);
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
        if (c.Value is TargetMemberExpr tm && StatesUnion(input) is { } states)
        {
            // `.paid` on a value with states: a tag test, whatever the state's payload.
            if (states.Find(tm.Member.Text) is { } state) return new BoundUnionPattern(c, input, state, []);
            var of = StateStruct(target)!;
            Report(DiagnosticCodes.UnknownState, tm.Member.Span, [of.Name, tm.Member.Text], help: $"check the states declared in `{of.Name}`");
            return new BoundBindPattern(c, input, null);
        }
        var value = c.Value is NameExpr && target is ErrorSetSymbol or ErrorUnionType ? BindExprOrType(c.Value) : BindExpr(c.Value, target);
        if (value is BoundTypeRef { Referenced: ErrorSetSymbol set }) return BindErrorSetPattern(c, input, set, null);
        if (value is BoundTypeRef) value = BindExpr(c.Value, target);
        if (value is BoundUnionNew { Payload.Length: 0 } unit && (target.IsError || unit.Type.Equals(target)))
            return new BoundUnionPattern(c, input, unit.Variant, []);
        if (value is BoundErrorVariant ev && target is ErrorUnionType u && u.Sets.Count > 0 && (!u.Sets.Contains(ev.Variant.Set) || u.Excluded.Contains(ev.Variant)))
            Report(DiagnosticCodes.PatternMismatch, c.Span, [$"`{ev.Variant.Set.DisplayName}.{ev.Variant.Name}`", target.DisplayName]);
        else if (!(value is BoundErrorVariant && target is ErrorUnionType))
            value = Convert(value, target, c.Value.Span);
        RequireConstant(value, c.Value.Span);
        return new BoundConstantPattern(c, input, value);
    }

    /// <summary>
    /// <c>DbError</c> or <c>DbError e</c> in a <c>catch switch</c>: matches every variant of <paramref name="set"/>
    /// (an <c>or</c> of its variants), then binds the error as that set.
    /// </summary>
    private BoundPattern BindErrorSetPattern(Pattern p, TypeSymbol input, ErrorSetSymbol set, Name? name)
    {
        var target = Unwrap(input);
        if (!target.IsError && !target.Equals(set) && !(target is ErrorUnionType u && (u.Sets.Count == 0 || u.Sets.Contains(set))))
            Report(DiagnosticCodes.PatternMismatch, p.Span, [$"`{set.DisplayName}`", target.DisplayName]);
        BoundPattern test = set.Variants.Count == 0
            ? new BoundNotPattern(p, input, new BoundBindPattern(p, input, null))
            : set.Variants.Select(v => (BoundPattern)new BoundConstantPattern(p, input, new BoundErrorVariant(p, v)))
                .Aggregate((a, b) => new BoundBinaryPattern(p, input, false, a, b));
        if (name is null) return test;
        var local = DeclareLocal(name, set, LocalKind.PatternVariable);
        return new BoundBinaryPattern(p, input, true, test, new BoundBindPattern(p, input, local));
    }

    private BoundPattern BindPropertyPattern(PropertyPattern pp, TypeSymbol input)
    {
        var valueType = Unwrap(input);
        if (pp.Type is { } t)
        {
            var declared = ResolveType(t);
            if (!declared.IsError && !valueType.IsError && !declared.Equals(valueType))
                Report(DiagnosticCodes.PatternMismatch, t.Span, [$"`{declared.DisplayName}`", valueType.DisplayName],
                    help: "remove the type; type patterns apply only to unions and `dyn` values, which arrive in a later version");
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
        if (designation is not null) designation.BorrowsSubject = !_patternOwns;
        return new BoundPropertyPattern(pp, input, valueType, subs.ToArray(), designation);
    }

    private BoundMemberStep? ResolveStep(TypeSymbol type, Name name)
    {
        switch (type is StateType state ? state.Struct : type)
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
            return new BoundMemberStep(null, null, -1, b.Member, PropertyType(b.Member, b.Type));
        if (ReportRenamed(type, name)) return null;
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
            if (Irrefutable(pattern))
            {
                // Every union is declared in the package being compiled: there are no dependencies yet.
                if (pattern.Syntax is DiscardPattern && type is UnionSymbol u)
                {
                    var rest = domain!.Where(k => !covered.Contains(k)).ToList();
                    Report(DiagnosticCodes.WildcardOnLocalUnion, pattern.Syntax.Span, [u.DisplayName],
                        label: "matches every case added later",
                        help: rest.Count > 0 ? "list the cases instead: " + string.Join(", ", rest) : "remove the `_` arm");
                }
                return true;
            }
            Collect(pattern, type, covered);
        }
        if (domain is not null && domain.All(covered.Contains)) return true;
        if (statement && (domain is null || domain.Contains(Some))) return false;
        var missing = domain?.Where(k => !covered.Contains(k)).Select(k => k == Some ? "a value" : k).ToList();
        // A set none of whose variants is handled is named once, as the `catch switch` arm that would cover it.
        if (Unwrap(type) is ErrorUnionType { Sets.Count: > 1 } union && missing is not null)
            foreach (var set in union.Sets)
            {
                var keys = set.Variants.Select(v => VariantKey(v, type)).ToList();
                if (keys.Count == 0 || !keys.All(missing.Contains)) continue;
                missing.Insert(missing.IndexOf(keys[0]), set.DisplayNameAmong(union.Sets));
                missing.RemoveAll(keys.Contains);
            }
        if (Unwrap(type) is ErrorUnionType { Sets.Count: > 1 } shown && missing is not null)
        {
            var names = shown.Sets.SelectMany(s => s.Variants).ToDictionary(v => VariantKey(v, type), v => $"{v.Set.DisplayNameAmong(shown.Sets)}.{v.Name}");
            missing = missing.Select(k => names.GetValueOrDefault(k, k)).ToList();
        }
        Report(DiagnosticCodes.NonExhaustive, span, [type.DisplayName],
            label: missing is { Count: > 0 } ? "not covered: " + string.Join(", ", missing) : null,
            help: statement ? "add the missing cases or a `default:` section"
                // A `_` arm on a union of this package is BYN2712, so name the arms instead.
                : type is UnionSymbol && missing is { Count: > 0 } ? "add the missing arms: " + string.Join(", ", missing)
                : "add the missing arms, or `_ => …` for the rest");
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
        ErrorUnionType { Sets.Count: > 0 } u => u.Sets.SelectMany(s => s.Variants).Except(u.Excluded).Select(v => VariantKey(v, type)).ToList(),
        OptionalType o => Domain(o.Inner) is { } inner ? [.. inner, "null"] : ["null", Some],
        _ => null,
    };

    // Across sets a case is keyed by its program-wide code, not its text: `ValidationError<Point>` from two modules reads alike.
    private static string VariantKey(ErrorVariantSymbol v, TypeSymbol type) =>
        Unwrap(type) is ErrorUnionType { Sets.Count: > 1 } ? "\0code" + v.Code : "." + v.Name;

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
            case BoundErrorPattern ep when ep.Subpatterns.All(Irrefutable):
                covered.Add(VariantKey(ep.Variant, type));
                break;
            case BoundConstantPattern { Value.Constant.Value: bool b }:
                covered.Add(b ? "true" : "false");
                break;
            case BoundBinaryPattern { IsAnd: false } bp:
                Collect(bp.Left, type, covered);
                Collect(bp.Right, type, covered);
                break;
            case BoundBinaryPattern { IsAnd: true } bp when Irrefutable(bp.Right):
                Collect(bp.Left, type, covered);
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
        // A local whose state is known is written as its declared type; the value's state is what it is known to be after.
        if (target is BoundLocal known && KnownState(known.Local) is not null) target = new BoundLocal(known.Syntax, known.Local);
        var op = CompoundOperator(a.Op);
        if (target.Type.IsError)
        {
            BindExpr(a.Value);
            return new BoundErrorStmt(a);
        }
        target = MapWriteTarget(target);
        if (!CheckFieldWrite(target, a.Span))
        {
            BindExpr(a.Value);
            return new BoundErrorStmt(a);
        }
        CheckWritable(target, a.Target.Span, null);
        if (op is null)
        {
            var assigned = BindExpr(a.Value, target.Type);
            Constrain(target, assigned, a.Value);
            // A `ref` parameter or `this` keeps its state: only a value in the one state its type names may replace it (BYN7012).
            if (target is BoundLocal { Local: ParameterSymbol { Modifier: ParamModifier.Ref } } or BoundThis
                && (target.Type is StateType || target.Type is StructSymbol s && _c.StatesOf(s) is not null)
                && !(target.Type is StateType { States: [var one] } && (Subject(assigned)?.Type ?? assigned.Type) is StateType { States: [var given] } && given == one))
            {
                Report(DiagnosticCodes.AssignmentChangesState, a.Span, [target is BoundLocal p ? p.Local.Name : "this"],
                    help: "assign a value in the same state, or move the transition into a `becomes` method");
                return new BoundErrorStmt(a);
            }
            if (target is BoundLocal written)
            {
                ForgetState(written.Local);
                if (assigned.Type is StateType st) LearnState(written.Local, st);
            }
            else if (target is BoundThis) ForgetState(_thisFacts);
            return new BoundAssign(a, target, Convert(assigned, target.Type, a.Value.Span, constrains: !IsPending(target)), null);
        }
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
        Constrain(target, value, a.Value);
        if (target.Type.IsString && op == BinaryOp.Add)
        {
            var text = Source.GetText(a.Target.Span);
            Report(DiagnosticCodes.StringConcatenation, a.Span, [],
                help: "write the parts as one interpolation: `s = $\"{s}{x}\"`, or build text in a `StrBuf`: `buf.append($\"{a}{b}\")`",
                fix: new Fix(a.Span, $"{text} = $\"{{{text}}}{InterpolationSource(a.Value)}\""));
            return new BoundErrorStmt(a);
        }
        if (!OperatorApplies(op.Value, target.Type))
        {
            Report(DiagnosticCodes.BadBinaryOperands, a.Span, [BinaryText(op.Value), target.Type.DisplayName, value.Type.DisplayName]);
            return new BoundErrorStmt(a);
        }
        return new BoundAssign(a, target, Convert(value, target.Type, a.Value.Span, constrains: !IsPending(target)), op);
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
        if (!CheckFieldWrite(target, i.Span)) return new BoundErrorStmt(i);
        CheckWritable(target, i.Target.Span, null);
        var one = Literal(i, target.Type, (Int128)1);
        return new BoundAssign(i, target, one, i.Increment ? BinaryOp.Add : BinaryOp.Sub);
    }

    /// <summary>
    /// J78 (O11, revision 0007): a field of a struct with <c>requires</c>, states or <c>@sealed</c> is written only by
    /// its own methods (BYN3216 elsewhere); there, a field of a struct with <c>requires</c> is BYN3208, since the rules
    /// are checked at construction. A constructor sets fields freely, and plain data is written anywhere.
    /// </summary>
    private bool CheckFieldWrite(BoundExpr target, TextSpan span)
    {
        if (target is not BoundField { Field.Owner: var owner } f) return true;
        // Still a write for BYN2311, whichever way it is reported.
        if (WrittenLocal(target) is { } written) written.Written = true;
        var own = _struct is { } s && (s == owner || (s.Template ?? s) == (owner.Template ?? owner));
        if (!own)
        {
            if (Invariant(owner.Template ?? owner) is not { } reason) return true;
            Report(DiagnosticCodes.FieldWriteOutside, span, [owner.Name, f.Field.Name, reason],
                help: $"add a `ref this` method to `{owner.Name}`, or build a new value with `with`");
            return false;
        }
        if (_fn is { Kind: FunctionKind.Constructor } || !_c.Checks.ContainsKey(owner.Template ?? owner)) return true;
        Report(DiagnosticCodes.CheckedFieldAssignment, span, [owner.Name], help: "build a new value with `with`: `try x with { f: v }`");
        return false;
    }

    /// <summary>Why BYN3216 protects <paramref name="s"/>'s fields, the first of <c>requires</c>, states and <c>@sealed</c>; null for plain data.</summary>
    private static string? Invariant(StructSymbol s) =>
        s.Decl.Rules.Length > 0 || s.Decl.Members.OfType<FieldDecl>().Any(f => f.Requires.Length > 0) ? "has `requires`"
        : s.Decl.States.Length > 0 ? "has states"
        : s.Decl.Attributes.Any(a => a.Name.Text == "sealed") ? "is `@sealed`"
        : null;

    /// <summary><c>map[k]</c> as an assignment target: <c>=</c> inserts <c>k</c> when missing; <c>op=</c>, <c>++</c> and <c>--</c> read it first and panic (lowering).</summary>
    private static BoundExpr MapWriteTarget(BoundExpr target) =>
        target is BoundBuiltinCall { Member: BuiltinMember.MapIndex } b ? b with { Member = BuiltinMember.MapIndexSet } : target;

    /// <summary>BYN2115 for a method that writes <c>this</c> at <paramref name="span"/> first, with a fix inserting <c>ref this</c>.</summary>
    internal void ReportMutatesWithoutRefThis(FunctionSymbol fn, TextSpan span)
    {
        var decl = fn.Decl!;
        Report(DiagnosticCodes.MutatesWithoutRefThis, span, [fn.Name], label: "`this` is modified here",
            help: "declare it `ref this` as the first parameter",
            // A receiver written as `this is .s` gains only `ref`.
            fix: new Fix(new TextSpan(decl.ParamsStart, 0), decl.ReceiverStates.Length > 0 ? "ref " : decl.Params.Length > 0 ? "ref this, " : "ref this"));
    }

    internal FunctionSymbol? Function => _fn;

    /// <summary>
    /// Reports when <paramref name="target"/> may not be written. A write through <c>this</c> in a
    /// method not declared <c>ref this</c> is noted for BYN2115; getters may not write.
    /// </summary>
    internal bool CheckWritable(BoundExpr target, TextSpan span, string? why)
    {
        if (WrittenLocal(target) is { } written) written.Written = true;
        if (CheckGuardWrite(target, span, why)) return false;
        switch (target)
        {
            case BoundErrorExpr:
                return false;
            case BoundLocal { Local: { BorrowsSubject: true } l } when TypeFacts.IsUnique(l.Type):
                Report(DiagnosticCodes.MutateBorrow, span, [l.Name], label: why,
                    help: "change the subject itself, or work on a `.copy(to: alloc)`; a loop or pattern variable of a unique type borrows from its subject");
                return false;
            case BoundLocal { Local: var l }:
                if (l.IsMutable || l is ParameterSymbol { IsByRef: true }) return true;
                ReportImmutable(l, span, why);
                return false;
            case BoundThis:
                if (_fn is { Kind: FunctionKind.Constructor }) return true;
                if (_fn is { Kind: FunctionKind.Method, IsStatic: false })
                {
                    if (!_fn.IsMutating && !_fn.IsInstance && (!_c.ThisWrites.TryGetValue(_fn, out var first) || span.Start < first.Span.Start))
                        _c.ThisWrites[_fn] = (this, span);
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
                    Report(DiagnosticCodes.ReadonlyField, span, [f.Field.Name], help: "set `readonly` fields only in a constructor or a struct literal");
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
                help = "copy it into a local `var` to change it";
                break;
            case LocalKind.PatternVariable:
                label = "pattern variables are immutable";
                break;
        }
        if (why is not null) label = label is null ? why : why + "; " + label;
        Report(DiagnosticCodes.ImmutableAssignment, span, [l.Name], label: label, help: help, fix: fix);
    }
}
