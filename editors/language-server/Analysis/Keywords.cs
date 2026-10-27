using System.Collections.Frozen;

namespace Bayan.LanguageServer.Analysis;

/// <summary>
/// Keyword list and hover docs. Source of truth is the compiler's Keywords.cs;
/// HANDOFF §3 replaces this table with a reference to it.
/// </summary>
public static class Keywords
{
    public static readonly FrozenDictionary<string, string> Docs = new Dictionary<string, string>
    {
        ["struct"] = "Declares a value type. `struct unique Name` makes it move-only.",
        ["union"] = "Declares a tagged union. `union error Name` declares an error union; one `case` per line.",
        ["error"] = "`union error Name { … }` declares the error cases a function can `fail` with.",
        ["type"] = "`type Name = T` is an alias; `type distinct Name = T requires …` is a new type with rules.",
        ["distinct"] = "`type distinct Name = T` creates a new type that does not mix with `T`.",
        ["unique"] = "`struct unique Name` is move-only: values are moved, never copied. Cannot be `const`.",
        ["interface"] = "Declares an interface. Properties are bare `Type name`; static members may use `Self`.",
        ["effect"] = "`effect db = net, fs` declares an effect alias for `uses` clauses.",
        ["states"] = "Typestate block inside a struct: one state per line. Refer to a state as `.s` or `Type.s`.",
        ["let"] = "Immutable local binding, name first: `let w: Grams = 0g`.",
        ["var"] = "Mutable local binding, name first: `var w: Grams = 0g`.",
        ["const"] = "Compile-time constant: `const name = …`.",
        ["pattern"] = "`const name = pattern T { … }` names a reusable pattern.",
        ["if"] = "Conditional. No parentheses; braces required. Postfix form: `stmt if cond`.",
        ["unless"] = "Negated conditional: `unless cond { … }` or postfix `stmt unless cond`.",
        ["else"] = "Alternative branch; written after `}` on the same line.",
        ["while"] = "Loop while a condition holds. `while let x = …` loops while a value is present.",
        ["for"] = "`for x in xs { … }` iterates any type with `moveNext`/`current`. `for try x in xs` for fallible sequences.",
        ["in"] = "Separates the loop variable from the sequence in `for x in xs`.",
        ["switch"] = "Pattern switch; exhaustive over unions and states.",
        ["case"] = "A union case, or a `switch` arm.",
        ["return"] = "Returns from the function.",
        ["fail"] = "Fails with an error case: `fail .timeout`. Also postfix (`fail .x if c`) and after `??`.",
        ["fails"] = "Clause listing the error unions a function can fail with: `fails IoError, ParseError`.",
        ["try"] = "Propagates a failure to the caller. `try … catch` handles it.",
        ["catch"] = "Handles failures: `catch e { … }` or `catch switch { case .x -> … }`.",
        ["except"] = "Exclusion (istithnāʾ): `for x in xs except null`, `union error A = B except .c`.",
        ["with"] = "Copy with changed fields: `order with { quantity: 2 }`. Re-checks the type's rules.",
        ["provide"] = "Effect-handler block: `provide log: handler { … }` redirects an effect inside the block.",
        ["uses"] = "Effect clause: `uses alloc, net, fs, clock, random, env, process, console, suspend`.",
        ["where"] = "Generic constraints clause. Clause order: `where`, `expects`, `becomes`, `fails`, `uses`.",
        ["expects"] = "Precondition checked at function entry in every build mode; traps on failure.",
        ["requires"] = "Rule on a field, struct or distinct type, checked at construction and `with`.",
        ["becomes"] = "Clause: the method moves `this` to a state, `becomes .placed`. Every returning path must `become`.",
        ["become"] = "Transitions `this` to a state: `become .placed`.",
        ["because"] = "Purpose clause, required on `@allow`, `@json(expose)`, `…Unchecked()` and `unsafe`.",
        ["defer"] = "Runs at scope exit. `defer failing` runs only on failure, `defer panicking` only on panic.",
        ["failing"] = "`defer failing stmt` runs `stmt` only when the scope exits by failure.",
        ["panicking"] = "`defer panicking stmt` runs `stmt` only when the scope exits by panic.",
        ["is"] = "Type, pattern or state test: `x is not null`, `order is .paid`.",
        ["as"] = "Particle (`as: .hex`) and pattern binding.",
        ["not"] = "Boolean or pattern negation (same as `!` in expressions).",
        ["and"] = "Boolean or pattern conjunction (same as `&&` in expressions).",
        ["or"] = "Boolean or pattern disjunction (same as `||` in expressions).",
        ["move"] = "Transfers ownership: `move x` at a call site, `move this` as a consuming receiver.",
        ["ref"] = "Passes by reference. `ref this` marks a method that changes `this`.",
        ["rest"] = "Variadic parameter: `rest ReadOnlySpan<T> items`.",
        ["this"] = "The receiver. `ref this` mutates; `this is .s` requires a state; `move this` consumes.",
        ["Self"] = "The implementing type inside an interface.",
        ["it"] = "Implicit single parameter in lambdas and rules: `xs.where(it > 0)`.",
        ["checked"] = "`checked(expr)` returns a failure instead of trapping on overflow.",
        ["extern"] = "Declares a foreign (C) function or type. Pointers are allowed only here.",
        ["unsafe"] = "Unsafe block; needs `because \"…\"`.",
        ["test"] = "Test declaration: `test \"name\" { … }`, lives beside the code.",
        ["bench"] = "Benchmark declaration.",
        ["law"] = "Opt-in law declaration checked by the toolchain.",
        ["import"] = "Imports a module (`core` needs no import).",
        ["module"] = "Module declaration.",
        ["public"] = "Visible outside the module (default visibility is module).",
        ["private"] = "Visible only inside the declaring type.",
        ["internal"] = "Visible inside the package.",
        ["static"] = "Type-level member.",
        ["readonly"] = "Read-only storage or pointer target.",
        ["panic"] = "Aborts with a message. Handled only by `Panic.handler` set in `main`.",
        ["assert"] = "Power assert: on failure prints every sub-expression's value. `assert … fails` checks failures.",
        ["true"] = "Boolean true.",
        ["false"] = "Boolean false.",
        ["null"] = "No value; only for `T?`. Test with `is null` / `is not null`.",
        ["break"] = "Leaves the loop.",
        ["continue"] = "Skips to the next iteration.",
    }.ToFrozenDictionary(StringComparer.Ordinal);

    static readonly FrozenSet<string> Set = Docs.Keys.ToFrozenSet(StringComparer.Ordinal);
    static readonly FrozenSet<string>.AlternateLookup<ReadOnlySpan<char>> SpanSet = Set.GetAlternateLookup<ReadOnlySpan<char>>();

    public static bool IsKeyword(ReadOnlySpan<char> word) => SpanSet.Contains(word);

    public static readonly FrozenSet<string> Primitives = new[]
    {
        "void", "bool", "rune", "string", "unit", "never",
        "i8", "i16", "i32", "i64", "i128", "u8", "u16", "u32", "u64", "u128", "isize", "usize", "f32", "f64",
        "sbyte", "byte", "short", "ushort", "int", "uint", "long", "ulong", "nint", "nuint", "float", "double",
    }.ToFrozenSet(StringComparer.Ordinal);

    static readonly FrozenSet<string>.AlternateLookup<ReadOnlySpan<char>> PrimitiveSpans = Primitives.GetAlternateLookup<ReadOnlySpan<char>>();
    public static bool IsPrimitive(ReadOnlySpan<char> word) => PrimitiveSpans.Contains(word);

    /// <summary>Modifiers skipped when reading a declaration header.</summary>
    public static bool IsModifier(ReadOnlySpan<char> w) =>
        w is "public" or "private" or "internal" or "static" or "readonly" or "extern" or "unsafe";
}

/// <summary>Built-in members and functions shown in completion and hover until the binder provides them.</summary>
public static class Builtins
{
    public readonly record struct Member(string Name, string Signature, string Doc, bool IsMethod);

    public static readonly Member[] Functions =
    [
        new("print", "void print(string text, to: Stream = .stdout) uses console", "Writes a line to stdout, or `to: .stderr`.", true),
        new("log", "void log(string message)", "Non-allocating log sink; writes `level: message` to stderr. Redirect with `provide log:`.", true),
        new("panic", "never panic(string message)", "Aborts the program with a message.", true),
        new("min", "T min(T a, T b)", "Smaller of two values.", true),
        new("max", "T max(T a, T b)", "Larger of two values.", true),
        new("string", "string string(T value, as: Format = .decimal)", "Formats a value through `Display.formatInto`. Never decodes bytes; use `Utf8.decode`.", true),
    ];

    public static readonly Member[] StringMembers =
    [
        new("length", "i64 length", "Length in UTF-8 bytes.", false),
        new("isEmpty", "bool isEmpty", "True when `length` is 0.", false),
        new("isAscii", "bool isAscii", "True when every byte is ASCII.", false),
        new("runeCount", "i64 runeCount", "Number of Unicode scalar values (O(n)).", false),
        new("bytes", "ReadOnlySpan<u8> bytes", "The UTF-8 bytes.", false),
        new("runes", "Runes runes", "Iterates Unicode scalar values.", false),
        new("utf16", "Utf16View utf16", "UTF-16 view; `.copy()` to own it.", false),
        new("lines", "Lines lines", "Iterates lines without allocating.", false),
        new("parts", "Parts parts(string separator)", "Iterates the parts between separators.", true),
        new("position", "i64? position(string value)", "Byte position of the first match, or null.", true),
        new("lastPosition", "i64? lastPosition(string value)", "Byte position of the last match, or null.", true),
        new("count", "i64 count(string value)", "Number of non-overlapping matches.", true),
        new("contains", "bool contains(string value)", "Assertion verb: true when `value` occurs.", true),
        new("startsWith", "bool startsWith(string value)", "Assertion verb: true when the string begins with `value`.", true),
        new("endsWith", "bool endsWith(string value)", "Assertion verb: true when the string ends with `value`.", true),
        new("equals", "bool equals(string other, as: Comparison = .ordinal)", "Ordinal by default; `as: .caseless` is ASCII-only in v0.1.", true),
        new("trimmed", "string trimmed()", "Borrowed view without leading/trailing whitespace.", true),
        new("lowercased", "string lowercased() uses alloc", "ASCII lowercase copy (culture-aware forms live in `kit.intl`).", true),
        new("uppercased", "string uppercased() uses alloc", "ASCII uppercase copy.", true),
        new("replaced", "string replaced(string value, by: string) uses alloc", "Copy with every literal match replaced.", true),
        new("repeated", "string repeated(i64 times) uses alloc", "Copy repeated `times` times.", true),
        new("copy", "string copy(to: Allocator = alloc) uses alloc", "Owned copy of a borrowed string.", true),
    ];

    public static readonly Member[] CollectionMembers =
    [
        new("length", "i64 length", "Number of elements.", false),
        new("isEmpty", "bool isEmpty", "True when there are no elements.", false),
        new("get", "T get(at i64 i)", "Element at `i`; traps when out of range. Behind `xs[i]`.", true),
        new("tryGet", "T? tryGet(at i64 i)", "Element at `i`, or null when out of range.", true),
        new("append", "void append(T item)", "Adds `item` at the end.", true),
        new("prepend", "void prepend(T item)", "Adds `item` at the start.", true),
        new("add", "bool add(T item)", "Sets: adds `item`; false when already present.", true),
        new("remove", "void remove(at i64 i)", "Removes the element at `i` (or `from:until:` / `from:through:`).", true),
        new("replace", "void replace(at i64 i, by T item)", "Replaces the element at `i`.", true),
        new("sum", "T sum()", "Sum of the elements.", true),
        new("product", "T product()", "Product of the elements.", true),
        new("average", "T average()", "Mean of the elements.", true),
        new("min", "T min()", "Smallest element (`min(by:)` for a key).", true),
        new("max", "T max()", "Largest element.", true),
    ];
}
