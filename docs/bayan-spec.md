# Bayan — Language Specification (draft 0.1)

> **Status:** design complete enough to build v0.1. This document is normative unless a section says otherwise.
> **Source files:** `.byn` · **CLI:** `bayan` · **Manifest:** `package.byn` · **Build script:** `build.byn`
> Code samples use C#-style highlighting because Bayan's surface syntax is C#-derived.

Bayan (بيان, "clarity, eloquent exposition") is a value-oriented, natively compiled language derived from C#. It has no runtime, no garbage collector (v1), and no exceptions. A zero-allocation subset of C# is valid Bayan.

---

## 0. Principles

1. **Performance is a feature.** No hidden costs: allocation, suspension and failure are always visible in the source or the tooling.
2. **Every keystroke carries meaning.** If the compiler can infer it reliably, don't type it; if it carries meaning, type it.
3. **Brief but complete.** Few forms, each with one meaning (the "keyword budget": one form, one meaning).
4. **Predictable.** Roots plus a closed set of patterns (ishtiqāq, §5).
5. **Elegant by default.** One formatter, one canonical form. Style defaults (`docs/bayan-style.md`) are configurable per project in `package.byn` (§19.2); grammar rules (§5) are not style and cannot be configured away.

> Round 2 §18: style is guidance applied by `bayan fmt`, configurable per project; "no options" contradicted it.
6. **Kind to the programmer.** Errors explain what, where, why and how to fix (§21).
7. **Instant feedback.** REPL, scripts and incremental builds under 100 ms.
8. **Joy is a requirement.**
9. **Memory safety is never a build-mode option.** Bounds checks and region checks apply in every build.
10. **Promise as little as possible.** Anything specified exactly is permanent; leave room where it costs nothing (§19.4).

---

## 1. The core on one page

| Area | Rule |
|---|---|
| Data | Structs only (value types). No classes, no inheritance, no `object`, no GC in v1. |
| Strings | `string` is an immutable, validated UTF-8 slice. `char` is a 32-bit Unicode scalar. |
| Errors | `T f() fails E` in signatures; `Result<T, E>` as a type; `try` / `catch`; panics abort. |
| Bindings | `let` (immutable, default), `var` (mutable), `const` (compile-time). Parameters are immutable. |
| Parameters | Read-only by default (compiler chooses copy or reference); `ref` mutable borrow; `move` ownership transfer. |
| Memory | Stack, arenas, `Owned<T>`, `.rodata`. Allocators flow through `context`. Borrows are region-checked. |
| Naming | Types `PascalCase`; everything else `camelCase`. Casing encodes part of speech (§5). |
| Derivation | A closed set of patterns derives forms (`tryX`, `xInto`, `xOp`, `T.Builder`, …). |
| Generics | C# syntax, constraints checked at definition, monomorphized in release. |
| Async | Colorless: no `async` on declarations. `async`/`await` at call sites; I/O through `context.Io`. |
| Comptime | Ordinary Bayan run at compile time by the interpreter; deterministic and sandboxed. |
| Modules | A directory is a module. `import`. No cycles. Default visibility: module. |
| Safety | No undefined behavior outside `unsafe`. Taint tracking, `Secret<T>`, capability declarations. |
| Output | Native executables and libraries via C; WASM via clang `wasm32`. |

Hello world:

```csharp
print("Hello, world!");
```

---

## 2. Lexical structure

### 2.1 Source text
- UTF-8, NFC-normalized identifiers. Arabic and other scripts are allowed in identifiers.
- **Bidi control characters (U+202A–U+202E, U+2066–U+2069) are errors outside string literals** (Trojan Source, CVE-2021-42574).
- Lint (off by default): identifiers mixing scripts (homoglyphs such as Cyrillic `а` in `pаypal`).

### 2.2 Comments
- `//` line, `/* */` block.
- `///` doc comments are **Markdown**. The first line is the summary (used by `$"{e}"` on errors and by the prose view). Fenced code blocks in doc comments run as tests.

### 2.3 Literals

| Kind | Examples | Notes |
|---|---|---|
| Integer | `42`, `1_000_000`, `0xFF`, `0b1010` | **Untyped** until context gives a type; out-of-range is a compile error |
| Float | `3.14`, `1e5`, `2.5e-3` | `e` followed by a digit or sign is an exponent |
| Unit-suffixed | `5s`, `250ms`, `4KiB`, `100m` | Suffix must be an imported unit symbol; not allowed on hex/binary literals |
| Built-in suffixes | `u`, `l`, `ul`, `f` | Reserved. C#'s `m`/`d` suffixes are retired; decimal is `1.50dec` |
| Char | `'a'`, `'\n'`, `'\u{1F600}'` | 32-bit scalar; ASCII char literals narrow to `u8` in comparisons |
| String | `"text"`, `"""raw multi-line"""` | Stored in `.rodata` |
| Interpolated | `$"total {x}"` | Interpolation handler writes into a buffer; no hidden allocation |
| C string | `c"text"` | NUL-terminated, in `.rodata` |
| UTF-16 | `u16"text"` | For Windows `…W` APIs |
| Collection | `[1, 2, 3]`, `[]`, `[..a, ..b, 4]` | Target-typed (§6.3); with no target type, `[1, 2, 3]` is `i32[3]` on the stack |
| Map | `["alice": 500]`, `[]` | Target-typed; with no target type, `["k": "v"]` is `Map<string, string>` |
| Struct | `{ x: 1, y: 2 }` | Target-typed; only where a type is expected |
| Tuple | `(1, "a")`, `(x: 1, y: 2)` | |

> Round 2 §10, §11: field initialization uses `:`; an empty map is `[]` like every empty collection; untyped literals get a default type. Codes: BYN1021, BYN2235, BYN2236.

An integer literal (or a negated one) that initializes an unannotated `var` takes the type of the variable's first constraining use in the same function, in bind order: an assignment to or from a typed value, a comparison or arithmetic with one, or an argument for a typed parameter. `var i = 0; while i < xs.length { i += 1; }` makes `i` an `i64`. Literals and other such `var`s do not constrain, so `var n = 0; n += 1;` leaves `n` an `i32`. Only an integer type retypes the variable: when the first use is a float or a string, or there is none, it keeps the literal's default type (`i32` when it fits). A literal that does not fit the inferred type is the usual out-of-range error at the literal (`var n = 300; u8 b = n;` is BYN2203 on `300`). `bayan types` shows the resolved types (`docs/typed-dump.md`).

### 2.4 Keywords

**Declarations:** `struct unique union enum open extensible interface extension derivation attribute error set unit distinct type law import as public private static readonly required const let var embed extern unsafe`

The declaration-only keywords `error unit distinct open extensible set type law derivation attribute embed` are **contextual**: they act as keywords only where a declaration form needs them (`error enum E { … }`, `type X = …;`) and are names everywhere else (locals, parameters, fields, functions, members, error variants, argument names): `let error = 3;` and `struct S { i64 set; }` are valid. Every other keyword, including all control-flow and expression keywords, is reserved and cannot be an identifier.

**Control:** `if else unless switch case default for in while do break continue return yield defer using context`

**Errors & effects:** `fails try catch never requires noalloc nosuspend suspends`

**Concurrency:** `async await`

**Expressions:** `is and or not with move ref out this null true false comptime checked`

**Tests:** `test property fuzz model assert`

**Contextual words** (keywords only in one position, names everywhere else): `failing`, `panicking` after `defer` (§7.6); `unlikely`, `never` after `if` (§7.2); `it` inside a lambda argument (§3.16); `bench`, `measure` in benchmarks (§20).

**Reserved (no meaning yet; may not be used as identifiers):** `actor effect macro select become ensures Gc gc` (`ensures`: v1.x contracts, handoff addendum 002; `Gc`, `gc`: the future GC tier, §9.8; `law`, for v0.2 laws, left this list for the contextual declaration keywords)

`flags`, `errdefer` and `delegate` are not keywords. `foreach`, `errdefer` and `delegate` in source get a teaching error with a fix (§21.5). `new` is accepted and removed by the formatter. `goto` does not exist.

> Round 2 §1, §6, §7, §8, §16, carried over (GC): `flags`, `errdefer` and `delegate` are gone; `requires` and `assert` are keywords; the new statement words are contextual; `Gc`/`gc` reserved now. Codes: BYN0012, BYN1019, BYN1020, BYN1028.

---

## 3. Types

### 3.1 Primitives

| Canonical | C# name (input form, §21.5) | Notes |
|---|---|---|
| `i8 i16 i32 i64` | `sbyte short int long` | |
| `u8 u16 u32 u64` | `byte ushort uint ulong` | |
| `isize usize` | `nint nuint` | |
| `f32 f64` | `float double` | |
| `bool` | | |
| `char` | | **32-bit Unicode scalar** (differs from C#) |
| `string` | | UTF-8 slice, 16 bytes (pointer, length) |
| `never` | | Bottom type; see §3.9 |
| `dec` | `decimal` | Decimal arithmetic type |

> Round 2 §15: C# type names are input forms, rewritten by the editor and `bayan fmt` and rejected in source. Codes: BYN1030.

Lowercase type names are reserved for the language. C interop types: `c_int`, `c_long`, `c_char`, … (`c_long` follows the platform).

### 3.2 Strings
- Always **valid UTF-8**, validated on construction (`string.tryFrom(bytes)` returns `string?`; `string.from(bytes) fails Utf8Error`).
  - Both take a `ReadOnlySpan<u8>` and return a fresh copy from `context.allocator` that does not borrow the bytes. An empty span decodes to `""`. Overlong forms, surrogates, code points above U+10FFFF and truncated sequences are invalid (law L010).
  - `Utf8Error` is a built-in `error enum` with one variant, `invalidSequence`. Until error variants carry payloads, `string.invalidUtf8At(bytes)` returns the byte offset of the first invalid sequence as `i64?`, `null` when the bytes are valid (`[0x68, 0xC0, 0x80]` gives 1).
- `n.toHex()` on every integer type returns lowercase hexadecimal with no prefix and no leading zeros (`255` gives `"ff"`, `0` gives `"0"`), allocated from `context.allocator`. A negative value prints its two's-complement bits at the type's width (an `i8` holding `-1` gives `"ff"`). Format specifiers are in §6.6.
- **`s.length` is the O(1) byte length** (round 3 keeps length). Other units are named: `s.chars.count`, `s.graphemes.count` (O(n)).
- **No single-index indexer.** `s[i]` is a compile error with fix-its: `s.bytes[i]`, `s.chars.take(n)`, `s.graphemes.take(n)`, range slicing.
- All offsets are **byte offsets**, as ranges are everywhere (§6.1). `s[a..b]` slices by byte offset and panics off a character boundary. `s.chars.take(n)`, `s.graphemes.take(n)`, `skip`, `takeLast` count characters or graphemes.
- **Searching** returns offsets or views: `contains`, `startsWith`, `endsWith`, `indexOf` (`i32?`), `lastIndexOf`.
- **Splitting** is lazy and returns views: `split(',')`, `split(", ")`, `split(predicate)`, `split(",", max: 2)`, `splitOnce('=')` → `(string, string)?`, `lines()`.
- **New strings** (Form I, allocated from `context.allocator`): `trim()`, `trimStart()`, `trimEnd(c)`, `lowercase()`, `uppercase()`, `replace(a, b)`, `padStart(n, c)`, `repeat(n)`.
- **No `+` on strings**, constants included. Concatenate with interpolation (`$"{a}{b}"`), `[a, b, c].join("")`, `Path.join`, or `StrBuf` in loops. `a + b` on strings is an input form rewritten to interpolation (§21.5).
- `==` is **ordinal**; `a.equals(b, .ignoreCase)`; culture-aware comparison only through `Collator`.
- `string.fromUtf8(bytes)` fails on invalid UTF-8; `toUtf16()` for interop. `string16` exists for UTF-16 interop.
- Raw strings: `"""…"""`.
- Not in v0.1: `graphemes`, `Collator`, `split` views, `trim*`, `padStart`, `repeat`, `fromUtf8`, `toUtf16`.

> Round 2 §12: one size name per unit instead of an ambiguous `length`; `+` on strings hid allocation. Codes: BYN2201, BYN2209, BYN2230.

### 3.3 Symbols
`Symbol` is an interned identifier (4-byte ID). Equality and hashing use the ID; **ordering uses the text** (UTF-8 bytes = code-point order). String literals convert to `Symbol` at compile time (interned table in `.rodata`).

### 3.4 Units and distinct types

```csharp
unit Seconds(s) : i64;           // name, literal symbol, storage
unit Millis(ms) = Seconds / 1000;
unit Bytes(B)   : i64;
unit KiB        = 1024 B;
unit Cents      : i64;           // no symbol
unit Speed      = Meters / Seconds;

distinct type UserId = i64;      // a unit without arithmetic
```

- The unit name is the type; the symbol is the literal suffix.
- Mismatched units are compile errors. Scaled units convert automatically (`2s + 500ms` is `Millis 2500`).
- Wrap and unwrap with casts: `(Seconds)raw`, `(i64)t`. A bare literal takes the unit of the other operand.
- Units are erased at runtime.
- `s.length` is `Bytes`; `s.chars.count` is `Runes`.

> Round 2 §12 removed `length` from strings; round 3 restores it (`s.length` is the byte length).

### 3.5 Arrays, spans, collections
- `T[N]`: fixed-size inline array (a value). Constant out-of-range indexes are compile errors.
- `Span<T>`: a borrowed view (pointer, length) whose elements may be assigned.
- `ReadOnlySpan<T>`: a borrowed view whose elements cannot be assigned. `Span<T>` → `ReadOnlySpan<T>` is implicit. `string.bytes` is a `ReadOnlySpan<u8>`, so strings stay immutable and valid UTF-8. A `Span<T>` parameter never written through gets lint BYN2310 ("use `ReadOnlySpan<T>`").
- `List<T>`, `Deque<T>`, `Map<K, V>`, `HashMap<K, V>`, `Set<T>`, `Pool<T>`, `Ring<T, const i32 N>`, `NonEmpty<T>`: standard library, allocator-backed (API: §16).
- `Map` **preserves insertion order**; `HashMap` is unordered and faster.
- `xs.removeRange(start, end)` removes the elements in `[start, end)` and keeps the order of the rest; `start > end` or `end > xs.length` panics (`slice out of range`). Like `append` and `removeLast`, it is a structural modification (§9.4 rule 5).
- A module-level `const T[N]` with literal or constant elements is static data in `.rodata` when `T` is a number type, `bool`, `char` or `string` (`const string[2] names = ["a", "b"];`); it can be indexed, iterated and asked its `.length`. Any other module-level `const` array is BYN2229.
- **`StrBuf`** is the string builder and the target of every `xInto(ref StrBuf)` (§5.2): `StrBuf(capacity: n)` allocates from `context.allocator`; `StrBuf.over(stackalloc u8[n] uninit)` uses the stack first and spills to the allocator only if outgrown; `append(x)`, `append($"…")` (interpolation writes straight into the buffer), `appendRepeated(c, n)`; `buf.view` is a borrowed string view, no allocation; `buf.finish()` consumes the buffer and returns it as a string without copying. `StrBuf.over` is not in v0.1.

> Round 2 §11, §12, §15: `append` replaces `add` on ordered collections (round 3 keeps `length` for size); `finish()` and `view` replace `asString()`; C# type names are input forms. Codes: BYN1030, BYN2230.
- Every allocator-backed collection (`List`, `Map`, `HashMap`, `Set`, `Pool`) and `StrBuf` is **`unique`** (§3.11): assigning or storing one moves it, and a copy is explicit, `xs.copy(to: alloc)`. Reading without owning goes through `ReadOnlySpan<T>`, a read-only parameter or `ref`.
- Hashing is **seeded** by default (hash-flooding resistance); tests use a fixed seed.

### 3.6 Optionals
`T?` for any type. References are non-null; `null` is the literal for "none" of an optional. `??`, `?.`, `is null` / `is not null` (with narrowing, §7.2) and `?? return/break/continue` apply. A call's non-null result is bound with `let order = db.find(id) ?? return .notFound;`, not `is { } order`.

> Round 2 §4: one null-check spelling; `x == null` is an input form. Codes: BYN2027.

### 3.7 Tuples
C# value tuples with named elements and deconstruction: `(Cents share, i32 extra)`.

> Round 2 §15: C# type names are input forms. Codes: BYN1030.

### 3.8 Results
`Result<T, E>` is a standard-library union (§3.12): a tag plus the larger of the value and the error payload, inline. `fails` desugars onto `Result`. Every error variant in a program has a unique compile-time number, so merging error sets is free.

> Round 2 §1: `Result` and `never` are stdlib unions (not in v0.1).

### 3.9 `never`
The empty union in the standard library. Returned by `panic`, `fatal`, `exit` and infinite loops. A `never` expression fits any position (`catch`, `??`, switch arms); code after it is reported unreachable. `fatal` and `exit` are linted as errors in packages that are not `kind: .app`.

> Round 2 §1, §10: `never` is the empty union; manifest fields use `:`.

### 3.10 Structs

```csharp
/// An order placed by a buyer.
[Derive(Id, Builder, Patch)]
public struct Order : IEquatable<Order>, IJsonEncode {
    public required BuyerId buyer;
    public OrderStatus status = .submitted;
    List<OrderItem> items;                        // module-visible

    public Money total => items.sumBy(it.price * it.units);   // properties may not allocate or suspend
    public bool isEmpty => items.isEmpty;

    public Order(BuyerId buyer) { this.buyer = buyer; items = List<OrderItem>(); }

    public void ship(ref this) fails OrderError {
        return .alreadyShipped if status is .shipped;
        return .empty if isEmpty;
        status = .shipped;
    }
}
```

- Fields, properties, constructors, methods, operators, indexers, static members.
- **Members are read-only by default.** A member that modifies `this` declares `ref this` as its first parameter (Form V, §5.2); see §10 for the call-site rules.
- `required` fields must be set at construction. Definite assignment as in C#.
- A type is **defaultable** only if every field has a default and the type is not `[NoDefault]`.
- Equality, hashing and debug formatting are derived automatically for every struct whose fields support them (override with `[Equality(by: .id)]`; opt out of `==` with `[NoEquality]`, §6.7).
- Structs have **no default `format()`**; see §6.6.

> Round 2 §9, §2, §13: mutation is declared with `ref this`, not inferred; `[NoEquality]` is the opt-out; display is never implicit. Codes: BYN2115, BYN2117.
- `embed T;` promotes the embedded struct's fields and methods; the embedded value sits at offset 0.
- No implementation inheritance (revisit only if a GC tier is added).

### 3.11 Move-only types
`unique struct` cannot be copied; use `move`. Cleanup is derived (disposes fields in reverse declaration order) and runs exactly once, at scope end or by the final owner.

- **A type with a `unique` part is `unique`.** A struct with a `unique` field, a union with a `unique` payload, and a tuple, array or optional of a `unique` type are all `unique`, without being declared so. A struct holding a `List` is therefore move-only.
- An implicit copy of a `unique` value is a compile error whose fixes are `move x`, `x.copy(to: alloc)` (when the type has one) or passing it by `ref`.

### 3.12 Unions

```csharp
union Shape { circle(f32 r), rect(f32 w, f32 h) }
union PaymentResult { approved(string transactionId), declined, pending }
union OrderStatus { pending, paid; bool isFinal => this is .paid; }
union Mode { read = 1, write = 2 }
```

- `union` and `error union` share one grammar; `error` only allows use in `fails` clauses and `catch switch` (§8).
- Case names are **camelCase**. A payload-free union is the enum form; cases may carry data.
- Members may follow the cases after a `;` (not in v0.1).
- **Closed:** no integer casts. Outside data enters only through `T.tryParse(text)`, which fails on unknown input (not in v0.1). `.name` gives the case name.
- Payload-free cases may declare explicit integer values for C interop (`read = 1`). A value must be an integer constant, only on a payload-free case, and unique in the union.
- `switch` on a union is exhaustive; a missing case is an error naming the case. A `_` arm on a union declared in the same package is a lint warning (it hides future cases).
- Layout: tag plus largest payload, inline, no heap, no boxing.
- `Result<T, E>` and `never` are standard-library unions (§3.8, §3.9).

> Round 2 §1: unions are the one closed sum type; camelCase cases match values; explicit values exist only for C interop. Codes: BYN2410, BYN2709, BYN2710, BYN2711, BYN2712.

### 3.13 Enums (Java-style)

```csharp
public enum Planet(f64 mass, f64 radius) {
    mercury(3.303e23, 2.4397e6),
    earth(5.976e24, 6.37814e6);
    public f64 surfaceGravity => G * mass / (radius * radius);
}
```

- A value is a small integer tag; associated data lives in `.rodata` tables.
- Constant-specific bodies compile to jump tables.
- Generated: `count`, `values`, `name`, `parse` (perfect hash), `fromOrdinal` (returns `T?`), `ordinal`.
- `EnumMap<T, V>` (inline array).
- Associated data must be constant.
- `open enum` allows unknown values (C interop only). Public enums and error sets are closed unless marked `extensible`, which forces callers to include a fallback case.
- No cast from integer to enum.
- **No flags construct.** `Set<T>` of a payload-free union with at most 64 cases is guaranteed to be a single bitmask. `Set` supports `|`, `&`, `-`, `contains`, `containsAll`, `.bits` (`u64`) and `T.tryFromBits(bits)` (fails on bits matching no case). Not in v0.1.

```csharp
union Permission { read, write, delete, admin }
Set<Permission> editor = [.read, .write];
return .forbidden unless user.grants.containsAll(editor);
```

> Round 2 §1: a bitmask `Set<T>` covers flags with one construct, so `flags enum` and `EnumSet<T>` are removed.

### 3.14 Errors
`error enum`, `error union` (variants with payloads), `error set Name = A, B, C;`. See §8.

> Round 2 §5: commas everywhere a list of errors appears. Codes: BYN1029.

### 3.15 Interfaces

```csharp
interface IShape {
    f32 area();
    string describe() => $"shape with area {area()}";   // default method
}
interface IParse<TSelf> where TSelf : IParse<TSelf> { static abstract TSelf parse(string s) fails ParseError; }
interface IEnumerator { type Item; bool moveNext(ref this); ref readonly Item current { get; } }  // associated type
```

- Interfaces declare mutating members with `ref this`, like structs (§3.10); implementations must match.

> Round 2 §9: `ref this` is part of an interface member's signature. Codes: BYN2117.

- Nominal, explicit conformance (`struct Circle : IShape`). Interface names take an `I` prefix.
- Used as constraints (static dispatch) by default; `dyn ref IShape` / `dyn IShape` for dynamic dispatch through a fat pointer (no allocation).
- Interfaces with generic methods or `static abstract` members cannot be used with `dyn` (checked, with explanation).
- Retroactive conformance (`extension Money : IJsonEncode`) only when the module owns the type or the interface.

### 3.16 Lambdas and function types

```csharp
users.where(it.isActive)
lines.sumBy(it.price * it.qty)
xs.select(x => x * 2)
pairs.select((a, b) => a + b)
type Predicate<T> = T => bool;
type OrderFilter = Order => bool;
```

- **Lambda forms:** `x => x * 2`, `(a, b) => a + b`, block bodies, and `it` for a one-parameter lambda (`select(toDto(it))`). `it` inside a lambda that already uses `it` is an error; name one lambda.
- `.member` has one meaning: a member of the **expected type** (`return .notFound`, `color: .red`). It is never lambda shorthand.
- **One function-type syntax** replaces `Func`/`Action`/`delegate`: `i32 => i32`, `(i32, i32) => i32`, `User => void`, `() => void`, `() => T fails DbError`. Aliases use `type`. `delegate` is a teaching error pointing to a function type.
- Lambdas passed as parameters are monomorphized and inlined; captures borrow; zero allocation.
- A stored lambda is non-capturing (a function pointer) or an explicit `dyn (A => B)` allocated from `context.allocator`.
- `move () => …` moves captures in. Capturing a `var` follows the borrow rules (§9.4).
- No multicast delegates and no `event`.
- Not in v0.1 (spec 007).

> Round 2 §8: one function-type syntax and one shorthand (`it`); `.member` keeps a single meaning; storage cost is explicit. Codes: BYN1028, BYN2029.

---

## 4. Declarations and modules

### 4.1 Visibility

| Keyword | Visible to |
|---|---|
| `private` | The declaring type |
| *(none)* | The module (directory) |
| `public` | Other packages |

Lint: a type with `[NoDefault]` or a constructor-like pattern (`parse`, `validate`) whose fields are not `private` gets a warning.

### 4.2 Module-level declarations
- Functions may be declared at module level.
- Module-level `let` must be a compile-time constant.
- A mutable module-level `var` must be `Atomic<T>`, `Mutex<T>` or `[ThreadLocal]`.
- **No runtime static initializers.** Runtime setup uses `static let x = lazy load();` (thread-safe, runs once on first use).

### 4.3 Extensions

```csharp
extension string { public bool isBlank => trim().isEmpty; }
extension<T> Span<T> where T : INumber<T> { public T sum() { … } }
extension static Duration { public static Duration fromHours(f64 h) => (Duration)(h * 3600s); }
```

Resolved statically. Instance members win over extensions. Extensions cannot add fields.

### 4.4 Attributes
C# syntax `[Name(args)]`. User-defined attributes are `attribute struct Route { string path; }`. Arguments must be compile-time constants. Attributes are readable only at compile time.

---

## 5. Naming and morphology (ishtiqāq)

### 5.1 Casing
- **Types** (structs, unions, enums, interfaces, units, derived noun types): `PascalCase`.
- **Everything else** (methods, properties, fields, locals, parameters, enum members, union cases, error variants): `camelCase`.
- Acronyms: `HttpClient`, `IO` (two letters stay uppercase).
- Interface names: `I` prefix.

> Round 2 §1: union cases are camelCase.
- Enforced as errors in new code; ported C# compiles with warnings; `bayan migrate --rename` fixes casing.

### 5.2 Every type is a root; patterns are a closed set
Users may create roots, never patterns. Derived members are generated only when referenced. Pattern names are reserved: a hand-written member with a pattern name must satisfy the pattern's contract. The naming decision tree, the ḥāl rule for statement modifiers and the source of each rule are in `docs/bayan-grammar.md`.

**Verb patterns (members, `camelCase`):**

| Pattern | Arabic source | Meaning | Contract | Checked by |
|---|---|---|---|---|
| `x()` | Form I | Base act | Returns a value, may `fails` | |
| `x(ref this)` | Form V | Reflexive: mutates self | Named with a verb | BYN5101 (lint) |
| `xed()` | mafʿūl (participle) | Result of the act: **a view if possible, otherwise a copy** | Derived from a mutating `x(ref this)`; exists only as its copy form | BYN5001 |
| `tryX()` | Form X | Attempt | Returns `bool` or `T?`; derived from `x() fails` | BYN5003 |
| `ensureX()` | Form II | Causative, idempotent | Returns `void` or `fails` | |
| `xWith(T other)` | Form III | Act with another | Takes `T other` | |
| `xInto(ref dst)` | particle إلى | Write into a destination | Takes `ref StrBuf` or `Span<T>`; `x()` allocating form derived from it | |
| `xOp(…)` | maṣdar | The act as a value | Derived; type `Op<T, E>` | |
| `isX` / `hasX` / `canX` | ṣifa | State | `bool`, no arguments, no `ref this`; a `bool` state member takes one of these prefixes | BYN5002, BYN5102 (lint) |
| `xUnchecked()` | — | Skips validation | Requires a checked sibling `x()` | |

> Round 2 §9, §17, §19: `ref this` replaces mutation inference; each pattern contract names the checker rule that enforces it (errors `BYN50xx`, lints `BYN51xx`, which never block builds); derived `withF(v)` is removed because `x with { f: v }` is the one copy-with-changes form, so `with…` is no longer a reserved name. Codes: BYN5001, BYN5002, BYN5003, BYN5101, BYN5102.

**Composition order:** `[try] verb [Into] [Op]` — e.g. `tryFormatInto`, `parseOp`, `tryFormatIntoOp`.

**Noun patterns (types, `PascalCase`):**

| Pattern | Arabic source | Meaning |
|---|---|---|
| `T.Builder`, verb + `er` (`Json.Parser`) | fāʿil | Stateful doer |
| `T.Hasher`, `T.Comparer`, `T.Codec` | mifʿāl | Stateless instrument |
| `T.Validated` | mafʿūl | Proof an action happened (type-state); constructible only via `validate()` |
| `T.Field` | mafʿal | Enum of the type's fields |
| `T.<Verb>ed` (`Order.Shipped`) | faʿla | Event: one occurrence |
| `T.Id` | — | Identity (distinct type) |
| `T.Patch` | — | Partial changes |

### 5.3 Derivations
Open set, written in Bayan with `derivation Name for T { … }` (§12.5). Standard derivations: automatic equality/hash/debug, `Order`, `Id`, `Builder`, `Patch`, `Validate` (from `requires`, §7.9), `Events`, `Observable`, `Json`, `Csv`, `Row` (generates `readRow(ref Reader)`), `CopyIn` (generates binary COPY writers), `Binary` (CBOR), `Proto`, `Query`, `Config`, `Args`, `Gen`, `Diff`, `Ts`. Derivations only add members; they never change existing ones. The `derivation` mechanism is not in v0.1.

> Round 2 §7, §19: `requires` replaces `[Check]`; `CopyIn` joins the standard set for database row types.

---

## 6. Expressions

### 6.1 Operators
C# operators and precedence, plus:

| Operator | Meaning |
|---|---|
| `+% -% *%` | Wrapping arithmetic |
| `+\| -\| *\|` | Saturating arithmetic |
| `a..b` / `a..=b` | Exclusive / inclusive range (everywhere: loops, slices, patterns) |
| `^n` | Index from end |
| `x ?? return …` / `?? break` / `?? continue` | Early exit on none |
| `x catch …` / `catch switch { … }` / `catch (E.v) …` | Handle failure (§8) |
| `x in a..=b` | Boolean range test |

No user-defined operator symbols. Operator implementations must not allocate, suspend or fail (checked). Overloading and equality: §6.7.

> Round 2 §5, §7: `catch (e)` is gone; errors bind per arm of `catch switch`; `in` on a range is a boolean test. Codes: BYN1025.

### 6.2 Casts and conversions

| Conversion | Rule |
|---|---|
| Lossless numeric widening | Implicit |
| Narrowing `(u8)x` | Explicit; traps in every build mode if out of range |
| Truncation | `x.truncate<u8>()` |
| Bit reinterpretation | `bitcast<T>(x)` (same size; non-plain types require `unsafe`) |
| Units / distinct types | `(Seconds)raw`, `(i64)t` |
| Enum ↔ number | `.ordinal` / `.value`; `T.fromOrdinal(i)` returns `T?` |
| Views | `Span<T>` → `ReadOnlySpan<T>` implicit; as a call argument, `List<T>` and `T[N]` → `Span<T>` / `ReadOnlySpan<T>`, and `string` → `ReadOnlySpan<u8>`. Never `ReadOnlySpan<T>` → `Span<T>`. |
| User types | `(T)x` calls `IFrom<T, U>.from` (infallible). Fallible conversions use `ITryFrom` and `try T.from(x)` |
| `List<T>` → `Span<T>` | Implicit **only as a function argument**; otherwise `.asSpan` |
| User-defined implicit conversions | Not allowed |

There are no implicit user conversions; use `from` / `into`. `checked(expr)` evaluates a whole expression with overflow detection and returns `T?` (`null` on overflow). Unlike C#, where `checked` throws, it never traps. Float-to-integer conversion saturates (NaN becomes 0). Integer division by zero traps. `checked(expr)` is not in v0.1.

> Round 2 §2, §3: overflow traps in every mode (§17), so narrowing does too; `checked` returns `T?` and the C# difference is stated.

### 6.3 Target-typed literals
`[…]`, `["k": v]`, `{ f: v }` and `.member` take their type from context (variable type, parameter type, return type). If more than one overload accepts a target-typed literal, it is an error, never a silent pick. A ternary inside a collection literal must be parenthesized. `{` at the start of a statement is a block.

Field initialization uses `:` everywhere: `Money { cents: 250 }`, nested `unitPrice: { cents: 250 }` (type from the field), `with { lastName: "Doe" }`, named arguments `take: 10`. `{ cents = 250 }` is an input form (§21.5). There is no VB-style `with` block and no standalone `.field = value` statement.

> Round 2 §10: one separator for every field initialization, the same as named arguments. Codes: BYN1021.

### 6.4 Copy-and-update
`order with { status: .shipped }`. This is the only copy-with-changes form; there is no derived `withF(v)` (§5.2).

> Round 2 §10, §19: user decision 2026-10-21; two forms for one meaning broke "one form, one meaning". Codes: BYN1021.

### 6.5 Patterns
All C# patterns: constant, relational, `and`/`or`/`not`, property and extended property, positional, list patterns with slices (`[var first, .. var rest]`), `var`, `_`, `when` guards. A binding is `let x` (immutable) or `var x` (mutable), as for locals, and the lint that suggests `let` for a never-reassigned `var` (§7.1) applies to pattern bindings too. An arm whose whole pattern is irrefutable (`_`, `let x`, `var x`) covers every case, like `default:`; an arm with a `when` guard never counts toward exhaustiveness. Additions:

```csharp
x is 500..=599                          // range
path is "/api/v1/" + var rest           // string prefix / suffix / infix (zero-copy)
input is Email(var e)                   // active pattern from Email.tryParse
msg is .text(let body)                  // union destructuring
```

> Round 2 §1: union cases are camelCase.

`+` inside a pattern splits a string into a literal part and a binding; it is not concatenation. The round-2 ban (§3.2) covers `+` on string values only.

> Round 2 §12: string patterns stay; the `+` ban targets hidden allocation, and a pattern allocates nothing.

Type patterns apply only to `dyn` values and unions. `switch` must be exhaustive over unions, enums and `bool`.

### 6.6 Interpolation
`$"{x}"` uses an interpolation handler; formatting writes into the destination buffer. `$"{e}"` on an error prints the variant's doc-comment summary (fallback: its name; public errors without docs warn). `log.info($"user {user.id}")` captures `user.id` as a structured field.

**Floats** (`f64`, `f32`) format as the shortest digits that round-trip to the same value. Fixed notation when `1e-5 ≤ |x| < 1e15`, exponential outside it, spelled lowercase with no `+` and no leading zeros (`1e15`, `1.5e-7`, `-2.25e20`), so the text is a valid Bayan literal and valid JSON. Integral values print without a decimal point (`42`); `-0.0` prints `-0`; special values print `nan`, `inf`, `-inf` (`Json.encode` rejects them with `JsonError.nonFinite`).

**`format()` replaces `toString()`.** There is no `toString()`. A type displays through `formatInto(ref StrBuf)`; the allocating `format()` is derived from it (§5.2). Interpolation calls `formatInto` directly. Calling `toString()` / `ToString()` is a teaching error pointing to `format()`.

- **Specifiers** (C#-compatible; not in v0.1): `D8`, `N0`, `N2`, `F2`, `X`, `X8`, `B`, `E3`, `P1`, alignment `{x,8}` / `{x,-12}`; a `locale:` parameter for culture-aware output.
- Distinct types format as their underlying value; payload-free union cases as their name; error unions as their doc comment; `Secret<T>` as `***`.
- **Structs have no default `format()`.** Interpolating a struct without `formatInto` is a compile error suggesting `format()` or `{x:debug}`.
- **Debug** (the automatic `debug` derivation): `dbg(x)` and `$"{x:debug}"` print the struct-literal form, which is valid Bayan and round-trips (snapshot tests can read it back). `Secret<T>` still prints `***`. `dbg` is an error in release builds (§21.2). The derived `format()` and `{x:debug}` are not in v0.1.

> Round 2 §13: one display protocol, no allocating default, explicit debug output. Codes: BYN2031, BYN2225.

### 6.7 Operator overloading and equality

| Rule | Operators |
|---|---|
| Overloadable | `+ - * / %`, unary `-`, `[]`, and `compare` |
| Derived | `+=` from `+`; `==` `!=` field by field; `<` `<=` `>` `>=` from one `static Ordering compare(T a, T b)` |
| Not overloadable | `&& \|\| ?? is = ?.`, and `+% +\|` (built-in integers only in v1) |

- Operators are `static`, cannot `fails`, and cannot take `ref this`.
- No implicit conversions; use `from` / `into`.
- `==` is derived for every struct whose fields all support `==`. Opt out with `[NoEquality]`. Distinct types compare only with the same distinct type. `Secret<T>` has no `==`; it has `equalsConstantTime(other)`.
- Lint: `==` against a partial struct literal ("missing fields default to …; did you mean `is`?").
- Strings have no `+` (§3.2).
- Not in v0.1: operator overloading.

> Round 2 §2: a small fixed overload set with derived relatives keeps operators cheap and predictable. Codes: BYN2116.

---

## 7. Statements

### 7.1 Bindings
`let` (immutable), `var` (mutable), `const` (comptime; any type). A lint suggests `let` for never-reassigned `var`s. Parameters are immutable (copy to a `var` to modify).

**Shadowing** is allowed with `let`, in the same scope, only when the type changes. Same-type shadowing is a lint warning.

> Round 2 §4: shadowing names a value's new form without inventing names; same-type shadowing hides a bug more often than not. Codes: BYN2007, BYN2028.

### 7.2 Conditionals

**No parentheses around conditions; braces are mandatory.** Parentheses only group. When a condition has a brace at its top level (a struct literal, a property pattern), the whole condition is wrapped in parentheses; otherwise the formatter removes them.

```csharp
if n > 0 { … } else if n < 0 { … } else { … }
if (p is { x: 0, y: 0 }) { … }
unless user.isVerified { sendVerification(user); }
if unlikely buf.isFull { flush(); }
if never state is .corrupt { panic("corrupt"); }
switch day { case .sat or .sun: rest(); default: work(); }
```

- **`unless`** has no `else`. `unless !x` is an error. A top-level `&&` or `||` in its condition is an error with a fix to `if`. `unless x is null` is not allowed; write `if x is not null`. `unless` takes no likelihood word. `bayan fmt` rewrites `if !x { … }` (no `else`, simple condition) to `unless x { … }`.
- **`if unlikely`** is a layout hint only (the block is cold); semantics are unchanged. A lint fires when it guards an error return (already cold). Emitting the hint to C is not in v0.1.
- **`if never`** panics if the condition is true, **in every build mode**, and lays the block out cold.
- **Null checks** are `is null` / `is not null`. After `is not null` on a `let` binding or a parameter, the value is narrowed to the non-null type. `x ?? return …`, `?? break` and `?? continue` exit early.
- `if let` is deferred; `while let` (§7.4) is not.

> Round 2 §4: conditions read without C punctuation; braces remove the dangling-statement class of bugs; `if never` is a check, so it cannot depend on the build mode. Codes: BYN1017, BYN1018, BYN1022, BYN1023, BYN1032, BYN1033, BYN2027, BYN2233.

### 7.3 Postfix modifiers
Postfix modifiers are the one-line form. Allowed on **simple statements only**: `return`, `break`, `continue`, `panic`, a single call, a single assignment, `++`/`--`. Not on blocks, not chained.

```csharp
let order = db.find(id) ?? return .notFound;
return .badRequest("Cart is empty") unless cart.hasItems;
flush() if unlikely buf.isFull;
d++ if debtor.amount == 0;
```

**The condition is evaluated first;** the statement runs only if it applies.

> Round 2 §4: a call's non-null result is bound with `?? return`, not the C# `is { } order` idiom.

### 7.4 Loops

```csharp
for i in 0..n { … }
for x in xs { … }
for (i, x) in xs.indexed() { … }
for (key, value) in prices { … }
outer: for row in grid {
    for cell in row {
        break outer if cell.isTarget;
    }
}
while queue.hasItems { … }
do { … } while try socket.hasMore();
```

**Fallible iteration:**
- `for try x in seq`: every step may fail and failures propagate. Iterating a fallible sequence without `for try` is a compile error with a fix-it.
- Queries and readers are **lazy**: opening happens on the first step, so one `try` covers both. If creating the sequence can fail eagerly, both are written: `for try line in try File.open(p).lines() { … }`.
- The loop owns the sequence and drops it on every exit.
- `while try cond { … }`: the condition may fail on each evaluation.
- `while let x = expr { … }` loops while `expr` (a `T?`) is non-null, binding `x`. It works with `try`: `while let row = try rows.next() { … }`.
- A fallible sequence is a value with a method `T? next() fails E`; `for try` steps it until `next()` returns `null`. Iterating one without `for try` reports BYN2221 with a fix-it inserting `try`.
- Not in v0.1: lazy queries and readers.

> Round 2 §4: one `for x in xs` form with no parentheses; fallibility is marked once, at the loop head. Codes: BYN1017, BYN1018, BYN1019, BYN2022, BYN2232, BYN2234.

### 7.5 `switch` statements
No fall-through. `break` is optional at the end of a section and, if used, leaves the `switch` (C# semantics). Combined patterns replace stacked labels (`case .sat or .sun:`). `goto case` does not exist; leave an enclosing loop with a labeled `break`.

### 7.6 Cleanup

```csharp
defer arena.reset();                 // every exit
defer failing tx.rollback();         // exit by failure
defer panicking (p) log.error(p);    // panic in progress
```

- `using let x = …;` disposes at scope end.
- `defer stmt;` runs at scope exit, always.
- `defer failing stmt;` / `defer failing (e) { … }` runs only when the function exits by failing; it is allowed only in functions that `fails`. It also runs on fiber cancellation (not in v0.1: no fibers).
- `defer panicking stmt;` / `defer panicking (p) { … }` runs only while a panic is in progress, and is allowed anywhere.
- `failing` and `panicking` are contextual words, valid only after `defer`. Future statement modifiers follow the same ḥāl rule: head verb, then a participle.
- All run in reverse registration order. Bodies **cannot fail** (no `try`).
- **On a panic, only `defer panicking` handlers run** (innermost first); plain `defer` and `defer failing` do not, because a panic aborts without unwinding. Order: the panicking fiber's `defer panicking` handlers, then `Panic.onPanic`, then the default trace, then abort. The test runner is its own entry point with its own handler (it fails the test).
- **Panic handler bodies are restricted:** implicitly `noalloc` and `nosuspend`; no `try`; they read only `let` bindings and parameters; they write only to panic-safe sinks (stderr, panic-safe log sinks). `return`, `break` and assignment are errors ("panic handlers can't recover").
- A panic inside a handler aborts at once and prints both messages.
- Handlers are a per-fiber linked list of records, pushed and popped only in scopes that declare one.

**Process-wide hook:**

```csharp
void main() {
    Panic.onPanic = p => crashReport.write(p);
    run();
}
```

- Allowed only as the **first statement of the entry method**, at most once; libraries cannot set it. Anywhere else is a compile error. Same body restrictions as `defer panicking`.
- In v0.1 the hook takes a named function `void f(Panic p)` (`Panic.onPanic = onCrash;`); the lambda form needs §3.16.
- Fiber isolation on panic is deferred.

> Round 2 §6: `errdefer` becomes `defer failing`, one head verb with participles; panic handlers run in a known order with restrictions that keep a dying process safe. Codes: BYN1020, BYN2601, BYN2606, BYN2612, BYN2613, BYN2614.

### 7.7 `context`
`context (allocator: arena, io: Io.evented()) { … }` sets implicit context values for the block.

### 7.8 `goto`
Does not exist. `become f(x)` (guaranteed tail call) is reserved.

### 7.9 Contracts: `requires`
`requires` is a programmer contract. A violation is a bug: it **panics in every build mode** (`contract violated: <text>`) and is never a `fails`.

```csharp
public Money discount(Money total, i32 percent)
    requires percent in 0..=100
    => …;

distinct type Quantity = i32
    requires value in 1..=100;

struct OrderForm {
    string? coupon requires coupon.chars.count == 8;   // checked only when non-null
}
```

- **On functions:** checked on entry; the clause must be `bool`. A constant argument that violates it is a compile error. When the caller has proven it (for example, the argument's type carries the rule), the check is removed. Contracts appear in docs and in the prose translation.
- **On fields:** checked at construction and on every write.
- **On distinct types:** checked on every construction path. `T.tryFrom(raw)` fails with `RangeError`; `T(constant)` is checked at compile time; `T(runtimeValue)` panics on violation. The value is immutable afterwards. A distinct type with `requires` has **no default value** unless the default satisfies the rule.
- `x in a..=b` is a boolean range test.
- **`requires` replaces `[Check]`** everywhere, including the `Validate` derivation (§5.3) and boundary-test generation (§20).
- `Untrusted<T>.validated()` runs every field's construction rules and collects **all** failures into `ValidationError.fieldErrors`, producing `T.Validated`.
- Which tool: non-null types (no check), `requires` (bugs), distinct types (rules true everywhere), `Untrusted<T>.validated()` (user input, all errors), `return .x unless …` (business outcomes).
- Not in v0.1: `requires` on fields and distinct types (BYN2322), proof-based check removal, `Untrusted<T>.validated()`, property-test generation from `requires`.

> Round 2 §7: one contract form replaces `[Check]`, and a bug panics in every build instead of being skipped in `fast`. Codes: BYN2320, BYN2321, BYN2322.

---

## 8. Errors

```csharp
public Money parse(string text) fails MoneyError;               // one set
public User signUp(SignupForm f) fails EmailError, SignupError; // several
public User signUp(SignupForm f) fails SignupFailure;           // named set
Response handle(Request r) fails;                               // inferred: private functions only
```

```csharp
let resp = signUp(form) catch switch {
    EmailError      => .badRequest("Invalid email"),
    DbError.timeout => .unavailable,
    DbError e       => .serverError(e),
};
```

- **Public functions must list their errors.** The language server shows inferred sets as inlay hints.
- Variants are namespaced by their set (`EmailError.invalid` ≠ `UrlError.invalid`); an ambiguous `.invalid` is an error.
- Call sites: `try f()` propagates; `f() catch value`; `f() catch return …`; `f() catch switch { … }` (exhaustive; each arm is a pattern over the error union and binds its own name, `DbError e => …`); `f() catch (Set.variant) …` handles some and propagates the rest (which must appear in the caller's `fails`). There is no `catch (e)`.
- `f() catch { … }` runs a block when `f` fails. On a `void` call the block may complete normally. When `f` returns a value, the block has no value to give, so it must not complete normally: it ends in `return`, `break`, `continue`, a panic or another `never` expression (`let n = parse(s) catch { return -1; };`), and a block that can fall through is BYN2611. A brace body of `name: value` fields with no `;` is a target-typed struct literal, not a block: `f() catch { x: 1 }` gives that struct value. `catch {}` is an empty block, so an empty struct handler names its type: `f() catch P{}`.
- Built-in error sets: `IoError` (codes 1–7) and `Utf8Error` (`invalidSequence`, code 8, from `string.from`, §3.2); user error variants are numbered from 9.
- Payloads: `error union ParseError { badDigit(Bytes at), overflow }`.
- Debug builds record **error return traces**.
- `[Status(400)]` maps a variant to HTTP; the client sees a generic message unless the variant is `[Public]`. `[ExitCode(n)]` maps to a process exit code when `main` fails (default 1).
- **Panics abort** with message, error return trace and stack trace. No unwinding in v1; only `defer panicking` handlers and `Panic.onPanic` run (§7.6). Tests catch panics per test.

> Round 2 §5, §6, §15: errors bind per arm of `catch switch`; `--explicit` is gone, so inferred sets are inlay hints. Codes: BYN1025, BYN1029.

---

## 9. Memory

### 9.1 Where values live

| Place | How | Freed |
|---|---|---|
| Stack | Locals, `stackalloc` | Scope end |
| Arena | `context.allocator` or explicit `Arena` | Arena end, all at once |
| Heap | `Owned<T>` via an allocator | When the owner is disposed |
| Static | Literals, `const` | Never (`.rodata`) |

### 9.2 Allocators
`Arena`, `Pool<T>`, `FixedBuffer`, `General` (debug mode detects leaks, double frees, use-after-free), `Page`.

| Entry point | Default `context.allocator` |
|---|---|
| `main` | `General` |
| HTTP request | Per-request `Arena`, with a memory cap (default 16 MiB) |
| Script | Never-freed `Arena` |
| Test | `General`, leak-checking (a leak fails the test) |

### 9.3 Ownership
`unique` types move with `move` and are disposed exactly once. Using a moved value is a compile error.

- **Only a local or a `move` parameter can be moved from.** Moving out of a field, element or plain parameter is a compile error (it would leave a half-moved value). `x.take()` moves the value out of any place it can write and leaves that type's empty value behind: `let items = ledger.items.take();` leaves `ledger.items` an empty list. `take()` exists on `List`, `Map`, `StrBuf` (empty value: an empty one) and on optionals of those (`null`); on any other type it is BYN2788, and on a place that can't be written (a `let` local, a plain parameter) it reports the usual mutation error. `map[k].take()` panics on a missing key, like `map[k]`.
- **Pattern bindings on a `unique` subject borrow.** `switch x` and `x is …` bind borrows of `x`'s parts, so `x` stays usable after the switch. `switch move x` moves `x` into the switch, and its bindings then own their parts; `x` is moved afterwards on every path. The subject must be a local or a `move` parameter (BYN2789 otherwise). An owning pattern may not bind a value and a part of it, through both sides of an `and` or through a designation with bindings inside it (BYN2790): two bindings would own one storage. Two bindings overlap only when one's path is a prefix of the other's, and only bindings of a `unique` type count (`{ a: { b: let x }, a: { c: let y } }` and `{ items: let x, items.length: let n }` are accepted). In a `when` guard of an owning switch the arm's bindings and their parts are read-only (BYN2791): a guard that fails gives the value back to the switch. As an expression the subject needs parentheses, `(move x) switch { … }`: `move x switch { … }` parses as `move (x switch { … })`.

> Round 2 §4, §11: no parentheses around a `switch` subject (round 3 keeps `length` for size).
- **Debug builds check ownership at runtime.** Each `unique` allocation records its current owner and every move updates it; a second owner, or a read through a moved-from local, panics with both source locations, in the usual panic format: `second owner of List<i64>: owned since a.byn:3` or `use of moved List<i64>: moved at a.byn:4`, followed by `  at a.byn:5`. Safe and fast builds compile the checker out. Rules here hold outside `unsafe`, where raw pointers may alias.

### 9.4 Borrowing and regions (no lifetime annotations in the common case)
1. Every scope that frees memory is a region (function locals, `using` arenas, requests).
2. A borrow (`ref`, `Span<T>`, `string` slice, or a struct containing one) carries its source's region; the compiler infers it.
3. **A borrow cannot be stored anywhere that outlives its region.** Fix: `.copy(to: alloc)` or `move` into an `Owned`.
4. A returned borrow derives from **all** borrowed arguments unless narrowed: `string first(string a, string b) borrows a`.
5. **While any borrow into a value is alive, the value cannot be structurally modified.** Passing `x` and `ref x` in one call is an error. A call to a `ref this` method modifies its receiver's root local, so `lexer.push(t)` is an error while a borrow of `lexer` is alive. Loans are tracked per root local, not per field.
   A method whose return type is a span may return a borrow of its own receiver's fields: `struct Lexer { List<Token> toks; Span<Token> tokens() => toks.asSpan(); }`. At the call site the result borrows the receiver, and rules 3 and 5 apply to it as to any borrow of the receiver. A `string` view of an own field, a span inside a returned tuple, and a borrow of a local's storage are still rejected (BYN3003).
6. `arena.reset()` requires exclusive access.
7. Literals and `const` data are static and may go anywhere.

> Round 2 §9: mutation is declared with `ref this`, not inferred. Codes: BYN2115.

### 9.5 Uninitialized memory
`stackalloc T[n] uninit` yields `Uninit<T>`, which cannot be read. Writing APIs return the initialized prefix as a `Span<T>`. A runtime-sized `stackalloc` requires a bound: `stackalloc u8[n] max 4KiB`.

> Round 2 §15: C# type names are input forms. Codes: BYN1030.

### 9.6 Handles
`Pool<T>` + `Handle<T>` (index + generation, 8 bytes) for graphs and long-lived data; stale handles are detected.

### 9.7 Bounds and `unsafe`
Bounds checks apply in **every** build mode; the compiler removes only those it proves redundant. Raw pointers, pointer arithmetic, `bitcast` on non-plain types and `extern` calls require `unsafe`, where region checks do not apply.

### 9.8 GC (future)
A GC, if added, is an allocator plus a `Gc<T>` handle, never a build or language mode. It is not coupled to inheritance. A package opts in with the `gc` capability (§14). `Gc` and `gc` are reserved now (§2.4). Not in v0.1 or v1.

> Round 2, carried over from round 1: fixes the shape of a future GC so v1 code stays valid. Codes: BYN0012.

---

## 10. Parameters and arguments

| Modifier | Meaning |
|---|---|
| *(none)* | Read-only; the compiler passes by value or by reference based on size |
| `ref` | Mutable borrow |
| `move` | Ownership transfer (required for `unique` values) |
| `out` | C# compatibility; prefer returning `T?` or a tuple |
| `in` | Accepted, redundant; removed by the formatter |

- When two parameters share a type, call sites must name them (`copy(from: a, to: b)`).
- `bool` parameters must be passed by name.
- An unnamed argument counts as named when it is a variable with the parameter's name, or a member access whose last name is the parameter's: given `Span fromBounds(i64 start, i64 end)`, `fromBounds(start, end)` and `fromBounds(tok.start, tok.end)` need no names (BYN2305 does not fire).
- When the unnamed arguments before any named one all carry such names, each for a different parameter, in an order other than the parameters', the call is an error naming the swapped pair: `fromBounds(end, start)` is BYN2307. A call mixing such names with other arguments gets BYN2305 for each argument that must be named and isn't.
- Overloads may not differ only by `ref`/`out`. Default and named parameters as in C#.

**Mutating members: `ref this`.** `mut` is not a keyword.

```csharp
public void add(ref this, CartLine item) fails CartError { … }
public bool isEmpty => lines.isEmpty;          // read-only: the default
```

- Read-only is the default for every member. `ref this` must be the first parameter. A member whose body modifies `this` (or calls a `ref this` member on it) without declaring `ref this` is an error with a fix that inserts it. Operators cannot take `ref this` (§6.7).
- Calling a `ref this` member on a `let` is an error with a fix to `var`.
- Passing for mutation uses `ref` at the call site: `fill(ref cart)`.
- Mutating a value while it is borrowed (for example, inside `for line in cart.lines`) is an error (§9.4 rule 5).
- Element access on a `var` `List` or `Map` yields the stored value in place, with no silent copy: `stats.getOrAdd("api").record(12ms)` changes the stored value. On a `let` collection it is the `let` error above.

> Round 2 §9: mutation is declared, not inferred, so a signature tells the reader what a call can change. Codes: BYN2104, BYN2115, BYN2116, BYN2117.

---

## 11. Generics

```csharp
T max<T>(T a, T b) where T : IOrder<T> => a > b ? a : b;
struct Ring<T, const i32 N> { T[N] items; i32 head; }
interface IRepository<T, K = T.Id> where T : IEntity<T> { … }
```

| Constraint | Meaning |
|---|---|
| `T : IA, IB` | Implements interfaces |
| `T : struct` / `T : unique` | Copyable / move-only |
| `T : default` | Defaultable |
| `const i32 N` | Compile-time value parameter |
| `E.Item : INumber` | Constraint on an associated type |

> Round 2 §15: C# type names are input forms. Codes: BYN1030.

- Constraints are **checked at definition**.
- **No variance.**
- **Specialization: the specific overrides the general** (Pāṇini). Hand-written beats derived; instance beats extension; concrete beats generic; more specific constraint beats less specific.
- Debug builds share generic code via witness tables; release builds monomorphize. `[Specialize]` / `[Shared]` override per function.
- Inference from arguments; explicit type arguments only when nothing can be inferred.
- No higher-kinded types. Variadic generics: v2 candidate.

---

## 12. Comptime

### 12.1 Where it runs
`const` declarations, `comptime { }` blocks, `const` parameters, type-returning `comptime` functions, derivations, program queries.

### 12.2 Sandbox
Deterministic. Allowed: pure computation, reading files by **literal path inside the package** (hidden and VCS-ignored files refused unless listed in `comptimeAllow`), type reflection, raising diagnostics. Not allowed: network, clock, randomness, writing files, environment variables (pass through `build.byn`). Every file read is recorded in the lockfile.

### 12.3 Budgets
10,000,000 interpreter steps and 256 MiB per top-level evaluation; raise per function with `[ComptimeBudget(n)]`. A dependency's budgets cannot be raised by its dependents. Steps, not time, keep results identical on every machine.

### 12.4 Semantics
- Runs on the build machine with the **target's** layout rules (pointer size, endianness, alignment).
- Values crossing into runtime must be const-serializable and land in `.rodata`.
- `typeof(T)` returns `TypeInfo` (name, fields, methods, interfaces, attributes, size, align, kind).
- `comptime for`, `comptime if`, `this.[field]` field access.
- `comptime if target.os is .windows { … }` replaces `#if`; all branches are type-checked for all targets.
- `comptime print(…)`; errors show both the comptime stack and the triggering user code.

### 12.5 Derivations, program queries, generators

```csharp
public derivation Csv for T where T : struct {
    public void writeRowInto(ref StrBuf b) {
        comptime for (i, f) in typeof(T).fields.indexed() {
            comptime if i > 0 { b.append(','); }
            b.appendCsv(this.[f]);
        }
    }
}

const routes = comptime Program.functionsWith<HttpRoute>();
```

Derivations are applied with `[Derive(Row)]`, `[Derive(Json)]`, `[Derive(Csv)]` and only add members. `bayan expand T` shows the generated code, which must be identical to the hand-written equivalent (for example, ordinal reads for `Row`). Not in v0.1.

> Round 2 §4, §19: loop and condition syntax as in §7 (also `comptime if` in §12.4); derived code is inspectable and no different from hand-written code.

Tier-3 **generators** run from `build.byn`, read declared inputs, emit Bayan source into `gen/`, are sandboxed (no network), cached by input hash, and checked for determinism by the toolchain.

### 12.6 Library diagnostics

```csharp
diagnostic JQ0001 error "unknown column `{column}` in `{table}`" {
    help: "did you mean `{suggestion}`?",
    explain: """…""",
}
public static Query<T> sql<T>(const string text) {
    comptime { … fail JQ0001(column: c, table: t, suggestion: s, at: text); }
}
```

> Round 2 §10: diagnostic fields use `:` like every field initialization.

Packages reserve a code prefix in `package.byn`; the registry enforces uniqueness. `[WhenMissing("…")]` customizes unmet-constraint messages. `[Replaced(by: "…", since: "2.0")]` drives `bayan migrate`. Projects may raise a library's warnings to errors but never lower its errors.

---

## 13. Concurrency

### 13.1 I/O model
All I/O goes through `context.Io`:

| Implementation | Use |
|---|---|
| `Io.Blocking` | Simple tools |
| `Io.Threaded` | **Default for scripts**; the pool starts on the first `async` |
| `Io.Evented` | Servers (io_uring / IOCP / kqueue + fibers) |
| `Io.Simulated` | Tests: fake clock, seeded scheduling, fault injection, simulated files |

Libraries never choose an implementation. Blocking work is offloaded automatically (DNS, file I/O on epoll, `[Blocking]` externs). Debug builds warn when a fiber runs over 50 ms without yielding. Scripts may override with `#:io evented`.

### 13.2 Async
- No coloring: functions are never declared `async`. `async` as a function modifier is a teaching error showing the call-site form.
- `async f(args)` is a call-site expression that schedules `f` on `context.io` and returns a `Future<T>` (a `ref struct`); `await` joins; `try await` propagates errors. `await (a, b)` awaits several. Implementation: `async f(x)` hands `fOp(x)` (the maṣdar, §5.2) to `context.io`.
- Under `Io.Blocking`, `async` runs the call eagerly (same code, sequential).
- **Every await can fail**; `Canceled` is in every I/O error set.
- `nosuspend` on a function is a compiler-checked guarantee; a violation names the suspending call chain.
- **Futures are scoped:** a future cannot escape its block; leaving the block cancels unfinished futures, which runs their `defer failing` handlers (§7.6).
- v1 uses **fibers** (stackful, lazily committed stacks, guard pages). The stackless transform arrives with the WASM target.

**Deadlines: `context (timeout: d) { … }`.**
- Sets a deadline for every suspending operation inside the block, including in called functions, with no parameter plumbing.
- A nested `context (timeout: …)` can only shorten the remaining time (the effective deadline is the minimum of the remaining and the new one).
- Expiry fails the suspended operation with `TimeoutError`.
- CPU-bound loops check explicitly: `try context.checkDeadline();`.
- Not in v0.1: async, futures and deadlines (no fibers).

> Round 2 §14: functions stay colorless; scoped futures and block deadlines give cancellation without plumbing. Codes: BYN1016.

### 13.3 Structured concurrency
`using let group = Io.group(); group.spawn(…); try await group.all();` Futures cannot outlive the scope that started them; leaving the scope cancels children; tasks may borrow the caller's stack.

### 13.4 Suspension inference
The compiler infers which functions may suspend (calls through `dyn` are assumed to). The language server marks such calls with inlay hints (`suspends`). Optional annotations are verified: `suspends`, `nosuspend` (a promise on a signature, or a block). Errors:
- a suspension point inside `nosuspend`;
- a **`Mutex` guard alive across a suspension point**;
- a **`[ThreadLocal]` borrow alive across a suspension point**.

> Round 2 §15: `bayan fmt --explicit` is gone; inferred effects are inlay hints, never a stored form.

### 13.5 Data sharing
- `ref` borrows: one task at a time; passing `ref x` to a spawned task locks `x` until the group ends.
- Read-only borrows: any number of tasks.
- Shared mutation: `Mutex<T>`, atomics (sequentially consistent by default; weaker orderings explicit), channels (`Channel<T>`, `Io.first(…)`).
- Types containing raw pointers cannot cross threads without an explicit `unsafe` marker.

`Io.offload(op)` runs CPU-heavy work on the thread pool.

---

## 14. Effects and capabilities

| Annotation | Checked |
|---|---|
| `noalloc` | The function cannot allocate (inferred through calls) |
| `nosuspend` | The function cannot suspend |
| `suspends` | The function does suspend (optional, verified) |
| `fails E` | §8 |

**Package capabilities:** `package.byn` declares `capabilities: [.fs, .net, .process, .env, .ffi]` (and, in a future GC tier, `.gc`, §9.8). The compiler infers what a package actually uses (all I/O flows through `context.Io`) and fails the build if it uses more than declared. An update that adds a capability is flagged in the lockfile diff.

> Round 2 §10, carried over (GC): manifest fields use `:`; `gc` is a capability, not a mode.

---

## 15. Security

### 15.1 Taint tracking
- **Sources** produce `Untrusted<T>`: HTTP requests (body, query, headers, cookies), stdin, command-line arguments, external files, text from C.
- **Sinks** reject `Untrusted<T>`: raw SQL, raw HTML, command-running APIs (the program and its arguments), file paths, redirect URLs.
- **Processes.** `Io.run(program, args)` never goes through a shell, and refuses `.bat`/`.cmd` files on Windows (`cmd.exe` re-parses their arguments). `Io.runBatch(path, args)` runs a batch file: it runs the system directory's `cmd.exe /d /e:ON /v:OFF /s /c` with the path and each argument in double quotes, and fails with `invalidInput` without starting anything when the path does not end in `.bat`/`.cmd`, has no `/` or `\` (a bare name would make `cmd.exe` search the current directory and `PATH`; write `.\build.cmd`), or the path or an argument holds NUL, CR, LF, `"` or `%`. Off Windows it fails with `unsupported`. The arguments of both are a sink: an `Untrusted<T>` program, path or argument (or a list, span or array of them) is a compile error (BYN2786). `Io.run` involves no shell, but an untrusted program path runs whatever the attacker names, and option injection (`--upload-pack=…` to `git`, `-oProxyCommand=…` to `ssh`) makes the program itself run attacker-chosen commands. `IoError` gains `invalidInput` and `unsupported`, so its codes are 1–7.
- **Standard streams.** `Io.stdout.write(text)` and `Io.stderr.write(text)` write without a newline (stdout is buffered with `print`). `Io.stdin.readAll()` returns `Result<string, IoError>`: the rest of standard input, bytes as-is (binary mode on Windows, not checked for UTF-8), allocated from `context.allocator`. For now it is a plain `string`, not `Untrusted<string>`.
- **Clearing taint:** `parse`/`validate` (yielding `T.Validated`), escaped forms (interpolated SQL parameters, HTML escaping), or explicit `trust(x, reason: "…")`.
- Terminal output and logs escape control characters in untrusted values.
- **Implemented so far:** `Untrusted<T>` has the C representation of `T`; `Untrusted(x)` wraps and `trust(x, reason: "…")` unwraps, with a non-empty string literal as the reason. There is no implicit conversion either way. Sources are not yet wrapped.

### 15.2 Secrets
`Secret<T>` and `[Secret]` fields: excluded from debug output, logs, `dbg`, `Json`, `Diff` and every derivation (formatted as `***`); no `==`, only `equalsConstantTime(other)`; zeroed on dispose.

> Round 2 §2, §13: constant-time comparison is a named method so `==` cannot leak timing.

### 15.3 Randomness and cryptography
`Random` (seeded, reproducible) and `CryptoRandom` are separate types; token, key and nonce APIs accept only `CryptoRandom`. Cryptography APIs are misuse-resistant (AEAD with nonce management; no ECB; raw block ciphers only in `unsafe`).

### 15.4 Resource limits
Decoders default to safe limits (JSON nesting depth 128, maximum size). Per-request arenas have a memory cap. Regexes are compile-time finite automata (no ReDoS).

### 15.5 Invariants versus validation
`requires` contracts (§7.9) detect bugs and panic in every build mode. **`validate()`, `Untrusted<T>.validated()` and `T.Validated` are the security boundary** and are checked in every build.

> Round 2 §7: `requires` replaces `[Check]` and is no longer skipped in `fast`.

---

## 16. Strings and collections conventions

**Naming follows the grammar (§5.2):** a verb on `ref this` mutates; the same verb without `ref this` returns a new value; the `-ed` participle exists only as the copy form of a mutating verb.

| Mutating (`ref this`) | Result form (`-ed`, derived: a view if possible, otherwise a copy) |
|---|---|
| `append(x)`, `prepend(x)`, `insert(x, at:)`, `appendAll(xs)` | `appended`, `prepended` |
| `sort()`, `sort(by)`, `reverse()` | `sorted`, `reversed` |
| `removeAt(i)`, `removeWhere(f)`, `removeFirst()` → `T?`, `removeLast()` → `T?`, `replaceRange(r, with:)`, `fill(x)`, `clear()` | |

- Ordered collections (`List`, `Deque`) use `append` / `prepend`; unordered ones (`Set`, `Map`) use `add`.
- Costs are stated per method: `prepend`, `insert` and `removeFirst` are O(n) on `List` and O(1) on `Deque`.
- **Size:** `length` (O(1)) on every collection (round 3 keeps length); `count(p)` only counts elements matching a predicate (strings: §3.2).
- **Queries** (never allocate): `length`, `isEmpty`, `hasItems`, `first` / `last` (`T?`), `xs[i]` (panics out of range), `at(i)` (`T?`), `contains`, `indexOf` (`i32?`), `indexWhere`, `any`, `all`.
- **Views** (never allocate): slices `xs[1..4]`, `xs[1..^1]`, `take`, `skip`, `takeLast`, `skipLast`, `stride`, `chunks`, `windows`, `splitAt`. C#'s `Substring(start, length)` migrates to `s[start..(start + length)]`.
- **Lazy iterators** are structs with inlined lambdas: `where`, `select`, `sumBy`, `minBy`, `maxBy`, `any`, `all`, `groupBy`; nothing allocates until `toList()`.
- **New data** (allocate from `context.allocator`): `toList()`, `distinct()`, `join(sep)`, `join(sep, f)`, `flatten()` (Form I, not participles), spread `[..a, ..b]`.
- Out-of-range slices panic in safe builds; `trySlice(a..b)` returns `Span<T>?`.
- Read-only queries and views take `ReadOnlySpan<T>` parameters (a `Span<T>`, `List<T>`, array or, for bytes, `string` converts implicitly).
- Collections copy explicitly: `xs.copy(to: alloc)` for `List`, `Map` and `StrBuf` returns an independent value of the same type (not allowed when an element, key or value holds a `List`, `Map`, `StrBuf` or `Owned`).
- Compile-time work: bounds checks removed when provably in range; constant slices of fixed arrays have type `T[N]`; `xs[^1]` lowers to `xs[xs.length - 1]`; slice chains fuse.

**Literals:**

```csharp
List<i32> xs = [1, 2, 3];
Set<Permission> p = [.read, .write];
Span<i32> s = [1, 2, 3];                         // stack, no allocation
let all = [..xs, ..more, 4];                     // spread
Map<string, i32> stock = ["A-1": 12, "B-2": 0];  // map literal
Map<Sku, Money> prices = [];
let fixed = [1, 2, 3];                           // no target type: i32[3] on the stack
let headers = ["Accept": "*/*"];                 // no target type: Map<string, string>
```

- Not in v0.1: everything that takes a lambda (`where`, `select`, `sumBy`, `removeWhere`, `sort(by)`, `join(sep, f)`), `Deque`, `Set` (and its literals), slices on lists, `prepend`, `insert`, `sorted`.

> Round 2 §11: one size name and verb/participle pairs that follow §5.2; `first`/`last` return `T?` instead of a parallel `tryFirst`/`tryLast`. Codes: BYN2104, BYN2230, BYN2235, BYN2236.
- Maps: reading `map[k]` panics if missing; `map.getOr(k, fallback)` reads without inserting; `ref map.slot(k)` gets or inserts a default (requires `V : default`).
- `Grid<T>`-style types accept multi-dimensional ranges: `grid[1..^1, 1..^1]`.

---

## 17. Numerics

| Build | `a + b` on overflow |
|---|---|
| debug, safe, fast, small | Traps |

Overflow is a bug and traps in every build mode. `+% -% *%` wrap; `+| -| *|` saturate; compound forms `+%=` etc. The compiler removes checks it can prove unnecessary (loop counters, values of distinct types with `requires` ranges; not in v0.1).

Floats are IEEE 754; C is emitted with `-ffp-contract=off`; `[FastMath]` opts in per function. Derived equality and hashing on floats use IEEE `totalOrder` (so a struct containing NaN equals itself, and `0.0` and `-0.0` differ there, unlike `==`).

> Round 2 §3: replaces "wraps in fast"; an arithmetic bug must not change meaning with the build mode.

---

## 18. FFI

```csharp
[Link("sqlite3")]
extern c {
    c_int sqlite3_open(cstr filename, out Sqlite3* db);
    [Blocking] c_int sqlite3_step(Sqlite3Stmt* stmt);
}
[CLayout] struct Timespec { c_long tvSec; c_long tvNsec; }
[ExportC("tally_settle")] public c_int settleC(…) { … }
```

- `comptime CImport("header.h")` translates headers.
- Calling `extern` functions requires `unsafe`; libraries wrap them once in safe APIs.
- `build.addCSource(…)` compiles C dependencies from source via `zig cc`.
- `kind: .sharedLib` / `.staticLib` generates a C header automatically.
- Callbacks from C onto fibers switch to the thread stack. A panic in a callback aborts.
- `cstr` → `string` validates UTF-8; non-validating conversion only in `unsafe`. Exported signatures use C-compatible types; handing pointers to C requires `unsafe`.

> Round 2 §10: manifest fields use `:`.

---

## 19. Packages, editions and builds

### 19.1 Modules
A directory is a module (`src/orders/` → `app.orders`). `import app.orders;` / `import app.users as u;`. No circular imports. `using` is only for disposal.

### 19.2 Manifest
`package.byn` is **data only** (no calls, no `comptime`), type-checked against `Package`:

```csharp
Package {
    name: "example/tally",
    version: "0.1.0",
    edition: 2027,
    kind: .app,
    codes: "TLY",
    capabilities: [.fs],
    deps: [],
    test: Tests { diffCoverage: 80, errorPaths: 100, mutation: 75, nightly: [.fuzz] },
    style: Style {
        indent: 4,
        braces: .sameLine,        // or .nextLine
        maxLine: 100,
        trailingCommas: true,
    },
}
```

`style` holds the project's formatting choices (defaults in `docs/bayan-style.md`); `bayan fmt` reads it. Style is never compiler-enforced, and grammar rules (§5) cannot be configured here. The `bayan new` templates use the same `:` syntax. Formatter behaviour is not in v0.1.

> Round 2 §10, §18: `package.byn` is a Bayan struct literal, so it uses `:` like every literal; style is per-project data. Codes: BYN3104.

The registry publishes JSON metadata, and `bayan pkg export --json` exists, so outside tools never parse Bayan.

### 19.3 Dependencies and supply chain
- Minimal version selection (Go). Lockfile with content hashes.
- Verified namespaces (domain or handle). Private registries scoped by namespace.
- **Isnād:** each version carries a signed provenance chain (author → source commit → reproducible build → registry), graded by strength.
- **No install scripts.** `build.byn` is sandboxed: declared tools and paths only, no network.
- `bayan audit`: advisories, plus every `unsafe` block, `extern` declaration and C source per dependency.

### 19.4 Editions and stability
`edition: 2027`. Editions change syntax and lints, never the ABI; packages on different editions link together. `core` follows the Fundamento rule: once released it only grows. Unproven APIs live in `std.experimental`.

> Round 2 §10: manifest fields use `:`.

### 19.5 Build modes and reproducibility
`debug`, `safe`, `fast`, `small`. Builds are reproducible: no timestamps, stripped paths, deterministic comptime and generators; CI verifies by building twice.

---

## 20. Testing

```csharp
test "parse rejects missing @" { assert Email.parse("x.y") is .invalid; }
[Cases("a@b.co", "x.y@z.io")] test "parse accepts valid" (string s) { assert Email.tryParse(s) is not null; }
[Cases((0, false), (1, true), (100, true), (101, false))]
test "quantity bounds" (i32 input, bool valid) { … }
[Cases(discountCases)]                      // from a const list (xUnit MemberData)
test "discount" (Money total, i32 percent, Money expected) { … }
[Pairwise] test "parses across currencies" (Currency c, bool negative, bool fractional) { … }
bench "parse ledger" { measure Ledger.read(sample); }
property "balances sum to zero" (Span<Expense> xs) { … }
[Nightly] fuzz "reader never crashes" (string text) { _ = Ledger.read(text) catch return; }
model test "cart behaves like a list" { real = …; model = …; ops = [Cart.addOp, …]; check … }
```

- **`assert expr;`** is a statement (no parentheses). A failure prints **power-assert** output: the value of every sub-expression.
  ```
  ✗ cart total after two adds                    cart.byn:31
    assert cart.subtotal == { cents: 750 }
           │    │        │
           │    │        false
           │    { cents: 500 }
           Cart { lines: [CartLine { sku: "A-1", qty: 2, … }], coupon: null }
  ```
- **No fluent assertion API** in the standard library; plain expressions plus power output cover it.
- **`[Cases(...)]`** takes single values, tuples, or a const list. Each case is a separate test, reported with its values: `✗ quantity bounds (input: 101, valid: false)`. The value count must match the test's parameters, and every value must be a constant. `[Cases(constList)]` is not in v0.1.
- **Doctests:** fenced code in `///` comments is compiled and run by `bayan test`. `assert` lines are checked; a `// prints: …` comment is compared with the output. Not in v0.1.
- **Benchmarks:** `bench "name" { measure expr; }` reports time per op, allocations per op and variance, with seeded randomness. `bench` and `measure` are contextual words. Not in v0.1.
- **`property`** tests generate inputs from the parameter types' `requires` rules (§7.9) and shrink failures into `testdata/`. Not in v0.1.
- **Laws** are opt-in, never imposed, and never feed the optimizer: the release binary is byte-identical with laws on or off. When on, laws generate property tests, and a violated law fails `bayan test` with a minimal counterexample.
- Tests live in the same file or in a sibling `x.test.byn` (same module, private access). Integration tests in `tests/` see only the public API. Release builds strip tests.
- Tests run with a **simulated context** by default (fixed clock, seeded randomness, `Io.Simulated`).
- **Derived law tests** run automatically (round trips, `tryX` consistency, equality/hash, ordering, idempotent `ensureX`, `Xed()` non-mutation, codec round trips, `Builder`, `Op`) plus **boundary tests from `requires`** and **differential tests of specializations against the general version**.
- `noalloc` is proven at compile time; `assertNoAlloc` checks at runtime.
- `bayan new test X` scaffolds one case per success and error variant; `todo(…)` reports **pending**, never passing.
- Coverage: lines, branches and **error paths** (every `fails` variant triggered). `bayan build` reports public functions no test reaches.
- Mutation testing runs mutants in the interpreter: `bayan test --mutate`. Its output names the surviving mutant and suggests the missing case (not in v0.1).
- Fuzz findings are minimized into `testdata/fuzz/<test>/` and require a name and a reason to be committed.
- `bayan test --explore` enumerates interleavings for small concurrent tests; `--sanitize` builds with ASan/UBSan.
- `snapshot(x)` with `bayan test --update`. `Io.Recording` / `Io.Replay` capture and replay traffic (untrusted fields redacted).
- CI gates live in `package.byn` (§19.2); diff coverage is preferred over global coverage.

> Round 2 §16, carried over (laws): `assert` is a statement with power output instead of an assertion API; `[Cases]` scales to tables; `requires` drives boundary and property tests. Codes: BYN2773, BYN2774, BYN2775.

---

## 21. Diagnostics and tooling

### 21.1 Diagnostics
Every diagnostic states **what**, **where**, **why** and a **fix** (machine-applicable when possible), with a code (`BYN####` for the compiler, a registered prefix for packages) and `bayan explain CODE` (offline). Diagnostics are snapshot-tested and come from message tables (localizable later).

### 21.2 CLI

| Command | Purpose |
|---|---|
| `bayan new X`, `bayan init` | Create an app (`--lib` library, `--script` script) in a new or existing directory (§21.4) |
| `bayan run` | Build and run a program or script |
| `bayan build` | Build (`--mode debug\|safe\|fast\|small`, `--target`) |
| `bayan test` | Tests (`--json`, `--changed [--base <ref>]`, `--tier`, `--mutate`, `--explore`, `--sanitize`, `--update`, `--triage`) |
| `bayan dist` | Release builds into `dist/<version>/` (`--target t\|all`, `--mode`, `--force`, `--allow-dirty`; §21.4) |
| `bayan clean` | Remove `out/` (`--dist` also `dist/`, `--cache` the build cache) |
| `bayan cache stats` | Location, entries and size of the shared build cache (§21.4) |
| `bayan doctor` | Check the compiler, host target, C compiler, `zig`, git and cache; every failure prints a fix; exits 4 on any failure |
| `bayan --version` | Compiler version and the commit it was built from |
| `bayan fmt` | Format to canonical form and rewrite input forms (§21.5) (`--check`, `--prose`, `--from-prose`, `--check-prose`); reads `style` from `package.byn` (§19.2); not in v0.1 |
| `bayan lint`, `bayan doc`, `bayan repl`, `bayan lsp` | |

Every command takes `--color auto|always|never` (before any `--`). `auto` colors output that goes to a terminal, unless `NO_COLOR` is set. `bayan test --changed` runs only the tests of modules with a file that differs from `HEAD` (or `--base <ref>`, which implies `--changed`), untracked files included; a changed `package.byn` runs everything. It uses git read-only.
| `bayan pkg` | Packages (`add`, `audit`, `export --json`) |
| `bayan expand X` | Show every derived member |
| `bayan explain CODE` | Long explanation of a diagnostic |
| `bayan migrate` | Rename casing, upgrade editions, apply `[Replaced]` |
| `bayan new test X` | Test scaffolds |

`check`, `build`, `run` and `test` take `--diagnostics json`: each diagnostic goes to stderr as one JSON object per line, with `code`, `severity`, `file`, `span` (byte offsets `start`/`end` plus one-based `line`/`column`/`endLine`/`endColumn` counted in Unicode scalars), `message`, `label`, `help` and `fixes` (each a `span` and a `replacement`). `file` and `span` are null for diagnostics without a location.

Exit codes, documented in `bayan --help`:

| Code | Meaning |
|---|---|
| `0` | Success |
| `1` | Compile errors |
| `2` | Usage errors (unknown command or option, missing input path) |
| `3` | Test failures |
| `4` | Toolchain problems (no C compiler, unwritable cache, C compiler failure) |

`bayan run` uses the CLI's codes until the program starts (1 for compile errors, 4 for toolchain problems), then passes the program's own exit code through unchanged, as `cargo run` and `go run` do. It therefore cannot tell a build failure from a program that exits 1; anything that needs the distinction runs `bayan build` first or reads `--diagnostics json`.

The language server runs inside the compiler process. Display-only views (inferred types, inferred error sets and effects, expanded derived members) are **language-server inlay hints**, never a stored form. `dbg(expr)` prints the expression text and value and is an error in release builds.

> Round 2 §15, §18: there are no terse/explicit formatter modes; `--explicit` and `--terse` are removed and their views are inlay hints.

### 21.4 Projects, outputs and VCS

**Projects.** `bayan new <name>` creates an app: `package.byn`, `README.md`, `.gitignore` (`/out/`, `/dist/`, `/gen/`; `bayan.lock` stays committed), `.gitattributes` (LF; `*.byn linguist-language=C#`), `.editorconfig`, `src/app/main.byn`, `tests/app.test.byn`, `testdata/` and `docs/`. `--lib` puts the code in `src/<name>/<name>.byn` and tests in `tests/<name>.test.byn`; `--script` writes a single `<name>.byn` starting with `#!/usr/bin/env bayan` and `#:io threaded`, with no manifest and no git. `bayan init` does the same in an existing directory, adding only missing files and listing the ones it skipped; it refuses a directory that has a `package.byn`. `--ci github` adds `.github/workflows/bayan.yml`. A fresh project builds and passes its tests with no edits.

**Names.** A project directory is named in kebab-case (`my-app`) or camelCase (`myApp`). The package name is `local/<kebab-case>` until published; a library's module is the camelCase form, so it can be imported.

**Layout.** In a project, `src/` holds the modules (`src/orders/` is `import orders;`), the entry module is `src/app/` for an app and `src/<name>/` for a library, and in test builds every `tests/*.test.byn` joins the entry module. Commands given no path use the current directory when it has a `package.byn`. A library has no program, so `bayan build` and `bayan run` on one are usage errors. A file starting with `#!` is a script and compiles alone, and `bayan <file>.byn` runs it.

**Outputs.** In a project, `bayan build`, `run` and `test` write to `out/<mode>/<target>/` (the binary, the test binary `<name>-test`, and the emitted C in `c/` for inspection), and `bayan test` writes `out/test/results.json`, the same JSON that `bayan test --json` prints. `out/` is disposable. `--target` takes `linux-x64`, `linux-arm64`, `macos-x64`, `macos-arm64`, `windows-x64`, `windows-arm64`, `wasm32` or `host`; any target other than the host needs zig. `bayan dist` builds the targets in `dist.targets` (or `--target`, `all` for every one) into `dist/<version>/<target>/`, with `SHA256SUMS` (`sha256sum` format) and `BUILDINFO.json` (package, version, compiler, mode, targets, source commit and dirtiness, a hash of the inputs; no timestamps). A version directory is immutable: `bayan dist` refuses to rebuild one without `--force` and says to bump the version. It refuses a dirty git tree without `--allow-dirty`. `dist/latest/` is a copy (not a symlink) of the highest semantic version present, plus a `VERSION` file. Libraries get distributions once FFI export exists; scripts have none. `bayan clean` removes `out/`, and `dist/` or the cache only when asked.

**Cache.** Compiled programs live in one cache per machine, shared by every project, worktree and process: `$BAYAN_CACHE`, else `%LOCALAPPDATA%\bayan\cache` (Windows), `~/Library/Caches/bayan` (macOS), `$XDG_CACHE_HOME/bayan` or `~/.cache/bayan` (elsewhere). Keys hash the compiler version, mode, target, C compiler, flags and the emitted C, and the emitted C names files relative to the project root, so a second worktree or a moved project compiles nothing. An entry is built in a private directory and renamed into place with no lock; one whose executable no longer matches its recorded SHA-256 is rebuilt. `bayan run script.byn` remembers the program for the script's exact bytes, mode and compiler version and starts it directly next time. `out/` stays per worktree. `bayan cache stats` reports the location, entry count and size; `bayan clean --cache` empties it.

**Version control.** Bayan reads git and never writes history: `bayan new` and `bayan init` run `git init` only when git is installed, `--vcs none` isn't given, and the directory isn't already inside a work tree; they never commit, branch or configure.

### 21.3 Performance targets
Incremental rebuild under 100 ms on a reference project; cold-build budget tracked in CI.

### 21.5 Input forms
Input forms are alternate spellings that the editor and `bayan fmt` accept and rewrite, one way, into canonical syntax. The compiler rejects them in source with a machine-applicable fix. This is the only list.

| Input form | Canonical | Code |
|---|---|---|
| `int long short byte uint ulong float double` | `i32 i64 i16 u8 u32 u64 f32 f64` | BYN1030 |
| `User!EmailError f()`, `User!(A, B) f()` | `User f() fails EmailError`, `fails A, B` | BYN1031 |
| `for (let x in xs)`, `foreach (var x in xs)` | `for x in xs` | BYN1019 |
| `if (x) {`, `while (x) {`, `switch (x) {` | no parentheses (whole-condition parentheses kept when a brace is present) | BYN1017 |
| `x == null`, `x != null` | `x is null`, `x is not null` | BYN2027 |
| `if !x { … }` (no `else`, simple condition) | `unless x { … }` | BYN1032 |
| `{ cents = 250 }` | `{ cents: 250 }` | BYN1021 |
| `"Hello " + name` (strings) | `$"Hello {name}"` | BYN2209 |
| `errdefer stmt;` | `defer failing stmt;` | BYN1020 |
| `catch (e) switch { … }` | `catch switch { E e => … }` (fix only before `switch`; otherwise help text) | BYN1025 |
| `error set S = A \| B;` | `error set S = A, B;` | BYN1029 |
| `name = "…"` in `package.byn` | `name: "…"` | BYN3104 |
| `List.add`, `StrBuf.asString()` | `append`, `finish()` (round 3 keeps `.length`) | BYN2230 |
| `toString()`, `ToString()` | `format()` | BYN2031 |

The last six rows are beyond the HANDOFF round 2 table; the plan adds them because each retired form gets the same treatment.

- `bayan fmt` is idempotent, and rewriting an input form then formatting again produces identical bytes (acceptance tests arrive with `bayan fmt`, not in v0.1).
- Prose mode (§22) is unchanged.

> Round 2 §15: input forms replace the terse/explicit formatter modes, so stored source has one spelling and C# habits still work at the keyboard. Codes: BYN1017, BYN1019, BYN1020, BYN1021, BYN1025, BYN1029, BYN1030, BYN1031, BYN1032, BYN2027, BYN2031, BYN2209, BYN2230, BYN3104.

---

## 22. Prose translation

- `bayan fmt --prose` renders code as controlled-natural-language Markdown; `--from-prose` reads it back. Templates are data files per language (`templates/en.*`; Arabic later).
- **Liberal in what it accepts, strict in what it prints:** many phrasings map to one AST; the AST prints one canonical form.
- A sentence matching two templates with different meanings is an error, never a guess.
- Statements become prose; expressions stay as inline code. Types render as tables.
- Prose files are **generated artifacts**; CI runs `--check-prose` and fails on drift.
- Implemented as a separate tool (`bayan-prose`) over the compiler's AST API.

---

## 23. Scripting

```csharp
#!/usr/bin/env bayan
#:package acme/http
#:allow none except net("api.github.com"), read("./dist")
#:io threaded
```

- Compile-and-cache (hash of source, directives and compiler version), with interpreter tiering: the first run interprets while compiling in the background.
- Default allocator: never-freed arena. Default `Io`: `Threaded`. Safety checks on.
- Permissions are deny-by-default (ḥaṣr).
- Kiln-compatible `#:` directive syntax; zero-allocation C# scripts run unchanged.

---

## 24. Compilation pipeline

```
source → lex → parse → AST → sema (types, regions, effects, comptime) → DIR
DIR → interpreter (comptime, scripts, REPL, tests, mutation)
DIR → C → libtcc (debug, scripts) | clang / zig cc (release, cross-compile)
DIR → LLVM (later) | WASM via clang wasm32 (+ generated .d.ts)
```

DIR: typed SSA, generics resolved, allocation and cleanup explicit, content-hashed per function for incremental builds. Packages are distributed as source. The C emitter defines all behavior C leaves undefined (`-fwrapv`/explicit checks, `-ffp-contract=off`, `#line` for debuggers).

Roadmap: v0.3 WASM + `.d.ts`; v0.4 `bayan gen ts`; post-v1 portable JS backend (JS + `.d.ts`, no `unsafe`, `i32`).

---

## 25. C# compatibility

**Rule:** zero-allocation C# (structs, `Span`, `ref struct`, function pointers, constrained generics) compiles as Bayan, with naming warnings where casing differs.

| C# | Bayan |
|---|---|
| UTF-16 `string`, 16-bit `char`, `s[i]` | UTF-8 `string`, 32-bit `char`, no indexer (fix-its) |
| Classes, inheritance, `object`, `dynamic`, reflection | Removed (structs, interfaces, comptime) |
| Exceptions | `fails` / `Result` / panics |
| `new T()` | `T()` (`new` accepted, removed by formatter) |
| `in` parameters | Redundant |
| `using` (import / dispose / alias) | `import`, `using let`, `import … as` |
| `goto`, `goto case` | Removed |
| `IConvertible`, `Convert.ChangeType` | `IFrom` / `ITryFrom`, `convert<T>` |
| `Substring(i, n)` | `s[i..(i + n)]` (migrated) |
| Culture-sensitive default comparison | Ordinal |
| Silent unchecked casts | Checked narrowing |
| Static constructors | `lazy` |
| `event`, multicast delegates | Removed |
| `Func<…>`, `Action<…>`, `delegate` | Function types `A => B`, `type Name = A => B;` (§3.16) |
| `[Flags] enum` | `Set<T>` of a payload-free union (§3.13) |
| `Count` / `Count()` / `Length` | `length` (round 3 keeps length) |
| `ToString()` | `format()` / `formatInto(ref StrBuf)` (§6.6) |
| `foreach`, `if (…)`, `int`, `x == null` | Input forms, rewritten (§21.5) |
| `m` / `d` literal suffixes | `dec`; doubles unsuffixed |

> Round 2 §1, §8, §11, §13, §15: compatibility rows follow the new forms.

---

## Appendix A — Settled; do not re-propose

- No `!` shorthand for `fails` in source; `User!E` is an editor input form only (§21.5).
- No symbol for `noalloc` / `nosuspend`.
- No Ruby-style `?` suffix on boolean members; `?` means only `T?`, `?.`, `??`, ternary.
- No angle-bracket unit syntax (`5<s>`, `int<Bytes>`).
- No inheritance before a GC tier exists.
- No language-level JSON; JSON is a first-class standard-library feature.
- `Op` suffix; `async`/`await`; `fails`; `.byn`; Bayan.
- One copy-with-changes form: `x with { f: v }`. No derived `withF(v)`.

> Round 2 §15, §19: `!` survives only as an input form (C22); the `with` decision of 2026-10-21.

## Appendix B — Deferred

Isnād registry design · Arabic prose templates · regex API, date/time · SIMD types, inline assembly · variadic generics · `bayan verify` (contract proofs) · JS backend, `bayan gen ts` · hot reload · GC tier and inheritance · effect system for pure/effectful functions · `become` tail calls · `sql"…"` interpolation with taint tracking · fiber isolation on panic · `if let`.

> Round 2 §13, Deferred: format specifiers are now specified (§6.6); the three HANDOFF deferrals are listed, not designed.
