# Sūq round-3 review: response

Input: `docs/suq-agent-brief.md` (on `dev`) Parts A, B, C, E; `docs/bayan-reference-app-friction.md` (on `dev`). Docs changed on `feat/design-round-3`: `docs/bayan-design-round-3.md` (HANDOFF), `docs/plans/2026-10-22-002-design-round-3.md` (plan), `docs/bayan-spec.md` (SPEC), `docs/bayan-grammar.md` (GRAMMAR). Line numbers are after the change unless marked "old"; old lines are the SPEC and GRAMMAR before the review commit.

## Part A

Where the docs had decided an item differently, the brief's decision was applied and the conflict recorded in the plan's Conflicts table (C22–C27, plan `:172-177`). The new codes, BYN1056 and BYN1057, were checked free across `docs/` and `src/` on this branch and `.worktrees/feat-*/docs/specs/`. The plan entry for the decisions is §22 (`:135-139`).

**A1. `defer` modifier evaluated at registration.** Confirmed by the user: same rule as `unless`; `defer { if c { … } }` is the exit-time form.
- Changed: SPEC §7.6 `:869` (adds "once", the `if`/`unless` rule and the exit-time form); GRAMMAR §2 `:42`; HANDOFF §19 `:393`, decisions list `:463`; plan §19 `:117-121` (adds golden `defer-modifier-registration.byn` from brief D7), J20 `:200`.
- Conflict: none in the docs. SPEC old `:854` already said "evaluated when the `defer` is **registered**, and its value is captured", as did HANDOFF `:393` and plan J20. Only the compiler differs (it evaluates at exit); J20 changes it in 5.3a.
- J step: J20.

**A2. `fail .x` for errors.**
- Changed:
  - SPEC: §2.4 `:110` keyword `fail`, `:126` note; §3.10 example `:291-292`; §7.3 `:810`, `:815-816`; §7.9 `:956`; §8 bullet `:983` and note `:998`; §21.5 row `:1688`.
  - GRAMMAR: §3 row `:88`; §4 guard row `:111`, example `:117`, paragraph `:120`, tool list `:126`.
  - HANDOFF: §20 row `:409`; decisions list `:464`.
  - Plan: §22, C23, J28, step 5.3f.
- Conflict (C23):
  - SPEC old `:288-289`: "`return .alreadyShipped if status is .shipped`" and "`return .empty if isEmpty`".
  - SPEC old `:941`: "`return .x unless …` (business outcomes)".
  - GRAMMAR old `:121`: "`return .x unless …` (a business outcome)".
- New code: BYN1056 `ReturnErrorInput`, "`return .{0}` returns an error; write `fail .{0}`".
  - Label at `return`.
  - Help: "leave with the error: `fail .{0}`".
  - Fix: yes.
  - Raised only when the function fails and its return type has no case `.{0}`.
- Reused codes: BYN2603 for a case missing from `fails`; BYN2604 for `fail` in a function that cannot fail.
- J step: J28.

**A3. Whole-value binding in a union arm.**
- Changed: SPEC §6.5 `:694` and note `:696`; HANDOFF `:465`; plan §22, J29.
- Conflict: none. Positional binding stays; SPEC old §6.5 showed only positional (`msg is .text(let body)`).
- J step: J29.

**A4. Conversions `T(x)`, `T.tryFrom(x)`.**
- Changed:
  - SPEC: §3.4 `:214`; §4.3 `:537`; §6.2 `:651`, `:654`, `:657`, `:663`, note `:667`; §21.5 row `:1689`; §25 `:1761`.
  - GRAMMAR: §3 row `:89`.
  - HANDOFF: §20 row `:410`; decisions list `:466`.
  - Plan: §22, C22, J30.
- Conflict (C22):
  - SPEC old `:646`: "Narrowing `(u8)x` | Explicit; traps in every build mode if out of range".
  - SPEC old `:649`: "`(Seconds)raw`, `(i64)t`".
  - SPEC old `:652`: "`(T)x` calls `IFrom<T, U>.from` (infallible). Fallible conversions use `ITryFrom` and `try T.from(x)`".
  - SPEC old `:211`: "Wrap and unwrap with casts: `(Seconds)raw`, `(i64)t`".
  - SPEC old `:532`: "`(Duration)(h * 3600s)`".
  - SPEC old `:1738`: "Checked narrowing".
- New code: BYN1057 `CastInput`, "`({0})x` is an input form; write `{0}(x)`".
  - Label at the parenthesized type.
  - Help: "call the conversion: `{0}(x)`".
  - Fix: yes.
- J step: J30.

**A5. `s.copy()`, `s.copy(to: arena)`.**
- Changed: SPEC §3.2 `:178` and note `:194`; §9.4 rule 3 `:1035`; HANDOFF `:467`; plan §22, J31.
- Conflict: none.
  - SPEC old `:1017` "Fix: `.copy(to: alloc)` or `move` into an `Owned`" is extended, not reversed.
  - The docs had no `owned()`.
- J step: J31.

**A6. `assert f() fails E.case`.**
- Changed: SPEC §20 `:1567` and note `:1588`; HANDOFF `:468`; plan §22, C24, J32, §6 Deferred `:272`.
- Conflict (C24):
  - Plan old §6: "Round-2 pending items stay pending: … §16 `assert`".
  - HANDOFF Deferred `:473`: "String `+` and fluent asserts".
  - Resolution: only the statement form lands; the fluent API stays deferred.
- Reused codes: BYN2605 (the call cannot fail); BYN2411 (`E` has no such case).
- J step: J32.

**A7. Adopt implemented behaviour.**
- Changed:
  - SPEC §3.11 `:318-319` and note `:323` (F-12, F-14; BYN2780 named).
  - SPEC §6.2 `:657` (F-19).
  - HANDOFF `:469`.
  - Plan §22, J33.
- F-26 was already in SPEC `:98` ("an empty map is `[]` like every empty collection").
- Conflict: none.
  - SPEC old `:315` "A struct holding a `List` is therefore move-only" is extended to any `unique` field.
  - SPEC old `:652` is part of C22.
- J step: J33 (docs only).

**BYN1049.** Retired, never reused: it duplicated BYN1007 `UnlessNegated`, and the lower, earlier code survives.
- Recorded in:
  - plan legend `:7`, §5 (c) `:36`, C25, J27 `:207`, J34 `:214`, §7 total `:293` ("2 retired (1032, 1049)");
  - SPEC §21.5 `:1692`.
- BYN1049 was never raised, so it has no golden. It keeps a `RetiredUnlessNot` descriptor and a "No longer reported" page (`docs/diagnostics/BYN1049.md`, 15fda4c).

**Other new codes.** These come from Part B and are listed in plan §23 (`:141-145`).
- BYN7005 `StateLiteralNeedsState`: J37, step 5.3c.
- BYN8005 `SqlArgumentCount`: plan allocation, reserved; J40.
- Totals: 52 new (18 syntax, 12 types, 5 grammar, 7 effects, 5 typestate, 5 security) and 9 reserved.

## Part B

Plan entry: §23. Each step's text is in plan J35–J53 (`:215-233`); F-07 extends J19 (`:199`).

| F-# | Plan step | Docs answer it? | What they say | Recommendation |
|---|---|---|---|---|
| F-02 | new J35 (spec-only) | Partly | SPEC §20 `:1571` "`bench "name" { measure expr }` reports time per op, allocations per op and variance … Not in v0.1." Setup is not mentioned | Agree with Sūq: statements before `measure` are untimed setup; `measure` takes an expression or block, once per `bench` |
| F-03 | new J36 (no syntax) | No | SPEC §20 `:1573` "Laws are opt-in, never imposed, and never feed the optimizer"; no declaration form, no level setting | Defer the syntax and the tested/exhaustive/proved level to a later round; Sūq writes no `law` in v0.1 code |
| F-05, F-06 | new J37 (5.3c), BYN7005 | No | SPEC §3.17 `:474-484` declares states and transitions; nothing on literals or the initial state | `T.state { fields, payload }` (Sūq's `Order.paid { … }`); a plain `T { … }` of a struct with states is BYN7005, since no state is a safe default |
| F-07 | J19, amended (5.3b) | Partly | SPEC §7.9 `:929` "Updates go through `with`, which re-checks: `try f with { password: p }`"; plan J19 "`with` re-checks only structs that have at least one `requires`" | Agree with Sūq; J19 now makes `with` infallible when no changed field has a rule and no struct-level rule reads one |
| F-08 | new J38 (5.3b) | Partly | SPEC §7.9 `:927` "a constant argument is checked at compile time" (distinct types); silent on `try` and on `const`s | Constant-only construction needs no `try`; a violation is BYN2321 at compile time |
| F-09 | new J39 (5.3e) | No | SPEC §10 `:1096` "Every other required parameter takes a particle (BYN5006)", no exemptions | Exempt `extern` declarations, operator declarations, union and state payloads (fields, not parameters) and laws |
| F-10 | new J40 (spec-only), BYN8005 reserved | Partly | SPEC §15.1 `:1331` `db.query(sql"SELECT * FROM users WHERE name = $1", term)   // literal plus parameters`; no count check | Agree with Sūq: variadic, count must equal the highest `$n` |
| F-11 | new J41 (5.3e) | Partly | SPEC §15.1 `:1351` "Facts like `isEmpty`, `length` and comparisons are clean, so branching on untrusted data is allowed." `Untrusted<bool>` itself is not mentioned | Allow it directly as a condition; a bool cannot reach a sink |
| F-13 | new J42 (spec-only); conflict C27 | Yes, differently | HANDOFF §17 `:380` and SPEC §15.1 `:1350` "the program's own database rows are trusted" | Keep rows trusted by default; `@untrusted` on a `Row` field or table makes reads `Untrusted<T>` (stored XSS is already blocked by sink types, SPEC `:1324`) |
| F-16 | new J43 (5.3b) | No | SPEC §7.9 `:927` "Construction fails with `ValidationError<T> { violations: List<Violation<T>> }`"; no rule on dependent struct-level rules | Agree with Sūq: skip a struct-level rule that reads a failed field |
| F-17 | new J44 (spec-only) | No | SPEC §15.2 `:1364` `Secret<T>`: "no `==`, only `equalsConstantTime(other)`"; nothing on `requires` | Allow a field rule to read a `Secret` (`it.runeCount`); violations carry no values, so nothing leaks |
| F-18 | new J45 (spec sentence) | Yes, in general | SPEC §9.4 `:1035` "A borrow cannot be stored anywhere that outlives its region." | An `xOp` value follows the struct rule: borrowed arguments bind it to their region; a long-lived container needs owned arguments |
| F-21 | new J46 (5.1b) | No | SPEC §3.10 `:299` "A computed property takes a type like a field: `public isEmpty: bool => lines.isEmpty`" (expression body only) | Allow `public total: Money { … }` with the same no-`uses` rule; after the 2026-10-22 type-first revert this is `public Money total { … }` |
| F-22 | new J47 (5.4) | No | SPEC §16 `:1390` "`sort()`, `sort(by)`, `reverse()`" with no stability stated | Agree: `sort` is stable; no unstable sort in v0.1 |
| F-23 | none | Yes | SPEC §13.2 `:1233-1236` "**Deadlines: `provide timeout: d { … }`.** … Expiry fails the suspended operation with `TimeoutError`." | Disagree with adding `Io.group(timeout:)`: wrap the group in `provide timeout:` and map `TimeoutError` once with `catch` |
| F-24, F-25 | none (O2, J26) | Yes | SPEC §3.4 `:204` `unit Bytes(B): i64`; `:218` "Counts are plain `i64`: `s.length`, `s.runeCount` and collection `length`. The `Bytes` and `Runes` units apply only to slice offsets."; `:226` "`string.bytes` is a `ReadOnlySpan<u8>`" | Agree; `Bytes` is the unit, data is `Span<u8>` |
| F-27 | none | Yes | SPEC §5.2 `:584` "`xOp(…)` … Derived; type `Op<T, E>`"; no `@forms` attribute exists in SPEC | The value type is `Op<T, E>`, not a per-function `BookWithRetryOp`; derived forms come from the pattern, so drop `@forms(Op)` |
| F-28 | new J48 (spec-only) | Partly | SPEC §13.1 `:1222` "Blocking work is offloaded automatically (DNS, file I/O on epoll, `@blocking` externs). Debug builds warn when a fiber runs over 50 ms without yielding." | Allow `@blocking` on Bayan functions, offloaded like externs; disagree with a warning code, since offload is automatic and the 50 ms warning exists |
| F-29 | new J49 (5.3a) | Partly | SPEC §8 `:985` "`f() catch switch { … }` (exhaustive; each arm is a pattern over the error union and binds its own name, `DbError e => …`)"; BYN2611 for `catch` blocks | An arm is an expression; a `return …` arm has type `never` and is allowed; a block arm must leave (BYN2611) |
| F-30 | new J50 (spec-only) | Partly | SPEC §18 `:1446` `extern c { … }` with `@link("sqlite3")` (`:1445`); §3.11 `:314` "Cleanup is derived (disposes fields in reverse declaration order)" | Keep `extern c { … }` plus `@link`, not `extern "lib"`. A user cleanup member is an open question; conservative choice `dispose(ref this)` |
| F-31 | new J51 (GRAMMAR note) | Partly | GRAMMAR §1 `:18` tree 2 "Plain verb: `join`, `trim`, `split`"; BYN5101 (`:351`) checks only `ref this` members | Agree: note that a static factory naming its result is a noun or adjective. The SPEC has no `T.zero` (the friction log cites one) |
| F-33 | new J52 (spec-only) | Partly | SPEC §15.2 `:1364` "zeroed on dispose" | Agree: `Secret` owns its value; a byte secret holds an owning buffer, so it is `unique` (A7) |
| F-34 | new J53 (5.2a) | Partly | SPEC §7.4 `:832-833` `for (i, x) in xs.indexed()`, `for (key, value) in prices`; §21.5 `:1665` "`for (let x in xs)` … BYN1019" | Agree: parentheses are a tuple pattern when `in` is outside them |

## Part C

| Item | GRAMMAR/SPEC reference | Matches or differs |
|---|---|---|
| 1 Waqf termination | SPEC §2 `:135-136`, `:142`; GRAMMAR §6 `:178-179`, `:193` | Matches |
| 2 Head-first declarations | SPEC `:314`, `:405-406`, `:411`, `:1672`, `:1679`; GRAMMAR `:75`, `:97`, `:156-157` | Matches for the kind keywords. Declaration order changed after this review: see "Revert: type-first declarations" below |
| 3 `@attr`, `because` list | SPEC §4.4 `:544-551`; GRAMMAR §7 `:203-212` | Differs: no `Secret.expose()`. SPEC `:547-551`: "`@allow(X) because "…"`", "`@json(expose) because "…"` on a `Secret` field (§15.2)", "`xUnchecked() because "…"`, including `trustUnchecked()` (§15.1)", "`unsafe because "…" { … }`". A secret is exposed per field with `@json(expose) because`, not per call |
| 4 `requires it`, `at .field` | SPEC §7.9 `:906-920`; GRAMMAR `:83` | Matches (HANDOFF §11 body still shows `on`, superseded by its "Resolved 2026-10-22" paragraph) |
| 5 `->` in function types | SPEC §3.16 `:457`, §14 `:1287`; GRAMMAR §9 `:259`, `:266` | Matches. 007 Q115 makes a stored function type a field (`(Order -> void uses net) onPaid`) BYN2822 in 007's scope |
| 6 `case not .x` | SPEC §6.8 `:756`, `:1684`; GRAMMAR `:84` | Matches |
| 7 Unit conversions | SPEC §3.4 `:213`, `:215` | Matches |
| 8 `log(msg, as: .level)` | SPEC §5.2 `:607`; GRAMMAR §10 `:299` | Matches |
| 9 `provide` | SPEC §7.7 `:895`, §14 `:1299-1309`; GRAMMAR §9 `:272-274` | Matches |
| 10 No `noalloc`/`nosuspend`/`suspends` | SPEC `:45`, `:1271`, `:1687`; GRAMMAR `:85` | Matches |
| 11 `format()`/`formatInto`, no string `+` | SPEC §3.2 `:184`, §6.6 `:720`, `:1675`; GRAMMAR `:23`, `:99` | Matches |
| O1 one case per line | SPEC `:144`, `:988`; GRAMMAR `:195` | Matches |
| O2 counts are plain integers | SPEC §3.4 `:218`, `:1805` | Matches for counts. Wording is narrower on units: "The `Bytes` and `Runes` units apply only to slice offsets" (`:218`); sizes keep units elsewhere (`:215` `size.convert(to: KiB)`) |
| O3 field or struct level by content | SPEC §7.9 `:926` | Matches |
| O4 `at .field` | SPEC §7.9 `:919-920` | Matches |
| O5 `->` in function types | SPEC §3.16 `:457`, `:1805`; GRAMMAR `:266` | Matches |
| O6 `for` particle | SPEC §10 `:1098`; GRAMMAR §8 `:240` | Matches |
| O7 `Money` traps, `tryAdd`, `add(…) fails` | SPEC §3.4 `:219` | Matches |
| O8 `state` contextual | SPEC §3.17 `:501` | Matches |
| O9 no bare literals for distinct types | SPEC §3.4 `:214` (`ParcelId(0)`, BYN3204) | Matches |
| O10 trusted decode | SPEC §15.1 `:1353` | Matches |

## Revert: type-first declarations (2026-10-22)

At the user's request, round-3 "names first" (HANDOFF §2, audit flaw #8) is reverted for declarations: it was meant only for local bindings. This supersedes every names-first item above that concerns fields, parameters, returns or payloads, including the F-21 recommendation and Part C item 2.
- Declarations are type-first again (round-2 order): `public Grams shippingWeight(Order order)`, `void` when nothing is returned, `(Order order)`, `(ref this, Line line)`, field `Sku sku`, payload `timeout(Millis after)`.
- Local bindings stay name-first: `let w = 0g`, `let w: Grams = 0g`, `var w: Grams = 0g`; `Grams w = 0g` in a function body stays an input form.
- `f() -> T`, `f(x: T)`, field `x: T` and payload `case(x: T)` are input forms that `bayan format` rewrites to type-first (BYN1043, now two message forms).
- Changed: HANDOFF §2; SPEC every declaration example, and the rules in §0, §1, §3.10, §3.16, §7.1, §18, §21.5, §25, Appendix A; GRAMMAR §1, §3, §5, §8–§10; STYLE; plan §2, §20, J54, step 5.1b. The plan entry is J54, with its conflicts list.
- The Sūq app (`docs/suq-reference-app.zip`) is updated separately.

## Part D

filled in by the Sūq-0 golden work on feat/design-round-2

## Attachments

- Round-3 HANDOFF: `docs/bayan-design-round-3.md`
- Round-3 plan (J1–J53): `docs/plans/2026-10-22-002-design-round-3.md`
- Q89–Q113: `.worktrees/feat-006-interfaces-iterators/docs/specs/006-interfaces-iterators/006-plan.md:102-126`
- Q114–Q131: `.worktrees/feat-007-lambdas/docs/specs/007-lambdas/007-plan.md:105-122`
- The spec files' "Open questions" sections say "(none; gaps are recorded as handoff Questions with the conservative choice, per AGENTS.md)" (`006-spec.md:78`, `007-spec.md:86`).

### Appendix: Q89–Q131

Those branches are not merged, so the list is copied here. The conservative choice condenses the plan text; "Kept", "Amended" and "New" are the plan's own status.

| Q# | Question | Conservative choice | Spec |
|---|---|---|---|
| Q89 | Where does `where` go? | Functions `… (params) [fails …] where T : I, J { }`; types `struct S<T> : I where T : J {`. Kept | 006 |
| Q90 | How do `current` and `get` return items? | By value; the loop variable is a `let` copy; `unique` items are BYN2022 until ref-returning getters exist; the member is `get(i64 index)`. Kept, amended | 006 |
| Q91 | How are default methods compiled? | Cloned per conformer; body checked once on the interface with `Self` as a constrained type parameter. Kept | 006 |
| Q92 | Vtable layout? | A slot per method and getter, defaults included; one `static const` table per (conformer, interface instance); no sharing. Kept | 006 |
| Q93 | What is dyn-safe? | No generic methods, no associated types; the interface's own name as a type is BYN2816. Amended | 006 |
| Q94 | Enum conformance? | Superseded by Q100 | 006 |
| Q95 | Generic methods in interfaces? | Declarable only so BYN2808 can name them; calling one is BYN2022. Kept | 006 |
| Q96 | Where does implicit `ToDynRef` apply? | Arguments, `let`/`var` with a declared type, returns; no `dyn ref J` → `dyn ref I`; a `dyn ref` field is BYN3001. Amended | 006 |
| Q97 | How are built-in interfaces provided? | Synthesized, not prelude source; redeclaration is a duplicate name; the pair is `IEnumerator` and `IIndexable`. Amended | 006 |
| Q98 | Specialization ranking? | A conformer's own member beats the default; no other ranking. Kept | 006 |
| Q99 | What may `for … in` iterate? | `IEnumerator` (rvalue spilled to a hidden `var`; a named place must be a writable `var`) and `IIndexable` (`0..count`, `count` read once, iterable loaned for the loop) | 006 |
| Q100 | Which types may conform? | Structs only; a base on a union or enum is BYN2815 "not supported yet" | 006 |
| Q101 | Which members mutate? | Read-only unless `ref this`; a witness must match `ref this` both ways (BYN2814); `dyn ref readonly I` allows only non-`ref this` members (BYN2813). Superseded by round 2 C1 | 006 |
| Q102 | How is generated C checked? | L012 compiles standalone with `-Wall -Werror`; `Toolchain.cs:197` keeps `-w` | 006 |
| Q103 | Do primitives conform to user interfaces? | No; BYN2807's help says so | 006 |
| Q104 | When is each code registered? | In the task that first raises it, with its diag golden and page; `EveryCodeAppearsInAGoldenCase` stays green | 006 |
| Q105 | Associated types? | `type Item;` only, no bounds | 006 |
| Q106 | May criteria rely on TypedDump? | No; run/diag goldens or unit tests | 006 |
| Q107 | How is `this` plumbed in defaults? | `FunctionSymbol.Owner` stays `StructSymbol?`; a default template has `Interface` set and `Owner = null`; `BoundThis` takes a `TypeSymbol` | 006 |
| Q108 | When is conformance checked for generics? | BYN2807 checked inside the signature's `Instantiating` call; generic conformers once on the template; instance witnesses built in `InstantiateType` | 006 |
| Q109 | Who decides `ToDynRef` ownership? | `Conversions.Implicit` from conformances and constraints; the operand stays a place; a writable target needs a writable source | 006 |
| Q110 | How are vtables emitted? | Slots follow `Signature`'s `fails` convention; thunks through `Helper`; vtables in a separate data list; `dyn ref readonly I` shares `byn_dyn_<I>` | 006 |
| Q111 | What about a `let` enumerator? | `CheckWritable` with the abstract `moveNext` as callee; BYN2104 with the fix to `var`. Amended | 006 |
| Q112 | How is an associated type bound? | `type Item = T` in a struct body is the existing `TypeAliasDecl`, matched by name to the base's associated type | 006 |
| Q113 | Interface name as a plain type, and corner cases? | BYN2816; default templates never lowered; vtables emitted on first `ToDynRef`; `IIndexable` wins over `IEnumerator`; keyword bounds stay parse errors | 006 |
| Q114 | How is a function value represented? | (code pointer, environment pointer); null environment when nothing is captured; captures live in the creating frame; `FunctionType.IsBorrow` keeps it there. Kept | 007 |
| Q115 | Where may function types appear? | Parameters, `type` alias targets, locals; anywhere stored (field, return, type argument, collection element) is BYN2822, including `(Order -> void uses net) onPaid`. Kept | 007 |
| Q116 | What may a lambda capture? | Immutable locals and unmodified parameters, by copy; anything mutable, `ref`/`out`/`in` parameters and `this` are BYN2821. Kept | 007 |
| Q117 | When is `it` a lambda? | An argument mentioning `it` where a one-parameter function type is expected; nested `it` is BYN2029; elsewhere BYN2001. Kept | 007 |
| Q118 | What may lambda parameters carry? | Names only; types, modifiers, particles and defaults are parse errors; arity is BYN2820; no expected type is BYN2218. Amended | 007 |
| Q119 | How do lambdas interact with inference? | Bound after type arguments are inferred and never contribute; a type parameter only they could fix is BYN2740. Kept | 007 |
| Q120 | `fails` on function types? | Parsed, typed and callable with `try`/`catch`; conversion needs equal error sets. Kept | 007 |
| Q121 | Grammar corners of `->`? | Right-associative; `?` and `[N]` bind tighter; `=>` in a function type is BYN1055 `FatArrowFunctionType` with a fix to `->` (round-3 plan C26; 007 docs updated in feat/007-lambdas ba89acd; BYN1050 is round-3 `RequiresValue`). Amended | 007 |
| Q122 | Generic aliases? | None; `type Predicate<T> = T -> bool` waits for them. Kept | 007 |
| Q123 | Which named functions are values? | Module-level non-generic functions only; others are BYN2824 with a wrap-in-a-lambda help. Kept | 007 |
| Q124 | Who registers BYN1028 and BYN1050? | 007's parser unless round 2 or round-3 5.2a registers them first (same collision as Q121, C26). Amended | 007 |
| Q125 | `.member` where a function is expected? | BYN2823 with a fix to `it.member`. Kept | 007 |
| Q126 | Lambda bodies? | Expression or block; control flow is the lambda's own; a non-`void` expression body for `void` is BYN3201; `dyn (A -> B)` is a parse error. Amended | 007 |
| Q127 | Built-ins that take lambdas? | Out of v0.1; codes BYN2820–2824, spare 2825–2829. Kept | 007 |
| Q128 | Effects on function-typed locals? | The stored rule: the type's `uses` is an upper bound (BYN6005 beyond it). New | 007 |
| Q129 | Particles and function values? | Function-typed parameters take particles; function types carry none; calls through a value are positional. New | 007 |
| Q130 | `it` inside `requires`? | The checked value; an `it` shorthand argument there is BYN2029. New | 007 |
| Q131 | Effects lowering for function values? | Follows round-3 step 5.3d; no convention of its own. New | 007 |
