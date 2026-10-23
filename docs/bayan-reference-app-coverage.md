# Sūq — feature coverage

Which `suq/` files exercise each feature that HANDOFF §21 (`docs/bayan-design-round-3.md`) asks Sūq to cover. The tree holds the paper storefront, order lifecycle and packing parts (`suq/README.md`); the other parts exist only in `docs/suq-reference-app.zip`, whose own coverage matrix is the full one. Until the compiler implements `bayan features --used`, this table is kept by hand.

## Parts

| Part (HANDOFF §21) | Files |
|---|---|
| Storefront HTTP API | `suq/storefront/accounts.byn`, `suq/storefront/catalog.byn`, `suq/storefront/checkout.byn` |
| Order lifecycle | `suq/orders/order.byn`, with the lifecycle handlers in `suq/storefront/checkout.byn` |
| Warehouse packing | `suq/packing/packing.byn` |
| Carrier integration, Database, Payments, Admin scripts, Libraries, Packages | Gap: zip only |

## Features

| Feature (HANDOFF §) | Files |
|---|---|
| Derivations, `@derive` (§4, §16) | all five files |
| `requires`, `with` re-check (§11) | `suq/orders/order.byn`, `suq/storefront/accounts.byn` |
| `T.Patch` (§11) | `suq/storefront/checkout.byn`, `suq/storefront/accounts.byn` |
| `Untrusted<T>` (§17) | `suq/storefront/accounts.byn`, `suq/storefront/catalog.byn`, `suq/storefront/checkout.byn` |
| Sink types `sql"…"`, `Html`, `Url` (§17) | `suq/storefront/catalog.byn`, `suq/storefront/checkout.byn`, `suq/storefront/accounts.byn` (`sql`) |
| `except` on sequences (§7) | `suq/orders/order.byn`, `suq/packing/packing.byn`, `suq/storefront/catalog.byn`, `suq/storefront/checkout.byn` |
| Named patterns (§2) | `suq/orders/order.byn` (`inactive`), `suq/storefront/catalog.byn` (`hidden`) |
| Typestate: `states { … }`, `this is .s`, `becomes`/`become` (§14) | `suq/orders/order.byn` |
| `T.s` state types (§14) | `suq/orders/order.byn`, `suq/storefront/checkout.byn`, `suq/packing/packing.byn` |
| `union error`, `fail .x` (§2, §7) | `suq/orders/order.byn`, `suq/packing/packing.byn`, `suq/storefront/accounts.byn` |
| Exhaustive switches | `suq/orders/order.byn` (`statusLabel`), `suq/storefront/accounts.byn`, `suq/storefront/checkout.byn` |
| `defer failing` (§19) | `suq/storefront/checkout.byn` |
| Events (§26) | `suq/storefront/checkout.byn` (`OrderPaid`) |
| Units `g`, `kg` (§8) | `suq/packing/packing.byn`, `suq/orders/order.byn` |
| Typed indices (§9) | `suq/packing/packing.byn` (`BoxId`), `.indices` also in `suq/orders/order.byn`, `suq/storefront/checkout.byn` |
| Zero-allocation hot path, `@budget` bench | `suq/packing/packing.byn` (`@budget(allocs: 0)`) |
| `expects`, `until` particle (§11, §15) | `suq/packing/packing.byn` (`sumLoad`) |
| Argument particles (§15) | `suq/packing/packing.byn`, `suq/storefront/accounts.byn`, `suq/storefront/catalog.byn`, `suq/storefront/checkout.byn` |
| Effects `uses` (§13) | all five files |
| Ambient `log` (§13) | `suq/storefront/accounts.byn`, `suq/storefront/checkout.byn` |
| `struct unique` (§2) | `suq/storefront/checkout.byn` (`Storefront`) |
| `type distinct` (§2) | `suq/packing/packing.byn` |
| Postfix and block `unless` (§5) | `suq/orders/order.byn`, `suq/storefront/checkout.byn` |
| `///` docs (§6) | all five files |
| `rune` literals (§10) | `suq/storefront/catalog.byn` |
| `try` scope (§18), `?? return`, `for try`, `while let` | all five files (`try`); `suq/packing/packing.byn`, `suq/storefront/catalog.byn`, `suq/storefront/checkout.byn` |
| Derived forms `tryX`, `xInto`, `XOp` | `suq/packing/packing.byn`, `suq/storefront/catalog.byn`, `suq/storefront/checkout.byn` (`BookWithRetryOp`) |
| Extensions, interfaces | `suq/storefront/checkout.byn` |
| Fibers `async`/`await`, `Io.offload` | `suq/storefront/checkout.byn`, `suq/storefront/accounts.byn` |
| `never` return type | `suq/storefront/checkout.byn` (`sweepOutbox`) |
| comptime | `suq/packing/packing.byn` (`comptime sample(40)`) |
| Overflow `*%`, `+%` | `suq/packing/packing.byn` |
| Views and copies (`.span`, `.copy()`) | `suq/packing/packing.byn`, `suq/storefront/catalog.byn`, `suq/storefront/checkout.byn` |
| Tests | `suq/orders/order.byn`, `suq/packing/packing.byn`, `suq/storefront/accounts.byn`, `suq/storefront/catalog.byn` |
| Waqf termination, type-first declarations, `let`/`var` locals (§2, §3) | all five files |
| Compile-fail corpus (§21 method 2) | `suq/fail/README.md`, 53 snippets in `suq/fail/` |

## Gaps

Not used by any file in `suq/`. Items marked "zip" are exercised in `docs/suq-reference-app.zip`; the rest are in the zip's own "Not yet exercised" list.

- zip: `rest` parameters and overloads (§24); unit rounding `floor`/`ceiling`/`rounded<Unit>()` (§8); word booleans `not`/`and`/`or` in code (§5, `style.booleans` lives in `package.byn`); doctests; `@cases`; laws; generics, `embed`, interface default methods, `Set` of a union; effect capabilities and the pure `pricing` package; `provide` (§13); `Secret<T>` and `because` bypasses (§4, §16); FFI, `unique` resources freed with `move this`; `std.db` shape, `Row`, binary COPY, transactions, regions; deadlines and fiber groups; scripting mode, `@derive(Args)`, `@derive(Config)`; `Panic.handler`; `primitives: .sized`; the C# baseline `suq-csharp/`.
- nowhere: `defer panicking`, `if likely`/`if never`, `while try`, `do … while`, `T.Id`, `T.Hasher`, `T.Comparer`, `T.<Verb>ed` events, comptime source generators that return declarations, library-defined diagnostics, channels and `Io.first`, atomics, `Io.Simulated`, slicing, `+|`, `-|`, `rune` as a declared type, `mutableSpan()`, `rows.all()`, `Panic` in a test.

## Per-milestone status

The v0.1 compiler does not implement round 3, so no Sūq file compiles yet (the paper stage of §21).
