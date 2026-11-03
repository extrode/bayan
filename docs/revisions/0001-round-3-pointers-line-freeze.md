# 0001 — Round 3 update: postfix pointers, BYN1030 retired, `#line` mapping, spec freeze

Date: 2026-10-24. Adopted by the user with the updated HANDOFF (`docs/bayan-design-round-3.md`) and plan (`docs/plans/2026-10-22-002-design-round-3.md`).

## Problem
- Pointer spelling was mixed: SPEC §18 used postfix `Sqlite3*`, the Sūq drafts used prefix `*u8` / `*mut u8` (handoff Question 120).
- BYN1030 rejected C# primitive names, so a `style.primitives: .csharp` project could not build, against §28 "the compiler accepts both" (handoff Question 140).
- Native debuggers stepped through generated C, not Bayan source.
- SPEC, GRAMMAR and STYLE had no change process once round 3's documentation lands.

## Decision
- Pointers are postfix `T*`, `T**`; `readonly T*` for a pointee that cannot be written (C `const`); prefix `*T`, `*mut T`, `*const T` are input forms, BYN1066 `PrefixPointerInput` with a fix-it (HANDOFF §2, plan "Pointers", C47, J91).
- BYN1030 retires with a tombstone ("retired: the rewrite moved to `bayan format` `style.primitives`; never reuse"); the C# names stay reserved words (HANDOFF §28, J92).
- The C emitter writes `#line N "file.byn"` before each statement's code; generated helpers carry none; a C compiler diagnostic in a user build is an internal compiler error with the C text attached (HANDOFF §30, J93).
- After the documentation deliverables 1–3, 5 and 9, SPEC/GRAMMAR/STYLE change only through `docs/revisions/NNNN-title.md` (HANDOFF §32, J94).

## Sections
HANDOFF §2, §24–§32 intro, §28, §30, §32, deliverables list (new item 4, renumbered 5–10), changelog. Plan header, §5 (c), new "Pointers" and "§32" items, C47, J76, J91–J94, row 5.2b, totals.

## Codes
New: BYN1066. Retired: BYN1030.

## Sūq files
Prefix pointers in the Sūq drafts (`suq/fail/drop-ref-this.byn:4` `*mut u8`) become BYN1066 input forms.

## Effect on work already done
- BYN1030 retirement: done on `chore/worker-r3-q140` (gate pending); tombstone text aligned to the wording above.
- SPEC §18 pointer bullet (`3f92bdf`): matches; BYN1066 replaces "allocated when pointer types are parsed".
- 5.2b (`d3f50d8`): unchanged; the plan now also gives it BYN1030, which the q140 step covers.
- C# names as reserved words: the lexer does not reserve them yet (work/todo.md).

## Diff

### HANDOFF `docs/bayan-design-round-3.md`
```diff
--- a/docs/bayan-design-round-3.md
+++ b/docs/bayan-design-round-3.md
@@ -4,7 +4,7 @@
 
 Bayan is a C#-derived, natively compiled language (no .NET runtime). `SPEC.md` exists, the round-2 HANDOFF has been applied or is in progress, and the v0.1 bootstrap compiler is being built.
 
-This handoff records every decision from design round 3, including the signature review of 2026-10-23 and its follow-ups (panic handler, views and copies, formatting, grammar-checker scope, the performance contract, references in structs). It **supplements** round 2: everything in round 2 still stands unless this document changes it, and this document wins on any conflict. Section numbers §22–§23 are used by the plan for the Sūq review and friction items; the sections added by the signature review are §24–§31. The changelog at the end lists what changed and when.
+This handoff records every decision from design round 3, including the signature review of 2026-10-23 and its follow-ups (panic handler, views and copies, formatting, grammar-checker scope, the performance contract, references in structs). It **supplements** round 2: everything in round 2 still stands unless this document changes it, and this document wins on any conflict. Section numbers §22–§23 are used by the plan for the Sūq review and friction items; the sections added by the signature review are §24–§32. The changelog at the end lists what changed and when.
 
 For each area: update `SPEC.md` (with a one-line rationale per change), update `GRAMMAR.md` and `STYLE.md` where named, implement in the compiler where the area is in v0.1 scope, add tests, and add diagnostics with `BYN####` codes and `bayan explain` entries. Use these code ranges: 1xxx syntax, 3xxx types, 5xxx grammar, 6xxx effects, 7xxx typestate, 8xxx security.
 
@@ -96,6 +96,15 @@
 - **A particle comes before the passing mode:** `to ref Span<u8> dst`.
 - **Function types are parenthesized when a name follows them**, in fields and parameters: `(Line -> Money uses net) pricer`, `by (Token -> bool) p`. An alias avoids the parentheses: `type Pricer = Line -> Money uses net`, then `Pricer pricer`.
 - **State types** are written `Order.paid` (one state) or `Order order is .paid or .shipped` (several, inline after the name). Details in §14.
+- **Pointers are postfix**, like `T?`: `Sqlite3* db`, `u8** argv`, and `readonly u8* data` for a pointer that cannot be written through (C's `const char*`). The type's head comes first and its modifiers follow (head-first, §1), and a declaration line always starts with a type name, never with `*`. Pointers appear only in `extern` declarations and `unsafe because` blocks:
+  ```csharp
+  extern c {
+      c_int sqlite3_open(cstr filename, out Sqlite3* db)
+      c_int sqlite3_close(Sqlite3* db)
+      void* sodium_malloc(usize size) uses alloc
+      c_int crypto_auth_hmacsha256_verify(readonly u8* tag, readonly u8* msg, u64 len, readonly u8* key)
+  }
+  ```
 
 ### The receiver
 
@@ -558,6 +567,7 @@
 | `params T[] items`, `params ReadOnlySpan<T> items`, `T... items` | `rest ReadOnlySpan<T> items` (§24) |
 | Interface property `Type name { get }` | `Type name` (§26) |
 | `A => B` as a function type | `A -> B` (O5) |
+| `*T`, `*mut T`, `*const T` pointers | `T*`, `T*`, `readonly T*` (§2) |
 
 Remove from the round-2 table: `if !x { … }` → `unless x { … }`.
 
@@ -907,7 +917,7 @@
 }
 ```
 
-- `.sized` is the default. `bayan format` normalizes each project to its spelling; the compiler accepts both and raises no diagnostic (style is never compiler-enforced, as for booleans).
+- `.sized` is the default. `bayan format` normalizes each project to its spelling; the compiler accepts both and raises no diagnostic (style is never compiler-enforced, as for booleans). BYN1030, which rejected the C# names as input forms, is retired with a tombstone ("retired: the rewrite moved to `bayan format` `style.primitives`; never reuse").
 - **Excluded:** `char` (16-bit in C#; it stays the input form for `rune`), `decimal` and `object` (no equivalent). `bool` and `string` are shared.
 - The C# names are reserved words in every project, whatever its style.
 - Tooling renders types in the reader's project style: diagnostics, hover, `bayan expand`, generated docs and `bayan explain`.
@@ -929,6 +939,7 @@
   public T max<T>(ReadOnlySpan<T> xs) where T : IOrder<T> { … }   // static: one copy per T
   public void print(rest ReadOnlySpan<Display> items) uses console  // dynamic: one copy, vtable calls
   ```
+- **Debug mapping (v0.0.1):** the emitted C carries `#line` directives that point back to the `.byn` file and line, so native debuggers (gdb, lldb, Visual Studio) step through Bayan source and report Bayan locations. A C compiler error that reaches the user is a Bayan compiler bug.
 - **Zero runtime cost by design:** typestate (erased), effects and capabilities (compile-time), `rest` spans (caller's stack), `Event.raise` (no allocation), units (plain integers), `T.Patch` and derivations (generated code, no reflection).
 
 ## 31. References in structs, trees and graphs
@@ -955,6 +966,15 @@
   ```
   Generational handles (`Handle<T>`, which catch a stale index after deletion at the cost of one compare per access) are added only when the selfhost AST or Sūq shows the need.
 
+## 32. Spec freeze and revisions
+
+When the agent finishes this round's documentation deliverables (1–3, 5 and 9), `SPEC.md`, `GRAMMAR.md` and `STYLE.md` are **frozen**:
+
+- Changes go only through numbered revisions in `docs/revisions/NNNN-title.md`. Each one states the problem, the decision, the sections changed, the diagnostic codes affected and the Sūq files affected.
+- A revision is applied to the spec in one commit, and the revision file stays as the record. Design rounds and addenda become revisions; no new HANDOFF-style documents.
+- The agent never changes the spec on its own. A conflict it finds goes into the end-of-run summary or a draft revision for the user to accept.
+- Sūq coverage CI (§21) is the gate: a revision that adds a feature also adds its Sūq use.
+
 ---
 
 ## Open questions (resolve before implementing these parts)
@@ -1013,20 +1033,21 @@
 
 ## Deliverables
 
-1. `SPEC.md` updated for §1–20 and §24–29, each change with a one-line rationale and its diagnostic codes.
+1. `SPEC.md` updated for §1–20 and §24–32, each change with a one-line rationale and its diagnostic codes.
 2. `GRAMMAR.md` and `STYLE.md` updated (§1, §5, §12, §13, §15, §28). In STYLE.md's member order, rename "state members" to "properties" and use the order in §26 (fields, `states` block, events, properties, static factories, methods). STYLE: `uses` after `fails`, the clause order of §2, and the `style.primitives` default.
 3. Sweep every code example in SPEC.md, GRAMMAR.md, STYLE.md and `docs/` to round-3 syntax: name-first local bindings, type-first declarations; no semicolons; `@` attributes; particles; `rune`; head-first declaration keywords; `states` blocks and `T.s` types; `rest`; `bayan format`.
-4. Verify collections use `length` for size and `count(p)` only for predicate counts (round 2 §11 lists `count` among the queries).
-5. Compiler changes, in this order:
+4. Freeze the three documents and create `docs/revisions/` with a README describing the revision format (§32).
+5. Verify collections use `length` for size and `count(p)` only for predicate counts (round 2 §11 lists `count` among the queries).
+6. Compiler changes, in this order:
    1. Lexer and parser: semicolon-free termination (§3), `@` attributes and `because` (§4), head-first declaration keywords, name-first local bindings and type-first declarations with their input forms (§2), receivers including `move this` (§2), `//`-only comments, `except` (§7), `states`/`becomes`/`become` and `T.s` types (§14), particles including `until`/`through` (§15), `provide` (§13), `rest` and named parameters after it (§24), lambda and loop bindings (§25), `expects` (§11).
    2. Input-form rejection with fix-its (§20), and `bayan format` rewrites including `style.booleans` and `style.primitives`.
    3. Semantics: unused-value error, `requires` at construction and `with` re-checking, `expects` (§11), typestate (§14), effects, aliases and capabilities (§13), overload checks (§24), unit conversion and literal rules (§8), typed indices (§9), trust layers (§17), `try` scope (§18), `defer` modifier capture (§19).
    4. Standard library: `rune`, `log`, `print` with `console`, particle names, `tryGet`, `remove`/`replace`, math names (§27), sink types, `T.rules`.
    5. `bayan format` (renamed), `bayan audit` and `bayan features --used`.
-6. Tests for each item: positive, negative, and diagnostic snapshots.
-7. Sūq: coverage doc, friction log, compile-fail corpus skeleton, and the paper versions of the first three parts (§21).
-8. Verification: confirm SPEC §9 states the §31 rule in so many words (a borrow stored in a struct field binds the struct to the referent's region and never outlives it); add any missing sentence, the two safety measures (§31 items 1–2), and the typed-index tree idiom to STYLE, since the selfhost parser's AST is the first large tree.
-9. An end-of-run summary: what changed, what was not done and why, spec conflicts that needed a judgment call, and the top friction-log items.
+7. Tests for each item: positive, negative, and diagnostic snapshots.
+8. Sūq: coverage doc, friction log, compile-fail corpus skeleton, and the paper versions of the first three parts (§21).
+9. Verification: confirm SPEC §9 states the §31 rule in so many words (a borrow stored in a struct field binds the struct to the referent's region and never outlives it); add any missing sentence, the two safety measures (§31 items 1–2), and the typed-index tree idiom to STYLE, since the selfhost parser's AST is the first large tree.
+10. An end-of-run summary: what changed, what was not done and why, spec conflicts that needed a judgment call, and the top friction-log items.
 
 ---
 
@@ -1042,5 +1063,5 @@
   - `expects` for preconditions beside `requires` types; validation errors as a composable `union error`; `Rule` payloads named; `at .password` in the example (§11).
   - Units: coarser conversions are `floor`/`ceiling`/`rounded` with a type argument; `round(to:)`, `roundUp`, `roundDown` and `convert(to:)` removed (§8).
   - New sections: overloading, defaults and `rest` (§24); lambdas, loops, identifiers (§25); interfaces, properties, events (§26); standard library shapes, including `std.db` (§27); primitive spellings (§28); `bayan format` (§29).
-  - Follow-ups: `Panic.handler` (§13); O11 and O12 resolved (§15, §26); views, copies and conversions without `toX`/`asX` (§12, §27); `string(x, as: Radix)` and the `utf16` view (§27); O13–O15 resolved (no `fromUtf8`; codecs for hex and base64; checker scope); project dictionary and plain-English diagnostics (§12); performance contract (§30); references in structs and typed-index trees (§31).
+  - Follow-ups: `Panic.handler` (§13); O11 and O12 resolved (§15, §26); views, copies and conversions without `toX`/`asX` (§12, §27); `string(x, as: Radix)` and the `utf16` view (§27); O13–O15 resolved (no `fromUtf8`; codecs for hex and base64; checker scope); project dictionary and plain-English diagnostics (§12); performance contract (§30); references in structs and typed-index trees (§31); postfix pointers and `readonly T*` (§2); BYN1030 retired (§28); `#line` debug mapping (§30); spec freeze and numbered revisions (§32).
   - Example fixes: `transfer` gains its receiver, `insert` gains `uses alloc`, `crc` takes `ReadOnlySpan<u8>`, `Date(year:month:day:)`, `FakeNet(label)`, the `Checkout` callback example replaced by a `Pricer` alias.
```

### Plan `docs/plans/2026-10-22-002-design-round-3.md`
```diff
--- a/docs/plans/2026-10-22-002-design-round-3.md
+++ b/docs/plans/2026-10-22-002-design-round-3.md
@@ -1,6 +1,6 @@
 # Design round 3 — Plan
 
-Input: `docs/bayan-design-round-3.md` (HANDOFF round 3; wins over round 2), `docs/bayan-design-round-2.md`, `docs/plans/2026-10-21-001-design-round-2.md`, `docs/suq-agent-brief.md` Parts A–B (§22, §23; wins over the round-3 docs). Tree: `e60ab6f`. O1–O10 are resolved in the HANDOFF ("Resolved 2026-10-22"). The HANDOFF signature review (2026-10-23) changes §2, §3, §8, §11–§15, §17, §20 and adds §24–§31; those changes are marked **R** below, with steps 5.1e and 5.3g, J55–J90, C28–C46. O11–O15 are resolved. Size is `length`; `count(p)` is a predicate count only. Status: approved 2026-10-23.
+Input: `docs/bayan-design-round-3.md` (HANDOFF round 3; wins over round 2), `docs/bayan-design-round-2.md`, `docs/plans/2026-10-21-001-design-round-2.md`, `docs/suq-agent-brief.md` Parts A–B (§22, §23; wins over the round-3 docs). Tree: `e60ab6f`. O1–O10 are resolved in the HANDOFF ("Resolved 2026-10-22"). The HANDOFF signature review (2026-10-23) changes §2, §3, §8, §11–§15, §17, §20 and adds §24–§32; those changes are marked **R** below, with steps 5.1e and 5.3g, J55–J94, C28–C47. O11–O15 are resolved. Size is `length`; `count(p)` is a predicate count only. Status: approved 2026-10-23; J91–J94, C47 and §32 adopted 2026-10-24 (user, revision 0001).
 
 ## 1. Per section
 
@@ -33,7 +33,7 @@
 ### §5 Conditionals and booleans
 - (a) `stmt if c` / `stmt unless c` stay; `unless not c` rejected (existing BYN1007); block `unless` unchanged; `if !x` no longer rewritten; `!`/`&&`/`||` and `not`/`and`/`or` both allowed in boolean expressions; `style.booleans` picks one per project; patterns words-only.
 - (b) SPEC §7.2 `:555` delete "`bayan fmt` rewrites `if !x`"; `:561` code list drops BYN1032; §7.3 `:566-570`; §6.1 operators table adds the word spellings; §19.2 `style` gains `booleans: .symbols | .words`; §21.5 drop the `if !x` row, add the booleans row. GRAMMAR §4 `:83` drop BYN1032; `:86` postfix row. STYLE `:7-14` new default row `booleans | .symbols`; `:20-35` config example gains `booleans: .symbols`.
-- (c) BYN1032 retired before it was raised ("No longer reported"). No code for the non-project boolean spelling (SPEC §21.5: style is never compiler-enforced); `bayan format` rewrites it and `bayan format --check` lists it. BYN1007 `UnlessNegated` extended to `not`, block and postfix, same fix-it. BYN1049 (a draft code for `unless not`; earlier a draft `ReturnTypeFirst`, whose case is now BYN1043's `-> T` input form, J54) is retired, merged into BYN1007: descriptor `RetiredUnlessNot` and a "No longer reported" page, never reused (J34).
+- (c) BYN1032 retired before it was raised ("No longer reported"). **R:** BYN1030 (C# primitive names rejected as input forms) is retired with a tombstone `docs/diagnostics/BYN1030.md` ("retired: the rewrite moved to `bayan format` `style.primitives`; never reuse"), so `.csharp` projects build (J92). No code for the non-project boolean spelling (SPEC §21.5: style is never compiler-enforced); `bayan format` rewrites it and `bayan format --check` lists it. BYN1007 `UnlessNegated` extended to `not`, block and postfix, same fix-it. BYN1049 (a draft code for `unless not`; earlier a draft `ReturnTypeFirst`, whose case is now BYN1043's `-> T` input form, J54) is retired, merged into BYN1007: descriptor `RetiredUnlessNot` and a "No longer reported" page, never reused (J34).
 - (d) `Parser.cs:1523-1559 BinaryOperator` (`BarBar :1527`, `AmpAmp :1528`: add `OrKeyword`/`AndKeyword` at the same precedence), `:1652-1668 ParseUnary` (`Bang :1659`: add `NotKeyword`); `and`/`or`/`not` are keywords (`Keywords.cs:30`) used only in patterns today (`ParsePattern :2121-2150`). `:1099-1120 ParseUnless`, `:1439-1446` postfix `unless !`: match `UnaryOp.Not` from either spelling. `ParseCondition :976`: `is not null` already words-only. The `if !x` rewrite exists only in spec text (no parser code). `Manifest.cs:102-106` `style` object: read `booleans`. v0.1: yes.
 
 ### §6 Comments
@@ -209,6 +209,18 @@
 - (b) SPEC §9.4 (verify, then add the two measures); STYLE: the typed-index tree idiom.
 - (c) BYN3217 (plan) `BorrowCrossesFiber` "`{0}` holds a reference into `{1}` and cannot be passed to a fiber that may outlive it" (label at the argument; help "copy the data first, or spawn the fiber in a scope that ends before `{1}`"; fix no). If SPEC §9 already has a code for this, reuse it (grep `Fiber`, `Escape` in `DiagnosticCodes.cs`). Copying a struct with a mutable reference reuses the existing use-after-move error, as for any `unique` value (J87).
 - (d) The region checker (grep `Region` in `src/Bayan.Semantics`); the implicit-`unique` rule beside A7's "a struct with a `unique` field is `unique`". v0.1: yes for both if fibers exist in v0.1; otherwise BYN3217 is reserved.
+
+### Pointers (R, HANDOFF §2)
+- (a) Postfix pointers `T*`, `T**`; `readonly T*` for a pointee that cannot be written; prefix forms are input forms.
+- (b) SPEC §18 FFI (every pointer example), §3.x types (pointer types), §21.5 row (BYN1066).
+- (c) BYN1066 (plan) `PrefixPointerInput` "`{0}` is an input form; write `{1}`" (label at `*`; help "write the pointer after the type: `{1}`"; fix yes).
+- (d) `ParseType :757-812` (postfix `*` after a type name, `readonly` prefix); the Sūq drafts' `*u8`/`*mut u8` are rewritten by the fix. v0.1: yes.
+
+### §32 Spec freeze and revisions (R)
+- (a) After this round's documentation deliverables (1–3, 5 and 9), SPEC/GRAMMAR/STYLE change only through `docs/revisions/NNNN-title.md`.
+- (b) New `docs/revisions/README.md` (format: problem, decision, sections, codes, Sūq files).
+- (c) None.
+- (d) Docs only. The end-of-run summary lists conflicts as draft revisions.
 
 ## 2. Conflicts
 
@@ -259,6 +271,7 @@
 | C44 | Round-2 `Panic.onPanic` vs the `on`-prefix rule (HANDOFF §12) | `Panic.handler`; BYN2230 (R, J77). |
 | C45 | Stdlib and draft members `toList()`, `asSpan()`, `toHex()`, `toUtf16()` vs the `toX`/`asX` rule | `copy()`, `span`/`mutableSpan()`, `string(x, as:)`, `utf16`; BYN2230 (R, J80, J81). |
 | C46 | The Sūq reference app (`docs/suq-reference-app.zip`) predates round 3's final shape: name-first declarations (`ptr: *mut u8`, `load(…) -> SecretKey`), `.owned()`, `drop(ref this)`, `extern "sodium"`, `using let`, `Bytes`/`1B` counts, `toLine`/`toOrder` members, `Panic.onPanic`, per-line states | Regenerate Sūq from this HANDOFF after step 0 lands, rather than patching; the regeneration doubles as the first friction-log pass (HANDOFF §21). |
+| C47 | Pointer spelling: Sūq drafts and SPEC §18 mix prefix (`*u8`, `*mut u8`) and postfix (`Sqlite3*`) | Postfix `T*`, `readonly T*`; prefix forms are BYN1066 input forms (R, J91). |
 | C43 | Round-2 "`-ed` adds `alloc`" vs scalar and plain-struct copies (`x.clamped()`, `v.normalized()`) | `-ed` adds `alloc` only for heap-owning types (R). |
 
 ## 3. Judgment calls
@@ -357,7 +370,7 @@
 - J73 (R) Spec-only in round 3: `Event<T>` and `Subscription`, `std.db`, `std.math` vectors and statistics, static interface members and `Self`, interface default methods. `Self` and static members are needed by 006; note them in `006-spec.md` before merge.
 - J74 (R) Payload names are required in declarations (BYN1064); construction may omit the label when there is exactly one payload. The J39 exemption from particles is unchanged.
 - J75 (R) BYN5009 is raised by the BYN5xxx checker; stdlib members named after a particle are renamed in 5.4 (grep `"at"` in `BuiltinMembers.cs`).
-- J76 (R) Every R code is a plan allocation in a free slot (J27): BYN1058–1061, BYN1063–1065, BYN3213–3217, BYN5009–5011, BYN5013–5014, BYN7006–7008, BYN8006.
+- J76 (R) Every R code is a plan allocation in a free slot (J27): BYN1058–1061, BYN1063–1066, BYN3213–3217, BYN5009–5011, BYN5013–5014, BYN7006–7008, BYN8006.
 
 - J77 (R) `Panic.handler` is a builtin static field of type `PanicHandler`; the round-2 checks (single assignment, first statement of the entry function) move from `onPanic` to it unchanged; the handler's effects are checked against `uses console`.
 - J78 (R) O11: assignments to a field of a struct type from outside that struct's own methods are BYN3216; `with` is unaffected. Fields of local tuples and of anonymous struct literals are not covered (they have no owning methods).
@@ -375,6 +388,11 @@
 - J89 (R) Trap emission: one `static void bayan_trap_bounds(...) __attribute__((noreturn, cold))` per kind, called after a single compare; overflow through `__builtin_add_overflow`/`_sub_`/`_mul_`. No range analysis in v0.0.1.
 - J90 (R) Generics are monomorphized (one C function per instantiation, named by mangled type arguments); interface values are `{data, vtable}` pairs. If the current lowering already does this, the step is documentation only.
 
+- J91 (R) Postfix pointers parse as a type suffix, like `?`; `readonly` before a pointer type marks the pointee read-only and lowers to C `const`. `T*?` (nullable pointer) is not a separate form: C pointers are nullable, so `T*` already maps to a nullable C pointer and reads through `unsafe because`.
+- J92 (R) BYN1030 retires because SPEC §21.5 says style is never compiler-enforced; `.csharp` projects must build. Its fix moves into `bayan format`'s `style.primitives` rewrite. The C# names stay reserved words (HANDOFF §28).
+- J93 (R) The C emitter writes `#line N "file.byn"` before each statement's code (v0.0.1); generated helpers carry no `#line`, so debuggers skip them. A C compiler diagnostic in a user build is reported as an internal compiler error with the C text attached.
+- J94 (R) The spec freeze starts after this round's documentation deliverables (1–3, 5 and 9); the remaining compiler steps implement the frozen text, and any change they need is a draft revision.
+
 ## 4. Order of work
 
 Each step is one commit, named for its HANDOFF section, sized for one writer. "∥" marks steps that can run at the same time; overlap names the shared files.
@@ -387,7 +405,7 @@
 | 5.1c §4 §5 §6 §10 §11-syntax | `@` attributes + `because` clause, `//` only, word booleans, `rune`, `requires` rules on fields with `it`/`at .f`; BYN1045, 1047, 1048, 1050, 8001 (binder part in 5.3a) | `Lexer.cs`, `Keywords.cs`, `Parser.cs` (`:203-218`, `:356-374`, `:458-464`, `:1523-1559`, `:1652-1668`), `Nodes.cs`, the `[Cases]` consumer, goldens (10 `[Attr`, 3 `/*`, 12 `char`, 2 `requires value`) | After 5.1b; ∥ with 5.1d only if one writer owns `Parser.cs` merges (both touch `Parser.cs`, `Keywords.cs`, `Nodes.cs`) |
 | 5.1d §7 §13 §14 §15 | `except`, `uses`, `provide`, `noalloc`/`nosuspend` removal, `state`/`becomes`/`become`, particles in params and arguments; BYN1051, 1052, 6001, 6006, 5008 (parse-level) | `Keywords.cs`, `TokenKind.cs`, `Parser.cs` (`:281-320`, `:412`, `:477-562`, `:1281-1300`, `:1321`, `:1809`, `:2121-2150`), `Nodes.cs`, `AstDump.cs`, goldens (18 `context`, 2 `noalloc`/`nosuspend`) | After 5.1b; see 5.1c |
 | 5.2a §20 | Input-form sweep: every round-3 code has a fix-it and a `diag/` golden; `bayan format` applies them all; O1 one-line union rewrite; BYN1062; `for (a, b) in xs` tuple head (J53) | `Format.cs`, `Parser.cs` (fix spans), goldens | After 5.1d |
-| 5.2b §5 §28 | `bayan format --check`, `style.booleans` and `style.primitives` (no code, J70), idempotency test | `Format.cs`, `Manifest.cs:21,102-106`, `CliApp.cs`, `Messages.cs`, `tests/Bayan.Tests/FormatTests.cs` (new) | ∥ with 5.3a–5.3c; overlap none |
+| 5.2b §5 §28 | `bayan format --check`, `style.booleans` and `style.primitives` (no code, J70), retire BYN1030 with its tombstone (J92), idempotency test | `Format.cs`, `Manifest.cs:21,102-106`, `CliApp.cs`, `Messages.cs`, `tests/Bayan.Tests/FormatTests.cs` (new) | ∥ with 5.3a–5.3c; overlap none |
 | 5.3a §3 §18 §19 | Unused value (BYN3201), `try` scope (BYN1054), `defer` guard capture, `because` check (BYN8001), `catch switch` arms (J49) | `BinderExpressions.cs:44-48`, `Parser.cs:1671-1673`, `BinderCalls.cs`, `Lowerer.cs` | After 5.2a; ∥ with 5.2b, 5.3d |
 | 5.3b §11 §2 | `type distinct` wrapper, `requires` at construction, `try` literals, `with` re-check, `ValidationError<T>`, `T.Field`, infallible `with` (J19), constant construction (J38), skipped struct rules (J43); BYN3204, 3206–3208; §8/§9 codes reserved | `Compilation.cs`, `Binder*.cs`, `Lowerer.cs`, `CEmitter.cs`, `bayan-rt.h` | After 5.3a (shares `BinderExpressions.cs`) |
 | 5.3c §14 | Typestate symbols, refinement types `T.s` and `T is .a or .b` (J56, J57), widening, flow tracking, exhaustiveness, tag layout, state literals (J37); BYN7001–7008 | `Symbols/Types.cs`, `Compilation.cs`, `Binder*.cs`, `FlowAnalysis.cs`, `Lowerer.cs`, `CEmitter.cs` | After 5.3b (shares `Compilation.cs`, `CEmitter.cs`) |
@@ -437,5 +455,5 @@
 | 5.1e R syntax | M | `Parser.cs` receivers, clauses, types, `states`, `rest`, `expects`; ~30 goldens; 7 codes |
 | 5.3g R semantics | M | `expects`, `rest` lowering, overload checks; 3 codes (+2 reserved) |
 | 7 Sūq | M | 4 docs/dirs, paper code |
-| Total codes | | 73 new (25 syntax, 17 types, 10 grammar, 7 effects, 8 typestate, 6 security), 12 reserved (the 9 above plus BYN3213, BYN3215, BYN5010); 2 retired (1032, 1049), plus BYN2305/BYN2307 when particles land |
+| Total codes | | 74 new (26 syntax, 17 types, 10 grammar, 7 effects, 8 typestate, 6 security), 12 reserved (the 9 above plus BYN3213, BYN3215, BYN5010); 3 retired (1030, 1032, 1049), plus BYN2305/BYN2307 when particles land |
 | Total goldens | | ~430 of 450 `.byn` rewritten; ~40 new `diag/`, ~15 new `run/` |
```
