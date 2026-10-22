# HANDOFF — Bayan, design round 3

## Context

Bayan is a C#-derived, natively compiled language (no .NET runtime). `SPEC.md` exists, the round-2 HANDOFF has been applied or is in progress, and the v0.1 bootstrap compiler is being built.

This handoff records every decision from design round 3. It **supplements** round 2: everything in round 2 still stands unless this document changes it, and this document wins on any conflict.

For each area: update `SPEC.md` (with a one-line rationale per change), update `GRAMMAR.md` and `STYLE.md` where named, implement in the compiler where the area is in v0.1 scope, add tests, and add diagnostics with `BYN####` codes and `bayan explain` entries. Use these code ranges: 1xxx syntax, 3xxx types, 5xxx grammar, 6xxx effects, 7xxx typestate, 8xxx security.

Code fences in docs use `csharp` highlighting until Bayan has its own grammar. Example files use the `.byn` extension.

---

## 1. Principles (SPEC §1, GRAMMAR.md)

- **Bayan follows its own principles.** C# familiarity is a benefit, never a deciding reason.
- **Rename "one form, one meaning" to "one spelling per construct; free composition."** Three categories:
  - **Synonyms**: two spellings of the same construct. Rejected: one is canonical, the other is an input form with a fix-it.
  - **Paraphrases**: different compositions of the general grammar that mean the same thing (`users except null` / `users.where(it is not null)`). Allowed.
  - **Ellipsis (ḥadhf)**: omitting a part recoverable from local context (`it`, inferred types, target-typed `.member`). Allowed.
- **Guards against drift:**
  1. No synonyms in the lexicon (except the boolean spellings, §5, which are a documented exception).
  2. New syntax must have its own job; it may not only re-spell an existing construct.
  3. Paraphrases must be truly equivalent: same narrowing, cost and effects. Where two forms are not equivalent, a lint flags the difference (e.g. `0..=n - 1` vs `0..n`, `p == Point { … }` vs `p is Point { … }`).
- **Head-first word order (Arabic).** Statements: the verb comes first and ḥāl modifiers follow (`defer failing`). Declarations: the kind comes first and modifiers follow (`struct unique`, `type distinct`, `union error`). Local bindings: the name comes first and the type follows (§2); declarations keep the type first.

## 2. Declarations

### Keyword order

| Old | New |
|---|---|
| `unique struct File` | `struct unique File` |
| `distinct type Sku = string` | `type distinct Sku = string` |
| `error union CarrierError { … }` | `union error CarrierError { … }` |
| `error set ShipError = A, B` | `union error ShipError = A, B` |

### `union error` absorbs `set error`

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
- Two components sharing a case name is a compile error.
- `set error` is removed.

### Declarations type first, local bindings name first

```csharp
struct Item {
    Sku sku
    Grams weight
}

union error CarrierError {
    timeout(Millis after)
}

public Grams shippingWeight(Order order) {
    let w = 0g              // immutable, inferred
    let w: Grams = 0g       // immutable, explicit type
    var w: Grams = 0g       // mutable, explicit type
    Grams w = 0g            // input form → `let w: Grams = 0g`
    …
}

public void ship(ref this, Line line) { … }
```

- **Declarations put the type first** (round-2 order): return types (`public Grams shippingWeight(Order order)`, `void` when nothing is returned), parameters (`(Order order)`, `(ref this, Line line)`, a particle leads like `ref`: `by Carrier carrier`), fields (`Sku sku`), and union and state payloads (`timeout(Millis after)`). Visibility (`public`, `static`) leads the whole declaration. Computed properties take a type like a field: `public bool isEmpty => lines.isEmpty`. Test and benchmark parameters follow the same order: `test "bounds" (i32 input, bool valid) { … }`.
- **Local bindings put the name first** with explicit mutability: `let` (immutable) or `var` (mutable), with an optional `: Type`.
- **Input forms** (`bayan fmt` rewrites them, BYN1043): `f() -> T` → `T f()`; `f(x: T)` → `f(T x)`; field `x: T` → `T x`; payload `case(x: T)` → `case(T x)`; in a function body, `Grams w = 0g` → `let w: Grams = 0g`.

**Revised 2026-10-22 (user request):** this reverts audit flaw #8 ("names first") for declarations. Names first was meant only for local bindings, where `let`/`var` already lead and the name is what the reader scans for; declarations return to round 2's type-first order. Every other rule in this section and in round 3 stands as written.

### The receiver

`this` is the receiver parameter's name; its type is the enclosing struct and is elided.

| Receiver | Meaning |
|---|---|
| (not written) | Read-only, any state |
| `this is .shipped` | Read-only, only in that state (§14) |
| `ref this` | Mutating, any state (Form V, unchanged) |
| `ref this is .placed` | Mutating, only in that state |

### `unique`

`unique` stays as the move-only keyword, for resources (`File`, `Socket`, `Transaction`, `Lock`). It is unrelated to `const`. A `unique` type cannot be `const`. Plain data types (orders, lines) are never `unique`.

### Named patterns

```csharp
const inactive = pattern Line { status: .removed or .backordered }
for line in order.lines except inactive { … }
```

Patterns are `const` values of pattern type, named in camelCase. `pattern` is a contextual keyword, valid only after `=` in a `const` declaration.

## 3. Statement termination: no semicolons (waqf rules)

`;` is removed. `bayan fmt` strips trailing semicolons and splits same-line statements; the compiler rejects `;` with that fix-it.

| Waqf mark | Rule |
|---|---|
| مـ lāzim (mandatory stop) | A newline after a complete statement ends it |
| لا lā (forbidden stop) | The statement continues if the line ends with a binary operator, `,`, `=`, `=>` or `{`, or the break is inside `( )` or `[ ]` |
| وصل waṣl (joining) | The next line continues the statement if it starts with `.`, `?.`, `??`, `except` or `requires` |
| Never-join | A line starting with `(`, `[` or `-` always begins a new statement |

Supporting rules:

- **An unused non-void value is a compile error.** This turns every mis-split line into a diagnostic (`let x = a` newline `-b`).
- `return`, `break` and `continue` take their operand on the same line. A postfix modifier (`unless`, `if`) must be on the same line as its statement.
- `else` must follow `}` on the same line; `else` on a new line is rejected with a fix-it.
- Comment lines are transparent to continuation. Outside `( )` and `[ ]`, a blank line always ends a statement; inside them, blank lines are ignored like other newlines.
- Struct literals in a control-flow head: whole-condition parentheses, as already decided: `if (p is Point { x: 0, y: 0 }) { … }`.

**Separators:** declarations (fields, union cases, states) are separated by newlines. Entries in literals and argument lists are separated by commas (with trailing commas, per STYLE.md).

## 4. Attributes and purpose clauses

- Attributes use `@`: `@derive(Json, Row)`, `@allow(BYN5012)`, `@cases(...)`. `[Attr]` becomes an input form (a line starting with `[` is a collection literal under §3). Attribute names are camelCase (`@derive`, `@allow`); derivation names stay PascalCase (`Json`, `Row`).
- **Purpose clauses (mafʿūl li-ajlihi):** `because "…"` is required wherever code bypasses a safety rule:
  - `@allow(X) because "…"`
  - `@json(expose) because "…"` on a `Secret` field (§16)
  - `xUnchecked() because "…"`, including `trustUnchecked()`
  - `unsafe because "…" { … }`
- Missing `because` is a compile error. Ordinary attributes (`@derive`) take none.
- Add `bayan audit`: lists every bypass in a project with its reason.

## 5. Conditionals and booleans

- **Postfix guards:** `stmt if cond` and `stmt unless cond`. `stmt unless not cond` is rejected with a fix-it to `if`.
- **Block `unless x { … }` stays**, with its existing restrictions. `unless … else` stays rejected.
- **`if !x { … }` is no longer rewritten to `unless x`.** Both are allowed paraphrases. Remove that row from the round-2 input-forms table.
- **Boolean spellings are both allowed** (documented synonym exception): `!` `&&` `||` and `not` `and` `or` in boolean expressions. `bayan fmt` normalizes each project to one spelling:
  ```csharp
  style: Style {
      booleans: .symbols,   // or .words
  }
  ```
- **Patterns are words-only:** `is not null`, `.removed or .backordered`, `not .draft`.

## 6. Comments

Only `//`. `///` stays for doc comments. `/* */` is an input form, rewritten to `//` lines.

## 7. `except` (istithnāʾ)

- Subtracts from a **sequence, set or error set**: `for line in lines except inactive`, `users except null`, `union error Retryable = CarrierError except .rejected`.
- Takes **patterns only**, never boolean expressions: `orders except o.total > 100` is an error with a fix-it to `where`.
- Never inside a `case` pattern; `case _ except .draft` is an error with a fix-it to `case not .draft`.
- The loop variable narrows (`except null` gives a non-null type). Taint is preserved (§17).

## 8. Units

Unit syntax is unchanged (`unit Seconds(s): i64`, `30s`, `4MiB`). Additions:

- **Implicit conversion only toward finer units** (`2s + 500ms` → `Millis`). Converting to a coarser unit is explicit and names the rounding: `t.round(to: Seconds)`, `t.roundDown(to: Seconds)`, `t.roundUp(to: Seconds)`.
- **Bare literals are always rejected for unit types, including `0`:** `let w: Grams = 0` is an error with a fix-it to `0g`.
- **Display:** a unit formats as its value plus its symbol (`524288 B`). `size.convert(to: KiB)` converts for display (`512 KiB`). (Not `.in(KiB)`: `in` is a keyword and not a verb.)
- **Quantities are dimensionless:** `i32 qty`, and `i32 × Grams → Grams`.
- **`Money` is a struct with a currency field, not a unit.** Adding mixed currencies is a runtime error (exact behavior: Open question O7). The unit docs cite `Money` as something units don't model.

## 9. Typed indices (opt-in)

There are no automatic `T.Count` or `T.Index` types. Typed indices are opt-in per collection, keyed by the collection rather than the element type (rustc's `IndexVec`), so parallel arrays work:

```csharp
type distinct RowId = Index

let names: List<string, RowId> = …
let prices: List<Money, RowId> = …
for i in names.indices {       // i: RowId
    print(names[i], prices[i])
}
```

- Plain `List<T>` keeps plain `Index`, and `0..xs.length` works as today.
- Typed collections expose `.indices`. `xs[^1]` resolves against the collection's own index type.

## 10. Text

`rune` replaces `char` everywhere: the 32-bit scalar type, the `Runes` unit, `runeCount`. `char` becomes an input form. `s.length` stays the O(1) byte length.

## 11. Validation: `requires` everywhere

`requires` has one meaning everywhere: **checked at construction.** A value with `requires` cannot exist in an invalid state.

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

    requires password != email.value  on .password
    requires password != username     on .password
}
```

- `it` replaces `value` in `requires`.
- Each `requires` line is a separate rule and reports its own violation. Struct-level rules attach to a field with `on .field` (target-typed to `T.Field`).
- **Decoders collect every violation**: `SignupForm.fromJson(body) catch e => … e.violations`. Decoders and `complete()` fail with `ValidationError<T> { List<Violation<T>> violations }`.
- Struct literals of a type with `requires` are fallible: `try SignupForm { … }`.
- Fields of a struct with `requires` cannot be assigned one at a time. Updates go through `with`, which re-checks: `try f with { password: p }`.
- **Drafts** use the existing `T.Patch` (all fields optional, no `requires`), with `patch.complete()` returning `T fails ValidationError<T>`.
- **Rules as data:** the derivation exposes `T.rules: List<(T.Field, Rule)>` at compile time, for web frameworks (HTML5 attributes). A recognized expression shape maps to a `Rule` case; anything else is `custom` and is checked only on the server:
  ```csharp
  union Rule {
      length(Range<i64>)    // it.runeCount in a..=b
      range(Range<i64>)     // it in a..=b
      pattern(Regex)        // it.matches(re)
      required              // non-nullable field
      kind(TypeId)          // Email, Url, Date (`type` is a keyword)
      custom(RuleId)
  }
  ```
- Violations are structured (field, rule, parameters) with **no message strings**. Frameworks localize them.
- Remove `T.Validated` from the §5.2 noun-pattern table, the `Validate` derivation from the §5.3 list, and any field-level `[Check]` from the spec.
- Form-encoded decoding (`@derive(Form)`) belongs to the future web framework, not the standard library.

## 12. GRAMMAR rules added

- **`T.X` vs `X<T>`:** `T.X` is a different shape derived from `T` (`Builder`, `Patch`, `Field`, `Id`). `X<T>` is the same value with a different status (`Untrusted`, `Secret`).
- **States** (§14) are named as participles or adjectives (`paid`, `shipped`) and are exempt from the `-ed` copy-pattern rule, which applies only to members.
- **Logging:** `log` is the verb; the level is a particle argument (§13). `Log.debug` fails the grammar check (`debug` is a level, not the act).

## 13. Effects

Unify `noalloc`, `nosuspend` and package capabilities into one effect system.

```csharp
public ParcelId book(Parcel parcel, by Carrier carrier)
    fails CarrierError uses net, alloc, suspend { … }

public u32 crc(Span<u8> data) { … }   // no `uses`: no alloc, no suspend, no I/O
```

- **Closed effect list:** `alloc`, `suspend`, `net`, `fs`, `clock`, `random`, `env`. It grows only through spec revisions.
- **No `uses` clause means pure.** This replaces `noalloc nosuspend`, which are removed.
- Public functions declare their effects; private functions infer them (same rule as error sets).
- `panic` is **not** an effect.
- `fails` stays a separate clause, because it carries a type. GRAMMAR.md states this reason.
- **Effect polymorphism for closure parameters:** `map` inherits its closure's effects. There is no `tryMap` or `mapAsync`.
- **Stored function types and interface methods declare an upper bound** (option A). Implementations may use fewer effects:
  ```csharp
  interface IPricer {
      Money price(Order o) uses net, alloc
  }
  struct Checkout {
      (Order => void uses net) onPaid
  }
  ```
  Effect variables (option B) are deferred.
- **Derived forms compute their effects:** `-ed` adds `alloc`, the allocating form derived from `xInto` adds `alloc`, and `tryX` keeps the original's effects. `bayan expand` shows them.
- **Package capabilities are an upper bound:** a package without `.net` cannot contain any function that `uses net`, transitively.
- **Logging is ambient:** allowed in pure functions, never allocates (the interpolated message is formatted through a handler into a fixed buffer, only when the level is enabled), escapes control characters, and is interceptable:
  ```csharp
  log($"weighing {order.id}")                // default level: info
  log($"retry {n} of {max}", as: .debug)
  ```
- **Handlers use `provide`.** `context` remains only the value (`context.Io`):
  ```csharp
  let net = FakeNet(reply: label)
  provide net: net, clock: FakeClock(at: Date(2026, 12, 1)) {
      try shipAll(order, by: carrier)
  }
  assert net.calls == 1

  let logs = LogRecorder()
  provide log: logs { … }
  ```
  Inner `provide` blocks override individual handlers and inherit the rest. Handlers are one-shot, so they compile to context passing at zero cost.

## 14. Typestate (kāna / ṣāra)

States are declared inside the struct:

```csharp
@derive(Json)                  // not Row: `lines` is not flat (§16)
struct Order {
    OrderId id
    List<Line> lines

    state draft
    state placed
    state paid(PaymentId payment)
    state shipped(PaymentId payment, TrackingId tracking)
    state cancelled(string reason)
}

public void pay(ref this is .placed, Payment p) becomes .paid fails PaymentError uses net {
    let id = try p.charge(total)
    become .paid(payment: id)
}

public Url trackingUrl(this is .shipped) { … }   // `tracking` itself is a payload field
public Label label(Order is .shipped order) { … }
```

- `T is .state` is a type: the values of `T` that match the pattern. It is usable in any parameter, return type or local.
- `becomes .state` declares a transition; `become .state(…)` performs it. **A failed transition leaves the state unchanged.**
- State is tracked through control flow on a `var` with no live borrows (the borrow rules already guarantee this): after `try order.pay(card)`, `order` is `Order is .paid`.
- State payload fields are accessible only in that state.
- A value of unknown state narrows with `is` or `switch`. A state switch is exhaustive, so adding a state is a compile error at every non-exhaustive switch.
- Zero cost: at runtime the state is a tag, like a union's; the checks are compile-time only.

## 15. Argument particles

A closed set of role particles (Arabic مِن/إلى/بِ):

| Particle | Role |
|---|---|
| (none) | The object being acted on |
| `from` | Source |
| `to` | Destination or recipient |
| `at` | Position or time |
| `by` | Means or replacement |
| `for` | Beneficiary or purpose |
| `as` | Role |

```csharp
public void copy(from Path src, to Path dst) fails FsError uses fs
public void transfer(Money amount, from Account payer, to Account payee) fails LedgerError
public void insert(ref this, T item, at Index i)

copy(from: a, to: b)
try ledger.transfer(amount, from: alice, to: bob)
lines.insert(x, at: 3)
```

- A function has at most one unlabeled parameter (the object). Every other required parameter takes a particle, and call sites must write it. Optional settings remain ordinary named parameters with defaults.
- The particles are part of the function's name (`copy(from:to:)`).
- The set is closed for user code and grows only through spec revisions.
- Update the stdlib: `replaceRange(r, with:)` becomes `replaceRange(r, by:)`. `xInto(ref dst)` is unchanged.

## 16. Secrets and derivations

- A derivation that would read a `Secret<T>` field is a compile error unless the field opts in: `@json(expose) because "…"`. `debug` still prints `***`.
- `@derive(Row)` rejects non-flat fields (nested structs, lists) with a clear diagnostic.

## 17. Trust: two layers

**Layer 1: sinks take dedicated types, never `string`.**

```csharp
public void execute(Sql q) …
public void render(Html h) …
public void run(Path program, List<Arg> args = []) …   // optional setting, not a second object

db.query(sql"SELECT * FROM users WHERE name = $1", term)   // literal plus parameters
let page = Html.escape(name)                               // escaping constructor
```

This applies whatever the origin of the data, so it also blocks stored XSS from your own database.

Only parameterized `sql"…"` literals with `$n` placeholders are in scope now. Interpolating prefixed literals (`sql"…{x}…"`, `html"…"`) stays deferred from round 2.

**Layer 2: `Untrusted<T>` for values from outside**, where escaping is impossible:

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

- **Sources** return `Untrusted<T>` in their signatures: request query, body, headers, cookies, route parameters, uploaded files, third-party API responses, files from user-chosen paths. Startup environment and config, literals, constants and your own database rows are trusted.
- Operations on an `Untrusted` value return `Untrusted` (strings and structs derived from it). Facts like `isEmpty`, `length` and comparisons are clean, so branching on untrusted data is allowed.
- Filtering (`except`, `where`, patterns) preserves taint. **Only construction of a type with `requires` removes it.**
- `T.fromJson(untrusted)` returns a trusted `T` only if `T`'s `requires` cover its fields; otherwise `Untrusted<T>` (precise rule: Open question O10).
- Explicit escape: `x.trustUnchecked() because "…"`.
- This model is provisional and must be exercised in the reference application (§21).

## 18. `try` scope

`try` covers the entire expression to its right, down to the lowest-precedence operator. Applying it to a sub-expression requires parentheses; the diagnostic suggests where.

## 19. `defer` with postfix modifiers

In `defer failing tx.rollback() unless dryRun`, the modifier is evaluated once, when the `defer` is **registered**, and the value is captured, the same rule as `unless` on any statement. The exit-time form is `defer { if c { … } }` (Sūq review A1).

## 20. Input forms added (SPEC §15 table)

| Input form | Canonical |
|---|---|
| Trailing `;`, `a; b` on one line | Newlines |
| `Grams w = 0g` (local binding) | `let w: Grams = 0g` |
| `f() -> T`, `f(x: T)`, field `x: T`, payload `case(x: T)` | `T f()`, `f(T x)`, `T x`, `case(T x)` (§2, revised 2026-10-22) |
| `[Derive(Json)]` | `@derive(Json)` |
| `/* … */` | `//` lines |
| `unique struct`, `distinct type`, `error union`, `error set` | `struct unique`, `type distinct`, `union error` |
| `char` | `rune` |
| `requires value…` | `requires it…` |
| `case _ except .x` | `case not .x` |
| Booleans in the non-project spelling | The project's `style.booleans` spelling |
| `noalloc`, `nosuspend` | Removed: no `uses` clause already means pure |
| `return .x` in a function that fails, when the return type has no case `.x` | `fail .x` (Sūq review A2) |
| `(u8)x`, `(T)x` | `u8(x)`, `T(x)` (Sūq review A4) |

Remove from the round-2 table: `if !x { … }` → `unless x { … }`.

---

## 21. Reference application: Sūq

Build **Sūq** (سوق, "market"), a commerce and warehouse system that uses every Bayan feature, most of them at least twice and in different ways. It is both the language-design test and the compiler's end-to-end test.

### Parts

| Part | Exercises |
|---|---|
| Storefront HTTP API (JSON and forms) | Derivations, `requires`, `Untrusted<T>`, `T.Patch`, `except`, sink types |
| Order lifecycle | Typestate, unions, exhaustive switches, `defer failing` |
| Warehouse packing | Units (g, kg), typed indices, the zero-allocation hot path |
| Carrier integration | Effects, `fails`, deadlines, fibers, `provide` in tests |
| Database | `Row`, binary COPY, transactions, regions, `sql"…"` |
| Payments | `Secret<T>`, `because`, FFI (e.g. libsodium), `unique` resources |
| Admin scripts | Scripting mode, `Args` and `Config` derivations |
| Libraries | Generics, extensions, `Op` and `xInto` patterns, laws, particles |
| Packages | Capabilities: a pure `pricing` package without `.net` |

### Method

1. **Coverage matrix** (`docs/bayan-reference-app-coverage.md`): every SPEC section maps to the Sūq files that use it. Add `bayan features --used` (the compiler reports the features a build used). CI fails if any non-deferred spec feature is unused, so adding a feature to the spec means adding it to Sūq.
2. **Compile-fail corpus** (`suq/fail/`): one broken snippet per diagnostic, with a snapshot of the expected error and fix-it.
3. **Friction log** (`docs/bayan-reference-app-friction.md`): write each part in Bayan before the compiler supports it. Log every place the code reads badly or needs a workaround, as a design issue with a proposed fix. Do not change the spec from the friction log on your own; flag the issues in the end-of-run summary.
4. **C# baseline** (`suq-csharp/`): the same app in C#, to compare line counts, allocations and throughput.
5. **Benchmark budgets:** the packing path must be zero allocations per operation; record time per operation and variance for each hot path.
6. **Phasing:** each compiler milestone unlocks the Sūq parts it can compile. Record per milestone which parts compile, in the coverage doc.

Start with the paper version of the storefront, order lifecycle and packing parts, since they exercise the most round-3 features.

---

## Open questions (resolve before implementing these parts)

- **O1. One-line unions.** §3 makes declarations newline-separated, so `union LineStatus { active, removed, backordered }` would be invalid. Recommendation: newline-only; `bayan fmt` rewrites the one-line form.
- **O2. Units on text lengths.** If `runeCount` returns `Runes` and `length` returns `Bytes`, then `requires it.runeCount in 8..=128` violates the bare-literal rule (§8). Either give `Runes` a literal symbol, or make counts plain `i64` and keep the units only for slice offsets.
- **O3. Field-level vs struct-level `requires`.** In §11 the only thing separating `age`'s rule from the struct-level rules is a blank line. Recommendation: decide by content — a `requires` that refers only to `it` belongs to the field above; one that refers to other fields is struct-level wherever it appears.
- **O4. `on .password`.** `on` is a new word. Recommendation: reuse `at` (position): `requires password != username at .password`.
- **O5. Two return arrows.** Declarations now use `->` (§2), but round-2 function types use `=>` (`Order => void`). Recommendation: `->` for "returns" everywhere, including function types (`Order -> void uses net`); `=>` only for "evaluates to" (lambdas, expression bodies, switch arms).
- **O6. Particles spelled like keywords.** The particle `for` collides with the `for` loop. Recommendation: allow it, because a particle only appears in label position (`for:`), which is unambiguous.
- **O7. Mixed-currency `Money`.** Recommendation: `+` traps like integer overflow; `tryAdd` and `add(…) fails` exist for code that expects it.
- **O8. `state` as an identifier.** Recommendation: `state` is a contextual keyword at the start of a member declaration only, so fields named `state` (e.g. an address's state) still work.
- **O9. Bare literals for distinct types.** Recommendation: rejected, as for units: `let id: ParcelId = 0` is an error; write `ParcelId(0)`.
- **O10. When decoding produces a trusted `T`.** Recommendation: trusted only if every field's type is either a type with `requires` or a non-text scalar (numbers, bools, units, plain unions); any free `string` or byte field makes the result `Untrusted<T>`.

**Resolved 2026-10-22 (user accepted every recommendation):** O1 newline-only unions, `bayan fmt` rewrites the one-line form. O2 (no recommendation given; smallest choice taken): counts (`length`, `runeCount`) are plain `i64`; units apply only to slice offsets. O3 by content. O4 `at`, not `on`. O5 `->` for "returns" everywhere, including function types; `=>` only for "evaluates to". O6 particle `for` allowed (label position only). O7 mixed-currency `+` traps; `tryAdd` and `add(…) fails` exist. O8 `state` is contextual at the start of a member declaration. O9 bare literals for distinct types rejected. O10 the trusted-field rule as recommended.

**Sūq review decisions, 2026-10-22** (`docs/suq-agent-brief.md` Part A; record in `docs/suq-round3-review-response.md`):
- **A1.** A `defer` modifier is evaluated once at registration and its value captured (§19); `defer { if c { … } }` is the exit-time form.
- **A2.** `fail .x` leaves with an error; `return` always returns a value. `fail .x if cond` and `?? fail .x` follow. `return .x` in a function that fails is an input form for `fail .x`, only when the return type has no case `.x`.
- **A3.** Union payloads bind by position (`.circle(let r)`) or as the whole value: `.percentOff p => p.rate`, with `p` narrowed to that case.
- **A4.** Numeric conversions are `T(x)`, checked (trap when out of range); `T.tryFrom(x)` returns `T?`. `(T)x` is an input form.
- **A5.** `s.copy()` and `s.copy(to: arena)` copy a borrowed string.
- **A6.** `assert f() fails E.case` and `assert f() fails E`, with power-assert output. This is a statement form, not the deferred fluent assert API.
- **A7.** Adopted as implemented: a struct with any `unique` field is `unique` (explicit `struct unique` stays for raw resources); owning values never copy implicitly; fallible constructors are `T(x)`, `T.tryFrom(x)`, `T.tryParse(text)`; the empty map literal is `[]`.

## Deferred (do not implement)

- String `+` and fluent asserts (kept for future consideration).
- Effect variables in function types and interfaces (option B).
- `format(into: ref buf)` as a particle form of `xInto`.
- Collection-value-tied indices (`orders.Index`).
- Growing the particle set (`until`, `between`, `per`, `within`) — only by spec revision, when Sūq shows a need.

## Rejected (do not re-propose)

- `@` as lambda shorthand (keep `it`).
- `mut` (keep `ref this`).
- Automatic `T.Count` / `T.Index`; `Count of T`, `per` for units.
- Bare `0` for unit types.
- `T.Validated` / `validated(T)`.
- `stmt unless not cond`.
- `with` as the handler keyword (it is struct update).
- A bare string as a purpose clause (keep `because`).
- `panic` as an effect.

## Deliverables

1. `SPEC.md` updated for §1–20, each change with a one-line rationale and its diagnostic codes.
2. `GRAMMAR.md` and `STYLE.md` updated (§1, §5, §12, §13, §15). In STYLE.md's member order, rename "state members" to "properties" and place typestate `state` declarations directly after fields.
3. Sweep every code example in SPEC.md, GRAMMAR.md, STYLE.md and `docs/` to round-3 syntax: name-first local bindings, type-first declarations; no semicolons; `@` attributes; particles; `rune`; head-first declaration keywords.
4. Verify collections use `length` for size and `count(p)` only for predicate counts (round 2 §11 lists `count` among the queries).
5. Compiler changes, in this order:
   1. Lexer and parser: semicolon-free termination (§3), `@` attributes and `because` (§4), head-first declaration keywords, name-first local bindings and type-first declarations with their name-first input forms (§2), `//`-only comments, `except` (§7), `state`/`becomes`/`become` (§14), particles (§15), `provide` (§13).
   2. Input-form rejection with fix-its (§20), and `bayan fmt` rewrites including `style.booleans`.
   3. Semantics: unused-value error, `requires` at construction and `with` re-checking (§11), typestate (§14), effects and capabilities (§13), unit conversion and literal rules (§8), typed indices (§9), trust layers (§17), `try` scope (§18), `defer` modifier capture (§19).
   4. Standard library: `rune`, `log`, particle names, sink types, `T.rules`.
   5. `bayan audit` and `bayan features --used`.
6. Tests for each item: positive, negative, and diagnostic snapshots.
7. Sūq: coverage doc, friction log, compile-fail corpus skeleton, and the paper versions of the first three parts (§21).
8. An end-of-run summary: what changed, what was not done and why, spec conflicts that needed a judgment call, and the top friction-log items.
