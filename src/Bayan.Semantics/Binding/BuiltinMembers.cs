using Bayan.Semantics.Symbols;

namespace Bayan.Semantics.Binding;

/// <summary>A built-in method: its parameters, result, and whether it writes to its receiver.</summary>
internal sealed record BuiltinMethod(
    BuiltinMember Member, (string Name, TypeSymbol Type)[] Parameters, TypeSymbol Returns, bool Mutating = false);

/// <summary>Members of strings, spans, lists, maps, arrays, results, optionals, <c>Owned</c>, <c>Arena</c>, <c>StrBuf</c>, <c>Io.stdout</c>/<c>Io.stderr</c>/<c>Io.stdin</c>, <c>ProcessResult</c>, <c>char</c> and enums (spec §3, §16).</summary>
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
        (MapType, "length") => (BuiltinMember.MapLength, PrimitiveType.I64),
        (Symbols.ArrayType, "length") => (BuiltinMember.ArrayLength, PrimitiveType.I64),
        (BuiltinType { Kind: BuiltinKind.StrBuf }, "length") => (BuiltinMember.StrBufLength, PrimitiveType.I64),
        (BuiltinType { Kind: BuiltinKind.ProcessResult }, "exitCode") => (BuiltinMember.ProcessExitCode, PrimitiveType.I32),
        (BuiltinType { Kind: BuiltinKind.ProcessResult }, "stdout") => (BuiltinMember.ProcessStdout, PrimitiveType.String),
        (BuiltinType { Kind: BuiltinKind.ProcessResult }, "stderr") => (BuiltinMember.ProcessStderr, PrimitiveType.String),
        (PrimitiveType { Kind: PrimitiveKind.Char }, "value") => (BuiltinMember.CharValue, PrimitiveType.U32),
        (ResultType, "isOk") => (BuiltinMember.ResultIsOk, PrimitiveType.Bool),
        (ResultType r, "value") => (BuiltinMember.ResultValue, r.Value),
        (ResultType r, "err") => (BuiltinMember.ResultError, r.Error),
        (OwnedType o, "value") => (BuiltinMember.OwnedValue, o.Inner),
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
        (ListType l, "copy") => new(BuiltinMember.ListCopy, [("to", BuiltinType.Arena)], l),
        (ListType l, "add") => new(BuiltinMember.ListAdd, [("item", l.Element)], PrimitiveType.Void, Mutating: true),
        (ListType l, "asSpan") => new(BuiltinMember.ListAsSpan, [], new SpanType(l.Element)),
        (ListType, "clear") => new(BuiltinMember.ListClear, [], PrimitiveType.Void, Mutating: true),
        (ListType l, "removeLast") => new(BuiltinMember.ListRemoveLast, [], l.Element, Mutating: true),
        (ListType, "removeRange") =>
            new(BuiltinMember.ListRemoveRange, [("start", PrimitiveType.I64), ("end", PrimitiveType.I64)], PrimitiveType.Void, Mutating: true),
        (ListType l, "take") => new(BuiltinMember.ListTake, [], l, Mutating: true),
        (MapType m, "getOr") => new(BuiltinMember.MapGetOr, [("key", m.KeyType), ("default", m.ValueType)], m.ValueType),
        (MapType m, "slot") => new(BuiltinMember.MapSlot, [("key", m.KeyType)], m.ValueType, Mutating: true),
        (MapType m, "contains") => new(BuiltinMember.MapContains, [("key", m.KeyType)], PrimitiveType.Bool),
        (MapType m, "remove") => new(BuiltinMember.MapRemove, [("key", m.KeyType)], PrimitiveType.Bool, Mutating: true),
        (MapType m, "copy") => new(BuiltinMember.MapCopy, [("to", BuiltinType.Arena)], m),
        (MapType, "clear") => new(BuiltinMember.MapClear, [], PrimitiveType.Void, Mutating: true),
        (MapType m, "take") => new(BuiltinMember.MapTake, [], m, Mutating: true),
        (Symbols.ArrayType a, "asSpan") => new(BuiltinMember.ArrayAsSpan, [], new SpanType(a.Element)),
        (BuiltinType { Kind: BuiltinKind.Arena }, "reset") => new(BuiltinMember.ArenaReset, [], PrimitiveType.Void, Mutating: true),
        (BuiltinType { Kind: BuiltinKind.StrBuf }, "append") =>
            new(BuiltinMember.StrBufAppend, [("text", PrimitiveType.String)], PrimitiveType.Void, Mutating: true),
        (BuiltinType { Kind: BuiltinKind.StrBuf } s, "copy") => new(BuiltinMember.StrBufCopy, [("to", BuiltinType.Arena)], s),
        (BuiltinType { Kind: BuiltinKind.StrBuf }, "asString") => new(BuiltinMember.StrBufAsString, [], PrimitiveType.String),
        (BuiltinType { Kind: BuiltinKind.StrBuf }, "clear") => new(BuiltinMember.StrBufClear, [], PrimitiveType.Void, Mutating: true),
        (BuiltinType { Kind: BuiltinKind.StrBuf } s, "take") => new(BuiltinMember.StrBufTake, [], s, Mutating: true),
        (OptionalType o, "take") when TypeFacts.IsUnique(o.Inner) => new(BuiltinMember.OptionalTake, [], o, Mutating: true),
        ({ IsInteger: true }, "toHex") => new(BuiltinMember.IntToHex, [], PrimitiveType.String),
        (BuiltinType { Kind: BuiltinKind.IoStream }, "write") => new(BuiltinMember.IoWrite, [("text", PrimitiveType.String)], PrimitiveType.Void),
        // The binder wraps the result in `Result<string, IoError>`.
        (BuiltinType { Kind: BuiltinKind.IoInput }, "readAll") => new(BuiltinMember.IoReadAll, [], PrimitiveType.String),
        _ => null,
    };

    public static bool HasMethod(TypeSymbol type, string name) => Method(type, name) is not null;

    private static readonly string[] Candidates =
        ["length", "bytes", "isEmpty", "value", "copy", "startsWith", "endsWith", "contains", "add", "asSpan", "clear", "removeLast", "removeRange", "getOr", "slot", "remove", "reset", "append", "asString", "take", "write", "readAll", "toHex", "name", "ordinal", "exitCode", "stdout", "stderr"];

    /// <summary>Every member name of <paramref name="type"/>, for suggestions.</summary>
    public static IEnumerable<string> Names(TypeSymbol type) =>
        Candidates.Where(n => Property(type, n) is not null || Method(type, n) is not null);
}
