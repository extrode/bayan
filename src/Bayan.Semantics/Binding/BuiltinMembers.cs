using Bayan.Semantics.Symbols;

namespace Bayan.Semantics.Binding;

/// <summary>A built-in method: its parameters, result, and whether it writes to its receiver.</summary>
internal sealed record BuiltinMethod(
    BuiltinMember Member, (string Name, TypeSymbol Type)[] Parameters, TypeSymbol Returns, bool Mutating = false);

/// <summary>Members of strings, spans, lists, arrays, results, <c>Arena</c>, <c>StrBuf</c>, <c>Io.stdout</c>/<c>Io.stderr</c>, <c>char</c> and enums (spec §3, §16).</summary>
internal static class BuiltinMembers
{
    /// <summary>A built-in property (read without parentheses), or null.</summary>
    public static (BuiltinMember Member, TypeSymbol Type)? Property(TypeSymbol type, string name) => (type, name) switch
    {
        ({ IsString: true }, "length") => (BuiltinMember.StringLength, PrimitiveType.I64),
        ({ IsString: true }, "bytes") => (BuiltinMember.StringBytes, new SpanType(PrimitiveType.U8, readOnly: true)),
        ({ IsString: true }, "isEmpty") => (BuiltinMember.StringIsEmpty, PrimitiveType.Bool),
        (SpanType, "length") => (BuiltinMember.SpanLength, PrimitiveType.I64),
        (SpanType, "isEmpty") => (BuiltinMember.SpanIsEmpty, PrimitiveType.Bool),
        (ListType, "length") => (BuiltinMember.ListLength, PrimitiveType.I64),
        (ListType, "isEmpty") => (BuiltinMember.ListIsEmpty, PrimitiveType.Bool),
        (Symbols.ArrayType, "length") => (BuiltinMember.ArrayLength, PrimitiveType.I64),
        (BuiltinType { Kind: BuiltinKind.StrBuf }, "length") => (BuiltinMember.StrBufLength, PrimitiveType.I64),
        (PrimitiveType { Kind: PrimitiveKind.Char }, "value") => (BuiltinMember.CharValue, PrimitiveType.U32),
        (ResultType, "isOk") => (BuiltinMember.ResultIsOk, PrimitiveType.Bool),
        (ResultType r, "value") => (BuiltinMember.ResultValue, r.Value),
        (ResultType r, "err") => (BuiltinMember.ResultError, r.Error),
        (EnumSymbol, "name") => (BuiltinMember.EnumName, PrimitiveType.String),
        (EnumSymbol, "ordinal") => (BuiltinMember.EnumOrdinal, PrimitiveType.I32),
        _ => null,
    };

    public static BuiltinMethod? Method(TypeSymbol type, string name) => (type, name) switch
    {
        ({ IsString: true }, "copy") => new(BuiltinMember.StringCopy, [("to", BuiltinType.Arena)], PrimitiveType.String),
        ({ IsString: true }, "startsWith") => new(BuiltinMember.StringStartsWith, [("prefix", PrimitiveType.String)], PrimitiveType.Bool),
        ({ IsString: true }, "endsWith") => new(BuiltinMember.StringEndsWith, [("suffix", PrimitiveType.String)], PrimitiveType.Bool),
        ({ IsString: true }, "contains") => new(BuiltinMember.StringContains, [("text", PrimitiveType.String)], PrimitiveType.Bool),
        (SpanType s, "copy") => new(BuiltinMember.SpanCopy, [("to", BuiltinType.Arena)], new ListType(s.Element)),
        (ListType l, "add") => new(BuiltinMember.ListAdd, [("item", l.Element)], PrimitiveType.Void, Mutating: true),
        (ListType l, "asSpan") => new(BuiltinMember.ListAsSpan, [], new SpanType(l.Element)),
        (ListType, "clear") => new(BuiltinMember.ListClear, [], PrimitiveType.Void, Mutating: true),
        (ListType l, "removeLast") => new(BuiltinMember.ListRemoveLast, [], l.Element, Mutating: true),
        (Symbols.ArrayType a, "asSpan") => new(BuiltinMember.ArrayAsSpan, [], new SpanType(a.Element)),
        (BuiltinType { Kind: BuiltinKind.Arena }, "reset") => new(BuiltinMember.ArenaReset, [], PrimitiveType.Void, Mutating: true),
        (BuiltinType { Kind: BuiltinKind.StrBuf }, "append") =>
            new(BuiltinMember.StrBufAppend, [("text", PrimitiveType.String)], PrimitiveType.Void, Mutating: true),
        (BuiltinType { Kind: BuiltinKind.StrBuf }, "asString") => new(BuiltinMember.StrBufAsString, [], PrimitiveType.String),
        (BuiltinType { Kind: BuiltinKind.StrBuf }, "clear") => new(BuiltinMember.StrBufClear, [], PrimitiveType.Void, Mutating: true),
        (BuiltinType { Kind: BuiltinKind.IoStream }, "write") => new(BuiltinMember.IoWrite, [("text", PrimitiveType.String)], PrimitiveType.Void),
        _ => null,
    };

    public static bool HasMethod(TypeSymbol type, string name) => Method(type, name) is not null;

    private static readonly string[] Candidates =
        ["length", "bytes", "isEmpty", "value", "copy", "startsWith", "endsWith", "contains", "add", "asSpan", "clear", "removeLast", "reset", "append", "asString", "write", "name", "ordinal"];

    /// <summary>Every member name of <paramref name="type"/>, for suggestions.</summary>
    public static IEnumerable<string> Names(TypeSymbol type) =>
        Candidates.Where(n => Property(type, n) is not null || Method(type, n) is not null);
}
