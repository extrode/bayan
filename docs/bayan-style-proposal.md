# Bayān Style — Proposal

Status: decided (all items in §5 settled; C21 revised 2026-11-04). Layout rules land in `bayan format` and docs/bayan-style.md in round 4; no grammar revision is needed.

---

## 1. What the research says

| Finding | Source | What it means for Bayān |
| --- | --- | --- |
| Blank lines are the strongest positive local readability feature; comments matter less than blank lines. Long lines and many identifiers per line are the strongest negatives. | Buse & Weimer, *Learning a Metric for Code Readability* (TSE 2010) | Vertical whitespace ("paragraphs") and line width are the two biggest levers. |
| Lined-up `=` in a sequence of assignments is a visual regularity humans rate as readable; experienced raters weigh whitespace more than novices. | Dorn, *A General Software Readability Model* (UVA) | Column alignment helps, where things genuinely repeat. |
| 2–4 spaces of indentation gave the best comprehension; 0 and 6+ were worse. Brace/block placement made no difference. | Miara, Musselman, Navarro, Shneiderman, *Program Indentation and Comprehensibility* (CACM 1983). A 2019 Java replication (Bauer et al.) did not clearly reproduce the effect. | Indentation is not semantic in Bayān (braces delimit blocks); it only has to be consistent. Default 4, set per project. Don't spend design energy on brace placement; the grammar already decides it. |
| camelCase vs under_score: no accuracy difference; recognition speed follows what the reader was trained on. | Sharif & Maletic (ICPC 2010); Binkley et al. (EMSE 2012) | Keep camelCase. Consistency beats the choice itself. |
| "Gofmt's style is no one's favorite, yet gofmt is everyone's favorite." A formatter with few or no options ends style debates. | Go proverbs; Griesemer, *gofmt* talk (2015) | Make layout mechanical, with a small closed set of knobs. |

Two conclusions:

1. Bayān today reads "Java-like" not because of camelCase or brace placement (neither affects comprehension much) but because of **density**: no paragraphs, long single-line headers, long chains, nothing in columns.
2. The grammar already did half the work. Waqf rules force `} else {`, operand-on-same-line, and leading continuation tokens (`.`, `and`, `uses`, `fails`, …). Style only has to finish the shape.

---

## 2. Principles

1. **Paragraphs, not walls.** A blank line separates steps of a function the way it separates paragraphs in prose.
2. **Shape follows grammar.** When a header wraps, each clause (`where`, `expects`, `uses`, `becomes`, `fails`) gets its own line, so the signature reads as a sentence with its clauses.
3. **Columns where things repeat.** Consecutive fields, properties and switch arms form a table; align them.
4. **Break at meaning.** A wrapped line starts with what continues it (`.`, `and`, `??`, a clause keyword). Bayān's waṣl rules already require this.
5. **Layout is mechanical; idioms are advice.** The formatter decides whitespace without diagnostics. Idioms (postfix guards, `it`) are lints with configurable severity.

---

## 3. Rules with before/after

Examples are from a Sūq-style storefront.

### 3.1 Paragraphs inside functions

```cs
// Before
public Money quote(Cart cart, for Customer customer) expects cart.lines.length > 0 uses db fails QuoteError {
    let rates = taxRates.forRegion(customer.region) ?? fail .noTaxRegion
    var subtotal = Money.zero(cart.currency)
    for line in cart.lines {
        let product = try catalog.find(line.sku)
        fail .discontinued(line.sku) if product.isDiscontinued
        subtotal = subtotal + product.price * line.quantity
    }
    let discount = customer.tier.discount(on: subtotal)
    let total = (subtotal - discount) * rates.multiplier
    log($"quote {cart.id}: {total}", as: .debug)
    return total
}
```

```cs
// After
public Money quote(Cart cart, for Customer customer)
        expects cart.lines.length > 0
        uses db
        fails QuoteError {
    let rates = taxRates.forRegion(customer.region) ?? fail .noTaxRegion
    var subtotal = Money.zero(cart.currency)

    for line in cart.lines {
        let product = try catalog.find(line.sku)
        fail .discontinued(line.sku) if product.isDiscontinued

        subtotal = subtotal + product.price * line.quantity
    }

    let discount = customer.tier.discount(on: subtotal)
    let total = (subtotal - discount) * rates.multiplier

    log($"quote {cart.id}: {total}", as: .debug)
    return total
}
```

Rules:
- The formatter **keeps** a single blank line the author wrote, collapses runs of blank lines to one, and removes blank lines directly after `{` and before `}`.
- It never inserts blank lines inside a body; where to break paragraphs is the author's call. docs/bayan-style.md recommends: after setup, after a run of guards, around loops, before the final `return`.

### 3.2 Declarations and members

```cs
// Before
@derive(Json, Row)
public struct Customer {
    CustomerId id
    Email email
    string name
    Region region
    Tier tier = .standard
    Instant createdAt
    public bool isVip => tier is .gold or .platinum
    public string displayName => $"{name} <{email}>"
    public void promote(ref this, to Tier next) { tier = next }
}
```

```cs
// After
@derive(Json, Row)
public struct Customer {
    CustomerId id
    Email      email
    string     name
    Region     region
    Tier       tier = .standard
    Instant    createdAt

    public bool   isVip       => tier is .gold or .platinum
    public string displayName => $"{name} <{email}>"

    public void promote(ref this, to Tier next) {
        tier = next
    }
}
```

Rules:
- **Alignment in runs.** A run is consecutive lines of the same shape with no blank line or comment line between them. Within a run, align the name column (fields, properties) and the `=>` column (properties). A blank line ends the run, which keeps diff noise local. This is gofmt's model, and it matters more in Bayān than in Go: type-first declarations put a variable-width type before every name.
- One blank line between member groups: fields, `states`, properties, static members, methods. One blank line between methods.
- Blocks with statements always span lines; only `=>` bodies stay on one line.
- Attributes go on their own line above types and functions, and inline before fields and parameters (`@json(name: "note") string note`).

### 3.3 Switch arms

```cs
// Before
let label = switch order {
    .placed => "Awaiting payment"
    .paid p => $"Paid via {p.payment.method}"
    .shipped s => $"Shipped, tracking {s.tracking}"
    .canceled => "Canceled"
}
```

```cs
// After
let label = switch order {
    .placed    => "Awaiting payment"
    .paid p    => $"Paid via {p.payment.method}"
    .shipped s => $"Shipped, tracking {s.tracking}"
    .canceled => "Canceled"
}
```

Same run rule: single-line arms align `=>`. A multi-line arm ends the run.

### 3.4 Wrapped headers

One brace rule (user decision 2026-11-04, review row C21): **`{` always ends the line.** There is no `braces` knob and no own-line `{`.

- A parameter list that doesn't fit wraps like any other list (§3.6): one parameter per line, a trailing comma, and the closer on its own line, where it shares the line with `{` as `) {` (as rustfmt does). There is never a `)` line followed by a `{` line.
- Control heads (`if`, `while`, `for`) have no parentheses in Bayān, so they are not lists. A wrapped condition breaks before `and`/`or` and ends with ` {`. `conditions.named` suggests naming it instead.
- Clauses (`where` → `expects` → `uses` → `becomes` → `fails`, the proposed round-4 order) follow the closer on the `) {` line when they fit: `) uses net fails BookingError {`.

```cs
Booking book(
    Carrier carrier,
    Route route,
    Date date,
) uses net, suspend fails BookingError {
    let quote = try carrier.quote(route, on: date)
    …
}
```

- **Header continuations indent two levels (8)** (user decision 2026-11-04, C21 follow-up). When the clauses do not fit on the header line, each clause gets its own line at two levels and the last one ends with ` {`. A wrapped condition's continuation lines take the same two levels. The body stays at one level, so `fails QuoteError {` never lines up with the first statement below it (PEP 8's hanging indent). This is a formatter rule, not a knob. See the §3.1 example.

```cs
// Before
if order is .paid and order.total >= freeShippingThreshold and customer.region.shipsFree and not order.hasOversizedItems {
    order.shipping = .free
}
```

```cs
// After (formatter)
if order is .paid
        and order.total >= freeShippingThreshold
        and customer.region.shipsFree
        and not order.hasOversizedItems {
    order.shipping = .free
}
```

```cs
// After (lint suggestion TBD (x9xx of BYN4xxx, A2): name the condition)
let shipsFree = order is .paid
    and order.total >= freeShippingThreshold
    and customer.region.shipsFree
    and not order.hasOversizedItems

order.shipping = .free if shipsFree
```

No grammar change: `{` never starts a line, so the waqf table needs no new row (the earlier own-line rule needed one). `} else {` stays mandatory.

### 3.5 Chains and queries

```cs
// Before
let reminders = orders.where(it.placedAt < cutoff and it.total > minimum).sorted(by: it.placedAt).take(50).map(Reminder(it.id, for: it.customer))
```

```cs
// After
let reminders = orders
    .where(it.placedAt < cutoff and it.total > minimum)
    .sorted(by: it.placedAt)
    .take(50)
    .map(Reminder(it.id, for: it.customer))
```

A chain breaks one call per line, with a leading `.`, when it exceeds the width or has three or more calls taking lambdas. If any link breaks, all links break.

### 3.6 Argument lists and literals

```cs
// Before
let shipment = carrier.book(parcel, from: warehouse.address, to: order.shippingAddress, by: order.deadline, as: .express)
```

```cs
// After
let shipment = carrier.book(
    parcel,
    from: warehouse.address,
    to: order.shippingAddress,
    by: order.deadline,
    as: .express,
)
```

Rules:
- If the list fits, it stays on one line. Otherwise there is one item per line, a trailing comma, and the closing bracket on its own line. This is the one list rule: arguments, literals and parameter lists alike (§3.4 for `) {`).
- **Trailing comma as intent** (Black's "magic trailing comma"): if the author writes a trailing comma, the list stays one-per-line even when it would fit. This is how an author keeps a short particle call vertical.
- Continuation indent is one level (4), except header clauses and wrapped conditions, which take two (§3.4). Nothing aligns to an opening parenthesis, since that alignment shifts every line on a rename.

### 3.7 Top-level layout

```cs
// Before
type distinct CustomerId = i64
type distinct Sku = string requires it.length <= 16
effect db = net, fs
union error QuoteError {
    noTaxRegion
    discontinued(Sku sku)
}
public Money quote(Cart cart, for Customer customer) …
```

```cs
// After
type distinct CustomerId = i64
type distinct Sku        = string requires it.length <= 16

effect db = net, fs

union error QuoteError {
    noTaxRegion
    discontinued(Sku sku)
}

public Money quote(Cart cart, for Customer customer) …
```

One blank line between top-level declarations. Consecutive one-line declarations of the same kind form a run, keep no blank line between them, and align `=`.

### 3.8 Horizontal spacing

| Context | Rule | Example |
| --- | --- | --- |
| Binary operators, `=>`, `??`, `=` | one space each side | `a + b`, `x ?? 0` |
| Ranges | no spaces | `0..n`, `from: 2..<8` |
| Unary symbols | tight | `-x`, `!done` |
| Labels, particles, literal fields, annotations | no space before `:`, one after | `at: i`, `let w: Grams`, `{ x: 1 }` |
| Struct literals | spaces inside braces | `Point { x: 1, y: 2 }` |
| List and map literals, calls | no inner padding; no space before `(` | `[1, 2]`, `f(x)` |
| Generics, pointers, units | tight | `List<T>`, `u8*`, `30s` |
| Interpolation holes | as named arguments | `{n, as: .hex}` |
| Comments | `// ` + text; trailing comments align within a run | `i64 port // default 8080` |
| Files | UTF-8, LF, final newline, no trailing whitespace | |

### 3.9 Naming (unchanged, now written down)

- `PascalCase`: types, unions, attributes' derivation names (`@derive(Json)`).
- `camelCase`: functions, fields, locals, cases (`.paid`), effect aliases (`effect db = net, fs`), attributes (`@json`), constants.
- Acronyms are words: `Utf8`, `Json`, `Sql`, `CustomerId`, `httpClient`.
- No prefixes or suffixes for kind (`IFoo` aside from the existing interface convention, `_field`, `m_`).

---

## 4. Enforcement

### 4.1 Three layers

| Layer | Who enforces | Configurable | Examples |
| --- | --- | --- | --- |
| **Grammar** | compiler, BYN1xxx and BYN5xxx errors | no | `} else {`, operand on same line, `;`, particles, naming grammar |
| **Layout** | `bayan format` rewrites; no diagnostics | a few knobs | indent, width, blank lines, wrapping, alignment, spacing |
| **Idioms** | style lints, `x900–x999` of the matching area (A2) | severity per lint | postfix guards, `it` lambdas, nesting depth |

Layout is never compiler-enforced; idiom lints are diagnostics, `.suggestion` by default, with severity set per lint in `package.byn` (review row C9: spec :2104 and bayan-style.md:5 get this wording in round 4). Layout never produces a warning. You don't fix it, you run the formatter. That is the gofmt lesson. Idioms are judgement calls, so they are lints with the same severity model as `dotnet_diagnostic.X.severity`.

Style and idiom lints take codes in the `x900–x999` range of their area (addendum A2, docs/revisions/0017-addendum-a2-diagnostic-codes.md): 0xxx project, files, `package.byn`, CLI and tools; 1xxx syntax, 2xxx types, 3xxx memory, 4xxx control flow and failure, 5xxx grammar and naming, 6xxx effects and concurrency, 7xxx typestate and contracts, 8xxx trust and security, 9xxx build, C backend, toolchain and runtime traps. Numbers are assigned at the round-4 renumbering. A lint's area follows A2 rule 2 (the mistake the user made): if the only mistake is the spelling, 19xx; otherwise the area of what the user misunderstood. Whether a lint has a fix-it does not decide its area (user decision 2026-11-04).

Initial lints (all `.suggestion` by default):

| Code | Name | Flags |
| --- | --- | --- |
| TBD (x9xx of BYN1xxx syntax, A2) | `guards.postfix` | an `if` block whose only statement is `return`, `fail`, `break` or `continue` → `fail .x if c` |
| TBD (x9xx of BYN1xxx syntax, A2) | `lambdas.it` | `x => x.f` with one parameter → `it.f` |
| TBD (x9xx of BYN4xxx control flow, A2) | `nesting.depth` | blocks nested more than 4 deep → suggest early exit |
| TBD (x9xx of BYN0xxx project and tools, A2) | `functions.length` | body over N lines (off by default) |
| TBD (x9xx of BYN4xxx control flow, A2) | `conditions.named` | a control head that wraps → suggest a named `let` |

### 4.2 Configuration: `package.byn` is the source of truth

```cs
Package {
    name: "local/suq",
    edition: 2026,

    style: Style {
        indent: 4,
        width: 120,
        align: .runs,               // .none
        trailingCommas: .multiline,  // .never
        booleans: .symbols,          // .words
        primitives: .sized,          // .csharp

        lints: [
            "guards.postfix": .warning,
            "functions.length": .none,
        ],
    },
}
```

- The set of knobs is closed and short. Each knob is a real trade-off people disagree on; everything else is fixed.
- Precedence: `package.byn` → `.editorconfig` (universal keys only) → defaults. A workspace-level `style` is inherited, and a package overrides individual keys.

### 4.3 `.editorconfig` interop

Visual Studio, VS Code and Rider already read `.editorconfig`, so editors get indentation right while you type, before any format runs. But a second file with its own syntax would split the source of truth.

- `bayan` **reads** the universal keys for `[*.byn]`: `indent_style`, `indent_size`, `max_line_length`, `end_of_line`, `insert_final_newline`, `trim_trailing_whitespace`, `charset`. They are used only where `package.byn` is silent.
- `bayan format --editorconfig` **writes** a `[*.byn]` section from `package.byn`, so the two never drift. `--check` reports drift.
- There are no `bayan_*` keys in `.editorconfig`. Bayān-specific style lives in Bayān syntax, consistent with the `package.byn` decision.

### 4.4 Opting out

```cs
@unformatted because "rotation matrix laid out by hand"
const rotation = [
    1, 0,  0,
    0, c, -s,
    0, s,  c,
]
```

This reuses the existing `because` purpose clause, and `bayan audit` lists every use. It applies to declarations only, with no comment-based on/off toggles.

### 4.5 Tooling

- **One engine.** The formatter is in the compiler and works on the lossless syntax tree, so comments and trivia are preserved. It is a Wadler-style group/break printer, as in Prettier: a group that doesn't fit breaks into one item per line.
- **CLI.** `bayan format` writes. `bayan format --check` writes nothing, lists each file that would change and exits 1 (spec :2243, unchanged); `--check --diff` prints the diff instead, for CI logs (review row C23). `bayan format --range a:b` formats a selection.
- **LSP.** `formatting`, `rangeFormatting` and `onTypeFormatting` call the same engine, so Visual Studio and VS Code format on save identically. Lints arrive as ordinary diagnostics with code actions.
- **Guarantees.** The formatter is idempotent (`FormattingIsIdempotent` in `tests/Bayan.Tests/FormatTests.cs`), output re-parses to the same AST, and generated code goes through the same formatter, so it stays readable.

---

## 5. Decisions

| # | Item | Status |
| --- | --- | --- |
| 1 | Indent | 4 by default; not semantic, consistency only; `indent` knob stays |
| 2 | Width | **120** |
| 3 | Wrapped headers | **`{` always ends the line**; a wrapped parameter list closes with `) {` and a trailing comma (§3.4, C21 2026-11-04); no `braces` knob, no waqf revision |
| 4 | Alignment | **`.runs`** by default; projects may set `.none` |
| 5 | Config | **`package.byn`** is the source of truth |
| 6 | Style lints | **x900–x999 of the matching area** (A2), assigned at renumbering |
| 7 | Opt-out | **`@unformatted because "…"`** only |

Timing: layout rules and lints touch `bayan format`, docs/bayan-style.md and the `style` knobs, which the C# manifest check (src/Bayan.Syntax/Packages/Manifest.cs:35) and its self-hosted port (selfhost/semantics/manifest.byn) validate. They land in the round-4 revision, after self-hosting.

Round-4 edits (review row C8, user decision 2026-11-04: drop `braces`, take this proposal's knobs):

| Site | Change |
| --- | --- |
| spec §19.2 manifest example (:2088-2094), bayan-style.md:11-14 and :72-74 | `maxLine: 100` → `width: 120`; drop `braces`; `trailingCommas: true` → `.multiline` (or `.never`); add `align: .runs` (or `.none`) and `lints` |
| Manifest.cs:35, :179-183 and selfhost/semantics/manifest.byn | the same knob set and types; an old knob name gets the manifest migration (review rows C24, I14) |
| spec :2104, bayan-style.md:5 | C9 wording (§4.1) |

