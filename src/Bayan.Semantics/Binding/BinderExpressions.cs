using System.Globalization;
using System.Numerics;
using System.Text;
using Bayan.Semantics.Symbols;
using Bayan.Syntax.Ast;
using Bayan.Syntax.Diagnostics;
using Bayan.Syntax.Parsing;
using Bayan.Syntax.Text;

namespace Bayan.Semantics.Binding;

internal sealed partial class Binder
{
    // ── Entry points ─────────────────────────────────────────────────────────────

    /// <summary>Binds a value. <paramref name="expected"/> only guides literals and target-typed forms; no conversion is applied.</summary>
    private BoundExpr BindExpr(Expr e, TypeSymbol? expected = null) => AsValue(BindExprOrType(e, expected), e);

    private BoundExpr AsValue(BoundExpr bound, Expr e)
    {
        switch (bound)
        {
            case BoundTypeRef t:
                Report(DiagnosticCodes.NotAValue, e.Span, [t.Referenced.DisplayName, "type"]);
                return new BoundErrorExpr(e);
            case BoundModuleRef m:
                Report(DiagnosticCodes.NotAValue, e.Span, [m.Module.Name, "module"]);
                return new BoundErrorExpr(e);
            case BoundVariantGroup g:
                Report(DiagnosticCodes.VariantNeedsPayload, e.Span, [g.Variant.Name],
                    help: $"pass it: `{g.Variant.Union.Name}.{g.Variant.Name}({string.Join(", ", g.Variant.Fields.Select(f => f.Name))})`");
                return new BoundErrorExpr(e);
            case BoundMethodGroup g:
                Report(DiagnosticCodes.NotAValue, e.Span, [g.Function.Name, g.Function.Kind == FunctionKind.Function ? "function" : "method"],
                    help: $"call it: `{g.Function.Name}({(g.Function.Parameters.Count == 0 ? "" : "…")})`");
                return new BoundErrorExpr(e);
            case BoundBuiltinGroup g:
                Report(DiagnosticCodes.NotAValue, e.Span, [g.Name, "method"], help: $"call it: `{g.Name}()`");
                return new BoundErrorExpr(e);
            default:
                return bound;
        }
    }

    private BoundExpr BindConverted(Expr e, TypeSymbol target, bool argument = false)
    {
        var bound = Built(BindExprFor(e, target), e);
        return Convert(bound, target, e.Span, argument);
    }

    /// <summary>
    /// As <see cref="BindExpr"/>, but a function, constructor or variant named where a function type is expected stays a group,
    /// so <see cref="Convert"/> can make it a function value or say why it is not one (spec 007 R4).
    /// </summary>
    private BoundExpr BindExprFor(Expr e, TypeSymbol? expected)
    {
        var bound = BindExprOrType(e, expected);
        return expected is FunctionType && (bound is BoundMethodGroup or BoundVariantGroup || bound is BoundTypeRef { Referenced: StructSymbol s } && Constructible(s))
            ? bound : AsValue(bound, e);
    }

    /// <summary><c>S(…)</c> builds an <paramref name="s"/>: it has a constructor, or is distinct or <c>@positional</c>.</summary>
    private bool Constructible(StructSymbol s) => s.Constructors.Count > 0 || _c.Distinct.Contains(s) || _c.Positional.Contains(s.Decl);

    /// <summary>An expression statement: a value it leaves is an error (BYN3201), so a mis-split line is never silent (§3, J2).</summary>
    private BoundExpr BindStatementExpression(Expr e)
    {
        var bound = BindExpr(e);
        if (!bound.Type.IsVoid && !bound.Type.IsNever && !bound.Type.IsError)
            Report(DiagnosticCodes.UnusedValue, e.Span, [Source.GetText(e.Span)], label: "value dropped",
                help: "assign it with `let _ = …`, or remove the expression");
        return bound;
    }

    private BoundExpr Convert(BoundExpr value, TypeSymbol target, TextSpan span, bool argument = false, bool constrains = true)
    {
        if (constrains) Constrain(value, target);
        if (target is FunctionType function && value is BoundMethodGroup or BoundVariantGroup or BoundTypeRef) return ConvertToFunction(value, function, span);
        if (value.Type.Equals(target) || target.IsError || value.Type.IsError) return value;
        // An `Untrusted<bool>` is a clean fact, usable as a condition (J41): a `bool` cannot reach a sink.
        if (value.Type is UntrustedType { Inner.IsBool: true } && target.IsBool) return Fact(value);
        if (value.Constant is { Value: Int128 n } && (target.IsInteger || target.IsFloat || target is OptionalType { Inner.IsNumeric: true }) && value.Type.IsInteger)
        {
            var inner = target is OptionalType o ? o.Inner : target;
            if (inner.IsInteger && !Fits(n, (PrimitiveType)inner))
            {
                Report(DiagnosticCodes.LiteralOutOfRange, span, [n.ToString(), $"`{inner.DisplayName}`"],
                    help: RangeHelp((PrimitiveType)inner));
                return new BoundErrorExpr(value.Syntax);
            }
            var literal = Literal(value.Syntax, inner, inner.IsFloat ? (object)(double)n : n);
            return inner == target ? literal : new BoundConversion(value.Syntax, literal, target, ConversionKind.ToOptional);
        }
        if (value.Constant is { Value: double d } && target is PrimitiveType { Kind: PrimitiveKind.F32 } && value.Syntax is LiteralExpr)
            return Literal(value.Syntax, target, (double)(float)d);
        if (_printArgument && argument && value.Type is EnumSymbol && target.IsString)
            return new BoundBuiltinCall(value.Syntax, BuiltinMember.EnumName, value, [], PrimitiveType.String);
        if (value is BoundStaticArray && target is SpanType { ReadOnly: false })
        {
            Report(DiagnosticCodes.TypeMismatch, span, [value.Type.DisplayName, target.DisplayName],
                label: "a `const` array cannot be written through", help: $"take a `ReadOnlySpan<{((SpanType)target).Element.DisplayName}>`");
            return new BoundErrorExpr(value.Syntax);
        }
        if (Conversions.Implicit(value.Type, target, argument) is { } kind)
        {
            if (kind == ConversionKind.Identity) return value;
            if (kind == ConversionKind.NullToOptional) return new BoundDefault(value.Syntax, target);
            // A writable `dyn ref` borrows its source as `ref` does; never a copy or a move.
            if (kind == ConversionKind.ToDynRef && target is DynRefType { ReadOnly: false })
                CheckWritable(value, span, $"it is borrowed as `{target.DisplayName}`");
            var converted = new BoundConversion(value.Syntax, value, target, kind);
            return value.Constant is { } c && kind == ConversionKind.Widen ? converted with { Constant = WidenConstant(c, target) } : converted;
        }
        if (_sinkArgument is { } sink && ContainsUntrusted(value.Type))
        {
            Report(DiagnosticCodes.UntrustedSink, span, [value.Type.DisplayName, sink],
                help: "check the value, then clear the mark with `x.trustUnchecked() because \"…\"`");
            return new BoundErrorExpr(value.Syntax);
        }
        // Only a string literal is a `Symbol` at compile time (spec 008 R2); any other text is interned, which allocates.
        if (value.Type.IsString && (target == BuiltinType.Symbol || target is OptionalType { Inner: BuiltinType { Kind: BuiltinKind.Symbol } }))
        {
            var text = Source.GetText(span);
            Report(DiagnosticCodes.StringNotSymbol, span, [], help: $"convert it with `Symbol.intern({text})`",
                fix: new Fix(span, $"Symbol.intern({text})"));
            return new BoundErrorExpr(value.Syntax);
        }
        // O9: a bare literal never takes a distinct type; `ParcelId(0)` builds one.
        if (target is StructSymbol distinct && _c.Distinct.Contains(distinct) && value.Constant is not null)
        {
            var text = Source.GetText(span);
            Report(DiagnosticCodes.BareLiteralForDistinct, span, [text, distinct.Name], help: $"wrap it: `{distinct.Name}({text})`",
                fix: new Fix(span, $"{distinct.Name}({text})"));
            return new BoundErrorExpr(value.Syntax);
        }
        ReportMismatch(value, target, span);
        return new BoundErrorExpr(value.Syntax);
    }

    /// <summary>
    /// A name where function type <paramref name="target"/> is expected: a module-level, non-generic function whose signature
    /// matches exactly is a <see cref="BoundFunctionRef"/>, its particles dropped (R4); a mismatch shows both signatures (BYN2202);
    /// a built-in, generic function, method, constructor or variant is BYN2824.
    /// </summary>
    private BoundExpr ConvertToFunction(BoundExpr value, FunctionType target, TextSpan span)
    {
        var text = Source.GetText(span);
        string reason;
        IEnumerable<ParameterSymbol> parameters;
        switch (value)
        {
            case BoundMethodGroup { Receiver: null, Function: { Kind: FunctionKind.Function, Owner: null, IsGeneric: false } fn, TypeArgs.Length: 0 }:
                var overloads = fn.Module.Functions.TryGetValue(fn.Name, out var fs) && fs.Contains(fn) ? fs : [fn];
                if (overloads.FirstOrDefault(f => Conversions.Implicit(f, target) is not null) is { } match)
                    return new BoundFunctionRef(value.Syntax, match, target);
                var signature = $"{fn.ReturnType.DisplayName} {fn.Name}({string.Join(", ", fn.Parameters.Select(p => (p.Modifier == ParamModifier.None ? "" : p.Modifier.ToString().ToLowerInvariant() + " ") + p.Type.DisplayName))})"
                    + (fn.Fails ? " fails" + (fn.ErrorSets.Count > 0 ? " " + string.Join(", ", fn.ErrorSets.Select(s => s.DisplayName)) : "") : "");
                // Inferred error sets are not known until every body is bound (Q364 a).
                var help = fn is { Fails: true, ErrorSets.Count: 0 } && target.Fails.Count > 0
                    ? $"declare the error sets of `{fn.Name}`: `fails {string.Join(", ", target.Fails.Select(s => s.DisplayName))}`"
                    : $"pass a function of type `{target.DisplayName}`";
                Report(DiagnosticCodes.TypeMismatch, span, [signature, target.DisplayName], help: help);
                return new BoundErrorExpr(value.Syntax);
            case BoundMethodGroup g:
                reason = g.Function.Kind switch
                {
                    FunctionKind.Builtin => "it is built in",
                    FunctionKind.Constructor => "it is a constructor",
                    _ when g.Receiver is not null || g.Function.Owner is not null || g.Function.Interface is not null => "it is a method",
                    _ when g.Function.IsGeneric || g.TypeArgs.Length > 0 => "it is generic",
                    _ => "it is not a module function",
                };
                parameters = g.Function.Parameters;
                break;
            case BoundVariantGroup v:
                (reason, parameters) = ("it is a union variant", v.Variant.Fields);
                break;
            case BoundTypeRef { Referenced: StructSymbol s } when Constructible(s):
                reason = "it is a constructor";
                parameters = s.Constructors.Count > 0 ? s.Constructors[0].Parameters : s.Fields.Select((f, i) => new ParameterSymbol(f.Name, null, f.Type, ParamModifier.None, i));
                break;
            default:
                return value;
        }
        // The lambda takes what the function type passes; a built-in's defaulted tail stays out.
        parameters = parameters.Take(target.Parameters.Count).ToList();
        var names = parameters.Select(p => p.Name).ToList();
        var lambda = names is [var one] ? one : "(" + string.Join(", ", names) + ")";
        var args = string.Join(", ", parameters.Select(p => p.Syntax?.Particle is { } particle ? $"{particle.Text}: {p.Name}" : p.Name));
        Report(DiagnosticCodes.NotAFunctionValue, span, [text, reason], help: $"wrap it in a lambda: `{lambda} => {text}({args})`");
        return new BoundErrorExpr(value.Syntax);
    }

    /// <summary>
    /// Records <paramref name="target"/> for each pending `var` literal <paramref name="value"/> is made of, if it is the
    /// first use; a float or string first use is kept too, so the literal stays `i32` (Q74).
    /// </summary>
    private void Constrain(BoundExpr value, TypeSymbol target)
    {
        if (!target.IsInteger && !target.IsFloat && !target.IsString) return;
        if (value is BoundLocal { Local: var l } && _pending.TryGetValue(l, out var decl))
            _constraints.TryAdd(decl, target);
        else if (value is BoundBinary b && IsPending(b))
        {
            Constrain(b.Left, target);
            Constrain(b.Right, target);
        }
        else if (value is BoundUnary u && IsPending(u)) Constrain(u.Operand, target);
    }

    /// <summary>
    /// A pending `var` met by <paramref name="other"/>: a typed value constrains it, a literal does not, and another
    /// pending `var` joins its group, so both take one inferred type.
    /// </summary>
    private void Constrain(BoundExpr value, BoundExpr other, Expr otherSyntax)
    {
        if (IsPending(other))
        {
            if (!IsPending(value)) return;
            var root = PendingDecls(value).Concat(PendingDecls(other)).Select(FindPeer).ToList();
            foreach (var r in root.Skip(1).Where(r => r != root[0])) _peers[r] = root[0];
        }
        else if (!IsUntypedLiteral(otherSyntax)) Constrain(value, other.Type);
    }

    private IEnumerable<LocalDecl> PendingDecls(BoundExpr e) => e switch
    {
        BoundLocal { Local: var l } when _pending.TryGetValue(l, out var decl) => [decl],
        BoundBinary b => PendingDecls(b.Left).Concat(PendingDecls(b.Right)),
        BoundUnary u => PendingDecls(u.Operand),
        _ => [],
    };

    private LocalDecl FindPeer(LocalDecl d)
    {
        while (_peers.TryGetValue(d, out var parent)) d = parent;
        return d;
    }

    /// <summary>A pending `var`, or arithmetic or `-`/`+`/`~` on pending `var`s and literals: its type is not settled yet.</summary>
    private bool IsPending(BoundExpr e) => e switch
    {
        BoundLocal { Local: var l } => _pending.ContainsKey(l),
        BoundBinary { Type.IsInteger: true } b => (IsPending(b.Left) || IsPending(b.Right)) && Untyped(b.Left) && Untyped(b.Right),
        BoundUnary { Op: UnaryOp.Negate or UnaryOp.Plus or UnaryOp.Complement, Type.IsInteger: true } u => IsPending(u.Operand),
        _ => false,
    };

    private bool Untyped(BoundExpr e) => IsPending(e) || e.Syntax is Expr x && IsUntypedLiteral(x);

    private static bool ContainsUntrusted(TypeSymbol t) => t switch
    {
        UntrustedType => true,
        ListType l => ContainsUntrusted(l.Element),
        SpanType s => ContainsUntrusted(s.Element),
        Symbols.ArrayType a => ContainsUntrusted(a.Element),
        OptionalType o => ContainsUntrusted(o.Inner),
        _ => false,
    };

    private static ConstantValue WidenConstant(ConstantValue c, TypeSymbol target) =>
        target.IsFloat && c.Value is Int128 n ? new ConstantValue((double)n) : c;

    private void ReportMismatch(BoundExpr value, TypeSymbol target, TextSpan span)
    {
        string? help = null;
        Fix? fix = null;
        if (Conversions.Explicit(value.Type, target) is ConversionKind.NumericCast or ConversionKind.CharToInteger or ConversionKind.IntegerToChar)
        {
            var text = Source.GetText(span);
            help = value.Type.IsNumeric && target.IsNumeric
                ? $"convert it if the value always fits; `{value.Type.DisplayName}` does not convert to `{target.DisplayName}` without loss"
                : "convert it explicitly";
            fix = new Fix(span, $"{target.DisplayName}({text})");
        }
        else if (value.Type is OptionalType o && o.Inner.Equals(target))
        {
            help = "unwrap it with `??`, `is { } v`, or `?? return`; the value may be absent";
        }
        else if (value.Type is ListType && target is SpanType)
        {
            help = "use `.span` here; a `List` becomes a `Span` implicitly only as a call argument";
        }
        Report(DiagnosticCodes.TypeMismatch, span, [value.Type.DisplayName, target.DisplayName], help: help, fix: fix);
    }

    private static string RangeHelp(PrimitiveType t)
    {
        var (min, max) = t.Range;
        return $"use a value from {min} to {max}, the range of `{t.Name}`";
    }

    private static bool Fits(Int128 n, PrimitiveType t)
    {
        var (min, max) = t.Range;
        return n >= min && n <= max;
    }

    private static BoundLiteral Literal(Node syntax, TypeSymbol type, object value)
    {
        var c = new ConstantValue(value);
        return new BoundLiteral(syntax, type, c) { Constant = c };
    }

    // ── Dispatch ─────────────────────────────────────────────────────────────────

    private BoundExpr BindExprOrType(Expr e, TypeSymbol? expected = null)
    {
        switch (e)
        {
            case LiteralExpr l:
                return BindLiteral(l, expected);
            case ParenExpr p:
                return BindExprOrType(p.Inner, expected);
            case NameExpr n:
                return BindName(n);
            case ThisExpr t:
                if (_thisType is null || Host is not { IsStatic: false, Kind: FunctionKind.Method or FunctionKind.Getter or FunctionKind.Constructor })
                {
                    Report(DiagnosticCodes.ThisOutsideMethod, t.Span, []);
                    return new BoundErrorExpr(t);
                }
                return InLambda(t, "this", "a lambda does not capture `this`") ? new BoundErrorExpr(t) : new BoundThis(t, _thisType);
            case MemberExpr m:
                return BindMember(m);
            case TargetMemberExpr t:
                return BindTargetMember(t, expected);
            case CallExpr c:
                return BindErrorNew(c, expected) ?? BindCall(c, expected);
            case IndexExpr i:
                return BindIndex(i);
            case UnaryExpr u:
                return BindUnary(u, expected);
            case BinaryExpr b:
                return BindBinary(b, expected);
            case RangeExpr r:
                return BindRange(r, expected);
            case ConditionalExpr c:
                return BindConditional(c, expected);
            case CastExpr c:
                return BindCast(c);
            case IsExpr i:
            {
                var value = BindExpr(i.Value);
                var pattern = BindPattern(i.Pattern, value.Type);
                return new BoundIs(i, value, pattern);
            }
            case InExpr i:
                return BindIn(i);
            case SwitchExpr s:
                return BindSwitchExpr(s, expected);
            case TryExpr t:
                return BindTry(t, expected);
            case MoveExpr m:
                return BindMove(m, BindExpr(m.Operand, expected));
            case CatchExpr c:
                return BindCatch(c, expected);
            case CaughtErrorExpr ce:
                return _caught is { } caught ? new BoundLocal(ce, caught) : new BoundErrorExpr(ce);
            case JumpExpr j:
                return new BoundJump(j, BindStatement(j.Jump));
            case BlockExpr b:
                return new BoundBlockExpr(b, BindBlock(b.Block), PrimitiveType.Void);
            case StructLiteralExpr s:
                return BindStructLiteral(s, expected);
            case WithExpr w:
                return BindWith(w);
            case CollectionExpr c:
                return BindCollection(c, expected);
            case TupleExpr t:
                return BindTuple(t, expected);
            case InterpolatedExpr i:
                return BindInterpolated(i);
            case StackallocExpr s:
                return BindStackalloc(s);
            case SpreadExpr s:
                Report(DiagnosticCodes.Unsupported, s.Span, ["spreads (`..xs`) outside a collection literal"]);
                return new BoundErrorExpr(s);
            case PatternExpr p:
                Report(DiagnosticCodes.Unsupported, p.Span, ["a named pattern"]);
                return new BoundErrorExpr(p);
            case LambdaExpr l:
                return BindLambda(l, l.Params, l.Body, l.Block, (expected is OptionalType o ? o.Inner : expected) as FunctionType);
            case ErrorExpr:
                return new BoundErrorExpr(e);
            default:
                Report(DiagnosticCodes.Unsupported, e.Span, ["this expression"]);
                return new BoundErrorExpr(e);
        }
    }

    // ── Literals ─────────────────────────────────────────────────────────────────

    private BoundExpr BindLiteral(LiteralExpr l, TypeSymbol? expected, bool negate = false, TextSpan? whole = null)
    {
        var target = expected is OptionalType o ? o.Inner : expected;
        switch (l.Kind)
        {
            case LiteralKind.Integer:
            {
                var parsed = ParseInteger(l.Text);
                if (negate) parsed = -parsed;
                if (l.Suffix is { } suffix)
                {
                    TypeSymbol? suffixed = suffix.Text switch
                    {
                        "u" => parsed <= uint.MaxValue ? PrimitiveType.U32 : PrimitiveType.U64,
                        "l" => PrimitiveType.I64,
                        "ul" => PrimitiveType.U64,
                        "f" => PrimitiveType.F32,
                        _ => null,
                    };
                    if (suffixed is null)
                    {
                        Report(DiagnosticCodes.Unsupported, suffix.Span, [$"the unit suffix `{suffix.Text}`"]);
                        return new BoundErrorExpr(l);
                    }
                    if (suffixed.IsFloat) return Literal(l, suffixed, (double)parsed);
                    return CheckedIntegerLiteral(l, whole ?? l.Span, parsed, (PrimitiveType)suffixed);
                }
                if (target is PrimitiveType { IsFloat: true } ft) return Literal(l, ft, (double)parsed);
                if (target is PrimitiveType { IsInteger: true } it) return CheckedIntegerLiteral(l, whole ?? l.Span, parsed, it);
                var natural = parsed >= int.MinValue && parsed <= int.MaxValue ? PrimitiveType.I32
                    : parsed >= long.MinValue && parsed <= long.MaxValue ? PrimitiveType.I64
                    : PrimitiveType.U64;
                return CheckedIntegerLiteral(l, whole ?? l.Span, parsed, natural);
            }
            case LiteralKind.Float:
            {
                var text = l.Text.Replace("_", "");
                var value = double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
                if (negate) value = -value;
                if (l.Suffix is { Text: "f" } || target is PrimitiveType { Kind: PrimitiveKind.F32 })
                    return Literal(l, PrimitiveType.F32, (double)(float)value);
                if (l.Suffix is { } s)
                {
                    Report(DiagnosticCodes.Unsupported, s.Span, [$"the suffix `{s.Text}`"]);
                    return new BoundErrorExpr(l);
                }
                return Literal(l, PrimitiveType.F64, value);
            }
            case LiteralKind.Char:
            {
                var value = LiteralDecoder.DecodeChar(l.Text);
                if (value < 0) return new BoundErrorExpr(l);
                if (target is PrimitiveType { Kind: PrimitiveKind.U8 } && value < 0x80)
                    return Literal(l, PrimitiveType.U8, (Int128)value);
                return Literal(l, PrimitiveType.Char, new Rune(value));
            }
            case LiteralKind.String or LiteralKind.RawString when target == BuiltinType.Symbol:
                return new BoundSymbolLiteral(l, l.Text) { Constant = new ConstantValue(l.Text) };
            case LiteralKind.String:
                return Literal(l, PrimitiveType.String, l.Text);
            case LiteralKind.RawString:
                return Literal(l, PrimitiveType.String, l.Text);
            case LiteralKind.True:
                return Literal(l, PrimitiveType.Bool, true);
            case LiteralKind.False:
                return Literal(l, PrimitiveType.Bool, false);
            case LiteralKind.Null:
                if (expected is OptionalType) return new BoundDefault(l, expected);
                return new BoundLiteral(l, NullType.Instance, new ConstantValue("null"));
            default:
                Report(DiagnosticCodes.Unsupported, l.Span, [l.Kind == LiteralKind.CString ? "C string literals" : "UTF-16 string literals"]);
                return new BoundErrorExpr(l);
        }
    }

    private BoundExpr CheckedIntegerLiteral(LiteralExpr l, TextSpan span, BigInteger value, PrimitiveType type)
    {
        if (value < (BigInteger)type.Range.Min || value > (BigInteger)type.Range.Max)
        {
            Report(DiagnosticCodes.LiteralOutOfRange, span, [value.ToString(CultureInfo.InvariantCulture), $"`{type.Name}`"], help: RangeHelp(type));
            return new BoundErrorExpr(l);
        }
        return Literal(l, type, (Int128)value);
    }

    internal static BigInteger ParseInteger(string text)
    {
        text = text.Replace("_", "");
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return BigInteger.Parse("0" + text[2..], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
        if (text.StartsWith("0b", StringComparison.OrdinalIgnoreCase))
        {
            BigInteger v = 0;
            foreach (var ch in text[2..]) v = v * 2 + (ch - '0');
            return v;
        }
        return BigInteger.Parse(text, CultureInfo.InvariantCulture);
    }

    private static bool IsUntypedLiteral(Expr e) => e switch
    {
        LiteralExpr { Kind: LiteralKind.Integer or LiteralKind.Float or LiteralKind.Char or LiteralKind.Null, Suffix: null } => true,
        UnaryExpr { Op: UnaryOp.Negate or UnaryOp.Plus, Operand: var o } => IsUntypedLiteral(o),
        ParenExpr p => IsUntypedLiteral(p.Inner),
        BinaryExpr { Op: not (BinaryOp.Lt or BinaryOp.Gt or BinaryOp.Le or BinaryOp.Ge or BinaryOp.Eq or BinaryOp.Ne or BinaryOp.LogicalAnd or BinaryOp.LogicalOr or BinaryOp.Coalesce) } b =>
            IsUntypedLiteral(b.Left) && IsUntypedLiteral(b.Right),
        _ => false,
    };

    // ── Names ────────────────────────────────────────────────────────────────────

    private BoundExpr BindName(NameExpr n)
    {
        var name = n.Name.Text;
        if (n.TypeArgs.Length > 0)
        {
            if (_scope.Lookup(name) is null && Module.Functions.TryGetValue(name, out var generic))
                return new BoundMethodGroup(n, null, generic[0]) { TypeArgs = n.TypeArgs };
            var t = ResolveType(new NamedType(n.Span, [n.Name], n.TypeArgs));
            return t.IsError ? new BoundErrorExpr(n) : new BoundTypeRef(n, t);
        }
        if (name == "it" && _it is not null)
            return InLambda(n, "it", "the checked value is read through `this`, which a lambda does not capture")
                ? new BoundErrorExpr(n)
                : new BoundField(n, new BoundThis(n, _thisType!), _it);
        if (_scope.Lookup(name) is { } local)
        {
            if (local.Kind == LocalKind.Const && local.Constant is { } c)
                return ConstantLiteral(n, local.Type, c);
            var read = IsNonNull(local)
                ? new BoundConversion(n, new BoundLocal(n, local), ((OptionalType)local.Type).Inner, ConversionKind.Narrow)
                // A local whose state is known reads as its state type (spec §3.17).
                : KnownState(local) is { } known ? new BoundLocal(n, local) { Type = known } : (BoundExpr)new BoundLocal(n, local);
            return Capture(n, local, read.Type) ? read : new BoundErrorExpr(n);
        }
        // A default cloned onto a struct sees only the interface's members through `this`, as its template did,
        // so a bare name means the same for every conformer.
        if (_thisType is not null && (Host?.Template?.Interface is not { } cloneOf || LookupMember(cloneOf.Self, name) is not null)
            && LookupMember(_thisType, name) is { } member)
            return BindImplicitThisMember(n, member);
        // A bare payload field reads through `this`, in the states the receiver declares.
        if (_thisType is StructSymbol self && Host is { IsStatic: false } && _c.StatesOf(self) is { } states
            && states.Variants.Any(v => v.Fields.Any(f => f.Name == name)))
            return InLambda(n, name, "it is read through `this`, which a lambda does not capture") ? new BoundErrorExpr(n) : BindValueMember(n, new BoundThis(n, self), n.Name);
        if (Module.Constants.TryGetValue(name, out var constant)) return ConstantRef(n, constant);
        if (Module.Functions.TryGetValue(name, out var fns)) return new BoundMethodGroup(n, null, fns[0]);
        if (Module.Types.TryGetValue(name, out var type))
            return new BoundTypeRef(n, ResolveType(new NamedType(n.Span, [n.Name], [])));
        if (_file.Imports.TryGetValue(name, out var module)) return new BoundModuleRef(n, module);
        if (_c.Builtins.Find(name) is { } builtin) return new BoundMethodGroup(n, null, builtin);
        if (PrimitiveType.ByName.TryGetValue(name, out var primitive)) return new BoundTypeRef(n, primitive);
        if (name is "List" or "Map" or "Span" or "ReadOnlySpan" or "Arena" or "StrBuf" or "IoError" or "Utf8Error" or "ParseError" or "ValidationError" or "ProcessResult" or "Panic")
            return new BoundTypeRef(n, ResolveType(new NamedType(n.Span, [n.Name], [])));
        if (name == "Io") return new BoundTypeRef(n, BuiltinType.Io);
        if (name == "Hex") return new BoundTypeRef(n, BuiltinType.Hex);
        if (name == "Symbol") return new BoundTypeRef(n, BuiltinType.Symbol);
        if (name == "Utf8") return new BoundTypeRef(n, BuiltinType.Utf8);
        if (name == "Path") return new BoundTypeRef(n, BuiltinType.Path);
        if (name == "Target") return new BoundTypeRef(n, BuiltinType.Target);
        if (name == "Env") return new BoundTypeRef(n, BuiltinType.Env);
        ReportUndefined(n.Name);
        return new BoundErrorExpr(n);
    }

    private void ReportUndefined(Name name)
    {
        var candidates = new List<string>();
        for (var s = _scope; s is not null; s = s.Parent) candidates.AddRange(s.Locals.Keys);
        if (_struct is not null)
        {
            candidates.AddRange(_struct.Fields.Select(f => f.Name));
            candidates.AddRange(_struct.Properties.Select(f => f.Name));
            candidates.AddRange(_struct.Methods.Select(f => f.Name));
        }
        candidates.AddRange(Module.Functions.Keys);
        candidates.AddRange(Module.Constants.Keys);
        candidates.AddRange(Module.Types.Keys);
        candidates.AddRange(_file.Imports.Keys);
        candidates.AddRange(["print", "panic", "todo", "Io"]);
        var suggestion = Suggest(name.Text, candidates);
        Report(DiagnosticCodes.UndefinedName, name.Span, [name.Text],
            help: suggestion is null ? null : $"did you mean `{suggestion}`?",
            fix: suggestion is null ? null : new Fix(name.Span, suggestion));
    }

    internal static string? Suggest(string name, IEnumerable<string> candidates)
    {
        string? best = null;
        var bestDistance = int.MaxValue;
        foreach (var c in candidates.Distinct())
        {
            if (c == name) continue;
            var d = Distance(name.ToLowerInvariant(), c.ToLowerInvariant());
            if (d < bestDistance) (best, bestDistance) = (c, d);
        }
        return best is not null && bestDistance <= Math.Max(1, name.Length / 3) ? best : null;
    }

    private static int Distance(string a, string b)
    {
        var d = new int[a.Length + 1, b.Length + 1];
        for (var i = 0; i <= a.Length; i++) d[i, 0] = i;
        for (var j = 0; j <= b.Length; j++) d[0, j] = j;
        for (var i = 1; i <= a.Length; i++)
            for (var j = 1; j <= b.Length; j++)
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
        return d[a.Length, b.Length];
    }

    private BoundExpr ConstantRef(Node syntax, ConstSymbol c)
    {
        _c.BindConstant(c);
        if (c.IsStaticArray) return new BoundStaticArray(syntax, c);
        if (c.Value is null) return new BoundErrorExpr(syntax);
        return ConstantLiteral(syntax, c.Type, c.Value);
    }

    /// <summary>A read of a <c>const</c>; a <c>Symbol</c> one is its literal-table entry (spec 008 R2).</summary>
    private static BoundExpr ConstantLiteral(Node syntax, TypeSymbol type, ConstantValue c) => type == BuiltinType.Symbol
        ? new BoundSymbolLiteral(syntax, (string)c.Value) { Constant = c }
        : new BoundLiteral(syntax, type, c) { Constant = c };

    /// <summary>A member reachable through an implicit <c>this</c>; in a default method, the interface's members.</summary>
    private static Symbol? LookupMember(TypeSymbol t, string name) => t switch
    {
        StructSymbol s => (Symbol?)s.FindField(name) ?? (Symbol?)s.FindProperty(name) ?? s.FindMethods(name).FirstOrDefault(),
        TypeParameterType p => p.Constraints.Select(i => i.FindProperty(name) ?? i.FindMethods(name).FirstOrDefault()).FirstOrDefault(m => m is not null),
        _ => null,
    };

    private BoundExpr BindImplicitThisMember(NameExpr n, Symbol member)
    {
        var inInstance = Host is { IsStatic: false, Kind: FunctionKind.Method or FunctionKind.Getter or FunctionKind.Constructor };
        if (member is FunctionSymbol { IsStatic: true } staticMethod) return new BoundMethodGroup(n, null, staticMethod);
        if (!inInstance)
        {
            Report(DiagnosticCodes.InstanceMemberWithoutThis, n.Span, [n.Name.Text],
                help: Host is { IsStatic: true } ? "call it on an instance; a `static` method has no `this`" : "set it in a constructor; field defaults cannot read other fields");
            return new BoundErrorExpr(n);
        }
        if (InLambda(n, n.Name.Text, "it is read through `this`, which a lambda does not capture")) return new BoundErrorExpr(n);
        var self = new BoundThis(n, _thisType!);
        return member switch
        {
            FieldSymbol f => new BoundField(n, self, f),
            PropertySymbol p => new BoundCall(n, p.Getter, self, [], p.Type),
            FunctionSymbol { Kind: FunctionKind.Getter } g => new BoundCall(n, g, self, [], g.ReturnType),
            FunctionSymbol m => new BoundMethodGroup(n, self, m),
            _ => new BoundErrorExpr(n),
        };
    }

    // ── Members ──────────────────────────────────────────────────────────────────

    private BoundExpr BindMember(MemberExpr m)
    {
        if (m.NullConditional)
        {
            Report(DiagnosticCodes.Unsupported, m.Span, ["`?.`"]);
            return new BoundErrorExpr(m);
        }
        var target = BindExprOrType(m.Target);
        var name = m.Member.Text;
        switch (target)
        {
            case BoundErrorExpr:
                return target;
            case BoundModuleRef mod:
                return BindModuleMember(m, mod.Module);
            case BoundTypeRef { Referenced: ErrorSetSymbol set }:
                if (set.Find(name) is { } variant) return new BoundErrorVariant(m, variant);
                Report(DiagnosticCodes.UnknownVariant, m.Member.Span, [name, set.DisplayName],
                    help: Suggest(name, set.Variants.Select(v => v.Name)) is { } close ? $"did you mean `{close}`?" : null);
                return new BoundErrorExpr(m);
            case BoundTypeRef { Referenced: EnumSymbol e }:
                return BindEnumStatic(m, e);
            case BoundTypeRef { Referenced: UnionSymbol u }:
                return BindVariant(m, m.Member, u);
            case BoundTypeRef { Referenced: StructSymbol s }:
            {
                var method = s.FindMethods(name).FirstOrDefault();
                if (method is { IsStatic: true })
                {
                    CheckAccess(method.Access, method.Owner, method.Module, m.Member);
                    return new BoundMethodGroup(m, null, method);
                }
                Report(DiagnosticCodes.UnknownMember, m.Member.Span, [name, s.Name],
                    help: method is not null || s.FindField(name) is not null || s.FindProperty(name) is not null
                        ? $"call it on a `{s.Name}` value; `{name}` belongs to a value, not the type" : null);
                return new BoundErrorExpr(m);
            }
            case BoundTypeRef { Referenced: BuiltinType { Kind: BuiltinKind.Io } io }:
            {
                if (_c.Builtins.FindIo(name) is not { } member)
                {
                    Report(DiagnosticCodes.UnknownMember, m.Member.Span, [name, io.Name],
                        help: Suggest(name, ["readFile", "readText", "writeFile", "listDir", "run", "runBatch", "monotonicNanos", "stdout", "stderr", "stdin"]) is { } near ? $"did you mean `{near}`?" : null);
                    return new BoundErrorExpr(m);
                }
                // `stdout`, `stderr` and `stdin` read like properties; the rest are called.
                if (member.Builtin is BuiltinFunction.IoStdout or BuiltinFunction.IoStderr or BuiltinFunction.IoStdin) return new BoundCall(m, member, null, [], member.ReturnType);
                return new BoundMethodGroup(m, null, member);
            }
            case BoundTypeRef { Referenced: BuiltinType { Kind: BuiltinKind.Hex } hex }:
                if (name == "encode") return new BoundMethodGroup(m, null, _c.Builtins.HexEncode);
                if (name == "decode") return new BoundMethodGroup(m, null, _c.Builtins.HexDecode);
                Report(DiagnosticCodes.UnknownMember, m.Member.Span, [name, hex.Name], help: Suggest(name, ["encode", "decode"]) is { } closest ? $"did you mean `{closest}`?" : null);
                return new BoundErrorExpr(m);
            case BoundTypeRef { Referenced: BuiltinType { Kind: BuiltinKind.Symbol } symbol }:
                if (name == "intern") return new BoundMethodGroup(m, null, _c.Builtins.SymbolIntern);
                Report(DiagnosticCodes.UnknownMember, m.Member.Span, [name, symbol.Name], help: Suggest(name, ["intern"]) is { } like ? $"did you mean `{like}`?" : null);
                return new BoundErrorExpr(m);
            case BoundTypeRef { Referenced: BuiltinType { Kind: BuiltinKind.Utf8 } utf8 }:
                if (_c.Builtins.FindUtf8(name) is { } decode) return new BoundMethodGroup(m, null, decode);
                Report(DiagnosticCodes.UnknownMember, m.Member.Span, [name, utf8.Name], help: Suggest(name, ["decode", "tryDecode"]) is { } nearest ? $"did you mean `{nearest}`?" : null);
                return new BoundErrorExpr(m);
            case BoundTypeRef { Referenced: BuiltinType { Kind: BuiltinKind.Path } path }:
                if (_c.Builtins.FindPath(name) is { } pathMember) return new BoundMethodGroup(m, null, pathMember);
                Report(DiagnosticCodes.UnknownMember, m.Member.Span, [name, path.Name], help: Suggest(name, ["isRooted", "relative", "fileName", "directoryName", "combine", "fullPath"]) is { } nearPath ? $"did you mean `{nearPath}`?" : null);
                return new BoundErrorExpr(m);
            case BoundTypeRef { Referenced: BuiltinType { Kind: BuiltinKind.Env } env }:
                if (_c.Builtins.FindEnv(name) is { } envMember) return new BoundMethodGroup(m, null, envMember);
                Report(DiagnosticCodes.UnknownMember, m.Member.Span, [name, env.Name], help: Suggest(name, ["get"]) is { } nearEnv ? $"did you mean `{nearEnv}`?" : null);
                return new BoundErrorExpr(m);
            case BoundTypeRef { Referenced: BuiltinType { Kind: BuiltinKind.Target } targetType }:
            {
                if (name is "os" or "arch")
                {
                    if (_c.Options.Target is null) return new BoundHostTarget(m, name == "os");
                    var (os, arch) = HostTarget.Split(_c.Options.Target);
                    var text = new ConstantValue(name == "os" ? os : arch);
                    return new BoundLiteral(m, PrimitiveType.String, text) { Constant = text };
                }
                Report(DiagnosticCodes.UnknownMember, m.Member.Span, [name, targetType.Name], help: Suggest(name, ["os", "arch"]) is { } nearTarget ? $"did you mean `{nearTarget}`?" : null);
                return new BoundErrorExpr(m);
            }
            case BoundTypeRef { Referenced: PrimitiveType { IsString: true } str }:
            {
                if (_c.Builtins.FindString(name) is { } member) return new BoundMethodGroup(m, null, member);
                // Decoding is a codec (J82, Q115): the old `string` statics name the `Utf8` member that replaces them.
                if ((name switch { "from" or "fromUtf8" => "Utf8.decode", "tryFrom" => "Utf8.tryDecode", _ => null }) is { } codec)
                {
                    Report(DiagnosticCodes.RenamedMember, m.Member.Span, [$"string.{name}", codec], label: "the old name",
                        help: $"write `{codec}`", fix: new Fix(m.Span, codec));
                    return new BoundErrorExpr(m);
                }
                Report(DiagnosticCodes.UnknownMember, m.Member.Span, [name, str.Name],
                    help: Suggest(name, ["invalidUtf8At"]) is { } near ? $"did you mean `{near}`?" : null);
                return new BoundErrorExpr(m);
            }
            case BoundTypeRef t:
                Report(DiagnosticCodes.UnknownMember, m.Member.Span, [name, t.Referenced.DisplayName]);
                return new BoundErrorExpr(m);
        }
        return BindValueMember(m, target, m.Member);
    }

    /// <summary>A unit variant is a value; a variant with a payload is called like a function.</summary>
    private BoundExpr BindVariant(Node syntax, Name member, UnionSymbol u)
    {
        if (u.Find(member.Text) is { } v)
            return v.Fields.Count == 0 ? new BoundUnionNew(syntax, v, []) : new BoundVariantGroup(syntax, v);
        Report(DiagnosticCodes.UnknownUnionVariant, member.Span, [member.Text, u.Name],
            help: Suggest(member.Text, u.Variants.Select(x => x.Name)) is { } close ? $"did you mean `{close}`?" : null);
        return new BoundErrorExpr(syntax);
    }

    /// <summary><c>Color.red</c>, <c>Color.count</c>, <c>Color.values</c>, <c>Color.fromOrdinal</c> and <c>Color.parse</c>.</summary>
    private BoundExpr BindEnumStatic(MemberExpr m, EnumSymbol e)
    {
        var name = m.Member.Text;
        if (e.Find(name) is { } member) return new BoundEnumMember(m, member);
        switch (name)
        {
            case "count":
                var count = new ConstantValue((Int128)e.Members.Count);
                return new BoundLiteral(m, PrimitiveType.I64, count) { Constant = count };
            case "values":
                return new BoundEnumValues(m, e, new SpanType(e, readOnly: true));
            case "fromOrdinal":
                return new BoundMethodGroup(m, null, e.FromOrdinal);
            case "parse":
                return new BoundMethodGroup(m, null, e.Parse);
        }
        Report(DiagnosticCodes.UnknownEnumMember, m.Member.Span, [name, e.Name],
            help: Suggest(name, e.Members.Select(v => v.Name).Concat(["count", "values", "fromOrdinal", "parse"])) is { } close ? $"did you mean `{close}`?" : null);
        return new BoundErrorExpr(m);
    }

    private BoundExpr BindModuleMember(MemberExpr m, ModuleSymbol module)
    {
        var name = m.Member.Text;
        if (module.Constants.TryGetValue(name, out var c))
        {
            CheckAccess(Compilation.AccessOf(c.Decl is { } ? Modifiers.None : Modifiers.None), null, module, m.Member, publicRequired: true);
            return ConstantRef(m, c);
        }
        if (module.Functions.TryGetValue(name, out var fns))
        {
            CheckAccess(fns[0].Access, null, module, m.Member);
            return new BoundMethodGroup(m, null, fns[0]);
        }
        if (module.Types.ContainsKey(name))
            return new BoundTypeRef(m, ResolveType(new NamedType(m.Span, [((NameExpr)m.Target).Name, m.Member], [])));
        Report(DiagnosticCodes.UnknownMember, m.Member.Span, [name, "module " + module.Name]);
        return new BoundErrorExpr(m);
    }

    /// <summary>Reports a member the current code may not see. Module constants are always module-private in v0.1.</summary>
    private void CheckAccess(Accessibility access, StructSymbol? owner, ModuleSymbol module, Name name, bool publicRequired = false)
    {
        if (publicRequired && module != Module)
        {
            Report(DiagnosticCodes.NotVisible, name.Span, [name.Text], label: "module constants are not visible outside their module");
            return;
        }
        switch (access)
        {
            case Accessibility.Private when owner is not null && owner != _struct:
                Report(DiagnosticCodes.NotVisible, name.Span, [name.Text], label: $"`private` to `{owner.Name}`");
                break;
            case Accessibility.Module or Accessibility.Private when module != Module && module != _c.Builtins.Module:
                Report(DiagnosticCodes.NotVisible, name.Span, [name.Text], label: "not `public`",
                    help: $"mark `{name.Text}` `public` to use it from another module");
                break;
        }
    }

    private BoundExpr BindValueMember(Node syntax, BoundExpr target, Name member)
    {
        var name = member.Text;
        // A state type has its struct's members.
        switch (target.Type is StateType state ? state.Struct : target.Type)
        {
            case StructSymbol s:
            {
                if (s.FindField(name) is { } f)
                {
                    CheckAccess(f.Access, s, s.Module, member);
                    return new BoundField(syntax, target, f);
                }
                if (s.FindProperty(name) is { } p)
                {
                    CheckAccess(p.Access, s, s.Module, member);
                    return new BoundCall(syntax, p.Getter, target, [], p.Type);
                }
                if (s.FindMethods(name).FirstOrDefault() is { } method)
                {
                    CheckAccess(method.Access, s, s.Module, member);
                    return new BoundMethodGroup(syntax, method.IsStatic ? null : target, method);
                }
                if (_c.StatesOf(s) is { } states && BindPayloadField(syntax, target, states, member) is { } payload) return payload;
                break;
            }
            case ErrorSetSymbol or ErrorUnionType when target is BoundLocal el && _errorNarrowed.TryGetValue(el.Local, out var errorCase)
                && errorCase.Fields.FindIndex(f => f.Name == name) is var field and >= 0:
                return new BoundErrorField(syntax, target, errorCase, field);
            case UnionSymbol when target is BoundLocal l && _narrowed.TryGetValue(l.Local, out var narrowed) && narrowed.Fields.FindIndex(f => f.Name == name) is var index and >= 0:
                return new BoundUnionField(syntax, target, narrowed, index);
            case UnionSymbol u when u.Variants.Any(v => v.Fields.Any(f => f.Name == name)):
                Report(DiagnosticCodes.PayloadNeedsPattern, member.Span, [u.Name, name],
                    help: "match the variant and bind the field, as in `case .Variant(let x):`");
                return new BoundErrorExpr(syntax);
            case Symbols.TupleType t:
            {
                var index = t.IndexOf(name);
                if (index >= 0) return new BoundTupleElement(syntax, target, index, t.Fields[index].Type);
                break;
            }
            case EnumSymbol e when e.FindField(name) is { } f:
                return new BoundBuiltinCall(syntax, BuiltinMember.EnumField, target, [Literal(syntax, PrimitiveType.I32, (Int128)f.Index)], f.Type);
            // Any other member reads through the pointer: `o.field`, `o.method()`.
            case OwnedType o when name != "value" && !o.Inner.IsError:
                return BindValueMember(syntax, new BoundBuiltinCall(target.Syntax, BuiltinMember.OwnedValue, target, [], o.Inner), member);
            // `take()` is the one member an optional has itself.
            case OptionalType o when name != "take":
                Report(DiagnosticCodes.MemberOfOptional, member.Span, [name, o.DisplayName],
                    help: $"check it first: `if ({Source.GetText(target.Syntax.Span)} is {{ }} value) {{ … value.{name} … }}`");
                return new BoundErrorExpr(syntax);
            case ErrorType:
                return new BoundErrorExpr(syntax);
            // Inside a generic body, `t.area()` is the interface member; each instance rebinds it to the conformer's.
            case TypeParameterType tp when tp.Constraints.Count > 0:
            {
                if (tp.Constraints.Select(i => i.FindProperty(name) ?? i.FindMethods(name).FirstOrDefault()).FirstOrDefault(m => m is not null) is { } m)
                    return BindInterfaceMember(syntax, target, m, member);
                var self = tp.Constraints[0].Self == tp;
                Report(DiagnosticCodes.ConstraintMember, member.Span, [tp.Name, name, string.Join(", ", tp.Constraints.Select(i => $"`{i.DisplayName}`"))],
                    help: self
                        ? $"declare `{name}` in `{tp.Constraints[0].DisplayName}`; a default method sees only its interface's members"
                        : $"add an interface declaring `{name}` to the `where` clause");
                return new BoundErrorExpr(syntax);
            }
            case TypeParameterType tp:
                Report(DiagnosticCodes.TypeParameterMember, member.Span, [name, tp.Name],
                    help: $"add `where {tp.Name} : …` naming an interface that declares `{name}`, or use a concrete type");
                return new BoundErrorExpr(syntax);
            case DynRefType d when (d.Interface.FindProperty(name) ?? d.Interface.FindMethods(name).FirstOrDefault()) is { } m:
            {
                if (d.ReadOnly && m.IsMutating)
                {
                    Report(DiagnosticCodes.DynReadonlyMutation, member.Span, [name, d.Interface.DisplayName], label: "declared `ref this`",
                        help: $"take `dyn ref {d.Interface.DisplayName}`");
                    return new BoundErrorExpr(syntax);
                }
                return BindInterfaceMember(syntax, target, m, member);
            }
        }
        if (BuiltinMembers.Property(target.Type, name) is { } builtin)
            return new BoundBuiltinCall(syntax, builtin.Member, target, [],
                builtin.Type is SpanType span && target is BoundStaticArray ? new SpanType(span.Element, readOnly: true) : PropertyType(builtin.Member, builtin.Type));
        // `length` and `isEmpty` of an untrusted value are clean facts (spec §15.1).
        if (target.Type is UntrustedType untrusted && name is "length" or "isEmpty" && BuiltinMembers.Property(untrusted.Inner, name) is { } fact)
            return new BoundBuiltinCall(syntax, fact.Member, Fact(target), [], fact.Type);
        if (BuiltinMembers.HasMethod(target.Type, name))
            return new BoundBuiltinGroup(syntax, target, name);
        if (ReportRenamed(target.Type, member)) return new BoundErrorExpr(syntax);
        if (name == "take")
        {
            Report(DiagnosticCodes.TakeUnsupported, member.Span, [target.Type.DisplayName],
                help: "copy it, or move the whole local; only an owned collection or optional has an empty value to leave behind");
            return new BoundErrorExpr(syntax);
        }
        // Revision 0011: regexes are not in v0.1.
        Report(DiagnosticCodes.UnknownMember, member.Span, [name, target.Type.DisplayName],
            label: name == "matches" && target.Type.IsString ? "`matches` arrives with `std.regex` after v0.1" : null,
            help: MemberSuggestion(target.Type, name));
        return new BoundErrorExpr(syntax);
    }

    /// <summary>A member of an interface, reached through a constrained type parameter or a <c>dyn ref</c>.</summary>
    private BoundExpr BindInterfaceMember(Node syntax, BoundExpr target, FunctionSymbol m, Name member)
    {
        if (m.TypeParameters.Count > 0)
        {
            Report(DiagnosticCodes.Unsupported, member.Span, ["calling a generic interface method"]);
            return new BoundErrorExpr(syntax);
        }
        if (m.Kind == FunctionKind.Getter) return new BoundCall(syntax, m, target, [], ReturnTypeThrough(target.Type, m));
        return new BoundMethodGroup(syntax, target, m);
    }

    /// <summary>The return type of interface member <paramref name="m"/> called on <paramref name="receiver"/>: an associated type <c>Item</c> is <c>T.Item</c>.</summary>
    private TypeSymbol ReturnTypeThrough(TypeSymbol receiver, FunctionSymbol m) =>
        receiver is TypeParameterType tp && m.Interface is { } i && i.Self != tp && m.ReturnType is TypeParameterType r && i.AssociatedTypes.Contains(r)
            ? _c.AssociatedType(tp, r.Name)
            : m.ReturnType;

    /// <summary>The call whose callee is being bound, so <see cref="ReportRenamed"/> can rewrite its arguments.</summary>
    private CallExpr? _calleeOf;

    /// <summary>BYN2230 and BYN2031: a member under its round-1 or C# name; the fix writes the new one.</summary>
    private bool ReportRenamed(TypeSymbol type, Name member)
    {
        var name = member.Text;
        if (name is "toString" or "ToString")
        {
            Report(DiagnosticCodes.ToStringCall, member.Span, [], label: "not a Bayan method",
                help: "call `format()`, or interpolate the value: `$\"{x}\"`", fix: new Fix(member.Span, "format"));
            return true;
        }
        if (RenamedCall(type, member) is { } shape)
        {
            Report(DiagnosticCodes.RenamedMember, member.Span, [shape.Old, shape.New], label: "the old name",
                help: $"write `{shape.Fix?.Replacement ?? shape.New}`", fix: shape.Fix);
            return true;
        }
        var renamed = (type, name) switch
        {
            (ListType, "add") => "append",
            (BuiltinType { Kind: BuiltinKind.StrBuf }, "asString") => "finish",
            ({ IsString: true }, "offset") => "position",
            (ListType or SpanType, "toList") => "copy",
            // An array has no `copy()`; its span does.
            (Symbols.ArrayType, "toList") => "span.copy",
            _ => null,
        };
        if (renamed is null) return false;
        var call = name is "asString" or "toList" ? "()" : "";
        Report(DiagnosticCodes.RenamedMember, member.Span, [name + call, renamed + call], label: "the old name",
            help: $"rename it `{renamed}`",
            fix: new Fix(member.Span, renamed));
        return true;
    }

    /// <summary>
    /// The renames that change the call's shape (J72, round 3 §27): the fix rewrites the member and its arguments,
    /// so it is written only when the member is called with the old arguments.
    /// </summary>
    private (string Old, string New, Fix? Fix)? RenamedCall(TypeSymbol type, Name member)
    {
        var call = _calleeOf is { Callee: MemberExpr me } c && me.Member == member ? c : null;
        var args = call?.Args.Select(a => a.Name is null && a.Modifier == ArgModifier.None ? Source.GetText(a.Value.Span) : null).ToArray() ?? [];
        var tail = new TextSpan(member.Span.Start, (call?.Span.End ?? 0) - member.Span.Start);
        switch (type, member.Text)
        {
            case (ListType, "removeRange"):
                return ("removeRange", "remove(from:until:)",
                    args is [{ } from, { } until] ? new Fix(tail, $"remove(from: {from}, until: {until})") : null);
            case (ListType, "replaceRange"):
                return ("replaceRange", "replace(at:by:)", call is { Args: [{ Name: null, Modifier: ArgModifier.None } at, { Name: null, Modifier: ArgModifier.None } by] }
                    ? new Fix(tail, $"replace(at: {Source.GetText(at.Value.Span)}, by: {Source.GetText(by.Value.Span)})") : null);
            case (MapType, "getOr"):
                // `??` binds looser than arithmetic, so the fix keeps the call one operand.
                return ("getOr", "tryGet(at:) ??", args is [{ } key, { } fallback] && call is { Callee: MemberExpr { Target: var map } }
                    ? new Fix(call.Span, $"({Source.GetText(map.Span)}.tryGet(at: {key}) ?? {fallback})") : null);
            case (ListType or Symbols.ArrayType, "asSpan"):
                return ("asSpan()", "span", call is { Args.Length: 0 } ? new Fix(tail, "span") : null);
            case ({ IsString: true }, "toUtf16"):
                return ("toUtf16()", "utf16", call is { Args.Length: 0 } ? new Fix(tail, "utf16") : null);
            case ({ IsInteger: true }, "toHex" or "toBinary" or "toOctal"):
            {
                var radix = member.Text[2..].ToLowerInvariant();
                return ($"{member.Text}()", $"string(x, as: .{radix})", call is { Args.Length: 0, Callee: MemberExpr target }
                    ? new Fix(call.Span, $"string({Source.GetText(target.Target.Span)}, as: .{radix})") : null);
            }
        }
        return null;
    }

    private static string? MemberSuggestion(TypeSymbol type, string name)
    {
        if (name is "Length" or "Count" or "count" or "size" or "len") return "use `length` for the size of anything";
        IEnumerable<string> names = type switch
        {
            StructSymbol s => s.Fields.Select(f => f.Name).Concat(s.Properties.Select(p => p.Name)).Concat(s.Methods.Select(m => m.Name)),
            EnumSymbol e => e.Fields.Select(f => f.Name).Concat(BuiltinMembers.Names(type)),
            _ => BuiltinMembers.Names(type),
        };
        return Suggest(name, names) is { } s2 ? $"did you mean `{s2}`?" : null;
    }

    private BoundExpr BindTargetMember(TargetMemberExpr t, TypeSymbol? expected)
    {
        var target = expected switch
        {
            OptionalType o => o.Inner,
            ResultType r => r.Error,
            _ => expected,
        };
        switch (target)
        {
            // `.member` is never a lambda (spec 007 R7).
            case FunctionType:
                Report(DiagnosticCodes.MemberNotLambda, t.Span, [t.Member.Text], help: $"write `it.{t.Member.Text}`",
                    fix: new Fix(t.Span, "it." + t.Member.Text));
                return new BoundErrorExpr(t);
            case ErrorSetSymbol set:
                if (set.Find(t.Member.Text) is { } v) return new BoundErrorVariant(t, v);
                Report(DiagnosticCodes.UnknownVariant, t.Member.Span, [t.Member.Text, set.DisplayName]);
                return new BoundErrorExpr(t);
            case ErrorUnionType u:
            {
                var matches = u.Sets.Select(s => s.Find(t.Member.Text)).Where(v => v is not null && !u.Excluded.Contains(v)).ToList();
                if (matches.Count == 1) return new BoundErrorVariant(t, matches[0]!);
                if (matches.Count > 1)
                    Report(DiagnosticCodes.AmbiguousVariant, t.Span, [t.Member.Text, string.Join("`, `", matches.Select(m => m!.Set.DisplayName))]);
                else
                    Report(DiagnosticCodes.UnknownVariant, t.Member.Span, [t.Member.Text, u.DisplayName]);
                return new BoundErrorExpr(t);
            }
            case EnumSymbol e:
                if (e.Find(t.Member.Text) is { } member) return new BoundEnumMember(t, member);
                Report(DiagnosticCodes.UnknownEnumMember, t.Member.Span, [t.Member.Text, e.Name],
                    help: Suggest(t.Member.Text, e.Members.Select(v => v.Name)) is { } close ? $"did you mean `{close}`?" : null);
                return new BoundErrorExpr(t);
            case UnionSymbol un:
                return BindVariant(t, t.Member, un);
            case StructSymbol s when s.FindMethods(t.Member.Text).FirstOrDefault() is { IsStatic: true } m:
                return new BoundMethodGroup(t, null, m);
            case null or ErrorType:
                if (target is null)
                    Report(DiagnosticCodes.NoTargetType, t.Span, [t.Member.Text]);
                return new BoundErrorExpr(t);
            default:
                Report(DiagnosticCodes.UnknownMember, t.Member.Span, [t.Member.Text, target.DisplayName]);
                return new BoundErrorExpr(t);
        }
    }

    // ── Indexing ─────────────────────────────────────────────────────────────────

    private BoundExpr BindIndex(IndexExpr i)
    {
        var target = BindExpr(i.Target);
        if (target.Type.IsError) { BindExpr(i.Index); return new BoundErrorExpr(i); }
        if (target.Type.IsString)
        {
            if (i.Index is RangeExpr range) return BindSlice(i, target, range, PrimitiveType.String);
            ReportStringIndexer(i);
            BindExpr(i.Index);
            return new BoundErrorExpr(i);
        }
        if (target.Type is MapType map)
        {
            var key = BindConverted(i.Index, map.KeyType);
            return key.Type.IsError ? new BoundErrorExpr(i) : new BoundBuiltinCall(i, BuiltinMember.MapIndex, target, [key], map.ValueType);
        }
        TypeSymbol? element = target.Type switch
        {
            SpanType s => s.Element,
            ListType l => l.Element,
            Symbols.ArrayType a => a.Element,
            _ => null,
        };
        if (element is null)
        {
            Report(DiagnosticCodes.NotIndexable, i.Target.Span, [target.Type.DisplayName]);
            BindExpr(i.Index);
            return new BoundErrorExpr(i);
        }
        if (i.Index is RangeExpr r) return BindSlice(i, target, r, new SpanType(element, target.Type is SpanType { ReadOnly: true } || target is BoundStaticArray));
        var fromEnd = i.Index is UnaryExpr { Op: UnaryOp.FromEnd };
        var indexSyntax = i.Index is UnaryExpr { Op: UnaryOp.FromEnd } u ? u.Operand : i.Index;
        var index = BindIndexValue(indexSyntax);
        if (target.Type is Symbols.ArrayType array && index.Constant is { Value: Int128 n })
        {
            var effective = fromEnd ? array.Length - n : n;
            if (effective < 0 || effective >= array.Length || (fromEnd && n == 0))
                Report(DiagnosticCodes.IndexOutOfRange, i.Index.Span, [fromEnd ? "^" + n : n.ToString(), array.DisplayName]);
        }
        return new BoundIndex(i, target, index, fromEnd, element);
    }

    private BoundExpr BindIndexValue(Expr syntax)
    {
        var index = BindExpr(syntax, PrimitiveType.I64);
        if (index.Type.IsError) return index;
        if (!index.Type.IsInteger)
        {
            Report(DiagnosticCodes.TypeMismatch, syntax.Span, [index.Type.DisplayName, "an integer index"]);
            return new BoundErrorExpr(syntax);
        }
        if (index.Type.IsUnsignedInteger && ((PrimitiveType)index.Type).Bits == 64)
            return new BoundConversion(syntax, index, PrimitiveType.I64, ConversionKind.NumericCast);
        return Convert(index, PrimitiveType.I64, syntax.Span);
    }

    private BoundExpr BindSlice(IndexExpr i, BoundExpr target, RangeExpr r, TypeSymbol type)
    {
        BoundExpr? Bound(Expr? e, out bool fromEnd)
        {
            fromEnd = e is UnaryExpr { Op: UnaryOp.FromEnd };
            if (e is null) return null;
            return BindIndexValue(e is UnaryExpr { Op: UnaryOp.FromEnd } u ? u.Operand : e);
        }
        var start = Bound(r.Start, out var startFromEnd);
        var end = Bound(r.End, out var endFromEnd);
        return new BoundSlice(i, target, start, startFromEnd, end, endFromEnd, r.Inclusive, type);
    }

    /// <summary>
    /// The text <paramref name="e"/> contributes to the interpolation that replaces a string <c>+</c>:
    /// a plain literal or an interpolation inlines its contents, anything else becomes a hole.
    /// </summary>
    private string InterpolationSource(Expr e)
    {
        var text = Source.GetText(e.Span);
        return e switch
        {
            LiteralExpr { Kind: LiteralKind.String } when text.IndexOfAny(['{', '}']) < 0 => text[1..^1],
            InterpolatedExpr when text.StartsWith("$\"", StringComparison.Ordinal) && !text.StartsWith("$\"\"\"", StringComparison.Ordinal) => text[2..^1],
            _ => "{" + text + "}",
        };
    }

    /// <summary>BYN2201: strings have no indexer (spec §3.2); offer the four alternatives.</summary>
    private void ReportStringIndexer(IndexExpr i)
    {
        var s = Source.GetText(i.Target.Span);
        var index = Source.GetText(i.Index.Span);
        Report(DiagnosticCodes.StringIndexer, TextSpan.FromBounds(i.Target.Span.End, i.Span.End), [],
            label: "strings have no indexer",
            help: $"""
                read a byte, slice, or take characters; `string` is UTF-8, so position {index} is not a character:
                  `{s}.bytes[{index}]`       the byte at offset {index} (applied below)
                  `{s}[a..b]`          the text between byte offsets a and b
                  `{s}.runes.take(n)`  the first n runes
                  `{s}.graphemes`      user-perceived characters
                """,
            fix: new Fix(i.Span, $"{s}.bytes[{index}]"));
    }

    // ── Operators ────────────────────────────────────────────────────────────────

    private BoundExpr BindUnary(UnaryExpr u, TypeSymbol? expected)
    {
        if (u.Op == UnaryOp.Negate && u.Operand is LiteralExpr { Kind: LiteralKind.Integer or LiteralKind.Float } lit)
            return BindLiteral(lit, expected, negate: true, whole: u.Span) is var l && l is BoundLiteral bl ? bl with { Syntax = u } : l;
        if (u.Op == UnaryOp.FromEnd)
        {
            Report(DiagnosticCodes.FromEndOutsideIndex, u.Span, []);
            return new BoundErrorExpr(u);
        }
        var operand = BindExpr(u.Operand, u.Op == UnaryOp.Not ? PrimitiveType.Bool : expected);
        var t = operand.Type;
        if (t.IsError) return new BoundErrorExpr(u);
        if (t is TypeParameterType tp) return TypeParameterOperator(u, UnaryText(u.Op), tp);
        var ok = u.Op switch
        {
            UnaryOp.Not => t.IsBool,
            UnaryOp.Complement => t.IsInteger,
            UnaryOp.Negate => t.IsSignedInteger || t.IsFloat,
            UnaryOp.Plus => t.IsNumeric,
            _ => false,
        };
        if (!ok)
        {
            Report(DiagnosticCodes.BadUnaryOperand, u.Span, [UnaryText(u.Op), t.DisplayName],
                help: u.Op == UnaryOp.Negate && t.IsUnsignedInteger ? "convert to a signed type first; unsigned values cannot be negative" : null);
            return new BoundErrorExpr(u);
        }
        var result = new BoundUnary(u, u.Op, operand, t);
        if (operand.Constant is { } c && ConstantFolding.Unary(u.Op, c, t) is { } folded)
        {
            if (folded.Value is Int128 n && t is PrimitiveType p && !Fits(n, p))
            {
                Report(DiagnosticCodes.ConstantOverflow, u.Span, [t.DisplayName]);
                return new BoundErrorExpr(u);
            }
            return result with { Constant = folded };
        }
        return result;
    }

    private static string UnaryText(UnaryOp op) => op switch
    {
        UnaryOp.Not => "!",
        UnaryOp.Complement => "~",
        UnaryOp.Negate => "-",
        UnaryOp.Plus => "+",
        _ => "^",
    };

    internal static string BinaryText(BinaryOp op) => op switch
    {
        BinaryOp.Add => "+", BinaryOp.Sub => "-", BinaryOp.Mul => "*", BinaryOp.Div => "/", BinaryOp.Rem => "%",
        BinaryOp.AddWrap => "+%", BinaryOp.SubWrap => "-%", BinaryOp.MulWrap => "*%",
        BinaryOp.AddSat => "+|", BinaryOp.SubSat => "-|", BinaryOp.MulSat => "*|",
        BinaryOp.Shl => "<<", BinaryOp.Shr => ">>",
        BinaryOp.Lt => "<", BinaryOp.Gt => ">", BinaryOp.Le => "<=", BinaryOp.Ge => ">=", BinaryOp.Eq => "==", BinaryOp.Ne => "!=",
        BinaryOp.BitAnd => "&", BinaryOp.BitOr => "|", BinaryOp.BitXor => "^",
        BinaryOp.LogicalAnd => "&&", BinaryOp.LogicalOr => "||", BinaryOp.Coalesce => "??",
        _ => op.ToString(),
    };

    internal static bool IsComparison(BinaryOp op) => op is BinaryOp.Lt or BinaryOp.Gt or BinaryOp.Le or BinaryOp.Ge or BinaryOp.Eq or BinaryOp.Ne;

    private BoundExpr BindBinary(BinaryExpr b, TypeSymbol? expected)
    {
        var nullTested = b.Op is BinaryOp.Eq or BinaryOp.Ne
            ? b.Right is LiteralExpr { Kind: LiteralKind.Null } ? b.Left : b.Left is LiteralExpr { Kind: LiteralKind.Null } ? b.Right : null
            : null;
        switch (b.Op)
        {
            case BinaryOp.LogicalAnd or BinaryOp.LogicalOr:
            {
                var l = BindConverted(b.Left, PrimitiveType.Bool);
                var r = Narrowed(l, b.Op == BinaryOp.LogicalAnd, () => BindConverted(b.Right, PrimitiveType.Bool));
                var result = new BoundBinary(b, b.Op, l, r, PrimitiveType.Bool);
                if (l.Constant is { } lc && r.Constant is { } rc)
                    return result with { Constant = ConstantFolding.Binary(b.Op, lc, rc, PrimitiveType.Bool) };
                return result;
            }
            case BinaryOp.Coalesce:
                return BindCoalesce(b, expected);
        }

        var arithmetic = !IsComparison(b.Op);
        // A comparison of an untrusted value is a clean fact (spec §15.1): it compares the inner values.
        BoundExpr Operand(Expr e, TypeSymbol? hint) => arithmetic ? BindExpr(e, hint) : Fact(BindExpr(e, hint));
        BoundExpr left, right;
        // A string literal facing a `Symbol` binds as one (spec 008 R2), so the other operand goes first.
        if (IsUntypedLiteral(b.Left) && !IsUntypedLiteral(b.Right) || !arithmetic && IsStringLiteral(b.Left) && !IsStringLiteral(b.Right))
        {
            right = Operand(b.Right, arithmetic ? expected : null);
            left = BindExpr(b.Left, right.Type is OptionalType o ? o.Inner : right.Type);
        }
        else
        {
            left = Operand(b.Left, arithmetic ? expected : null);
            var hint = b.Op is BinaryOp.Shl or BinaryOp.Shr ? PrimitiveType.I32 : left.Type is OptionalType o ? o.Inner : left.Type;
            right = Operand(b.Right, hint);
        }
        // `x is null` is valid only on an optional `x`: for `null == null` or a non-optional operand BYN2208 reports alone.
        if (nullTested is not null && nullTested is not LiteralExpr { Kind: LiteralKind.Null } && (nullTested == b.Left ? left : right).Type is OptionalType)
        {
            var test = Source.GetText(nullTested.Span) + (b.Op == BinaryOp.Eq ? " is null" : " is not null");
            Report(DiagnosticCodes.NullEquality, b.Span, [], help: $"write `{test}`", fix: new Fix(b.Span, test));
        }
        if (left.Type.IsError || right.Type.IsError) return new BoundErrorExpr(b);
        if (b.Op is not (BinaryOp.Eq or BinaryOp.Ne) && (TypeParameterOf(left.Type) ?? TypeParameterOf(right.Type)) is { } tp)
            return TypeParameterOperator(b, BinaryText(b.Op), tp);

        if (b.Op is BinaryOp.Shl or BinaryOp.Shr)
        {
            if (!left.Type.IsInteger || !right.Type.IsInteger)
                return BadOperands(b, left, right);
            var shift = new BoundBinary(b, b.Op, left, right, left.Type);
            return Fold(shift, left, right, left.Type);
        }

        Constrain(left, right, b.Right);
        Constrain(right, left, b.Left);
        var common = Conversions.Common(left.Type, right.Type);
        if (common is null && left.Constant is { Value: Int128 } && right.Type.IsInteger) common = right.Type;
        if (common is null && right.Constant is { Value: Int128 } && left.Type.IsInteger) common = left.Type;
        if (common is null) return BadOperands(b, left, right);

        if (common.IsString && b.Op == BinaryOp.Add)
        {
            // Two plain literals merge into one literal, which (unlike an interpolation) stays a constant.
            if (IsPlainStringLiteral(b.Left) && IsPlainStringLiteral(b.Right))
            {
                var merged = Source.GetText(b.Left.Span)[..^1] + Source.GetText(b.Right.Span)[1..];
                Report(DiagnosticCodes.StringConcatenation, b.Span, [], help: $"write one literal: `{merged}`", fix: new Fix(b.Span, merged));
                return new BoundErrorExpr(b);
            }
            Report(DiagnosticCodes.StringConcatenation, b.Span, [],
                help: "write the parts as one interpolation: `$\"{a}{b}\"`, or `buf.append($\"{a}{b}\")` in a loop",
                fix: new Fix(b.Span, $"$\"{InterpolationSource(b.Left)}{InterpolationSource(b.Right)}\""));
            return new BoundErrorExpr(b);
        }

        // A bare case compares by code (Q312), so it needs no payload equality (Q344).
        var byCode = b.Op is BinaryOp.Eq or BinaryOp.Ne && common is ErrorSetSymbol or ErrorUnionType && (IsBareCase(left) || IsBareCase(right));
        if (!byCode && !OperatorApplies(b.Op, common)) return BadOperands(b, left, right);
        left = Convert(left, common, b.Left.Span, constrains: false);
        right = Convert(right, common, b.Right.Span, constrains: false);
        var type = IsComparison(b.Op) ? PrimitiveType.Bool : common;
        var bound = new BoundBinary(b, b.Op, left, right, type);
        return Fold(bound, left, right, common);
    }

    /// <summary>The inner value of an <c>Untrusted&lt;T&gt;</c>, read for a fact; other values unchanged.</summary>
    private static BoundExpr Fact(BoundExpr value) =>
        value.Type is UntrustedType u ? new BoundConversion(value.Syntax, value, u.Inner, ConversionKind.Identity) : value;

    private static bool IsStringLiteral(Expr e) => e is LiteralExpr { Kind: LiteralKind.String or LiteralKind.RawString } or ParenExpr { Inner: LiteralExpr { Kind: LiteralKind.String or LiteralKind.RawString } };

    private bool IsPlainStringLiteral(Expr e) =>
        e is LiteralExpr { Kind: LiteralKind.String } && Source.GetText(e.Span) is var text
        && text.StartsWith('"') && !text.StartsWith("\"\"\"", StringComparison.Ordinal);

    private static TypeParameterType? TypeParameterOf(TypeSymbol t) =>
        t as TypeParameterType ?? (t as OptionalType)?.Inner as TypeParameterType;

    /// <summary>Without constraints a <c>T</c> is only copied, compared with <c>==</c>/<c>!=</c>, passed, stored, returned and formatted.</summary>
    private BoundErrorExpr TypeParameterOperator(Node syntax, string op, TypeParameterType tp)
    {
        Report(DiagnosticCodes.TypeParameterOperator, syntax.Span, [op, tp.Name],
            help: $"use a `{tp.Name}` only to copy, compare with `==` and `!=`, pass, store, return and format; type parameters have no constraints");
        return new BoundErrorExpr(syntax);
    }

    private BoundExpr Fold(BoundBinary bound, BoundExpr left, BoundExpr right, TypeSymbol operandType)
    {
        if (left.Constant is not { } lc || right.Constant is not { } rc) return bound;
        var folded = ConstantFolding.Binary(bound.Op, lc, rc, operandType);
        if (folded is null)
        {
            if (bound.Op is BinaryOp.Div or BinaryOp.Rem && rc.Value is Int128 { } z && z == 0)
            {
                Report(DiagnosticCodes.DivisionByZero, bound.Syntax.Span, []);
                return new BoundErrorExpr(bound.Syntax);
            }
            return bound;
        }
        if (folded.Value is Int128 n && bound.Type is PrimitiveType { IsInteger: true } p && !Fits(n, p))
        {
            Report(DiagnosticCodes.ConstantOverflow, bound.Syntax.Span, [p.Name]);
            return new BoundErrorExpr(bound.Syntax);
        }
        return bound with { Constant = folded };
    }

    private static bool IsBareCase(BoundExpr e) => e is BoundErrorVariant { Payload.Length: 0 } or BoundConversion { Operand: BoundErrorVariant { Payload.Length: 0 } };

    private static bool OperatorApplies(BinaryOp op, TypeSymbol t) => op switch
    {
        BinaryOp.Add or BinaryOp.Sub or BinaryOp.Mul or BinaryOp.Div or BinaryOp.Rem => t.IsNumeric,
        BinaryOp.AddWrap or BinaryOp.SubWrap or BinaryOp.MulWrap or BinaryOp.AddSat or BinaryOp.SubSat or BinaryOp.MulSat => t.IsInteger,
        BinaryOp.BitAnd or BinaryOp.BitOr or BinaryOp.BitXor => t.IsInteger || t.IsBool,
        BinaryOp.Lt or BinaryOp.Gt or BinaryOp.Le or BinaryOp.Ge => t.IsNumeric || t == PrimitiveType.Char || t.IsString || t == BuiltinType.Symbol,
        BinaryOp.Eq or BinaryOp.Ne => IsEquatable(t),
        _ => false,
    };

    internal static bool IsEquatable(TypeSymbol t) => IsEquatable(t, []);

    /// <summary><c>Owned</c> compares the values it points to; a type reached again through one is assumed equatable.</summary>
    private static bool IsEquatable(TypeSymbol t, HashSet<TypeSymbol> seen) => t switch
    {
        PrimitiveType p => p.Kind is not (PrimitiveKind.Void or PrimitiveKind.Never),
        OptionalType o => IsEquatable(o.Inner, seen),
        ResultType r => IsEquatable(r.Value, seen) && IsEquatable(r.Error, seen),
        OwnedType o => IsEquatable(o.Inner, seen),
        UntrustedType u => IsEquatable(u.Inner, seen),
        EnumSymbol or TypeParameterType or BuiltinType { Kind: BuiltinKind.Symbol } => true,
        ErrorSetSymbol s => !seen.Add(s) || s.Variants.All(v => v.Fields.All(f => IsEquatable(f.Type, seen))),
        ErrorUnionType u => u.Sets.All(s => IsEquatable(s, seen)),
        StructSymbol s => !seen.Add(s) || s.Fields.All(f => IsEquatable(f.Type, seen)),
        UnionSymbol u => !seen.Add(u) || u.Variants.All(v => v.Fields.All(f => IsEquatable(f.Type, seen))),
        Symbols.TupleType tt => tt.Fields.All(f => IsEquatable(f.Type, seen)),
        Symbols.ArrayType a => IsEquatable(a.Element, seen),
        _ => false,
    };

    private BoundExpr BadOperands(BinaryExpr b, BoundExpr left, BoundExpr right)
    {
        string? help = null;
        if (left.Type.IsNumeric && right.Type.IsNumeric)
            help = $"convert one side explicitly, e.g. `({(Conversions.IsWidening((PrimitiveType)right.Type, PrimitiveType.I64) && left.Type.IsSignedInteger ? left.Type : right.Type).DisplayName})…`; mixed signedness never converts implicitly";
        else if (left.Type == PrimitiveType.Char && right.Type.IsInteger)
            help = "use `(u32)c` for its value; a `rune` is a Unicode scalar, not a number";
        Report(DiagnosticCodes.BadBinaryOperands, b.Span, [BinaryText(b.Op), left.Type.DisplayName, right.Type.DisplayName], help: help);
        return new BoundErrorExpr(b);
    }

    private BoundExpr BindCoalesce(BinaryExpr b, TypeSymbol? expected)
    {
        var left = BindExpr(b.Left, expected is null ? null : new OptionalType(expected));
        if (left.Type.IsError) { BindExpr(b.Right); return new BoundErrorExpr(b); }
        if (left.Type is not OptionalType o)
        {
            Report(DiagnosticCodes.CoalesceNotOptional, b.Left.Span, [left.Type.DisplayName]);
            BindExpr(b.Right);
            return new BoundErrorExpr(b);
        }
        var right = BindExpr(b.Right, o.Inner);
        if (right.Type.IsNever) return new BoundCoalesce(b, left, right, o.Inner);
        if (right.Type is OptionalType && right.Type.Equals(o))
            return new BoundCoalesce(b, left, right, o);
        right = Convert(right, o.Inner, b.Right.Span);
        return new BoundCoalesce(b, left, right, o.Inner);
    }

    private BoundExpr BindRange(RangeExpr r, TypeSymbol? expected)
    {
        if (r.Start is null || r.End is null)
        {
            Report(DiagnosticCodes.OpenRange, r.Span, [], help: "use open ranges (`a..`, `..b`) only inside `[ ]`");
            return new BoundErrorExpr(r);
        }
        var hint = expected is RangeType rt ? rt.Element : null;
        BoundExpr start, end;
        if (IsUntypedLiteral(r.Start) && !IsUntypedLiteral(r.End))
        {
            end = BindExpr(r.End, hint);
            start = BindExpr(r.Start, end.Type);
        }
        else
        {
            start = BindExpr(r.Start, hint);
            end = BindExpr(r.End, start.Type);
        }
        if (start.Type.IsError || end.Type.IsError) return new BoundErrorExpr(r);
        var common = Conversions.Common(start.Type, end.Type);
        if (common is null || !(common.IsInteger || common == PrimitiveType.Char))
        {
            Report(DiagnosticCodes.BadBinaryOperands, r.Span, [r.Inclusive ? "..=" : "..", start.Type.DisplayName, end.Type.DisplayName],
                help: "make both range ends integers or chars of the same type");
            return new BoundErrorExpr(r);
        }
        return new BoundRange(r, Convert(start, common, r.Start.Span), Convert(end, common, r.End.Span), r.Inclusive, new RangeType(common));
    }

    /// <summary><c>x in a..=b</c>: a range pattern test whose ends may be computed at run time.</summary>
    private BoundExpr BindIn(InExpr i)
    {
        var value = BindExpr(i.Value);
        if (i.Range is not RangeExpr r)
        {
            var other = BindExpr(i.Range);
            if (!value.Type.IsError && !other.Type.IsError)
                Report(DiagnosticCodes.BadBinaryOperands, i.Span, ["in", value.Type.DisplayName, other.Type.DisplayName],
                    help: "write a range after `in`: `x in a..=b`");
            return new BoundErrorExpr(i);
        }
        if (BindRange(r, value.Type.IsInteger || value.Type == PrimitiveType.Char ? new RangeType(value.Type) : null) is not BoundRange range
            || value.Type.IsError)
            return new BoundErrorExpr(i);
        var common = Conversions.Common(value.Type, range.Start.Type);
        if (common is null || !(common.IsInteger || common == PrimitiveType.Char))
        {
            Report(DiagnosticCodes.BadBinaryOperands, i.Span, ["in", value.Type.DisplayName, range.Type.DisplayName],
                help: "make the value and the range ends integers or chars of the same type");
            return new BoundErrorExpr(i);
        }
        var pattern = new BoundRangePattern(r, common, Convert(range.Start, common, r.Start!.Span), Convert(range.End, common, r.End!.Span), r.Inclusive);
        return new BoundIs(i, Convert(value, common, i.Value.Span), pattern);
    }

    private BoundExpr BindConditional(ConditionalExpr c, TypeSymbol? expected)
    {
        var cond = BindCondition(c.Condition);
        var a = BindExpr(c.WhenTrue, expected);
        var b = BindExpr(c.WhenFalse, expected ?? a.Type);
        var type = expected is not null && Conversions.Implicit(a.Type, expected) is not null && Conversions.Implicit(b.Type, expected) is not null
            ? expected
            : Conversions.Common(a.Type, b.Type);
        if (type is null)
        {
            Report(DiagnosticCodes.BranchTypeMismatch, c.Span, [a.Type.DisplayName, b.Type.DisplayName]);
            return new BoundErrorExpr(c);
        }
        if (type is NullType)
        {
            Report(DiagnosticCodes.CannotInferType, c.Span, ["null"]);
            return new BoundErrorExpr(c);
        }
        a = Convert(a, type, c.WhenTrue.Span);
        b = Convert(b, type, c.WhenFalse.Span);
        var result = new BoundConditional(c, cond, a, b, type);
        if (cond.Constant is { Value: bool v} && (v ? a : b).Constant is { } k) return result with { Constant = k };
        return result;
    }

    private BoundExpr BindCast(CastExpr c) => BindConversion(c, ResolveType(c.Type), c.Operand);

    /// <summary><c>u8(x)</c> (or the input form <c>(u8)x</c>): a checked conversion that traps when the value does not fit.</summary>
    private BoundExpr BindConversion(Expr c, TypeSymbol target, Expr operandSyntax)
    {
        var operand = BindExpr(operandSyntax, target.IsNumeric ? null : target);
        if (target.IsError || operand.Type.IsError) return new BoundErrorExpr(c);
        if (operand.Constant is { Value: Int128 } && target.IsInteger && operand.Syntax is LiteralExpr or UnaryExpr)
        {
            // `u8(300)` is a compile-time error rather than a runtime trap.
            return Convert(operand, target, c.Span);
        }
        var kind = Conversions.Explicit(operand.Type, target);
        if (kind is null)
        {
            Report(DiagnosticCodes.InvalidCast, c.Span, [operand.Type.DisplayName, target.DisplayName]);
            return new BoundErrorExpr(c);
        }
        if (kind == ConversionKind.Identity) return operand;
        if (kind == ConversionKind.NullToOptional) return new BoundDefault(c, target);
        var conv = new BoundConversion(c, operand, target, kind.Value);
        if (operand.Constant is { } k && ConstantFolding.Cast(k, operand.Type, target) is { } folded) return conv with { Constant = folded };
        return conv;
    }

    // ── Composite values ─────────────────────────────────────────────────────────

    private BoundExpr BindStructLiteral(StructLiteralExpr s, TypeSymbol? expected)
    {
        TypeSymbol type;
        if (s.Type is { } t) type = ResolveType(t);
        else if (expected is OptionalType { Inner: StructSymbol inner }) type = inner;
        else if (expected is not null) type = expected;
        else
        {
            Report(DiagnosticCodes.NoTargetType, s.Span, ["{ … }"], help: "name the type: `Point { x: 1 }`");
            return new BoundErrorExpr(s);
        }
        if (type.IsError) return new BoundErrorExpr(s);
        // `Order.paid { … }` builds a value in one state; with several states, none is named.
        var stateType = type as StateType;
        var state = stateType is { States.Count: 1 } ? stateType.States[0] : null;
        if (stateType is not null) type = stateType.Struct;
        if (type is not StructSymbol st)
        {
            Report(DiagnosticCodes.NotAStruct, s.Span, [type.DisplayName]);
            return new BoundErrorExpr(s);
        }
        if (st.Constructors.Count > 0 && st != _struct)
        {
            Report(DiagnosticCodes.LiteralWithConstructor, s.Span, [st.Name],
                help: $"call the constructor: `{st.Name}({string.Join(", ", st.Constructors[0].Parameters.Select(p => p.Name + ": …"))})`");
            return new BoundErrorExpr(s);
        }
        if (state is null && _c.StatesOf(st) is { } states)
        {
            Report(DiagnosticCodes.StateLiteralNeedsState, s.Type?.Span ?? s.Span, [st.Name, states.Variants[0].Name],
                help: "name the state the value starts in");
            foreach (var init in s.Fields) BindExpr(init.Value);
            return new BoundErrorExpr(s);
        }
        var values = new BoundExpr?[st.Fields.Count];
        var payload = new BoundExpr?[state?.Fields.Count ?? 0];
        foreach (var init in s.Fields)
        {
            var field = st.FindField(init.Name.Text);
            if (field is null && state?.Fields.FirstOrDefault(f => f.Name == init.Name.Text) is { } payloadField)
            {
                if (payload[payloadField.Ordinal] is not null)
                    Report(DiagnosticCodes.DuplicateFieldInit, init.Name.Span, [payloadField.Name]);
                else
                    payload[payloadField.Ordinal] = BindConverted(init.Value, payloadField.Type);
                continue;
            }
            if (field is null)
            {
                Report(DiagnosticCodes.UnknownMember, init.Name.Span, [init.Name.Text, st.Name],
                    help: Suggest(init.Name.Text, st.Fields.Select(f => f.Name)) is { } sug ? $"did you mean `{sug}`?" : null);
                BindExpr(init.Value);
                continue;
            }
            CheckAccess(field.Access, st, st.Module, init.Name);
            if (values[field.Index] is not null)
            {
                Report(DiagnosticCodes.DuplicateFieldInit, init.Name.Span, [field.Name]);
                continue;
            }
            values[field.Index] = BindConverted(init.Value, field.Type);
        }
        var missing = new List<string>();
        for (var i = 0; i < values.Length; i++)
        {
            if (values[i] is not null) continue;
            var f = st.Fields[i];
            if (f.IsRequired || !f.HasDefault)
            {
                missing.Add(f.Name);
                values[i] = new BoundErrorExpr(s);
            }
            else
            {
                values[i] = _c.FieldDefault(st, f, Source, s.Span) ?? new BoundErrorExpr(s);
            }
        }
        for (var i = 0; i < payload.Length; i++)
        {
            if (payload[i] is not null) continue;
            missing.Add(state!.Fields[i].Name);
            payload[i] = new BoundErrorExpr(s);
        }
        if (missing.Count > 0)
        {
            var first = st.FindField(missing[0]);
            var written = state is null ? st.Name : $"{st.Name}.{state.Name}";
            Report(DiagnosticCodes.MissingFields, s.Span, [st.Name, string.Join("`, `", missing)],
                label: missing.Count == 1 && first is { IsRequired: true } ? "`" + first.Name + "` is `required`" : null,
                help: $"set {(missing.Count == 1 ? "it" : "them")}: `{written} {{ {string.Join(", ", missing.Select(m => m + ": …"))} }}`");
            return new BoundErrorExpr(s);
        }
        var literal = new BoundStructLiteral(s, st, values!);
        return Checked(s, state is null ? literal : literal with { State = state, Payload = payload!, Type = stateType! });
    }

    private BoundExpr BindWith(WithExpr w)
    {
        var target = BindExpr(w.Target);
        if (target.Type.IsError) return new BoundErrorExpr(w);
        if (target.Type is not StructSymbol st)
        {
            Report(DiagnosticCodes.NotAStruct, w.Target.Span, [target.Type.DisplayName]);
            return new BoundErrorExpr(w);
        }
        var updates = new List<(FieldSymbol, BoundExpr)>();
        foreach (var init in w.Fields)
        {
            var field = st.FindField(init.Name.Text);
            if (field is null)
            {
                Report(DiagnosticCodes.UnknownMember, init.Name.Span, [init.Name.Text, st.Name]);
                continue;
            }
            CheckAccess(field.Access, st, st.Module, init.Name);
            if (updates.Any(u => u.Item1 == field))
            {
                Report(DiagnosticCodes.DuplicateFieldInit, init.Name.Span, [field.Name]);
                continue;
            }
            updates.Add((field, BindConverted(init.Value, field.Type)));
        }
        return Checked(w, new BoundWith(w, target, updates.ToArray()), updates.Select(u => u.Item1).ToList());
    }

    private BoundExpr BindCollection(CollectionExpr c, TypeSymbol? expected)
    {
        var target = expected is OptionalType o ? o.Inner : expected;
        if (c.Elements.Any(e => e is MapEntryExpr)) return BindMapLiteral(c, target);
        if (target is MapType && c.Elements.Length == 0) return new BoundCollection(c, target, []);
        TypeSymbol? element = target switch
        {
            ListType l => l.Element,
            SpanType s => s.Element,
            Symbols.ArrayType a => a.Element,
            _ => null,
        };
        if (element is null && target is null && c.Elements.Length > 0)
            return BindUntypedCollection(c);
        if (element is null)
        {
            if (target is null || target.IsError)
            {
                if (target is null)
                    Report(DiagnosticCodes.NoTargetType, c.Span, ["[ … ]"], help: "declare the type: `List<i64> xs = [1, 2]`");
            }
            else
            {
                Report(DiagnosticCodes.TypeMismatch, c.Span, ["a collection literal", target.DisplayName]);
            }
            foreach (var e in c.Elements) BindExpr(e);
            return new BoundErrorExpr(c);
        }
        var elements = c.Elements.Select(e => e is SpreadExpr s ? BindSpread(s, element) : BindConverted(e, element)).ToArray();
        return FinishCollection(c, target!, elements);
    }

    /// <summary><c>[1, 2, 3]</c> with no target type: a fixed array of the first element's type.</summary>
    private BoundExpr BindUntypedCollection(CollectionExpr c)
    {
        var first = c.Elements[0] is SpreadExpr s ? BindSpread(s, null) : BindExpr(c.Elements[0]);
        var element = first.Type;
        if (element.IsError)
        {
            foreach (var e in c.Elements.Skip(1)) BindExpr(e);
            return new BoundErrorExpr(c);
        }
        if (element is NullType or PrimitiveType { Kind: PrimitiveKind.Void or PrimitiveKind.Never })
        {
            Report(DiagnosticCodes.CannotInferType, c.Elements[0].Span, [Source.GetText(c.Elements[0].Span)], help: "declare the type: `List<i64> xs = [1, 2]`");
            foreach (var e in c.Elements.Skip(1)) BindExpr(e);
            return new BoundErrorExpr(c);
        }
        if (_c.FunctionTypeNotHere(element, Source, c.Elements[0].Span, "an array element"))
        {
            foreach (var e in c.Elements.Skip(1)) BindExpr(e);
            return new BoundErrorExpr(c);
        }
        if (first is not BoundSpread) first = Convert(first, element, c.Elements[0].Span);
        var elements = c.Elements.Skip(1).Select(e => e is SpreadExpr s ? BindSpread(s, element) : BindConverted(e, element)).Prepend(first).ToArray();
        return FinishCollection(c, new Symbols.ArrayType(element, KnownLength(elements) ?? elements.Length), elements);
    }

    /// <summary>Checks the length of an array or span literal: spreads into stack storage must have a compile-time length.</summary>
    private BoundExpr FinishCollection(CollectionExpr c, TypeSymbol target, BoundExpr[] elements)
    {
        if (elements.Any(e => e is BoundErrorExpr { Syntax: SpreadExpr })) return new BoundErrorExpr(c);
        if (target is not ListType && elements.OfType<BoundSpread>().FirstOrDefault(s => s.Operand.Type is not Symbols.ArrayType) is { } open)
        {
            var text = Source.GetText(open.Syntax.Span);
            Report(DiagnosticCodes.NotConstant, open.Syntax.Span, [$"the length of `{text}` in a `{(target is SpanType ? "Span" : "T[N]")}` literal"],
                label: $"a `{open.Operand.Type.DisplayName}` has a run-time length",
                help: $"spread a fixed array `T[N]` here, or build a list: `List<{open.Type.DisplayName}> all = {Source.GetText(c.Span)}`");
            return new BoundErrorExpr(c);
        }
        if (target is Symbols.ArrayType at && at.Length != KnownLength(elements))
            Report(DiagnosticCodes.ArrayLengthMismatch, c.Span, [at.DisplayName, KnownLength(elements)!]);
        return new BoundCollection(c, target, elements);
    }

    /// <summary>The number of elements, counting each spread fixed array by its length; null if a spread's length is only known at run time.</summary>
    private static long? KnownLength(BoundExpr[] elements)
    {
        long n = 0;
        foreach (var e in elements)
        {
            if (e is not BoundSpread s) n++;
            else if (s.Operand.Type is Symbols.ArrayType a) n += a.Length;
            else return null;
        }
        return n;
    }

    /// <summary><c>..xs</c> in a list, span or array literal: <c>xs</c> is a <c>List</c>, <c>Span</c> or fixed array of <paramref name="element"/> (any element type when null).</summary>
    private BoundExpr BindSpread(SpreadExpr s, TypeSymbol? element)
    {
        var operand = BindExpr(s.Operand);
        if (operand.Type.IsError) return new BoundErrorExpr(s);
        TypeSymbol? item = operand.Type switch
        {
            ListType l => l.Element,
            SpanType sp => sp.Element,
            Symbols.ArrayType a => a.Element,
            _ => null,
        };
        var text = Source.GetText(s.Operand.Span);
        if (item is null)
        {
            Report(DiagnosticCodes.SpreadNotSequence, s.Span, [text, operand.Type.DisplayName],
                label: "not a `List`, `Span` or fixed array",
                help: element is not null && Conversions.Implicit(operand.Type, element) is not null
                    ? $"drop the `..` to add `{text}` as one element"
                    : "spread a `List`, `Span` or fixed array; a single value goes in without `..`");
            return new BoundErrorExpr(s);
        }
        if (element is not null && !item.Equals(element))
        {
            Report(DiagnosticCodes.TypeMismatch, s.Span, [item.DisplayName, element.DisplayName],
                label: $"`{text}` holds `{item.DisplayName}`",
                help: $"spread a sequence of `{element.DisplayName}`, or change the literal's element type to `{item.DisplayName}`");
            return new BoundErrorExpr(s);
        }
        if (TypeFacts.IsUnique(item))
        {
            Report(DiagnosticCodes.Unsupported, s.Span, [$"spreading elements that own memory (`{item.DisplayName}`)"],
                help: "add the elements one at a time with `move`");
            return new BoundErrorExpr(s);
        }
        return new BoundSpread(s, operand, item);
    }

    /// <summary><c>["k": v, …]</c>: a <c>Map</c>, typed by the target or, without one, by the first entry.</summary>
    private BoundExpr BindMapLiteral(CollectionExpr c, TypeSymbol? target)
    {
        if (c.Elements.FirstOrDefault(e => e is not MapEntryExpr) is { } bare)
        {
            Report(DiagnosticCodes.MapLiteralMixed, bare.Span, [], label: "no `key:` here",
                help: "write every element as `key: value`, or remove the keys to make a list");
            foreach (var e in c.Elements) BindMapPart(e);
            return new BoundErrorExpr(c);
        }
        var entries = c.Elements.Cast<MapEntryExpr>().ToArray();
        var parts = new List<BoundExpr>();
        MapType map;
        if (target is MapType m)
        {
            map = m;
        }
        else if (target is null)
        {
            var key = BindExpr(entries[0].Key);
            var value = BindExpr(entries[0].Value);
            if (key.Type.IsError || value.Type.IsError)
            {
                foreach (var e in entries.Skip(1)) BindMapPart(e);
                return new BoundErrorExpr(c);
            }
            if (!MapType.IsValidKey(key.Type))
            {
                if (MapType.InvalidKeyPart(key.Type) is var (part, partType))
                    Report(DiagnosticCodes.KeyPartNotHashable, entries[0].Key.Span,
                        [key.Type.DisplayName, key.Type is Symbols.TupleType ? "element" : "field", part, partType.DisplayName],
                        help: "use a field that identifies the value as the key, such as an id or a `Symbol`");
                else
                    Report(DiagnosticCodes.InvalidMapKey, entries[0].Key.Span, [key.Type.DisplayName],
                        help: "use an integer type, `rune`, `bool`, `string`, `Symbol`, a plain enum, or a tuple or struct of those as the key");
                foreach (var e in entries.Skip(1)) BindMapPart(e);
                return new BoundErrorExpr(c);
            }
            if (value.Type is NullType or PrimitiveType { Kind: PrimitiveKind.Void or PrimitiveKind.Never })
            {
                Report(DiagnosticCodes.CannotInferType, entries[0].Value.Span, [Source.GetText(entries[0].Value.Span)],
                    help: $"declare the type: `Map<{key.Type.DisplayName}, i64?> m = [ … ]`");
                foreach (var e in entries.Skip(1)) BindMapPart(e);
                return new BoundErrorExpr(c);
            }
            if (_c.FunctionTypeNotHere(value.Type, Source, entries[0].Value.Span, "a map value"))
            {
                foreach (var e in entries.Skip(1)) BindMapPart(e);
                return new BoundErrorExpr(c);
            }
            map = new MapType(key.Type, value.Type);
            parts.Add(Convert(key, map.KeyType, entries[0].Key.Span));
            parts.Add(Convert(value, map.ValueType, entries[0].Value.Span));
            entries = entries[1..];
        }
        else
        {
            if (!target.IsError) Report(DiagnosticCodes.TypeMismatch, c.Span, ["a map literal", target.DisplayName]);
            foreach (var e in entries) BindMapPart(e);
            return new BoundErrorExpr(c);
        }
        foreach (var e in entries)
        {
            parts.Add(BindConverted(e.Key, map.KeyType));
            parts.Add(BindConverted(e.Value, map.ValueType));
        }
        if (parts.Any(p => p.Type.IsError)) return new BoundErrorExpr(c);
        return new BoundCollection(c, map, parts.ToArray());
    }

    /// <summary>Binds the parts of a map literal element for recovery.</summary>
    private void BindMapPart(Expr e)
    {
        if (e is MapEntryExpr entry)
        {
            BindExpr(entry.Key);
            BindExpr(entry.Value);
        }
        else if (e is SpreadExpr s) BindExpr(s.Operand);
        else BindExpr(e);
    }

    private BoundExpr BindTuple(TupleExpr t, TypeSymbol? expected)
    {
        var target = expected is OptionalType o ? o.Inner : expected;
        if (target is Symbols.TupleType tt && tt.Fields.Count == t.Elements.Length)
        {
            var values = new BoundExpr[t.Elements.Length];
            for (var i = 0; i < values.Length; i++)
            {
                var e = t.Elements[i];
                if (e.Name is { } n && tt.Fields[i].Name is { } fieldName && n.Text != fieldName)
                    Report(DiagnosticCodes.TupleNameMismatch, n.Span, [n.Text, fieldName]);
                values[i] = BindConverted(e.Value, tt.Fields[i].Type);
            }
            return new BoundTuple(t, tt, values);
        }
        var elements = t.Elements.Select(e => BindExpr(e.Value)).ToArray();
        if (elements.Any(e => e.Type.IsError) || target is { IsError: true }) return new BoundErrorExpr(t);
        var fields = new List<TupleField>();
        for (var i = 0; i < elements.Length; i++)
        {
            var type = elements[i].Type;
            if (type is NullType or PrimitiveType { Kind: PrimitiveKind.Void or PrimitiveKind.Never })
            {
                Report(DiagnosticCodes.CannotInferType, t.Elements[i].Span, [Source.GetText(t.Elements[i].Span)]);
                return new BoundErrorExpr(t);
            }
            fields.Add(new TupleField(t.Elements[i].Name?.Text, type));
        }
        if (fields.Zip(t.Elements).Count(p => _c.FunctionTypeNotHere(p.First.Type, Source, p.Second.Value.Span, "a tuple element")) > 0) return new BoundErrorExpr(t);
        var tupleType = new Symbols.TupleType(fields);
        return new BoundTuple(t, tupleType, elements);
    }

    // ── Lambdas ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// A lambda, lifted to a <see cref="FunctionKind.Lambda"/> function of the module and bound inline with its own scope,
    /// loops and <c>return</c>; the outer locals it reads are its captures (spec 007 R6, R8).
    /// </summary>
    private BoundExpr BindLambda(Node syntax, Name[] parameters, Expr? body, BlockStmt? block, FunctionType? expected)
    {
        if (expected is null)
        {
            Report(DiagnosticCodes.NoTargetType, syntax.Span, [Source.GetText(syntax.Span)],
                help: "write the function type it has, such as `let f: i64 -> i64 = x => x`");
            return new BoundErrorExpr(syntax);
        }
        var count = expected.Parameters.Count;
        if (parameters.Length != count)
        {
            var names = count == 1 ? "x" : "(" + string.Join(", ", Enumerable.Range(0, count).Select(i => (char)('a' + i))) + ")";
            Report(DiagnosticCodes.LambdaArity, syntax.Span, [parameters.Length, expected.DisplayName, count],
                help: $"write {count} parameter name(s): `{names} => …`");
            return new BoundErrorExpr(syntax);
        }
        if (_fn is null)
        {
            Report(DiagnosticCodes.Unsupported, syntax.Span, ["a lambda outside a function body"]);
            return new BoundErrorExpr(syntax);
        }
        // Numbered in the order the enclosing function's lambdas are bound, which is source order (L009).
        var ordinal = _lambdaCount[_fn] = _lambdaCount.GetValueOrDefault(_fn) + 1;
        var lambda = new FunctionSymbol($"{_fn.Name}$lambda{ordinal}", new Location(Source, syntax.Span), Module, FunctionKind.Lambda)
        {
            File = _file, ReturnType = expected.Return, Fails = expected.Fails.Count > 0, Enclosing = _fn,
        };
        lambda.ErrorSets.AddRange(expected.Fails);
        Module.AllFunctions.Add(lambda);
        var captures = new List<BoundLocal>();
        // `break`, `continue`, `return` and `defer` in the body are the lambda's own.
        var (fn, targets, deferDepth, inPanicHandler, firstStatement) = (_fn, _targets.ToArray(), _deferDepth, _inPanicHandler, _firstStatement);
        (_fn, _deferDepth, _inPanicHandler, _firstStatement) = (lambda, 0, false, null);
        _targets.Clear();
        _lambdas.Add((lambda, captures));
        PushScope();
        for (var i = 0; i < parameters.Length; i++)
        {
            CheckValueName(parameters[i]);
            AddLocal(parameters[i], new ParameterSymbol(parameters[i].Text, new Location(Source, parameters[i].Span), expected.Parameters[i], ParamModifier.None, i) { Id = _nextLocalId++ });
        }
        lambda.Parameters.AddRange(lambda.Locals.OfType<ParameterSymbol>());
        lambda.Body = block is not null ? BindBlock(block) : new BoundBlock(body!, [BindExpressionBody(body!)]);
        PopScope();
        _lambdas.RemoveAt(_lambdas.Count - 1);
        _targets.AddRange(targets);
        (_fn, _deferDepth, _inPanicHandler, _firstStatement) = (fn, deferDepth, inPanicHandler, firstStatement);
        return new BoundLambda(syntax, lambda, captures.ToArray(), expected);
    }

    /// <summary>The function whose body is being bound: the one around every lambda being bound.</summary>
    private FunctionSymbol? Host
    {
        get
        {
            var fn = _fn;
            while (fn is { Kind: FunctionKind.Lambda }) fn = fn.Enclosing;
            return fn;
        }
    }

    /// <summary>A read of <c>this</c> inside a lambda, which does not capture it: BYN2821 (spec 007 R8).</summary>
    private bool InLambda(Node syntax, string name, string reason)
    {
        if (_fn is not { Kind: FunctionKind.Lambda }) return false;
        Report(DiagnosticCodes.CannotCapture, syntax.Span, [name, reason], help: "copy the fields you need into `let` values first");
        return true;
    }

    /// <summary>
    /// Records <paramref name="local"/>, read as <paramref name="type"/>, as a capture of each lambda being bound that it is
    /// outer to; false after BYN2821 when it is mutable or a parameter with a modifier (spec 007 R8).
    /// </summary>
    private bool Capture(NameExpr n, LocalSymbol local, TypeSymbol type)
    {
        if (_lambdas.Count == 0 || _lambdas[^1].Lambda.Locals.Contains(local)) return true;
        var (reason, help) = local switch
        {
            ParameterSymbol { Modifier: not ParamModifier.None and var m } => ($"it is a `{m.ToString().ToLowerInvariant()}` parameter", "pass it as a plain parameter"),
            { IsMutable: true } => ("it is a `var`", "copy it into a `let` first"),
            _ => (null, null),
        };
        if (reason is not null)
        {
            Report(DiagnosticCodes.CannotCapture, n.Span, [local.Name, reason], help: help);
            return false;
        }
        for (var i = _lambdas.Count - 1; i >= 0 && !_lambdas[i].Lambda.Locals.Contains(local); i--)
        {
            var (lambda, captures) = _lambdas[i];
            if (lambda.Captures.Contains(local)) continue;
            lambda.Captures.Add(local);
            captures.Add(new BoundLocal(n, local) { Type = type });
        }
        return true;
    }

    private BoundExpr BindStackalloc(StackallocExpr s)
    {
        var element = ResolveType(s.Element);
        var count = BindConverted(s.Count, PrimitiveType.I64);
        if (element.IsError || count.Type.IsError) return new BoundErrorExpr(s);
        if (count.Constant is not { Value: Int128 n })
        {
            Report(DiagnosticCodes.NotConstant, s.Count.Span, ["a `stackalloc` length"],
                help: "use a constant length: `stackalloc u8[64]`; v0.1 supports no other");
            return new BoundErrorExpr(s);
        }
        if (n <= 0 || n > 1 << 20)
        {
            Report(DiagnosticCodes.LiteralOutOfRange, s.Count.Span, [n.ToString(), "a `stackalloc` length (1 to 1048576)"]);
            return new BoundErrorExpr(s);
        }
        return new BoundStackalloc(s, element, (long)n, new SpanType(element));
    }

    private BoundExpr BindInterpolated(InterpolatedExpr i)
    {
        var parts = new List<BoundInterpolationPart>();
        foreach (var part in i.Parts)
        {
            if (part is InterpolationText text)
            {
                parts.Add(new BoundInterpolationPart(text.Text, null));
                continue;
            }
            var hole = (InterpolationHole)part;
            var value = BindExpr(hole.Expr);
            // `as: .debug` shows a struct without `formatInto` in its literal form (spec §13).
            var debug = hole.Specs.Any(a => a is { Name.Text: "as", Value: TargetMemberExpr { Member.Text: "debug" } });
            if (!value.Type.IsError && (debug ? WhyNotDebugFormattable(value.Type, []) : WhyNotFormattable(value.Type, [])) is { } why)
                Report(DiagnosticCodes.NotFormattable, hole.Expr.Span, [value.Type.DisplayName], label: why == "" ? null : why,
                    help: value.Type is StructSymbol s && !debug ? $"add `formatInto(ref StrBuf)` to `{s.Name}`, or use `{{x, as: .debug}}`" : null);
            parts.Add(new BoundInterpolationPart(null, value) { Spec = hole.Specs.Length == 0 || value.Type.IsError ? null : BindFormatSpec(hole.Specs, value.Type) });
        }
        return new BoundInterpolated(i, parts.ToArray());
    }

    /// <summary>
    /// The specs of a hole (spec §16.4): <c>as:</c> a radix on integers or <c>.debug</c>, <c>digits:</c> on floats,
    /// <c>width:</c>, <c>fill:</c> and <c>align:</c> on any value. Each is a constant; a spec that does not apply is BYN2021.
    /// </summary>
    private BoundFormatSpec BindFormatSpec(Argument[] specs, TypeSymbol type)
    {
        var spec = new BoundFormatSpec(0, false, 0, ' ', -1, -1);
        var seen = new HashSet<string>();
        foreach (var a in specs)
        {
            var name = a.Name?.Text ?? "";
            if (!seen.Add(name))
            {
                Report(DiagnosticCodes.DuplicateArgument, a.Span, [name]);
                continue;
            }
            switch (name)
            {
                case "as" when a.Value is TargetMemberExpr { Member.Text: "debug" }:
                    spec = spec with { Debug = true };
                    break;
                case "as" when a.Value is TargetMemberExpr { Member.Text: var radix } && _c.Builtins.Radix.Find(radix) is { } member:
                    if (!type.IsInteger && !TakesFormatSpec(type)) Report(DiagnosticCodes.UnknownParameter, a.Span, [type.DisplayName, $"as: .{radix}"]);
                    else spec = spec with { Radix = member.Ordinal switch { 0 => 2, 1 => 8, 2 => 10, _ => 16 } };
                    break;
                case "as":
                    BindConverted(a.Value, _c.Builtins.Radix);
                    break;
                case "width" or "digits":
                    var n = BindConverted(a.Value, PrimitiveType.I64);
                    if (n.Type.IsError) break;
                    if (n.Constant is not { Value: Int128 v })
                        Report(DiagnosticCodes.NotConstant, a.Value.Span, [$"a format spec's `{name}`"]);
                    else if (name == "width" && (v < 0 || v > 1024))
                        Report(DiagnosticCodes.LiteralOutOfRange, a.Value.Span, [v.ToString(), "a format spec's `width` (0 to 1024)"]);
                    else if (name == "digits" && (v < 0 || v > 15))
                        Report(DiagnosticCodes.LiteralOutOfRange, a.Value.Span, [v.ToString(), "a format spec's `digits` (0 to 15)"]);
                    else if (name == "digits" && !type.IsFloat && !TakesFormatSpec(type))
                        Report(DiagnosticCodes.UnknownParameter, a.Span, [type.DisplayName, "digits"]);
                    else spec = name == "width" ? spec with { Width = (long)v } : spec with { Digits = (int)v };
                    break;
                case "fill":
                    var fill = BindConverted(a.Value, PrimitiveType.Char);
                    if (fill.Type.IsError) break;
                    if (fill.Constant?.Value is not { } c) Report(DiagnosticCodes.NotConstant, a.Value.Span, ["a format spec's `fill`"]);
                    else spec = spec with { Fill = (uint)((System.Text.Rune)c).Value };
                    break;
                case "align" when a.Value is TargetMemberExpr { Member.Text: "start" or "end" or "center" } edge:
                    spec = spec with { Align = edge.Member.Text switch { "start" => 0, "end" => 1, _ => 2 } };
                    break;
                case "align":
                    Report(DiagnosticCodes.UnknownEnumMember, a.Value.Span, [a.Value is TargetMemberExpr tm ? tm.Member.Text : Source.GetText(a.Value.Span), "align"]);
                    break;
                default:
                    Report(DiagnosticCodes.UnknownParameter, a.Span, [type.DisplayName, name]);
                    break;
            }
        }
        return spec;
    }

    internal static bool IsFormattable(TypeSymbol t) => WhyNotFormattable(t, []) is null;

    /// <summary>
    /// Null when <paramref name="t"/> formats; otherwise the part that does not ("" for <paramref name="t"/> itself).
    /// <c>Owned</c> formats the value it points to; a type reached again is assumed formattable.
    /// </summary>
    private static string? WhyNotFormattable(TypeSymbol t, HashSet<TypeSymbol> seen) => t switch
    {
        PrimitiveType p => p.Kind is not (PrimitiveKind.Void or PrimitiveKind.Never) ? null : "",
        ErrorSetSymbol or ErrorUnionType or EnumSymbol or TypeParameterType or BuiltinType { Kind: BuiltinKind.Symbol } => null,
        OptionalType o => Part("the value", o.Inner, seen),
        ResultType r => Part("the value", r.Value, seen),
        OwnedType o => WhyNotFormattable(o.Inner, seen),
        // Structs have no default display (HANDOFF §13): only `formatInto` formats one.
        StructSymbol s => HasFormatInto(s) ? null : "",
        UnionSymbol u => !seen.Add(u) ? null
            : u.Variants.SelectMany(v => v.Fields.Select(f => Part($"`{v.Name}` field `{f.Name}`", f.Type, seen))).FirstOrDefault(w => w is not null),
        Symbols.TupleType tt => tt.Fields.Select((f, i) => Part($"element {i + 1}", f.Type, seen)).FirstOrDefault(w => w is not null),
        MapType m => Part("the key", m.KeyType, seen) ?? Part("the value", m.ValueType, seen),
        // Lists, spans and arrays do not format; an unformattable element is still named.
        ListType l => Part("an element", l.Element, seen) ?? "",
        SpanType sp => Part("an element", sp.Element, seen) ?? "",
        ArrayType a => Part("an element", a.Element, seen) ?? "",
        _ => "",
    };

    /// <summary>As <see cref="WhyNotFormattable"/>, under <c>as: .debug</c>: a struct formats when each of its fields does.</summary>
    private static string? WhyNotDebugFormattable(TypeSymbol t, HashSet<TypeSymbol> seen) => t switch
    {
        StructSymbol s when !TakesFormatSpec(s) => !seen.Add(s) ? null
            : s.Fields.Select(f => WhyNotDebugFormattable(f.Type, seen) is { } why ? why == "" ? $"field `{f.Name}` is `{f.Type.DisplayName}`" : why : null)
                .FirstOrDefault(w => w is not null),
        _ => WhyNotFormattable(t, seen),
    };

    /// <summary>Why <paramref name="part"/> of a type does not format, naming it when it is the culprit.</summary>
    private static string? Part(string name, TypeSymbol part, HashSet<TypeSymbol> seen) =>
        WhyNotFormattable(part, seen) is { } why ? why == "" ? $"{name} is `{part.DisplayName}`" : why : null;

    /// <summary>The type of a built-in property, with the builtin enums <see cref="BuiltinMembers"/> cannot name.</summary>
    private TypeSymbol PropertyType(BuiltinMember member, TypeSymbol type) => member switch
    {
        BuiltinMember.FormatSpecRadix => _c.Builtins.Radix,
        BuiltinMember.FormatSpecAlign => _c.Builtins.Align,
        _ => type,
    };

    /// <summary>Matches the C emitter's <c>formatInto</c> lookup.</summary>
    private static bool HasFormatInto(StructSymbol s) =>
        s.Methods.Any(m => m.Name == "formatInto" && !m.IsStatic && !m.Fails && m.Parameters.Count is 1 or 2
            && m.Parameters[0] is { IsByRef: true, Type: BuiltinType { Kind: BuiltinKind.StrBuf } }
            && (m.Parameters.Count == 1 || m.Parameters[1] is { IsByRef: false, Type: BuiltinType { Kind: BuiltinKind.FormatSpec } }));

    /// <summary>Whether <paramref name="t"/> is a struct whose <c>formatInto</c> takes the hole's <c>FormatSpec</c>, so reads every spec itself.</summary>
    private static bool TakesFormatSpec(TypeSymbol t) =>
        t is StructSymbol s && s.Methods.Any(m => m.Name == "formatInto" && !m.IsStatic && !m.Fails && m.Parameters.Count == 2
            && m.Parameters[0] is { IsByRef: true, Type: BuiltinType { Kind: BuiltinKind.StrBuf } }
            && m.Parameters[1] is { IsByRef: false, Type: BuiltinType { Kind: BuiltinKind.FormatSpec } });
}

/// <summary>A function named but not yet called (only valid as a callee).</summary>
internal sealed record BoundMethodGroup(Node Syntax, BoundExpr? Receiver, FunctionSymbol Function) : BoundExpr(Syntax, ErrorType.Instance)
{
    /// <summary>The explicit <c>&lt;i64&gt;</c> of <c>pick&lt;i64&gt;(…)</c>.</summary>
    public TypeSyntax[] TypeArgs { get; init; } = [];
}

/// <summary>A built-in method named but not yet called.</summary>
/// <summary>A union variant with a payload, before its call.</summary>
internal sealed record BoundVariantGroup(Node Syntax, UnionVariantSymbol Variant) : BoundExpr(Syntax, ErrorType.Instance);

internal sealed record BoundBuiltinGroup(Node Syntax, BoundExpr Receiver, string Name) : BoundExpr(Syntax, ErrorType.Instance);
