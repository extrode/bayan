# Bayān shorthand notation — proposal for the round-4 batch (final draft)

Status: final draft for review. It lands in the round-4 syntax batch (one migration after 012), not as an addendum.
Diagnostic codes are TBD until the round-4 renumbering.
SPEC references are to `docs/bayan-spec.md`.

## Changes since draft 3

- **D1 closed:** `T[N]` is already the fixed-array spelling (SPEC §3.5, §6.2, §11, §16). S2 is no longer a proposal; it is existing syntax that the view shorthand extends.
- **D2 corrected:** SPEC §6.2 already borrows `List<T>`, `T[N]` and `string` as views at call arguments. Draft 3's "explicit `.span` only" is withdrawn.
- **D3 specified:** a new derived `==` on views, in the §6.7 model. `==` is never user-defined in Bayān.
- **S9 removed:** `?? break` and `?? continue` already exist (SPEC §6.1).
- **Examples aligned with the SPEC:** `xs.copy(to: alloc)`, `stackalloc u8[n] uninit`, `Map<K, V>`, and `static Ordering compare(T a, to T b)`.
- **BYN2310's message** now names the shorthand.

---

## 1. Round-4 §7 rows

| Feature | Pros | Cons |
|---|---|---|
| View shorthand `T[]`, `var T[]`, `ref var T[]` | Shortest hot-path signatures; `void main(string[] args)`; matches the existing `T[N]` | Postfix `[]` in types next to `[]` literals and indexing (type position only) |
| Derived `==` on views | Same model as structs and `string`; structs holding views get `==` | New rule in §6.7 |
| Implicit type and error-set parameters | `T max(T[] xs) where T : IOrder`; `fails E` without declaring `E` | Type names need two or more characters |
| `some I` parameters | One-use bounds without naming a parameter | Can't be instantiated explicitly |
| `Self` interfaces without a self argument | `where T : IOrder`, not `IOrder<T>` | Stdlib interface signatures change |
| Shared parameter types | `Vec3 cross(Vec3 a, b)` | One more parse rule |
| Target-typed statics `.zero` | Drops repeated type names in fields, arguments and returns | Already in the spec (§6.2 target typing); S6 is a pointer |
| Local type aliases + hidden-allocation lint | Short local names without hiding `List` | One more lint |
| Case-indexed arrays `T[U]` | Per-case tables with no hashing; extends `T[N]` | Binder decides constant versus union in `[…]` |

---

## 2. `rest` versus `main(string[] args)`

`rest` is a calling convention; `T[]` is a type. They compose.

```cs
i64 sum(rest i64[] xs) { … }      // callers: sum(1, 2, 3) or sum(..values)
void main(string[] args) { … }    // the runtime passes one vector
void main() { … }                 // canonical when arguments are unused
```

**Rules**

- Inside the body, `xs` and `args` have the same type, `T[]`.
- `rest` applies only to a `T[]` parameter.
- `main` may not use `rest` (BYN TBD, with a fix that removes it).

---

## 3. Grammar check

| Rule | How the shorthands follow it |
|---|---|
| Type-first declarations, name-first locals | `u8[] data`; `let xs: u8[] = buf.view`. |
| `?` and `[N]` bind to the type they follow (SPEC §3.16) | `[]` binds the same way: `u8[]?`, `u8[4][]`. |
| Head-first keyword order | Applies to declaration keywords (`struct unique`, `type distinct`, `union error`), not to type modifiers. |
| Type modifiers precede the type | Like `readonly u8*` and `rest`; the new ones follow suit: `var u8[]`, `some IShape`. |
| Parameter slot order | particle → mode → modifier → type → name: `to ref var u8[] dst`. |
| Closed particle set | `from to until through at by for as`; no shorthand adds one. |
| One form, one meaning | Long forms become input forms with fixes. No style key chooses between forms. |
| Conversions (§6.2, §6.7) | No new conversions. The existing view borrows at call arguments carry over unchanged. |
| Allocation stays visible | `List<T>` and `Map<K, V>` keep their names, even through aliases (lint). |
| Operators (§6.1, §6.7) | No new operator symbols. `==` stays derived, never user-defined. |
| Lambdas (J69) | One typed parameter takes no parentheses: `Token t => …`; two or more take them: `(Token a, b) => …`. |
| Clause order (round 4) | where → expects → uses → becomes → fails. |
| `T.X` shape versus `X<T>` status | `T[]` is a type form; S7 resolves camelCase statics only. |

---

## 4. The shorthands

### S1. Views: `T[]`, `var T[]`, `ref var T[]`

| Shorthand | Real type (§3.5) | Notes |
|---|---|---|
| `T[]` | `ReadOnlySpan<T>` | Borrowed, elements not assignable, copyable, `{ptr, len}`. |
| `var T[]` | `Span<T>` | Elements assignable. A struct with a field of this type is implicitly `unique` (J87). |
| `ref var T[]` | `ref Span<T>` | The callee may also rebind or advance the caller's view. |
| `T[N]` | (unchanged) | The existing fixed-size inline array. |

```cs
// before
void main(ReadOnlySpan<string> args) { … }
u32 checksum(ReadOnlySpan<u8> data) { … }
void zero(Span<u8> dst) { … }
i64 put(ref Span<u8> dst, u8 b) { … }
i64 total(ReadOnlySpan<i64> xs) { … }
let record: TableRecord = try TableRecord.decode(bytes)   // bytes: Untrusted<ReadOnlySpan<u8>>

// after
void main(string[] args) { … }
u32 checksum(u8[] data) { … }
void zero(var u8[] dst) { … }
i64 put(ref var u8[] dst, u8 b) { … }      // advances dst
i64 total(i64[] xs) { … }
let record: TableRecord = try TableRecord.decode(bytes)   // bytes: Untrusted<u8[]>
```

**Where to use each form**

- `T[]` for every parameter that only reads a sequence.
- `var T[]` when the callee writes elements.
- `ref var T[]` only for cursors and writers that consume the caller's buffer.

#### Call sites

Call sites are unchanged; SPEC §6.2 already borrows these as arguments:

```cs
let lines: List<Line> = …
var buf: u8[512] = …
let n = count(lines)               // List<T> → T[]
zero(buf)                          // T[N] → var T[]
let h = checksum(name)             // string → u8[]
let v: u8[] = buf.span             // owned T[N] → view needs .span outside a call argument; T[] → var T[] never
```

#### Nesting

Postfix forms bind to the type they follow (SPEC §3.16) and apply left to right. `var` binds to the outermost view.

```cs
u8[4][] quads          // a view of u8[4] arrays
var u8[4][] quads      // a writable view of them
u8[][] rows            // a view of views; region rules apply to the inner views
u8[]? maybe            // optional view
```

#### Literals and constants

These are target-typed literals, not conversions:

```cs
let c = checksum([0x7F, 0x45, 0x4C, 0x46])     // caller-frame array, the same lowering as rest (J62)
const names: string[2] = ["a", "b"]            // existing §3.5 rodata rule, unchanged
```

#### Equality (new §6.7 rule)

```cs
bool isElf(u8[] head) => head == [0x7F, 0x45, 0x4C, 0x46]

struct Token {
    u8[] text
    i32 start
}
// derived == compares text by content, then start, as §6.7 does for fields
```

- **Definition:** `==` and `!=` are derived for `T[]` and `var T[]` exactly when `T` supports `==`. Lengths are compared first, then elements in order, each with `T`'s derived `==`.
- **Structs holding views:** a struct whose fields include a view now gets derived `==` under the existing §6.7 rule ("every struct whose fields all support `==`"). `@noEquality` opts out as usual.
- **No user definition:** `==` is never user-defined (§6.7 derives it, and §6.1 allows no new symbols), so neither a view's `==` nor an element's can be replaced by user code.
- **What doesn't get `==`:** `Secret<T>` still has none, so neither does a view of it. Floats follow their own `==`, so a NaN element makes the views unequal.
- **Lowering:** a `memcmp` fast path applies when the element's derived `==` is bitwise: integers, `bool`, `rune`, and derived structs of those with no padding and no floats. This matches the §3.5 key-hashing rule of "never padding bytes".
- **No general `equals`:** `a.equals(b, as: .caseless)` stays a string member (§3.2, §16.4), because it takes an option.

#### Optional views

`T[]?` uses the null niche (round-4 P4), so it's 16 bytes. `null` and the empty view are different values.

#### Other rules

- **Type position only.** In expressions, `[]` stays the empty literal and `xs[i]` stays indexing.
- **`extern c` signatures** reject `T[]`. Write `readonly T*` plus a length (BYN TBD).
- **View fields** follow the region-bound reference rules (round-4 §3 risk row; the 010 corpus).
- **Unchanged producers:**
  - `xs.span` gives `T[]`; `xs.mutableSpan()` gives `var T[]`.
  - `xs.copy(to: alloc)` gives an owned copy.
  - `string.bytes` gives `u8[]`; `StrBuf.view` gives a string view.
  - `stackalloc u8[n] uninit` gives `Uninit<u8>`; writing APIs return the initialized prefix as `var u8[]`.
- **Input forms:** `ReadOnlySpan<T>` → `T[]` and `Span<T>` → `var T[]`, with fixes. The language server may show the long forms (display option or inlay hint).
- **BYN2310's new message:** "`{0}` is never written through; write `{1}[]`".
- **`bayan migrate cs`** (post-v1, tiers doc) maps `ReadOnlySpan<T>`, `Span<T>` and `params T[]` to `T[]`, `var T[]` and `rest T[]`.
- **Suspend points:** views may cross them (fibers keep their stacks; storing a view is still BYN3214). *Pending Q745.*

### S2. Implicit type and error-set parameters

```cs
// before (spec §11 :1437; round-4 §7)
T max<T>(ReadOnlySpan<T> xs) where T : IOrder<T>
void retain<T>(ref List<T> xs, where (T -> bool fails E) keep) fails E

// after
T max(T[] xs) where T : IOrder
void retain(ref List<T> xs, by (T -> bool fails E) keep) fails E
List<V> collect(rest V[] xs) uses alloc
TKey keyOf<TKey>(Row row)          // a descriptive name is still declared
```

**Rules**

- **Scope:** an undeclared single capital letter, optionally followed by a digit, in a *function signature* is a type parameter. In a `fails` clause it's an error-set parameter.
- **Structs and interfaces** still declare theirs (`struct Pair<A, B>`, `Ring<T, const i32 N>`). A letter already in scope refers to the outer one.
- **Naming:** user type names need two or more characters (BYN TBD).
- **Explicit order:** parameters left to right, then the return type. `max<i64>(xs)` works.
- **`bayan format`:** removes an explicit list only when it matches the implicit order.
- **Const parameters** (`const i32 N`) are always declared.

### S3. Anonymous generic parameters: `some I`

```cs
// before
f32 area<S>(S shape) where S : IShape
void addAll<I>(ref this, I orders) where I : IIterator<Order>

// after
f32 area(some IShape shape)
void addAll(ref this, some IIterator<Order> orders)
```

- **Meaning:** each `some` is a fresh, monomorphized parameter with static dispatch.
- **Placement:** only as a whole parameter type. These are rejected (BYN TBD):
  - `List<some IShape>`
  - `rest some IShape[]`; write `rest T[] shapes where T : IShape` instead
  - `some` in a return type
- **API note:** `some` can't be instantiated explicitly, so switching between `some` and a letter changes the API.

### S4. `Self` interfaces drop the self argument

```cs
// before
interface IOrder<T> { … }
T max<T>(ReadOnlySpan<T> xs) where T : IOrder<T>

// after
interface IOrder {
    static Ordering compare(Self a, to Self b)
}
T max(T[] xs) where T : IOrder
```

The `compare` shape follows §6.7 (`static Ordering compare(T a, to T b)`). S5 doesn't apply here, because sharing never crosses the particle `to`.

### S5. Shared parameter types

```cs
// before
Vec3 cross(Vec3 a, Vec3 b)
f32 lerp(f32 a, f32 b, by f32 t)
let earlier = (Token a, Token b) => a.start < b.start

// after
Vec3 cross(Vec3 a, b)
f32 lerp(f32 a, b, by f32 t)
let earlier = (Token a, b) => a.start < b.start
```

**Rules**

- Adjacent parameters with no particle and no mode share the preceding type.
- Sharing never crosses a particle, a mode or `rest`.
- Fields stay one per line.
- Collapsing is not the formatter's job: `bayan format` changes layout only. An opt-in 19xx lint suggests `Vec3 a, b` for `Vec3 a, Vec3 b` on one line, and a wrapped list gives each line its own type (§5). The formatter could take this over only once the open questions below are settled and the grouping is equivalent in every position (user decision 2026-11-04).

**Open questions (S5)**

- Defaults: does `Vec3 a = .zero, b` share the type, and does `b` take the default?
- Per-parameter attributes: does `@json(name: "x") Vec3 a, b` apply the attribute to `b`, and can `b` carry its own?
- Inline refinements (J57): does `T a is .x, b` give `b` the refinement, the bare type, or is it an error?
- Modes: sharing never crosses `ref`, `out` or `move` (the rules above), but can a bare name follow a moded parameter (`ref Vec3 a, b`), and what is `b`'s mode?

### S6. Target-typed static members: `.member` (existing rule)

Not a proposal (user decision 2026-11-04, review row C20). Spec :554 and :865 already target-type `.member` to "a member of the expected type", which covers statics. Round 4 spells the rule out in the spec (Swift's rule): `.m` resolves to a case of the expected type, or to a static field or factory of it whose type is the expected type. No P7 gate. The current compiler already resolves a factory (`let b: Vec = .of(3)` checks); static fields are BYN2022 in v0.1, so `.zero` waits on static fields, not on this rule. Readability of nested leading dots is the 29xx lint. The examples and rules below stay as illustration.

```cs
// before
struct Body {
    Vec3 velocity = Vec3.zero
}
tint(Color.red)
return Money.zero

// after
struct Body {
    Vec3 velocity = .zero
}
tint(.red)
return .zero
```

**Rules**

- Resolves camelCase statics and factories of a single expected type. There's no search across overloads (round-4 §3, §5 Swift).
- PascalCase segments stay derived shapes (`T.Patch`).
- Constructors stay `T(x)`.
- `.a(.b())` is a lint: TBD (x9xx of BYN2xxx, A2).
- BYN7008 is extended to union cases versus statics.

### S7. Local type aliases and the hidden-allocation lint

```cs
void mix(to var f32[] dst, f32[] a, b) {
    type Frame = f32[256]
    …
}

type Orders = List<Order>     // lint, TBD (x9xx of BYN3xxx, A2): the alias hides an allocating type
```

### S8. Case-indexed arrays: `T[U]`

```cs
union Currency {
    myr
    usd
}

u64[Currency] totals
totals[.myr] += line.cents
```

- **Meaning:** one slot per case of `U`, which must be a union with no payload cases. No hashing; the size is known at compile time.
- **Inside `[…]`:** a constant means `T[N]`; a union type means `T[U]`. The binder resolves which.
- **Constant indexes:** a constant out-of-range index is a compile error, as for `T[N]`.

### Not proposed here

- **`@soa`** stays a v0.3 attribute (round-4 §7).
- **`?? break` and `?? continue`** already exist (§6.1).

---

## 5. Style

This section follows bayan-style-proposal.md:

- Indentation is consistency-only.
- Width is 120.
- `{` always ends the line; a wrapped parameter list closes with `) {` (style proposal §3.4, C21).
- Alignment is `.runs` by default, with `.none` allowed.
- `package.byn` is the source of truth.
- Style lints are `TBD (x9xx of their area, A2)`: `lambdas.it` and `guards.postfix` in 1xxx syntax (spelling only), `nesting.depth` and `conditions.named` in 4xxx control flow, `functions.length` in 0xxx project and tools.
- `@unformatted because "…"` is the only opt-out.

**Wrapped lists:** each item goes on its own line, indented once, with a trailing comma; the closer goes on its own line and shares it with `{` as `) {` (user decision 2026-11-04, review row C21).

```cs
Matrix4 lookAt(
    Vec3 eye,
    Vec3 target,
    Vec3 up,
) {
    …
}

public Grams shippingWeight(
    Order        order,
    by Carrier   carrier,
    at Timestamp when,
) {
    …
}
```

**Spelling:** `u8[]`, `u8[64]`, `var u8[]`, `u8[]?`, `Vec3 a, b`, `.zero`; no inner spaces.

**Lints (TBD, x9xx of their area, A2: nested leading dot in 2xxx types, hidden allocation and redundant `.span` in 3xxx memory, repeated adjacent types in 1xxx syntax, opt-in):**

| Lint | Trigger | Fix |
|---|---|---|
| Nested leading dot | `.a(.b())` | Spell the inner type. |
| Hidden allocation | an alias of an allocating type | Use the type directly. |
| Repeated adjacent types (opt-in, off by default) | `Vec3 a, Vec3 b` on one line | Collapse. |
| Redundant `.span` | `f(xs.span)` where §6.2 already borrows | Remove `.span`. |

---

## 6. Diagnostics (codes assigned in the renumbering)

| Name | Message | Fix |
|---|---|---|
| `SpanInput` | "`{0}` is an input form; write `{1}`" | yes |
| `RestInput` (BYN1058) | "`{0}` is an input form; write `rest {1}[] {2}`" (round-4 text change; goldens change then) | yes |
| `RestOnEntry` | "`main` cannot take `rest`; write `string[] args`" | yes |
| BYN2310 | "`{0}` is never written through; write `{1}[]`" (updated; round-4 text change, goldens change then) | yes |
| `ViewInExtern` | "`{0}` cannot cross `extern c`; pass `readonly {1}*` and a length" | no |
| `SingleLetterType` | "type names need two or more characters; single letters are type parameters" | no |
| `SomePosition` | "`some` is allowed only as a whole parameter type" | no |
| `StateShadowsStatic` (BYN7008) | extended to union cases | — |
| Style lints | §5 | yes |

---

## 7. SPEC and GRAMMAR edits

| Section | Edit |
|---|---|
| §3.5 | Add `T[]` / `var T[]` as the spellings of `ReadOnlySpan<T>` / `Span<T>`; add `T[U]` beside `T[N]`. |
| §3.16 | "`?`, `[N]` and `[]` bind to the type they follow." (the rule exists there for `?` and `[N]`; `[]` is new; spec §5 is naming) |
| §6.2 | Views row: restate it with the shorthands; the conversions are unchanged. |
| §6.7 | Derived `==` for views; structs with view fields derive `==`. |
| §10 | `rest T[] xs`; shared parameter types; implicit type and error-set parameters; `some I`. |
| §11 | `Self` interfaces take no self argument. |
| §21.5 | Input-form rows below. |
| GRAMMAR | Type modifiers precede the type. Pointers default writable because they mirror C and live only in `extern`; views follow `let`/`var`. Single letters are type and error-set parameters. |

| Input form | Canonical |
|---|---|
| `ReadOnlySpan<T>` | `T[]` |
| `Span<T>` | `var T[]` |
| `rest ReadOnlySpan<T> xs` | `rest T[] xs` |
| explicit `<T>` in the implicit order | removed |
| `IOrder<T>` on a `Self` interface | `IOrder` |
| `T.zero` where `T` is expected | `.zero` (lint) |
| `Vec3 a, Vec3 b` on one line | `Vec3 a, b` (opt-in lint, not the formatter) |

---

## 8. Tests

- **Parse goldens:** S1 forms (nesting, `T[]?`), S5, S8; `T[]` versus `[]` versus `xs[i]`.
- **Formatter:** each input form; wrapped-list rule; `.runs` alignment.
- **Diagnostics:** one test per row in §6.
- **Lowering:**
  - `{ptr, len}` arguments.
  - Literal arguments on the caller frame.
  - View `==`, including the `memcmp` fast path and the float and NaN path.
  - Derived `==` on structs with view fields.
  - The `T[]?` niche.
- **Migration:** a check that flags existing one-letter user type names (S2 rule); single capitals that are type parameters in a function signature, such as `T` and `E`, are exempt; the round-4 batch migration, plus the C#-embedded tests that spell `ReadOnlySpan` or `Span` (J9).
- **`bayan migrate cs`** (post-v1; not a round-4 test): the mappings in S1.

---

## 9. Considered and rejected

| Idea | Reason |
|---|---|
| `rest` on `main` | Nothing calls `main` with separate arguments. |
| Explicit `.span` at every call | §6.2 already borrows at arguments. |
| A `sharedTypes` style key | Per-project dialects (round-4 §2). |
| No `==` on views | Inconsistent with `string` and with the §6.7 derived model. |
| A general `equals` member | Duplicates `==`; `equals(…, as:)` stays for options only. |
| `each T` | Conflicts with Swift's parameter packs; `some` covers it. |
| Chained comparisons, relational patterns | Synonyms for ranges and `in`. |
| A `soa` type keyword | Round 4 keeps `@soa` as an attribute. |
| `[K: V]` for `Map<K, V>` | Hides allocation. |
| `with` topic blocks, `anytype`, `List Order`, `\|>`, Gleam `use`, `.new(…)` | Scope injection, unbounded, too far from C#, synonym, unneeded, two construction forms. |
| .NET signature display | No .NET interop (round-4 §3). |

---

## 10. Decisions

| # | Question | Decision | Basis |
|---|---|---|---|
| D1 | Fixed-array spelling | `T[N]`, unchanged | SPEC §3.5 |
| D2 | Owned → view at call sites | Implicit at arguments, as today | SPEC §6.2 |
| D3 | `==` on views | Derived elementwise; never user-defined | New §6.7 rule, in its existing model |
| D4 | Shared-type style | One formatter rule; no key | Round-4 §2 |
| D5 | Landing | Round-4 batch | Round-4 §1 |
| D6 | Views across suspend | Allowed | Pending Q745 |
| D7 | `T[]?` | Allowed, using the null niche | Round-4 P4 |

---

## 11. Issues found in the round-4 draft

1. **`where` as a particle (§7):** `retain(…, where (T -> bool fails E) keep)` and `retain(where: …)` use `where` as a particle. It's outside the closed set, and it's also a clause keyword and a waṣl starter (J61). Use `by`.
2. **Lambda parentheses (§2):** `(Line l) => …` should be `Line l => …` (J69).
3. **Mixed generics (§7):** `retain<T>` declares `T` but leaves `E` undeclared. S2 makes both implicit.
4. **Long span spellings:** P2's `ReadOnlySpan<i64>` and §7's `Untrusted<ReadOnlySpan<u8>>` become `i64[]` and `Untrusted<u8[]>` once S1 lands.

---

## 12. Handoff touch points

| Item | Where |
|---|---|
| S1, S8 | `ParseType` (`Parser.cs`): postfix `[]`, prefix `var`, left-to-right binding (next to the existing `[N]` and `?`). `ParseParams`: `ref var`. `Nodes.cs` maps them to the existing span types. Binder resolves constant versus union in `[…]`. |
| S1 equality | §6.7 derivation gains views; `memcmp` fast-path check; struct derivation picks up view fields. |
| S1 literals | `Lowerer.cs`/`CEmitter.cs` caller-frame path (J62). |
| S2, S3 | Binder: implicit type and error-set parameters; `some` desugaring; type-name length check. |
| S4 | Stdlib interface declarations. |
| S5 | `ParseParams` and typed lambda parameters: carry the type when a bare name follows `,`. |
| S6 | None: existing rule. Static fields (BYN2022 in v0.1) are a separate item. |
| S7 | `ParseTypeAlias` at statement level; lint. |
| §5 | Formatter: shared-type collapse, the wrapped-list rule. |
| BYN2310 | Message text. |
