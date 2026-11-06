# Bayān — Language Specification (draft 0.1)

> **Status:** design complete enough to build v0.1. This document is normative unless a section says otherwise.
> **Source files:** `.byn` · **CLI:** `bayan` · **Manifest:** `package.byn` · **Build script:** `build.byn`
> Code samples use C#-style highlighting because Bayan's surface syntax is C#-derived.

Bayan (بيان, "clarity, eloquent exposition") is a value-oriented, natively compiled language derived from C#. It has no runtime, no garbage collector (v1), and no exceptions. A zero-allocation subset of C# is valid Bayan.

---

## 0. Principles

1. **Performance is a feature.** No hidden costs: allocation, suspension and failure are always visible in the source or the tooling.
2. **Every keystroke carries meaning.** If the compiler can infer it reliably, don't type it; if it carries meaning, type it.
3. **Brief but complete.** One spelling per construct; free composition (the "keyword budget"). Three categories:
   - **Synonyms**, two spellings of one construct, are rejected: one is canonical, the other is an input form with a fix-it (§21.5). Exactly two documented exceptions exist, each from a closed token set, one-to-one in meaning, cost and effects, and normalized per project by `bayan format` from a `style` setting: the boolean spellings (§7.2) and the primitive spellings (§3.1).
   - **Paraphrases**, different compositions of the general grammar with the same meaning (`users except null` / `users.where(it is not null)`), are allowed.
   - **Ellipsis** (ḥadhf), omitting a part recoverable from local context (`it`, inferred types, target-typed `.member`), is allowed.

   Guards against drift: no synonyms in the lexicon; new syntax must have its own job, not re-spell an existing construct; paraphrases must be truly equivalent (same narrowing, cost and effects), and where two forms are not, the difference is a style note with no diagnostic code (`0..=n - 1` vs `0..n`, `p == Point { … }` vs `p is Point { … }`).
4. **Predictable.** Roots plus a closed set of patterns (ishtiqāq, §5).
5. **Elegant by default.** One formatter, one canonical form. Style defaults (`docs/bayan-style.md`) are configurable per project in `package.byn` (§19.2); grammar rules (§5) are not style and cannot be configured away.

> Round 2 §18: style is guidance applied by `bayan format`, configurable per project; "no options" contradicted it.
6. **Kind to the programmer.** Errors explain what, where, why and how to fix (§21).
7. **Instant feedback.** REPL, scripts and incremental builds under 100 ms.
8. **Joy is a requirement.**
9. **Memory safety is never a build-mode option.** Bounds checks and region checks apply in every build.
10. **Promise as little as possible.** Anything specified exactly is permanent; leave room where it costs nothing (§19.4).
11. **Bayan follows its own principles.** C# familiarity is a benefit, never a deciding reason.
12. **Head-first word order** (Arabic). Statements: the verb comes first and ḥāl modifiers follow (`defer failing`). Declarations: the kind comes first and modifiers follow (`struct unique`, `type distinct`, `union error`). Local bindings: the name comes first and the type follows (`let w: Grams`); declarations keep the type first (`Grams weight(Order order)`, §3.10).

> Round 3 §1, §28: "one form, one meaning" banned paraphrase and ellipsis along with synonyms; the new wording bans only synonyms, and head-first order gives every declaration and statement one shape. A third spelling pair would need the same three criteria, so none other qualifies.

---

## 1. The core on one page

| Area | Rule |
|---|---|
| Data | Structs only (value types). No classes, no inheritance, no `object`, no GC in v1. |
| Syntax | Newlines end statements (no `;`). Attributes use `@`. Declaration keywords are head-first; declarations are type-first, local bindings name-first. |
| Strings | `string` is an immutable, validated UTF-8 slice. `rune` is a 32-bit Unicode scalar. |
| Errors | `T f() fails E` in signatures; `Result<T, E>` as a type; `try` / `catch`; panics abort. |
| Effects | `uses alloc, suspend, net, …` on public functions; no `uses` clause means pure. Handlers are supplied with `provide`. |
| Bindings | `let w: Grams = 0g` (immutable, default), `var` (mutable), `const` (compile-time). Parameters are immutable. |
| Parameters | Read-only by default (compiler chooses copy or reference); `ref` mutable borrow; `move` ownership transfer. Required parameters after the object take a particle (`from:`, `to:`, `at:`, …). |
| Typestate | One `states { … }` block in a struct; `Order.paid` is a type; `becomes` / `become` transition. |
| Memory | Stack, arenas, `Owned<T>`, `.rodata`. Allocators flow through `context`. Borrows are region-checked. |
| Naming | Types `PascalCase`; everything else `camelCase`. Casing encodes part of speech (§5). |
| Derivation | A closed set of patterns derives forms (`tryX`, `xInto`, `xOp`, `T.Builder`, …). |
| Generics | C# syntax, constraints checked at definition, monomorphized. |
| Async | Colorless: no `async` on declarations. `async`/`await` at call sites; I/O through `context.Io`. |
| Comptime | Ordinary Bayan run at compile time by the interpreter; deterministic and sandboxed. |
| Modules | A directory is a module. `import`. No cycles. Default visibility: module. |
| Safety | No undefined behavior outside `unsafe`. Taint tracking, `Secret<T>`, capability declarations. |
| Output | Native executables and libraries via C; WASM via clang `wasm32`. |

Hello world:

```csharp
print("Hello, world!")
```

---

## 2. Lexical structure

### 2.1 Source text
- UTF-8, NFC-normalized identifiers. Arabic and other scripts are allowed in identifiers.
- **Bidi control characters (U+202A–U+202E, U+2066–U+2069) are errors outside string literals** (Trojan Source, CVE-2021-42574).
- Lint (off by default): identifiers mixing scripts (homoglyphs such as Cyrillic `а` in `pаypal`).

### 2.2 Comments
- `//` only. `/* */` is an input form, rewritten to `//` lines (BYN1047).
- `///` doc comments are **Markdown**. The first line is the summary (used by `$"{e}"` on errors and by the prose view). Fenced code blocks in doc comments run as tests.

> Round 3 §6: two comment forms were synonyms. Codes: BYN1047.

### 2.3 Literals

| Kind | Examples | Notes |
|---|---|---|
| Integer | `42`, `1_000_000`, `0xFF`, `0b1010` | **Untyped** until context gives a type; out-of-range is a compile error |
| Float | `3.14`, `1e5`, `2.5e-3` | `e` followed by a digit or sign is an exponent |
| Unit-suffixed | `5s`, `250ms`, `4KiB`, `100m` | Suffix must be an imported unit symbol; not allowed on hex/binary literals |
| Built-in suffixes | `u`, `l`, `ul`, `f` | Reserved. C#'s `m`/`d` suffixes are retired; decimal is `1.50dec` |
| Rune | `'a'`, `'\n'`, `'\u{1F600}'` | 32-bit scalar; ASCII rune literals narrow to `u8` in comparisons |
| String | `"text"`, `"""raw multi-line"""` | Stored in `.rodata` |
| Interpolated | `$"total {x}"`, `$"{n, as: .hex}"` | Allocates, except when passed directly to a formatting sink (`print`, `log`, `panic`, `StrBuf.append`); specs are named arguments in the hole (§16.4) |
| Bytes | `b"text"` | Bytes, not text (§16.4) |
| C string | `c"text"` | NUL-terminated, in `.rodata` |
| UTF-16 | `u16"text"` | For Windows `…W` APIs |
| Collection | `[1, 2, 3]`, `[]`, `[..a, ..b, 4]` | Target-typed (§6.3); with no target type, `[1, 2, 3]` is `i32[3]` on the stack |
| Map | `["alice": 500]`, `[]` | Target-typed; with no target type, `["k": "v"]` is `Map<string, string>` |
| Struct | `{ x: 1, y: 2 }` | Target-typed; only where a type is expected |
| Tuple | `(1, "a")`, `(x: 1, y: 2)` | |

> Round 2 §10, §11: field initialization uses `:`; an empty map is `[]` like every empty collection; untyped literals get a default type. Codes: BYN1021, BYN2235, BYN2236.

> Revision 0003 §3, §4: `b"…"` names bytes as bytes, and `$"…"` allocates unless a formatting sink takes it directly, so the allocation is visible. Codes: none.

An integer literal (or a negated one) that initializes an unannotated `var` takes the type of the variable's first constraining use in the same function, in bind order: an assignment to or from a typed value, a comparison or arithmetic with one, or an argument for a typed parameter. `var i = 0` followed by `while i < xs.length { i += 1 }` makes `i` an `i64`. Literals and other such `var`s do not constrain, so `var n = 0` followed by `n += 1` leaves `n` an `i32`. Only an integer type retypes the variable: when the first use is a float or a string, or there is none, it keeps the literal's default type (`i32` when it fits). A literal that does not fit the inferred type is the usual out-of-range error at the literal (`var n = 300` followed by `let b: u8 = n` is BYN2203 on `300`). Bare literals never initialize a unit type or a distinct type (§3.4). `bayan types` shows the resolved types (`docs/typed-dump.md`).

### 2.4 Keywords

**Declarations:** `struct unique union enum open extensible interface extension derivation attribute error unit distinct type effect law import as public private static readonly required const let var embed extern unsafe rest`

The declaration-only keywords `error unit distinct open extensible type effect law derivation attribute embed` are **contextual**: they act as keywords only where a declaration form needs them (`union error E { … }`, `type X = …`) and are names everywhere else (locals, parameters, fields, functions, members, error variants, argument names): `let error = 3` and `struct S { i64 set }` are valid. Every other keyword, including all control-flow and expression keywords, is reserved and cannot be an identifier.

**Control:** `if else unless switch case default for in while do break continue return yield defer using context provide become`

**Errors & effects:** `fails fail try catch never requires uses because`

**Concurrency:** `async await`

**Expressions:** `is and or not except with move ref out this null true false comptime checked`

**Tests:** `test property fuzz model assert`

**Contextual words** (keywords only in one position, names everywhere else): `failing`, `panicking` after `defer` (§7.6); `unlikely`, `never` after `if` (§7.2); `it` inside a lambda argument (§3.16) and a `requires` clause (§7.9); `bench`, `measure` in benchmarks (§20); `states` at the start of a member declaration when followed by `{`, and `becomes` after a parameter list (§3.17); `pattern` after `=` in a `const` declaration (§6.5); the effect names `alloc suspend net fs clock random env process console` inside a `uses` clause (§14). The particles `from to until through at by for as` (§10) are labels, not keywords; `for` and `as` stay reserved and are allowed only in label position (`for:`). The C# primitive names `sbyte byte short ushort int uint long ulong nint nuint float double` are reserved in every project, whatever its `style.primitives` (§3.1).

**Reserved (no meaning yet; may not be used as identifiers):** `actor macro select ensures Gc gc` (`ensures`: v1.x contracts, handoff addendum 002; `Gc`, `gc`: the future GC tier, §9.8; `law`, for v0.2 laws, and `effect`, for effect aliases (§14), left this list for the contextual declaration keywords; `become` left it for typestate, §3.17)

`flags`, `errdefer` and `delegate` are not keywords. `foreach`, `errdefer` and `delegate` in source get a teaching error with a fix (§21.5). `new` is accepted and removed by the formatter. `goto` does not exist.

> Round 2 §1, §6, §7, §8, §16, carried over (GC): `flags`, `errdefer` and `delegate` are gone; `requires` and `assert` are keywords; the new statement words are contextual; `Gc`/`gc` reserved now. Codes: BYN0012, BYN1019, BYN1020, BYN1028.

> Round 3 §2, §4, §7, §13, §14, §24, §28, O6: `set`, `noalloc`, `nosuspend` and `suspends` leave (`union error` absorbs `set`; `uses` replaces the effect words); `uses`, `because`, `except`, `provide`, `become`, `rest` and `fail` (Sūq review A2, §8) join; `states`, `becomes`, `pattern`, `effect` and the effect names are contextual so existing names keep working; the C# primitive names are reserved so no project can use one as an identifier. Codes: BYN6001.

### 2.5 Statement termination (waqf)

There is no `;`. `bayan format` strips trailing semicolons and splits same-line statements; the compiler rejects `;` with that fix-it (BYN1040).

| Waqf mark | Rule |
|---|---|
| مـ lāzim (mandatory stop) | A newline after a complete statement ends it |
| لا lā (forbidden stop) | The statement continues if the line ends with a binary operator, `,`, `=`, `=>` or `{`, or the break is inside `( )` or `[ ]` |
| وصل waṣl (joining) | The next line continues the statement if it starts with `.`, `?.`, `??`, `except`, `requires`, `expects`, `where`, `becomes`, `fails`, `uses`, `catch`, `&&`, `\|\|`, `and` or `or` |
| Never-join | A line starting with `(`, `[` or `-` always begins a new statement |

- **An unused non-void value is a compile error** (BYN3201), so every mis-split line is a diagnostic (`let x = a` newline `-b`). Discard on purpose with `_ = f()`.
- `return`, `break`, `continue` and `fail` take their operand on the same line. A postfix modifier (`if`, `unless`) is on the same line as its statement (BYN1042).
- `else` follows `}` on the same line; `else` on a new line is rejected with a fix-it (BYN1041).
- A waṣl line is indented deeper than the line its statement starts on; one that is not draws a warning that `bayan format` fixes (BYN5103), except `catch` after a line ending in `}` and a struct-level `requires`.
- Comment lines are transparent to continuation. Outside `( )` and `[ ]`, a blank line always ends a statement; inside them, blank lines are ignored like other newlines.
- A struct literal in a control-flow head takes whole-condition parentheses (§7.2).
- **Separators:** declarations (fields, union cases, enum entries, states) are separated by newlines; a signature may continue on the next line with a `where`, `expects`, `becomes`, `fails` or `uses` clause. A one-line `union U { a, b }` is rejected and `bayan format` rewrites it (BYN1053). Entries in literals and argument lists are separated by commas, with trailing commas per `docs/bayan-style.md`.

> Round 3 §3, O1: newline termination removes punctuation that carried no meaning; the unused-value error makes the newline rules safe. Codes: BYN1040, BYN1041, BYN1042, BYN1053, BYN3201.

> Revision 0002 items 1, 25: `expects` joins the clause order (`where` → `expects` → `becomes` → `fails` → `uses`), and `fail` takes its operand like `return`, so it follows the same line rule. Codes: BYN1042, BYN1065.

---

## 3. Types

### 3.1 Primitives

| Canonical (`.sized`) | C# name (`.csharp`) | Notes |
|---|---|---|
| `i8 i16 i32 i64` | `sbyte short int long` | |
| `u8 u16 u32 u64` | `byte ushort uint ulong` | |
| `isize usize` | `nint nuint` | |
| `f32 f64` | `float double` | |
| `bool` | | |
| `rune` | | **32-bit Unicode scalar** (differs from C#); `char` is an input form |
| `string` | | UTF-8 slice, 16 bytes (pointer, length) |
| `never` | | Bottom type; see §3.9 |
| `dec` | | Decimal arithmetic type; `decimal` is not accepted |

C# primitive names are accepted per project (`style.primitives: .sized | .csharp`, default `.sized`, §19.2), the second documented synonym exception (§0). The pairs map one to one; `bool` and `string` are shared. `char` (16-bit in C#; it stays the input form for `rune`), `decimal` and `object` have no equivalent and are not accepted. The compiler accepts both spellings and raises no diagnostic; `bayan format` normalizes each project to its spelling, and tooling (diagnostics, hover, generated docs, `bayan explain`, and `bayan expand` once it lands, §21.2) renders types in the reader's project style. The standard library, this spec's examples and the self-hosted compiler are written in `.sized`. Conversions read the same in both styles: `int(x)`, `int.tryFrom(x)`.

> Round 2 §15, round 3 §28: C# type names were input forms (BYN1030); round 3 makes them a per-project spelling because the mapping is closed, one-to-one and free of cost, and style is never compiler-enforced. Codes: none.

> Revision 0002 item 17: `bayan expand` is deferred until after v0.1, so this text says what it will show. Codes: none.

> Round 3 §10: `char` suggests C#'s 16-bit unit; `rune` names the scalar, and the `Runes` unit and `runeCount` follow it. Codes: BYN1048.

Lowercase type names are reserved for the language. C interop types: `c_int`, `c_long`, `c_char`, … (`c_long` follows the platform).

### 3.2 Strings
- Always **valid UTF-8** and immutable; a string borrows its bytes (a literal, a request body, a slice) or owns them. Decoding is a codec (§16.4): `Utf8.tryDecode(bytes)` returns `string?`; `try Utf8.decode(bytes)` fails `Utf8Error`. There is no `fromUtf8`, and `string(bytes)` formats the bytes (`[104, 105]`), it never decodes.
  - Both take a `ReadOnlySpan<u8>`. Decoding validates without copying: when the input bytes are borrowed, the resulting `string` borrows them too, so decode-to-validate allocates nothing. An empty span decodes to `""`. Overlong forms, surrogates, code points above U+10FFFF and truncated sequences are invalid (law L010).
  - `Utf8Error` is a built-in `union error` with one variant, `invalidSequence`. `try Utf8.decode(bytes)` reports the byte offset of the first invalid sequence through `Utf8Error` (`[0x68, 0xC0, 0x80]` gives 1).
  - `Utf8.decode(bytes, invalid: .replace)` is the lossy form: each invalid sequence becomes U+FFFD, and it never fails. Other text encodings are codecs in `kit` (`try Latin1.decode(bytes)`, spec-only).
- `string(n, as: .hex)` on every integer type returns lowercase hexadecimal with no prefix and no leading zeros (`255` gives `"ff"`, `0` gives `"0"`), allocated from `context.allocator`; `.binary`, `.octal` and `.decimal` (the default) are the other `Radix` cases (§6.2). With `.hex`, a negative value prints its two's-complement bits at the type's width (an `i8` holding `-1` gives `"ff"`). Format specifiers are in §6.6.
- **Bytes to hex or base64 text** are codecs with static `encode` and `decode`, like `Html.escape`: `Hex.encode(digest)`, `try Hex.decode(text)`, `Base64.encode(bytes)`, `try Base64.decode(text)`; `decode` fails with a codec error. `string(x, as: Radix)` is for integers only.
- **Sizes:** `s.length` is the byte length (O(1)); `s.runeCount` counts scalars (O(n)); `s.runes` and `s.graphemes` iterate scalars and grapheme clusters. Both counts are plain `i64` (§3.4).
- **Copying:** `s.copy()` returns an independent copy from `context.allocator` (`uses alloc`); `s.copy(to: arena)` copies into the given allocator, as collections do (§16). It is the fix when a borrowed string must outlive its region (§9.4).
- **No single-index indexer.** `s[i]` is a compile error with fix-its: `s.bytes[i]`, `s.runes.take(n)`, `s.graphemes.take(n)`, range slicing.
- All offsets are **byte offsets**, as ranges are everywhere (§6.1). `s[a..b]` slices by byte offset and panics off a rune boundary. `s.runes.take(n)`, `s.graphemes.take(n)`, `skip`, `takeLast` count runes or graphemes.
- **Searching** returns byte positions or `bool`: `contains`, `startsWith`, `endsWith`, `position(x)` (`i64?`), `lastPosition(x)`, `count(x)` (§16.4).
- **Splitting** is lazy and returns views: `parts(by: ',')`, `parts(by: ", ", limit: 2)`, `lines`.
- **Copies** (`-ed`; new text is allocated from `context.allocator`, so it `uses alloc`): `lowercased()`, `uppercased()`, `replaced(a, by: b)`, `padded(to: n, by: c)`, `repeated(n)`. A sub-range result (`trimmed()`, `parts`, slices) is a borrowed view and never allocates (§16.4).
- **No `+` on strings**, constants included. Concatenate with interpolation (`$"{a}{b}"`), `[a, b, c].joined(by: "")`, `Path.join`, or `StrBuf` in loops. `a + b` on strings is an input form rewritten to interpolation (§21.5).
- `==` is **ordinal**; `a.equals(b, as: .caseless)`; there is no ambient culture, and culture-aware comparison is only through `kit.intl` (`Collator(for: culture)`, §16.4).
- `s.utf16` is a view of UTF-16 code units (`u16`), beside `s.runes` and `s.bytes`; `s.utf16.copy()` returns an owned `List<u16>` (`uses alloc`), and `try Utf16.decode(units)` decodes a `ReadOnlySpan<u16>` (a codec in `std.text`). `string16` exists for UTF-16 interop; nul-terminated UTF-16 for FFI is in §18.
- Raw strings: `"""…"""`.
- Not in v0.1: the `std.text` and `kit` members of §16.4 (`graphemes`, `Collator`, `truncated`, `Base64`, encodings other than UTF-8, …); §16.4 lists the v0.1 scope.

> Round 2 §12: `+` on strings hid allocation. Codes: BYN2201, BYN2209.

> Round 3 §10, deliverable 4, O2: `length` is the one size name everywhere (bytes for strings, the O(1) answer); `runeCount` names the O(n) count. Codes: BYN1048.

> Sūq review A5: one copy verb for strings and collections; `$"{s}"` copied only as a side effect of formatting.

> Round 3 §12, §27, O13, O14: `toHex()` and `toUtf16()` spelled conversions with the `to` particle; a radix is an option of the `string(x)` conversion, UTF-16 is a view like `runes` and `bytes`, decoding uses the A7 constructor forms, and byte-to-text encodings are codecs like `Html.escape`. Codes: BYN2230, BYN5009.

> Revision 0003 §1, §3, §4: decoding is a codec like `Hex`, so `string(x)` means one thing in generic and concrete code; members follow the naming rules of GRAMMAR §1 (`position`, `parts`, `-ed` copies). Codes: BYN2230.

### 3.3 Symbols
`Symbol` is an interned identifier (4-byte ID). Equality and hashing use the ID; **ordering uses the text** (UTF-8 bytes = code-point order). String literals convert to `Symbol` at compile time (interned table in `.rodata`).

**Supported subset (spec 008).**

```csharp
struct MemberKey {
    i32 owner
    Symbol name
}

Symbol readName(string src, from i64 start, until i64 end) uses alloc {
    return Symbol.intern(src[start..end])
}

void main() {
    let keywords: Map<Symbol, i32> = ["let": 1, "var": 2]
    var members: Map<MemberKey, i32> = []
    let name = readName("let x", from: 0, until: 3)
    if keywords.contains(name) {
        members[MemberKey { owner: 1, name: name }] = keywords[name]
    }
    for (key, value) in members {
        print($"{key.name} {value}")      // let 1
    }
}
```

- **The type:** `Symbol` is a built-in 4-byte value, copyable (not `unique`) and not a borrow. It is defaultable; the default is the symbol of `""`. It displays as `Symbol` in diagnostics and `bayan types`. A user type named `Symbol` does not replace it: the name still resolves to the built-in, as `StrBuf` does.
- **Comparison:** `==` and `!=` compare IDs, in O(1). `<`, `<=`, `>` and `>=` compare the texts by UTF-8 bytes, as for `string`, so the order does not depend on the order of interning.
- **Literals:** a string literal without interpolation (plain or raw) where `Symbol` is expected (a typed `let`/`var`/`const`, an argument, a `return`, a field initializer, a collection or map literal element or key, the other operand of a comparison with a `Symbol`) is a `Symbol` fixed at compile time. The program holds one table of the distinct literal texts in `.rodata`, sorted by UTF-8 bytes with `""` first. Any other `string` where a `Symbol` is expected (a local, a parameter, a slice, an interpolated string, a call result, also through `Symbol?` or a generic argument) is BYN2832 with a fix to `Symbol.intern(…)`.
- **Interning:** `Symbol Symbol.intern(string text)` returns the symbol for `text`; a text equal to a literal's gets the literal's symbol. A new text is copied into storage the runtime owns until the program exits, so the result borrows nothing. Interning allocates from the runtime's general allocator, never `context.allocator`: `Symbol.intern` is `uses alloc` (BYN6002 on a public caller without it), it is restricted in a panic handler (BYN2612), and the table is never freed, so a program that interns untrusted text keeps it for the whole run.
- **Text:** `sym.text` is a `string` that borrows nothing and allocates nothing; `$"{sym}"` writes the text, and a `Map` holding symbols formats them as their texts. There is no other conversion: no cast between `Symbol` and `string` or an integer, and no ID member (BYN2003). IDs are not observable, so they may change between versions.
- **Keys:** a `Symbol` hashes its ID; `Map<Symbol, V>` and composite keys with `Symbol` parts are §3.5's keys. A program that uses no `Symbol` emits and links none of the table.
- Not in v0.1: freeing interned text, several independent tables, `const` arrays of `Symbol` and `Symbol` in static data beyond literals (BYN2229), interning from several threads (the table is single-threaded, §13).
- Laws: L015 "Interning is identity on text". Codes: BYN2832. HANDOFF Questions 371–381.

**Self-hosting gaps** (what spec 009's port must work around):
- No `const` symbol arrays: a keyword table is a `Map<Symbol, TokenKind>` literal built at start-up.
- No `Set<T>`: a set of names is a `Map<Symbol, bool>`.
- No stored lambdas keyed by symbol (`Map<Symbol, () -> void>` is BYN2822, §3.16): key a `Map<Symbol, i32>` and switch on the value.
- A lambda cannot capture a `Map` (BYN2780): pass the symbol table as a parameter.

### 3.4 Units and distinct types

```csharp
unit Seconds(s): i64             // name, literal symbol, storage
unit Millis(ms) = Seconds / 1000
unit Bytes(B): i64
unit KiB = 1024 B
unit Cents: i64                  // no symbol
unit Speed = Meters / Seconds

type distinct UserId = i64       // a unit without arithmetic
```

- The unit name is the type; the symbol is the literal suffix.
- Mismatched units are compile errors. Conversion is implicit **only toward finer units** (`2s + 500ms` is `Millis 2500`). Converting to a coarser unit is explicit and names the rounding with a type argument: `t.floor<Seconds>()`, `t.ceiling<Seconds>()`, `t.rounded<Seconds>()` (nearest, ties away from zero). A typed target fills the argument in: `let billed: Minutes = duration.ceiling()`. An implicit coarser conversion is BYN3205.
- Wrap and unwrap with conversions: `Seconds(raw)`, `i64(t)` (§6.2). **A bare literal never takes a unit, `0` included:** `let w: Grams = 0` is an error with a fix-it to `0g` (BYN3203). Likewise for distinct types: `let id: ParcelId = 0` is an error; write `ParcelId(0)` (BYN3204).
- A unit formats as its value plus its symbol (`524288 B`); `size.rounded<KiB>()` converts for display (`512 KiB`). There is no separate `convert` member.
- Quantities are dimensionless: `i32 qty`, and `i32 × Grams → Grams`.
- Units are erased at runtime.
- Counts are plain `i64`: `s.length`, `s.runeCount` and collection `length`. The `Bytes` and `Runes` units apply only to slice offsets.
- Units do not model money: `Money` is a struct with a currency field. Adding mixed currencies traps like integer overflow (`currency mismatch`); `tryAdd` and `add(…) fails` exist for code that expects it. `price.rounded()` rounds to the currency's minor unit.
- Real uses of the roundings: billing per started minute (`ceiling`), carrier weight brackets per started kilogram (`weight.ceiling<Kilograms>()`), a `Retry-After` header in whole seconds (`wait.ceiling<Seconds>()`, never early), and countdown display (`elapsed.floor<Seconds>()`).

> Round 3 §2, §8, O2, O7, O9: head-first `type distinct`; a bare `0` hid which unit was meant; implicit coarsening lost precision silently, and a type argument names the rounding where `round(to:)` took a value; counts stay plain so `requires it.runeCount in 8..=128` needs no unit literal. Codes: BYN1044, BYN3203, BYN3204, BYN3205.

### 3.5 Arrays, spans, collections
- `T[N]`: fixed-size inline array (a value). Constant out-of-range indexes are compile errors.
- `Span<T>`: a borrowed view (pointer, length) whose elements may be assigned.
- `ReadOnlySpan<T>`: a borrowed view whose elements cannot be assigned. `Span<T>` → `ReadOnlySpan<T>` is implicit. `string.bytes` is a `ReadOnlySpan<u8>`, so strings stay immutable and valid UTF-8. A `Span<T>` parameter never written through gets lint BYN2310 ("use `ReadOnlySpan<T>`").
- `List<T>`, `Deque<T>`, `Map<K, V>`, `Set<T>`, `Pool<T>`, `Ring<T, const i32 N>`, `NonEmpty<T>`: standard library, allocator-backed (API: §16).
- `Map` **preserves insertion order** and is the only hash map. `HashMap<K, V>` is BYN2830 with a fix to `Map<K, V>`, wherever a type is written; the type is then read as `Map<K, V>`, so it is reported once (spec 008). An unordered `HashMap` would iterate in seed order; whether to add one later is HANDOFF Question 371.
- **`Map` keys:** an integer type, `rune`, `bool`, `string`, a plain enum, `Symbol` (§3.3), a tuple whose elements are all keys, or a struct whose fields are all keys and which is not `@noEquality`. Two keys are equal when `==` says so (§6.7). A struct or tuple that is not a key is BYN2831, naming the first field or element that rules it out and its type; any other key type (a float, an optional, an enum with data, a collection) is BYN2760. A type parameter used as a key is checked at each instantiation. A key hashes its value only: a `Symbol` its ID, a `string` its text, a tuple or struct its parts in declaration order, never padding bytes, so two `==` keys built apart are one entry.
- `xs.remove(from: start, until: end)` removes the elements in `[start, end)` and keeps the order of the rest; `xs.remove(from: start, through: end)` includes `end`; `xs.remove(at: range)` takes a range. `start > end` or `end > xs.length` panics (`slice out of range`). Like `append` and `removeLast`, they are structural modifications (§9.4 rule 5).
- A module-level `const T[N]` with literal or constant elements is static data in `.rodata` when `T` is a number type, `bool`, `rune` or `string` (`const names: string[2] = ["a", "b"]`); it can be indexed, iterated and asked its `.length`. Any other module-level `const` array is BYN2229.
- **Typed indices (opt-in).** There are no automatic `T.Count` or `T.Index` types. A collection may take an index type as a second parameter, keyed by the collection rather than the element type, so parallel arrays share one:

  ```csharp
  type distinct RowId = Index

  let names: List<string, RowId> = …
  let prices: List<Money, RowId> = …
  for i in names.indices {       // i: RowId
      print(names[i], prices[i])
  }
  ```

  Plain `List<T>` keeps plain `Index`, and `0..xs.length` works as before. Typed collections expose `.indices`; `xs[^1]` resolves against the collection's own index type. Indexing with another index type is an error (BYN3210).

> Round 3 §9: typed indices catch mixed-up indices in parallel arrays without imposing a type per element type. Codes: BYN3210.
- **`StrBuf`** is the string builder and the target of every `xInto(ref StrBuf)` (§5.2): `StrBuf(capacity: n)` allocates from `context.allocator`; `StrBuf.over(stackalloc u8[n] uninit)` uses the stack first and spills to the allocator only if outgrown; `append(x)`, `append($"…")` (interpolation writes straight into the buffer), `appendRepeated(c, by: n)`; `buf.view` is a borrowed string view, no allocation; `buf.finish()` returns the text as a loan on the buffer, without copying (revision 0018). The buffer stays usable: `append` and `appendRepeated` keep text taken earlier valid, because growth moves to a fresh block and leaves the old one. While a string from `view` or `finish()` is live, `clear` and `insert` (BYN3004) and `take` (BYN3006) are refused. `StrBuf.over` is not in v0.1.

> Round 2 §11, §12, §15: `append` replaces `add` on ordered collections; `finish()` and `view` replace `asString()`; C# type names are accepted in type positions (BYN1030 is retired). Codes: none. Round 3 deliverable 4 restores `length` as the size name.
- Every allocator-backed collection (`List`, `Map`, `Set`, `Pool`) and `StrBuf` is **`unique`** (§3.11): assigning or storing one moves it, and a copy is explicit, `xs.copy(to: alloc)`. Reading without owning goes through `ReadOnlySpan<T>`, a read-only parameter or `ref`.
- Hashing is **seeded** by default (hash-flooding resistance); tests use a fixed seed.

### 3.6 Optionals
`T?` for any type. References are non-null; `null` is the literal for "none" of an optional. `??`, `?.`, `is null` / `is not null` (with narrowing, §7.2) and `?? return/break/continue` apply. A call's non-null result is bound with `let order = db.find(id) ?? return .notFound`, not `is { } order`.

> Round 2 §4: one null-check spelling; `x == null` is an input form. Codes: BYN2027.

### 3.7 Tuples
C# value tuples with named elements and deconstruction: `(Cents share, i32 extra)`.

> Round 2 §15: C# type names are accepted in type positions (BYN1030 is retired). Codes: none.

### 3.8 Results
`Result<T, E>` is a standard-library union (§3.12): a tag plus the larger of the value and the error payload, inline. `fails` desugars onto `Result`. Every error variant in a program has a unique compile-time number, so merging error sets is free.

> Round 2 §1: `Result` and `never` are stdlib unions (not in v0.1).

### 3.9 `never`
The empty union in the standard library. Returned by `panic`, `fatal`, `exit` and infinite loops. A `never` expression fits any position (`catch`, `??`, switch arms); code after it is reported unreachable. `fatal` and `exit` are linted as errors in packages that are not `kind: .app`.

> Round 2 §1, §10: `never` is the empty union; manifest fields use `:`.

### 3.10 Structs

```csharp
/// An order placed by a buyer.
@derive(Id, Builder, Patch)
public struct Order : IEquatable<Order>, IJsonEncode {
    public required BuyerId buyer
    public OrderStatus status = .submitted
    List<OrderItem> items                        // module-visible

    public Money total => items.sumBy(it.price * it.units)   // properties take no `uses` clause
    public bool isEmpty => items.isEmpty

    public Order(BuyerId buyer) {
        this.buyer = buyer
        items = List<OrderItem>()
    }

    public void ship(ref this) fails OrderError {
        fail .alreadyShipped if status is .shipped
        fail .empty if isEmpty
        status = .shipped
    }
}
```

- Fields, the `states` block (§3.17), events, properties, constructors, methods, operators, indexers, static members. The order is the style order (`docs/bayan-style.md`).
- **C# mapping:**

  | C# | Bayan |
  |---|---|
  | Auto-property `{ get; }` | Field |
  | Computed property | `Type name => expr` or `Type name { … }` |
  | Delegate, `Func<>`, `Action<>` | Function type, usually through an alias (`type Pricer = Line -> Money uses net`) |
  | `event` | `Event<T>` field (§16) |
  | Interface property `{ get; }` | Bare `Type name` (§3.15) |

- **Field visibility (O11).** A public field is readable anywhere. A field of a struct that has a `requires` rule, a `states` block or the `@sealed` attribute (§4.4) is writable only from the struct's own `ref this` methods or through `with` (§6.4), which re-checks `requires`. This matches `{ get; private set; }` and keeps `requires` and typestate invariants in one place. Writing it elsewhere is BYN3216, whose message names the reason. A plain data struct allows direct field writes everywhere (`modes[depth].braceDepth += 1`).
- **A stored function field is named with a noun for its role** (`handler`, `pricer`, `formatter`), never `onX`. Events are past participles or adjectives (`placed`, `low`, §16). BYN5010.
- **Type first.** Fields, parameters and return types put the type first (`Sku sku`, `Grams weight(Order order)`); `void` marks a function that returns nothing; visibility (`public`, `static`) leads the whole declaration. A computed property takes a type like a field: `public bool isEmpty => lines.isEmpty`. Union and state payloads follow the same order (`timeout(Millis after)`). The name-first forms `f() -> T`, `f(x: T)`, field `x: T` and payload `case(x: T)` are input forms (BYN1043). Local bindings are the exception: they put the name first (§7.1).
- **The receiver.** `this` is the receiver parameter's name; its type is the enclosing struct and is elided. Not written: read-only, any state. `this is .shipped`: read-only, only in that state (§3.17). `ref this`: mutating, any state. `ref this is .placed`: mutating, only in that state.
- **Members are read-only by default.** A member that modifies `this` declares `ref this` as its first parameter (Form V, §5.2); see §10 for the call-site rules.
- `required` fields must be set at construction. Definite assignment as in C#.
- A type is **defaultable** only if every field has a default and the type is not `@noDefault`.
- Equality, hashing and debug formatting are derived automatically for every struct whose fields support them (override with `@equality(by: .id)`; opt out of `==` with `@noEquality`, §6.7).
- Structs have **no default `format()`**; see §6.6.

> Round 2 §9, §2, §13: mutation is declared with `ref this`, not inferred; `@noEquality` is the opt-out; display is never implicit. Codes: BYN2115, BYN2117.

> Round 3 §2, revised 2026-10-22: names first was meant only for local bindings (§7.1), where `let`/`var` lead; declarations keep round 2's type-first order. Codes: BYN1043.

> Round 3 §26, §12, O11: a field-shaped member needs no `{ get }`, events are `Event<T>` fields named for what happened, and a public field writable from anywhere would bypass `requires` and state checks. Revision 0007: plain data has no invariant to protect, so only structs with `requires`, states or `@sealed` restrict writes. Codes: BYN3216, BYN5010.
- `embed T` promotes the embedded struct's fields and methods; the embedded value sits at offset 0.
- No implementation inheritance (revisit only if a GC tier is added).

### 3.11 Move-only types
`struct unique` cannot be copied; use `move`. Cleanup is derived (disposes fields in reverse declaration order) and runs exactly once, at scope end or by the final owner.

- `unique` is for resources (`File`, `Socket`, `Transaction`, `Lock`). Plain data types (orders, lines) are never declared `unique`. `unique` is unrelated to `const`, and a `unique` type cannot be `const` (BYN3209).

- **A type with a `unique` part is `unique`.** A struct with a `unique` field, a union with a `unique` payload, and a tuple, array or optional of a `unique` type are all `unique`, without being declared so. A struct holding a `List` or a `SecretKey` is therefore move-only. The explicit `struct unique` stays for raw resources, whose fields are not `unique` themselves (a C handle, a file descriptor). A struct holding a mutable reference (`Span<T>`, a `ref` field) is `unique` the same way (§9.4); one holding only read-only references (`ReadOnlySpan<T>`) copies freely.
- Owning values never copy implicitly. An implicit copy of a `unique` value is a compile error (BYN2780) whose fixes are `move x`, `x.copy(to: alloc)` (when the type has one) or passing it by `ref`.

> Round 3 §2: head-first `struct unique`; `unique` is the move-only marker, kept apart from `const`. Codes: BYN1044, BYN3209.

> Sūq review A7 (F-12, F-14): the inference already implemented for collections extends to every `unique` field; copies are always explicit. Codes: BYN2780.

### 3.12 Unions

```csharp
union Shape {
    circle(f32 r)
    rect(f32 w, f32 h)
}
union PaymentResult {
    approved(string transactionId)
    declined
    pending
}
union OrderStatus {
    pending
    paid

    bool isFinal => this is .paid
}
union Mode {
    read = 1
    write = 2
}
```

- `union` and `union error` share one grammar; `error` only allows use in `fails` clauses and `catch switch` (§8).
- Case names are **camelCase**. A payload-free union is the enum form; cases may carry data.
- Cases are separated by newlines; the one-line form is rejected (BYN1053, §2.5).
- **Payloads are named in the declaration** (`percentOff(Percent rate)`), so whole-value binding (§6.5) can read them (`p.rate`). Construction may omit the label when the case has exactly one payload (`.percentOff(10%)`); with several, labels are required (`.buyXGetY(buy: 2, free: 1)`). A payload without a name is BYN1064.
- Members may follow the cases (not in v0.1).
- **Closed:** no integer casts. Outside data enters only through `T.tryParse(text)`, which fails on unknown input (not in v0.1). `.name` gives the case name.
- Payload-free cases may declare explicit integer values for C interop (`read = 1`). A value must be an integer constant, only on a payload-free case, and unique in the union.
- `switch` on a union is exhaustive; a missing case is an error naming the case. A `_` arm on a union declared in the same package is a lint warning (it hides future cases).
- Layout: tag plus largest payload, inline, no heap, no boxing.
- `Result<T, E>` and `never` are standard-library unions (§3.8, §3.9).

> Round 2 §1: unions are the one closed sum type; camelCase cases match values; explicit values exist only for C interop. Codes: BYN2410, BYN2709, BYN2710, BYN2711, BYN2712.

### 3.13 Enums (Java-style)

```csharp
public enum Planet(f64 mass, f64 radius) {
    mercury(3.303e23, 2.4397e6)
    earth(5.976e24, 6.37814e6)

    public f64 surfaceGravity => G * mass / (radius * radius)
}
```

- A value is a small integer tag; associated data lives in `.rodata` tables.
- Constant-specific bodies compile to jump tables.
- Generated: `length`, `values`, `name`, `parse` (perfect hash), `fromOrdinal` (returns `T?`), `ordinal`.
- `EnumMap<T, V>` (inline array).
- Associated data must be constant.
- `enum open` allows unknown values (C interop only). Public enums and error sets are closed unless marked `extensible`, which forces callers to include a fallback case.
- No cast from integer to enum.
- **No flags construct.** `Set<T>` of a payload-free union with at most 64 cases is guaranteed to be a single bitmask. `Set` supports `|`, `&`, `-`, `contains`, `containsAll`, `.bits` (`u64`) and `T.tryFromBits(bits)` (fails on bits matching no case). Not in v0.1.

```csharp
union Permission {
    read
    write
    delete
    admin
}
let editor: Set<Permission> = [.read, .write]
return .forbidden unless user.grants.containsAll(editor)
```

> Round 2 §1: a bitmask `Set<T>` covers flags with one construct, so `flags enum` and `EnumSet<T>` are removed.

### 3.14 Errors
`union error` declares an error set, with or without payloads, and composes or narrows other sets. See §8.

```csharp
union error CarrierError {
    timeout(Millis after)
    rejected(string reason)
}
union error StockError {
    outOfStock(Sku sku)
}
union error ShipError = CarrierError, StockError          // composed
union error Retryable = CarrierError except .rejected     // narrowed
```

- Composed sets are **flattened**: `ShipError` has cases `timeout`, `rejected` and `outOfStock`. A component error converts implicitly to the composed set.
- Two components sharing a case name is a compile error (BYN3202).
- Different instantiations of one generic error set compose: `ValidationError<SignupForm>` and `ValidationError<Address>` are distinct components, matched by type (`ValidationError<SignupForm> e => …`). A bare case arm (`.invalid e`) is accepted when only one instantiation is in the set, and is ambiguous (BYN2227) otherwise.
- Generic error sets compose the same way: `union error DecodeError<T> = JsonError, ValidationError<T>` (§7.9).
- `error enum` and `error union` are input forms for `union error` (BYN1044); `error set S = A | B` is the round-2 input form (BYN1029).

> Round 2 §5: commas everywhere a list of errors appears. Codes: BYN1029.

> Round 3 §2, §7: one head-first declaration covers defining, composing and narrowing error sets, so `set` is gone. Codes: BYN1044, BYN3202.

### 3.15 Interfaces

```csharp
interface IShape {
    f32 area()
    string describe() => $"shape with area {area()}"   // default method
}
interface IParse<TSelf> where TSelf : IParse<TSelf> {
    static abstract TSelf parse(string s) fails ParseError
}
interface IEnumerator {
    type Item                                  // associated type
    bool moveNext(ref this)
    Item current                               // read-only property, returned by value
}
```

- Interfaces declare mutating members with `ref this`, like structs (§3.10); implementations must match.
- An interface method's `uses` clause is an upper bound; implementations may use fewer effects (§14).
- **Property requirements are bare `Type name`.** Interfaces have no fields, so a field-shaped member is a read-only property requirement, satisfied by a field or a computed property. `Type name { get }` is an input form (BYN1060). Settable property requirements are not part of round 3; mutation goes through a `ref this` method.
- **Static members and `Self`.** Interfaces may declare static members and use `Self` for the implementing type; generic code calls them as `T.fromRow(…)`.
- **Default methods** (bodies) are how C# extension-method families are written (§16).

> Round 2 §9: `ref this` is part of an interface member's signature. Codes: BYN2117.

> Round 3 §26: a bare `Type name` says "read-only property" without C#'s `{ get }` ceremony. Codes: BYN1060.

- Nominal, explicit conformance (`struct Circle : IShape`). Interface names take an `I` prefix.
- Used as constraints (static dispatch) by default; `dyn ref IShape` / `dyn IShape` for dynamic dispatch through a fat pointer (no allocation).
- Interfaces with generic methods or `static abstract` members cannot be used with `dyn` (checked, with explanation).
- Retroactive conformance (`extension Money : IJsonEncode`) only when the module owns the type or the interface.

**Supported subset (spec 006).**
- `current` and `get` return `Item` by value; a `for … in` loop variable is a `let` copy. `unique` items are BYN2022 until ref-returning getters exist.
- Static dispatch through `where` constraints (`where T : I, J`), monomorphized.
- `dyn ref I` and `dyn ref readonly I`; the latter permits only members without `ref this` (BYN2813).
- Default methods, cloned per conformer.
- Associated types, `type Item;` without bounds; a conformer binds it with `type Item = T;`.
- Conformance by structs only; unions and enums are BYN2815.
- Dyn-safe interfaces have no generic methods and no associated types; using an interface name as a plain type is BYN2816.
- `for … in` over user `IIndexable` and `IEnumerator` conformers. Codes: BYN2800–BYN2816.
- Deferred: retroactive conformance, `static abstract` members, generic interface methods called through constraints.

**Self-hosting gaps** (what spec 009's port must work around):
- No heterogeneous child lists: no `dyn I` by value and no borrowed fields means no `List<dyn ref I>`; the tree is unions plus `Owned`, visited by `switch`.
- By-value `current`/`get`: user iterables cannot yield `unique` items, so node lists are index loops, not `for … in`.
- No generic interface methods: no `T accept<T>(…)` visitor; visitors are `dyn ref IVisitor` with one non-generic `visit` per node kind, or `switch`.
- No interface inheritance: shared members are repeated per interface or reached through a default.
- No union/enum conformance until union members land: a union that must conform is wrapped in a struct.
- No borrowed enumerators: an `IEnumerator` either owns its data or a collection conforms to `IIndexable`.
- No associated-type bounds: `type Item;` only.

### 3.16 Lambdas and function types

```csharp
users.where(it.isActive)
lines.sumBy(it.price * it.qty)
xs.select(x => x * 2)
pairs.select((a, b) => a + b)
type Predicate<T> = T -> bool
type OrderFilter = Order -> bool
```

- **Lambda forms:** `x => x * 2`, `(a, b) => a + b`, block bodies, and `it` for a one-parameter lambda (`select(toDto(it))`). One parameter takes no parentheses, typed or not (`t => …`, `Token t => …`); two or more take them, and typed parameters are type-first (`(Token a, Token b) => …`). A typed single parameter with a generic type (`List<i32> xs => …`) is told from `<` by lookahead. `it`, `t =>` and `Token t =>` are ellipsis levels of one construct. `it` inside a lambda that already uses `it` is an error; name one lambda.
- `.member` has one meaning: a member of the **expected type** (`return .notFound`, `color: .red`). It is never lambda shorthand.
- **One function-type syntax** replaces `Func`/`Action`/`delegate`: `i32 -> i32`, `(i32, i32) -> i32`, `User -> void`, `() -> void`, `() -> T fails DbError`, `Order -> void uses net`. Aliases use `type`. A function type followed by a name is parenthesized, in fields and parameters: `(Line -> Money uses net) pricer`, `by (Token -> bool) p`; an alias avoids the parentheses (`type Pricer = Line -> Money uses net`, then `Pricer pricer`). `delegate` is a teaching error pointing to a function type.
- `->` marks the return in a function type; a declaration puts its return type first (§3.10), so `->` after a declaration's parameters is an input form (BYN1043). `=>` means "evaluates to" (lambdas, expression bodies, switch arms).
- A stored function type's `uses` clause is an upper bound (§14). A closure parameter's effects flow to the caller: `map` inherits its closure's effects, so there is no `tryMap` or `mapAsync`.
- Lambdas passed as parameters are monomorphized and inlined; captures borrow; zero allocation.
- A stored lambda is non-capturing (a function pointer) or an explicit `dyn (A -> B)` allocated from `context.allocator`.
- `move () => …` moves captures in. Capturing a `var` follows the borrow rules (§9.4).
- No multicast delegates; `Event<T>` replaces `event` (§16).
- v0.1 ships the subset below (spec 007); the rest of this section is later.

> Round 2 §8: one function-type syntax and one shorthand (`it`); `.member` keeps a single meaning; storage cost is explicit. Codes: BYN1028, BYN2029.

> Round 3 §13, O5: round-2 function types used `=>`, which collided with the lambda arrow; `->` becomes the function-type arrow and `=>` keeps "evaluates to". Declarations return type-first. `A => B` as a type is an input form. Codes: BYN1055.

**Supported subset (spec 007).**

```csharp
type Pred = i64 -> bool

bool any(ReadOnlySpan<i64> xs, by Pred p) {
    for x in xs {
        if p(x) {
            return true
        }
    }
    return false
}

i64 apply(i64 x, by (i64 -> i64) f) => f(x)

bool isBig(i64 x) => x > 2

void main() {
    let xs: Span<i64> = [1, 2, 3]
    let base = 10
    print($"{any(xs, by: it > 2)} {any(xs, by: x => x > 5)} {any(xs, by: isBig)}")   // true false true
    print($"{apply(1, by: x => x + base)}")                                          // 11
}
```

- **Function types** `A -> B`, `(A, B) -> C`, `() -> void`, with `fails E, F` and then `uses e, f` after the return type. `->` is right-associative; `?` and `[N]` bind to the type they follow (`A -> B?` returns `B?`). Two function types are the same type when parameters, return, error sets and effects are equal; there is no variance. A type displays in source form, error sets with their type arguments (`() -> Form fails ValidationError<Form>`); a bare `ValidationError` is BYN2023 and an unknown effect BYN6003.
- **Placement:** a parameter type (parenthesized before the name, `by (Token -> bool) p`, or through an alias), a `type` alias target and a local's type (`let f: Pred = isBig`). A field, property, return type, constant, union or error payload, type argument, or an optional, span, list, map, array or tuple of a function type is BYN2822, so the stored `(Line -> Money uses net) pricer` above is not in v0.1. Aliases take no type parameters: `type Predicate<T>` is later; spell one alias per type.
- **Named functions as values:** a module-level, non-generic function whose signature matches exactly. Particles are part of the name, not the type: `i32 clamp(i32 x, to i32 max)` is a `(i32, i32) -> i32`. A built-in, generic function, method, constructor or variant named as a value is BYN2824 (wrap it in a lambda); a signature mismatch is BYN2202 showing both signatures. `Panic.handler` keeps its own rule (§7.6).
- **Lambdas** are `x => …`, `(a, b) => …` or `() => …` with an expression or block body. Parameters are names only; a typed, modified or particle-labelled parameter is BYN1001. A lambda takes its parameter types, return type and error sets from the function type expected where it appears: none expected is BYN2218, a parameter count that differs is BYN2820. `return`, `break`, `continue` and `defer` in the body are the lambda's own; a non-`void` expression body where `void` is returned is BYN3201.
- **`it`:** an argument that mentions `it`, where a one-parameter function type is expected, is a lambda with the parameter `it`. A second `it` shorthand inside it, or one inside a `requires` rule (where `it` is the checked value), is BYN2029; name one lambda. `.member` where a function type is expected is BYN2823 with a fix to `it.member`.
- **Captures are by copy**, not by borrow: a lambda may read the enclosing `let`, `const`, `for`, pattern and `catch` bindings and parameters without a modifier, copied when the lambda is created. A `var`, a `var` pattern binding, a `ref`/`out`/`in` parameter and `this` (explicit or through a field) are BYN2821; a `unique` value is BYN2780. A captured borrow keeps its source borrowed while the lambda lives (BYN3004 on a structural change).
- **Lifetime:** a function value is a borrow of the frame that made it: a local, an argument or a callee, never stored or returned. A capturing lambda held into the next loop iteration is BYN3002; one returned from a lambda is BYN3003. The value is a code and environment pointer pair; the environment lives in the creating frame, and nothing is allocated.
- **Calls through a value** are positional; a named or particle-labelled argument is the missing-parameter diagnostic. A call through a `fails` type is handled with `try` or `catch` like any failing call (BYN2602).
- **Effects:** a function-typed parameter is effect-polymorphic: a call through it adds nothing to the function that declares it, and the call that passes a function or lambda carries that argument's effects (BYN6002 on a public caller that does not declare them). A function-typed local's `uses` is an upper bound: a function or lambda beyond it is BYN6005, and a call through the local has the declared effects. Function types match exactly, so a local with a `uses` bound passed to a parameter without one is BYN2202.
- **Input forms:** `A => B` in a type is BYN1055 with a fix to `A -> B`; `delegate bool F(Order o)` is BYN1028 with the fix `type F = Order -> bool`. `bayan format` applies both.
- Not in v0.1: generic aliases, typed lambda parameters, `ref` captures of `var`, `move () => …` (BYN2782), function values that outlive their frame, `dyn (A -> B)`, built-in collection methods that take a lambda (`where`, `select`, `sumBy`; §16), effect variables.
- Laws: L013 "Direct and indirect calls agree", L014 "Capture by copy never aliases the source". Codes: BYN1028, BYN1055, BYN2029, BYN2820–BYN2824. HANDOFF Questions 345–369.

**Self-hosting gaps** (what spec 009's port must work around):
- No stored callbacks: a visitor table, a `Dictionary<TokenKind, Func<…>>` or a parser rule table becomes a `switch` or a function-typed parameter threaded through the calls.
- No function-typed fields or return types: no combinator style; helpers take their callback as a parameter on each call.
- No `ref` captures: a lambda that must update a counter becomes a `ref` parameter of the called function, or the loop is written out.
- No built-in `where`/`select`/`sort(by:)`: the port writes `any`, `indexWhere` and a sort over spans once in its own prelude.
- No generic aliases: `type Predicate<T>` is spelled per type (`type TokenPred = Token -> bool`).
- No effect variables: a function-typed local that may use an effect declares it (`let hook: string -> string uses fs = …`); parameters need nothing.
- No lambdas in `const` initializers and no function values in static data: keyword tables stay arrays of names, not of handlers.
- No `this` capture: a method that passes a lambda copies the fields it needs into `let` values first.

### 3.17 Typestate (kāna / ṣāra)

States are declared in one `states` block inside the struct, after the fields:

```csharp
@derive(Json)                  // not Row: `lines` is not flat (§5.3)
struct Order {
    OrderId id
    List<Line> lines

    states {
        draft
        placed
        paid(PaymentId payment)
        shipped(PaymentId payment, TrackingId tracking)
        cancelled(string reason)
    }
}

type Fulfilled = Order is .paid or .shipped

public void pay(ref this is .placed, Payment p) becomes .paid fails PaymentError uses net {
    let id = try p.charge(total)
    become .paid(payment: id)
}

public Url trackingUrl(this is .shipped) => carrierUrl(tracking)      // `tracking` is a payload field
public Label label(Order.shipped order) { … }
public Url receiptUrl(Order order is .paid or .shipped) { … }
public Order.shipped ship(move Order.paid order, by Carrier carrier)
    fails CarrierError uses net { … }
```

1. `T.s` is the type of `T` values in state `s`. It is valid anywhere a type is: parameters, returns, fields, locals and generics (`List<Order.paid>`). Passing a value not known to be in that state is an error (BYN7001).
2. Several states: `Type name is .a or .b` inline in a parameter or field, `let x: T is .a or .b` in a local, or an alias (`type Fulfilled = Order is .paid or .shipped`). Return types use `T.s` or an alias.
3. The receiver keeps its own form: `this is .s`, `ref this is .s`, `move this is .s`, and the same with `or` (§3.10, §10).
4. Widening is implicit and free: `Order.paid` → `Fulfilled` → `Order`. Narrowing happens only through `is`, `switch`, or a `becomes` call on a `var`. A state changes in place only through `become`, or by consuming a value and returning a new one (`move Order.paid` → `Order.shipped`); so a call to a method without `becomes` leaves what the caller knows unchanged.
5. Narrowing results: `if o is .paid { … }` gives `Order.paid`; `.paid p => …` binds `p: Order.paid`.
6. A payload field is readable only when the type guarantees its state (BYN7002). With several states, a field is readable if every listed state has it with the same type (`payment` is readable on `Fulfilled`, `tracking` is not). Payloads name their fields in the declaration (BYN1064, §3.12).
7. Literals name a state: `Order.paid { id: i, lines: l, payment: p }`. A plain `Order { … }` on a struct with states is an error (BYN7005). An unknown state is BYN7007.
8. Runtime: `T.s` has the same layout as `T` (a `u8` tag plus a payload union) and is erased in C. Widening is a no-op; the checks are compile-time only.
9. Canonical spelling: one state is always written `T.s`. `bayan format` rewrites `Order order is .paid` → `Order.paid order` and `type X = Order is .paid` → `type X = Order.paid` (BYN7006). The receiver is exempt.
10. State names are camelCase and may not equal a static member name of `T`, otherwise `Order.empty` would name both a type and a factory (BYN7008).
11. `becomes .state` declares a transition; `become .state(…)` performs it. Only a `ref this` method may declare `becomes`: a `move this` or `this` receiver leaves the caller with no value whose state could change (BYN7010). A method that calls a `becomes .t` method on `this` declares `becomes` including `.t` itself, and the call counts as `become .t` (BYN7003). A `becomes` method can't be called on a `ref` parameter (BYN7011). A whole-value assignment to `this` or to a `ref` parameter keeps its state: allowed only when the target's type names one state and the new value is in it (BYN7012). A `become` to a state the signature does not declare is an error (BYN7003). **A failed transition leaves the state unchanged.** A method that declares `becomes .s` must `become` on every path that returns normally; a path that fails leaves the state unchanged (BYN7009). After a `becomes` call on a `var` (with no live borrows), the variable's static type changes: after `try order.pay(card)`, `order` is `Order.paid`. A `let` cannot call `ref this` methods, so it needs no tracking.
12. A state switch is exhaustive, so adding a state is a compile error at every non-exhaustive switch (BYN7004).
13. `states` is a contextual keyword at the start of a member declaration when followed by `{`, so fields named `state` or `states` still work. At most one `states` block per struct. The per-line `state x` form is an input form (BYN1059). `becomes` is contextual after a parameter list.
14. States are named as participles or adjectives (`paid`, `shipped`) and are exempt from the `-ed` copy-pattern rule (§5.2), which applies only to members.

> Round 3 §14, §12, O8, payload names: a lifecycle stated in the type moves "order not paid yet" bugs to compile time at no runtime cost; one `states` block and `Order.paid` replace per-line `state` and the `Order is .shipped order` form, which collided with whole-value binding. Codes: BYN1059, BYN1064, BYN7001, BYN7002, BYN7003, BYN7004, BYN7005, BYN7006, BYN7007, BYN7008, BYN7010, BYN7011, BYN7012.

---

## 4. Declarations and modules

### 4.1 Visibility

| Keyword | Visible to |
|---|---|
| `private` | The declaring type |
| *(none)* | The module (directory) |
| `public` | Other packages |

Lint: a type with `@noDefault` or a constructor-like pattern (`parse`, `validate`) whose fields are not `private` gets a warning.

### 4.2 Module-level declarations
- Functions may be declared at module level.
- Module-level `let` must be a compile-time constant.
- A mutable module-level `var` must be `Atomic<T>`, `Mutex<T>` or `@threadLocal`.
- **No runtime static initializers.** Runtime setup uses `static let x = lazy load()` (thread-safe, runs once on first use).

### 4.3 Extensions

```csharp
extension string {
    public bool isBlank => trim().isEmpty
}
extension<T> Span<T> where T : INumber<T> {
    public T sum() { … }
}
extension static Duration {
    public static Duration fromHours(f64 h) => Duration(h * 3600s)
}
```

Resolved statically. Instance members win over extensions. Extensions cannot add fields.

### 4.4 Attributes and purpose clauses
Attributes use `@`: `@derive(Json, Row)`, `@allow(BYN5102)`, `@cases(…)`, `@sealed` (a struct whose fields only its own methods and `with` may write, §3.10; no arguments, no `because`), `@positional` (a struct that gets a constructor taking every field in declaration order, `Point(3, 4)` ≡ `Point { x: 3, y: 4 }`, fallible like the literal when the struct has `requires`; not on a struct with `states`; no arguments, no `because`; revision 0008). Attribute names are camelCase (`@derive`, `@allow`); derivation names stay PascalCase (`Json`, `Row`). `[Name(args)]` is an input form (BYN1045): a line starting with `[` is a collection literal (§2.5). User-defined attributes are `struct attribute Route { string path }`. Arguments must be compile-time constants. Attributes are readable only at compile time. A statement may be preceded by attributes from an allow-list, which applies to the next statement only; the list is `@unobservable` (§20). Any other attribute before a statement is BYN1069 (revision 0014).

**Purpose clauses** (mafʿūl li-ajlihi): `because "…"` is required wherever code bypasses a safety rule. A missing `because` is a compile error (BYN8001); ordinary attributes (`@derive`) take none.

- `@allow(X) because "…"`
- `@json(expose) because "…"` on a `Secret` field (§15.2)
- `xUnchecked() because "…"`, including `trustUnchecked()` (§15.1)
- `unsafe because "…" { … }`

`bayan audit` lists every bypass in a project with its reason (§19.3).

> Round 3 §4: `[…]` at line start collides with collection literals once `;` is gone; a written reason makes every bypass reviewable. Codes: BYN1045, BYN8001, BYN1069.

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
| `xed()` | mafʿūl (participle) | Result of the act: **a view if possible, otherwise a copy** | Names a changed copy or a view; never takes `ref this` and does not need a plain verb to exist | BYN5001 |
| `tryX()` | Form X | Attempt | Returns `bool` or `T?`; derived from `x() fails` | BYN5003 |
| `ensureX()` | Form II | Causative, idempotent | Returns `void` or `fails` | |
| `xWith(T other)` | Form III | Act with another | Takes `T other` | |
| `xInto(ref dst)` | particle إلى | Write into a destination | Takes `ref StrBuf` or `Span<T>`; `x()` allocating form derived from it (adds `alloc`) | |
| `xOp(…)` | maṣdar | The act as a value | Derived; type `Op<T, E>` | |
| `isX` / `hasX` / `canX` | ṣifa | State | `bool`, no arguments, no `ref this`; a `bool` property takes one of these prefixes | BYN5002, BYN5102 (lint) |
| `xUnchecked()` | — | Skips validation | Requires a checked sibling `x()`; calls take `because "…"` (§4.4) | BYN8001 |

> Round 2 §9, §17, §19: `ref this` replaces mutation inference; each pattern contract names the checker rule that enforces it (errors `BYN50xx`, lints `BYN51xx`, which never block builds); derived `withF(v)` is removed because `x with { f: v }` is the one copy-with-changes form, so `with…` is no longer a reserved name. Codes: BYN5001, BYN5002, BYN5003, BYN5101, BYN5102.

**Composition order:** `[try] verb [Into] [Op]` — e.g. `tryFormatInto`, `parseOp`, `tryFormatIntoOp`.

**Derived effects:** `-ed` adds `alloc` only for types that own heap memory (`xs.sorted()` on a list allocates; `v.normalized()` on a `Vec3` and `x.clamped(…)` on a scalar copy without allocating), the allocating form derived from `xInto` adds `alloc`, and `tryX` keeps the original's effects (§14). `bayan expand` will show them (§21.2).

**Noun patterns (types, `PascalCase`):**

| Pattern | Arabic source | Meaning |
|---|---|---|
| `T.Builder`, verb + `er` (`Json.Parser`) | fāʿil | Stateful doer |
| `T.Hasher`, `T.Comparer`, `T.Codec` | mifʿāl | Stateless instrument |
| `T.Field` | mafʿal | Enum of the type's fields |
| `T.<Verb>ed` (`Order.Shipped`) | faʿla | Event: one occurrence |
| `T.Id` | — | Identity (distinct type) |
| `T.Patch` | — | Partial changes |

- **`T.X` vs `X<T>`:** `T.X` with a PascalCase `X` is a different shape derived from `T` (`Builder`, `Patch`, `Field`, `Id`). `T.x` with a camelCase `x` is `T` in state `x` (`Order.paid`, §3.17). `X<T>` is the same value with a different status (`Untrusted`, `Secret`).
- **States** (§3.17) are participles or adjectives and are exempt from the `-ed` copy-pattern rule, which applies only to members.
- **Events** (§16) are named for what happened: a past participle (`placed`, `changed`) or an adjective (`low`). The `on` prefix (`onChange`, `onPaid`) is rejected, and so is any stored function field named with it: a member is named with a verb or a noun, a stored function field with a noun for its role (`handler`, `pricer`, `formatter`), and subscribing is spelled `subscribe(by:)` (BYN5010).
- **Particle words are not member names.** `at(k)`, `to(x)`, `by(f)` fail the grammar check; members are verbs or nouns (BYN5009).
- **No `toX` or `asX` instance members.** `as` and `to` are particles, and a member named `asSpan` or `toList` spells a conversion that already has a form: a view is a noun (`xs.span`), an owned copy is `copy()`, a conversion is `T(x)` (§16, §6.2) (BYN5009).
- **Nouns with no arguments are properties** (`x.abs`, `v.magnitude`, `rows.isEmpty`); with arguments or type arguments they are calls (`x.pow(2)`, `t.floor<Seconds>()`). A generic member stays a call when its type argument is inferred (`let s: Seconds = t.floor()`). A noun member that needs `ref this` is a call (`xs.mutableSpan()`), because mutable access must be visible at the call site. Static factories keep `from` (`T.fromJson(…)`, `T.tryFrom(x)`, `fromBounds(start, until:)`): a factory names the source a `T` is built from.
- **Math vocabulary is a documented naming exemption** (a naming rule, not a synonym): `abs`, `sign`, `sqrt`, `cbrt`, `pow`, `exp`, `ln`, `log2`, `log10`, `sin`, `cos`, `tan`, `asin`, `acos`, `atan2`, `radians`, `degrees`. There is no `log` member: `log` is the logging verb.
- **No abbreviations** applies to tools as well: the formatter is `bayan format` (§21.2).
- **Logging:** `log` is the verb and the level is a particle argument (`log(msg, as: .debug)`, §14). `Log.debug` fails the grammar check, because `debug` is a level, not the act (BYN5007).

> Round 3 §11, §12, §27: `T.Validated` is gone because `requires` makes every value valid at construction; the new rules name what `T.X`, states, events, particles, conversions and logging mean, so a name never hides which of them it is. Codes: BYN5001, BYN5007, BYN5009, BYN5010.

> Revision 0002 item 17: `bayan expand` is deferred until after v0.1, so this text says what it will show. Codes: none.

- **Checker scope (O15).** The pattern rules that make derived forms work (`tryX`, `-ed`, `xInto`, particles; `BYN50xx`) apply to all code. The naming-style rules (`BYN51xx`) apply to `public` declarations only. A name forced by an outside system (a JSON field, a C symbol, a database column) uses the derivation's rename attribute (`@json(name: …)`, `@column(…)`), which exempts it from `BYN51xx`, or `@allow(…) because "…"` (§4.4).
- **Project dictionary.** The checker classifies each word of a member name as a verb, noun or adjective. Words it does not know (domain terms such as `upsert`, `sku`, `kyc`) are declared in `grammar` in `package.byn` (§19.2), merged with the built-in word list; an unknown word is a warning (BYN5014) whose fix adds it there.
- **Wording.** Grammar diagnostics are phrased in plain English ("nouns name values, verbs name actions; `get` adds nothing here"). The Arabic source of a rule appears only in `bayan explain` (a "Where this comes from" section) and `docs/bayan-grammar.md`.

> Round 3 §12, O15: pattern rules are what derived forms depend on, so they hold everywhere, while naming style matters where others read the names; domain words are declared once instead of suppressed per member, and a reader needs no Arabic to act on a diagnostic. Codes: BYN5014.

### 5.3 Derivations
Open set, written in Bayan with `derivation Name for T { … }` (§12.5). Standard derivations: automatic equality/hash/debug, `Order`, `Id`, `Builder`, `Patch`, `Events`, `Observable`, `Json`, `Csv`, `Row` (generates `readRow(ref Reader)`), `CopyIn` (generates binary COPY writers), `Binary` (CBOR), `Proto`, `Query`, `Config`, `Args`, `Gen`, `Diff`, `Ts`. Derivations only add members; they never change existing ones. The `derivation` mechanism is not in v0.1.

- A derivation that would read a `Secret<T>` field is a compile error unless the field opts in with `@json(expose) because "…"` (BYN8002). `debug` still prints `***`.
- `@derive(Row)` rejects non-flat fields (nested structs, lists) (BYN3211).
- Decoders (`fromJson`) check `requires` at construction and collect every violation (§7.9). The derivation also exposes `T.rules` (§7.9).
- Form-encoded decoding (`@derive(Form)`) belongs to the future web framework, not the standard library.

> Round 2 §19: `CopyIn` joins the standard set for database row types.

> Round 3 §11, §16: `Validate` is gone (construction validates); a derivation must not leak a secret by default, and `Row` reads flat rows only. Codes: BYN3211, BYN8002.

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
| `xs except p` | Subtract a pattern from a sequence, set or error set (§6.8) |
| `!` `&&` `\|\|` / `not` `and` `or` | Boolean operators, two spellings (§7.2) |

`with` binds looser than every prefix operator, as in C#: `move tx with { … }` is `(move tx) with { … }` (revision 0008). Whether an operand of `move` can be moved is a semantic check (BYN2782).

No user-defined operator symbols. Operator implementations must not allocate, suspend or fail (checked). Overloading and equality: §6.7.

> Round 2 §5, §7: `catch (e)` is gone; errors bind per arm of `catch switch`; `in` on a range is a boolean test. Codes: BYN1025.

### 6.2 Casts and conversions

| Conversion | Rule |
|---|---|
| Lossless numeric widening | Implicit |
| Narrowing `u8(x)` | Explicit; traps in every build mode if out of range. `u8.tryFrom(x)` returns `u8?` (`null` when out of range) |
| Truncation | `x.truncate<u8>()` |
| Bit reinterpretation | `bitcast<T>(x)` (same size; non-plain types require `unsafe`) |
| Units / distinct types | `Seconds(raw)`, `i64(t)` |
| Enum ↔ number | `.ordinal` / `.value`; `T.fromOrdinal(i)` returns `T?` |
| Views | `Span<T>` → `ReadOnlySpan<T>` implicit; as a call argument, `List<T>` and `T[N]` → `Span<T>` / `ReadOnlySpan<T>`, and `string` → `ReadOnlySpan<u8>`. Never `ReadOnlySpan<T>` → `Span<T>`. |
| User types | `T(x)` calls `IFrom<T, U>.from` (infallible). A fallible conversion (`ITryFrom`, or a type with `requires`) is `try T(x)`; `T.tryFrom(x)` returns `T?` (like `u8.tryFrom`); `T.tryParse(text)` reads text |
| `List<T>` → `Span<T>` | Implicit **only as a function argument**; otherwise the view `xs.span` (read-only, allowed on a `let`) or `xs.mutableSpan()` (needs `ref this`, so only on a `var`) |
| Owned copy | `xs.copy()` returns an owned `List<T>` from a span or list (`uses alloc`); a conversion from a sequence is `List(seq)` |
| Text | `string(x)` or `$"{x}"` formats through `Display`, for every type, including byte spans (`[104, 105]`); there is no `x.toString()` (§6.6, §16.4) |
| Integer → text in a radix | `string(n, as: .hex)`; `as:` takes a `Radix` and defaults to `.decimal`; output is lowercase with no prefix (§3.2). Integers only |
| Text → integer | `u64.tryParse(text)` returns `u64?`; `tryParse(text, as: .hex)` accepts the same `Radix` |
| Bytes → text | A codec: `try Utf8.decode(bytes)` (fails `Utf8Error`), `Utf8.tryDecode(bytes)` (`string?`), `Utf8.decode(bytes, invalid: .replace)` (lossy, never fails); `try Latin1.decode(bytes)` (`kit`), `try Utf16.decode(units)` (`std.text`) (§3.2). Hex and base64 are codecs too: `Hex.encode(bytes)`, `try Hex.decode(text)` |
| User-defined implicit conversions | Not allowed |

There are no implicit user conversions; a conversion is `T(x)`, never an instance `toX` or `asX` member (§5.2). `checked(expr)` evaluates a whole expression with overflow detection and returns `T?` (`null` on overflow). Unlike C#, where `checked` throws, it never traps. Float-to-integer conversion saturates (NaN becomes 0). Integer division by zero traps. `checked(expr)` is not in v0.1.

The C# cast `(T)x` is an input form for `T(x)` (BYN1057).

```csharp
union Radix {
    binary
    octal
    decimal
    hex
}
```

> Round 2 §2, §3: overflow traps in every mode (§17), so narrowing does too; `checked` returns `T?` and the C# difference is stated.

> Sūq review A4, A7 (F-19): a conversion reads as a call, so it needs no parentheses rule of its own (`if i32(big) > 3`), and every type is built the same way. Codes: BYN1057.

> Round 3 §27, O13: `tryParse` takes the same `Radix` as `string(n, as:)`, so formatting and parsing a number name the base the same way, and bytes become text through the constructor forms every type uses. Codes: BYN2230.

> Revision 0003 §1 (amends revision 0002 item 24): in generic code only the `Display` bound is known, so `string(x)` always formats and every binary-to-text conversion is a codec. Codes: BYN2230.

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
line is .removed or .backordered        // words only: `or`, `and`, `not`
path is "/api/v1/" + var rest           // string prefix / suffix / infix (zero-copy)
input is Email(var e)                   // active pattern from Email.tryParse
msg is .text(let body)                  // union destructuring
```

> Round 2 §1: union cases are camelCase.

A union case pattern binds its payload by position (`.circle(let r)`) or binds the whole value by name: in `.percentOff p => p.rate`, `p` is the value narrowed to `.percentOff`, the same shape as `DbError e =>` in `catch switch` (§8).

> Sūq review A3: a name reads better than positions for payloads of three or more fields.

`+` inside a pattern splits a string into a literal part and a binding; it is not concatenation. The round-2 ban (§3.2) covers `+` on string values only.

> Round 2 §12: string patterns stay; the `+` ban targets hidden allocation, and a pattern allocates nothing.

Type patterns apply only to `dyn` values and unions. `switch` must be exhaustive over unions, enums and `bool`.

Patterns combine with words only: `is not null`, `.removed or .backordered`, `not .draft`.

**Named patterns** are `const` values of pattern type, named in camelCase. `pattern` is a contextual keyword, valid only after `=` in a `const` declaration:

```csharp
const inactive = pattern Line { status: .removed or .backordered }
for line in order.lines except inactive { … }
```

> Round 3 §2, §5: naming a pattern lets `except`, `is` and `switch` share it; pattern combinators stay words so a pattern never reads as a boolean expression.

### 6.6 Interpolation
`$"{x}"` uses an interpolation handler; formatting writes into the destination buffer. `$"{e}"` on an error prints the variant's doc-comment summary (fallback: its name; public errors without docs warn). `log($"user {user.id}")` captures `user.id` as a structured field.

**Floats** (`f64`, `f32`) format as the shortest digits that round-trip to the same value. Fixed notation when `1e-5 ≤ |x| < 1e15`, exponential outside it, spelled lowercase with no `+` and no leading zeros (`1e15`, `1.5e-7`, `-2.25e20`), so the text is a valid Bayan literal and valid JSON. Integral values print without a decimal point (`42`); `-0.0` prints `-0`; special values print `nan`, `inf`, `-inf` (`Json.encode` rejects them with `JsonError.nonFinite`).

**`format()` replaces `toString()`.** There is no `toString()`. A type displays through `Display.formatInto(ref StrBuf out, as FormatSpec spec)` (§16.4); the allocating `format()` is derived from it (§5.2). Interpolation calls `formatInto` directly. Calling `toString()` / `ToString()` is a teaching error pointing to `format()`. The conversion spelling is `string(x)` (or `$"{x}"`): a conversion is `T(x)`, never an instance `toX` member (§5.2).

- **Specs** are named arguments inside the hole, replacing the C#-style specifiers: `{n, as: .hex}`, `{x, digits: 2}`, `{id, width: 6, fill: '0'}`, `align:`, `grouping:`, `case:`, `as: .debug`; `for: culture` exists only in `kit.intl` (§16.4). The compact forms `{n:hex}`, `{x:.2}`, `{d:iso}`, `{x:debug}` are input forms (BYN1068).
- Distinct types format as their underlying value; payload-free union cases as their name; error unions as their doc comment; `Secret<T>` as `***`.
- **Structs have no default `format()`.** Interpolating a struct without `formatInto` is a compile error suggesting `format()` or `{x, as: .debug}`.
- **Debug** (the automatic `debug` derivation): `dbg(x)` and `$"{x, as: .debug}"` print the struct-literal form, which is valid Bayan and round-trips (snapshot tests can read it back). `Secret<T>` still prints `***`. `dbg` is an error in release builds (§21.2). The derived `format()` is not in v0.1; the `as: .debug` spec is (§16.4).

> Round 2 §13: one display protocol, no allocating default, explicit debug output. Codes: BYN2031, BYN2225.

> Revision 0003 §6: specs are named arguments, like every other option, so the hole reads as a call; the compact forms stay as input forms. Codes: BYN1068.

### 6.7 Operator overloading and equality

| Rule | Operators |
|---|---|
| Overloadable | `+ - * / %`, unary `-`, `[]`, and `compare` |
| Derived | `+=` from `+`; `==` `!=` field by field; `<` `<=` `>` `>=` from one `static Ordering compare(T a, to T b)` |
| Not overloadable | `&& \|\| ?? is = ?.`, and `+% +\|` (built-in integers only in v1) |

- Operators are `static`, cannot `fails`, and cannot take `ref this`.
- No implicit conversions; a conversion is written `T(x)` (§6.2).
- `==` is derived for every struct whose fields all support `==`. Opt out with `@noEquality`. Distinct types compare only with the same distinct type. `Secret<T>` has no `==`; it has `equalsConstantTime(other)`.
- Lint: `==` against a partial struct literal ("missing fields default to …; did you mean `is`?").
- Strings have no `+` (§3.2).
- Not in v0.1: operator overloading.

> Round 2 §2: a small fixed overload set with derived relatives keeps operators cheap and predictable. Codes: BYN2116.

### 6.8 `except` (istithnāʾ)

```csharp
for line in lines except inactive { … }
let present = users except null
union error Retryable = CarrierError except .rejected
```

- Subtracts a pattern from a **sequence, set or error set**.
- Takes **patterns only**, never boolean expressions: `orders except o.total > 100` is an error whose help points to `where` (BYN1052).
- Never inside a `case` pattern: `case _ except .draft` is an error with a fix-it to `case not .draft` (BYN1051).
- The loop variable narrows (`except null` gives a non-null type). Taint is preserved (§15.1).
- A line starting with `except` continues the previous statement (§2.5).

> Round 3 §7: subtraction by pattern reads as the sentence it is; limiting it to patterns keeps it from becoming a second `where`. Codes: BYN1051, BYN1052.

---

## 7. Statements

A statement may be preceded by attributes from an allow-list (§4.4), one per line, which apply to the next statement only. The list is `@unobservable` (§20). Any other attribute before a statement is BYN1069.

### 7.1 Bindings
`let` (immutable), `var` (mutable), `const` (comptime; any type). A lint suggests `let` for never-reassigned `var`s. Parameters are immutable (copy to a `var` to modify).

```csharp
let w = 0g              // immutable, inferred
let w: Grams = 0g       // immutable, explicit type
var w: Grams = 0g       // mutable, explicit type
```

A local binding puts the name first and the type after it; declarations (fields, parameters, returns, payloads) put the type first (§3.10). In a function body, `Grams w = 0g` is an input form for `let w: Grams = 0g` (BYN1043); in a struct body the same text is a field with a default, which is canonical.

**Shadowing** is allowed with `let`, in the same scope, only when the type changes. Same-type shadowing is a lint warning.

> Round 2 §4: shadowing names a value's new form without inventing names; same-type shadowing hides a bug more often than not. Codes: BYN2007, BYN2028.

### 7.2 Conditionals

**No parentheses around conditions; braces are mandatory.** Parentheses only group. When a condition has a brace at its top level (a struct literal, a property pattern), the whole condition is wrapped in parentheses; otherwise the formatter removes them.

```csharp
if n > 0 { … } else if n < 0 { … } else { … }
if (p is { x: 0, y: 0 }) { … }
unless user.isVerified { sendVerification(user) }
if unlikely buf.isFull { flush() }
if never state is .corrupt { panic("corrupt") }
switch day {
    case .sat or .sun: relax()
    default: work()
}
```

- **`unless`** has no `else`. `unless !x` and `unless not x` are errors. A top-level `&&` or `||` (or `and`, `or`) in its condition is an error with a fix to `if`. `unless x is null` is not allowed; write `if x is not null`. `unless` takes no likelihood word.
- **`if !x { … }` and `unless x { … }` are both allowed** paraphrases; neither is rewritten.
- **Boolean spellings:** `!` `&&` `||` and `not` `and` `or` are both allowed in boolean expressions, the one documented synonym exception (§0). `bayan format` normalizes each project to the spelling in `style.booleans` (`.symbols` or `.words`, §19.2). Patterns use words only (§6.5).
- **`if unlikely`** is a layout hint only (the block is cold); semantics are unchanged. A lint fires when it guards an error return (already cold). Emitting the hint to C is not in v0.1.
- **`if never`** panics if the condition is true, **in every build mode**, and lays the block out cold.
- **Null checks** are `is null` / `is not null`. After `is not null` on a `let` binding or a parameter, the value is narrowed to the non-null type. `x ?? return …`, `?? break` and `?? continue` exit early.
- `if let` is deferred; `while let` (§7.4) is not.

> Round 2 §4: conditions read without C punctuation; braces remove the dangling-statement class of bugs; `if never` is a check, so it cannot depend on the build mode. Codes: BYN1017, BYN1018, BYN1022, BYN1023, BYN1033, BYN2027, BYN2233.

> Round 3 §5: rewriting `if !x` to `unless` replaced one paraphrase with another for no gain, so BYN1032 retires; each boolean spelling reads well in some code, so a project picks one instead of the language.

### 7.3 Postfix modifiers
Postfix modifiers are the one-line form. Allowed on **simple statements only**: `return`, `fail`, `break`, `continue`, `panic`, a single call, a single assignment, `++`/`--`. Not on blocks, not chained.

```csharp
let order = db.find(id) ?? return .notFound
return .badRequest("Cart is empty") unless cart.hasItems
fail .outOfStock if line.qty > stock
let user = users.find(id) ?? fail .unknownUser
flush() if unlikely buf.isFull
d++ if debtor.amount == 0
```

**The condition is evaluated first;** the statement runs only if it applies. The modifier is on the statement's line (BYN1042). `stmt unless not cond` is rejected with a fix-it to `stmt if cond` (BYN1007).

> Round 3 §3, §5: a modifier on the next line would read as a new statement; `unless not` is a double negative. Codes: BYN1007, BYN1042.

> Round 2 §4: a call's non-null result is bound with `?? return`, not the C# `is { } order` idiom.

### 7.4 Loops

```csharp
for i in 0..n { … }
for x in xs { … }
for x: Token in tokens { … }                  // loop bindings follow the local rule (§7.1)
for line in order.lines except inactive { … }
for (i, x) in xs.indexed() { … }
for (key, value) in prices { … }
outer: for row in grid {
    for cell in row {
        break outer if cell.isTarget
    }
}
while queue.hasItems { … }
do { … } while try socket.hasMore()
```

- A typed loop binding puts the name first, like a local: `for x: T in e`. `for T x in e` is an input form (BYN1043). A tuple pattern destructures: `for (a, b) in pairs`.
- `for x in xs except p` subtracts a pattern from the sequence and narrows the loop variable (§6.8).
- **`for` and `for try` are pattern-based**, like C#'s `foreach`: any type with `bool moveNext(ref this)` and `current` iterates, and the loop takes the effects and error set of the concrete `moveNext`. `IIterator<T>` stays as the bounded interface for generic code; `Rows<T>` does not claim to implement it.

**Fallible iteration:**
- `for try x in seq`: every step may fail and failures propagate. Iterating a fallible sequence without `for try` is a compile error with a fix-it.
- Queries and readers are **lazy**: opening happens on the first step, so one `try` covers both. If creating the sequence can fail eagerly, both are written: `for try line in try File.open(p).lines() { … }`.
- The loop owns the sequence and drops it on every exit.
- `while try cond { … }`: the condition may fail on each evaluation.
- `while let x = expr { … }` loops while `expr` (a `T?`) is non-null, binding `x`. It works with `try`: `while let row = try rows.next() { … }`.
- A fallible sequence is a value with the `moveNext`/`current` pattern; `for try` steps it until `moveNext()` returns `false`. Iterating one without `for try` reports BYN2221 with a fix-it inserting `try`.
- Not in v0.1: lazy queries and readers.

> Round 2 §4: one `for x in xs` form with no parentheses; fallibility is marked once, at the loop head. Codes: BYN1017, BYN1018, BYN1019, BYN2022, BYN2232, BYN2234.

> Round 3 §25, §7: a loop binding is a local, so the name leads; `except` removes a filter loop for the common case. Codes: BYN1043, BYN1051, BYN1052.

> Revision 0002 item 2: a pattern lets a cursor whose `moveNext` fails and uses effects (`Rows<T>`) iterate without claiming an interface whose bound it exceeds. Codes: none.

### 7.5 `switch` statements
No fall-through. `break` is optional at the end of a section and, if used, leaves the `switch` (C# semantics). Combined patterns replace stacked labels (`case .sat or .sun:`). `goto case` does not exist; leave an enclosing loop with a labeled `break`.

### 7.6 Cleanup

```csharp
defer arena.reset()                           // every exit
defer failing tx.rollback()                   // exit by failure
defer panicking (p) log($"{p}", as: .error)   // panic in progress
```

- A `unique` value is cleaned up exactly once when its single owner goes away; there is no `using` for disposal.
- `defer stmt` runs at scope exit, always.
- `defer failing stmt` / `defer failing (e) { … }` runs only when the function exits by failing; it is allowed only in functions that `fails`. It also runs on fiber cancellation (not in v0.1: no fibers).
- `defer panicking stmt` / `defer panicking (p) { … }` runs only while a panic is in progress, and is allowed anywhere.
- A postfix modifier on a deferred statement is evaluated once, when the `defer` is **registered**, and its value is captured, the same rule as for any `if`/`unless` modifier (§7.3): in `defer failing tx.rollback() unless dryRun`, a later change to `dryRun` has no effect. To decide at exit, test inside the body: `defer { if dryRun { … } }`.
- `failing` and `panicking` are contextual words, valid only after `defer`. Future statement modifiers follow the same ḥāl rule: head verb, then a participle.
- All run in reverse registration order. Bodies **cannot fail** (no `try`).
- **On a panic, only `defer panicking` handlers run** (innermost first); plain `defer` and `defer failing` do not, because a panic aborts without unwinding. Order: the panicking fiber's `defer panicking` handlers, then `Panic.handler`, then the default trace, then abort. The test runner is its own entry point with its own handler (it fails the test).
- **Panic handler bodies are restricted:** no `alloc` or `suspend` effect (§14; logging is ambient); no `try`; they read only `let` bindings and parameters; they write only to panic-safe sinks (stderr, panic-safe log sinks). `return`, `break` and assignment are errors ("panic handlers can't recover").
- A panic inside a handler aborts at once and prints both messages.
- Handlers are a per-fiber linked list of records, pushed and popped only in scopes that declare one.

**Process-wide hook:**

```csharp
type PanicHandler = Panic -> void uses console     // stored function type: an upper bound

void main() {
    Panic.handler = p => print(p, to: .stderr)
    run()
}
```

- `Panic.handler` is a noun naming the role, not `onPanic` (a stored function field is named for its role, §3.10). `defer panicking (p) { … }` stays the scoped form; `Panic.handler` is the process-wide hook.
- Allowed only as the **first statement of the entry method**, at most once; libraries cannot set it. Anywhere else is a compile error. Same body restrictions as `defer panicking`.
- The handler's bound is `uses console`: it may write to stderr (`print(p, to: .stderr)`; `Stderr.write` stays as the raw, unformatted writer), and it may not allocate, suspend or do other I/O. This is round 2's "implicitly `noalloc` and `nosuspend`" expressed as effects.
- In v0.1 the hook takes a named function `void f(Panic p)` (`Panic.handler = onCrash`); the lambda form needs §3.16.
- Fiber isolation on panic is deferred.

> Round 2 §6: `errdefer` becomes `defer failing`, one head verb with participles; panic handlers run in a known order with restrictions that keep a dying process safe. Codes: BYN1020, BYN2601, BYN2606, BYN2612, BYN2613, BYN2614.

> Revision 0002 items 5, 28: the entry function is written `void main()`, and stderr goes through `print`'s `to:` stream (§10), so a panic is formatted like any other output. Codes: none.

> Round 3 §12, §13: `onPanic` broke the rule against the `on` prefix and repeated the type's name; `handler` names the role, and `uses console` states the old restriction as an effect bound. Codes: BYN2230, BYN2614, BYN5010.

> Round 3 §19: capturing the modifier at registration means the cleanup decision matches the state the scope was entered with.

### 7.7 `provide` and `context`
`provide allocator: arena, io: Io.evented() { … }` sets implicit context values and effect handlers for the block (§14). `context` is only the value (`context.Io`, `context.allocator`). The `context (…) { … }` block form is rejected with a fix-it to `provide` (BYN6006).

```csharp
let net = FakeNet(label)
provide net: net, clock: FakeClock(at: Date(year: 2026, month: 12, day: 1)) {
    try shipAll(order, by: carrier)
}
assert net.calls == 1

let logs = LogRecorder()
provide log: logs { … }
```

- Handler names are the effects `alloc`, `net`, `fs`, `clock`, `random`, `env`, `process`, `console`, plus `log`; any other name is BYN6007, and `suspend` has no handler.
- Inner `provide` blocks override individual handlers and inherit the rest. Handlers are one-shot, so they compile to context passing at zero cost.
- The test runner provides `console` per test as a capture buffer; output is shown when a test fails, or always with `bayan test --verbose`.

> Round 3 §13: `context` named both a value and a block; `provide` is the verb for the block. Codes: BYN6006.

### 7.8 `goto`
Does not exist. `become` performs typestate transitions (§3.17); the spelling of guaranteed tail calls is deferred.

### 7.9 Validation: `requires`
On types and fields, `requires` has one meaning: **checked at construction.** A value of a type with `requires` cannot exist in an invalid state.

```csharp
type distinct Email = string requires it.isEmailShaped

@derive(Json)
struct SignupForm {
    Email email
    string username
        requires it.runeCount in 3..=32
        requires it.isAsciiAlphanumeric
    string password
        requires it.runeCount in 8..=128
    i32 age
        requires it in 13..=120

    requires password != email.value  at .password
    requires password != username     at .password
}
```

- `it` is the value being checked; `requires value…` is an input form (BYN1050).
- Each `requires` line is a separate rule and reports its own violation.
- **Field or struct level is decided by content:** a `requires` that refers only to `it` belongs to the field above it; one that refers to other fields is struct-level wherever it appears. A struct-level rule attaches to a field with `at .field` (target-typed to `T.Field`).
- **Struct literals of a type with `requires` are fallible:** `try SignupForm { … }` (BYN3207). A distinct type with `requires` is built with `try Email(raw)`; a constant argument is checked at compile time. Construction fails with `ValidationError<T>`, a generic `union error` with one case; decoders report malformed input in the same set:

  ```csharp
  union error ValidationError<T> {
      invalid(List<Violation<T>> violations)
  }
  struct Violation<T> {
      T.Field field
      Rule rule
  }
  union error DecodeError<T> = JsonError, ValidationError<T>
  ```
- `ValidationError` always takes its type argument: a bare `ValidationError` is BYN2023, since without `T` there is no `T.Field`. A function that validates two types lists both: `fails ValidationError<SignupForm>, ValidationError<Address>` (§3.14), and diagnostics print the type arguments.

- **Decoders collect every violation:** `static T fromJson(Untrusted<string> body) fails DecodeError<T> uses alloc`. A caller matches both: `catch switch { .invalid e => … e.violations, .syntax s => … }`.
- Fields of a struct with `requires` cannot be assigned one at a time (BYN3208). Updates go through `with`, which re-checks: `try f with { password: p }`.
- **Drafts** use `T.Patch` (all fields optional, no `requires`); `patch.complete()` returns `T fails ValidationError<T>`. The two-step flow (`T.Patch.fromJson`, then `complete()`) is a paraphrase of `fromJson`: both report the same errors.
- **Rules as data:** the derivation exposes `T.rules: List<(T.Field field, Rule rule)>` at compile time, for web frameworks (HTML5 attributes). A recognized expression shape maps to a `Rule` case; anything else is `custom` and is checked only on the server:

  ```csharp
  union Rule {
      length(Range<i64> bounds)     // it.runeCount in a..=b
      range(Range<i64> bounds)      // it in a..=b
      required                      // non-nullable field
      kind(TypeId id)               // Email, Url, Date (`type` is a keyword)
      custom(RuleId id)
  }
  ```

- Not in v0.1: `pattern(Regex re)` (`it.matches(re)`), with `std.regex` (§15.4).
- Violations are structured (field, rule, parameters) with **no message strings**; frameworks localize them.
- A type with `requires` has **no default value** unless the default satisfies the rules.
- **Preconditions on functions are `expects`,** not `requires`. A type with `requires` is the reusable rule (it fails at construction); `expects` is a relation between parameters:

  ```csharp
  type distinct Count = i64 requires it > 0
  public List<T> take(Count n) uses alloc                  // reusable rule: fails at construction

  public Span<T> slice(Index start, until Index end)
      expects start <= end { … }                           // relation between parameters: traps
  ```

  | | Type with `requires` | `expects` |
  |---|---|---|
  | Purpose | Validating data | Contract between programmers |
  | On violation | Fails, recoverable (`ValidationError<T>`) | Traps, like a bounds check |
  | Checked | Once, at construction; the proof travels with the value | Every call, in every build mode |

  - The clause must be `bool`, a violation is a bug that **panics in every build mode** (`contract violated: <text>`), and a constant argument that violates it is a compile error. When the caller has proven it (for example, the argument's type carries the rule), the check is removed.
  - `requires` on a function is an input form for `expects` (BYN1063).
  - An `expects` that reads an `Untrusted<T>` value is a compile error (BYN8006): trapping on outside data would let a request crash the program.
  - A lint flags an `expects` that reads only one parameter and suggests a type with `requires` (BYN5011).

- `x in a..=b` is a boolean range test.
- `requires` drives boundary-test generation (§20).
- Which tool: non-null types (no check), types and fields with `requires` (valid by construction, all violations reported), function `expects` (bugs), `fail .x unless …` (business outcomes).
- Not in v0.1: proof-based check removal, property-test generation from `requires`. BYN2322, which marked `requires` on fields and distinct types as not built, is retired: `docs/diagnostics/BYN2322.md` (revision 0006).

> Round 2 §7: one contract form, and a bug panics in every build instead of being skipped in `fast`. Codes: BYN2320, BYN2321, BYN2322 (retired, revision 0006).

> Round 3 §11, O3, O4: checking at construction makes validity a property of the type, so `T.Validated`, `validated()` and `Validate` are gone; `it` matches lambdas; `at` reuses a particle instead of adding `on`; a function precondition is a different job from validating data, so it gets its own word, `expects`. Codes: BYN1050, BYN1063, BYN3206, BYN3207, BYN3208, BYN3218, BYN5011, BYN8006.

---

## 8. Errors

```csharp
public Money parse(string text) fails MoneyError                 // one set
public User signUp(SignupForm f) fails EmailError, SignupError   // several
public User signUp(SignupForm f) fails SignupFailure             // named set
Response handle(Request r) fails                                 // inferred: private functions only
```

```csharp
let resp = signUp(form) catch switch {
    EmailError      => .badRequest("Invalid email"),
    DbError.timeout => .unavailable,
    DbError e       => .serverError(e),
}
```

- **Public functions must list their errors.** The language server shows inferred sets as inlay hints.
- **`fail .x` leaves with an error; `return` always returns a value.** In `Response handle(Request r) fails DbError`, `return .badRequest` is a `Response` and `fail .timeout` is an error. The case must be in the `fails` clause (BYN2603), and a function that cannot fail cannot `fail` (BYN2604). `fail .x if cond` and `?? fail .x` follow the postfix rules (§7.3). `return .x` in a function that fails, where the return type has no case `.x`, is an input form with a fix-it to `fail .x` (BYN1056).
- Variants are namespaced by their set (`EmailError.invalid` ≠ `UrlError.invalid`); an ambiguous `.invalid` is an error.
- Call sites: `try f()` propagates; `f() catch value`; `f() catch return …`; `f() catch switch { … }` (exhaustive; each arm is a pattern over the error union and binds its own name, `DbError e => …`); `f() catch (Set.variant) …` handles some and propagates the rest (which must appear in the caller's `fails`). There is no `catch (e)`.
- `f() catch { … }` runs a block when `f` fails. On a `void` call the block may complete normally. When `f` returns a value, the block has no value to give, so it must not complete normally: it ends in `return`, `break`, `continue`, a panic or another `never` expression (`let n = parse(s) catch { return -1 }`), and a block that can fall through is BYN2611. A brace body of comma-separated `name: value` fields is a target-typed struct literal, not a block: `f() catch { x: 1 }` gives that struct value. `catch {}` is an empty block, so an empty struct handler names its type: `f() catch P{}`.
- Built-in error sets: `IoError` (codes 1–7) and `Utf8Error` (`invalidSequence`, code 8, from `string(bytes)`, §3.2); user error variants are numbered from 9.
- Payloads: `union error ParseError` with cases `badDigit(Bytes at)` and `overflow`, one per line (§3.14).
- **`try` covers the entire expression to its right**, down to the lowest-precedence operator. Applying it to a sub-expression requires parentheses (`(try f()) + g()`); the diagnostic suggests where (BYN1054).
- Debug builds record **error return traces**.
- `@status(400)` maps a variant to HTTP; the client sees a generic message unless the variant is `@public`. `@exitCode(n)` maps to a process exit code when `main` fails (default 1).
- **Panics abort** with message, error return trace and stack trace. No unwinding in v1; only `defer panicking` handlers and `Panic.handler` run (§7.6). Tests catch panics per test.

> Round 2 §5, §6, §15: errors bind per arm of `catch switch`; `--explicit` is gone, so inferred sets are inlay hints. Codes: BYN1025, BYN1029.

> Round 3 §18: one scope rule for `try`, with no exceptions to memorize. Codes: BYN1054.

> Sūq review A2: in a function returning a union that also fails, `return .badRequest` and `return .overflow` looked identical. Codes: BYN1056, BYN2603, BYN2604.

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

- **`x = x with { … }` is a move.** When an assignment's target is the variable its `with` expression reads, the old value is consumed: no copy, no BYN2780, for `unique` and every other struct. Assigning a `with` of a `unique` value anywhere else (`let copy = tx with { … }`) needs `move tx`: `let copy = move tx with { label: "copy" }` (BYN2780 otherwise). Revisions 0007, 0008.

- **Only a local or a `move` parameter can be moved from.** Moving out of a field, element or plain parameter is a compile error (it would leave a half-moved value). `x.take()` moves the value out of any place it can write and leaves that type's empty value behind: `let items = ledger.items.take()` leaves `ledger.items` an empty list. `take()` exists on `List`, `Map`, `StrBuf` (empty value: an empty one) and on optionals of those (`null`); on any other type it is BYN2788, and on a place that can't be written (a `let` local, a plain parameter) it reports the usual mutation error. `map[k].take()` panics on a missing key, like `map[k]`.
- **Pattern bindings on a `unique` subject borrow.** `switch x` and `x is …` bind borrows of `x`'s parts, so `x` stays usable after the switch. `switch move x` moves `x` into the switch, and its bindings then own their parts; `x` is moved afterwards on every path. The subject must be a local or a `move` parameter (BYN2789 otherwise). An owning pattern may not bind a value and a part of it, through both sides of an `and` or through a designation with bindings inside it (BYN2790): two bindings would own one storage. Two bindings overlap only when one's path is a prefix of the other's, and only bindings of a `unique` type count (`{ a: { b: let x }, a: { c: let y } }` and `{ items: let x, items.length: let n }` are accepted). In a `when` guard of an owning switch the arm's bindings and their parts are read-only (BYN2791): a guard that fails gives the value back to the switch. As an expression the subject needs parentheses, `(move x) switch { … }`: `move x switch { … }` parses as `move (x switch { … })`.

> Round 2 §4: no parentheses around a `switch` subject. Round 3 deliverable 4: `length` is the size name.
- **Debug builds check ownership at runtime.** Each `unique` allocation records its current owner and every move updates it; a second owner, or a read through a moved-from local, panics with both source locations, in the usual panic format: `second owner of List<i64>: owned since a.byn:3` or `use of moved List<i64>: moved at a.byn:4`, followed by `  at a.byn:5`. Safe and fast builds compile the checker out. Rules here hold outside `unsafe`, where raw pointers may alias.

### 9.4 Borrowing and regions (no lifetime annotations in the common case)
1. Every scope that frees memory is a region (function locals, arenas, requests).
2. A borrow (`ref`, `Span<T>`, `string` slice, or a struct containing one) carries its source's region; the compiler infers it.
3. **A borrow cannot be stored anywhere that outlives its region.** Fix: `.copy()` or `.copy(to: alloc)` (strings, §3.2; collections, §16) or `move` into an `Owned`.
4. A returned borrow derives from **all** borrowed arguments unless narrowed: `string head(string s, at string sep) borrows s`.
5. **While any borrow into a value is alive, the value cannot be structurally modified.** Passing `x` and `ref x` in one call is an error. A call to a `ref this` method modifies its receiver's root local, so `lexer.push(t)` is an error while a borrow of `lexer` is alive. Loans are tracked per root local, not per field.
   A method whose return type is a span may return a borrow of its own receiver's fields: in `struct Lexer` with a field `List<Token> toks`, a method `ReadOnlySpan<Token> tokens() => toks.span`. At the call site the result borrows the receiver, and rules 3 and 5 apply to it as to any borrow of the receiver. A `string` view of an own field, a span inside a returned tuple, and a borrow of a local's storage are still rejected (BYN3003).
6. `arena.reset()` requires exclusive access.
7. Literals and `const` data are static and may go anywhere.
8. **A borrow stored in a struct field binds the struct to the referent's region.** A struct that holds a reference (a `Span<T>`, a `ReadOnlySpan<T>`, a borrowed field) never outlives the referent, cannot be returned past its owner, and its owner cannot be modified while it is alive (rules 3 and 5). `Rows<T>`, `Subscription` and the span views rely on this.
   - A struct holding a mutable reference (`Span<T>`, a `ref` field) is implicitly `unique` (§3.11): it cannot be copied, so two mutable aliases cannot exist. Copying one is the use-after-move error of any `unique` value. Read-only references (`ReadOnlySpan<T>`) copy freely.
   - A struct holding a reference cannot be passed to a fiber that may outlive the referent (BYN3217); within a structured scope that ends first (§13.3), it can.

> Round 2 §9: mutation is declared with `ref this`, not inferred. Codes: BYN2115.

> Round 3 §31: a stored reference follows the region rules of any borrow; implicit `unique` rules out two mutable aliases, and the fiber rule keeps a reference from outliving its referent on another fiber. Codes: BYN3217.

### 9.5 Uninitialized memory
`stackalloc T[n] uninit` yields `Uninit<T>`, which cannot be read. Writing APIs return the initialized prefix as a `Span<T>`. A runtime-sized `stackalloc` requires a bound: `stackalloc u8[n] max 4KiB`.

> Round 2 §15: C# type names are accepted in type positions (BYN1030 is retired). Codes: none.

### 9.6 Handles
`Pool<T>` + `Handle<T>` (index + generation, 8 bytes) for graphs and long-lived data; stale handles are detected.

Trees and graphs use typed indices first (§3.5; the idiom is in `docs/bayan-style.md`), with no per-access cost. Generational handles, which catch a stale index after deletion at the cost of one compare per access, are added only when the selfhost AST or Sūq shows the need.

> Round 3 §31: typed indices cover trees with zero overhead, so handles wait for a demonstrated need.

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

Modifiers precede the parameter name: `ref buf: StrBuf`, `move file: File`.

**Argument particles.** A closed set of role particles (Arabic مِن/إلى/بِ) labels every required parameter after the object:

| Particle | Role |
|---|---|
| (none) | The object being acted on |
| `from` | Source or start |
| `to` | Destination or recipient |
| `until` | Exclusive end |
| `through` | Inclusive end |
| `at` | Position or time |
| `by` | Means or replacement |
| `for` | Beneficiary or purpose |
| `as` | Role |

```csharp
public void copy(from Path src, to Path dst) fails FsError uses fs
public void transfer(Money amount, from Account payer, to Account payee) fails LedgerError
public void insert(ref this, T item, at Index i) uses alloc
public void remove(ref this, from Index start, until Index end)

copy(from: a, to: b)
try ledger.transfer(amount, from: alice, to: bob)
lines.insert(x, at: 3)
xs.remove(from: 2, until: 5)
```

- A function has at most one unlabeled parameter, the object (BYN5004). **The receiver does not count** as the object. Every other required parameter takes a particle (BYN5006), and call sites must write it (BYN5005). An unknown particle is BYN5008.
- **Optional parameters** are named parameters with defaults. They may take a particle when one fits their role (`log(m, as: .debug)`, `FakeClock(at: …)`); otherwise they use their name (`timeout: 5s`).
- **Exempt:** a `rest` tail (below); the generated constructor of a struct marked `@positional` (§4.4; its static factories and methods are not exempt), whose components are the same kind and universally ordered (`Point(3, 4)`, `Vec3(1.0, 0.0, 0.0)`; not `Date`, which keeps `Date(year:month:day:)`); `extern` declarations; operator declarations; union and state payloads (fields, not parameters); laws.
- **A particle appears at most once per signature (O12, BYN5013).** Group the values in a struct or choose another particle.
- Particle words are not member names (§5.2).
- The particles are part of the function's name (`copy(from:to:)`).
- The set is closed for user code and grows only through spec revisions (`until` and `through` were added in round 3). `for` and `as` are keywords elsewhere; as particles they appear only in label position (`for:`), which is unambiguous.
- `bool` parameters must be passed by name.
- Particles supersede the same-type naming rule and its exemptions for required parameters; BYN2305 and BYN2307 retire when particles are implemented.
- Overloads may not differ only by `ref`/`out`. Default parameters as in C#.

> Round 3 §15, O6, O12: a particle names the argument's role, so call sites read as sentences and swapped arguments become label mismatches; `until` and `through` name the two ends of a range, and a repeated particle would make a label ambiguous. Codes: BYN5004, BYN5005, BYN5006, BYN5008, BYN5013.

**Overloading.** Overloads may differ by particle set (`move(from:to:)` vs `move(_, by:)`) or by parameter type. The checks run at the **declaration**, so a call never needs C#-style ambiguity resolution:

1. Overloads that differ only by type must not overlap through any implicit conversion (finer units, integer widening, state widening). `void f(Grams g)` beside `void f(Kilograms k)` is an error, because `Kilograms` converts to `Grams` (BYN3213).
2. Overloads may not differ only by function-type parameters, since `it` lambdas need one target type.
3. Defaults may not make two overloads accept the same argument list.
4. A reference to an overloaded name needs a target type: `let f: i32 -> i32 = abs` (BYN3215).
5. Type variation over number families uses generics, not overloads: `public T abs<T>(T x) where T : ISignedNumber<T>`.

```csharp
public void remove(ref this, at Index i)
public void remove(ref this, at Range<Index> r)        // ok: no conversion between Index and Range<Index>
```

**Variadic parameters: `rest`.**

```csharp
public void print(rest ReadOnlySpan<Display> items, to Stream stream = .stdout) uses console

print("total", total, "for", order.id)
let p = Path.join(root, "orders", $"{id}.json")        // rest ReadOnlySpan<PathSegment>
let top = max(a, b, c)                                  // rest ReadOnlySpan<T> where T : IOrder<T>
print(..names)                                          // spread an existing collection
```

- `rest` applies to the last positional parameter only. Its type is written out (`ReadOnlySpan<T>`), so the type inside the body is the type in the signature.
- `items` is **borrowed for the call** and cannot be stored; keeping the values needs an explicit copy (`items.copy()`, which returns a `List<T>` and `uses alloc`; BYN3214).
- **Zero allocation:** the caller reserves the argument slots in its own stack frame (the count is known at the call site). Each slot is the value itself, or for an interface element type a 16-byte interface value (data pointer plus vtable pointer) that points at the original, so nothing is boxed or copied. The callee receives a pointer and a length. C# `params object[]` allocates an array and boxes every struct.
- **Spread:** `..x` in an argument position passes an existing collection's span without copying.
- **Named optional parameters may follow the `rest` parameter** and can only be passed by name.
- `params` (C# habit) and `T... items` are input forms for `rest` (BYN1058).

> Round 3 §24: variadic calls without a heap array or boxing, and overloads checked where they are declared. Codes: BYN1058, BYN3213, BYN3214, BYN3215.

> Revision 0002 item 28: `print` takes `to Stream stream = .stdout`, so stderr is written with `print(p, to: .stderr)` through the same formatting; `Stderr.write` stays the raw writer. Codes: none.

**Mutating members: `ref this`.** `mut` is not a keyword.

```csharp
public void add(ref this, CartLine item) fails CartError { … }
public bool isEmpty => lines.isEmpty          // read-only: the default
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
T max<T>(ReadOnlySpan<T> xs) where T : IOrder<T> { … }
struct Ring<T, const i32 N> {
    T[N] items
    i32 head
}
interface IRepository<T, K = T.Id> where T : IEntity<T> { … }
```

| Constraint | Meaning |
|---|---|
| `T : IA, IB` | Implements interfaces |
| `T : struct` / `T : unique` | Copyable / move-only |
| `T : default` | Defaultable |
| `const N: i32` | Compile-time value parameter |
| `E.Item : INumber` | Constraint on an associated type |

> Round 2 §15: C# type names are accepted in type positions (BYN1030 is retired). Codes: none.

- Constraints are **checked at definition**.
- **No variance.**
- **Specialization: the specific overrides the general** (Pāṇini). Hand-written beats derived; instance beats extension; concrete beats generic; more specific constraint beats less specific.
- **Generics are monomorphized** in every build mode: each instantiation is compiled for its concrete types (one C function per instantiation, named by its mangled type arguments), so a call through a type parameter is a direct, inlinable call. Interface values (`Display` as a type, `rest ReadOnlySpan<Display>`) are the explicit way to ask for dynamic dispatch, as `{data, vtable}` pairs.
  ```csharp
  public T max<T>(ReadOnlySpan<T> xs) where T : IOrder<T> { … }   // static: one copy per T
  public void print(rest ReadOnlySpan<Display> items, to Stream stream = .stdout) uses console  // dynamic: one copy, vtable calls
  ```
- Inference from arguments; explicit type arguments only when nothing can be inferred.
- `List<T, I>` takes an optional second type argument, the index type (`List<Money, RowId>`); plain `List<T>` indexes with `Index` (§3.5).
- No higher-kinded types. Variadic generics: v2 candidate.

> Round 3 §30: one compilation strategy in every mode keeps a debug build's dispatch the same as a release build's; dynamic dispatch is chosen by writing an interface value, never by the build mode.

> Revision 0002 item 28: `print` takes `to Stream stream = .stdout`, so stderr is written with `print(p, to: .stderr)` through the same formatting; `Stderr.write` stays the raw writer. Codes: none.

---

## 12. Comptime

### 12.1 Where it runs
`const` declarations, `comptime { }` blocks, `const` parameters, type-returning `comptime` functions, derivations, program queries.

### 12.2 Sandbox
Deterministic. Allowed: pure computation, reading files by **literal path inside the package** (hidden and VCS-ignored files refused unless listed in `comptimeAllow`), type reflection, raising diagnostics. Not allowed: network, clock, randomness, writing files, environment variables (pass through `build.byn`). Every file read is recorded in the lockfile.

### 12.3 Budgets
10,000,000 interpreter steps and 256 MiB per top-level evaluation; raise per function with `@comptimeBudget(n)`. A dependency's budgets cannot be raised by its dependents. Steps, not time, keep results identical on every machine.

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
            comptime if i > 0 { b.append(',') }
            b.appendCsv(this.[f])
        }
    }
}

const routes = comptime Program.functionsWith<HttpRoute>()
```

Derivations are applied with `@derive(Row)`, `@derive(Json)`, `@derive(Csv)` and only add members. `bayan expand T` will show the generated code (§21.2), which must be identical to the hand-written equivalent (for example, ordinal reads for `Row`). Not in v0.1.

> Round 2 §4, §19: loop and condition syntax as in §7 (also `comptime if` in §12.4); derived code is inspectable and no different from hand-written code.

> Revision 0002 item 17: `bayan expand` is deferred until after v0.1, so this text says what it will show. Codes: none.

Tier-3 **generators** run from `build.byn`, read declared inputs, emit Bayan source into `gen/`, are sandboxed (no network), cached by input hash, and checked for determinism by the toolchain.

### 12.6 Library diagnostics

```csharp
diagnostic JQ0001 error "unknown column `{column}` in `{table}`" {
    help: "did you mean `{suggestion}`?",
    explain: """…""",
}
public static Query<T> sql<T>(const string text) {
    comptime { … fail JQ0001(column: c, table: t, suggestion: s, at: text) }
}
```

> Round 2 §10: diagnostic fields use `:` like every field initialization.

Packages reserve a code prefix in `package.byn`; the registry enforces uniqueness. `@whenMissing("…")` customizes unmet-constraint messages. `@replaced(by: "…", since: "2.0")` drives `bayan migrate`. Projects may raise a library's warnings to errors but never lower its errors.

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

Libraries never choose an implementation. Blocking work is offloaded automatically (DNS, file I/O on epoll, `@blocking` externs). Debug builds warn when a fiber runs over 50 ms without yielding. Scripts may override with `#:io evented`.

### 13.2 Async
- No coloring: functions are never declared `async`. `async` as a function modifier is a teaching error showing the call-site form.
- `async f(args)` is a call-site expression that schedules `f` on `context.io` and returns a `Future<T>` (a `ref struct`); `await` joins; `try await` propagates errors. `await (a, b)` awaits several. Implementation: `async f(x)` hands `fOp(x)` (the maṣdar, §5.2) to `context.io`.
- Under `Io.Blocking`, `async` runs the call eagerly (same code, sequential).
- **Every await can fail**; `Canceled` is in every I/O error set.
- A function without `suspend` in its effects cannot suspend (§14); a violation names the suspending call chain.
- **Futures are scoped:** a future cannot escape its block; leaving the block cancels unfinished futures, which runs their `defer failing` handlers (§7.6).
- v1 uses **fibers** (stackful, lazily committed stacks, guard pages). The stackless transform arrives with the WASM target.

**Deadlines: `provide timeout: d { … }`.**
- Sets a deadline for every suspending operation inside the block, including in called functions, with no parameter plumbing.
- A nested `provide timeout: …` can only shorten the remaining time (the effective deadline is the minimum of the remaining and the new one).
- Expiry fails the suspended operation with `TimeoutError`.
- CPU-bound loops check explicitly: `try context.checkDeadline()`.
- Not in v0.1: async, futures and deadlines (no fibers).

> Round 2 §14: functions stay colorless; scoped futures and block deadlines give cancellation without plumbing. Codes: BYN1016.

### 13.3 Structured concurrency
`let group = Io.group()`, then `group.spawn(…)` and `try await group.all()`. Futures cannot outlive the scope that started them; leaving the scope cancels children; tasks may borrow the caller's stack.

### 13.4 Suspension inference
The compiler infers which functions may suspend (calls through `dyn` are bounded by the function type's `uses` clause). The language server marks such calls with inlay hints (`uses suspend`). Public functions declare `suspend` in their `uses` clause (§14). Errors:
- a suspension point in a function whose effects do not include `suspend` (BYN6002);
- a **`Mutex` guard alive across a suspension point**;
- a **`@threadLocal` borrow alive across a suspension point**.

> Round 2 §15: `bayan format --explicit` is gone; inferred effects are inlay hints, never a stored form.

### 13.5 Data sharing
- `ref` borrows: one task at a time; passing `ref x` to a spawned task locks `x` until the group ends.
- Read-only borrows: any number of tasks.
- Shared mutation: `Mutex<T>`, atomics (sequentially consistent by default; weaker orderings explicit), channels (`Channel<T>`, `Io.first(…)`).
- Types containing raw pointers cannot cross threads without an explicit `unsafe` marker.

`Io.offload(op)` runs CPU-heavy work on the thread pool.

---

## 14. Effects and capabilities

One effect system covers allocation, suspension and package capabilities.

```csharp
public ParcelId book(Parcel parcel, by Carrier carrier)
    fails CarrierError uses net, alloc, suspend { … }

public u32 crc(ReadOnlySpan<u8> data) { … }   // no `uses`: no alloc, no suspend, no I/O
```

- **Closed effect list:**

  | Effect | Covers |
  |---|---|
  | `alloc` | Heap allocation |
  | `suspend` | Parking the current fiber (waiting on I/O or a timer) |
  | `net` | Any socket: TCP, UDP, Unix domain sockets, named pipes |
  | `fs` | Files and directories |
  | `clock` | Reading the time |
  | `random` | Randomness |
  | `env` | Environment variables and process-wide settings |
  | `process` | Spawning and controlling other processes |
  | `console` | Program input and output: stdin, stdout, and stderr written by `print` |

  The list grows only through spec revisions. An unknown name is BYN6003.
- **Effect aliases** name a set without adding a capability: `effect db = net, fs`. An alias expands to its primitives everywhere (capabilities, bounds, and `bayan expand` once it lands, §21.2). `std.db` declares `db`.
- **No `uses` clause means pure.** `noalloc`, `nosuspend` and `suspends` are removed (BYN6001).
- **Public functions declare their effects; private functions infer them,** as for error sets. Tests are private, so they infer. `main` and scripts infer too, bounded by the package capabilities. The entry function is exempt from BYN6002 whatever its visibility. There is no file-level or struct-level `uses`: a public signature is a contract, and an inherited effect would be invisible at the declaration. A function that uses an effect its signature does not declare is BYN6002.
- `panic` is **not** an effect.
- `fails` stays a separate clause because it carries a type.
- **Function-typed parameters with no `uses` are effect-polymorphic:** the call inherits the effects of the function passed. `map` inherits its closure's effects; there is no `tryMap` or `mapAsync`.

  ```csharp
  public List<U> map<U>((T -> U) f) uses alloc
  let names = users.map(it.fetchName())      // this call also uses net
  ```

  A function-typed parameter that the callee stores (it escapes the call) is not polymorphic: it takes the bound of the place it is stored in (`Event<T>.subscribe`, §16).
- **Stored function types, interface methods and event types declare an upper bound.** No `uses` there means pure. Implementations may use fewer effects. Exceeding the bound is BYN6005. An effect bound in a type (`Event<T> uses …`) is the only form of effect parameter; general effect variables are deferred.

  ```csharp
  interface IPricer {
      Money price(Order o) uses net, alloc
  }
  struct Checkout {
      Event<Order.paid> paid uses net
  }
  type Pricer = Line -> Money uses net
  type OrderPlaced = Event<Order.placed> uses net, alloc, suspend
  ```

- **Derived forms compute their effects** (§5.2): `-ed` adds `alloc` for heap-owning types, the allocating form derived from `xInto` adds `alloc`, `tryX` keeps the original's. `bayan expand` will show them (§21.2); inferred effects are inlay hints.
- **Logging is ambient:** allowed in pure functions, never allocates (the interpolated message is formatted through a handler into a fixed buffer, only when the level is enabled), escapes control characters, and is interceptable:

  ```csharp
  log($"weighing {order.id}")                // default level: info
  log($"retry {n} of {max}", as: .debug)
  ```

  `print` is program output, not diagnostics: it `uses console`.
- **Handlers use `provide`;** `context` remains only the value (`context.Io`). Handler names are the effects plus `log` (BYN6007 otherwise); `suspend` has no handler. Inner `provide` blocks override individual handlers and inherit the rest. Handlers are one-shot, so they compile to context passing at zero cost.

  ```csharp
  let net = FakeNet(label)
  provide net: net, clock: FakeClock(at: Date(year: 2026, month: 12, day: 1)) {
      try shipAll(order, by: carrier)
  }
  assert net.calls == 1

  let logs = LogRecorder()
  provide log: logs { … }
  ```
- **The test runner provides `console` per test as a capture buffer.** Output is shown when a test fails, or always with `bayan test --verbose`.

**Package capabilities are an upper bound:** `package.byn` declares `capabilities: [.fs, .net, .process, .env, .ffi]` (and, in a future GC tier, `.gc`, §9.8). A package without `.net` cannot contain any function that `uses net`, transitively (BYN6004). An update that adds a capability is flagged in the lockfile diff.

> Round 2 §10, carried over (GC): manifest fields use `:`; `gc` is a capability, not a mode.

> Round 3 §13: three mechanisms (`noalloc`, `nosuspend`, capabilities) for one idea become one checked clause, and the default is the cheapest promise. Codes: BYN6001, BYN6002, BYN6003, BYN6004, BYN6005, BYN6006.

> Revision 0002 items 5, 21: the entry function has no caller to declare effects to, so it is exempt from BYN6002; `suspend` parks the fiber itself and has nothing to intercept, so it has no handler. Codes: BYN6002, BYN6007.

> Revision 0002 item 17: `bayan expand` is deferred until after v0.1, so this text says what it will show. Codes: none.

---

## 15. Security

### 15.1 Trust: two layers

**Layer 1: sinks take dedicated types, never `string`.** Passing a `string` where a sink type is expected is BYN8003, with a fix-it to the literal or escaping constructor. This applies whatever the origin of the data, so it also blocks stored XSS from the program's own database.

```csharp
public i64 execute(ref this, Sql sql, rest ReadOnlySpan<ISqlValue> args) fails DbError uses db, suspend
public void render(Html h) …
public void run(Path program, List<Arg> args = []) fails ProcessError uses process   // optional setting

let users = try db.query<User>(sql"SELECT * FROM users WHERE name = $1", term).all()   // literal plus parameters
let page = Html.escape(name)                                                            // escaping constructor
```

Only parameterized `sql"…"` literals with `$n` placeholders are in scope now; interpolating prefixed literals (`sql"…{x}…"`, `html"…"`) stay deferred.

**Layer 2: `Untrusted<T>` for values from outside**, where escaping is impossible. An `Untrusted<T>` reaching one of these sinks without the required type is BYN8004:

| Sink | Required |
|---|---|
| File path segment | `PathSegment` (no `..`, no separators) |
| Redirect URL | `Url` with an allowlisted host |
| Outbound HTTP target | Host allowlist under `net` (SSRF) |
| Sizes, capacities, loop bounds | A `requires` bound |
| Regex pattern | Trusted, or `Regex.escape` |
| HTTP header values | `HeaderValue` (no control characters) |
| Process arguments | `Arg` (a leading `-` needs a trusted value) |
| Decoding | Depth and size limits in `fromJson` and `fromForm` |
| `expects` | Never reads an `Untrusted` value (BYN8006, §7.9) |

- **Sources** return `Untrusted<T>` in their signatures: request query, body, headers, cookies, route parameters, uploaded files, third-party API responses, files from user-chosen paths, stdin, command-line arguments, text from C. Startup environment and config, literals, constants and the program's own database rows are trusted.
- Operations on an `Untrusted` value return `Untrusted` (strings and structs derived from it). Facts like `isEmpty`, `length` and comparisons are clean, so branching on untrusted data is allowed.
- Filtering (`except`, `where`, patterns) preserves taint. **Only construction of a type with `requires` removes it.**
- `T.fromJson(untrusted)` returns a trusted `T` only if every field's type is either a type with `requires` or a non-text scalar (numbers, bools, units, plain unions); any free `string` or byte field makes the result `Untrusted<T>`.
- Explicit escape: `x.trustUnchecked() because "…"` (§4.4). `trust(x, reason: …)` is an input form (BYN1062).
- This model is provisional and must be exercised in the reference application.
- **Processes.** `Io.run(program, args: args)` never goes through a shell, and refuses `.bat`/`.cmd` files on Windows (`cmd.exe` re-parses their arguments). `Io.runBatch(path, args: args)` runs a batch file: it runs the system directory's `cmd.exe /d /e:ON /v:OFF /s /c` with the path and each argument in double quotes, and fails with `invalidInput` without starting anything when the path does not end in `.bat`/`.cmd`, has no `/` or `\` (a bare name would make `cmd.exe` search the current directory and `PATH`; write `.\build.cmd`), or the path or an argument holds NUL, CR, LF, `"` or `%`. Off Windows it fails with `unsupported`. The arguments of both are a sink: an `Untrusted<T>` program, path or argument (or a list, span or array of them) is a compile error (BYN2786). `Io.run` involves no shell, but an untrusted program path runs whatever the attacker names, and option injection (`--upload-pack=…` to `git`, `-oProxyCommand=…` to `ssh`) makes the program itself run attacker-chosen commands. `IoError` gains `invalidInput` and `unsupported`, so its codes are 1–7.
- **Standard streams.** `Io.stdout.write(text)` and `Io.stderr.write(text)` write without a newline (stdout is buffered with `print`). `Io.stdin.readAll()` returns `Result<string, IoError>`: the rest of standard input, bytes as-is (binary mode on Windows, not checked for UTF-8), allocated from `context.allocator`. For now it is a plain `string`, not `Untrusted<string>`.
- Terminal output and logs escape control characters in untrusted values.
- **Implemented so far:** `Untrusted<T>` has the C representation of `T`; `Untrusted(x)` wraps and `x.trustUnchecked() because "…"` unwraps, with a non-empty string literal as the reason; the old `trust(x, reason: "…")` form is BYN1062. There is no implicit conversion either way. Sources are not yet wrapped.

> Round 3 §17, O10: a dedicated sink type stops injection regardless of where the text came from; `Untrusted<T>` covers the sinks escaping cannot protect; an `expects` that trapped on outside data would let a request crash the program. Codes: BYN1062, BYN8003, BYN8004, BYN8005, BYN8006.

### 15.2 Secrets
`Secret<T>` and `@secret` fields: excluded from debug output, logs, `dbg`, `Json`, `Diff` and every derivation (formatted as `***`); no `==`, only `equalsConstantTime(other)`; zeroed on dispose. A derivation that would read one is a compile error unless the field opts in with `@json(expose) because "…"` (BYN8002); `debug` still prints `***`.

> Round 2 §2, §13: constant-time comparison is a named method so `==` cannot leak timing.

> Round 3 §16: exposing a secret is a bypass, so it takes a written reason. Codes: BYN8002.

### 15.3 Randomness and cryptography
`Random` (seeded, reproducible) and `CryptoRandom` are separate types; token, key and nonce APIs accept only `CryptoRandom`. Cryptography APIs are misuse-resistant (AEAD with nonce management; no ECB; raw block ciphers only in `unsafe`).

### 15.4 Resource limits
Decoders default to safe limits (JSON nesting depth 128, maximum size). Per-request arenas have a memory cap. Regexes (`std.regex`, after v0.1) run in linear time with no backtracking (no ReDoS); a pattern built from `Untrusted` input still needs `Regex.escape` (§15.2).

### 15.5 Invariants versus validation
Function `expects` contracts (§7.9) detect bugs and panic in every build mode. **Construction of a type with `requires` is the security boundary:** it is checked in every build, reports every violation, and is the only way to remove taint (§15.1).

> Round 2 §7: contracts are no longer skipped in `fast`. Round 3 §11 makes construction the validation boundary and `expects` the contract word.

---

## 16. Strings and collections conventions

**Naming follows the grammar (§5.2):** a plain verb acts on `ref this`; `-ed` names a changed copy or a view. The `-ed` form does not require a mutating verb to exist: an immutable type (`string`) has `trimmed()` and `lowercased()` with no `trim` or `lowercase`. A mutable type with both forms keeps both (`xs.sort()` / `xs.sorted()`).

| Mutating (`ref this`) | Result form (`-ed`, derived: a view if possible, otherwise a copy) |
|---|---|
| `append(x)`, `prepend(x)`, `insert(x, at:)`, `appendAll(xs)` | `appended`, `prepended` |
| `sort()`, `sort(by)`, `reverse()` | `sorted`, `reversed` |
| `remove(at: i)`, `remove(at: range)`, `remove(from: a, until: b)`, `remove(from: a, through: b)`, `removeWhere(f)`, `removeFirst()` → `T?`, `removeLast()` → `T?`, `replace(at: r, by: items)`, `fill(x)`, `clear()` | |

- Ordered collections (`List`, `Deque`) use `append` / `prepend`; unordered ones (`Set`, `Map`) use `add`.
- Costs are stated per method: `prepend`, `insert` and `removeFirst` are O(n) on `List` and O(1) on `Deque`.
- **Size:** `length` (O(1), `i64`) on every collection; `count(p)` only counts the elements matching a predicate (strings: §3.2).
- **Queries** (never allocate): `length`, `count(p)`, `isEmpty`, `hasItems`, `first` / `last` (`T?`), `xs[i]` (panics out of range), `tryGet(at: i)` (`T?`), `contains`, `indexOf` (`i32?`), `indexWhere`, `any`, `all`.
- **Views** (never allocate): slices `xs[1..4]`, `xs[1..^1]`, `take`, `skip`, `takeLast`, `skipLast`, `stride`, `chunks`, `windows`, `splitAt`. C#'s `Substring(start, length)` migrates to `s[start..(start + length)]`.
- **Lazy iterators** are structs with inlined lambdas: `where`, `select`, `sumBy`, `minBy`, `maxBy`, `any`, `all`, `groupBy`; nothing allocates until `copy()` or `List(seq)`.
- **New data** (allocate from `context.allocator`): `distinct()`, `join(sep)`, `join(sep, by: f)`, `flatten()` (Form I, not participles), spread `[..a, ..b]`.
- Out-of-range slices panic in safe builds; `trySlice(a..b)` returns `Span<T>?`.
- Read-only queries and views take `ReadOnlySpan<T>` parameters (a `Span<T>`, `List<T>`, array or, for bytes, `string` converts implicitly).
- **Queries beside `where`:** `except` subtracts a pattern from a sequence and narrows its element type (`events.where(it.isValid) except .heartbeat`, §6.8).
- `fromBounds(start, until: end)` names the exclusive end.
- **Views and copies (no `toX` or `asX` members):** `xs.span` is a read-only view (a property, allowed on a `let`); `xs.mutableSpan()` is mutating access (`ref this`, `var` only; calling it on a `let` is the usual `ref this` error); `xs.copy()` returns an owned `List<T>` and `uses alloc`, on spans and lists alike; a sequence becomes a list with `List(seq)`; a cursor's own member `rows.all()` consumes it. While a `mutableSpan()` view is live, no other access to `xs` is allowed (§9.4). Strings have the views `s.bytes`, `s.runes` and `s.utf16` (§3.2).
- **Lookups:** `get(at:)` is declared once as the operator method behind `[]`, so call sites write `xs[i]`; `tryGet(at:)` is derived and returns `T?`. Lists and maps share the shape: `prices.tryGet(at: sku) ?? 0g` replaces `getOr`.
- Collections copy explicitly: `xs.copy(to: alloc)` for `List`, `Map` and `StrBuf` returns an independent value of the same type (not allowed when an element, key or value holds a `List`, `Map`, `StrBuf` or `Owned`).
- Compile-time work: bounds checks removed when provably in range; constant slices of fixed arrays have type `T[N]`; `xs[^1]` lowers to `xs[xs.length - 1]`; slice chains fuse.

**Literals:**

```csharp
let xs: List<i32> = [1, 2, 3]
let p: Set<Permission> = [.read, .write]
let s: Span<i32> = [1, 2, 3]                         // stack, no allocation
let all = [..xs, ..more, 4]                          // spread
let stock: Map<string, i32> = ["A-1": 12, "B-2": 0]  // map literal
let prices: Map<Sku, Money> = []
let fixed = [1, 2, 3]                                // no target type: i32[3] on the stack
let headers = ["Accept": "*/*"]                      // no target type: Map<string, string>
```

- Not in v0.1: everything that takes a lambda (`where`, `select`, `sumBy`, `removeWhere`, `sort(by)`, `join(sep, by: f)`), `Deque`, `Set` (and its literals), slices on lists, `prepend`, `insert`, `sorted`.

> Round 2 §11: one size name and verb/participle pairs that follow §5.2; `first`/`last` return `T?` instead of a parallel `tryFirst`/`tryLast`. Codes: BYN2104, BYN2230, BYN2235, BYN2236.

> Round 3 deliverable 4, §12, §15, §27: the size name stays `length`, as in round 2, so `count(p)` reads as a predicate count only. `removeRange` and `replaceRange` give way to particle forms (stdlib overloads, J65, kept because `until` and `through` read well at call sites; guard 1 governs the language's spellings, not library APIs, revision 0002); `getOr` becomes `tryGet(at:) ?? x`; `toList()` and `asSpan()` would spell conversions that already have a form. Codes: BYN2230, BYN5009.
- Maps: reading `map[k]` panics if missing; `map.tryGet(at: k) ?? fallback` reads without inserting; `ref map.slot(k)` gets or inserts a default (requires `V : default`). Keys are §3.5's: scalars, `string`, plain enums, `Symbol`, and tuples and structs of those; every member works the same on each, and a map literal's string keys become symbols where the key type is `Symbol`.
- `Grid<T>`-style types accept multi-dimensional ranges: `grid[1..^1, 1..^1]`.

### 16.1 Events

`Event<T>` is in-process notification: the raising code doesn't know who listens.

```csharp
public struct Event<T> {
    public i64 subscriberCount
    public Subscription subscribe(ref this, by (T -> void) handler) uses alloc
    public void raise(T value)
}

public struct unique Subscription {
    public void cancel(move this)          // or drop it
}

type OrderPlaced = Event<Order.placed> uses net, alloc, suspend

struct OrderService {
    public OrderPlaced placed
    public Order.placed place(ref this, move Order.draft order, by IOrders orders)
        fails OrderError uses net, alloc, suspend {
        let o = try orders.insert(move order)
        placed.raise(o)
        return o
    }
}

let mail = service.placed.subscribe(by: o => mailer.sendConfirmation(for: o))
```

1. Handlers run in subscription order, synchronously, on the raising fiber.
2. Handlers cannot fail; they handle or log their own errors.
3. Subscribing during a `raise` takes effect from the next `raise`.
4. `raise` never allocates; `subscribe` allocates one slot.
5. A `Subscription` borrows its event and cannot outlive it. Dropping it unsubscribes, which closes C#'s `+=`-without-`-=` leak at compile time.
6. The handler effect bound is part of the event's type (§14). `subscribe` stores the handler, so its parameter takes this bound rather than the polymorphic parameter rule, and `raise` has the bound's effects. No bound means pure handlers.
7. Events are named for what happened (`placed`, `changed`, `low`); there is no `on` prefix (BYN5010).

Cross-process pub/sub is not an `Event`: it is a stream iterated on its own fiber, `for try msg in bus.messages(from: "orders.placed") { … }`.

> Round 3 §26: an event is a field of a library type, not a language feature, and its handler bound is checked like any stored function type. Codes: BYN5010, BYN6005.

### 16.2 `std.math` and numeric members

- Scalars are values: they have nouns and `-ed` copies only (`x.abs`, `x.sqrt`, `x.floor`, `x.ceiling`, `x.rounded()`, `x.truncated()`, `x.clamped(from: lo, through: hi)`, `x.pow(n)`). To change a scalar, reassign it.
- `rounded()` means nearest, ties away from zero; `rounded(ties: .even)` and `rounded(digits: 2)` are the options. `floor`, `ceiling` and `truncated` name a direction.
- Aggregates have verb and copy pairs: `v.normalize()` / `v.normalized()`, `v.scale(by: 2.0)` / `v.scaled(by: 2.0)`, `xs.sort()` / `xs.sorted()`.
- Collection queries: `sum`, `product`, `average`, `min`, `max` trap on an empty collection; `tryAverage`, `tryMin`, `tryMax` return `T?`; `min(by: key)`, `max(by: key)`; `percentile(at: 95%)`.
- Free functions: `min(rest …)`, `max(rest …)`; `min(..xs)` is a paraphrase of `xs.min`. Also `print(rest ReadOnlySpan<Display> items, to Stream stream = .stdout) uses console` and `Path.join(rest ReadOnlySpan<PathSegment> parts)`.
- **Paths (spec 014, implemented slice):** `bool Path.isRooted(string path)`, `string Path.fileName(string path) uses alloc` and `string Path.relative(string from, to string path) uses alloc` need no import and give what .NET's `Path.IsPathRooted`, `Path.GetFileName` and `Path.GetRelativePath` give on the same OS. On Windows `\` and `/` both separate and drive letters (`C:\x`, `C:x`) and UNC paths (`\\server\share\x`) are understood; elsewhere only `/` separates. `relative` makes both paths full against the working directory first, compares case-insensitively on Windows and macOS (case-sensitively elsewhere, as .NET does), returns a `..`-prefixed path for a target outside `from` and the target itself when the roots differ, and panics on an empty path. Both string results are fresh copies from `context.allocator`; none of the three takes an `io`, `fs` or `process` effect.
- **Imports:** members of the numeric and collection types live in `core` and need no import; `min` and `max` are in the `core` prelude. Vectors, matrices, statistics and geo live in `std.math` and need an import.

> Round 3 §27: scalars read as nouns and aggregates as verb pairs, so the same rules (§5.2) cover math. Codes: BYN5001.

> Revision 0002 item 28: `print` takes `to Stream stream = .stdout`, so stderr is written with `print(p, to: .stderr)` through the same formatting; `Stderr.write` stays the raw writer. Codes: none.

### 16.3 `std.db` (spec-only)

The database layer collapses the C# extension-method explosion (684 `IDbConnection` extensions from sync/async × parameters × options × eight result shapes × mapping × arity × stored procedures):

| C# axis | Bayan mechanism |
|---|---|
| Sync and async | One function; `suspend` is an effect, fibers park on I/O |
| Parameters or none | `rest ReadOnlySpan<ISqlValue> args`; an empty span means none |
| Options or none | Named defaults after `rest`; the transaction is the receiver |
| Result shapes | One `unique Rows<T>` cursor with `first`, `single`, `all` members |
| Strict or partial mapping | `mapping: .partial` |
| Multi-entity arity | Tuple row types: `query<(Order, Customer)>` |
| Stored procedures | `call<T>(proc)` returning the same `Rows<T>` |
| `CancellationToken` | The enclosing fiber's deadline or scope |

```csharp
effect db = net, fs

public interface IQuerySource {                    // implemented by Connection and Transaction
    Command prepare(ref this, Sql sql, Millis timeout = 30s) fails DbError uses db, suspend
    i64 bulkCopy<T>(ref this, ReadOnlySpan<T> rows, bool ignoreConstraints)
        where T : IEntity fails DbError uses db, suspend

    Rows<T> query<T>(ref this, Sql sql, rest ReadOnlySpan<ISqlValue> args,
                     Millis timeout = 30s, Mapping mapping = .strict)
        where T : IRow fails DbError uses db, suspend { … }
    i64 execute(ref this, Sql sql, rest ReadOnlySpan<ISqlValue> args, Millis timeout = 30s)
        fails DbError uses db, suspend { … }
    T get<T>(ref this, T.Id id) where T : IEntity fails DbError uses db, suspend { … }
    T? tryGet<T>(ref this, T.Id id) where T : IEntity fails DbError uses db, suspend { … }
    T.Id insert<T>(ref this, T row) where T : IEntity fails DbError uses db, suspend { … }
    i64 update<T>(ref this, T row) where T : IEntity fails DbError uses db, suspend { … }
    i64 delete<T>(ref this, T.Id id) where T : IEntity fails DbError uses db, suspend { … }
}

public struct unique Rows<T> where T : IRow {
    public bool moveNext(ref this) fails DbError uses db, suspend
    public T current
    public T first(move this) fails DbError uses db, suspend            // .noRows when empty
    public T? tryFirst(move this) fails DbError uses db, suspend
    public T single(move this) fails DbError uses db, suspend           // .noRows, .moreThanOne
    public T? trySingle(move this) fails DbError uses db, suspend
    public List<T> all(move this, i64 capacity = 0) fails DbError uses db, suspend, alloc
    public void readInto(move this, ref List<T> dst) fails DbError uses db, suspend, alloc
}

let users = try db.query<User>(sql"SELECT * FROM users WHERE age > $1", 18).all()
let user = try tx.query<User>(sql"SELECT * FROM users WHERE id = $1", id).tryFirst()
for try u in db.query<User>(sql"SELECT * FROM users") { … }          // streams
```

- A driver implements two members (`prepare`, `bulkCopy`); everything else is a default method.
- `@derive(Row)` generates `T.checkShape` (one-time shape guard on the first row), `T.fromRow` (reads by ordinal), and for entities `T.Id`, `T.selectById`, `T.insertReturningId`, `T.updateById`, `T.deleteById` and `row.values`.
- The split point of a tuple row type is known at compile time because `sql"…"` literals are validated against the schema snapshot; there is no `splitOn`.
- `mapping: .partial` gives up build-time column checking; `.strict` keeps it.
- A blocking C call made by a driver must be `@blocking`.

> Round 3 §27: one function, one cursor and one effect alias replace the extension-method matrix; `rest` and `Sql` literals carry the parameters. Codes: BYN8003, BYN8005.

> Revision 0002 item 4: `prepare`'s `timeout` is a named optional like the other members', and `IRow`'s statics take `as Mapping mapping`, so every shape follows the particle rule; `bool` parameters are named parameters. Codes: none.

### 16.4 Text

**Model.**
- `string` is immutable UTF-8. It either borrows its bytes (a literal, a request body, a slice) or owns them; `s.copy()` and `s.copy(to: arena)` make owned copies.
- `s.length` is the byte length, O(1). There is no integer indexer; `rune` is the 32-bit Unicode scalar.
- A string has no mutating members; text is built with `StrBuf`, interpolation or `joined`. String `+` stays deferred.
- An `-ed` result that is a sub-range of the original (`trimmed()`, `parts`, slices) is a borrowed view and never allocates. Only results with new text allocate.
- `$"…"` allocates, except passed directly to a formatting sink: `print`, `log`, `panic`, `StrBuf.append`.

**Queries and views**

| Member | Returns | Alloc | Notes |
|---|---|---|---|
| `length` | `i64` | no | Bytes |
| `isEmpty`, `isAscii` | `bool` | no | |
| `runeCount` | `i64` | no | O(n) |
| `bytes`, `runes`, `utf16` | views | no | `bytes` is a `ReadOnlySpan<u8>` |
| `lines` | view of `string` | no | `\n` and `\r\n` |
| `parts(by: sep)`, `parts(by: sep, limit: n)` | view of `string` | no | Splitting |
| `s[a..b]` | `string` (borrowed) | no | Byte range; traps when not on a rune boundary |
| `position(x)`, `position(x, from: i)`, `lastPosition(x)` | `i64?` | no | Byte position of the first or last occurrence |
| `count(x)` | `i64` | no | Non-overlapping occurrences |
| `words`, `graphemes` | views | no | `std.text` (Unicode segmentation) |

**Assertions:** `contains(x)`, `startsWith(x)`, `endsWith(x)`, `equals(t, as: .caseless)`. Plain equality is `==`. After v0.1, `matches(re)` and `replaced(re, by: y)` arrive with `std.regex` (§15.4).

**Copies**

| Member | Alloc | Notes |
|---|---|---|
| `trimmed()`, `trimmed(from: .start / .end)`, `trimmed(by: [' ', '\t'])` | no | View |
| `lowercased()`, `uppercased()`, `caseFolded()` | yes | Invariant |
| `camelCased()`, `pascalCased()`, `snakeCased()`, `kebabCased()`, `slugged()` | yes | Identifier and URL forms; derivations use them |
| `replaced(x, by: y)` | yes | |
| `truncated(to: n)`, `truncated(to: n, by: "…")` | yes | Cuts on a grapheme boundary |
| `padded(to: n, by: ' ')` | yes | `width:` in a spec is usually better |
| `wrapped(at: 80)` | yes | |
| `repeated(n)` | yes | |
| `escaped(for: .json / .csv / .shell)` | yes | Display escaping; sinks stay typed (`Html`, `Sql`, `Arg`) |
| `normalized(as: .nfc / .nfd)` | yes | `std.text` |
| `xs.joined(by: sep)` | yes | On a collection of strings |

Prefix and suffix views (`prefix(through:)`, `dropped(first:)`) are not named yet: the Sūq friction log decides, and byte slices cover them meanwhile.

**Building: `StrBuf`**

```csharp
var buf = StrBuf(capacity: 256)
buf.append($"order {order.id}: ")       // formats straight into the buffer
buf.append(total)                        // any Display value
buf.insert("!", at: 0)
buf.clear()
let text = buf.finish()                  // a loan on the buffer, no copy (revision 0018)
```

**Conversions:** `string(x)` (`Display`), `string(n, as: .hex / .binary / .octal / .decimal)`, `T.parse(text)` / `T.tryParse(text)` with the same `as:`, and the codecs. Every binary-to-text conversion is a codec, like `Hex` and `Base64`; `string(x)` is only the `Display` conversion, for every type, including byte spans (`[104, 105]`):

```csharp
let s = try Utf8.decode(bytes)                   // fails Utf8Error with the position of the bad byte
let s = Utf8.tryDecode(bytes)                    // string?
let s = Utf8.decode(bytes, invalid: .replace)    // lossy: U+FFFD, never fails
let s = try Latin1.decode(bytes)                 // kit
let s = try Utf16.decode(units)                  // std.text
let hex = Hex.encode(digest)
let raw = try Base64.decode(text)
```

`string.fromUtf8`, `try string(bytes)`, `string.tryFrom(bytes)` and `string(bytes, as: .latin1)` are renamed under BYN2230 to the codec forms.

**Literals:** `"…"`, `$"…"`, `b"…"` (bytes), `sql"…"` with `$n`. `html"…"` and interpolating `sql"…{x}…"` stay deferred. A raw multi-line literal is `"""…"""` (§2.3).

**Culture.**
- **No ambient culture.** No operation reads a global or per-thread locale; a call's result never depends on the machine.
- **`std` is ordinal or invariant:** `==`, `<`, `compare`, `contains`, `startsWith`, `endsWith`, `position` and `count` compare bytes (UTF-8 byte order is code-point order). `lowercased()` and `uppercased()` use Unicode's invariant mappings (`"I".lowercased()` is always `"i"`). `equals(t, as: .caseless)` uses Unicode case folding without the Turkic special cases.
- **Culture is explicit, through `kit.intl`,** with the `for` particle:
  ```csharp
  let tr = Culture("tr-TR")
  "FILE".lowercased(for: tr)                  // "fıle"
  names.sorted(by: Collator(for: tr))          // dictionary order
  let shown = string(price, for: Culture("ms-MY"))
  ```
  `titlecased()` exists only in `kit.intl`, because title case is culture-dependent.

**Formatting.** `Display` has one required member, in the `xInto` pattern:

```csharp
public interface Display {
    void formatInto(ref StrBuf out, as FormatSpec spec)
}
```

`string(x)`, interpolation and `StrBuf.append(x)` all call it; `@derive(Display)` generates it for plain structs. Specs are named arguments inside the interpolation hole (canonical):

| Spec | Applies to | Example |
|---|---|---|
| `as: .hex / .binary / .octal / .decimal` | Integers | `{n, as: .hex}` → `ff` |
| `digits: n` | Floats, `Money` | `{x, digits: 2}` → `3.14` |
| `width: n`, `fill: c`, `align: .start / .end / .center` | All | `{id, width: 6, fill: '0'}` → `000042` |
| `grouping: true` | Numbers | `1,234,567` (invariant) |
| `case: .upper` | Hex, booleans | `FF` |
| `as: .iso / .date / .time` | Dates and times | `2026-10-24T09:30:00Z` |
| `as: .debug` | All | Developer view: quoted strings, field names, `[104, 105]` |
| `for: culture` | Numbers, dates, `Money` | `kit.intl` only |

Compact input forms, expanded by `bayan format` (BYN1068): `{n:hex}`, `{n:binary}`, `{n:octal}`, `{x:.2}`, `{d:iso}`, `{x:debug}` (§21.5).

**Scope.**
- v0.1: the model; `length`, `isEmpty`, `isAscii`, `runeCount`, `bytes`, `runes`, `utf16`, `lines`, `parts`, slices, `position`, `lastPosition`, `count`; the assertions (`equals(as: .caseless)` ASCII-only until `std.text`); `trimmed`, ASCII `lowercased`/`uppercased`, `replaced` (literal), `joined`, `repeated`; `StrBuf`; `string(x)`, `string(n, as:)`, `parse`/`tryParse`; `Utf8` and `Hex`; `Display.formatInto` and the specs `as`, `width`, `fill`, `align`, `digits`, `debug`; BYN1068.
- After v0.1 (`std.text`): `words`, `graphemes`, full Unicode case mapping and folding, `normalized`, `truncated` (needs graphemes), `wrapped`, the identifier-case members, `slugged`, `escaped`, `Utf16.decode`, `Base64`, regex `replaced`, `grouping`, date specs.
- `kit`: `kit.intl` (`Culture`, `Collator`, `for:` on case, formatting and sorting, `titlecased`), `Latin1` and other encodings.

> Revision 0003 §3–§6: one place for the string model, the API, culture and formatting (round 2's pending `format()` item); no operation depends on the machine's locale, and specs are named arguments like every other option. Codes: BYN1068, BYN2230.

---

## 17. Numerics

| Build | `a + b` on overflow |
|---|---|
| debug, safe, fast, small | Traps |

Overflow is a bug and traps in every build mode. `+% -% *%` wrap; `+| -| *|` saturate; compound forms `+%=` etc. The compiler removes checks it can prove unnecessary (loop counters, values of distinct types with `requires` ranges; not in v0.1).

Floats are IEEE 754; C is emitted with `-ffp-contract=off`; `@fastMath` opts in per function. Derived equality and hashing on floats use IEEE `totalOrder` (so a struct containing NaN equals itself, and `0.0` and `-0.0` differ there, unlike `==`).

> Round 2 §3: replaces "wraps in fast"; an arithmetic bug must not change meaning with the build mode.

---

## 18. FFI

```csharp
@link("sqlite3")
extern c {
    c_int sqlite3_open(cstr filename, out Sqlite3* db)
    @blocking c_int sqlite3_step(Sqlite3Stmt* stmt)
}

@cLayout
struct Timespec {
    c_long tvSec
    c_long tvNsec
}

@exportC("tally_settle")
public c_int settleC(…) { … }
```

- Pointer types are postfix: `T*` (`Sqlite3* db`, `u8* buf`). A pointer C must not write through is `readonly T*` (C `const char*` is `readonly u8*`). The prefix forms `*T`, `*mut T` and `*const T` are input forms (BYN1066).
- Pointer types are allowed only in `extern` declarations (BYN1067); `unsafe because` blocks extend the rule when they first parse (not v0.1).
- `comptime CImport("header.h")` translates headers.
- Calling `extern` functions requires `unsafe`; libraries wrap them once in safe APIs.
- `build.addCSource(…)` compiles C dependencies from source via `zig cc`.
- `kind: .sharedLib` / `.staticLib` generates a C header automatically.
- Callbacks from C onto fibers switch to the thread stack. A panic in a callback aborts.
- `cstr` → `string` validates UTF-8; non-validating conversion only in `unsafe`. Exported signatures use C-compatible types; handing pointers to C requires `unsafe`.
- Nul-terminated UTF-16 strings (Windows APIs) follow the same form as `cstr`: the conversion to `string` validates, and the non-validating one needs `unsafe`. `s.utf16` (§3.2) is the view for UTF-16 code units.

> Round 2 §10: manifest fields use `:`.

> Round 3 §2, §5, §6: extern declarations are type-first like C signatures and every other declaration (revised 2026-10-22), and attributes use `@` like everywhere else. Codes: BYN1043, BYN1045.

> Handoff Question 120 (decided 2026-10-24): postfix `T*` reads like the C signature it mirrors in a type-first declaration; `readonly` already means "no writes through this" elsewhere, so it spells C's `const`. Codes: BYN1066 (revision 0001).

> Revision 0002 item 20: a pointer outside `extern` would let unchecked memory into safe code, so the C boundary is the only place one is written. Codes: BYN1067.

> Round 3 §27: wide strings replace `toUtf16()` with one FFI form beside `cstr`, so UTF-16 interop needs no conversion member. Codes: BYN2230.

---

## 19. Packages, editions and builds

### 19.1 Modules
A directory is a module (`src/orders/` → `app.orders`). `import app.orders` / `import app.users as u`. No circular imports.

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
    test: Tests { lines: 100, branches: 100, errorPaths: 100, mutation: 75, nightly: [.fuzz] },
    style: Style {
        indent: 4,
        braces: .sameLine,        // or .nextLine
        maxLine: 100,
        trailingCommas: true,
        booleans: .symbols,       // or .words: `and or not`
        primitives: .sized,       // or .csharp: `int`, `long`, `byte`, …
    },
    grammar: Grammar {
        verbs: ["upsert", "reindex"],
        nouns: ["sku", "kyc"],
        forms: ["upsert": "upserted"],
    },
}
```

`capabilities` is an upper bound on the effects of every function in the package (§14). `Tests` holds the CI gates (§20): `lines` and `branches` (minimum percentages), `errorPaths`, `mutation`, `nightly`, and `summary` (bool, default `true`; `false` turns off the default coverage summary). `diffCoverage` is accepted and not enforced yet. A run below a minimum fails and names what is missing. `style` holds the project's formatting choices (defaults in `docs/bayan-style.md`); `bayan format` reads it. Style is never compiler-enforced, and grammar rules (§5) cannot be configured here. `grammar` is the project dictionary (§5.2): domain words the checker does not know, as `verbs` and `nouns`, with irregular derived spellings in `forms`; it is merged with the built-in word list and adds words, never exemptions. The `bayan new` templates use the same `:` syntax. Formatter behaviour is not in v0.1.

> Round 2 §10, §18: `package.byn` is a Bayan struct literal, so it uses `:` like every literal; style is per-project data. Codes: BYN3104.

> Round 3 §8, §13, §28: both boolean spellings and both primitive spellings are canonical; `style.booleans` and `style.primitives` pick one per project and `bayan format` rewrites the other; capabilities bound effects instead of standing beside them. Codes: BYN6004.

> Round 3 §12, O15: domain words are declared once per project rather than suppressed member by member. Codes: BYN5014.

The registry publishes JSON metadata, and `bayan pkg export --json` exists, so outside tools never parse Bayan.

### 19.3 Dependencies and supply chain
- Minimal version selection (Go). Lockfile with content hashes.
- Verified namespaces (domain or handle). Private registries scoped by namespace.
- **Isnād:** each version carries a signed provenance chain (author → source commit → reproducible build → registry), graded by strength.
- **No install scripts.** `build.byn` is sandboxed: declared tools and paths only, no network.
- `bayan audit`: advisories, plus every `unsafe` block, `extern` declaration and C source per dependency, and every `because` bypass (§4.4) with its reason.

### 19.4 Editions and stability
`edition: 2027`. Editions change syntax and lints, never the ABI; packages on different editions link together. `core` follows the Fundamento rule: once released it only grows. Unproven APIs live in `std.experimental`.

> Round 2 §10: manifest fields use `:`.

### 19.5 Build modes and reproducibility
`debug`, `safe`, `fast`, `small`. Builds are reproducible: no timestamps, stripped paths, deterministic comptime and generators; CI verifies by building twice.

---

## 20. Testing

```csharp
test "parse rejects missing @" { assert Email.parse("x.y") is .invalid }

@cases("a@b.co", "x.y@z.io")
test "parse accepts valid" (string s) { assert Email.tryParse(s) is not null }

@cases((0, false), (1, true), (100, true), (101, false))
test "quantity bounds" (i32 input, bool valid) { … }

@cases(discountCases)                      // from a const list (xUnit MemberData)
test "discount" (Money total, i32 percent, Money expected) { … }

@pairwise
test "parses across currencies" (Currency c, bool negative, bool fractional) { … }

bench "parse ledger" { measure Ledger.read(sample) }
property "balances sum to zero" (Span<Expense> xs) { … }

@nightly
fuzz "reader never crashes" (string text) { _ = Ledger.read(text) catch return }

model test "cart behaves like a list" {
    real = …
    model = …
    ops = [Cart.addOp, …]
    check …
}
```

- **`assert expr`** is a statement (no parentheses). A failure prints **power-assert** output: the value of every sub-expression.
  ```
  ✗ cart total after two adds                    cart.byn:31
    assert cart.subtotal == { cents: 750 }
           │    │        │
           │    │        false
           │    { cents: 500 }
           Cart { lines: [CartLine { sku: "A-1", qty: 2, … }], coupon: null }
  ```
- **`assert f() fails E.case`** checks that the call fails with that case; `assert f() fails E` accepts any case of `E`. When the call succeeds or fails with another case, the output is the power-assert tree of the call and what it returned or failed with. The call must be able to fail (BYN2605), and the pattern must match its error set (BYN2411).
- **No fluent assertion API** in the standard library; plain expressions plus power output cover it.
- **`@cases(...)`** takes single values, tuples, or a const list. Each case is a separate test, reported with its values: `✗ quantity bounds (input: 101, valid: false)`. The value count must match the test's parameters, and every value must be a constant. `@cases(constList)` is not in v0.1.
- **Doctests:** fenced code in `///` comments is compiled and run by `bayan test`. `assert` lines are checked; a `// prints: …` comment is compared with the output. Not in v0.1.
- **Benchmarks:** `bench "name" { measure expr }` reports time per op, allocations per op and variance, with seeded randomness. `bench` and `measure` are contextual words. Not in v0.1.
- **`property`** tests generate inputs from the parameter types' `requires` rules (§7.9) and shrink failures into `testdata/`. Not in v0.1.
- **Laws** are opt-in, never imposed, and never feed the optimizer: the release binary is byte-identical with laws on or off. When on, laws generate property tests, and a violated law fails `bayan test` with a minimal counterexample.
- Tests live in the same file or in a sibling `x.test.byn` (same module, private access). Integration tests in `tests/` see only the public API. Release builds strip tests.
- Tests run with a **simulated context** by default (fixed clock, seeded randomness, `Io.Simulated`). The test runner provides `console` per test as a capture buffer: output is shown when the test fails, or always with `bayan test --verbose` (§14).
- **Derived law tests** run automatically (round trips, `tryX` consistency, equality/hash, ordering, idempotent `ensureX`, `Xed()` non-mutation, codec round trips, `Builder`, `Op`) plus **boundary tests from `requires`** and **differential tests of specializations against the general version**.
- A function without `uses alloc` is proven allocation-free at compile time (§14); `assertNoAlloc` checks at runtime.
- `bayan new test X` scaffolds one case per success and error variant; `todo(…)` reports **pending**, never passing.
- Coverage: lines, branches and **error paths**. An error path is a `fails` variant of a function that a test makes the function return, whether the function raises it with `fail` or passes it on through `try`; the detailed report names where it came from. `bayan test` prints a one-line coverage summary in Debug builds (package opt-out `summary: false`); `bayan test --coverage` prints the full report, a per-line listing and `bayan-quality.json`. A test killed on timeout leaves its coverage unknown, never uncovered. `--sanitize` runs show coverage only with `--coverage`. `bayan build` reports public functions no test reaches.
- Mutation testing compiles every mutant into one build (mutant schemata) and picks one per run: `bayan test --mutate`. Each mutant is killed, survived, timed out (counted as killed), not reached, or marked. The report names each surviving mutant's file, line, column, original and mutated text, suggests the missing case, and gives build time, mean and maximum time per mutant, and per-mutant time as a share of the build. `bayan mutate --binary <exe> --sites <map> -- <cmd>` runs an outside command per mutant against a prebuilt mutant binary. A mutation that would not type-check is never emitted; the report counts these as not emitted. The operators:

  | Operator | Changes |
  |---|---|
  | `boundary` | `<` ↔ `<=`, `>` ↔ `>=` |
  | `relational` | `==` ↔ `!=`, `<` ↔ `>=`, `>` ↔ `<=` |
  | `logical` | `&&` ↔ `\|\|` |
  | `condition` | the condition of `if`, `unless`, `while`, do-while, a switch guard, `stmt if c` → its negation |
  | `bool` | `true` ↔ `false` |
  | `arithmetic` | `+` ↔ `-`, `*` ↔ `/`, `%` → `*` |
  | `literal` | an integer literal `n` → `n + 1`, `n - 1` |
  | `negation` | unary `-x` → `x` |
  | `statement` | a call with no result, an assignment or a compound assignment is removed |
  | `emptyReturn` | `return x` → the type's empty value, where the type has one |
  | `variant` | `fail E.a` → another variant of `E` with the same payload |

  Each change is one mutant; an operator that does not apply to the operand types is not emitted.
- `@unobservable because "…"` on a statement marks that statement's mutants as ones no test can observe; an operator argument narrows it to one operator. Marked mutants still run: a marked mutant a test kills, or a mark with no mutant in its statement, is a warning in the report. The reason is required (BYN8001).

  ```csharp
  i32 clamp(i32 x, i32 lo, i32 hi)
      expects lo <= hi {
      @unobservable(boundary) because "x == hi returns hi either way"
      return hi if x >= hi
      return lo if x <= lo
      return x
  }
  ```
- Fuzz findings are minimized into `testdata/fuzz/<test>/` and require a name and a reason to be committed.
- `bayan test --explore` enumerates interleavings for small concurrent tests; `--sanitize` builds with ASan/UBSan.
- `snapshot(x)` with `bayan test --update`. `Io.Recording` / `Io.Replay` capture and replay traffic (untrusted fields redacted).
- CI gates live in `package.byn` (§19.2); diff coverage is preferred over global coverage.

> Round 2 §16, carried over (laws): `assert` is a statement with power output instead of an assertion API; `[Cases]` (now `@cases`, round 3 §4) scales to tables; `requires` drives boundary and property tests. Codes: BYN2773, BYN2774, BYN2775.

> Sūq review A6: asserting a failure took five lines (`catch` then `panic`); `fails` is the word the signature already uses. Codes: BYN2411, BYN2605.

> Round 3 §6, §14: test attributes use `@` (`@cases`, `@pairwise`, `@nightly`); allocation freedom comes from the absence of `uses alloc`. Codes: BYN1045, BYN6001.

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
| `bayan format` | Format to canonical form and rewrite input forms (§21.5); reads `style` from `package.byn` (§19.2). `--check` writes nothing, prints each file that would change and exits 1 if any would. `--prose`, `--from-prose`, `--check-prose` are prose translation (§22), not in v0.1 |
| `bayan lint`, `bayan doc`, `bayan repl`, `bayan lsp` | |
| `bayan pkg` | Packages (`add`, `audit`, `export --json`) |
| `bayan expand X` | Show every derived member and the derived effects of each function; deferred (after v0.1) |
| `bayan features --used` | Report the language features a build used (feeds the reference-app coverage matrix) |
| `bayan audit` | List every `because` bypass, `unsafe` block, `extern` declaration and C source, with reasons (§19.3) |
| `bayan explain CODE` | Long explanation of a diagnostic |
| `bayan migrate` | Rename casing, upgrade editions, apply `@replaced` |
| `bayan new test X` | Test scaffolds |

Every command takes `--color auto|always|never` (before any `--`). `auto` colors output that goes to a terminal, unless `NO_COLOR` is set. `bayan test --changed` runs only the tests of modules with a file that differs from `HEAD` (or `--base <ref>`, which implies `--changed`), untracked files included; a changed `package.byn` runs everything. It uses git read-only.

`bayan fmt` is not a command; its message names `bayan format` (HANDOFF §29).

`check`, `build`, `run` and `test` take `--diagnostics json`: each diagnostic goes to stderr as one JSON object per line, with `code`, `severity`, `file`, `span` (byte offsets `start`/`end` plus one-based `line`/`column`/`endLine`/`endColumn` counted in Unicode scalars), `message`, `label`, `help` and `fixes` (each a `span` and a `replacement`). `file` and `span` are null for diagnostics without a location.

Exit codes, documented in `bayan --help`:

| Code | Meaning |
|---|---|
| `0` | Success |
| `1` | Compile errors |
| `2` | Usage errors (unknown command or option, missing input path) |
| `3` | Test failures, or a coverage or mutation score below its manifest minimum (`bayan test --coverage`, `--mutate`) |
| `4` | Toolchain problems (no C compiler, unwritable cache, C compiler failure) |

`bayan run` uses the CLI's codes until the program starts (1 for compile errors, 4 for toolchain problems), then passes the program's own exit code through unchanged, as `cargo run` and `go run` do. It therefore cannot tell a build failure from a program that exits 1; anything that needs the distinction runs `bayan build` first or reads `--diagnostics json`.

The language server runs inside the compiler process. Display-only views (inferred types, inferred error sets and effects, expanded derived members) are **language-server inlay hints**, never a stored form. `dbg(expr)` prints the expression text and value and is an error in release builds.

> Round 2 §15, §18: there are no terse/explicit formatter modes; `--explicit` and `--terse` are removed and their views are inlay hints.

> Revision 0002 item 17: `bayan expand` is deferred until after v0.1, so the text that relies on it says it will show derived members and effects; language-server inlay hints cover them meanwhile. Codes: none.

### 21.4 Projects, outputs and VCS

**Projects.** `bayan new <name>` creates an app: `package.byn`, `README.md`, `.gitignore` (`/out/`, `/dist/`, `/gen/`; `bayan.lock` stays committed), `.gitattributes` (LF; `*.byn linguist-language=C#`), `.editorconfig`, `src/app/main.byn`, `tests/app.test.byn`, `testdata/` and `docs/`. `--lib` puts the code in `src/<name>/<name>.byn` and tests in `tests/<name>.test.byn`; `--script` writes a single `<name>.byn` starting with `#!/usr/bin/env bayan` and `#:io threaded`, with no manifest and no git. `bayan init` does the same in an existing directory, adding only missing files and listing the ones it skipped; it refuses a directory that has a `package.byn`. `--ci github` adds `.github/workflows/bayan.yml`. A fresh project builds and passes its tests with no edits.

**Names.** A project directory is named in kebab-case (`my-app`) or camelCase (`myApp`). The package name is `local/<kebab-case>` until published; a library's module is the camelCase form, so it can be imported.

**Layout.** In a project, `src/` holds the modules (`src/orders/` is `import orders`), the entry module is `src/app/` for an app and `src/<name>/` for a library, and in test builds every `tests/*.test.byn` joins the entry module. Commands given no path use the current directory when it has a `package.byn`. A library has no program, so `bayan build` and `bayan run` on one are usage errors. A file starting with `#!` is a script and compiles alone, and `bayan <file>.byn` runs it.

**Outputs.** In a project, `bayan build`, `run` and `test` write to `out/<mode>/<target>/` (the binary, the test binary `<name>-test`, and the emitted C in `c/` for inspection), and `bayan test` writes `out/test/results.json`, the same JSON that `bayan test --json` prints. `out/` is disposable. `--target` takes `linux-x64`, `linux-arm64`, `macos-x64`, `macos-arm64`, `windows-x64`, `windows-arm64`, `wasm32` or `host`; any target other than the host needs zig. `bayan dist` builds the targets in `dist.targets` (or `--target`, `all` for every one) into `dist/<version>/<target>/`, with `SHA256SUMS` (`sha256sum` format) and `BUILDINFO.json` (package, version, compiler, mode, targets, source commit and dirtiness, a hash of the inputs; no timestamps). A version directory is immutable: `bayan dist` refuses to rebuild one without `--force` and says to bump the version. It refuses a dirty git tree without `--allow-dirty`. `dist/latest/` is a copy (not a symlink) of the highest semantic version present, plus a `VERSION` file. Libraries get distributions once FFI export exists; scripts have none. `bayan clean` removes `out/`, and `dist/` or the cache only when asked.

**Cache.** Compiled programs live in one cache per machine, shared by every project, worktree and process: `$BAYAN_CACHE`, else `%LOCALAPPDATA%\bayan\cache` (Windows), `~/Library/Caches/bayan` (macOS), `$XDG_CACHE_HOME/bayan` or `~/.cache/bayan` (elsewhere). Keys hash the compiler version, mode, target, C compiler, flags and the emitted C, and the emitted C names files relative to the project root, so a second worktree or a moved project compiles nothing. An entry is built in a private directory and renamed into place with no lock; one whose executable no longer matches its recorded SHA-256 is rebuilt. `bayan run script.byn` remembers the program for the script's exact bytes, mode and compiler version and starts it directly next time. `out/` stays per worktree. `bayan cache stats` reports the location, entry count and size; `bayan clean --cache` empties it.

**Version control.** Bayan reads git and never writes history: `bayan new` and `bayan init` run `git init` only when git is installed, `--vcs none` isn't given, and the directory isn't already inside a work tree; they never commit, branch or configure.

### 21.3 Performance targets
Incremental rebuild under 100 ms on a reference project; cold-build budget tracked in CI.

**Runtime.** Bayan targets Rust and Zig speed, and the safe default stays on the fast path. Checks (bounds, overflow, `expects`) stay in every build mode and are emitted in a form the C compiler optimizes: a compare and a branch to a shared `noreturn`, cold trap function per check kind, and overflow through `__builtin_add_overflow` and its family (v0.0.1). Generics are monomorphized (§11). Typestate, effects, capabilities, units, `rest` spans, `Event.raise` and derivations cost nothing at run time. After v0.0.1: range analysis that proves an index in bounds and hoists the check out of a loop, zipped iteration (`for (p, w) in zip(pixels, weights)`) as the idiom that needs no index checks, and Sūq benchmark budgets against Rust and Zig as well as the C# baseline.

> Round 3 §30: keeping checks in release builds is affordable only if they are cheap to emit and easy for the C compiler to remove; v0.0.1 implements only the emission form.

### 21.5 Input forms
Input forms are alternate spellings that the editor and `bayan format` accept and rewrite, one way, into canonical syntax. The compiler rejects them in source with a machine-applicable fix. This is the only list.

| Input form | Canonical | Code |
|---|---|---|
| `User!EmailError f()`, `User!(A, B) f()` | `User f() fails EmailError`, `fails A, B` | BYN1031 |
| `for (let x in xs)`, `foreach (var x in xs)` | `for x in xs` | BYN1019 |
| `if (x) {`, `while (x) {`, `switch (x) {` | no parentheses (whole-condition parentheses kept when a brace is present) | BYN1017 |
| `if x return;`, `while x step();` | `if x { return; }` (a body is always a block) | BYN1018 |
| `x == null`, `x != null` | `x is null`, `x is not null` | BYN2027 |
| `{ cents = 250 }` | `{ cents: 250 }` | BYN1021 |
| `"Hello " + name` (strings) | `$"Hello {name}"` | BYN2209 |
| `errdefer stmt` | `defer failing stmt` | BYN1020 |
| `catch (e) switch { … }` | `catch switch { E e => … }` (fix only before `switch`; otherwise help text) | BYN1025 |
| `error set S = A \| B` | `union error S = A, B` | BYN1029 |
| `unless a && b { … }` | `if !(a && b) { … }` | BYN1022 |
| `unless x is null { … }` | `if x is not null { … }` | BYN1023 |
| `unless unlikely x { … }` | `if unlikely !x { … }`, or drop the word | BYN1033 |
| `delegate bool OrderFilter(Order o)` | `type OrderFilter = Order -> bool` (help text) | BYN1028 |
| `name = "…"` in `package.byn` | `name: "…"` | BYN3104 |
| `add`, `asString` | `append`, `finish` | BYN2230 |
| `toString()`, `ToString()` | `format()`, or `string(x)` / `$"{x}"` for a conversion | BYN2031 |
| Trailing `;`, `a; b` on one line | Newlines | BYN1040 |
| `else` at the start of a line | `} else` on one line | BYN1041 |
| `Grams w = 0g` (local binding in a function body) | `let w: Grams = 0g` | BYN1043 |
| `f() -> i32` (return), `f(x: i32)` (param), `name: string` (field), `timeout(after: Millis)` (payload) | `i32 f()`, `f(i32 x)`, `string name`, `timeout(Millis after)` | BYN1043 |
| `unique struct`, `distinct type`, `error union`, `error enum`, `attribute struct`, `open enum` | `struct unique`, `type distinct`, `union error`, `struct attribute`, `enum open` | BYN1044 |
| `[Derive(Json)]` | `@derive(Json)` | BYN1045 |
| `/* … */` | `//` lines | BYN1047 |
| `char` | `rune` | BYN1048 |
| `requires value…` | `requires it…` | BYN1050 |
| `case _ except .x` | `case not .x` | BYN1051 |
| `union S { a, b }` on one line | one case per line | BYN1053 |
| `A => B` as a type | `A -> B` | BYN1055 |
| `noalloc`, `nosuspend`, `suspends` | Removed: no `uses` clause already means pure; `uses suspend` marks suspension | BYN6001 |
| `return .x` in a function that fails, when the return type has no case `.x` | `fail .x` | BYN1056 |
| `(u8)x`, `(T)x` | `u8(x)`, `T(x)` | BYN1057 |
| Booleans in the non-project spelling | The project's `style.booleans` spelling (§19.2) | none (`bayan format` only; style is never compiler-enforced) |
| Primitives in the non-project spelling (`int` under `.sized`, `i32` under `.csharp`) | The project's `style.primitives` spelling (§3.1, §19.2) | none (`bayan format` only) |
| `requires …` on a function | `expects …` | BYN1063 |
| `context (allocator: a) { … }` | `provide allocator: a { … }` | BYN6006 |
| `trust(x, reason: "…")` | `x.trustUnchecked() because "…"` | BYN1062 |
| `Order order is .paid`, `type X = Order is .paid` | `Order.paid order`, `type X = Order.paid` | BYN7006 |
| `state x` member lines | One `states { … }` block | BYN1059 |
| `this` receiver written without a state | Receiver omitted | BYN1061 |
| `params T[] items`, `params ReadOnlySpan<T> items`, `T... items` | `rest ReadOnlySpan<T> items` | BYN1058 |
| Interface property `Type name { get }` | `Type name` | BYN1060 |
| `uses` before `fails`, or any clause out of order | `where`, `expects`, `becomes`, `fails`, `uses` | BYN1065 |
| `{n:hex}`, `{n:binary}`, `{n:octal}`, `{x:.2}`, `{d:iso}`, `{x:debug}` | `{n, as: .hex}`, `{n, as: .binary}`, `{n, as: .octal}`, `{x, digits: 2}`, `{d, as: .iso}`, `{x, as: .debug}` | BYN1068 |
| `for T x in e` | `for x: T in e` | BYN1043 |

The `add` through `toString()` rows are beyond the HANDOFF round 2 table; the plan added them because each retired form gets the same treatment. The rows from trailing `;` on are round 3 §20, with the codes this spec allocates. Round 3 removes `if !x { … }` → `unless x { … }`: both are paraphrases, and BYN1032 retires. The `return .x` and `(T)x` rows are Sūq review A2 and A4. BYN1049, once drafted for `unless not`, duplicated BYN1007 `UnlessNegated`; the earlier code survives and BYN1049 is retired, never reused.

- `bayan format` is idempotent, and rewriting an input form then formatting again produces identical bytes (acceptance tests arrive with `bayan format`, not in v0.1).
- Prose mode (§22) is unchanged.

> Round 2 §15: input forms replace the terse/explicit formatter modes, so stored source has one spelling and C# habits still work at the keyboard. Codes: BYN1017, BYN1018, BYN1019, BYN1020, BYN1021, BYN1022, BYN1023, BYN1025, BYN1028, BYN1029, BYN1031, BYN1033, BYN2027, BYN2031, BYN2209, BYN2230, BYN3104.

> Round 3 §20: every syntax change leaves its old spelling as an input form, so C# habits still type through. Codes: BYN1040, BYN1041, BYN1043, BYN1044, BYN1045, BYN1047, BYN1048, BYN1050, BYN1051, BYN1053, BYN1055, BYN1056, BYN1057, BYN1058, BYN1059, BYN1060, BYN1061, BYN1062, BYN1063, BYN1065, BYN6001, BYN6006, BYN7006.

> Revision 0003 §6: the compact format specs are input forms, so `{n:hex}` still types through and is stored as named arguments. Codes: BYN1068.

---

## 22. Prose translation

- `bayan format --prose` renders code as controlled-natural-language Markdown; `--from-prose` reads it back. Templates are data files per language (`templates/en.*`; Arabic later).
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
DIR → interpreter (comptime, scripts, REPL, tests)
DIR → C with mutant schemata (mutation: one build per run)
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
| UTF-16 `string`, 16-bit `char`, `s[i]` | UTF-8 `string`, 32-bit `rune`, no indexer (fix-its) |
| Classes, inheritance, `object`, `dynamic`, reflection | Removed (structs, interfaces, comptime) |
| Exceptions | `fails` / `Result` / panics |
| `new T()` | `T()` (`new` accepted, removed by formatter) |
| `in` parameters | Redundant |
| `using` (import / dispose / alias) | `import`, `import … as` |
| `goto`, `goto case` | Removed |
| `IConvertible`, `Convert.ChangeType` | `IFrom` / `ITryFrom`, `convert<T>` |
| `Substring(i, n)` | `s[i..(i + n)]` (migrated) |
| Culture-sensitive default comparison | Ordinal |
| Silent unchecked casts, `(T)x` | Checked conversions `u8(x)`; `u8.tryFrom(x)` returns `u8?` |
| `int`, `long`, `byte`, `uint` | Accepted per project: `style.primitives: .csharp` keeps them, `.sized` rewrites them to `i32`, `i64`, `u8`, `u32` (§3.1) |
| `list.AsSpan()`, `.ToList()` | `xs.span`, `xs.mutableSpan()`, `xs.copy()`, `List(seq)` (§16) |
| `params T[]` | `rest ReadOnlySpan<T>` (§10) |
| `event EventHandler<T>` | `Event<T>` field (§16.1) |
| Static constructors | `lazy` |
| `event`, multicast delegates | Removed |
| `Func<…>`, `Action<…>`, `delegate` | Function types `A -> B`, `type Name = A -> B` (§3.16) |
| `[Flags] enum` | `Set<T>` of a payload-free union (§3.13) |
| `Count` / `Count()` / `Length` | `length`; `count(p)` for a predicate count |
| `ToString()` | `format()` / `formatInto(ref StrBuf)` (§6.6); a conversion is `string(x)` |
| `foreach`, `if (…)`, `x == null`, `;`, `T x` (local), `[Attr]`, `/* */`, `char` | Input forms, rewritten (§21.5) |
| `m` / `d` literal suffixes | `dec`; doubles unsuffixed |

> Round 2 §1, §8, §11, §13, §15: compatibility rows follow the new forms.

> Round 3 §3, §5, §6, §10, §20, §24, §26, §28, O5: rows follow round-3 syntax. Codes: BYN1040, BYN1043, BYN1045, BYN1047, BYN1048, BYN1055, BYN1058.

---

## Appendix A — Settled; do not re-propose

- No `!` shorthand for `fails` in source; `User!E` is an editor input form only (§21.5).
- No symbol for effects; `noalloc` / `nosuspend` are gone (§14).
- No Ruby-style `?` suffix on boolean members; `?` means only `T?`, `?.`, `??`, ternary.
- No angle-bracket unit syntax (`5<s>`, `int<Bytes>`).
- No inheritance before a GC tier exists.
- No language-level JSON; JSON is a first-class standard-library feature.
- `Op` suffix; `async`/`await`; `fails`; `.byn`; Bayan.
- One copy-with-changes form: `x with { f: v }`. No derived `withF(v)`.

- **Round 3 (2026-10-22):**
  - One spelling per construct: synonyms are banned, paraphrase and ellipsis are allowed (§0).
  - Head-first order: `struct unique`, `type distinct`, `union error`, `struct attribute`, `enum open`; names before types in local bindings, types before names in fields, parameters, returns and payloads (revised 2026-10-22: names first was meant only for local bindings); `->` only in function types (§0, §3.10, §3.16, §7.1).
  - No `;`; waqf newline rules; an unused non-void value is an error (§2.5).
  - `@` attributes; `because` purpose clauses on every bypass (§4.4).
  - `union error` replaces `error set` (§3.14).
  - Postfix `if` / `unless`; both boolean spellings, chosen per project by `style.booleans` (§7.2, §7.3).
  - `//` comments only (§2.2). `rune`, not `char` (§3.1).
  - `except` takes patterns only (§6.8). Named patterns (§6.5).
  - Units convert implicitly only to finer units; bare literals are rejected (§3.4). Opt-in typed indices (§3.5).
  - Validation is `requires` on types, checked at construction (§7.9).
  - Effects: `uses` with a closed list; `provide` installs handlers (§14).
  - Typestate: the `states` block, `becomes`, `become` (§3.17).
  - Argument particles `from to until through at by for as`; a particle appears at most once per signature (§10).
  - `Panic.handler` with `uses console` (§7.6). A field of a struct with `requires`, states or `@sealed` is writable only from its own `ref this` methods or through `with` (§3.10, revision 0007). Views and copies are `span`, `mutableSpan()`, `copy()` and `T(x)`, never `toX` or `asX` members (§16).
  - Bytes decode through codecs, `try Utf8.decode(bytes)` and `Utf8.tryDecode(bytes)`, never `fromUtf8`, and `string(x)` always formats (§16.4); integers format with `string(n, as: Radix)`; hex and base64 are codecs (§3.2, §6.2). BYN50xx rules cover all code, BYN51xx the public API; domain words go in `grammar` (§5.2, §19.2). Generics are monomorphized (§11). A struct holding a mutable reference is `unique` and no reference crosses into a fiber that may outlive it (§9.4).
  - Secrets need `expose … because` in derivations; `Row` is flat (§5.3). Two trust layers (§15.1).
  - `try` covers the whole expression to its right (§8). `defer` modifiers are captured at registration (§7.6).
  - Size is `length`; `count(p)` is only a predicate count (§16).
- **Round 3 open questions, resolved 2026-10-22:** O1 unions are newline-only. O2 counts (`length`, `runeCount`) are plain `i64`; units apply only to slice offsets. O3 a `requires` that refers only to `it` belongs to the field above; one that refers to other fields is struct-level. O4 `at`, not `on`. O5 `->` is the function-type arrow (declarations return type-first); `=>` only for "evaluates to". O6 the particle `for` is allowed (label position only). O7 mixed-currency `+` traps; `tryAdd` and `add(…) fails` exist. O8 `state` is contextual at the start of a member declaration. O9 bare literals for distinct types are rejected. O10 decoding gives a trusted `T` only when every field is a `requires` type or a non-text scalar.
- **Rejected in round 3:**
  - `@` as lambda shorthand (keep `it`).
  - `mut` (keep `ref this`).
  - Automatic `T.Count` / `T.Index`; `Count of T`, `per` for units.
  - Bare `0` for unit types.
  - `T.Validated` / `validated(T)`.
  - `stmt unless not cond`.
  - `with` as the handler keyword (it is struct update).
  - A bare string as a purpose clause (keep `because`).
  - `panic` as an effect.

> Round 2 §15, §19: `!` survives only as an input form (C22); the `with` decision of 2026-10-21.

> Revision 0003 §1 (amends revision 0002 item 24): bytes decode through codecs (`try Utf8.decode(bytes)`, `Utf8.tryDecode(bytes)`), and `string(x)` always formats. Codes: BYN2230.

> Round 3, Rejected and "Resolved 2026-10-22": recorded here so they are not re-proposed.

## Appendix B — Deferred

Isnād registry design · Arabic prose templates · regex API, date/time · SIMD types, inline assembly · variadic generics · `bayan verify` (contract proofs) · JS backend, `bayan gen ts` · hot reload · GC tier and inheritance · guaranteed tail calls (`become` now means typestate, §3.17; the spelling is open) · interpolating prefixed literals (`sql"…{x}…"`, `html"…"`) · fiber isolation on panic · `if let` · string `+` and fluent asserts · effect variables in function types and interfaces · `format(into: ref buf)` as a particle form of `xInto` · collection-value-tied indices (`orders.Index`) · growing the particle set (`until`, `between`, `per`, `within`), only by spec revision when Sūq shows a need.

> Round 2 §13, Deferred: format specifiers are now specified (§6.6); the three HANDOFF deferrals are listed, not designed.

> Round 3, Deferred: the effect system is now specified (§14), so it leaves this list; the round-3 deferrals are added.
