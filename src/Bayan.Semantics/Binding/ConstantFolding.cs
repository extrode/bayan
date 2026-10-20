using System.Text;
using Bayan.Semantics.Symbols;
using Bayan.Syntax.Ast;

namespace Bayan.Semantics.Binding;

/// <summary>
/// Compile-time evaluation. Integers are <see cref="Int128"/> (callers check the result fits),
/// floats <c>double</c>, characters <see cref="Rune"/>. Returns null when the operation is
/// left to run time (division by zero, out-of-range shifts, …).
/// </summary>
internal static class ConstantFolding
{
    public static ConstantValue? Unary(UnaryOp op, ConstantValue c, TypeSymbol type) => (op, c.Value) switch
    {
        (UnaryOp.Plus, _) => c,
        (UnaryOp.Negate, Int128 n) => new(-n),
        (UnaryOp.Negate, double d) => new(-d),
        (UnaryOp.Not, bool b) => new(!b),
        (UnaryOp.Complement, Int128 n) when type is PrimitiveType p => new(Wrap(~n, p)),
        _ => null,
    };

    public static ConstantValue? Binary(BinaryOp op, ConstantValue l, ConstantValue r, TypeSymbol type)
    {
        switch (l.Value, r.Value)
        {
            case (Int128 a, Int128 b):
                return Integer(op, a, b, type as PrimitiveType);
            case (double a, double b):
                return op switch
                {
                    BinaryOp.Add => new(a + b),
                    BinaryOp.Sub => new(a - b),
                    BinaryOp.Mul => new(a * b),
                    BinaryOp.Div => new(a / b),
                    BinaryOp.Rem => new(a % b),
                    _ => Compare(op, a.CompareTo(b), a == b),
                };
            case (bool a, bool b):
                return op switch
                {
                    BinaryOp.LogicalAnd => new(a && b),
                    BinaryOp.LogicalOr => new(a || b),
                    BinaryOp.Eq => new(a == b),
                    BinaryOp.Ne => new(a != b),
                    BinaryOp.BitAnd => new(a & b),
                    BinaryOp.BitOr => new(a | b),
                    BinaryOp.BitXor => new(a ^ b),
                    _ => null,
                };
            case (Rune a, Rune b):
                return Compare(op, a.Value.CompareTo(b.Value), a == b);
            case (string a, string b):
                return op is BinaryOp.Eq or BinaryOp.Ne ? Compare(op, 0, string.Equals(a, b, StringComparison.Ordinal)) : null;
            default:
                return null;
        }
    }

    private static ConstantValue? Compare(BinaryOp op, int order, bool equal) => op switch
    {
        BinaryOp.Lt => new(order < 0),
        BinaryOp.Gt => new(order > 0),
        BinaryOp.Le => new(order <= 0),
        BinaryOp.Ge => new(order >= 0),
        BinaryOp.Eq => new(equal),
        BinaryOp.Ne => new(!equal),
        _ => null,
    };

    private static ConstantValue? Integer(BinaryOp op, Int128 a, Int128 b, PrimitiveType? type)
    {
        switch (op)
        {
            case BinaryOp.Add: return new(a + b);
            case BinaryOp.Sub: return new(a - b);
            case BinaryOp.Mul: return new(a * b);
            case BinaryOp.Div: return b == 0 ? null : new(a / b);
            case BinaryOp.Rem: return b == 0 ? null : new(a % b);
            case BinaryOp.BitAnd: return new(a & b);
            case BinaryOp.BitOr: return new(a | b);
            case BinaryOp.BitXor: return new(a ^ b);
        }
        if (type is { IsInteger: true })
        {
            switch (op)
            {
                case BinaryOp.AddWrap: return new(Wrap(a + b, type));
                case BinaryOp.SubWrap: return new(Wrap(a - b, type));
                case BinaryOp.MulWrap: return new(Wrap(a * b, type));
                case BinaryOp.AddSat: return new(Saturate(a + b, type));
                case BinaryOp.SubSat: return new(Saturate(a - b, type));
                case BinaryOp.MulSat: return new(Saturate(a * b, type));
                case BinaryOp.Shl when b >= 0 && b < type.Bits: return new(Wrap(a << (int)b, type));
                case BinaryOp.Shr when b >= 0 && b < type.Bits: return new(a >> (int)b);
            }
        }
        return Compare(op, a.CompareTo(b), a == b);
    }

    /// <summary>Two's-complement truncation to <paramref name="type"/>'s width.</summary>
    public static Int128 Wrap(Int128 n, PrimitiveType type)
    {
        if (!type.IsInteger) return n;
        var mask = (Int128.One << type.Bits) - 1;
        var bits = n & mask;
        if (type.IsSignedInteger && bits > type.Range.Max) bits -= Int128.One << type.Bits;
        return bits;
    }

    private static Int128 Saturate(Int128 n, PrimitiveType type)
    {
        var (min, max) = type.Range;
        return n < min ? min : n > max ? max : n;
    }

    /// <summary>A cast of a constant, or null when the value does not fit (left to the run-time check).</summary>
    public static ConstantValue? Cast(ConstantValue c, TypeSymbol from, TypeSymbol to)
    {
        if (to is not PrimitiveType target) return null;
        switch (c.Value)
        {
            case Int128 n when target.IsInteger:
                return n >= target.Range.Min && n <= target.Range.Max ? new(n) : null;
            case Int128 n when target.IsFloat:
                return new(target.Kind == PrimitiveKind.F32 ? (double)(float)n : (double)n);
            case Int128 n when target.Kind == PrimitiveKind.Char:
                return n >= 0 && n <= 0x10FFFF && Rune.IsValid((int)n) ? new(new Rune((int)n)) : null;
            case double d when target.IsFloat:
                return new(target.Kind == PrimitiveKind.F32 ? (double)(float)d : d);
            case double d when target.IsInteger:
            {
                if (double.IsNaN(d)) return new(Int128.Zero);
                var (min, max) = target.Range;
                var t = Math.Truncate(d);
                return new(t <= (double)min ? min : t >= (double)max ? max : (Int128)t);
            }
            case Rune r when target.IsInteger:
                return r.Value <= target.Range.Max ? new((Int128)r.Value) : null;
            default:
                return from.Equals(to) ? c : null;
        }
    }
}
