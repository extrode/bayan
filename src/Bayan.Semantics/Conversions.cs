using Bayan.Semantics.Binding;
using Bayan.Semantics.Symbols;

namespace Bayan.Semantics;

/// <summary>Implicit and explicit conversions (spec §6.2).</summary>
public static class Conversions
{
    /// <summary>The implicit conversion from <paramref name="from"/> to <paramref name="to"/>, or null.</summary>
    /// <param name="argument">The value is a call argument, where <c>List&lt;T&gt;</c> and <c>T[N]</c> become <c>Span&lt;T&gt;</c>.</param>
    public static ConversionKind? Implicit(TypeSymbol from, TypeSymbol to, bool argument = false)
    {
        if (from.Equals(to)) return ConversionKind.Identity;
        if (from.IsError || to.IsError) return ConversionKind.Identity;
        if (from.IsNever) return ConversionKind.FromNever;
        if (from is NullType) return to is OptionalType ? ConversionKind.NullToOptional : null;
        if (from is PrimitiveType f && to is PrimitiveType t && IsWidening(f, t)) return ConversionKind.Widen;
        if (to is OptionalType o && (from.Equals(o.Inner) || Implicit(from, o.Inner) is ConversionKind.Widen)) return ConversionKind.ToOptional;
        if (to is ResultType r)
        {
            if (from is ErrorSetSymbol && from.Equals(r.Error)) return ConversionKind.ErrorToResult;
            if (from.Equals(r.Value) || Implicit(from, r.Value) is ConversionKind.Widen) return ConversionKind.ToResult;
        }
        // A writable view may be read through a read-only one; never the reverse.
        if (from is SpanType ws && to is SpanType { ReadOnly: true } rs && ws.Element.Equals(rs.Element)) return ConversionKind.Identity;
        if (argument && to is SpanType span)
        {
            if (from is ListType list && list.Element.Equals(span.Element)) return ConversionKind.ListToSpan;
            if (from is Symbols.ArrayType array && array.Element.Equals(span.Element)) return ConversionKind.ArrayToSpan;
        }
        if (from is ErrorSetSymbol set && to is ErrorUnionType u && (u.Sets.Count == 0 || u.Sets.Contains(set))) return ConversionKind.Identity;
        return null;
    }

    /// <summary>Lossless numeric widening; 64-bit <c>isize</c>/<c>usize</c> are assumed.</summary>
    public static bool IsWidening(PrimitiveType from, PrimitiveType to)
    {
        if (from.IsInteger && to.IsInteger)
        {
            if (from.IsSignedInteger == to.IsSignedInteger)
                return from.Bits < to.Bits || (from.Bits == to.Bits && from.Kind != to.Kind && from.Bits < 64);
            return from.IsUnsignedInteger && to.IsSignedInteger && from.Bits < to.Bits;
        }
        if (from.IsInteger && to.IsFloat)
            return to.Kind == PrimitiveKind.F64 ? from.Bits <= 32 : from.Bits <= 16;
        return from.Kind == PrimitiveKind.F32 && to.Kind == PrimitiveKind.F64;
    }

    /// <summary>An explicit cast <c>(T)x</c>, or null when none exists.</summary>
    public static ConversionKind? Explicit(TypeSymbol from, TypeSymbol to)
    {
        if (Implicit(from, to) is { } k) return k;
        if (from.IsNumeric && to.IsNumeric) return ConversionKind.NumericCast;
        if (from == PrimitiveType.Char && to.IsInteger) return ConversionKind.CharToInteger;
        if (from.IsInteger && to == PrimitiveType.Char) return ConversionKind.IntegerToChar;
        return null;
    }

    /// <summary>
    /// The type both operands of a binary operator convert to: the wider of the two when one
    /// widens to the other, or null.
    /// </summary>
    public static TypeSymbol? Common(TypeSymbol a, TypeSymbol b)
    {
        if (a.Equals(b)) return a;
        if (a.IsError) return b;
        if (b.IsError) return a;
        if (a.IsNever) return b;
        if (b.IsNever) return a;
        if (a is NullType) return b is OptionalType ? b : null;
        if (b is NullType) return a is OptionalType ? a : null;
        if (Implicit(a, b) is { } ab && ab != ConversionKind.FromNever) return b;
        if (Implicit(b, a) is { } ba && ba != ConversionKind.FromNever) return a;
        return null;
    }
}
