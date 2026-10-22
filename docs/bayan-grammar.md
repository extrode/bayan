# Bayan grammar guide

How to name things the way the standard library does. This is the developer-facing companion to [SPEC §5](bayan-spec.md) (naming and morphology). SPEC §5.2 holds the full pattern table and contracts; this guide tells you which pattern to pick.

Grammar rules are not style (see [STYLE](bayan-style.md)): they cannot be configured away. Compiler errors enforce pattern contracts; lints enforce word-class heuristics and never block builds (`BYN5xxx`, HANDOFF §17).

Casing (SPEC §5.1): `PascalCase` types, `camelCase` everything else, `I` prefix on interfaces. Enforced by [BYN2008](diagnostics/BYN2008.md) (types) and [BYN2009](diagnostics/BYN2009.md) (everything else).

## 1. Naming decision tree

For any new member, take the first question that answers yes.

1. **Changes `this`?** Verb on `ref this`: `void sort(ref this)`. The `-ed` copy (`sorted`) is derived; do not write it.
   - A body that modifies `this` without `ref this`: [BYN2115](diagnostics/BYN2115.md).
   - Calling a `ref this` member on a `let`: [BYN2104](diagnostics/BYN2104.md) (fix: `var`).
   - `-ed` member with no `ref this` verb of the same stem (`joined`): [BYN5001](diagnostics/BYN5001.md).
   - A noun on a `ref this` member: [BYN5101](diagnostics/BYN5101.md) (lint).
2. **Returns a new value, changes nothing?** Plain verb: `join`, `trim`, `split`. Not a participle: `join`, never `joined` ([BYN5001](diagnostics/BYN5001.md)).
3. **Boolean state, no arguments?** `is` / `has` / `can`: `isEmpty`, `hasItems`, `canRetry`. Never a `?` suffix.
   - A state member that modifies `this`: [BYN5002](diagnostics/BYN5002.md).
   - A `bool` state without the prefix: [BYN5102](diagnostics/BYN5102.md) (lint, fix-it).
4. **Can fail, and callers may want `bool` or `T?`?** Write `x() fails E`; `tryX` is derived. A hand-written `tryX` must return `T?` or `bool`: [BYN5003](diagnostics/BYN5003.md).
5. **Writes into the caller's buffer?** `xInto(ref dst)` taking `ref StrBuf` or `Span<T>`; the allocating `x()` is derived (`formatInto` → `format`). No dedicated code yet.
6. **The act as a value?** `xOp` (derived; type `Op<T, E>`). Do not write it. No dedicated code yet.
7. **A thing that performs the act?** Noun type: `Parser`, `Builder` (stateful doer), `Hasher`, `Comparer` (stateless instrument). No dedicated code yet.

Composition order: `[try] verb [Into] [Op]`: `tryFormatInto`, `parseOp`. Other reserved patterns (`ensureX`, `xWith`, `xUnchecked`, noun patterns such as `T.Validated`) are in SPEC §5.2.

Collections follow the tree: `append`, `sort`, `reverse` on `ref this`; `appended`, `sorted`, `reversed` derived (a view if possible, otherwise a copy). Ordered collections `append`/`prepend`; unordered ones `add`.

## 2. Statement modifiers: the ḥāl rule

A statement modifier is a head verb followed by a participle that describes the circumstance: `defer failing`, `defer panicking`.

```csharp
defer arena.reset();                 // every exit
defer failing tx.rollback();         // exit by failure
defer panicking (p) log.error(p);    // panic in progress
```

`failing` and `panicking` are contextual keywords, valid only after `defer`. Future modifiers must follow the same shape: head verb, then participle.

Enforced by:
- `errdefer` is an input form for `defer failing`: [BYN1020](diagnostics/BYN1020.md).
- `defer failing` in a function that cannot fail: [BYN2606](diagnostics/BYN2606.md).
- `try` in a `defer` body: [BYN2601](diagnostics/BYN2601.md).
- Panic handler restrictions: [BYN2612](diagnostics/BYN2612.md) (statement not allowed), [BYN2613](diagnostics/BYN2613.md) (reads only `let` bindings and parameters), [BYN2614](diagnostics/BYN2614.md) (`Panic.onPanic` placement).

The shape itself (head verb, then participle) has no checker code; new modifiers are reviewed against it.

## 3. One form, one meaning

Each meaning has exactly one spelling in source. Familiar alternatives are **input forms**: the editor and `bayan fmt` accept them and rewrite them one way into canonical syntax; the compiler rejects them in source with a machine-applicable fix.

| Input form | Canonical | Code |
|---|---|---|
| `int long short byte uint ulong float double` | `i32 i64 i16 u8 u32 u64 f32 f64` | [BYN1030](diagnostics/BYN1030.md) |
| `User!EmailError f()`, `User!(A, B) f()` | `User f() fails EmailError`, `fails A, B` | [BYN1031](diagnostics/BYN1031.md) |
| `for (let x in xs)`, `foreach (var x in xs)` | `for x in xs` | [BYN1019](diagnostics/BYN1019.md) |
| `if (x) {`, `while (x) {`, `switch (x) {` | no parentheses (whole-condition parentheses kept when a brace is present) | [BYN1017](diagnostics/BYN1017.md) |
| `x == null`, `x != null` | `x is null`, `x is not null` | [BYN2027](diagnostics/BYN2027.md) |
| `if !x { … }` (no else, simple condition) | `unless x { … }` | [BYN1032](diagnostics/BYN1032.md) |
| `{ cents = 250 }` | `{ cents: 250 }` | [BYN1021](diagnostics/BYN1021.md); in `package.byn` [BYN3104](diagnostics/BYN3104.md) |
| `"Hello " + name` (strings) | `$"Hello {name}"` | [BYN2209](diagnostics/BYN2209.md) |

Also rejected with a fix-it (design round 2 plan §15):

| Old form | Canonical | Code |
|---|---|---|
| `errdefer` | `defer failing` | [BYN1020](diagnostics/BYN1020.md) |
| `catch (e) switch { … }` | `catch switch { E e => … }` | [BYN1025](diagnostics/BYN1025.md) |
| `error set S = A \| B` | `error set S = A, B` | [BYN1029](diagnostics/BYN1029.md) |
| `List.add`, `tryFirst` | `append`, `first` (round 3 keeps `.length`) | [BYN2230](diagnostics/BYN2230.md) |
| `toString()` | `format()` | [BYN2031](diagnostics/BYN2031.md) |

A body that is not a block: [BYN1018](diagnostics/BYN1018.md). Display-only views (inferred types, expanded derived members) are language-server inlay hints, never a stored form.

## 4. Conditionals

| Form | Use when | Codes |
|---|---|---|
| `if c { … }` | The general case. No parentheses, braces mandatory. | [BYN1017](diagnostics/BYN1017.md), [BYN1018](diagnostics/BYN1018.md) |
| `unless c { … }` | A guard on a simple positive condition with no `else`. Not with `else`, not with `!`, not with a top-level `&&`/`\|\|`, not with `is null`, no likelihood word. | [BYN1006](diagnostics/BYN1006.md), [BYN1007](diagnostics/BYN1007.md), [BYN1022](diagnostics/BYN1022.md), [BYN1023](diagnostics/BYN1023.md), [BYN1033](diagnostics/BYN1033.md), [BYN1032](diagnostics/BYN1032.md) |
| `if unlikely c { … }` | A cold path that is not already an error return. Layout hint only; semantics unchanged. | [BYN2233](diagnostics/BYN2233.md) (lint: redundant on error returns) |
| `if never c { … }` | A state that would be a bug. Panics if true in every build mode. For a caller's obligation on entry, use `requires` instead. | No dedicated code (runtime panic) |
| `stmt if c;`, `stmt unless c;` | A one-line guard: `return`, `break`, `continue` or one call. | [BYN1008](diagnostics/BYN1008.md) (simple statements only), [BYN1009](diagnostics/BYN1009.md) (no chaining) |

```csharp
flush() if unlikely buf.isFull;
return .badRequest("Cart is empty") unless cart.hasItems;
let order = db.find(id) ?? return .notFound;
```

Null checks are `is null` / `is not null` ([BYN2027](diagnostics/BYN2027.md)); write `if x is not null`, not `unless x is null`. Bind a call's non-null result with `?? return …`, `?? break` or `?? continue`.

Pick the tool by what a failed check means: non-null types (no check), `requires` (a bug), distinct types (a rule true everywhere), `Untrusted<T>.validated()` (user input, all errors), `return .x unless …` (a business outcome).

## 5. Error-message voice

For diagnostics:
- Lowercase first word, no trailing period.
- State the fact, not blame: "`{0}` is immutable", "strings cannot be joined with `+`".
- Quote code in backticks; name the construct the reader wrote.
- For an input form or a foreign idiom, name the Bayan form: "`errdefer` is now `defer failing`", "Bayan has no `toString()`; display is `format()`".
- Put the fix in the help line or the fix-it, not in the headline.

For error doc comments: the first line of a `///` comment on an error case is its summary, used by `$"{e}"` (SPEC §2.2, HANDOFF §13). Write it as a sentence a user of the program can read.

## 6. Sources

| Rule | Source | Code |
|---|---|---|
| Plain verb returns a new value (tree 2) | Arabic Form I (base verb) | [BYN5001](diagnostics/BYN5001.md) |
| Verb on `ref this` mutates (tree 1) | Arabic Form V (tafaʿʿala, reflexive) | [BYN2115](diagnostics/BYN2115.md), [BYN5101](diagnostics/BYN5101.md) (lint) |
| `-ed` copy of a mutating verb (tree 1, 2) | Arabic mafʿūl (passive participle) | [BYN5001](diagnostics/BYN5001.md) |
| `is` / `has` / `can` state (tree 3) | Arabic ṣifa (attribute) | [BYN5002](diagnostics/BYN5002.md), [BYN5102](diagnostics/BYN5102.md) (lint) |
| `tryX` (tree 4) | Arabic Form X (istafʿala, attempt) | [BYN5003](diagnostics/BYN5003.md) |
| `xInto(ref dst)` (tree 5) | Arabic particle إلى (ilā, "into") | none yet |
| `xOp` (tree 6) | Arabic maṣdar (verbal noun) | none yet |
| Doer and instrument nouns (tree 7) | Arabic fāʿil (doer: `Builder`, `Parser`) and mifʿāl (instrument: `Hasher`, `Comparer`) | none yet |
| Statement modifiers (§2) | Arabic ḥāl (circumstantial participle after the head verb) | none (shape); see §2 |
| Casing and `I` prefix | C# convention; no Arabic or Esperanto source | [BYN2008](diagnostics/BYN2008.md), [BYN2009](diagnostics/BYN2009.md) |
| One form, one meaning (§3) | Arabic iʿjām: the dots added to Arabic script to remove ambiguity (brand brief §2) | see §3 |
| Conditionals (§4) | source: to be confirmed | see §4 |
| Error-message voice (§5) | The hudhud in Sūrat an-Naml (27:20–22), whose news is certain and clear: exact about what went wrong, where, why and how to fix it (brand brief §4) | none |

## 7. Diagnostics

Every rule above links to the code that enforces it, at `docs/diagnostics/BYN####.md`; `bayan explain BYN####` prints the same text. Some pages are written with the compiler work and may not exist yet. Rules marked "no dedicated code yet" are guidance until a checker lands.

The built-in checker (`BYN5xxx`) runs on the members of every struct. Errors (`BYN50xx`) are pattern contracts; lints (`BYN51xx`) are word-class heuristics, reported as warnings that never block a build.

| Code | Rule | What the checker looks at |
|---|---|---|
| [BYN5001](diagnostics/BYN5001.md) | An `-ed` member needs a `ref this` verb of the same stem (tree 1, 2) | One lowercase word of five letters or more ending in `-ed`, on a member that is not `ref this` and does not return `bool`. The verb is the name minus `-ed` (`sorted` → `sort`), minus `d` (`reversed` → `reverse`), with a doubled consonant undone (`trimmed` → `trim`) or `-ied` → `-y` (`copied` → `copy`). Not participles: `-eed` words (`need`, `speed`, `seed`), shorter words (`red`, `bed`, `used`), and `hundred`, `embed`, `shred`, `naked`, `wicked`, `sacred`, `kindred`. |
| [BYN5002](diagnostics/BYN5002.md) | A state member does not modify `this` (tree 3) | A `ref this` member with no arguments named `is`, `has` or `can` followed by a capital. |
| [BYN5003](diagnostics/BYN5003.md) | A hand-written `tryX` returns `T?` or `bool` (tree 4) | `tryX` on a type with an `x()` that `fails`. |
| [BYN5101](diagnostics/BYN5101.md) (lint) | A `ref this` member is named with a verb (tree 1) | The first word of the name against a built-in noun list: `size`, `length`, `total`, `sum`, `value`, `values`, `data`, `item`, `items`, `element`, `elements`, `result`, `results`, `name`, `id`, `state`, `status`, `info`, `content`, `contents`, `capacity`, `amount`, `number`, `key`, `keys`, `entry`, `entries`, `node`, `nodes`. |
| [BYN5102](diagnostics/BYN5102.md) (lint, fix-it) | A `bool` state takes `is`, `has` or `can` (tree 3) | `bool` properties and `bool` methods with no arguments that are not `ref this`; fields are not checked. Names starting with `contains`, `equals`, `matches`, `startsWith`, `endsWith` or `try` are verbs, not states. |
