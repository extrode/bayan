namespace Bayan.Semantics.Symbols;

/// <summary>
/// A type. Primitives are singletons, user types (structs, error sets) have reference identity,
/// and constructed types (<c>T?</c>, <c>Span&lt;T&gt;</c>, tuples, …) compare structurally.
/// </summary>
public abstract class TypeSymbol
{
    /// <summary>The type as written in diagnostics.</summary>
    public abstract string DisplayName { get; }

    /// <summary>While set, declared types display with their module (<c>a.Point</c>); see <see cref="ErrorSetSymbol.DisplayNameAmong"/>.</summary>
    [ThreadStatic]
    internal static bool QualifyModules;

    private protected static string Qualify(ModuleSymbol module, string name) =>
        QualifyModules && module.Name != "builtin" ? module.Name + "." + name : name;

    public override string ToString() => DisplayName;

    public bool IsError => this is ErrorType;

    public bool IsNever => this == PrimitiveType.Never;

    public bool IsVoid => this == PrimitiveType.Void;

    public bool IsBool => this == PrimitiveType.Bool;

    public bool IsString => this == PrimitiveType.String;

    public bool IsInteger => this is PrimitiveType { Kind: >= PrimitiveKind.I8 and <= PrimitiveKind.Usize };

    public bool IsFloat => this is PrimitiveType { Kind: PrimitiveKind.F32 or PrimitiveKind.F64 };

    public bool IsNumeric => IsInteger || IsFloat;

    public bool IsSignedInteger => this is PrimitiveType { Kind: PrimitiveKind.I8 or PrimitiveKind.I16 or PrimitiveKind.I32 or PrimitiveKind.I64 or PrimitiveKind.Isize };

    public bool IsUnsignedInteger => IsInteger && !IsSignedInteger;

    /// <summary>Spans and string slices: values that point into memory owned elsewhere.</summary>
    public virtual bool IsBorrow => false;
}

public enum PrimitiveKind
{
    I8, I16, I32, I64, U8, U16, U32, U64, Isize, Usize,
    F32, F64,
    Bool, Char, String, Void, Never,
}

public sealed class PrimitiveType : TypeSymbol
{
    private PrimitiveType(PrimitiveKind kind, string name, int bits)
    {
        Kind = kind;
        Name = name;
        Bits = bits;
    }

    public PrimitiveKind Kind { get; }

    public string Name { get; }

    /// <summary>Width of a numeric type; 0 for the others.</summary>
    public int Bits { get; }

    public override string DisplayName => Name;

    /// <summary>
    /// <c>string</c> is not treated as a borrow in v0.1: struct fields may hold strings (literals or
    /// arena copies) so programs like tally-lite can name things. Spans stay borrows.
    /// </summary>
    public override bool IsBorrow => false;

    public static readonly PrimitiveType I8 = new(PrimitiveKind.I8, "i8", 8);
    public static readonly PrimitiveType I16 = new(PrimitiveKind.I16, "i16", 16);
    public static readonly PrimitiveType I32 = new(PrimitiveKind.I32, "i32", 32);
    public static readonly PrimitiveType I64 = new(PrimitiveKind.I64, "i64", 64);
    public static readonly PrimitiveType U8 = new(PrimitiveKind.U8, "u8", 8);
    public static readonly PrimitiveType U16 = new(PrimitiveKind.U16, "u16", 16);
    public static readonly PrimitiveType U32 = new(PrimitiveKind.U32, "u32", 32);
    public static readonly PrimitiveType U64 = new(PrimitiveKind.U64, "u64", 64);
    public static readonly PrimitiveType Isize = new(PrimitiveKind.Isize, "isize", 64);
    public static readonly PrimitiveType Usize = new(PrimitiveKind.Usize, "usize", 64);
    public static readonly PrimitiveType F32 = new(PrimitiveKind.F32, "f32", 32);
    public static readonly PrimitiveType F64 = new(PrimitiveKind.F64, "f64", 64);
    public static readonly PrimitiveType Bool = new(PrimitiveKind.Bool, "bool", 0);
    public static readonly PrimitiveType Char = new(PrimitiveKind.Char, "rune", 32);
    public static readonly PrimitiveType String = new(PrimitiveKind.String, "string", 0);
    public static readonly PrimitiveType Void = new(PrimitiveKind.Void, "void", 0);
    public static readonly PrimitiveType Never = new(PrimitiveKind.Never, "never", 0);

    /// <summary>Lowercase type names: canonical names and the C# aliases (spec §3.1).</summary>
    public static readonly IReadOnlyDictionary<string, PrimitiveType> ByName = new Dictionary<string, PrimitiveType>
    {
        ["i8"] = I8, ["i16"] = I16, ["i32"] = I32, ["i64"] = I64,
        ["u8"] = U8, ["u16"] = U16, ["u32"] = U32, ["u64"] = U64,
        ["isize"] = Isize, ["usize"] = Usize, ["f32"] = F32, ["f64"] = F64,
        ["bool"] = Bool, ["rune"] = Char, ["string"] = String, ["void"] = Void, ["never"] = Never,
        ["sbyte"] = I8, ["short"] = I16, ["int"] = I32, ["long"] = I64,
        ["byte"] = U8, ["ushort"] = U16, ["uint"] = U32, ["ulong"] = U64,
        ["nint"] = Isize, ["nuint"] = Usize, ["float"] = F32, ["double"] = F64,
    };

    /// <summary>Smallest and largest value of an integer type.</summary>
    public (Int128 Min, Int128 Max) Range => Kind switch
    {
        PrimitiveKind.I8 => (sbyte.MinValue, sbyte.MaxValue),
        PrimitiveKind.I16 => (short.MinValue, short.MaxValue),
        PrimitiveKind.I32 => (int.MinValue, int.MaxValue),
        PrimitiveKind.I64 or PrimitiveKind.Isize => (long.MinValue, long.MaxValue),
        PrimitiveKind.U8 => (0, byte.MaxValue),
        PrimitiveKind.U16 => (0, ushort.MaxValue),
        PrimitiveKind.U32 => (0, uint.MaxValue),
        PrimitiveKind.U64 or PrimitiveKind.Usize => (0, ulong.MaxValue),
        _ => throw new InvalidOperationException($"{Name} is not an integer type"),
    };
}

/// <summary>The type of an expression that failed to bind; converts to and from everything, so one error does not cascade.</summary>
public sealed class ErrorType : TypeSymbol
{
    public static readonly ErrorType Instance = new();

    private ErrorType() { }

    public override string DisplayName => "?";
}

/// <summary>The type of the <c>null</c> literal before a target type gives it one.</summary>
public sealed class NullType : TypeSymbol
{
    public static readonly NullType Instance = new();

    private NullType() { }

    public override string DisplayName => "null";
}

/// <summary>
/// A type parameter <c>T</c> of a generic function, struct or union, as its template sees it.
/// Each declaration has its own, compared by reference.
/// </summary>
public sealed class TypeParameterType(string name) : TypeSymbol
{
    public string Name { get; } = name;

    public override string DisplayName => Name;

    /// <summary>The interfaces of its <c>where</c> clauses (for <c>Self</c>, its interface); its members are theirs.</summary>
    public IReadOnlyList<InterfaceSymbol> Constraints { get; set; } = [];

    /// <summary><paramref name="t"/> mentions a type parameter, so it belongs to a template and is never emitted.</summary>
    public static bool Occurs(TypeSymbol t) => t switch
    {
        TypeParameterType => true,
        OptionalType o => Occurs(o.Inner),
        SpanType s => Occurs(s.Element),
        ListType l => Occurs(l.Element),
        MapType m => Occurs(m.KeyType) || Occurs(m.ValueType),
        ArrayType a => Occurs(a.Element),
        TupleType tt => tt.Fields.Any(f => Occurs(f.Type)),
        ResultType r => Occurs(r.Value),
        StructSymbol s => s.IsGeneric,
        StateType st => st.Struct.IsGeneric,
        UnionSymbol u => u.IsGeneric,
        FunctionType f => f.Parameters.Any(Occurs) || Occurs(f.Return),
        InterfaceSymbol i => i.IsGeneric,
        DynRefType d => Occurs(d.Interface),
        _ => false,
    };
}

/// <summary>Base for structurally compared constructed types.</summary>
public abstract class ConstructedType : TypeSymbol
{
    protected abstract object Key { get; }

    public override bool Equals(object? obj) => obj is ConstructedType other && GetType() == other.GetType() && Key.Equals(other.Key);

    public override int GetHashCode() => HashCode.Combine(GetType(), Key);
}

public sealed class OptionalType(TypeSymbol inner) : ConstructedType
{
    public TypeSymbol Inner { get; } = inner;

    protected override object Key => Inner;

    public override string DisplayName => Inner.DisplayName + "?";

    public override bool IsBorrow => Inner.IsBorrow;
}

/// <summary><c>Span&lt;T&gt;</c>: a borrowed (pointer, length) view.</summary>
public sealed class SpanType(TypeSymbol element, bool readOnly = false) : ConstructedType
{
    public TypeSymbol Element { get; } = element;

    /// <summary><c>ReadOnlySpan&lt;T&gt;</c>: elements cannot be assigned (<c>string.bytes</c> is one, so strings stay immutable and valid UTF-8).</summary>
    public bool ReadOnly { get; } = readOnly;

    protected override object Key => (Element, ReadOnly);

    public override string DisplayName => ReadOnly ? $"ReadOnlySpan<{Element.DisplayName}>" : $"Span<{Element.DisplayName}>";

    public override bool IsBorrow => true;
}

/// <summary>The string views, in the order <c>byn_view_more</c> reads them (spec §16.4).</summary>
public enum TextViewKind { Runes, Utf16, Lines, Parts }

/// <summary><c>s.runes</c>, <c>s.utf16</c>, <c>s.lines</c> or <c>s.parts(by:)</c>: a lazy view of a string's text, walked by <c>for</c>.</summary>
public sealed class TextViewType(TextViewKind kind) : ConstructedType
{
    public TextViewKind Kind { get; } = kind;

    /// <summary>What <c>for</c> yields: <c>rune</c>, <c>u16</c>, or a borrowed <c>string</c>.</summary>
    public TypeSymbol Element => Kind switch
    {
        TextViewKind.Runes => PrimitiveType.Char,
        TextViewKind.Utf16 => PrimitiveType.U16,
        _ => PrimitiveType.String,
    };

    protected override object Key => Kind;

    public override string DisplayName => Kind switch
    {
        TextViewKind.Runes => "RuneView",
        TextViewKind.Utf16 => "Utf16View",
        TextViewKind.Lines => "LineView",
        _ => "PartView",
    };

    public override bool IsBorrow => true;

    public static readonly TextViewType Runes = new(TextViewKind.Runes);
    public static readonly TextViewType Utf16 = new(TextViewKind.Utf16);
    public static readonly TextViewType Lines = new(TextViewKind.Lines);
    public static readonly TextViewType Parts = new(TextViewKind.Parts);
}

/// <summary><c>dyn ref I</c>: a borrow of any value conforming to <see cref="Interface"/>, passed as (data, vtable).</summary>
public sealed class DynRefType(InterfaceSymbol @interface, bool readOnly = false) : ConstructedType
{
    public InterfaceSymbol Interface { get; } = @interface;

    /// <summary><c>dyn ref readonly I</c>: only members without <c>ref this</c> can be called.</summary>
    public bool ReadOnly { get; } = readOnly;

    protected override object Key => (Interface, ReadOnly);

    public override string DisplayName => ReadOnly ? $"dyn ref readonly {Interface.DisplayName}" : $"dyn ref {Interface.DisplayName}";

    public override bool IsBorrow => true;
}

/// <summary><c>List&lt;T&gt;</c>: a growable array allocated from the context allocator.</summary>
public sealed class ListType(TypeSymbol element) : ConstructedType
{
    public TypeSymbol Element { get; } = element;

    protected override object Key => Element;

    public override string DisplayName => $"List<{Element.DisplayName}>";
}

/// <summary><c>Map&lt;K, V&gt;</c>: an insertion-ordered hash map allocated from the context allocator.</summary>
public sealed class MapType(TypeSymbol key, TypeSymbol value) : ConstructedType
{
    // `Key` is taken by the structural identity below.
    public TypeSymbol KeyType { get; } = key;

    public TypeSymbol ValueType { get; } = value;

    /// <summary>What <c>for (let e in m)</c> yields: <c>(K key, V value)</c>.</summary>
    public TupleType Entry => new([new TupleField("key", KeyType), new TupleField("value", ValueType)]);

    protected override object Key => (KeyType, ValueType);

    public override string DisplayName => $"Map<{KeyType.DisplayName}, {ValueType.DisplayName}>";

    /// <summary>
    /// Keys hash their value: integers, <c>char</c>, <c>bool</c>, <c>string</c>, <c>Symbol</c>, plain enums, and tuples
    /// and structs (not <c>@noEquality</c>) whose parts are keys. A type parameter part is checked at each instantiation.
    /// </summary>
    public static bool IsValidKey(TypeSymbol t) => IsValidKey(t, []);

    /// <summary><paramref name="path"/> holds the structs being checked, so a struct that contains itself (BYN2761) ends the walk.</summary>
    private static bool IsValidKey(TypeSymbol t, HashSet<TypeSymbol> path)
    {
        switch (t)
        {
            case TupleType tt:
                return tt.Fields.All(f => f.Type is TypeParameterType || IsValidKey(f.Type, path));
            case StructSymbol s:
                if (HasNoEquality(s) || !path.Add(s)) return false;
                var valid = s.Fields.All(f => f.Type is TypeParameterType || IsValidKey(f.Type, path));
                path.Remove(s);
                return valid;
            default:
                return t.IsInteger || t.IsString || t == PrimitiveType.Char || t.IsBool || t == BuiltinType.Symbol
                    || t is EnumSymbol { Fields.Count: 0 };
        }
    }

    /// <summary>
    /// For a tuple or struct that is not a key, its first part that is not one: a field's name, or an element's name
    /// or 1-based position. Null for any other type, and for a <c>@noEquality</c> struct, which no part disqualifies.
    /// </summary>
    public static (string Part, TypeSymbol Type)? InvalidKeyPart(TypeSymbol t)
    {
        IEnumerable<(string Part, TypeSymbol Type)> parts = t switch
        {
            TupleType tt => tt.Fields.Select((f, i) => (f.Name ?? (i + 1).ToString(), f.Type)),
            StructSymbol s when !HasNoEquality(s) => s.Fields.Select(f => (f.Name, f.Type)),
            _ => [],
        };
        foreach (var (part, type) in parts)
            if (type is not TypeParameterType && !IsValidKey(type))
                return (part, type);
        return null;
    }

    private static bool HasNoEquality(StructSymbol s) => s.Decl.Attributes.Any(a => a.Name.Text == "noEquality");
}

/// <summary><c>Owned&lt;T&gt;</c>: a non-null pointer to a <c>T</c> copied into the context allocator; copies share it.</summary>
public sealed class OwnedType(TypeSymbol inner) : ConstructedType
{
    public TypeSymbol Inner { get; } = inner;

    protected override object Key => Inner;

    public override string DisplayName => $"Owned<{Inner.DisplayName}>";
}

/// <summary><c>Untrusted&lt;T&gt;</c>: a <c>T</c> from outside the program; no implicit conversion either way, and the same C type as <c>T</c>.</summary>
public sealed class UntrustedType(TypeSymbol inner) : ConstructedType
{
    public TypeSymbol Inner { get; } = inner;

    protected override object Key => Inner;

    public override string DisplayName => $"Untrusted<{Inner.DisplayName}>";
}

/// <summary><c>T[N]</c>: a fixed-size inline array.</summary>
public sealed class ArrayType(TypeSymbol element, long length) : ConstructedType
{
    public TypeSymbol Element { get; } = element;

    public long Length { get; } = length;

    protected override object Key => (Element, Length);

    public override string DisplayName => $"{Element.DisplayName}[{Length}]";
}

/// <summary>The value of <c>a..b</c>: used by <c>for</c> loops, slices and patterns.</summary>
public sealed class RangeType(TypeSymbol element) : ConstructedType
{
    public TypeSymbol Element { get; } = element;

    protected override object Key => Element;

    public override string DisplayName => $"Range<{Element.DisplayName}>";

    /// <summary>How a range value is stored: its start, its end, and whether the end is included.</summary>
    public TupleType Layout => new([new(null, Element), new(null, Element), new(null, PrimitiveType.Bool)]);
}

public sealed record TupleField(string? Name, TypeSymbol Type);

public sealed class TupleType(IReadOnlyList<TupleField> fields) : ConstructedType
{
    private readonly string _key = string.Join(",", fields.Select(f => f.Type.GetHashCode() + ":" + f.Name));

    public IReadOnlyList<TupleField> Fields { get; } = fields;

    protected override object Key => _key;

    public override bool Equals(object? obj) =>
        obj is TupleType other && other.Fields.Count == Fields.Count &&
        Fields.Zip(other.Fields).All(p => p.First.Name == p.Second.Name && p.First.Type.Equals(p.Second.Type));

    public override int GetHashCode() => _key.GetHashCode(StringComparison.Ordinal);

    public override string DisplayName =>
        "(" + string.Join(", ", Fields.Select(f => f.Name is null ? f.Type.DisplayName : f.Type.DisplayName + " " + f.Name)) + ")";

    public override bool IsBorrow => Fields.Any(f => f.Type.IsBorrow);

    /// <summary>Index of the element called <paramref name="name"/> (or <c>item1</c>, <c>item2</c>, …), or -1.</summary>
    public int IndexOf(string name)
    {
        for (var i = 0; i < Fields.Count; i++)
            if (Fields[i].Name == name || "item" + (i + 1) == name) return i;
        return -1;
    }
}

/// <summary>
/// <c>(A, B) -> R fails E uses net</c>: a (code, environment) pair that borrows the frame that made it.
/// Error sets compare as a set; <see cref="Uses"/> holds effect names, sorted.
/// </summary>
public sealed class FunctionType(IReadOnlyList<TypeSymbol> parameters, TypeSymbol @return, IReadOnlyList<ErrorSetSymbol> fails, IReadOnlyList<string> uses)
    : ConstructedType
{
    public IReadOnlyList<TypeSymbol> Parameters { get; } = parameters;

    public TypeSymbol Return { get; } = @return;

    public IReadOnlyList<ErrorSetSymbol> Fails { get; } = fails;

    public IReadOnlyList<string> Uses { get; } = uses.Order(StringComparer.Ordinal).ToArray();

    // Unused: Equals and GetHashCode below compare all four parts.
    protected override object Key => Return;

    public override bool Equals(object? obj) =>
        obj is FunctionType other && Parameters.SequenceEqual(other.Parameters) && Return.Equals(other.Return) &&
        Fails.Count == other.Fails.Count && Fails.All(other.Fails.Contains) && Uses.SequenceEqual(other.Uses);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var p in Parameters) hash.Add(p);
        hash.Add(Return);
        hash.Add(Fails.Aggregate(0, (h, s) => h ^ s.GetHashCode()));
        foreach (var u in Uses) hash.Add(u, StringComparer.Ordinal);
        return hash.ToHashCode();
    }

    /// <summary>A lone parameter needs parentheses when it is itself a tuple or function type.</summary>
    public override string DisplayName =>
        (Parameters is [var p] && p is not (TupleType or FunctionType) ? p.DisplayName : "(" + string.Join(", ", Parameters.Select(p => p.DisplayName)) + ")") +
        " -> " + (Return is FunctionType && (Fails.Count > 0 || Uses.Count > 0) ? "(" + Return.DisplayName + ")" : Return.DisplayName) +
        (Fails.Count > 0 ? " fails " + string.Join(", ", Fails.Select(s => s.DisplayName)) : "") +
        (Uses.Count > 0 ? " uses " + string.Join(", ", Uses) : "");

    public override bool IsBorrow => true;
}

/// <summary><c>Result&lt;T, E&gt;</c>: a value or an error from <see cref="Error"/> (spec §3.8).</summary>
public sealed class ResultType(TypeSymbol value, TypeSymbol error) : ConstructedType
{
    public TypeSymbol Value { get; } = value;

    /// <summary>An <see cref="ErrorSetSymbol"/>.</summary>
    public TypeSymbol Error { get; } = error;

    protected override object Key => (Value, Error);

    public override string DisplayName => $"Result<{Value.DisplayName}, {Error.DisplayName}>";

    public override bool IsBorrow => Value.IsBorrow;
}

/// <summary>
/// <c>T.s</c> or <c>T is .a or .b</c>: a struct with states, known to be in one of <see cref="States"/> (spec §3.17).
/// The states are cases of the struct's synthetic states union, in declaration order.
/// </summary>
public sealed class StateType(StructSymbol @struct, IReadOnlyList<UnionVariantSymbol> states) : ConstructedType
{
    public StructSymbol Struct { get; } = @struct;

    public IReadOnlyList<UnionVariantSymbol> States { get; } = states.Distinct().OrderBy(v => v.Tag).ToArray();

    protected override object Key => (Struct, string.Join(",", States.Select(v => v.Name)));

    public override string DisplayName => States.Count == 1
        ? $"{Struct.DisplayName}.{States[0].Name}"
        : $"{Struct.DisplayName} is {string.Join(" or ", States.Select(v => "." + v.Name))}";

    public override bool IsBorrow => Struct.IsBorrow;
}

/// <summary>Built-in nominal types with runtime support.</summary>
public enum BuiltinKind
{
    /// <summary>A region allocator; the value is a handle to runtime state.</summary>
    Arena,
    /// <summary>A byte buffer for <c>formatInto</c>-style output: a span plus a fill length.</summary>
    StrBuf,
    /// <summary>The static type <c>Io</c>; it has no values.</summary>
    Io,
    /// <summary>The static codec <c>Hex</c>; it has no values.</summary>
    Hex,
    /// <summary>The static codec <c>Utf8</c>; it has no values.</summary>
    Utf8,
    /// <summary><c>Io.stdout</c> or <c>Io.stderr</c>; the value is the file descriptor.</summary>
    IoStream,
    /// <summary><c>Io.stdin</c>; it has no state.</summary>
    IoInput,
    /// <summary>What <c>Io.run</c> returns: <c>exitCode</c>, <c>stdout</c>, <c>stderr</c>.</summary>
    ProcessResult,
    /// <summary>The panic a <c>defer panicking</c> handler or <c>Panic.onPanic</c> receives: <c>message</c>.</summary>
    Panic,
    /// <summary>The specs of a hole, as <c>formatInto(ref StrBuf, as FormatSpec)</c> receives them: <c>radix</c>, <c>debug</c>, <c>width</c>, <c>fill</c>, <c>align</c>, <c>digits</c>.</summary>
    FormatSpec,
    /// <summary>A 4-byte interned identifier: copyable, compared by ID, ordered by its text (spec §3.3).</summary>
    Symbol,
    /// <summary>The static path functions <c>Path</c> (spec 014's slice); it has no values.</summary>
    Path,
    /// <summary>The compile-time constants <c>Target.os</c> and <c>Target.arch</c>; it has no values.</summary>
    Target,
}

public sealed class BuiltinType : TypeSymbol
{
    private BuiltinType(BuiltinKind kind, string name)
    {
        Kind = kind;
        Name = name;
    }

    public BuiltinKind Kind { get; }

    public string Name { get; }

    public override string DisplayName => Name;

    public override bool IsBorrow => Kind == BuiltinKind.StrBuf;

    public static readonly BuiltinType Arena = new(BuiltinKind.Arena, "Arena");
    public static readonly BuiltinType StrBuf = new(BuiltinKind.StrBuf, "StrBuf");
    public static readonly BuiltinType Io = new(BuiltinKind.Io, "Io");
    public static readonly BuiltinType Hex = new(BuiltinKind.Hex, "Hex");
    public static readonly BuiltinType Utf8 = new(BuiltinKind.Utf8, "Utf8");
    public static readonly BuiltinType IoStream = new(BuiltinKind.IoStream, "IoStream");
    public static readonly BuiltinType IoInput = new(BuiltinKind.IoInput, "IoInput");
    public static readonly BuiltinType ProcessResult = new(BuiltinKind.ProcessResult, "ProcessResult");
    public static readonly BuiltinType Panic = new(BuiltinKind.Panic, "Panic");
    public static readonly BuiltinType FormatSpec = new(BuiltinKind.FormatSpec, "FormatSpec");
    public static readonly BuiltinType Symbol = new(BuiltinKind.Symbol, "Symbol");
    public static readonly BuiltinType Path = new(BuiltinKind.Path, "Path");
    public static readonly BuiltinType Target = new(BuiltinKind.Target, "Target");
}

/// <summary>The type of a caught error when the call can fail with more than one set.</summary>
public sealed class ErrorUnionType(IReadOnlyList<ErrorSetSymbol> sets, IReadOnlyList<ErrorVariantSymbol>? excluded = null) : ConstructedType
{
    public IReadOnlyList<ErrorSetSymbol> Sets { get; } = sets;

    /// <summary>The cases taken out with <c>except .c</c>: never raised by a value of this type.</summary>
    public IReadOnlyList<ErrorVariantSymbol> Excluded { get; } = excluded ?? [];

    // By symbol identity: `ValidationError<a.Point>` and `ValidationError<b.Point>` display alike but are different sets.
    protected override object Key => new SetsKey(Sets, Excluded);

    private sealed class SetsKey(IReadOnlyList<ErrorSetSymbol> sets, IReadOnlyList<ErrorVariantSymbol> excluded)
    {
        public IReadOnlyList<ErrorSetSymbol> Sets { get; } = sets;

        public IReadOnlyList<ErrorVariantSymbol> Excluded { get; } = excluded;

        public override bool Equals(object? obj) => obj is SetsKey k && Sets.SequenceEqual(k.Sets) && Excluded.SequenceEqual(k.Excluded);

        public override int GetHashCode() => Sets.Aggregate(Excluded.Count, (h, s) => HashCode.Combine(h, s));
    }

    /// <summary>With no sets, any error (an inferred set not yet known).</summary>
    public override string DisplayName => Sets.Count == 0 ? "error" : string.Join(" | ", Sets.Select(s => s.DisplayNameAmong(Sets)))
        + (Excluded.Count == 0 ? "" : " except " + string.Join(", ", Excluded.Select(v => "." + v.Name)));

    public static TypeSymbol Of(IReadOnlyList<ErrorSetSymbol> sets, IReadOnlyList<ErrorVariantSymbol>? excluded = null) =>
        sets.Count == 1 && excluded is not { Count: > 0 } ? sets[0] : new ErrorUnionType(sets.ToArray(), excluded?.ToArray());
}
