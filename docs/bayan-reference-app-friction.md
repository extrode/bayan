# Sūq — friction log

Places where writing Sūq against the round-3 spec read badly, needed a workaround, or hit something the spec doesn't define. Per §21 these are proposals for you to decide on, not spec changes. Code that depends on an assumption is marked with the entry number.

**Status:** *Gap* means the spec is silent and I picked something. *Friction* means the spec covers it but the result reads badly. *Security* means the rules let something risky through.

## Missing syntax (gaps)

| # | Where | Issue | What Sūq assumes or proposes |
|---|---|---|---|
| F-01 | every test file | No way to assert that a call fails. `catch e => { assert …; return }` followed by `panic` works, but takes 5 lines | Assumed `assert expr fails Pattern` |
| F-02 | quote, packing benches | No way to separate a bench's setup from the part that's measured and budgeted | Assumed a `measure { … }` block inside `bench` |
| F-03 | money, stats | `law` syntax and the levels (tested, exhaustive, proved) aren't pinned down | `law name(params) { boolean expr }`, level set in package.byn |
| F-04 | quote, db | Binding a union case's payload in a switch arm | `.percentOff p => p.rate`, the same shape as `DbError e =>` in `catch switch` |
| F-05 | db `toOrder` | No syntax for constructing a typestate value in a given state. Restoring from storage needs one full literal per state, repeating every common field | Assumed `Order.paid { …, payment }`. Proposal: `Order { id, … } as .paid(payment)`, or a derived `Order.restore(state:…)` |
| F-06 | order tests | Which state a plain `Order { … }` literal starts in | Wrote `Order.draft { … }` everywhere to avoid the question |
| F-19 | ids, main | Names of the fallible constructors of distinct types | `T(x)` (fails); `T.tryFrom(value)`; `T.tryParse(text)` |
| F-20 | sodium, packing | Numeric conversions | `u8(x)`, `usize(x)`, checked (trap when out of range) |
| F-21 | order `total`/`weight` | Computed properties are only shown with `=>` in the spec | Assumed block bodies: `public Money total { … }` |
| F-26 | checkout, stats, main | Map literal syntax | `[key: value]`, `[:]` for empty |
| F-29 | accounts, checkout | A `catch switch` arm with several statements | `Arm => { …; return … }` blocks whose type is `never` |
| F-30 | sodium | `extern` block syntax, and how a `unique` type runs cleanup | `extern "lib" { … }` with C-style params; `public void drop(ref this)` |

## Rules that read badly (friction)

| # | Where | Issue | Proposal |
|---|---|---|---|
| F-07 | order `removeLine` | `with` re-checks every rule, so marking a line removed needs `try` and `fails ValidationError<Line>`, even though `status` has no rule | `with` is infallible when no changed field has a field rule and no struct-level rule reads it; the compiler knows both |
| F-08 | everywhere | `try Sku("TEA-0001")` on a literal is noise | Assumed: literal arguments and `const`s of `requires` types are checked at compile time, with no `try`. The checker enforces this (`fail/try-literal`) |
| F-09 | sodium, money, unions | The particle rule can't apply to `extern` C signatures, operators, union case payloads or laws | Exempt these four explicitly. The checker had to learn the first three |
| F-10 | db, catalog | `conn.query(sql"…$1, $2", a, b)` has several unlabeled arguments | State that the arguments after a `sql` literal are variadic and checked against its `$n` count at compile time |
| F-11 | carrier | Branching on `reply.accepted` (`Untrusted<bool>`) needs `== true`, because only comparisons are clean facts | Make `Untrusted<bool>` usable directly as a condition. A bool can't carry an injection |
| F-12 | gateway → Storefront | Holding one `unique` key makes every containing struct `unique` | Accept, or let a service hold a borrow of a resource owned by `main` (needs region rules for long-lived borrows) |
| F-14 | stats `mergeWith` | Copy semantics of structs that own collections aren't specified. `var merged = this` was ambiguous | Owning collections never copy implicitly; `copied()` is explicit |
| F-15 | db rows | Strings borrowed from a row and stored in a longer-lived value need copying | Name the operation (`owned()` assumed). The diagnostic should suggest it |
| F-16 | signup | A struct-level rule that reads a field which itself failed (`password != email.value` when `email` is invalid) can't be evaluated | Skip it and report only the field violation (assumed) |
| F-22 | packing | Sort stability isn't specified. The C# port needed a composite key to stay deterministic | Make `sort` stable, and give unstable sorting its own name |
| F-23 | carrier `bookAll` | Group deadlines surface as `Canceled`, which every caller has to map into its own error set | Let `Io.group(timeout:, failing: .timeout(after:))` map it once |
| F-24 | sodium, http | `Bytes` was both the size unit and the byte-buffer type | Reserve `Bytes` for the unit; data is `Span<u8>` |
| F-25 | sodium (O2) | With counts as plain integers, `raw.length` is `i64` but sizes are `KiB`, so mixing them needs `/ 1B` | Acceptable; record it with O2 |
| F-27 | carrier | `@forms(Op)` on a free function generates `BookWithRetryOp` | Confirm the naming rule for free functions |

## Security findings

| # | Where | Issue | Proposal |
|---|---|---|---|
| F-13 | checkout `DraftStore` | "Your own database rows are trusted" is wrong for a table that stores what customers typed | Let a `@table` or column be declared `untrusted`; reading it returns `Untrusted<T>` |
| F-17 | signup | `SignupForm.password` is a plain `string`, because `requires` on a `Secret<string>` would have to read the secret. So the password can reach logs | Let `requires` read a `Secret` (rules run in the type's own code), so password fields can be `Secret<string>` |
| F-18 | carrier outbox | `BookWithRetryOp` holds a `dyn IShipper` borrow inside a long-lived `Mutex<Outbox>` | Specify what `Op` values may capture: owned values only, or borrows whose region outlives the container |
| F-28 | signup | Argon2 (64 MiB) on a request fiber stalls the event loop. Nothing in the effect system marks CPU-heavy calls, and I only caught it in review | Add a `@blocking`-style marker on functions like `hashPassword` that warns when called outside `Io.offload` (the spec already treats `[Blocking]` externs this way) |

## Added in the double-check pass

| # | Where | Issue | Proposal |
|---|---|---|---|
| F-31 | money, carrier | Static factories that name the value they return (`Money.zero(c)`, `Outbox.empty()`) are nouns or adjectives, which the verb rule would reject | Exempt static factories, following the spec's own `T.zero` |
| F-32 | checkout | The `shipped` state holds one `TrackingId`, so a multi-parcel order loses the others | `shipped(PaymentId payment, List<TrackingId> tracking)`, or a parcels table |
| F-33 | sodium, main | A `Secret<Span<u8>>` loaded by `@derive(Config)` stays in ordinary memory for the life of the process; only the `SecretKey` copy is guarded | Let `Secret` own its storage and wipe it on drop (make it `unique` for byte secrets) |
| F-34 | checkout, stats | `for (a, b) in xs` (tuple destructuring) looks like the rejected `for (x in xs)` input form. The checker confused the two | Spec rule: parentheses in a `for` head are a tuple pattern only when `in` is outside them |

## Type-first declarations revert (2026-10-22)

Round 3's "names first" now applies only to local bindings (`let w: Grams = 0g`). Declarations are type-first again, as in round 2: `public Grams shippingWeight(Order order)`, `void` when nothing is returned, `Sku sku` fields, `timeout(Millis after)` payloads. `-> T` and `name: Type` declarations are input forms that `bayan format` rewrites; the checker reports them with the rewritten line (`fail/arrow-return`, `name-first-param`, `name-first-field`, `name-first-payload`). Sūq was converted mechanically from those fix-its.

| # | Where | Issue | What Sūq assumes or proposes |
|---|---|---|---|
| F-35 | quote, packing, checkout, db | Where a state refinement goes on a type-first parameter | `Order order is .paid`, the same order as the receiver's `this is .paid` |
| F-36 | particles (§15) | Particles were specified for name-first parameters (`by policy: Policy`) | The particle leads: `by Policy policy`, `from Span<PriceLine> lines` |
| F-37 | sodium (`extern`, `SecretKey.ptr`) | A pointer type (`*mut u8 ptr`) puts `*` at the start of the line, which waqf reads as a continuation (§3) | The leading-operator rule doesn't apply inside a struct or `extern` body; the checker exempts it there. The parser needs the same rule |
| F-38 | `fail/fn-type-arrow` | A type-first field or parameter of function type puts its name after the effect clause (`Order -> void uses net onPaid`) | Parenthesize the function type so the name follows the closing paren (`(Order -> void uses net) onPaid`, GRAMMAR §5, §9); the snippet is a struct field in that form, with `=>` as the error. `check_round3.py` needed no change: it reads the parenthesized field as neither name-first nor type-first |
| F-39 | checker | `Grams w = 0g` is a field in a struct body and an input form in a function body; only the enclosing block tells them apart | The checker tracks block kinds: struct, union, interface, extension and `extern` bodies hold declarations; function bodies and the module level (scripts) hold statements |

## 2026-10-23 signature review and addendum 1

Sūq was moved to the signature review (HANDOFF §2, §3, §8, §11-§15, §17, §20, §24-§29) and addendum 1: clause order `where`, `becomes`, `fails`, `uses`; `states { … }` and `T.s` types; `until`/`through`; `rest`; `Panic.handler`; `span`/`copy()`/`List(seq)` instead of `asSpan()`/`toList()`; no `onX` stored function fields. F-35 and F-36 above are superseded where they show `Order order is .paid` (now `Order.paid order`) and `Span<PriceLine>` parameters (now `ReadOnlySpan`). `check_round3.py` gained 11 rules and `fail/` 11 snippets (`clause-order`, `on-prefix`, `to-as-call`, `to-member`, `bare-this`, `get-property`, `owned-copied`, `state-line`, `mode-particle`, `repeated-particle`, `unit-round`).

| # | Where | Issue | What Sūq assumes or proposes |
|---|---|---|---|
| F-40 | orders_store (`bulkCopy`) | §27 shows `bulkCopy<T>(rows, ignoreConstraints)`, but it is unclear whether `T` needs an id or `IEntity`; `LineRow` has none | `bulkCopy` takes any `@derive(Row)` struct; the old hand-written COPY loop is gone |
| F-41 | checkout (`OrderPaid`) | §26 declares an event type (`Event<Order.paid> uses …`) but not how a field of it is created | `OrderPaid()` builds an empty event in the `Storefront` literal; `raise(order)` fires it, `subscribe(by:)` returns a handle for `using let` |
| F-42 | packing (`sumLoad`) | `first..end` over a typed index (`BoxId`) is not specified for §15 `until` | A range over a typed index yields that index type; `sumLoad(from BoxId first, until BoxId end) expects first <= end` |
| F-43 | restock (`run`) | `run(Path, args:)` and `Arg(Path)` are assumed; §13 names `process` but §27 gives no signature | `run(Path("gzip"), args: [Arg(options.file)])`; the script infers `uses process` and `.process` is in the package capabilities |
| F-44 | checkout (`toJson`) | `patch.toJson()` is an instance `toX` call (addendum §3) | `Json.encode(patch)` |
| F-45 | order store, checkout | `toOrder`, `toLine`, `toDto` are instance `toX` members | `restoreOrder`, `restoreLine` (private helpers), and static factories `OrderDto.fromOrder`, `LineDto.fromLine` (addendum §3 keeps `fromX`) |
| F-46 | storefront, db, main | `uses net` on code that only talks to Postgres | `db` (an alias for `net, fs`) on the storefront and db functions; `main` drops its `uses` (inferred, bounded by the package capabilities); `Handler` keeps an explicit upper bound |
| F-47 | main (`shop.paid.subscribe`) | A subscription on a `var shop` borrows it for the handle's life | The handle is `using let audit`; `shop` is not mutated while it lives |
| F-48 | orders_store, accounts | `conn.scalar<i64>(…)` is gone; `query<T>(…).single()` replaces it, and a single-column tuple is unclear | `query<i64>` for one column and `query<(i64, i32)>` for two, with `for try (cents, qty) in …`; `tryFirst()` for optional rows |
| F-49 | order (`Fulfilled`) | Several states are written inline for parameters (`Order is .paid or .shipped`); a one-state parameter is `Order.paid order` | `type Fulfilled = Order is .paid or .shipped`, and `lookUpPayment(Order is .paid or .shipped)` is used as a parameter |
| F-50 | warehouse, pricing, carrier | `Span<T>` parameters accept a `List` only through `.span` | Read-only parameters are `ReadOnlySpan<T>`; callers pass `xs.span` or a list literal |
| F-51 | carrier (`billedWeight`) | `ceiling<Kilograms>()` on `Grams` returns a `Kilograms` value; whether it interpolates directly into a log string is unspecified | `log` interpolates it with its unit suffix |
| F-52 | stats, money (overloads, `rest`) | Overloads by parameter type (§24) and a `rest` tail after a labeled parameter | `Tally.record(ref this, Line)` beside `record(ref this, i32, for Sku)`; `Money.sum(Currency, rest ReadOnlySpan<Money>)`, called with values or `..all` |
| F-53 | checker | `rest` is not a particle but is exempt from the one-unlabeled-parameter rule (§15) | The checker skips a `rest` parameter in that count |

## Findings from building both versions

- **A domain bug in both versions.** Free shipping skipped the weight-band check, so a 25 kg order over RM150 was quoted as shippable. The Bayan "over 20kg" test was wrong in the same way, but on paper nothing runs. The C# test failed. Both are fixed: the band lookup always runs, and free shipping only zeroes the fee. Until the compiler runs, the C# baseline is the only executable check of Sūq's logic.
- **Effects caught a real omission.** `pay` needed `clock` because shipping retries wait on it. The handler type's upper bound had to grow to match. In C# this is invisible.

## Double-check pass: bugs fixed

A full re-read found these. Each one was fixed in both versions where it applied, and the shared logic bugs now have C# tests that fail without the fix.

| Area | Bug | Both versions? | Now tested |
|---|---|---|---|
| Security | No ownership check on `saveAddress`, `pay` or `track`: anyone could overwrite another order's delivery address | Yes | `Someone_elses_order_is_not_found` |
| Payments | `pay` charged the line total, not the quote shown to the customer (shipping, tax and discount were missing) | Yes | `Pay_charges_the_quoted_total_then_ships` (RM 61.56) |
| Carrier | The outbox sweep dropped bookings that failed again, and nothing ran the sweep | Yes | `A_booking_that_failed_is_retried_and_recorded` |
| Carrier | A retried booking that succeeded never marked the order shipped | Yes | Same test |
| Carrier | Bayan: callers had to hold the `Mutex` guard across network calls (a §13.4 compile error) | Bayan only | Checked by hand |
| Catalog | `inStock` was always true: out-of-stock products were filtered out before it was set | Yes | `Search_matches_wildcards_literally_and_lists_out_of_stock` |
| Catalog | `%` and `_` in the search term acted as `ILIKE` wildcards | Yes | Same test |
| Webhooks | The body was read in full before any size check | C# (Bayan had no cap at all) | Checked by hand |
| Money | `multiply`: the `+ 5000` rounding step could overflow outside `checked`. C# wrapped silently; Bayan trapped instead of failing | Yes | Checked by hand |
| Pricing | `Policy` allowed amounts in other currencies, which were then compared by raw cents | Yes | Rules and constructor checks |
| Payments | Bayan never called `sodium_init()` | Bayan only | Checked by hand |
| Restock | Line numbers in errors skipped comments and blank lines | Bayan only | Checked by hand |
| Language rules | About 20 functions named with nouns (`bestDiscount`, `smallestBox`, `times`, …) broke the verb rule the audit enforced | Bayan only | Renamed |
| Language rules | Types and fields used across modules weren't `public`, so struct literals in `main` and field reads in checkout wouldn't compile | Bayan only | Checked by hand |
| Language rules | `List` passed where a `Span` was expected without `.asSpan` | Bayan only | Checked by hand |
| Warehouse | `measureRoom` used `-|` to clamp at 0 g, but on a signed type `-|` saturates at the minimum (`100 -| 250` is -150). Found while writing Sūq-0 | Bayan only (the C# used `Math.Max`) | Sūq-0 golden prints both cases |
