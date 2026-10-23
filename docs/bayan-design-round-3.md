# HANDOFF — Bayan, design round 3

## Context

Bayan is a C#-derived, natively compiled language (no .NET runtime). `SPEC.md` exists, the round-2 HANDOFF has been applied or is in progress, and the v0.1 bootstrap compiler is being built.

This handoff records every decision from design round 3, including the signature review of 2026-10-23. It **supplements** round 2: everything in round 2 still stands unless this document changes it, and this document wins on any conflict. Section numbers §22–§23 are used by the plan for the Sūq review and friction items; the sections added by the signature review are §24–§29. The changelog at the end lists what changed and when.

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
  1. No synonyms in the lexicon, except documented exceptions. An exception must (a) come from a closed token set, (b) map one-to-one with no change in meaning, cost or effects, and (c) be normalized per project by `bayan format` from a `style` setting. Exactly two exist: boolean spellings (§5) and primitive spellings (§28). No other construct qualifies.
  2. New syntax must have its own job; it may not only re-spell an existing construct.
  3. Paraphrases must be truly equivalent: same narrowing, cost and effects. Where two forms are not equivalent, a lint flags the difference (e.g. `0..=n - 1` vs `0..n`, `p == Point { … }` vs `p is Point { … }`).
- **Head-first word order (Arabic).** Statements: the verb comes first and ḥāl modifiers follow (`defer failing`). Declarations: the kind comes first and modifiers follow (`struct unique`, `type distinct`, `union error`), and the type is the head of a declaration, so it leads (`Grams weight`, `public Grams shippingWeight(…)`). Local bindings: `let`/`var` lead, then the name, then an optional type (§2).

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
- Generic error sets compose the same way: `union error DecodeError<T> = JsonError, ValidationError<T>` (§11).
- `set error` is removed.

### Declarations type first, local bindings name first

```csharp
struct Item {
    Sku sku
    Grams weight
}

public Grams shippingWeight(Order order) {
    // three alternatives for one local:
    let w = 0g              // immutable, inferred
    let w: Grams = 0g       // immutable, explicit type
    var w: Grams = 0g       // mutable, explicit type
    …
}
```

- **Declarations put the type first**: return types (`public Grams shippingWeight(Order order)`, `void` when nothing is returned), parameters (`(Order order)`, `(ref this, Line line)`), fields (`Sku sku`), union and state payloads (`timeout(Millis after)`), computed properties (`public bool isEmpty => lines.isEmpty`), and test and benchmark parameters (`test "bounds" (i32 input, bool valid) { … }`).
- **Local bindings put the name first** with explicit mutability: `let` (immutable) or `var` (mutable), with an optional `: Type`. Loop bindings follow the local rule (§25).
- **Input forms** (`bayan format` rewrites them, BYN1043): `f() -> T` → `T f()`; `f(x: T)` → `f(T x)`; field `x: T` → `T x`; payload `case(x: T)` → `case(T x)`; in a function body, `Grams w = 0g` → `let w: Grams = 0g`, or `var w: Grams = 0g` when the local is assigned later (the fix needs binder information).

### Signature shape

```csharp
// function or method
[modifiers] ReturnType name<T>(params) [where …] [becomes .s] [fails E] [uses e, …] { … }   // or => expr
// parameter
[particle] [ref | out | const | move] Type name [= default]
// receiver (first parameter only)
[ref | move] this [is .state [or .state …]]

Type name [= default]                 // field, then optional `requires` lines
Type name => expr | { … }             // computed property
(A, B) -> R [fails E] [uses …]        // function type
```

- **Modifiers** (`public`, `static`) lead the declaration.
- **Clause order** is fixed: `where` → `becomes` → `fails` → `uses` (success outcome, then failure, then effects). A signature may continue on the next line with any of these clauses (§3).
- **A particle comes before the passing mode:** `to ref Span<u8> dst`.
- **Function types are parenthesized when a name follows them**, in fields and parameters: `(Line -> Money uses net) pricer`, `by (Token -> bool) p`. An alias avoids the parentheses: `type Pricer = Line -> Money uses net`, then `Pricer pricer`.
- **State types** are written `Order.paid` (one state) or `Order order is .paid or .shipped` (several, inline after the name). Details in §14.

### The receiver

`this` is the receiver parameter's name; its type is the enclosing struct and is elided.

| Receiver | Meaning |
|---|---|
| (not written) | Read-only, any state |
| `this is .shipped` | Read-only, only in that state (§14) |
| `ref this` | Mutating, any state (Form V, unchanged) |
| `ref this is .placed` | Mutating, only in that state |
| `move this` | Consuming: the value is moved into the call and cannot be used after it |
| `move this is .open` | Consuming, only in that state |

- `this` written without a state is the same as not writing it, so it is rejected with a fix-it that removes it (BYN1061).
- Several states: `this is .shipped or .delivered`.
- The receiver never counts as the object for particles (§15).
- `move this` ends ownership (`tx.commit()`, `rows.all()`); typestate (`becomes`) moves a live value through a lifecycle. Both exist because they do different jobs.

### `unique`

`unique` stays as the move-only keyword, for resources (`File`, `Socket`, `Transaction`, `Lock`, `Rows<T>`). A `unique` value cannot be copied: assignment and passing move it, the compiler rejects any use after a move, and when its single owner goes away its cleanup runs once. It is unrelated to `const`. A `unique` type cannot be `const`. Plain data types (orders, lines) are never `unique`.

### Named patterns

```csharp
const inactive = pattern Line { status: .removed or .backordered }
for line in order.lines except inactive { … }
```

Patterns are `const` values of pattern type, named in camelCase. `pattern` is a contextual keyword, valid only after `=` in a `const` declaration.

## 3. Statement termination: no semicolons (waqf rules)

`;` is removed. `bayan format` strips trailing semicolons and splits same-line statements; the compiler rejects `;` with that fix-it.

| Waqf mark | Rule |
|---|---|
| مـ lāzim (mandatory stop) | A newline after a complete statement ends it |
| لا lā (forbidden stop) | The statement continues if the line ends with a binary operator, `,`, `=`, `=>` or `{`, or the break is inside `( )` or `[ ]` |
| وصل waṣl (joining) | The next line continues the statement if it starts with `.`, `?.`, `??`, `except`, `requires`, `expects`, `where`, `becomes`, `fails`, `uses`, `catch`, `&&`, `\|\|`, `and` or `or` |
| Never-join | A line starting with `(`, `[` or `-` always begins a new statement |

Supporting rules:

- **An unused non-void value is a compile error.** This turns every mis-split line into a diagnostic (`let x = a` newline `-b`).
- `return`, `break` and `continue` take their operand on the same line. A postfix modifier (`unless`, `if`) must be on the same line as its statement.
- `else` must follow `}` on the same line; `else` on a new line is rejected with a fix-it.
- Comment lines are transparent to continuation. Outside `( )` and `[ ]`, a blank line always ends a statement; inside them, blank lines are ignored like other newlines.
- Struct literals in a control-flow head: whole-condition parentheses, as already decided: `if (p is Point { x: 0, y: 0 }) { … }`.

**Separators:** declarations (fields, union cases, states) are separated by newlines. Entries in literals and argument lists are separated by commas (with trailing commas, per STYLE.md).

## 4. Attributes and purpose clauses

- Attributes use `@`: `@derive(Json, Row)`, `@allow(BYN5012)`, `@cases(...)`, `@positional` (§15). `[Attr]` becomes an input form (a line starting with `[` is a collection literal under §3). Attribute names are camelCase (`@derive`, `@allow`); derivation names stay PascalCase (`Json`, `Row`).
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
- **Boolean spellings are both allowed** (documented synonym exception 1, §1): `!` `&&` `||` and `not` `and` `or` in boolean expressions. `bayan format` normalizes each project to one spelling:
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

- **Implicit conversion only toward finer units** (`2s + 500ms` → `Millis`). Converting to a coarser unit is explicit and names the rounding with a type argument: `t.floor<Seconds>()`, `t.ceiling<Seconds>()`, `t.rounded<Seconds>()` (nearest, ties away from zero). When the target is typed, inference fills the type argument: `let billed: Minutes = duration.ceiling()`.
- **Bare literals are always rejected for unit types, including `0`:** `let w: Grams = 0` is an error with a fix-it to `0g`.
- **Display:** a unit formats as its value plus its symbol (`524288 B`). To display in a coarser unit, convert with a named rounding: `size.rounded<KiB>()` formats as `512 KiB`. There is no separate `convert` member.
- **Quantities are dimensionless:** `i32 qty`, and `i32 × Grams → Grams`.
- **`Money` is a struct with a currency field, not a unit.** Adding mixed currencies traps like integer overflow; `tryAdd` and `add(…) fails` exist for code that expects it (O7). `price.rounded()` rounds to the currency's minor unit. The unit docs cite `Money` as something units don't model.

Real-world uses: billing per started minute (`ceiling`), carrier weight brackets per started kg (`weight.ceiling<Kilograms>()`), a `Retry-After` header in whole seconds (`wait.ceiling<Seconds>()`, never early), and countdown display (`elapsed.floor<Seconds>()`).

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

`rune` replaces `char` everywhere: the 32-bit scalar type, `runeCount`. `char` becomes an input form. `s.length` stays the O(1) byte length. Counts (`length`, `runeCount`) are plain `i64` (O2).

## 11. Validation: `requires` everywhere, `expects` for contracts

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

    requires password != email.value  at .password
    requires password != username     at .password
}
```

- `it` replaces `value` in `requires`.
- Each `requires` line is a separate rule and reports its own violation. A rule that refers only to `it` belongs to the field above it; a rule that refers to other fields is struct-level wherever it appears (O3). Struct-level rules attach to a field with `at .field` (target-typed to `T.Field`, O4).
- **Errors:**
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
- **Decoders collect every violation** and report malformed input in the same error set: `static T fromJson(Untrusted<string> body) fails DecodeError<T> uses alloc`. A caller matches both: `catch switch { .invalid e => … e.violations, .syntax s => … }`.
- **Drafts** use the existing `T.Patch` (all fields optional, no `requires`), with `patch.complete()` returning `T fails ValidationError<T>`. The two-step flow (`T.Patch.fromJson`, then `complete()`) is a paraphrase of `fromJson`: both report the same errors.
- Struct literals of a type with `requires` are fallible: `try SignupForm { … }`.
- Fields of a struct with `requires` cannot be assigned one at a time. Updates go through `with`, which re-checks: `try f with { password: p }`.
- **Rules as data:** the derivation exposes `T.rules: List<(T.Field field, Rule rule)>` at compile time, for web frameworks (HTML5 attributes). A recognized expression shape maps to a `Rule` case; anything else is `custom` and is checked only on the server:
  ```csharp
  union Rule {
      length(Range<i64> bounds)     // it.runeCount in a..=b
      range(Range<i64> bounds)      // it in a..=b
      pattern(Regex re)             // it.matches(re)
      required                      // non-nullable field
      kind(TypeId id)               // Email, Url, Date
      custom(RuleId id)
  }
  ```
- Violations are structured (field, rule, parameters) with **no message strings**. Frameworks localize them.
- Remove `T.Validated` from the §5.2 noun-pattern table, the `Validate` derivation from the §5.3 list, and any field-level `[Check]` from the spec.
- Form-encoded decoding (`@derive(Form)`) belongs to the future web framework, not the standard library.

### Preconditions: `expects`

Function preconditions use `expects`, not `requires`:

```csharp
type distinct Count = i64 requires it > 0
public List<T> take(Count n) uses alloc                  // reusable rule: fails at construction

public Span<T> slice(Index start, until Index end)
    expects start <= end { … }                           // relation between parameters: traps
```

| | Type with `requires` | `expects` |
|---|---|---|
| Purpose | Validating data | Contract between programmers |
| On violation | Fails, recoverable (`ValidationError`) | Traps, like a bounds check |
| Checked | Once, at construction; the proof travels with the value | Every call, in every build mode |

- An `expects` that reads an `Untrusted<T>` value is a compile error: trapping on outside data would let a request crash the program.
- A lint flags an `expects` that reads only one parameter and suggests a type with `requires`.
- `requires` on a function is an input form for `expects`.

## 12. GRAMMAR rules added

- **`T.X` vs `X<T>`:** `T.X` with a PascalCase `X` is a different shape derived from `T` (`Builder`, `Patch`, `Field`, `Id`). `T.x` with a camelCase `x` is `T` in state `x` (`Order.paid`, §14). `X<T>` is the same value with a different status (`Untrusted`, `Secret`).
- **States** (§14) are named as participles or adjectives (`paid`, `shipped`) and are exempt from the `-ed` copy-pattern rule, which applies only to members.
- **Events** (§26) are named for what happened: a past participle (`placed`, `changed`) or an adjective (`low`). The `on` prefix (`onChange`, `onPaid`) is rejected: a member is named with a verb or a noun, and subscribing is spelled `subscribe(by:)`.
- **Particle words are not member names.** `at(k)`, `to(x)`, `by(f)` fail the grammar check; members are verbs or nouns.
- **Nouns with no arguments are properties** (`x.abs`, `v.magnitude`, `rows.isEmpty`); with arguments or type arguments they are calls (`x.pow(2)`, `t.floor<Seconds>()`). A generic member stays a call when its type argument is inferred (`let s: Seconds = t.floor()`).
- **`-ed` copies allocate only for types that own heap memory.** `v.normalized()` on a `Vec3` and `x.clamped(…)` on a scalar copy without allocating; `xs.sorted()` on a list allocates.
- **Math vocabulary is a documented naming exemption** (a naming rule, not a synonym): `abs`, `sign`, `sqrt`, `cbrt`, `pow`, `exp`, `ln`, `log2`, `log10`, `sin`, `cos`, `tan`, `asin`, `acos`, `atan2`, `radians`, `degrees`. There is no `log` member: `log` is the logging verb.
- **Logging:** `log` is the verb; the level is a particle argument (§13). `Log.debug` fails the grammar check (`debug` is a level, not the act).
- **No abbreviations** applies to tools as well: the formatter is `bayan format` (§29).

## 13. Effects

Unify `noalloc`, `nosuspend` and package capabilities into one effect system.

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

  The list grows only through spec revisions.
- **Effect aliases** name a set without adding a capability: `effect db = net, fs`. An alias expands to its primitives everywhere (capabilities, bounds, `bayan expand`). `std.db` declares `db`.
- **No `uses` clause means pure.** This replaces `noalloc nosuspend`, which are removed.
- **Public functions declare their effects; private functions infer them** (same rule as error sets). Tests are private, so they infer. `main` and scripts infer too, bounded by the package capabilities. There is no file-level or struct-level `uses`: a public signature is a contract, and an effect inherited from elsewhere would be invisible at the declaration.
- `panic` is **not** an effect.
- `fails` stays a separate clause, because it carries a type. GRAMMAR.md states this reason.
- **Function-typed parameters with no `uses` are effect-polymorphic:** the call inherits the effects of the function passed. `map` inherits its closure's effects; there is no `tryMap` or `mapAsync`.
  ```csharp
  public List<U> map<U>((T -> U) f) uses alloc
  let names = users.map(it.fetchName())      // this call also uses net
  ```
  A function-typed parameter that the callee stores (it escapes the call) is not polymorphic: it takes the bound of the place it is stored in (`Event<T>.subscribe`, §26).
- **Stored function types, interface methods and event types declare an upper bound** (option A). No `uses` there means pure. Implementations may use fewer effects:
  ```csharp
  interface IPricer {
      Money price(Order o) uses net, alloc
  }
  type Pricer = Line -> Money uses net
  type OrderPlaced = Event<Order.placed> uses net, alloc, suspend    // §26
  ```
  An effect bound in a type (`Event<T> uses …`) is the only form of effect parameter; general effect variables (option B) stay deferred.
- **Derived forms compute their effects:** `-ed` adds `alloc` for heap-owning types (§12), the allocating form derived from `xInto` adds `alloc`, and `tryX` keeps the original's effects. `bayan expand` shows them.
- **Package capabilities are an upper bound:** a package without `.net` cannot contain any function that `uses net`, transitively.
- **Logging is ambient:** allowed in pure functions, never allocates (the interpolated message is formatted through a handler into a fixed buffer, only when the level is enabled), escapes control characters, and is interceptable:
  ```csharp
  log($"weighing {order.id}")                // default level: info
  log($"retry {n} of {max}", as: .debug)
  ```
  `print` is program output, not diagnostics: it `uses console`.
- **Handlers use `provide`.** `context` remains only the value (`context.Io`). Handler names are the effects plus `log`:
  ```csharp
  let net = FakeNet(label)
  provide net: net, clock: FakeClock(at: Date(year: 2026, month: 12, day: 1)) {
      try shipAll(order, by: carrier)
  }
  assert net.calls == 1

  let logs = LogRecorder()
  provide log: logs { … }
  ```
  Inner `provide` blocks override individual handlers and inherit the rest. Handlers are one-shot, so they compile to context passing at zero cost.
- **The test runner provides `console` per test as a capture buffer.** Output is shown when a test fails, or always with `bayan test --verbose`.

## 14. Typestate (kāna / ṣāra)

States are declared in one `states` block inside the struct, after the fields:

```csharp
@derive(Json)                  // not Row: `lines` is not flat (§16)
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

**Rules**

1. `T.s` is the type of `T` values in state `s`. It is valid anywhere a type is: parameters, returns, fields, locals and generics (`List<Order.paid>`).
2. Several states: `Type name is .a or .b` inline in a parameter or field, `let x: T is .a or .b` in a local, or an alias (`type Fulfilled = Order is .paid or .shipped`). Return types use `T.s` or an alias.
3. The receiver keeps its own form: `this is .s`, `ref this is .s`, `move this is .s`, and the same with `or`.
4. Widening is implicit and free: `Order.paid` → `Fulfilled` → `Order`. Narrowing happens only through `is`, `switch`, or a `becomes` call on a `var`.
5. Narrowing results: `if o is .paid { … }` gives `Order.paid`; `.paid p => …` binds `p: Order.paid`.
6. A payload field is readable only when the type guarantees its state. With several states, a field is readable if every listed state has it with the same type (`payment` is readable on `Fulfilled`, `tracking` is not).
7. Literals name a state: `Order.paid { id: i, lines: l, payment: p }`. A plain `Order { … }` on a struct with states is an error (BYN7005).
8. Runtime: `T.s` has the same layout as `T` (a `u8` tag plus a payload union) and is erased in C. Widening is a no-op; the checks are compile-time only.
9. Canonical spelling: one state is always written `T.s`. `bayan format` rewrites `Order order is .paid` → `Order.paid order` and `type X = Order is .paid` → `type X = Order.paid`. The receiver is exempt.
10. State names are camelCase and may not equal a static member name of `T` (otherwise `Order.empty` would name both a type and a factory).
11. `becomes .state` declares a transition; `become .state(…)` performs it. **A failed transition leaves the state unchanged.** After a `becomes` call on a `var` (with no live borrows), the variable's static type changes: after `try order.pay(card)`, `order` is `Order.paid`. A `let` cannot call `ref this` methods, so it needs no tracking.
12. A state switch is exhaustive, so adding a state is a compile error at every non-exhaustive switch.
13. `states` is a contextual keyword at the start of a member declaration when followed by `{`, so fields named `state` or `states` still work. At most one `states` block per struct.

## 15. Argument particles

A closed set of role particles (Arabic مِن/إلى/بِ):

| Particle | Role |
|---|---|
| (none) | The object being acted on |
| `from` | Source, or start of a span |
| `to` | Destination or recipient |
| `until` | Exclusive end of a span |
| `through` | Inclusive end of a span |
| `at` | Position or time |
| `by` | Means or replacement |
| `for` | Beneficiary or purpose |
| `as` | Role |

```csharp
public void copy(from Path src, to Path dst) fails FsError uses fs
public void transfer(ref this, Money amount, from Account payer, to Account payee) fails LedgerError
public void insert(ref this, T item, at Index i) uses alloc
public void remove(ref this, from Index start, until Index end)

copy(from: a, to: b)
try ledger.transfer(amount, from: alice, to: bob)
lines.insert(x, at: 3)
xs.remove(from: 2, until: 5)
```

- A function has at most one unlabeled parameter (the object). Every other required parameter takes a particle, and call sites must write it. **The receiver does not count** as the object.
- **Optional parameters** are named parameters with defaults. They may take a particle when one fits their role (`log(m, as: .debug)`, `FakeClock(at: …)`); otherwise they use their name (`timeout: 5s`).
- **Exempt:** a `rest` tail (§24); constructors of a struct marked `@positional`, whose components are the same kind and universally ordered (`Point(3, 4)`, `Vec3(1.0, 0.0, 0.0)`, `Rgb(…)`; not `Date`, which keeps `Date(year:month:day:)`); `extern` declarations; operator declarations; union and state payloads (fields, not parameters); laws.
- The particles are part of the function's name (`copy(from:to:)`). Particle words are not member names (§12).
- The set is closed for user code and grows only through spec revisions (`until` and `through` were added by this round).
- `bool` parameters are still passed by name.

## 16. Secrets and derivations

- A derivation that would read a `Secret<T>` field is a compile error unless the field opts in: `@json(expose) because "…"`. `debug` still prints `***`.
- `@derive(Row)` rejects non-flat fields (nested structs, lists) with a clear diagnostic.

## 17. Trust: two layers

**Layer 1: sinks take dedicated types, never `string`.**

```csharp
public i64 execute(ref this, Sql sql, rest ReadOnlySpan<ISqlValue> args) fails DbError uses db, suspend
public void render(Html h) …
public void run(Path program, List<Arg> args = []) fails ProcessError uses process   // optional setting

let users = try db.query<User>(sql"SELECT * FROM users WHERE name = $1", term).all()   // literal plus parameters
let page = Html.escape(name)                                                            // escaping constructor
```

This applies whatever the origin of the data, so it also blocks stored XSS from your own database.

Only parameterized `sql"…"` literals with `$n` placeholders are in scope now (`$n` is Postgres's native syntax; the compiler rewrites placeholders per dialect). Interpolating prefixed literals (`sql"…{x}…"`, `html"…"`) stays deferred from round 2.

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
| `expects` | Never reads an `Untrusted` value (§11) |

- **Sources** return `Untrusted<T>` in their signatures: request query, body, headers, cookies, route parameters, uploaded files, third-party API responses, files from user-chosen paths. Startup environment and config, literals, constants and your own database rows are trusted.
- Operations on an `Untrusted` value return `Untrusted` (strings and structs derived from it). Facts like `isEmpty`, `length` and comparisons are clean, so branching on untrusted data is allowed.
- Filtering (`except`, `where`, patterns) preserves taint. **Only construction of a type with `requires` removes it.**
- `T.fromJson(untrusted)` returns a trusted `T` only if every field's type is either a type with `requires` or a non-text scalar (numbers, bools, units, plain unions); any free `string` or byte field makes the result `Untrusted<T>` (O10).
- Explicit escape: `x.trustUnchecked() because "…"`.
- This model is provisional and must be exercised in the reference application (§21).

## 18. `try` scope

`try` covers the entire expression to its right, down to the lowest-precedence operator. Applying it to a sub-expression requires parentheses; the diagnostic suggests where. One `try` covers a chain: `try db.query<User>(sql"…", id).single()` covers both calls.

## 19. `defer` with postfix modifiers

In `defer failing tx.rollback() unless dryRun`, the modifier is evaluated once, when the `defer` is **registered**, and the value is captured, the same rule as `unless` on any statement. The exit-time form is `defer { if c { … } }` (Sūq review A1).

## 20. Input forms added (SPEC §15 table)

| Input form | Canonical |
|---|---|
| Trailing `;`, `a; b` on one line | Newlines |
| `Grams w = 0g` (local binding) | `let w: Grams = 0g` (or `var` when reassigned) |
| `f() -> T`, `f(x: T)`, field `x: T`, payload `case(x: T)` | `T f()`, `f(T x)`, `T x`, `case(T x)` (§2) |
| `[Derive(Json)]` | `@derive(Json)` |
| `/* … */` | `//` lines |
| `unique struct`, `distinct type`, `error union`, `error set` | `struct unique`, `type distinct`, `union error` |
| `char` | `rune` |
| `requires value…` | `requires it…` |
| `requires …` on a function | `expects …` (§11) |
| `case _ except .x` | `case not .x` |
| Booleans in the non-project spelling | The project's `style.booleans` spelling |
| Primitives in the non-project spelling | The project's `style.primitives` spelling (§28) |
| `noalloc`, `nosuspend` | Removed: no `uses` clause already means pure |
| `return .x` in a function that fails, when the return type has no case `.x` | `fail .x` (Sūq review A2) |
| `(u8)x`, `(T)x` | `u8(x)`, `T(x)` (Sūq review A4) |
| `Order order is .paid`, `type X = Order is .paid` | `Order.paid order`, `type X = Order.paid` (§14) |
| `state x` member lines | One `states { … }` block (§14) |
| `this` receiver written without a state | Receiver omitted (§2) |
| `params T[] items`, `params ReadOnlySpan<T> items`, `T... items` | `rest ReadOnlySpan<T> items` (§24) |
| Interface property `Type name { get }` | `Type name` (§26) |
| `A => B` as a function type | `A -> B` (O5) |

Remove from the round-2 table: `if !x { … }` → `unless x { … }`.

---

## 21. Reference application: Sūq

Build **Sūq** (سوق, "market"), a commerce and warehouse system that uses every Bayan feature, most of them at least twice and in different ways. It is both the language-design test and the compiler's end-to-end test.

### Parts

| Part | Exercises |
|---|---|
| Storefront HTTP API (JSON and forms) | Derivations, `requires`, `Untrusted<T>`, `T.Patch`, `except`, sink types |
| Order lifecycle | Typestate (`states`, `Order.paid`), unions, exhaustive switches, `defer failing`, events (§26) |
| Warehouse packing | Units (g, kg), typed indices, the zero-allocation hot path, `rest` parameters |
| Carrier integration | Effects, `fails`, deadlines, fibers, `provide` in tests |
| Database | `std.db` shape (§27), `Row`, binary COPY, transactions, regions, `sql"…"`, `unique` cursors |
| Payments | `Secret<T>`, `because`, FFI (e.g. libsodium), `unique` resources, `move this` |
| Admin scripts | Scripting mode, `Args` and `Config` derivations, `process` and `console` |
| Libraries | Generics, overloads (§24), interface default methods, `Op` and `xInto` patterns, laws, particles |
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

## 24. Overloading, defaults and variadic parameters

### Defaults

Unchanged: parameters may have defaults (`List<Arg> args = []`). Named optional parameters may follow the `rest` parameter; they can only be passed by name.

### Overloading

Overloads may differ by particle set (`move(from:to:)` vs `move(_, by:)`) or by parameter type. The checks run at the **declaration**, so a call never needs C#-style ambiguity resolution:

1. Overloads that differ only by type must not overlap through any implicit conversion (finer units, integer widening, state widening). `void f(Grams g)` beside `void f(Kilograms k)` is an error, because `Kilograms` converts to `Grams`.
2. Overloads may not differ only by function-type parameters, since `it` lambdas need one target type.
3. Defaults may not make two overloads accept the same argument list.
4. A reference to an overloaded name needs a target type: `let f: i32 -> i32 = abs`.
5. Type variation over number families uses generics, not overloads: `public T abs<T>(T x) where T : ISignedNumber<T>`.

```csharp
public void remove(ref this, at Index i)
public void remove(ref this, at Range<Index> r)        // ok: no conversion between Index and Range<Index>
public T.Id insert<T>(ref this, T row) where T : IEntity …
public i64 insert<T>(ref this, ReadOnlySpan<T> rows, bool ignoreConstraints = false) where T : IEntity …
```

### Variadic parameters: `rest`

```csharp
public void print(rest ReadOnlySpan<Display> items) uses console

print("total", total, "for", order.id)
let p = Path.join(root, "orders", $"{id}.json")        // rest ReadOnlySpan<PathSegment>
let top = max(a, b, c)                                  // rest ReadOnlySpan<T> where T : IOrder<T>
print(..names)                                          // spread an existing collection
```

- `rest` applies to the last positional parameter only. Its type is written out (`ReadOnlySpan<T>`), so the type inside the body is the type in the signature.
- `items` is **borrowed for the call** and cannot be stored; keeping the values needs an explicit copy (`items.copy()`, which returns a `List<T>` and `uses alloc`; Addendum 1 §3).
- **Zero allocation:** the caller reserves the argument slots in its own stack frame (the count is known at the call site). Each slot is the value itself, or for an interface element type a 16-byte interface value (data pointer plus vtable pointer) that points at the original, so nothing is boxed or copied. String literals point into read-only data. The callee receives a pointer and a length. Nothing is freed on return; the slots are reused by the next statement. C# `params object[]` allocates an array and boxes every struct; C# 13 `params ReadOnlySpan<object>` still boxes.
- **Spread:** `..x` in an argument position passes an existing collection's span without copying when `x` is a collection, and is a range when `x` is an index. The type decides, as in C#'s collection expressions.
- `params` (C# habit) and `T... items` (shorthand) are input forms for `rest`.

## 25. Lambdas, loops and identifiers

```csharp
ts.where(it.isKeyword)                              // shortest; existing
ts.where(t => t.isKeyword)                          // one parameter: no parentheses
ts.where(Token t => t.isKeyword)                    // one typed parameter: type first, no parentheses
xs.sort(by: (a, b) => a.pos < b.pos)                // two or more: parentheses
xs.sort(by: (Token a, Token b) => a.pos < b.pos)
() => now()                                         // none

for t: Token in ts { … }                            // loop bindings follow the local rule
for (a, b) in pairs { … }                           // tuple pattern (J53)
```

- Typed lambda parameters are type-first, like declaration parameters. `it`, `t =>` and `Token t =>` are ellipsis levels of one construct.
- Two or more parameters need parentheses because commas separate arguments.
- A typed single parameter with a generic type (`List<i32> xs => …`) is disambiguated from `<` by lookahead, as C# does.
- **Identifiers start with a letter or `_`;** digits are allowed after the first character (`p95`, `utf8`). Unit literals start with digits (`30s`, `4MiB`, `95%`), so a digit-first identifier would collide with them.

## 26. Interfaces, properties and events

### Interfaces

```csharp
interface IIndexable<T> {
    i64 length                         // property requirement: bare `Type name`
    T get(at Index i)                  // the operator method behind `[]` (§27)
}

interface IIterator<T> {
    bool moveNext(ref this)
    ref readonly T current
}

interface IRow {
    static void checkShape(ref readonly Reader r, Mapping mapping) fails DbError
    static Self fromRow(ref Reader r, Mapping mapping) fails DbError
}
```

- Interfaces have no fields, so a field-shaped member is a **read-only property requirement**, satisfied by a field or a computed property. `Type name { get }` is an input form.
- Interfaces may declare **static members** and use **`Self`** for the implementing type; generic code calls them as `T.fromRow(…)`.
- Interfaces may give **default methods** (bodies), which is how C# extension-method families are written (§27).
- Settable property requirements are not part of round 3; mutation goes through a `ref this` method.

### Struct members

| C# | Bayan |
|---|---|
| Auto-property `{ get; }` | Field |
| Computed property | `Type name => expr` or `Type name { … }` |
| Delegate, `Func<>`, `Action<>` | Function type, usually through an alias (`type Pricer = Line -> Money uses net`) |
| `event` | `Event<T>` field |
| Interface property `{ get; }` | Bare `Type name` |

STYLE member order: fields, the `states` block, events, properties, static factories, methods.

### Events

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
```

1. Handlers run in subscription order, synchronously, on the raising fiber.
2. Handlers cannot fail; they handle or log their own errors.
3. Subscribing during a `raise` takes effect from the next `raise`.
4. `raise` never allocates; `subscribe` allocates one slot.
5. A `Subscription` borrows its event and cannot outlive it. Dropping it unsubscribes, which closes C#'s `+=`-without-`-=` leak at compile time.
6. The handler effect bound is part of the event's type, written through an alias: `type OrderPlaced = Event<Order.placed> uses net, alloc, suspend`. `subscribe` stores the handler, so its parameter takes this bound rather than the polymorphic parameter rule (§13), and `raise` has the bound's effects. No bound means pure handlers.
7. Events are named for what happened (`placed`, `changed`, `low`); there is no `on` prefix (§12).

```csharp
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
let stock = service.placed.subscribe(by: o => inventory.reserve(o.lines))
```

Change notification (`Setting<T>` with `Event<Change<T>> changed`, where `Change<T>` holds `T previous` and `T current`) and threshold alerts (`Stock.low`, raised once when `onHand` crosses `reorderPoint`) follow the same shape. Cross-process pub/sub is not an `Event`: it is a stream iterated on its own fiber, `for try msg in bus.messages(from: "orders.placed") { … }`.

## 27. Standard library shapes

### Lookups

```csharp
let p = prices[sku]                       // traps when missing
let p = prices.tryGet(at: sku) ?? 0g      // derived form: Grams?
```

- `get(at:)` is declared once as the operator method behind `[]`; call sites write `xs[i]`. `tryGet(at:)` is derived and returns `T?`. Lists and maps share the shape.
- `getOr(k, fallback)` becomes `m.tryGet(at: k) ?? fallback`.

### Ranges in collections

```csharp
xs.remove(at: 3)
xs.remove(at: 2..5)                       // overload by type (§24)
xs.remove(at: 2..=5)
xs.remove(from: 2, until: 5)
xs.remove(from: 2, through: 5)
xs.replace(at: 2..5, by: items)
```

`removeRange` and `replaceRange` are removed (renamed members, BYN2230). `fromBounds(start, end)` becomes `fromBounds(start, until: end)`. The particle forms are a deliberate stdlib exception to guard 1 (decided 2026-10-23): two API entries for one operation, kept because `until`/`through` read well at call sites. They are not a precedent for other APIs.

### Math

- Scalars are values: they have nouns and `-ed` copies only (`x.abs`, `x.sqrt`, `x.floor`, `x.ceiling`, `x.rounded()`, `x.truncated()`, `x.clamped(from: lo, through: hi)`, `x.pow(n)`). To change a scalar, reassign it.
- `rounded()` means nearest, ties away from zero; `rounded(ties: .even)` and `rounded(digits: 2)` are the options. `floor`, `ceiling` and `truncated` name a direction.
- Aggregates have verb and copy pairs: `v.normalize()` / `v.normalized()`, `v.scale(by: 2.0)` / `v.scaled(by: 2.0)`, `xs.sort()` / `xs.sorted()`.
- Collection queries: `sum`, `product`, `average`, `min`, `max` trap on an empty collection; `tryAverage`, `tryMin`, `tryMax` return `T?`; `min(by: key)`, `max(by: key)`; `percentile(at: 95%)`.
- Free functions: `min(rest …)`, `max(rest …)`. `min(..xs)` is a paraphrase of `xs.min`.
- **Imports:** members of the numeric and collection types live in `core` and need no import; `min` and `max` are in the `core` prelude. Vectors, matrices, statistics and geo live in `std.math` and need an import.

### `std.db` (spec-only)

The database layer collapses the C# extension-method explosion (Jaunty: 684 `IDbConnection` extensions from sync/async × parameters × options × eight result shapes × mapping × arity × stored procedures):

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
    Command prepare(ref this, Sql sql, Millis timeout) fails DbError uses db, suspend
    i64 bulkCopy<T>(ref this, ReadOnlySpan<T> rows, bool ignoreConstraints)
        where T : IEntity fails DbError uses db, suspend

    Rows<T> query<T>(ref this, Sql sql, rest ReadOnlySpan<ISqlValue> args,
                     Millis timeout = 30s, Mapping mapping = .strict)
        where T : IRow fails DbError uses db, suspend { … }
    i64 execute(ref this, Sql sql, rest ReadOnlySpan<ISqlValue> args, Millis timeout = 30s)
        fails DbError uses db, suspend { … }
    Rows<T> call<T>(ref this, Procedure proc, rest ReadOnlySpan<ISqlValue> args, Millis timeout = 30s)
        where T : IRow fails DbError uses db, suspend { … }
    T get<T>(ref this, T.Id id) where T : IEntity fails DbError uses db, suspend { … }
    T? tryGet<T>(ref this, T.Id id) where T : IEntity fails DbError uses db, suspend { … }
    T.Id insert<T>(ref this, T row) where T : IEntity fails DbError uses db, suspend { … }
    i64 insert<T>(ref this, ReadOnlySpan<T> rows, bool ignoreConstraints = false)
        where T : IEntity fails DbError uses db, suspend { … }
    i64 update<T>(ref this, T row) where T : IEntity fails DbError uses db, suspend { … }
    i64 delete<T>(ref this, T row) where T : IEntity fails DbError uses db, suspend { … }
    i64 delete<T>(ref this, T.Id id) where T : IEntity fails DbError uses db, suspend { … }
}

public struct unique Rows<T> where T : IRow {
    public bool moveNext(ref this) fails DbError uses db, suspend       // 006 iterator protocol
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

## 28. Primitive spellings (documented synonym exception 2)

C# primitive names are accepted alongside Bayan's, per project:

| C# | Bayan |
|---|---|
| `sbyte` / `byte` | `i8` / `u8` |
| `short` / `ushort` | `i16` / `u16` |
| `int` / `uint` | `i32` / `u32` |
| `long` / `ulong` | `i64` / `u64` |
| `nint` / `nuint` | `isize` / `usize` |
| `float` / `double` | `f32` / `f64` |

```csharp
style: Style {
    booleans: .symbols,      // or .words
    primitives: .sized,      // or .csharp
}
```

- `.sized` is the default. `bayan format` normalizes each project to its spelling; the compiler accepts both and raises no diagnostic (style is never compiler-enforced, as for booleans).
- **Excluded:** `char` (16-bit in C#; it stays the input form for `rune`), `decimal` and `object` (no equivalent). `bool` and `string` are shared.
- The C# names are reserved words in every project, whatever its style.
- Tooling renders types in the reader's project style: diagnostics, hover, `bayan expand`, generated docs and `bayan explain`.
- The standard library, the spec's examples and the selfhost compiler are written in `.sized`.
- Conversions read the same in both styles: `int(x)`, `int.tryFrom(x)`.

## 29. CLI: `bayan format`

The formatter command is `bayan format` (and `bayan format --check`), not `fmt`: every other command is a full word (`build`, `run`, `test`, `explain`, `audit`, `features`, `expand`), and GRAMMAR's no-abbreviation rule covers the tools. `bayan fmt` is an unknown command whose message names `bayan format`.

---

## Open questions (resolve before implementing these parts)

**Resolved 2026-10-22 (user accepted every recommendation):** O1 newline-only unions, `bayan format` rewrites the one-line form. O2 counts (`length`, `runeCount`) are plain `i64`; units apply only to slice offsets. O3 field vs struct rule by content. O4 `at`, not `on`. O5 `->` is the function-type arrow (declarations return type-first); `=>` only for "evaluates to". O6 particle `for` allowed (label position only). O7 mixed-currency `+` traps; `tryAdd` and `add(…) fails` exist. O8 superseded by the `states` block (§14 rule 13). O9 bare literals for distinct types rejected (`ParcelId(0)`). O10 the trusted-field rule (§17).

**Open (recommendations given; confirm before implementing):**

- **O11. Writing public fields from outside the struct.** Recommendation: a public field is readable anywhere and writable only from the struct's own `ref this` methods or through `with`. This matches `{ get; private set; }`, the common C# shape, and keeps `requires` and typestate invariants in one place.
- **O12. Repeated particles in one signature** (`wire(by Mailer m, by Inventory i)`). Recommendation: a particle appears at most once per signature; group the values in a struct or pick another particle. A repeated label makes `f(by:by:)` order-dependent, which particles exist to avoid.

**Sūq review decisions, 2026-10-22** (`docs/suq-agent-brief.md` Part A; record in `docs/suq-round3-review-response.md`):
- **A1.** A `defer` modifier is evaluated once at registration and its value captured (§19); `defer { if c { … } }` is the exit-time form.
- **A2.** `fail .x` leaves with an error; `return` always returns a value. `fail .x if cond` and `?? fail .x` follow. `return .x` in a function that fails is an input form for `fail .x`, only when the return type has no case `.x`.
- **A3.** Union payloads bind by position (`.circle(let r)`) or as the whole value: `.percentOff p => p.rate`, with `p` narrowed to that case.
- **A4.** Numeric conversions are `T(x)`, checked (trap when out of range); `T.tryFrom(x)` returns `T?`. `(T)x` is an input form.
- **A5.** `s.copy()` and `s.copy(to: arena)` copy a borrowed string.
- **A6.** `assert f() fails E.case` and `assert f() fails E`, with power-assert output. This is a statement form, not the deferred fluent assert API.
- **A7.** Adopted as implemented: a struct with any `unique` field is `unique` (explicit `struct unique` stays for raw resources); owning values never copy implicitly; fallible constructors are `T(x)`, `T.tryFrom(x)`, `T.tryParse(text)`; the empty map literal is `[]`.

**Payload names (signature review):** union and state payloads always name their fields in the declaration (`percentOff(Percent rate)`), so whole-value binding (A3) can read them (`p.rate`). Construction may omit the label when there is exactly one payload (`.percentOff(10%)`); with several, labels are required (`.buyXGetY(buy: 2, free: 1)`).

## Deferred (do not implement)

- String `+` and fluent asserts (kept for future consideration).
- General effect variables in function types and interfaces (option B). Effect bounds written in a type (`Event<T> uses …`) are not deferred (§13).
- `format(into: ref buf)` as a particle form of `xInto`.
- Collection-value-tied indices (`orders.Index`).
- Growing the particle set further (`between`, `per`, `within`) — only by spec revision, when Sūq shows a need.
- User-defined narrowing predicates (TypeScript-style `x is T` return types).
- Named `sql"…"` placeholders (`$minAge` bound from a local), with the interpolation work.
- Settable property requirements in interfaces.
- `QueryMultiple`-style batches in `std.db` (several cursors from one command).

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
- Parenthesized refinement types (`(Order is .shipped) order`): too much ceremony; `Order.shipped order` replaces them.
- Value-dependent return types (`public order? label(…)`).
- File-level or struct-level `uses`.
- Identifiers that start with a digit.
- The `on` prefix for events and handlers (`onChange`).
- Bracket calls (`xs.removeRange[2..5]`).
- `=to` / `to` for inclusive and exclusive ends (use `through` / `until`).
- `:n` SQL placeholders (`$n` is Postgres's native form; `:` clashes with `::` casts).
- A method named after a particle (`at(k)`).

## Deliverables

1. `SPEC.md` updated for §1–20 and §24–29, each change with a one-line rationale and its diagnostic codes.
2. `GRAMMAR.md` and `STYLE.md` updated (§1, §5, §12, §13, §15, §28). In STYLE.md's member order, rename "state members" to "properties" and use the order in §26 (fields, `states` block, events, properties, static factories, methods). STYLE: `uses` after `fails`, the clause order of §2, and the `style.primitives` default.
3. Sweep every code example in SPEC.md, GRAMMAR.md, STYLE.md and `docs/` to round-3 syntax: name-first local bindings, type-first declarations; no semicolons; `@` attributes; particles; `rune`; head-first declaration keywords; `states` blocks and `T.s` types; `rest`; `bayan format`.
4. Verify collections use `length` for size and `count(p)` only for predicate counts (round 2 §11 lists `count` among the queries).
5. Compiler changes, in this order:
   1. Lexer and parser: semicolon-free termination (§3), `@` attributes and `because` (§4), head-first declaration keywords, name-first local bindings and type-first declarations with their input forms (§2), receivers including `move this` (§2), `//`-only comments, `except` (§7), `states`/`becomes`/`become` and `T.s` types (§14), particles including `until`/`through` (§15), `provide` (§13), `rest` and named parameters after it (§24), lambda and loop bindings (§25), `expects` (§11).
   2. Input-form rejection with fix-its (§20), and `bayan format` rewrites including `style.booleans` and `style.primitives`.
   3. Semantics: unused-value error, `requires` at construction and `with` re-checking, `expects` (§11), typestate (§14), effects, aliases and capabilities (§13), overload checks (§24), unit conversion and literal rules (§8), typed indices (§9), trust layers (§17), `try` scope (§18), `defer` modifier capture (§19).
   4. Standard library: `rune`, `log`, `print` with `console`, particle names, `tryGet`, `remove`/`replace`, math names (§27), sink types, `T.rules`.
   5. `bayan format` (renamed), `bayan audit` and `bayan features --used`.
6. Tests for each item: positive, negative, and diagnostic snapshots.
7. Sūq: coverage doc, friction log, compile-fail corpus skeleton, and the paper versions of the first three parts (§21).
8. An end-of-run summary: what changed, what was not done and why, spec conflicts that needed a judgment call, and the top friction-log items.

---

## Changelog

- **2026-10-22.** Declarations revert to type-first (audit flaw #8, "names first", was meant only for local bindings; plan J54). O1–O10 resolved. Sūq review A1–A7 adopted.
- **2026-10-23 (signature review).**
  - Signature shape and clause order (`where` → `becomes` → `fails` → `uses`); particle before passing mode; function types parenthesized before a name; waṣl list extended (§2, §3).
  - Receiver forms gain `move this`; bare `this` rejected; the receiver is not the object for particles (§2, §15).
  - State types `Order.paid`, inline `is` for several states, aliases; `states { … }` block replaces per-line `state` (supersedes O8); the `Order is .shipped order` form is gone because it collided with whole-value binding (§14).
  - Particles: `until` and `through` added; optional parameters may take particles; `@positional`; `rest` tails exempt; particle words are not member names (§15, §12).
  - Effects: `process` and `console` added; `net` defined as any socket; effect aliases; function-typed parameters polymorphic unless stored, stored types bounded; effect bounds in types for events; `main`, scripts and tests infer; test runner captures console (§13).
  - `expects` for preconditions beside `requires` types; validation errors as a composable `union error`; `Rule` payloads named; `at .password` in the example (§11).
  - Units: coarser conversions are `floor`/`ceiling`/`rounded` with a type argument; `round(to:)`, `roundUp`, `roundDown` and `convert(to:)` removed (§8).
  - New sections: overloading, defaults and `rest` (§24); lambdas, loops, identifiers (§25); interfaces, properties, events (§26); standard library shapes, including `std.db` (§27); primitive spellings (§28); `bayan format` (§29).
  - Example fixes: `transfer` gains its receiver, `insert` gains `uses alloc`, `crc` takes `ReadOnlySpan<u8>`, `Date(year:month:day:)`, `FakeNet(label)`, the `Checkout` callback example replaced by a `Pricer` alias.
