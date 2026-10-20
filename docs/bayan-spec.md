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
5. **Elegant by default.** One formatter, no options.
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
| Collection | `[1, 2, 3]`, `[]`, `[..a, ..b]` | Target-typed (§6.3) |
| Map | `["alice": 500]`, `[:]` | Target-typed |
| Struct | `{ x = 1, y = 2 }` | Target-typed; only where a type is expected |
| Tuple | `(1, "a")`, `(x: 1, y: 2)` | |

### 2.4 Keywords

**Declarations:** `struct unique union enum flags open extensible interface delegate extension derivation attribute error set unit distinct type import as public private static readonly required const let var embed extern unsafe`

**Control:** `if else unless switch case default for in while do break continue return yield defer errdefer using context`

**Errors & effects:** `fails try catch never noalloc nosuspend suspends`

**Concurrency:** `async await`

**Expressions:** `is and or not with move ref out this null true false comptime checked`

**Tests:** `test property fuzz model`

**Reserved (no meaning yet; may not be used as identifiers):** `actor effect macro select become`

`foreach` is accepted for C# compatibility and formatted to `for`. `new` is accepted and removed by the formatter. `goto` does not exist.

---

## 3. Types

### 3.1 Primitives

| Canonical | C# alias (accepted, formatter normalizes) | Notes |
|---|---|---|
| `i8 i16 i32 i64` | `sbyte short int long` | `int` is a permitted alias for `i32` |
| `u8 u16 u32 u64` | `byte ushort uint ulong` | |
| `isize usize` | `nint nuint` | |
| `f32 f64` | `float double` | |
| `bool` | | |
| `char` | | **32-bit Unicode scalar** (differs from C#) |
| `string` | | UTF-8 slice, 16 bytes (pointer, length) |
| `never` | | Bottom type; see §3.9 |
| `dec` | `decimal` | Decimal arithmetic type |

Lowercase type names are reserved for the language. C interop types: `c_int`, `c_long`, `c_char`, … (`c_long` follows the platform).

### 3.2 Strings
- Always **valid UTF-8**, validated on construction (`string.tryFrom(bytes)` returns `string?`; `string.from(bytes) fails Utf8Error`).
- **No indexer.** `s[i]` is a compile error with fix-its: `s.bytes[i]`, `s.runeAt(i)`, `for (let c in s)`, `s.graphemes`.
- All offsets are **byte offsets** (`IndexOf`/`Substring`-style code stays correct). Slicing mid-code-point panics.
- `length` is the byte count. `runeCount` is O(n).
- `+` on strings is allowed only on constants; at runtime use interpolation into a buffer.
- Default comparison is **ordinal**; culture-aware comparison is an explicit API.
- `string16` exists for UTF-16 interop.

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
- `string.length` is `Bytes`; `runeCount` is `Runes`.

### 3.5 Arrays, spans, collections
- `T[N]`: fixed-size inline array (a value). Constant out-of-range indexes are compile errors.
- `Span<T>`: a borrowed view (pointer, length) whose elements may be assigned.
- `ReadOnlySpan<T>`: a borrowed view whose elements cannot be assigned. `Span<T>` → `ReadOnlySpan<T>` is implicit. `string.bytes` is a `ReadOnlySpan<u8>`, so strings stay immutable and valid UTF-8. A `Span<T>` parameter never written through gets lint BYN2310 ("use `ReadOnlySpan<T>`").
- `List<T>`, `Map<K, V>`, `HashMap<K, V>`, `Set<T>`, `Pool<T>`, `Ring<T, const int N>`, `NonEmpty<T>`: standard library, allocator-backed.
- `Map` **preserves insertion order**; `HashMap` is unordered and faster.
- Every allocator-backed collection (`List`, `Map`, `HashMap`, `Set`, `Pool`) and `StrBuf` is **`unique`** (§3.11): assigning or storing one moves it, and a copy is explicit, `xs.copy(to: alloc)`. Reading without owning goes through `ReadOnlySpan<T>`, a read-only parameter or `ref`.
- Hashing is **seeded** by default (hash-flooding resistance); tests use a fixed seed.

### 3.6 Optionals
`T?` for any type. References are non-null; `null` is the literal for "none" of an optional. `??`, `?.`, `is { } v`, and `?? return/break/continue` apply.

### 3.7 Tuples
C# value tuples with named elements and deconstruction: `(Cents share, int extra)`.

### 3.8 Results
`Result<T, E>` is a plain struct (value plus a small tag plus an optional payload). Every error variant in a program has a unique compile-time number, so merging error sets is free.

### 3.9 `never`
Returned by `panic`, `fatal`, `exit` and infinite loops. A `never` expression fits any position (`catch`, `??`, switch arms); code after it is reported unreachable. `fatal` and `exit` are linted as errors in packages that are not `kind = .app`.

### 3.10 Structs

```csharp
/// An order placed by a buyer.
[Derive(Id, Builder, Patch)]
public struct Order : IEquatable<Order>, IJsonEncode {
    public required BuyerId buyer;
    public OrderStatus status = .submitted;
    List<OrderItem> items;                        // module-visible

    public Money total => items.sum(i => i.price * i.units);   // properties may not allocate or suspend
    public bool isEmpty => items.length == 0;

    public Order(BuyerId buyer) { this.buyer = buyer; items = List<OrderItem>(); }

    public void ship() fails OrderError {
        return .alreadyShipped if status is .shipped;
        return .empty if isEmpty;
        status = .shipped;
    }
}
```

- Fields, properties, constructors, methods, operators, indexers, static members.
- `required` fields must be set at construction. Definite assignment as in C#.
- A type is **defaultable** only if every field has a default and the type is not `[NoDefault]`.
- Equality, hashing and debug formatting are derived automatically for every struct (opt out or override with `[Equality(by: .id)]`).
- `embed T;` promotes the embedded struct's fields and methods; the embedded value sits at offset 0.
- No implementation inheritance (revisit only if a GC tier is added).

### 3.11 Move-only types
`unique struct` cannot be copied; use `move`. Cleanup is derived (disposes fields in reverse declaration order) and runs exactly once, at scope end or by the final owner.

- **A type with a `unique` part is `unique`.** A struct with a `unique` field, a union with a `unique` payload, and a tuple, array or optional of a `unique` type are all `unique`, without being declared so. A struct holding a `List` is therefore move-only.
- An implicit copy of a `unique` value is a compile error whose fixes are `move x`, `x.copy(to: alloc)` (when the type has one) or passing it by `ref`.

### 3.12 Unions

```csharp
union Shape { Circle(f32 r), Rect(f32 w, f32 h) }
```

Tagged unions, stored inline. Exhaustive `switch` is required.

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
- `EnumSet<T>` (bitset) and `EnumMap<T, V>` (inline array).
- Associated data must be constant.
- `flags enum` for bit flags. `open enum` allows unknown values (C interop only). Public enums and error sets are closed unless marked `extensible`, which forces callers to include a fallback case.
- No cast from integer to enum.

### 3.14 Errors
`error enum`, `error union` (variants with payloads), `error set Name = A | B;`. See §8.

### 3.15 Interfaces

```csharp
interface IShape {
    f32 area();
    string describe() => $"shape with area {area()}";   // default method
}
interface IParse<TSelf> where TSelf : IParse<TSelf> { static abstract TSelf parse(string s) fails ParseError; }
interface IEnumerator { type Item; bool moveNext(); ref readonly Item current { get; } }  // associated type
```

- Nominal, explicit conformance (`struct Circle : IShape`). Interface names take an `I` prefix.
- Used as constraints (static dispatch) by default; `dyn ref IShape` / `dyn IShape` for dynamic dispatch through a fat pointer (no allocation).
- Interfaces with generic methods or `static abstract` members cannot be used with `dyn` (checked, with explanation).
- Retroactive conformance (`extension Money : IJsonEncode`) only when the module owns the type or the interface.

### 3.16 Delegates and closures
- `delegate bool OrderFilter(Order o);` declares a named function interface, usable as a constraint or with `dyn`.
- Lambdas are struct closures with captures stored inline. A captured `let` is copied; a captured `var` that the lambda modifies is borrowed by `ref`, tying the lambda to its scope. Escaping lambdas (fields, `dyn`, `Op`) cannot borrow.
- C# `event` is replaced by `Signal<T>` (standard library).

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
- **Everything else** (methods, properties, fields, locals, parameters, enum members, error variants): `camelCase`.
- Acronyms: `HttpClient`, `IO` (two letters stay uppercase).
- Interface names: `I` prefix.
- Enforced as errors in new code; ported C# compiles with warnings; `bayan migrate --rename` fixes casing.

### 5.2 Every type is a root; patterns are a closed set
Users may create roots, never patterns. Derived members are generated only when referenced. Pattern names are reserved: a hand-written member with a pattern name must satisfy the pattern's contract.

**Verb patterns (members, `camelCase`):**

| Pattern | Arabic source | Meaning | Contract |
|---|---|---|---|
| `x()` | Form I | Base act | Returns a value, may `fails` |
| `x()` on `ref this` | Form V | Reflexive: mutates self | |
| `xed()` | mafʿūl (participle) | Result of the act: **a view if possible, otherwise a copy** | Derived from a mutating `x()` |
| `tryX()` | Form X | Attempt | Returns `bool` or `T?`; derived from `x() fails` |
| `ensureX()` | Form II | Causative, idempotent | Returns `void` or `fails` |
| `xWith(T other)` | Form III | Act with another | Takes `T other` |
| `xInto(ref dst)` | particle إلى | Write into a destination | Takes `ref StrBuf` or `Span<T>`; `x()` allocating form derived from it |
| `xOp(…)` | maṣdar | The act as a value | Derived; type `Op<T, E>` |
| `isX` / `hasX` / `canX` | ṣifa | State | `bool`; inferred side-effect free |
| `withF(v)` | — | Copy with one field changed | Derived per field |
| `xUnchecked()` | — | Skips validation | Requires a checked sibling `x()` |

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
Open set, written in Bayan with `derivation Name for T { … }` (§12.5). Standard derivations: automatic equality/hash/debug, `Order`, `Id`, `Builder`, `Patch`, `Validate` (from `[Check]`), `Events`, `Observable`, `Json`, `Csv`, `Row`, `Binary` (CBOR), `Proto`, `Query`, `Config`, `Args`, `Gen`, `Diff`, `Ts`. Derivations only add members; they never change existing ones.

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
| `x catch …` / `catch (e) …` / `catch (E.v) …` | Handle failure (§8) |

No user-defined operator symbols. Operator implementations must not allocate, suspend or fail (checked).

### 6.2 Casts and conversions

| Conversion | Rule |
|---|---|
| Lossless numeric widening | Implicit |
| Narrowing `(u8)x` | Explicit; traps in safe builds if out of range |
| Truncation | `x.truncate<u8>()` |
| Bit reinterpretation | `bitcast<T>(x)` (same size; non-plain types require `unsafe`) |
| Units / distinct types | `(Seconds)raw`, `(i64)t` |
| Enum ↔ number | `.ordinal` / `.value`; `T.fromOrdinal(i)` returns `T?` |
| Views | `Span<T>` → `ReadOnlySpan<T>` implicit; as a call argument, `List<T>` and `T[N]` → `Span<T>` / `ReadOnlySpan<T>`, and `string` → `ReadOnlySpan<u8>`. Never `ReadOnlySpan<T>` → `Span<T>`. |
| User types | `(T)x` calls `IFrom<T, U>.from` (infallible). Fallible conversions use `ITryFrom` and `try T.from(x)` |
| `List<T>` → `Span<T>` | Implicit **only as a function argument**; otherwise `.asSpan` |
| User-defined implicit conversions | Not allowed |

`checked(expr)` evaluates with overflow detection and returns `T?`. Float-to-integer conversion saturates (NaN becomes 0). Integer division by zero traps.

### 6.3 Target-typed literals
`[…]`, `["k": v]`, `{ f = v }` and `.member` take their type from context (variable type, parameter type, return type). If more than one overload accepts a target-typed literal, it is an error, never a silent pick. A ternary inside a collection literal must be parenthesized. `{` at the start of a statement is a block.

### 6.4 Copy-and-update
`order with { status = .shipped }` (C# syntax).

### 6.5 Patterns
All C# patterns: constant, relational, `and`/`or`/`not`, property and extended property, positional, list patterns with slices (`[var first, .. var rest]`), `var`, `_`, `when` guards. A binding is `let x` (immutable) or `var x` (mutable), as for locals. Additions:

```csharp
x is 500..=599                          // range
path is "/api/v1/" + var rest           // string prefix / suffix / infix (zero-copy)
input is Email(var e)                   // active pattern from Email.tryParse
msg is Text(var body)                   // union destructuring
```

Type patterns apply only to `dyn` values and unions. `switch` must be exhaustive over unions, enums and `bool`.

### 6.6 Interpolation
`$"{x}"` uses an interpolation handler; formatting writes into the destination buffer. `$"{e}"` on an error prints the variant's doc-comment summary (fallback: its name; public errors without docs warn). `log.info($"user {user.id}")` captures `user.id` as a structured field.

**Floats** (`f64`, `f32`) format as the shortest digits that round-trip to the same value. Fixed notation when `1e-5 ≤ |x| < 1e15`, exponential outside it, spelled lowercase with no `+` and no leading zeros (`1e15`, `1.5e-7`, `-2.25e20`), so the text is a valid Bayan literal and valid JSON. Integral values print without a decimal point (`42`); `-0.0` prints `-0`; special values print `nan`, `inf`, `-inf` (`Json.encode` rejects them with `JsonError.nonFinite`). Fixed-format control (`{x:F2}`) is deferred to the format-specifier design.

---

## 7. Statements

### 7.1 Bindings
`let` (immutable), `var` (mutable), `const` (comptime; any type). A lint suggests `let` for never-reassigned `var`s. Parameters are immutable (copy to a `var` to modify).

### 7.2 Conditionals

```csharp
if (n > 0) { … } else { … }
unless (user.isVerified) { sendVerification(user); }   // no `else`; `unless (!x)` is an error
if unlikely (buf.isFull) flush();                      // cold path
if never (state is .corrupt) panic("corrupt");         // unreachable; panics in safe builds
```

### 7.3 Postfix modifiers
Allowed on **simple statements only**: `return`, `break`, `continue`, `panic`, a single call, a single assignment, `++`/`--`. Not on blocks, not chained.

```csharp
return .notFound unless db.find(id) is { } order;     // `order` stays in scope afterwards
d++ if debtor.amount == 0;
```

**The condition is evaluated first;** the statement runs only if it applies.

### 7.4 Loops

```csharp
for (let i in 0..n) …
for (let (i, x) in xs.indexed()) …
outer: for (let row in grid)
    for (let cell in row)
        break outer if cell.isTarget;
while (cond) … ; do … while (cond);
```

### 7.5 `switch` statements
No fall-through. `break` is optional at the end of a section and, if used, leaves the `switch` (C# semantics). Combined patterns replace stacked labels (`case .sat or .sun:`). `goto case` does not exist; leave an enclosing loop with a labeled `break`.

### 7.6 Cleanup
- `using let x = …;` disposes at scope end.
- `defer stmt;` runs at scope exit, always.
- `errdefer stmt;` / `errdefer (e) { … }` runs only when the function exits by failing (including cancellation).
- All run in reverse registration order. Bodies **cannot fail** (no `try`). `errdefer` is only allowed in functions that `fails`. A panic inside a cleanup body aborts.

### 7.7 `context`
`context (allocator: arena, io: Io.evented()) { … }` sets implicit context values for the block.

### 7.8 `goto`
Does not exist. `become f(x)` (guaranteed tail call) is reserved.

---

## 8. Errors

```csharp
public Money parse(string text) fails MoneyError;               // one set
public User signUp(SignupForm f) fails EmailError, SignupError; // several
public User signUp(SignupForm f) fails SignupFailure;           // named set
Response handle(Request r) fails;                               // inferred: private functions only
```

- **Public functions must list their errors.** `bayan fmt --explicit` writes out inferred sets.
- Variants are namespaced by their set (`EmailError.invalid` ≠ `UrlError.invalid`); an ambiguous `.invalid` is an error.
- Call sites: `try f()` propagates; `f() catch value`; `f() catch return …`; `f() catch (e) switch { … }` (exhaustive); `f() catch (Set.variant) …` handles some and propagates the rest (which must appear in the caller's `fails`).
- Payloads: `error union ParseError { badDigit(Bytes at), overflow }`.
- Debug builds record **error return traces**.
- `[Status(400)]` maps a variant to HTTP; the client sees a generic message unless the variant is `[Public]`. `[ExitCode(n)]` maps to a process exit code when `main` fails (default 1).
- **Panics abort** with message, error return trace and stack trace. No unwinding in v1. Tests catch panics per test.

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

### 9.4 Borrowing and regions (no lifetime annotations in the common case)
1. Every scope that frees memory is a region (function locals, `using` arenas, requests).
2. A borrow (`ref`, `Span<T>`, `string` slice, or a struct containing one) carries its source's region; the compiler infers it.
3. **A borrow cannot be stored anywhere that outlives its region.** Fix: `.copy(to: alloc)` or `move` into an `Owned`.
4. A returned borrow derives from **all** borrowed arguments unless narrowed: `string first(string a, string b) borrows a`.
5. **While any borrow into a value is alive, the value cannot be structurally modified.** Passing `x` and `ref x` in one call is an error.
6. `arena.reset()` requires exclusive access.
7. Literals and `const` data are static and may go anywhere.

### 9.5 Uninitialized memory
`stackalloc T[n] uninit` yields `Uninit<T>`, which cannot be read. Writing APIs return the initialized prefix as a `Span<T>`. A runtime-sized `stackalloc` requires a bound: `stackalloc byte[n] max 4KiB`.

### 9.6 Handles
`Pool<T>` + `Handle<T>` (index + generation, 8 bytes) for graphs and long-lived data; stale handles are detected.

### 9.7 Bounds and `unsafe`
Bounds checks apply in **every** build mode; the compiler removes only those it proves redundant. Raw pointers, pointer arithmetic, `bitcast` on non-plain types and `extern` calls require `unsafe`, where region checks do not apply.

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
- Overloads may not differ only by `ref`/`out`. Default and named parameters as in C#.

---

## 11. Generics

```csharp
T max<T>(T a, T b) where T : IOrder<T> => a > b ? a : b;
struct Ring<T, const int N> { T[N] items; int head; }
interface IRepository<T, K = T.Id> where T : IEntity<T> { … }
```

| Constraint | Meaning |
|---|---|
| `T : IA, IB` | Implements interfaces |
| `T : struct` / `T : unique` | Copyable / move-only |
| `T : default` | Defaultable |
| `const int N` | Compile-time value parameter |
| `E.Item : INumber` | Constraint on an associated type |

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
- `comptime if (target.os is .windows) { … }` replaces `#if`; all branches are type-checked for all targets.
- `comptime print(…)`; errors show both the comptime stack and the triggering user code.

### 12.5 Derivations, program queries, generators

```csharp
public derivation Csv for T where T : struct {
    public void writeRowInto(ref StrBuf b) {
        comptime for (let (i, f) in typeof(T).fields.indexed()) {
            comptime if (i > 0) b.append(',');
            b.appendCsv(this.[f]);
        }
    }
}

const routes = comptime Program.functionsWith<HttpRoute>();
```

Tier-3 **generators** run from `build.byn`, read declared inputs, emit Bayan source into `gen/`, are sandboxed (no network), cached by input hash, and checked for determinism by the toolchain.

### 12.6 Library diagnostics

```csharp
diagnostic JQ0001 error "unknown column `{column}` in `{table}`" {
    help = "did you mean `{suggestion}`?";
    explain = """…""";
}
public static Query<T> sql<T>(const string text) {
    comptime { … fail JQ0001(column: c, table: t, suggestion: s, at: text); }
}
```

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
- No coloring: functions are never declared `async`.
- `async f()` at a call site starts concurrent work and returns a `Future<T>` (a `ref struct`); `await` gets its result. `await (a, b)` awaits several.
- Under `Io.Blocking`, `async` runs the call immediately.
- **Every await can fail**; `Canceled` is in every I/O error set.
- v1 uses **fibers** (stackful, lazily committed stacks, guard pages). The stackless transform arrives with the WASM target.

### 13.3 Structured concurrency
`using let group = Io.group(); group.spawn(…); try await group.all();` Futures cannot outlive the scope that started them; leaving the scope cancels children; tasks may borrow the caller's stack.

### 13.4 Suspension inference
The compiler infers which functions may suspend (calls through `dyn` are assumed to). The IDE marks such calls; `bayan fmt --explicit` writes `suspends`. Optional annotations are verified: `suspends`, `nosuspend` (a promise on a signature, or a block). Errors:
- a suspension point inside `nosuspend`;
- a **`Mutex` guard alive across a suspension point**;
- a **`[ThreadLocal]` borrow alive across a suspension point**.

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

**Package capabilities:** `package.byn` declares `capabilities = [.fs, .net, .process, .env, .ffi]`. The compiler infers what a package actually uses (all I/O flows through `context.Io`) and fails the build if it uses more than declared. An update that adds a capability is flagged in the lockfile diff.

---

## 15. Security

### 15.1 Taint tracking
- **Sources** produce `Untrusted<T>`: HTTP requests (body, query, headers, cookies), stdin, command-line arguments, external files, text from C.
- **Sinks** reject `Untrusted<T>`: raw SQL, raw HTML, shell commands, file paths, redirect URLs.
- **Processes.** `Io.run(program, args)` never goes through a shell, and refuses `.bat`/`.cmd` files on Windows (`cmd.exe` re-parses their arguments). `Io.runBatch(path, args)` runs a batch file: it escapes each argument for `cmd.exe` when that can be done exactly and otherwise fails without starting anything. Its arguments are a sink: an `Untrusted<T>` argument is a compile error.
- **Standard streams.** `Io.stdout.write(text)` and `Io.stderr.write(text)` write without a newline (stdout is buffered with `print`). `Io.stdin.readAll()` returns `Result<string, IoError>`: the rest of standard input, bytes as-is (binary mode on Windows, not checked for UTF-8), allocated from `context.allocator`. For now it is a plain `string`, not `Untrusted<string>`.
- **Clearing taint:** `parse`/`validate` (yielding `T.Validated`), escaped forms (interpolated SQL parameters, HTML escaping), or explicit `trust(x, reason: "…")`.
- Terminal output and logs escape control characters in untrusted values.
- **Implemented so far:** `Untrusted<T>` has the C representation of `T`; `Untrusted(x)` wraps and `trust(x, reason: "…")` unwraps, with a non-empty string literal as the reason. There is no implicit conversion either way. Sources are not yet wrapped.

### 15.2 Secrets
`Secret<T>` and `[Secret]` fields: excluded from debug output, logs, `dbg`, `Json`, `Diff` and every derivation; constant-time equality; zeroed on dispose.

### 15.3 Randomness and cryptography
`Random` (seeded, reproducible) and `CryptoRandom` are separate types; token, key and nonce APIs accept only `CryptoRandom`. Cryptography APIs are misuse-resistant (AEAD with nonce management; no ECB; raw block ciphers only in `unsafe`).

### 15.4 Resource limits
Decoders default to safe limits (JSON nesting depth 128, maximum size). Per-request arenas have a memory cap. Regexes are compile-time finite automata (no ReDoS).

### 15.5 Invariants versus validation
`[Check]` invariants detect bugs (enforced in debug and safe builds, skipped in `fast`). **`validate()` and `T.Validated` are the security boundary** and are checked in every build.

---

## 16. Strings and collections conventions

- **`length`** is the only size name: an O(1) property everywhere. `count(p)` counts matches.
- Slicing is substring/subarray: `s[a..b]`, `xs[1..^1]`. C#'s `Substring(start, length)` migrates to `s[start..(start + length)]`. Copies are explicit: `.copy(to: alloc)`.
- Out-of-range slices panic in safe builds; `trySlice(a..b)` returns `Span<T>?`.
- Read-only queries and views take `ReadOnlySpan<T>` parameters (a `Span<T>`, `List<T>`, array or, for bytes, `string` converts implicitly).
- **Queries** (never allocate): `isEmpty contains indexOf lastIndexOf startsWith endsWith count any all none tryFirst tryLast`.
- **Views** (never allocate): `take skip takeLast skipLast reversed() stride chunks windows splitAt split lines trim where select`.
- **New data** (allocate from context; each has an `xInto` form): `toList sorted() distinct join upper() lower() replace() pad()`, spread `[..a, ..b]`.
- Collections copy explicitly: `xs.copy(to: alloc)` for `List`, `Map` and `StrBuf` returns an independent value of the same type (not allowed when an element, key or value holds a `List`, `Map`, `StrBuf` or `Owned`).
- `first` / `last` exist only on `NonEmpty<T>`; `tryFirst` / `tryLast` everywhere.
- Compile-time work: bounds checks removed when provably in range; constant slices of fixed arrays have type `T[N]`; `xs[^1]` lowers to `xs[xs.length - 1]`; slice chains fuse.
- Maps: reading `map[k]` panics if missing; `map.getOr(k, fallback)` reads without inserting; `ref map.slot(k)` gets or inserts a default (requires `V : default`).
- `Grid<T>`-style types accept multi-dimensional ranges: `grid[1..^1, 1..^1]`.

---

## 17. Numerics

| Build | `a + b` on overflow |
|---|---|
| debug, safe | Traps |
| fast | **Unspecified but never undefined behavior** (may wrap or trap) |

Overflow is a bug; explicit wrapping uses `+%`. Floats are IEEE 754; C is emitted with `-ffp-contract=off`; `[FastMath]` opts in per function. Derived equality and hashing on floats use IEEE `totalOrder` (so a struct containing NaN equals itself, and `0.0` and `-0.0` differ there, unlike `==`).

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
- `kind = .sharedLib` / `.staticLib` generates a C header automatically.
- Callbacks from C onto fibers switch to the thread stack. A panic in a callback aborts.
- `cstr` → `string` validates UTF-8; non-validating conversion only in `unsafe`. Exported signatures use C-compatible types; handing pointers to C requires `unsafe`.

---

## 19. Packages, editions and builds

### 19.1 Modules
A directory is a module (`src/orders/` → `app.orders`). `import app.orders;` / `import app.users as u;`. No circular imports. `using` is only for disposal.

### 19.2 Manifest
`package.byn` is **data only** (no calls, no `comptime`), type-checked against `Package`:

```csharp
Package {
    name = "example/tally", version = "0.1.0", edition = 2027,
    kind = .app, codes = "TLY", capabilities = [.fs], deps = [],
    test = Tests { diffCoverage = 80, errorPaths = 100, mutation = 75, nightly = [.fuzz] },
}
```

The registry publishes JSON metadata, and `bayan pkg export --json` exists, so outside tools never parse Bayan.

### 19.3 Dependencies and supply chain
- Minimal version selection (Go). Lockfile with content hashes.
- Verified namespaces (domain or handle). Private registries scoped by namespace.
- **Isnād:** each version carries a signed provenance chain (author → source commit → reproducible build → registry), graded by strength.
- **No install scripts.** `build.byn` is sandboxed: declared tools and paths only, no network.
- `bayan audit`: advisories, plus every `unsafe` block, `extern` declaration and C source per dependency.

### 19.4 Editions and stability
`edition = 2027`. Editions change syntax and lints, never the ABI; packages on different editions link together. `core` follows the Fundamento rule: once released it only grows. Unproven APIs live in `std.experimental`.

### 19.5 Build modes and reproducibility
`debug`, `safe`, `fast`, `small`. Builds are reproducible: no timestamps, stripped paths, deterministic comptime and generators; CI verifies by building twice.

---

## 20. Testing

```csharp
test "parse rejects missing @" { assert(Email.parse("x.y") is .invalid); }
[Cases("a@b.co", "x.y@z.io")] test "parse accepts valid" (string s) => assert(Email.tryParse(s) is { });
[Pairwise] test "parses across currencies" (Currency c, bool negative, bool fractional) { … }
property "balances sum to zero" (Span<Expense> xs) { … }
[Nightly] fuzz "reader never crashes" (string text) { _ = Ledger.read(text) catch return; }
model test "cart behaves like a list" { real = …; model = …; ops = [Cart.addOp, …]; check … }
```

- Tests live in the same file or in a sibling `x.test.byn` (same module, private access). Integration tests in `tests/` see only the public API. Release builds strip tests.
- Tests run with a **simulated context** by default (fixed clock, seeded randomness, `Io.Simulated`).
- **Derived law tests** run automatically (round trips, `tryX` consistency, equality/hash, ordering, idempotent `ensureX`, `Xed()` non-mutation, codec round trips, `Builder`, `Op`) plus **boundary tests from `[Check]`** and **differential tests of specializations against the general version**.
- `noalloc` is proven at compile time; `assertNoAlloc` checks at runtime.
- `bayan new test X` scaffolds one case per success and error variant; `todo(…)` reports **pending**, never passing.
- Coverage: lines, branches and **error paths** (every `fails` variant triggered). `bayan build` reports public functions no test reaches.
- Mutation testing runs mutants in the interpreter: `bayan test --mutate`.
- Fuzz findings are minimized into `testdata/fuzz/<test>/` and require a name and a reason to be committed.
- `bayan test --explore` enumerates interleavings for small concurrent tests; `--sanitize` builds with ASan/UBSan.
- `snapshot(x)` with `bayan test --update`. `Io.Recording` / `Io.Replay` capture and replay traffic (untrusted fields redacted).
- CI gates live in `package.byn` (§19.2); diff coverage is preferred over global coverage.

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
| `bayan fmt` | Format (`--explicit`, `--terse`, `--prose`, `--from-prose`, `--check-prose`) |
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

The language server runs inside the compiler process. `dbg(expr)` prints the expression text and value and is an error in release builds.

### 21.4 Projects, outputs and VCS

**Projects.** `bayan new <name>` creates an app: `package.byn`, `README.md`, `.gitignore` (`/out/`, `/dist/`, `/gen/`; `bayan.lock` stays committed), `.gitattributes` (LF; `*.byn linguist-language=C#`), `.editorconfig`, `src/app/main.byn`, `tests/app.test.byn`, `testdata/` and `docs/`. `--lib` puts the code in `src/<name>/<name>.byn` and tests in `tests/<name>.test.byn`; `--script` writes a single `<name>.byn` starting with `#!/usr/bin/env bayan` and `#:io threaded`, with no manifest and no git. `bayan init` does the same in an existing directory, adding only missing files and listing the ones it skipped; it refuses a directory that has a `package.byn`. `--ci github` adds `.github/workflows/bayan.yml`. A fresh project builds and passes its tests with no edits.

**Names.** A project directory is named in kebab-case (`my-app`) or camelCase (`myApp`). The package name is `local/<kebab-case>` until published; a library's module is the camelCase form, so it can be imported.

**Layout.** In a project, `src/` holds the modules (`src/orders/` is `import orders;`), the entry module is `src/app/` for an app and `src/<name>/` for a library, and in test builds every `tests/*.test.byn` joins the entry module. Commands given no path use the current directory when it has a `package.byn`. A library has no program, so `bayan build` and `bayan run` on one are usage errors. A file starting with `#!` is a script and compiles alone, and `bayan <file>.byn` runs it.

**Outputs.** In a project, `bayan build`, `run` and `test` write to `out/<mode>/<target>/` (the binary, the test binary `<name>-test`, and the emitted C in `c/` for inspection), and `bayan test` writes `out/test/results.json`, the same JSON that `bayan test --json` prints. `out/` is disposable. `--target` takes `linux-x64`, `linux-arm64`, `macos-x64`, `macos-arm64`, `windows-x64`, `windows-arm64`, `wasm32` or `host`; any target other than the host needs zig. `bayan dist` builds the targets in `dist.targets` (or `--target`, `all` for every one) into `dist/<version>/<target>/`, with `SHA256SUMS` (`sha256sum` format) and `BUILDINFO.json` (package, version, compiler, mode, targets, source commit and dirtiness, a hash of the inputs; no timestamps). A version directory is immutable: `bayan dist` refuses to rebuild one without `--force` and says to bump the version. It refuses a dirty git tree without `--allow-dirty`. `dist/latest/` is a copy (not a symlink) of the highest semantic version present, plus a `VERSION` file. Libraries get distributions once FFI export exists; scripts have none. `bayan clean` removes `out/`, and `dist/` or the cache only when asked.

**Cache.** Compiled programs live in one cache per machine, shared by every project, worktree and process: `$BAYAN_CACHE`, else `%LOCALAPPDATA%\bayan\cache` (Windows), `~/Library/Caches/bayan` (macOS), `$XDG_CACHE_HOME/bayan` or `~/.cache/bayan` (elsewhere). Keys hash the compiler version, mode, target, C compiler, flags and the emitted C, and the emitted C names files relative to the project root, so a second worktree or a moved project compiles nothing. An entry is built in a private directory and renamed into place with no lock; one whose executable no longer matches its recorded SHA-256 is rebuilt. `bayan run script.byn` remembers the program for the script's exact bytes, mode and compiler version and starts it directly next time. `out/` stays per worktree. `bayan cache stats` reports the location, entry count and size; `bayan clean --cache` empties it.

**Version control.** Bayan reads git and never writes history: `bayan new` and `bayan init` run `git init` only when git is installed, `--vcs none` isn't given, and the directory isn't already inside a work tree; they never commit, branch or configure.

### 21.3 Performance targets
Incremental rebuild under 100 ms on a reference project; cold-build budget tracked in CI.

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
| `event` | `Signal<T>` |
| `Count` / `Count()` / `Length` | `length` |
| `m` / `d` literal suffixes | `dec`; doubles unsuffixed |

---

## Appendix A — Settled; do not re-propose

- No `!` shorthand for `fails` (an editor shortcut may expand to `fails`).
- No symbol for `noalloc` / `nosuspend`.
- No Ruby-style `?` suffix on boolean members; `?` means only `T?`, `?.`, `??`, ternary.
- No angle-bracket unit syntax (`5<s>`, `int<Bytes>`).
- No inheritance before a GC tier exists.
- No language-level JSON; JSON is a first-class standard-library feature.
- `Op` suffix; `async`/`await`; `fails`; `.byn`; Bayan.

## Appendix B — Deferred

Isnād registry design · Arabic prose templates · format specifiers, regex API, date/time · SIMD types, inline assembly · variadic generics · `bayan verify` (contract proofs) · JS backend, `bayan gen ts` · hot reload · GC tier and inheritance · effect system for pure/effectful functions · `become` tail calls.
