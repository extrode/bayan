using Bayan.Semantics.Symbols;

namespace Bayan.Semantics.Binding;

/// <summary>A built-in method: its parameters, result, whether it writes to its receiver, and how many trailing parameters a call may leave out.</summary>
internal sealed record BuiltinMethod(
    BuiltinMember Member, (string Name, TypeSymbol Type)[] Parameters, TypeSymbol Returns, bool Mutating = false, int Optional = 0);

/// <summary>Members of strings, spans, lists, maps, arrays, results, optionals, <c>Owned</c>, <c>Arena</c>, <c>StrBuf</c>, <c>Symbol</c>, <c>Io.stdout</c>/<c>Io.stderr</c>/<c>Io.stdin</c>, <c>ProcessResult</c>, <c>rune</c> and enums (spec §3, §16).</summary>
internal static class BuiltinMembers
{
    /// <summary>A built-in property (read without parentheses), or null.</summary>
    public static (BuiltinMember Member, TypeSymbol Type)? Property(TypeSymbol type, string name) => (type, name) switch
    {
        ({ IsString: true }, "length") => (BuiltinMember.StringLength, PrimitiveType.I64),
        ({ IsString: true }, "bytes") => (BuiltinMember.StringBytes, new SpanType(PrimitiveType.U8, readOnly: true)),
        ({ IsString: true }, "isEmpty") => (BuiltinMember.StringIsEmpty, PrimitiveType.Bool),
        ({ IsString: true }, "runeCount") => (BuiltinMember.StringRuneCount, PrimitiveType.I64),
        ({ IsString: true }, "isAscii") => (BuiltinMember.StringIsAscii, PrimitiveType.Bool),
        ({ IsString: true }, "runes") => (BuiltinMember.StringRunes, TextViewType.Runes),
        ({ IsString: true }, "utf16") => (BuiltinMember.StringUtf16, TextViewType.Utf16),
        ({ IsString: true }, "lines") => (BuiltinMember.StringLines, TextViewType.Lines),
        (SpanType, "length") => (BuiltinMember.SpanLength, PrimitiveType.I64),
        (SpanType, "isEmpty") => (BuiltinMember.SpanIsEmpty, PrimitiveType.Bool),
        (ListType, "length") => (BuiltinMember.ListLength, PrimitiveType.I64),
        (ListType, "isEmpty") => (BuiltinMember.ListIsEmpty, PrimitiveType.Bool),
        // `span` is the read-only view, allowed on a `let`; `mutableSpan()` is the writable one (J80).
        (ListType l, "span") => (BuiltinMember.ListAsSpan, new SpanType(l.Element, readOnly: true)),
        (MapType, "length") => (BuiltinMember.MapLength, PrimitiveType.I64),
        (Symbols.ArrayType, "length") => (BuiltinMember.ArrayLength, PrimitiveType.I64),
        (Symbols.ArrayType a, "span") => (BuiltinMember.ArrayAsSpan, new SpanType(a.Element, readOnly: true)),
        (BuiltinType { Kind: BuiltinKind.StrBuf }, "length") => (BuiltinMember.StrBufLength, PrimitiveType.I64),
        (BuiltinType { Kind: BuiltinKind.StrBuf }, "view") => (BuiltinMember.StrBufAsString, PrimitiveType.String),
        (BuiltinType { Kind: BuiltinKind.Symbol }, "text") => (BuiltinMember.SymbolText, PrimitiveType.String),
        (BuiltinType { Kind: BuiltinKind.ProcessResult }, "exitCode") => (BuiltinMember.ProcessExitCode, PrimitiveType.I32),
        (BuiltinType { Kind: BuiltinKind.ProcessResult }, "stdout") => (BuiltinMember.ProcessStdout, PrimitiveType.String),
        (BuiltinType { Kind: BuiltinKind.ProcessResult }, "stderr") => (BuiltinMember.ProcessStderr, PrimitiveType.String),
        (BuiltinType { Kind: BuiltinKind.Panic }, "message") => (BuiltinMember.PanicMessage, PrimitiveType.String),
        // The binder gives `radix` the builtin `Radix` and `align` the builtin `Align`; `digits` is -1 when the hole gives none.
        (BuiltinType { Kind: BuiltinKind.FormatSpec }, "radix") => (BuiltinMember.FormatSpecRadix, PrimitiveType.I32),
        (BuiltinType { Kind: BuiltinKind.FormatSpec }, "debug") => (BuiltinMember.FormatSpecDebug, PrimitiveType.Bool),
        (BuiltinType { Kind: BuiltinKind.FormatSpec }, "width") => (BuiltinMember.FormatSpecWidth, PrimitiveType.I64),
        (BuiltinType { Kind: BuiltinKind.FormatSpec }, "fill") => (BuiltinMember.FormatSpecFill, PrimitiveType.Char),
        (BuiltinType { Kind: BuiltinKind.FormatSpec }, "align") => (BuiltinMember.FormatSpecAlign, PrimitiveType.I32),
        (BuiltinType { Kind: BuiltinKind.FormatSpec }, "digits") => (BuiltinMember.FormatSpecDigits, PrimitiveType.I64),
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
        // `s.copy()` copies from `context.allocator`, `s.copy(to: arena)` into the arena.
        ({ IsString: true }, "copy") => new(BuiltinMember.StringCopy, [("to", BuiltinType.Arena)], PrimitiveType.String, Optional: 1),
        ({ IsString: true }, "startsWith") => new(BuiltinMember.StringStartsWith, [("prefix", PrimitiveType.String)], PrimitiveType.Bool),
        ({ IsString: true }, "endsWith") => new(BuiltinMember.StringEndsWith, [("suffix", PrimitiveType.String)], PrimitiveType.Bool),
        ({ IsString: true }, "contains") => new(BuiltinMember.StringContains, [("text", PrimitiveType.String)], PrimitiveType.Bool),
        ({ IsString: true }, "position") =>
            new(BuiltinMember.StringPosition, [("text", PrimitiveType.String), ("from", PrimitiveType.I64)], new OptionalType(PrimitiveType.I64), Optional: 1),
        ({ IsString: true }, "lastPosition") =>
            new(BuiltinMember.StringLastPosition, [("text", PrimitiveType.String)], new OptionalType(PrimitiveType.I64)),
        ({ IsString: true }, "count") => new(BuiltinMember.StringCount, [("text", PrimitiveType.String)], PrimitiveType.I64),
        // The binder gives `from:` the builtin `Edge`, picks `trimmed(by:)` by the argument's name, and gives `equals`' `as:` the builtin `Comparison`.
        ({ IsString: true }, "trimmed") => new(BuiltinMember.StringTrimmed, [("from", ErrorType.Instance)], PrimitiveType.String, Optional: 1),
        ({ IsString: true }, "equals") =>
            new(BuiltinMember.StringEquals, [("text", PrimitiveType.String), ("as", ErrorType.Instance)], PrimitiveType.Bool, Optional: 1),
        ({ IsString: true }, "lowercased") => new(BuiltinMember.StringLowercased, [], PrimitiveType.String),
        ({ IsString: true }, "uppercased") => new(BuiltinMember.StringUppercased, [], PrimitiveType.String),
        ({ IsString: true }, "replaced") =>
            new(BuiltinMember.StringReplaced, [("text", PrimitiveType.String), ("by", PrimitiveType.String)], PrimitiveType.String),
        ({ IsString: true }, "repeated") => new(BuiltinMember.StringRepeated, [("count", PrimitiveType.I64)], PrimitiveType.String),
        ({ IsString: true }, "padded") =>
            new(BuiltinMember.StringPadded, [("to", PrimitiveType.I64), ("by", PrimitiveType.Char)], PrimitiveType.String),
        // A limit of 0 means no limit. The binder picks `StringPartsRune` for a `rune` separator.
        ({ IsString: true }, "parts") =>
            new(BuiltinMember.StringParts, [("by", PrimitiveType.String), ("limit", PrimitiveType.I64)], TextViewType.Parts, Optional: 1),
        (TextViewType { Kind: TextViewKind.Runes }, "take") => new(BuiltinMember.RunesTake, [("count", PrimitiveType.I64)], PrimitiveType.String),
        (TextViewType { Kind: TextViewKind.Runes }, "skip") => new(BuiltinMember.RunesSkip, [("count", PrimitiveType.I64)], PrimitiveType.String),
        (TextViewType { Kind: TextViewKind.Runes }, "takeLast") => new(BuiltinMember.RunesTakeLast, [("count", PrimitiveType.I64)], PrimitiveType.String),
        (TextViewType { Kind: TextViewKind.Utf16 }, "copy") => new(BuiltinMember.Utf16Copy, [], new ListType(PrimitiveType.U16)),
        (SpanType { Element.IsString: true }, "joined") or (ListType { Element.IsString: true }, "joined") =>
            new(BuiltinMember.StringJoined, [("by", PrimitiveType.String)], PrimitiveType.String),
        (ListType l, "mutableSpan") => new(BuiltinMember.ListMutableSpan, [], new SpanType(l.Element), Mutating: true),
        (Symbols.ArrayType a, "mutableSpan") => new(BuiltinMember.ArrayMutableSpan, [], new SpanType(a.Element), Mutating: true),
        // `copy()` copies from `context.allocator`, `copy(to: arena)` into the arena, as on strings.
        (SpanType s, "copy") => new(BuiltinMember.SpanCopy, [("to", BuiltinType.Arena)], new ListType(s.Element), Optional: 1),
        (ListType l, "copy") => new(BuiltinMember.ListCopy, [("to", BuiltinType.Arena)], l, Optional: 1),
        (ListType l, "append") => new(BuiltinMember.ListAdd, [("item", l.Element)], PrimitiveType.Void, Mutating: true),
        (ListType, "clear") => new(BuiltinMember.ListClear, [], PrimitiveType.Void, Mutating: true),
        (ListType l, "removeLast") => new(BuiltinMember.ListRemoveLast, [], l.Element, Mutating: true),
        // The binder picks `remove(at:)` (an index or a range) and `remove(from:through:)` by the arguments' names.
        (ListType, "remove") =>
            new(BuiltinMember.ListRemoveRange, [("from", PrimitiveType.I64), ("until", PrimitiveType.I64)], PrimitiveType.Void, Mutating: true),
        // The items are copied in, so elements that own memory are left out.
        (ListType l, "replace") when !TypeFacts.IsUnique(l.Element) => new(BuiltinMember.ListReplace,
            [("at", new RangeType(PrimitiveType.I64)), ("by", new SpanType(l.Element, readOnly: true))], PrimitiveType.Void, Mutating: true),
        (ListType l, "tryGet") => new(BuiltinMember.ListTryGet, [("at", PrimitiveType.I64)], new OptionalType(l.Element)),
        // Stable (J47); elements ordered by `<`: numbers, `rune` and `string`.
        (ListType l, "sort") when l.Element is { IsNumeric: true } or { IsString: true } or PrimitiveType { Kind: PrimitiveKind.Char } =>
            new(BuiltinMember.ListSort, [], PrimitiveType.Void, Mutating: true),
        (ListType l, "take") => new(BuiltinMember.ListTake, [], l, Mutating: true),
        (MapType m, "tryGet") => new(BuiltinMember.MapTryGet, [("at", m.KeyType)], new OptionalType(m.ValueType)),
        (MapType m, "slot") => new(BuiltinMember.MapSlot, [("key", m.KeyType)], m.ValueType, Mutating: true),
        (MapType m, "contains") => new(BuiltinMember.MapContains, [("key", m.KeyType)], PrimitiveType.Bool),
        (MapType m, "remove") => new(BuiltinMember.MapRemove, [("key", m.KeyType)], PrimitiveType.Bool, Mutating: true),
        (MapType m, "copy") => new(BuiltinMember.MapCopy, [("to", BuiltinType.Arena)], m),
        (MapType, "clear") => new(BuiltinMember.MapClear, [], PrimitiveType.Void, Mutating: true),
        (MapType m, "take") => new(BuiltinMember.MapTake, [], m, Mutating: true),
        (BuiltinType { Kind: BuiltinKind.Arena }, "reset") => new(BuiltinMember.ArenaReset, [], PrimitiveType.Void, Mutating: true),
        (BuiltinType { Kind: BuiltinKind.StrBuf }, "append") =>
            new(BuiltinMember.StrBufAppend, [("text", PrimitiveType.String)], PrimitiveType.Void, Mutating: true),
        // `at:` is a byte offset on a rune boundary, like a string slice's bounds.
        (BuiltinType { Kind: BuiltinKind.StrBuf }, "appendRepeated") =>
            new(BuiltinMember.StrBufAppendRepeated, [("rune", PrimitiveType.Char), ("by", PrimitiveType.I64)], PrimitiveType.Void, Mutating: true),
        (BuiltinType { Kind: BuiltinKind.StrBuf }, "insert") =>
            new(BuiltinMember.StrBufInsert, [("text", PrimitiveType.String), ("at", PrimitiveType.I64)], PrimitiveType.Void, Mutating: true),
        (BuiltinType { Kind: BuiltinKind.StrBuf } s, "copy") => new(BuiltinMember.StrBufCopy, [("to", BuiltinType.Arena)], s),
        // `finish()` returns the text borrowed from the buffer, like `view`; it does not consume the buffer yet.
        (BuiltinType { Kind: BuiltinKind.StrBuf }, "finish") => new(BuiltinMember.StrBufAsString, [], PrimitiveType.String),
        (BuiltinType { Kind: BuiltinKind.StrBuf }, "clear") => new(BuiltinMember.StrBufClear, [], PrimitiveType.Void, Mutating: true),
        (BuiltinType { Kind: BuiltinKind.StrBuf } s, "take") => new(BuiltinMember.StrBufTake, [], s, Mutating: true),
        (OptionalType o, "take") when TypeFacts.IsUnique(o.Inner) => new(BuiltinMember.OptionalTake, [], o, Mutating: true),
        (BuiltinType { Kind: BuiltinKind.IoStream }, "write") => new(BuiltinMember.IoWrite, [("text", PrimitiveType.String)], PrimitiveType.Void),
        // The binder wraps the result in `Result<string, IoError>`; it binds `readAllBytes()` as `Builtins.IoStdinReadAllBytes`, which fails.
        (BuiltinType { Kind: BuiltinKind.IoInput }, "readAll") => new(BuiltinMember.IoReadAll, [], PrimitiveType.String),
        _ => null,
    };

    // `Io.stdin.readAllBytes` is bound by the binder as a failing builtin function, so `Method` has no entry for it.
    public static bool HasMethod(TypeSymbol type, string name) =>
        Method(type, name) is not null || (type is BuiltinType { Kind: BuiltinKind.IoInput } && name == "readAllBytes");

    private static readonly string[] Candidates =
        ["length", "bytes", "isEmpty", "runeCount", "isAscii", "runes", "utf16", "lines", "value", "copy", "startsWith", "endsWith", "contains",
         "position", "lastPosition", "count", "trimmed", "equals", "lowercased", "uppercased", "replaced", "repeated", "padded", "parts", "skip", "takeLast", "joined", "span", "mutableSpan", "clear", "removeLast", "replace", "sort", "tryGet", "slot", "remove", "reset", "append", "appendRepeated", "insert", "finish", "view", "take", "write", "readAll", "readAllBytes", "name", "ordinal", "exitCode", "stdout", "stderr", "message", "radix", "debug", "width", "fill", "align", "digits"];

    /// <summary>Every member name of <paramref name="type"/>, for suggestions.</summary>
    public static IEnumerable<string> Names(TypeSymbol type) =>
        Candidates.Where(n => Property(type, n) is not null || Method(type, n) is not null);
}
