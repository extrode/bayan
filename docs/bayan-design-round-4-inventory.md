# Bayān — Round 4 inventory (2026-11-06)

Every open design item across `docs/bayan-design-round-4-draft.md` (R4D), `docs/bayan-handoff-addendum-001.md` (A001), `docs/bayan-handoff-addendum-002.md` (A002), `docs/bayan-design-round-3-addendum-1.md` (R3A1), `docs/suq-round3-review-response.md` (Sūq), the handoff *Questions* (HQ) needing a design decision, and `docs/bayan-library-tiers.md` (Tiers), as of dev 87637629. Decisions are taken batch by batch in the order at the end; each answered row moves to the round-4 plan or its own spec.

| id (sources) | title | kind | depends-on | size | status |
|---|---|---|---|---|---|
| R4D §1 + §9 Q2 | Clause order `where→expects→uses→becomes→fails`, including function types | syn | – | S | superseded: R4D §1 (lands first after 012); design-inputs-review S9 |
| R4D §2 A3 | Synonym styles per project | syn | – | S | superseded: A3, user 2026-11-04 |
| R4D §2 word order | One sentence in the guide that explains type-first declarations vs name-first locals | proc | – | S | open |
| R4D §2, §7, §9 Q3 | Renumber diagnostics before 1.0, with aliases for one edition | diag | – | M | superseded: lessons-index pre-1.0 gate (2026-11-05), rev 0017 A2 scheme |
| R4D §2 churn | No syntax change between 012 and the round-4 plan; one migration | proc | – | S | superseded: rev 0016 "Not mid-flight" |
| R4D §3 row 1 | Re-check region-bound references in structs | sem | evidence after 010 | L | open |
| R4D §3 row 2 + §7 v0.2 BYN5100/5101 | Particles/naming rules on public API (friction count) | diag | evidence after 012 | M | open |
| R4D §3 row 3 | Line-joining starters | syn | evidence after 012; R4.2 `extend` | M | open |
| R4D §3 row 4 | Type-based overloads | sem | P7 type-check budget | L | open |
| R4D §3 row 5 (+Sūq F-10) | `$n` SQL placeholders vs position drift | syn | `sql` interpolation | M | open (count rule decided by J40) |
| R4D §3 row 6 + HQ60 | Two-layer trust model. Q60's source list is now in SPEC §15.1 :1704; stdin is plain "for now" (:1711) | sem | Sūq runs | M | HQ60 superseded: SPEC :1704. Model review open |
| R4D §3 row 7 | Typestate scope (`states` used for plain data) | sem | Sūq runs | M | open |
| R4D §3 row 8 + A001 §1 float | `f64` text form: `1.0` prints `1`. The A001 rule "integral prints without decimal point" is reopened | std | – | S | open (conflicts with A001) |
| R4D §4 P1 | Benchmark suite that publishes losses | perf | – | M | superseded: lessons P09 → A2, v0.2a |
| R4D §4 P2 | Remove proven checks (`split__body`) | perf | – | M | superseded: lessons P04 → v0.2a |
| R4D §4 P3 | One C body per layout | perf | – | S | superseded: R4 queue (P02) |
| R4D §4 P4 | Null-niche optionals/Result. Byte figures unverified | perf | – | M | open |
| R4D §4 P5 | Fields reordered by default; `@layout(.declared)`, `@packed` | sem | v0.2 `@packed` | M | open |
| R4D §4 P6 | Drop state-tag checks when the state is known | perf | – | S | open |
| R4D §4 P7 | Compile-time budget: the target numbers | perf | – | S | open (item planned; values unset) |
| R4D §5 guardrails | No `!` suppression; `return` in a lambda is local; inference stays in the module; stability promise per edition | sem | R4.1 | S | partly superseded: stability promise in pre-1.0 gate; rest open |
| R4D §5 + §9 Q5 (+A001 M9) | Lockfile and registry timing | proc | – | S | lockfile superseded: v0.3 S04 `package.lock.byn` (A001 says `bayan.lock`, a conflict). Registry hosting open (lessons-index, v0.3 plan) |
| R4D §6 phase 3 | Risks: macOS SDK header licensing, libc source size per target | tool | phase 2 | M | open |
| R4D §6 phase 4 | QBE-style debug backend | tool | phase 3 | L | open (parking B04) |
| R4D §7 scope/spawn | Replace `async`/`await`, `Io.group` | syn | – | L | superseded: D-A 2026-11-04 |
| R4D §7 provide timeout | Remove `provide timeout:` | syn | – | S | superseded: D-B 2026-11-04 |
| R4D §7 shared-nothing | Structured concurrency, shared-nothing workers | sem | – | L | superseded: C3, bayan-concurrency.md |
| R4D §7 subjectless switch | `switch { cond => … }` (BYN1071 reserved) | syn | – | S | open |
| R4D §7 `when` + HQ70 | `when` guards on arms. SPEC :1287 already has owning-switch guards (BYN2791 read-only); Q70 asks whether guards may mutate | syn | – | S | open |
| HQ69 | `move x switch` parses `move (x switch)` vs `(move x) switch`. Current choice already in SPEC :1287 | syn | `when`/switch batch | S | open (confirm) |
| R4D §7 final + rev 0016 | `final` states, BYN7013 | sem | – | M | open |
| R4D §7 closures + §9 Q1 + rev 0016 | Closure escape rule + `fails` polymorphism (BYN2825). §9 Q1 (lowering) answered by 007 Q114 `{code, env}`, so this is checker-only | sem | – | L | Q1 superseded (007 Q114, Sūq response appendix); rule open |
| R4D §7 dbg | `dbg(x)` is an error in release (`dbg` already in SPEC :920) | diag | – | S | open |
| R4D §7 BYN1070 | `!(x is .s)` input form | diag | – | S | open |
| R4D §7 + rev 0016 | Payload size-ratio lint: threshold | diag | – | S | lint itself decided (rev 0016); threshold open |
| R4D §7 + rev 0018 | Consuming `finish()` (`move this`) | std | – | M | open |
| R4D §7 Q745 | Store/return summaries across packages: in the signature, or `bayan api diff` breaking | sem | – | M | open |
| R4D §7 v0.2 rows | TinyCC `run`, `@derive(Decode)`, `@packed(.big)`, `InlineList`/`Pair`/`sum(as:)` | std | T-Q12 for `InlineList` | M | superseded: v0.2a plan row "Draft v0.2" |
| R4D §7 bounds report | Bounds-check report | tool | P2 | S | open (not found in v0.2a plan) |
| R4D §7 `then` | `then` stages | syn | Sūq evidence | S | open |
| R4D §7 v0.3 comptime | Compile-time eval of empty-`uses` functions | sem | evidence | L | open |
| R4D §7 `@soa` | `@soa` layout | perf | – | M | superseded: parking lot (P06) |
| R4D §8 + SPEC §9.8 | No class, no GC. SPEC :123/§9.8/:1661 still reserve a "future GC tier" (`Gc`, `.gc`) | sem | – | S | open (conflict) |
| R4D §8 extern class | `extern class` handle wrapper for C object APIs | sem | – | M | open |
| R4D §8 Rc/Arc/Pool | `Rc`/`Arc`/`Pool`/`Handle` as std types. Tiers placement unverified | std | – | M | open |
| R4D §8 + §9 Q6 | Single-arena default allocator for `bayan run` and request handlers | sem | – | S | open |
| R4D §9 Q7 | TinyCC on macOS/Windows ARM64, or system `cc` | tool | – | S | open |
| R4D §10 R4.1 | Private signatures infer `fails`; `signatures:` key | sem | – | M | superseded: decided 2026-11-05 |
| R4D §10 R4.2 + "Open for round 4" | Coherence rule B decided. Spelling of `extend` open; SPEC :107 already has `extension` | syn | R4.4 | S | rule superseded (2026-11-05); spelling open |
| R4D §10 R4.3 | `@deprecated(by:, until:)` | diag | renumbering | S | open (proposed) |
| R4D §10 R4.4 | Keyword budget: count and cap | proc | – | S | open |
| R4D §10 R4.5 | Beginner golden per feature | proc | – | S | open |
| R4D §10 R4.6 | `BY_RESTRICT` from borrow facts | perf | K2 | S | open |
| R4D §10 R4.7 | Reflection is compile-time only | sem | – | S | superseded: R4D §9 not-adopted, parking X05 |
| R4D §10 R4.8 | `because` on every escape hatch (+`@unformatted`) | diag | – | S | `@unformatted` decided (style 2026-11-04); rule open |
| A001 §1 | Format specifiers `{x:F2}` (compact `{n:hex}` exists, HQ475) | std | – | M | open (deferred) |
| A002 §1 | Reserve `law`, `requires`, `ensures` | syn | – | S | superseded: SPEC §2.4 :107, :123 |
| A002 §2 + Sūq F-03/J36 + R4D §2/§7 "finish law" | `law` syntax and evidence levels. A002 examples use pre-round-3 syntax (`[Attr]`, `;`, `for (let…)`, `!`) | syn | – | L | runner superseded: v0.2a #9. Syntax open |
| A002 §2.4–2.5 | Laws gate in `package.byn`, `bayan laws` CLI and exit codes | tool | `law` syntax | M | open |
| A002 §3 vs R4D §9 | Contracts: `requires` is now field rules, preconditions are `expects`, `ensures` is "not adopted" in R4D, yet SPEC :123 reserves `ensures` for v1.x | syn | – | S | open (conflict) |
| A002 §3 verify | `bayan verify` (Z3, decidable subset) | tool | laws | L | open (v1.x) |
| A002 §4 | `.par()` parallel iterators. `Io.Threaded` is superseded by the `.compute` pool (C16); BYN3405 needs a renumbered code | sem | closure rule, renumbering | L | open (v0.3) |
| R3A1 §1–§3 | `Panic.handler`, O11/O12, views/copies | sem | – | M | superseded: decided in R3A1; O11 later reversed by rev 0007 |
| Sūq F-30 | User-written cleanup member (conservative `dispose(ref this)`); SPEC :385 has only derived cleanup | sem | – | S | open |
| HQ64 | `take()` on a `unique struct` whose fields all have empty values | sem | – | S | open |
| HQ68 | Narrow L006 to "types with `.copy`", or derive `.copy` for user types | std | – | M | open |
| HQ78 | Per-field-path loans | sem | R4D §3 row 1 | L | open |
| HQ342 | Generic `ref T` instantiated with a type that has states: accept same-state, or add a constraint | sem | typestate scope | M | open |
| HQ371 | Unordered `HashMap` in v0.1 or later (SPEC :284 points here) | std | – | S | open |
| HQ114 | `asSpan()` on a `let` | sem | – | S | superseded: R3A1 §3 (`span`/`mutableSpan()`) |
| HQ430 | Package-root option for `bayan build` | tool | – | S | superseded: `--package` (HQ1050) |
| HQ1042, HQ1065 | Staging directory; non-UTF-8 `run` output | tool | – | S | superseded: user 2026-11-05 (a), 2026-11-06 (a) |
| HQ949 | 014 merges to dev before 012 | proc | – | S | superseded: confirmed by user 2026-11-05 |
| Tiers Q1 | DB module names (`std.db`, `kit.db.sqlite`, `kit.db.pg`) | std | – | S | open |
| Tiers Q2 | Crypto scope: AEAD, signatures, Argon2 | std | – | M | open |
| Tiers Q3 | Byte-codec module | std | – | S | superseded: `std.encoding`, Nov 2 |
| Tiers Q4 | Unicode case tables, `core` vs `std.text`; confirm rev 0015 scope | std | – | S | open |
| Tiers Q5 | Legacy encodings as `kit.encoding` | std | – | S | open |
| Tiers Q6 | Drop `kit.template` | std | – | S | open |
| Tiers Q7 | Home of `Event<T>` | std | – | S | open (decide when implemented) |
| Tiers Q8 | `kit.tls` short-term backend: C library via `std.c` or `curl` | tool | – | M | open |
| Tiers Q9 | `kit.xml` placement and scope | std | 015 S9 condition | S | partly decided (conditional) |
| Tiers Q10 | Script mode auto-imports `kit` | tool | – | S | superseded: dropped Nov 2 (tiers §2 :40 is stale) |
| Tiers Q11 | `std.config` as a thin binder | std | – | S | open |
| Tiers Q12 | Operator forms, const generics, SIMD | syn | – | L | open |
| Tiers §4–§6 "Proposed" rows | Map hashing, SIMD `Vec<T,N>`, `std.codec`, `kit.retry`, `kit.otel`, `kit.svg`/`pdf`, `kit.markdown`, Unicode-classification home | std | T-Q12 for SIMD | M | open |

Not listed as rows:
- R3A1 has no open items.
- A001's remaining sections are decided.
- Handoff Q463/475/490/491/520/522/544/788–877 are implementation bookkeeping.

Unrelated issues seen while auditing:
- A001's manifest keys `capabilities:`/`deps:` are superseded by C13 (`uses:`/`dependencies:`).
- A001 says `bayan fmt` where everything else says `bayan format`.
- design-inputs-review notes two open items outside this scope: the multi-clause header layout (style proposal §3.4) and the shorthands doc's S5 questions.

**Batch order**

1. **Conflicts between documents (all S, no dependencies).** Rows: f64 text form vs A001; `ensures` reservation vs "not adopted"; GC tier in SPEC §9.8 vs R4D §8; lockfile name `bayan.lock` vs `package.lock.byn`; HQ371; tiers §2 :40 stale. Decision: which source wins in each pair.
2. **Library naming, accept or reject the proposed resolutions.** Tiers Q1, Q4, Q5, Q6, Q7, Q9, Q11, and the "Proposed" catalog rows. Decision: yes or no per row.
3. **Small round-4 syntax and diagnostics.** Subjectless switch, `when` guards + HQ70, HQ69, BYN1070, `dbg` in release, payload lint threshold. Decision: adopt each into the round-4 batch, pick the `move x switch` precedence, and set the lint threshold.
4. **Round-4 process rules.** R4.4 cap number, then `extend` spelling (weigh the existing `extension` keyword), R4.3, R4.5, R4.8, the word-order sentence, §5 guardrails. Decision: accept the rules, set the keyword cap, choose the spelling.
5. **Round-4 semantics, medium size.** Closure escape + `fails` polymorphism, `final` states, consuming `finish()`, Q745 summaries (signature vs `api diff`), Sūq F-30, HQ64, HQ68, HQ342. Decision: adopt and pick the option for each. All must be settled before the round-4 plan is written.
6. **Performance and layout.** P4, P5 (default reorder: yes or no), P6, P7 target numbers, R4.6, bounds report, single-arena default for `run`. Decision: which go into round 4 or v0.2a, and the budget values.
7. **Toolchain and ecosystem.** TinyCC on ARM64, phase-3 licensing and libc size, registry hosting, tiers Q8, tiers Q2, `extern class`, `Rc`/`Arc`/`Pool` placement. Decision: per-target compiler, registry host, TLS backend, crypto scope.
8. **Wait for evidence (after 012 or Sūq): set the triggers only.** R4D §3 rows 1–7, naming rules, `then`, comptime eval, HQ78, tiers Q12. Decision: confirm what evidence reopens each and when.
9. **Large designs for v0.2+.** `law` syntax (restated in round-3 syntax), then the laws gate/CLI, `bayan verify`, `.par()`, format specifiers, QBE backend. Decision: syntax and scope per feature, scheduled after round 4.

## Decisions

### Batch 1 (user, 2026-11-06)
- **f64 text form:** `1.0` keeps printing `1` (A001 stands). Ports compare floats as numbers (015 S8); `std.json` encodes
  a parsed number from its source text. R4D §3 row 8 is closed.
- **`ensures`:** unreserved. R4D §9 ("not adopted") wins; A002 §3 is superseded. Round 4 removes it from SPEC :123.
- **GC:** no GC tier. R4D §8 wins. Round 4 removes the "future GC tier" from SPEC §9.8, :123 (`Gc`, `gc`), :382 and
  :1661 (`.gc`), and checks whether BYN0012 still guards anything.
- **Lockfile:** `package.lock.byn` (v0.3 packages plan S04). A001's `bayan.lock` line is superseded.
- Still open from batch 1: HQ371 (unordered `HashMap`) and the stale tiers §2 :40 line; they move to batch 2.

### Batch 2 (user, 2026-11-06)
- **Tiers Q1:** interfaces in `std.db`; drivers `kit.db.sqlite`, `kit.db.pg`; `kit.sql` retired.
- **Tiers Q4:** accepted. Classification and simple case tables in `core`; folding, special casing and normalization in
  `std.text`. Full mapping uses a distinct spelling (e.g. `as: .full`) and never changes `core` names on import. This
  amends revision 0003's ASCII-only v0.1 line.
- **Tiers Q5:** legacy encodings in a separate `kit.encoding`.
- **Tiers Q6:** stays open; revisit after markup ships. Runtime-loaded templates are the case markup does not cover.
- **Tiers Q7:** `Event<T>` in `core` if it needs no allocator, else `std.event`; decided when built.
- **Tiers Q9:** `kit.xml` stays unbuilt (015 S9); if built, in `kit`, pull reader first, and no DTDs and no entity
  expansion by default.
- **Tiers Q11:** `std.config` is a thin binder over `std.cli` and `std.env`; sources are pluggable (file sources later);
  bound values follow the taint rules (§15).
- **HQ371:** accepted on the user's condition that `Map` is an insertion-ordered hash with O(1) lookup and per-process
  seeded hashing. It is: SPEC :284 (insertion order) and `src/runtime/bayan-rt.c` `byn_hash_seed_init` (seed from time
  and pid, fixed under the test flag). The seed feeds FNV-1a, which is not flood-resistant: a security-audit item.
  Now the runtime keys SipHash-1-3 from the OS CSPRNG (fix/runtime-security).
- Still open from the tiers list: Q2 (crypto scope), Q6, Q8 (`kit.tls` backend), Q12, the "Proposed" catalog rows;
  tiers §2 :40 ("auto-imports `kit`") is stale since Q10 was dropped.

### Batch 3 (user, 2026-11-06)
- **Subjectless `switch`:** adopted for the round-4 syntax batch (`switch { cond => … else => … }`, arms in order, same
  C as the `else if` chain; BYN1071 was reserved for it).
- **HQ69:** kept. `move x switch { … }` parses as `move (x switch { … })`; the owning expression form is
  `(move x) switch { … }`.
- **HQ70:** kept. Arm bindings are read-only inside the `when` guard of an owning switch (BYN2791).
- **Negated `is`:** both spellings stay valid, `!(x is P)` and `x is not P`. BYN1070 is repurposed for the precedence
  trap: an unparenthesized `!x is P` or `not x is P` is an error, with fix-its to `!(x is P)` and `x is not P`.
- Superseded rows found while preparing the batch: `when` guards exist (SPEC :877); `dbg` is an error in release
  (SPEC :920). Still open: the payload size-ratio lint threshold (moves to batch 6, with the other numbers).

### Batch 4 (user, 2026-11-06)
- **R4.4 keyword budget:** first audit the reserved list (92 today, `Keywords.cs`) and drop reserved-but-unused words,
  then GRAMMAR states the new count as the cap. The cap counts reserved keywords only; words that appear only in a
  clause position become contextual wherever the parser allows. A new reserved word names what it replaces or why no
  existing word fits, and arrives only through an edition.
- **R4.2 spelling:** keep `extension` (SPEC :107, already a keyword); no `extend`.
- **R4.3 `@deprecated(by:, until:)`:** accepted. The error from `until:` is keyed to the consuming package's edition.
  A fix-it is offered only when `by:` names a same-shape replacement. Without `by:`, the attribute requires `because`.
- **R4.5 beginner golden:** accepted for everyday features only; advanced features (typestate, `unsafe`, region refs,
  `extern`) are listed as exempt.
- **R4.8 `because` on every escape hatch:** accepted as written (`unsafe`, `@allow`, `@json(expose)`, `…Unchecked`
  calls, `@unformatted`; BYN8001's list grows).
- **Word order:** the guide gains one sentence on type-first declarations against name-first locals.
- **§5 guardrails:** accepted: no `!` suppression (a trapping unwrap, if wanted, is spelled `?? panic "…"`); `return`
  inside a lambda is local to it; inference never crosses the module.
