# Bayan grammar guide

How to name things the way the standard library does. This is the developer-facing companion to [SPEC §5](bayan-spec.md) (naming and morphology). SPEC §5.2 holds the full pattern table and contracts; this guide tells you which pattern to pick.

Grammar rules are not style (see [STYLE](bayan-style.md)): they cannot be configured away. Compiler errors enforce pattern contracts; lints enforce word-class heuristics and never block builds (`BYN5xxx`, SPEC §5.2; §13 below).

Casing (SPEC §5.1): `PascalCase` types, `camelCase` everything else, `I` prefix on interfaces. Enforced by [BYN2008](diagnostics/BYN2008.md) (types) and [BYN2009](diagnostics/BYN2009.md) (everything else).

## 1. Naming decision tree

For any new member, take the first question that answers yes.

1. **Acts on `this`?** Plain verb on `ref this`: `void sort(ref this)`. The `-ed` form (`sorted`) names a changed copy or a view and never takes `ref this` (item 2).
   - A body that modifies `this` without `ref this`: [BYN2115](diagnostics/BYN2115.md).
   - Calling a `ref this` member on a `let`: [BYN2104](diagnostics/BYN2104.md) (fix: `var`).
   - `-ed` member that takes `ref this` (`void sorted(ref this)`): [BYN5001](diagnostics/BYN5001.md).
   - A noun on a `ref this` member: [BYN5101](diagnostics/BYN5101.md) (lint).
2. **Returns a changed copy or a view of `this`?** `-ed` form: `trimmed`, `sorted`, `lowercased`. The `-ed` form does not require a mutating verb to exist: an immutable type (`string`) has `trimmed()` and `lowercased()` with no `trim` or `lowercase`. A mutable type with both forms keeps both (`xs.sort()` / `xs.sorted()`). A result that is not a changed copy of the receiver is a noun or a plain static verb (`join`, `split`).
3. **Boolean property, no arguments?** `is` / `has` / `can`: `isEmpty`, `hasItems`, `canRetry`. Never a `?` suffix.
   - A boolean property that modifies `this`: [BYN5002](diagnostics/BYN5002.md).
   - A `bool` property without the prefix: [BYN5102](diagnostics/BYN5102.md) (lint, fix-it).
4. **Can fail, and callers may want `bool` or `T?`?** Write `x() fails E`; `tryX` is derived. A hand-written `tryX` must return `T?` or `bool`: [BYN5003](diagnostics/BYN5003.md).
5. **Writes into the caller's buffer?** `xInto(ref dst)` taking `ref StrBuf` or `Span<T>`; the allocating `x()` is derived (`formatInto` → `format`). No dedicated code yet.
6. **The act as a value?** `xOp` (derived; type `Op<T, E>`). Do not write it. No dedicated code yet.
7. **A thing that performs the act?** Noun type: `Parser`, `Builder` (stateful doer), `Hasher`, `Comparer` (stateless instrument). No dedicated code yet.

Composition order: `[try] verb [Into] [Op]`: `tryFormatInto`, `parseOp`. Other reserved patterns (`ensureX`, `xWith`, `xUnchecked`, noun patterns such as `T.Patch`) are in SPEC §5.2.

Further rules that sit beside the tree:
- **Nouns with no arguments are properties** (`xs.span`, `order.total`), not calls. The exception is a noun member that needs `ref this`: it is a call (`xs.mutableSpan()`), because mutable access must be visible at the call site.
- **No instance `toX` or `asX` members.** `to` and `as` are particles, and such a member spells a conversion that already has a form: a view is a noun (`xs.span`), an owned copy is `xs.copy()`, a conversion is `T(x)` (`List(seq)`, `string(x)`). Static `fromX` factories stay (`T.fromJson(…)`, `fromBounds(start, until:)`): a factory names the source a `T` is built from. Enforced by [BYN5009](diagnostics/BYN5009.md).
- **A stored function field is a role noun** (`handler`, `pricer`, `formatter`), never `onX`. Events are past participles or adjectives (`placed`, `low`); there is no `on` prefix. Particle words (`at`, `by`, `from`) are not member names (`tryGet(at:)`, not `at(…)`). Enforced by [BYN5009](diagnostics/BYN5009.md) and [BYN5010](diagnostics/BYN5010.md).
- **A static factory that names its result is a noun or adjective** (`Money.zero(c)`, `Outbox.empty()`), not a verb: it names the value built, not an act on `this`. No checker rejects a verb here yet; [BYN5101](diagnostics/BYN5101.md) looks only at `ref this` members.
- **`Untrusted<T>` is a status,** the same value with a different standing (`X<T>`, §10), not a different shape.
- **Math vocabulary is exempt** from the `-ed` rule (`abs`, `sqrt`, `floor`, `ceiling`, `pow`, `rounded`): scalars have nouns and `-ed` copies only. Tool names are full words too (`bayan format`, not `fmt`).
- **Checker scope.** The pattern rules that make derived forms work (`tryX`, `-ed`, `xInto`, particles; `BYN50xx`) apply to all code. The naming-style rules (`BYN51xx`) apply to the public API only. A name forced by an outside system (a JSON field, a C symbol, a database column) uses the derivation's rename attribute (`@json(name: …)`, `@column(…)`), which exempts it from `BYN51xx`, or `@allow(…) because "…"` (§7).
- **What names stand for (revision 0003).** On an instance member, a noun or an adjective names a result (`s.length`, `s.position("x")`, `s.isEmpty`); a verb names an act on the receiver and takes `ref this` (`buf.append(x)`); `-ed` names a changed copy (`s.trimmed()`).
- **Assertion verbs are queries:** third-person verbs that state a fact about the receiver and return `bool`. The group is closed: `contains`, `startsWith`, `endsWith`, `matches`, `equals`. Adding one is a GRAMMAR change.
- **Static and free functions** have no receiver, so they name acts and are verbs: `print(x)`, `copy(from:to:)`, `Utf8.decode(b)`, `Hex.encode(d)`.
- **Fallible pairs:** a base function `fails` with detail (`T.parse(text)`, `Utf8.decode(b)`), its derived `tryX` returns `T?` (`T.tryParse`, `Utf8.tryDecode`), and the keyword `try` marks every call to the base form. Both forms always exist.

Collections follow the tree: `append`, `sort`, `reverse` on `ref this`; `appended`, `sorted`, `reversed` derived (a view if possible, otherwise a copy). Ordered collections `append`/`prepend`; unordered ones `add`. Size is `length`; `count(p)` only counts the elements matching `p`.

## 2. Statement modifiers: the ḥāl rule

A statement modifier is a head verb followed by a participle that describes the circumstance: `defer failing`, `defer panicking`. This is the statement case of head-first word order (§5).

```csharp
defer arena.reset()                           // every exit
defer failing tx.rollback()                   // exit by failure
defer failing tx.rollback() unless dryRun     // `dryRun` is read when the defer is registered
defer panicking (p) log($"{p}", as: .error)   // panic in progress
```

`failing` and `panicking` are contextual keywords, valid only after `defer`. Future modifiers must follow the same shape: head verb, then participle. A postfix `if` or `unless` on a `defer` is evaluated once, when the `defer` is registered, and its value is captured, as for any postfix modifier (§4). To decide at exit, test inside the body: `defer { if c { … } }`.

Enforced by:
- `errdefer` is an input form for `defer failing`: [BYN1020](diagnostics/BYN1020.md).
- `defer failing` in a function that cannot fail: [BYN2606](diagnostics/BYN2606.md).
- `try` in a `defer` body: [BYN2601](diagnostics/BYN2601.md).
- Panic handler restrictions: [BYN2612](diagnostics/BYN2612.md) (statement not allowed), [BYN2613](diagnostics/BYN2613.md) (reads only `let` bindings and parameters), [BYN2614](diagnostics/BYN2614.md) (`Panic.handler` placement). The process-wide hook is a role noun, `Panic.handler`, of type `Panic -> void uses console`; it replaces `Panic.onPanic` ([BYN2230](diagnostics/BYN2230.md), [BYN5010](diagnostics/BYN5010.md)).

The shape itself (head verb, then participle) has no checker code; new modifiers are reviewed against it.

## 3. One spelling per construct; free composition

Bayan follows its own principles. C# familiarity is a benefit, never a deciding reason.

| Category | What it is | Rule |
|---|---|---|
| Synonym | Two spellings of the same construct | Rejected: one is canonical, the other is an input form with a fix-it |
| Paraphrase | Different compositions of the general grammar that mean the same thing: `users except null` / `users.where(it is not null)` | Allowed |
| Ellipsis (ḥadhf) | Omitting a part recoverable from local context: `it`, inferred types, target-typed `.member` | Allowed |

Guards against drift:
1. No synonyms in the lexicon. Two documented exceptions, both chosen per project and normalized by `bayan format`: the boolean spellings (§4) and the primitive spellings (`i32` or `int`; `style.primitives`). The standard library has one more deliberate pair, `remove(at: 2..5)` and `remove(from: 2, until: 5)`, kept because `until` and `through` read well at call sites; it is not a precedent.
2. New syntax must have its own job; it may not only re-spell an existing construct.
3. Paraphrases must be truly equivalent: same narrowing, cost and effects. Where two forms are not equivalent, the difference is a style note with no diagnostic code (`0..=n - 1` vs `0..n`, `p == Point { … }` vs `p is Point { … }`).

Familiar alternatives are **input forms**: the editor and `bayan format` accept them and rewrite them one way into canonical syntax; the compiler rejects them in source with a machine-applicable fix.

| Input form | Canonical | Code |
|---|---|---|
| `char` | `rune` | [BYN1048](diagnostics/BYN1048.md) |
| Primitives in the non-project spelling (`int` under `.sized`, `i32` under `.csharp`) | The project's `style.primitives` spelling (SPEC §3.1); `char`, `decimal` and `object` are never accepted as primitives | none (`bayan format` only) |
| `User!EmailError f()`, `User!(A, B) f()` | `User f() fails EmailError`, `fails A, B` | [BYN1031](diagnostics/BYN1031.md) |
| `Grams w = 0g` (local binding in a function body) | `let w: Grams = 0g` | [BYN1043](diagnostics/BYN1043.md) |
| `f() -> T`, `f(x: T)`, field `x: T`, payload `case(x: T)` | `T f()`, `f(T x)`, `T x`, `case(T x)` (§5) | [BYN1043](diagnostics/BYN1043.md) |
| `unique struct`, `distinct type`, `error union`, `error enum` | `struct unique`, `type distinct`, `union error` | [BYN1044](diagnostics/BYN1044.md) |
| Trailing `;`, `a; b` on one line | Newlines (§6) | [BYN1040](diagnostics/BYN1040.md) |
| `[Derive(Json)]` | `@derive(Json)` (§7) | [BYN1045](diagnostics/BYN1045.md) |
| `/* … */` | `//` lines | [BYN1047](diagnostics/BYN1047.md) |
| `for (let x in xs)`, `foreach (var x in xs)` | `for x in xs` | [BYN1019](diagnostics/BYN1019.md) |
| `if (x) {`, `while (x) {`, `switch (x) {` | no parentheses (whole-condition parentheses kept when a brace is present) | [BYN1017](diagnostics/BYN1017.md) |
| `x == null`, `x != null` | `x is null`, `x is not null` | [BYN2027](diagnostics/BYN2027.md) |
| Booleans in the non-project spelling | The project's `style.booleans` spelling (§4) | none (`bayan format` only) |
| `requires value…` | `requires it…` | [BYN1050](diagnostics/BYN1050.md) |
| `case _ except .x` | `case not .x` | [BYN1051](diagnostics/BYN1051.md) |
| `noalloc`, `nosuspend` | Removed: no `uses` clause already means pure (§9) | [BYN6001](diagnostics/BYN6001.md) |
| `{ cents = 250 }` | `{ cents: 250 }` | [BYN1021](diagnostics/BYN1021.md); in `package.byn` [BYN3104](diagnostics/BYN3104.md) |
| `"Hello " + name` (strings) | `$"Hello {name}"` | [BYN2209](diagnostics/BYN2209.md) |
| `return .x` in a function that fails, when the return type has no case `.x` | `fail .x` | [BYN1056](diagnostics/BYN1056.md) |
| `(u8)x`, `(T)x` | `u8(x)`, `T(x)` | [BYN1057](diagnostics/BYN1057.md) |
| `requires …` on a function | `expects …` | [BYN1063](diagnostics/BYN1063.md) |
| `trust(x, reason: "…")` | `x.trustUnchecked() because "…"` | [BYN1062](diagnostics/BYN1062.md) |
| `context (…) { }` | `provide … { }` | [BYN6006](diagnostics/BYN6006.md) |
| `params T[] items`, `T... items` | `rest ReadOnlySpan<T> items` | [BYN1058](diagnostics/BYN1058.md) |
| `state x` member lines | One `states { … }` block | [BYN1059](diagnostics/BYN1059.md) |
| `Order order is .paid` | `Order.paid order` | [BYN7006](diagnostics/BYN7006.md) |
| Interface property `Type name { get }` | `Type name` | [BYN1060](diagnostics/BYN1060.md) |
| `uses` before `fails`, or any clause out of order | `where`, `expects`, `becomes`, `fails`, `uses` | [BYN1065](diagnostics/BYN1065.md) |

Also rejected with a fix-it (design round 2 plan §15):

| Old form | Canonical | Code |
|---|---|---|
| `errdefer` | `defer failing` | [BYN1020](diagnostics/BYN1020.md) |
| `catch (e) switch { … }` | `catch switch { E e => … }` | [BYN1025](diagnostics/BYN1025.md) |
| `error set S = A \| B` | `union error S = A, B` | [BYN1029](diagnostics/BYN1029.md) |
| `add`, `asString` | `append`, `finish` | [BYN2230](diagnostics/BYN2230.md) |
| `toString()` | `format()`; a conversion is `string(x)` | [BYN2031](diagnostics/BYN2031.md) |

A body that is not a block: [BYN1018](diagnostics/BYN1018.md). Display-only views (inferred types, expanded derived members) are language-server inlay hints, never a stored form.

## 4. Conditionals

| Form | Use when | Codes |
|---|---|---|
| `if c { … }` | The general case. No parentheses, braces mandatory. `if !x { … }` and `unless x { … }` are both allowed paraphrases. `else` follows `}` on the same line (§6). | [BYN1017](diagnostics/BYN1017.md), [BYN1018](diagnostics/BYN1018.md) |
| `unless c { … }` | A guard on a simple positive condition with no `else`. Not with `else`, not with `!` or `not`, not with a top-level `&&`/`\|\|` (`and`/`or`), not with `is null`, no likelihood word. | [BYN1006](diagnostics/BYN1006.md), [BYN1007](diagnostics/BYN1007.md), [BYN1022](diagnostics/BYN1022.md), [BYN1023](diagnostics/BYN1023.md), [BYN1033](diagnostics/BYN1033.md) |
| `if unlikely c { … }` | A cold path that is not already an error return. Layout hint only; semantics unchanged. | [BYN2233](diagnostics/BYN2233.md) (lint: redundant on error returns) |
| `if never c { … }` | A state that would be a bug. Panics if true in every build mode. For a caller's obligation on entry, use `expects` instead. | No dedicated code (runtime panic) |
| `stmt if c`, `stmt unless c` | A one-line guard: `return`, `fail`, `break`, `continue` or one call, on the same line as the guard. `stmt unless not c` is rejected with a fix-it to `stmt if c`. | [BYN1008](diagnostics/BYN1008.md) (simple statements only), [BYN1009](diagnostics/BYN1009.md) (no chaining), [BYN1007](diagnostics/BYN1007.md) (negated `unless`) |

```csharp
flush() if unlikely buf.isFull
return .badRequest("Cart is empty") unless cart.hasItems
let order = db.find(id) ?? return .notFound
fail .outOfStock if line.qty > stock
```

`return` gives the function's value; `fail .x` leaves with an error from its `fails` clause. `return .x` in a function that fails, when the return type has no case `.x`, is an input form for `fail .x` (§3).

Null checks are `is null` / `is not null` ([BYN2027](diagnostics/BYN2027.md)); write `if x is not null`, not `unless x is null`. Bind a call's non-null result with `?? return …`, `?? fail .x`, `?? break` or `?? continue`.

**Booleans** have two allowed spellings, the one documented synonym exception (§3): `!` `&&` `||` and `not` `and` `or`. `bayan format` normalizes each project to the spelling set in `style.booleans` ([STYLE](bayan-style.md)). **Patterns are words-only**: `is not null`, `.removed or .backordered`, `not .draft`.

Pick the tool by what a failed check means: non-null types (no check), `requires` on a type (user input, checked once at construction), `expects` (a bug in the caller; it traps and never reads an `Untrusted` value, [BYN8006](diagnostics/BYN8006.md)), distinct types (a rule true everywhere), `Untrusted<T>` (input from outside; constructing a type with `requires` removes the taint), `fail .x unless …` (a business outcome).

## 5. Declarations: head-first word order

Arabic puts the head first and its modifiers after. Bayan follows the same order:

| Construct | Head first | Example |
|---|---|---|
| Statement | The verb, then ḥāl modifiers (§2) | `defer failing` |
| Declaration | The kind, then modifiers | `struct unique File`, `type distinct Sku = string`, `union error ShipError` |
| Local binding | `let` or `var`, then the name, then the type | `let w: Grams = 0g` |

The declarations themselves (fields, parameters, return types, payloads) keep the type first, as in C#:

```csharp
struct Item {
    Sku sku
    Grams weight
}

public Grams shippingWeight(Order order) {
    let w = 0g              // immutable, inferred
    let w: Grams = 0g       // immutable, explicit type
    var w: Grams = 0g       // mutable, explicit type
    …
}
public bool isEmpty => lines.isEmpty
test "bounds" (i32 input, bool valid) { … }
```

- Fields, parameters, return types and union and state payloads put the type first (`timeout(Millis after)`); `void` marks a function that returns nothing. Visibility (`public`, `static`) leads the whole declaration. A particle leads its parameter like `ref` does: `by Carrier carrier`.
- Local bindings put the name first: `let`/`var` already lead, and the name is what the reader scans for.
- The name-first declaration forms are input forms that `bayan format` rewrites ([BYN1043](diagnostics/BYN1043.md)): `f() -> T` → `T f()`, `f(x: T)` → `f(T x)`, field `x: T` → `T x`, payload `case(x: T)` → `case(T x)`. In a function body, `Grams w = 0g` → `let w: Grams = 0g`; in a struct body the same text is a field with a default.
- A function type used as a field or parameter type is parenthesized so the name follows the closing paren: `(Line -> Money uses net) pricer`, `(Order -> bool) pred`; a single parameter type needs no inner parentheses. Without the parentheses the name would read as part of the return type or `uses` list.
- Revised 2026-10-22: round 3 first put names first in every declaration; that was meant only for local bindings, so declarations returned to type-first.
- `union error` also composes and narrows error sets; composed sets are flattened, and two components sharing a case name is an error:
  ```csharp
  union error ShipError = CarrierError, StockError
  union error Retryable = CarrierError except .rejected
  ```
- `unique` is the move-only modifier, for resources (`File`, `Socket`, `Transaction`, `Lock`). Plain data types are never `unique`, and a `unique` type cannot be `const`.
- Named patterns are `const` values of pattern type, named in camelCase: `const inactive = pattern Line { status: .removed or .backordered }`. `pattern` is a contextual keyword, valid only after `=` in a `const` declaration.

The receiver is named `this`; its type is the enclosing struct and is elided:

| Receiver | Meaning |
|---|---|
| (not written) | Read-only, any state |
| `this is .shipped` | Read-only, only in that state (§10) |
| `this is .placed or .paid` | Read-only, in either state |
| `ref this` | Mutating, any state (Form V, tree 1) |
| `ref this is .placed` | Mutating, only in that state |
| `move this is .draft` | Consuming, only in that state |

A bare `this` with no state is rejected ([BYN1061](diagnostics/BYN1061.md)).

## 6. Statement termination: waqf rules

There are no semicolons. Statements end at newlines, following the stop marks (waqf) of Qurʾanic recitation:

| Waqf mark | Rule |
|---|---|
| مـ lāzim (mandatory stop) | A newline after a complete statement ends it |
| لا lā (forbidden stop) | The statement continues if the line ends with a binary operator, `,`, `=`, `=>` or `{`, or the break is inside `( )` or `[ ]` |
| وصل waṣl (joining) | The next line continues the statement if it starts with `.`, `?.`, `??`, `except`, `requires`, `expects`, `where`, `becomes`, `fails`, `uses`, `catch`, `&&`, `\|\|`, `and` or `or` |
| Never-join | A line starting with `(`, `[` or `-` always begins a new statement |

```csharp
let total = lines
    .where(it.isActive)
    .map(it.price)
let label = carrier.name ??
    "unknown"
```

- An unused non-void value is a compile error, so a mis-split line (`let x = a`, newline, `-b`) is always a diagnostic.
- `return`, `break` and `continue` take their operand on the same line. A postfix `if` or `unless` is on the same line as its statement.
- `else` follows `}` on the same line; `else` on a new line is rejected with a fix-it.
- Comment lines are transparent to continuation. Outside `( )` and `[ ]`, a blank line always ends a statement; inside them, blank lines are ignored.
- A struct literal in a control-flow head takes whole-condition parentheses: `if (p is Point { x: 0, y: 0 }) { … }`.
- Declarations (fields, union cases, states) are separated by newlines, so a union has one case per line; `bayan format` rewrites the one-line form. Entries in literals and argument lists are separated by commas.

Enforced by [BYN1040](diagnostics/BYN1040.md) (`;`), [BYN1041](diagnostics/BYN1041.md) (`else` on a new line), [BYN1042](diagnostics/BYN1042.md) (operand or postfix modifier on the next line), [BYN1053](diagnostics/BYN1053.md) (one-line declarations) and [BYN3201](diagnostics/BYN3201.md) (unused value).

## 7. Attributes and purpose clauses

Attributes use `@`: `@derive(Json, Row)`, `@allow(BYN5102)`, `@cases(discountCases)`, `@sealed`, `@positional` (revision 0008). Attribute names are camelCase; derivation names stay PascalCase (`Json`, `Row`). `[Attr]` is an input form, since a line starting with `[` is a collection literal (§6).

A **purpose clause** (Arabic mafʿūl li-ajlihi, the object of purpose) states why code bypasses a safety rule. `because "…"` is required on every bypass, and missing it is a compile error:

```csharp
@allow(BYN5102) because "the wire protocol names this flag `active`"
@json(expose) because "the payment provider needs the token"   // on a `Secret` field
let raw = input.trustUnchecked() because "signed by our own gateway"
unsafe because "libsodium writes into the buffer" { … }
```

Every `xUnchecked()` takes one. Ordinary attributes (`@derive`) take none. A bare string as the reason is not allowed: the word `because` is part of the form. `bayan audit` lists every bypass in a project with its reason. Enforced by [BYN1045](diagnostics/BYN1045.md) (`[Attr]`) and [BYN8001](diagnostics/BYN8001.md) (missing `because`).

**Project dictionary.** The checker classifies each word of a member name as a verb, noun or adjective to apply the rules in §1. A word it does not know (a domain term like `upsert`, `sku`, `kyc`) is declared once in `package.byn`, not suppressed member by member; `forms` gives irregular derived spellings:

```csharp
grammar: Grammar {
    verbs: ["upsert", "reindex"],
    nouns: ["sku", "kyc"],
    forms: ["upsert": "upserted"],
}
```

The dictionary is merged with the built-in word list. It adds words; it cannot switch a rule off. An unknown word is a warning ([BYN5014](diagnostics/BYN5014.md)) whose fix adds it to `verbs` when the member takes `ref this`, otherwise to `nouns`.

## 8. Argument particles

A role in a call is marked by a particle from a closed set (Arabic مِن/إلى/بِ):

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

- A function has at most one unlabeled parameter: the object. The receiver does not count. Every other required parameter takes a particle, and call sites write it. Optional settings are named parameters with defaults; one may take a particle that fits its role (`log(m, as: .debug)`, `FakeClock(at: …)`), otherwise it uses its name (`void run(Path program, List<Arg> args = [])`, `timeout: 5s`).
- **Exempt:** a `rest` tail (`print(rest ReadOnlySpan<Display> items)`); the generated constructor of a struct marked `@positional` (every field in declaration order, revision 0008), whose components are the same kind and universally ordered (`Point(3, 4)`, not `Date`, which keeps `Date(year:month:day:)`); `extern` declarations; operator declarations; union and state payloads; laws.
- **A particle appears at most once per signature.** Group the values in a struct or choose another particle ([BYN5013](diagnostics/BYN5013.md)).
- Particle words are not member names (§1).
- The particles are part of the function's name: `copy(from:to:)`.
- `for` is allowed as a particle; it only appears in label position (`for:`), where it cannot be the loop.
- The set is closed for user code and grows only through spec revisions. Pick the particle by role: `replace(at: r, by: text)` (replacement), `log(msg, as: .debug)` (role). `until` and `through` name the two ends of a range. `xInto(ref dst)` keeps its own form (tree 5).

Enforced by [BYN5004](diagnostics/BYN5004.md) (more than one unlabeled parameter), [BYN5005](diagnostics/BYN5005.md) (call site without the particle), [BYN5006](diagnostics/BYN5006.md) (required parameter without a particle), [BYN5008](diagnostics/BYN5008.md) (not a particle) and [BYN5013](diagnostics/BYN5013.md) (particle repeated).

## 9. Effects

A function's effects are a `uses` clause from a closed list: `alloc`, `suspend`, `net`, `fs`, `clock`, `random`, `env`, `process`, `console`. No `uses` clause means pure: no allocation, no suspension, no I/O. An alias names a set without adding a capability: `effect db = net, fs`.

```csharp
public ParcelId book(Parcel parcel, by Carrier carrier)
    fails CarrierError uses net, alloc, suspend { … }

public u32 crc(ReadOnlySpan<u8> data) { … }   // pure

interface IPricer {
    Money price(Order o) uses net, alloc
}
type Pricer = Line -> Money uses net
type OrderPlaced = Event<Order.placed> uses net, alloc, suspend
```

- Public functions declare their effects; private functions infer them, as with error sets. Tests, `main` and scripts infer too.
- `fails` is not an effect because it carries a type; effects are names from a closed list.
- `panic` is not an effect.
- `->` marks the return in a function type; a declaration puts its return type first (§5). `=>` means "evaluates to" (lambdas, expression bodies, switch arms).
- A function-typed parameter with no `uses` is polymorphic: the call inherits the effects of the function passed, so there is no `tryMap` or `mapAsync`: `map` covers both. One that the callee stores takes the bound of the place it is stored in.
- Stored function types, interface methods and event types declare an upper bound (no `uses` there means pure); implementations may use fewer.
- Derived forms compute their effects: the `-ed` copy adds `alloc` for heap-owning types, the allocating form of `xInto` adds `alloc`, and `tryX` keeps `x`'s effects. `bayan expand` will show them (deferred).
- Package capabilities are an upper bound: a package without `.net` cannot contain a function that `uses net`, even transitively.
- Logging is ambient: `log` is allowed in pure functions and never allocates.
- Handlers are supplied with `provide`; `context` is only the value (`context.Io`). Handler names are the effects plus `log` ([BYN6007](diagnostics/BYN6007.md) otherwise). The test runner provides `console` per test as a capture buffer.
  ```csharp
  provide net: FakeNet(label), clock: FakeClock(at: Date(year: 2026, month: 12, day: 1)) {
      try shipAll(order, by: carrier)
  }
  ```

Enforced by [BYN6001](diagnostics/BYN6001.md) (`noalloc`, `nosuspend`), [BYN6002](diagnostics/BYN6002.md) (effect not declared), [BYN6003](diagnostics/BYN6003.md) (unknown effect), [BYN6004](diagnostics/BYN6004.md) (capability exceeded), [BYN6005](diagnostics/BYN6005.md) (bound exceeded) and [BYN6006](diagnostics/BYN6006.md) (`context (…) { }` block) and [BYN6007](diagnostics/BYN6007.md) (unknown handler).

## 10. Shapes, states and logging

- **`T.X` vs `X<T>`:** `T.X` is a different shape derived from `T` (`T.Builder`, `T.Patch`, `T.Field`, `T.Id`). `X<T>` is the same value with a different status (`Untrusted<T>`, `Secret<T>`).
- **States** are named as participles or adjectives (`paid`, `shipped`, `draft`). They are exempt from the `-ed` copy rule ([BYN5001](diagnostics/BYN5001.md)), which applies only to members. A transition is a verb that `becomes` a state; `become` performs it. States are declared in one `states` block after the fields; `states` is a contextual keyword at the start of a member declaration when followed by `{`, so a field may be named `state`. A state is a type, written `T.s` (`Order.paid`), and several states are written inline with `is .a or .b`. A state name may not equal a static member name of its type ([BYN7008](diagnostics/BYN7008.md)).
  ```csharp
  struct Order {
      OrderId id
      List<Line> lines

      states {
          placed
          paid(PaymentId payment)
      }
  }

  public void pay(ref this is .placed, Payment p) becomes .paid fails PaymentError uses net {
      let id = try p.charge(total)
      become .paid(payment: id)
  }
  ```
- **Logging:** `log` is the verb; the level is a particle argument. `Log.debug` fails the grammar check: `debug` is a level, not the act ([BYN5007](diagnostics/BYN5007.md)).
  ```csharp
  log($"weighing {order.id}")               // default level: info
  log($"retry {n} of {max}", as: .debug)
  ```

## 11. Error-message voice

For diagnostics:
- Lowercase first word, no trailing period.
- State the fact, not blame: "`{0}` is immutable", "strings cannot be joined with `+`".
- Quote code in backticks; name the construct the reader wrote.
- For an input form or a foreign idiom, name the Bayan form: "`errdefer` is now `defer failing`", "Bayan has no `toString()`; display is `format()`".
- Put the fix in the help line or the fix-it, not in the headline.
- Use plain English, also for grammar rules: "nouns name values, verbs name actions; `get` adds nothing here". The Arabic source of a rule appears only in this guide (§12) and in the "Where this comes from" section of its `bayan explain` page.

For error doc comments: the first line of a `///` comment on an error case is its summary, used by `$"{e}"` (SPEC §2.2). Write it as a sentence a user of the program can read.

## 12. Sources

| Rule | Source | Code |
|---|---|---|
| Plain verb returns a new value (tree 2) | Arabic Form I (base verb) | |
| Verb on `ref this` mutates (tree 1) | Arabic Form V (tafaʿʿala, reflexive) | [BYN2115](diagnostics/BYN2115.md), [BYN5101](diagnostics/BYN5101.md) (lint) |
| `-ed` changed copy or view (tree 2) | Arabic mafʿūl (passive participle) | [BYN5001](diagnostics/BYN5001.md) |
| `is` / `has` / `can` property (tree 3) | Arabic ṣifa (attribute) | [BYN5002](diagnostics/BYN5002.md), [BYN5102](diagnostics/BYN5102.md) (lint) |
| `tryX` (tree 4) | Arabic Form X (istafʿala, attempt) | [BYN5003](diagnostics/BYN5003.md) |
| `xInto(ref dst)` (tree 5) | Arabic particle إلى (ilā, "into") | none yet |
| `xOp` (tree 6) | Arabic maṣdar (verbal noun) | none yet |
| Doer and instrument nouns (tree 7) | Arabic fāʿil (doer: `Builder`, `Parser`) and mifʿāl (instrument: `Hasher`, `Comparer`) | none yet |
| Statement modifiers (§2) | Arabic ḥāl (circumstantial participle after the head verb) | none (shape); see §2 |
| Casing and `I` prefix | C# convention; no Arabic or Esperanto source | [BYN2008](diagnostics/BYN2008.md), [BYN2009](diagnostics/BYN2009.md) |
| One spelling per construct (§3) | Arabic iʿjām: the dots added to Arabic script to remove ambiguity (brand brief §2) | see §3 |
| Ellipsis (§3) | Arabic ḥadhf (omitting what context recovers) | none |
| Conditionals (§4) | source: to be confirmed | see §4 |
| Head-first word order (§5) | Arabic word order: the head before its modifiers | [BYN1043](diagnostics/BYN1043.md), [BYN1044](diagnostics/BYN1044.md) |
| Statement termination (§6) | Waqf marks of Qurʾanic recitation (lāzim, lā, waṣl) | see §6 |
| Purpose clauses (§7) | Arabic mafʿūl li-ajlihi (object of purpose) | [BYN8001](diagnostics/BYN8001.md) |
| Argument particles (§8) | Arabic prepositions (مِن, إلى, بِ) | see §8 |
| States and transitions (§10) | Arabic kāna / ṣāra ("was" / "became") | [BYN7001](diagnostics/BYN7001.md)–[BYN7008](diagnostics/BYN7008.md) |
| Error-message voice (§11) | The hudhud in Sūrat an-Naml (27:20–22), whose news is certain and clear: exact about what went wrong, where, why and how to fix it (brand brief §4) | none |

## 13. Diagnostics

Every rule above links to the code that enforces it, at `docs/diagnostics/BYN####.md`; `bayan explain BYN####` prints the same text. Some pages are written with the compiler work and may not exist yet. Rules marked "no dedicated code yet" are guidance until a checker lands.

The built-in checker (`BYN5xxx`) runs on the members of every struct. Errors (`BYN50xx`) are pattern contracts and apply to all code; lints (`BYN51xx`) are word-class heuristics on the public API only (§1), reported as warnings that never block a build. Words outside the built-in list come from the project dictionary (§7).

| Code | Rule | What the checker looks at |
|---|---|---|
| [BYN5001](diagnostics/BYN5001.md) | An `-ed` member does not take `ref this`: "`{0}` changes `this`; name it `{1}`" (tree 1, 2) | One lowercase word of five letters or more ending in `-ed`, on a member that takes `ref this` and does not return `bool`. The suggested name is the plain verb: the name minus `-ed` (`sorted` → `sort`), minus `d` (`reversed` → `reverse`), with a doubled consonant undone (`trimmed` → `trim`) or `-ied` → `-y` (`copied` → `copy`). Not participles: `-eed` words (`need`, `speed`, `seed`), shorter words (`red`, `bed`, `used`), and `hundred`, `embed`, `shred`, `naked`, `wicked`, `sacred`, `kindred`. Typestate `state` names are not members and are not checked (§10). |
| [BYN5002](diagnostics/BYN5002.md) | A boolean property does not modify `this` (tree 3) | A `ref this` member with no arguments named `is`, `has` or `can` followed by a capital. |
| [BYN5003](diagnostics/BYN5003.md) | A hand-written `tryX` returns `T?` or `bool` (tree 4) | `tryX` on a type with an `x()` that `fails`. |
| [BYN5004](diagnostics/BYN5004.md), [BYN5005](diagnostics/BYN5005.md), [BYN5006](diagnostics/BYN5006.md) | Particles (§8) | Parameter lists and call sites; `rest` tails, `@positional` constructors, `extern`, operators and payloads are exempt. |
| [BYN5007](diagnostics/BYN5007.md) | A level is not the act (§10) | `Log.{level}` member access. |
| [BYN5009](diagnostics/BYN5009.md) | A particle word is not a member name, and no instance member spells a conversion with `to` or `as` (§1) | Member names equal to a particle (`at`, `by`, `from`), and instance members named `toX` or `asX` (`toList`, `asSpan`); static `fromX` factories are not checked. |
| [BYN5010](diagnostics/BYN5010.md) | An event or stored function field has no `on` prefix (§1) | Fields of type `Event<T>` or a function type whose name starts with `on` and a capital. |
| [BYN5013](diagnostics/BYN5013.md) | A particle appears at most once per signature (§8) | The particle labels of one parameter list. |
| [BYN5014](diagnostics/BYN5014.md) (warning, fix-it) | Every word of a member name is a known verb or noun (§7) | Each word against the built-in word list and `grammar` in `package.byn`. |
| [BYN5101](diagnostics/BYN5101.md) (lint) | A `ref this` member is named with a verb (tree 1) | The first word of the name against a built-in noun list: `size`, `length`, `total`, `sum`, `value`, `values`, `data`, `item`, `items`, `element`, `elements`, `result`, `results`, `name`, `id`, `state`, `status`, `info`, `content`, `contents`, `capacity`, `amount`, `number`, `key`, `keys`, `entry`, `entries`, `node`, `nodes`. |
| [BYN5102](diagnostics/BYN5102.md) (lint, fix-it) | A `bool` property takes `is`, `has` or `can` (tree 3) | `bool` properties and `bool` methods with no arguments that are not `ref this`; fields are not checked. Names starting with `contains`, `equals`, `matches`, `startsWith`, `endsWith` or `try` are verbs, not states. |
