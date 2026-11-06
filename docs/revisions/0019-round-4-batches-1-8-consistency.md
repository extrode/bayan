# 0019 — Round-4 batches 1-9: contradiction pass

Status: accepted (user, 2026-11-06, batch 8 "Contradiction pass"; batch 9 "Tiers Q7", "Also")

## Problem

Batches 1-9 (`docs/bayan-design-round-4-inventory.md`, Decisions) settled questions that SPEC, the tiers doc, the roadmap, A001 and the round-4 draft still answer the old way.

## Decision

Apply the recorded decisions to the text they contradict, and nothing else. Each edit below is anchored on text, not line numbers.

| # | File | Old anchor text | New text | Decided by |
| --- | --- | --- | --- | --- |
| 1 | SPEC App. A | `- No inheritance before a GC tier exists.` | `- No inheritance; there is no GC tier (revision 0019, round-4 batch 1).` | Batch 1 "GC: no GC tier" |
| 2 | SPEC App. B | `hot reload · GC tier and inheritance · guaranteed` | `hot reload · guaranteed` | Batch 1 |
| 3 | SPEC App. A | `- Op suffix; async/await; fails; …` | `async`/`await` dropped from the settled list, plus "`async`/`await` was settled here; user decision 2026-11-04 supersedes it with `scope`/`spawn` (revision 0019)." | R4D §7 row 1, decision 2026-11-04 |
| 4 | SPEC design table | `async`/`await` at call sites; I/O through | "Round 4 replaces call-site `async`/`await` with `scope`/`spawn` (user decision 2026-11-04, revision 0019); I/O through" | same |
| 5 | SPEC §3.11 | `…runs exactly once, at scope end or by the final owner.` | adds "A `struct unique` may declare `void dispose(move this)`: it runs once, replaces derived cleanup, then fields still owned are disposed in reverse order." | Batch 5 row 70 |
| 6 | SPEC extern pointers | `Pointer types are allowed only in extern declarations (BYN1067);` | "…only in `extern` declarations and as a private `T*` field of a `struct unique` with `dispose(move this)`, touched only in `unsafe` (BYN1067, revision 0019);" | Batch 7 "R4D :328 C handles" |
| 7 | SPEC toolchain diagram | `DIR → C → libtcc (debug, scripts)` | `DIR → C → TinyCC per target where a CI probe passes, system cc elsewhere (debug, scripts)` | Batch 7 "R4D §9 Q7 TinyCC" |
| 8 | SPEC packages | `@replaced(by: "…", since: "2.0") drives bayan migrate.` | "`bayan migrate` reads `@deprecated(by: "…")` when `by:` names a same-shape replacement." | Batch 8 "`@replaced`" |
| 9 | SPEC packages (same sentence) | (`@deprecated` was absent from SPEC) | "`@deprecated(by:, until:)`: the error from `until:` is keyed to the consuming package's edition; a fix-it is offered only when `by:` names a same-shape replacement; without `by:` the attribute requires `because`." | Batch 4 R4.3 |
| 10 | SPEC CLI table | `apply @replaced` | `apply same-shape @deprecated(by:)` | Batch 8 |
| 11 | SPEC overloading rule 1 | `1. Overloads that differ only by type` | "1. Not in v0.1: until overloads exist, two same-name functions with different parameter types are a BYN2006 duplicate declaration, never first- or last-wins. Overloads that differ only by type" | Batch 8 row 4 |
| 12 | tiers, Hashing | `non-cryptographic only (wyhash or rapidhash class) for maps \| Proposed` | `seeded SipHash-1-3 for maps (what ships); a faster non-flood-resistant hasher may follow as an opt-in \| Decided (batch 7)` | Batch 7 "Tiers Proposed rows" |
| 13 | tiers, `std.alloc` | `Pool, FixedBuffer` | `Pool, Rc, Handle, FixedBuffer` | Batch 7 "placement" |
| 14 | tiers, `std.fiber` | `Arc, Rc, blocking` | `Arc, Atomic, blocking` (`Mutex` was already there) | Batch 7 "placement" |
| 15 | tiers, `std.crypto` | `…CSPRNG, ChaCha20-Poly1305, AES-GCM, Ed25519, X25519, Argon2;` and `scope Open (Q2)` | `…CSPRNG (AEAD, Ed25519, X25519 and Argon2 are in kit.crypto);` and `scope Decided (Q2)` | Batch 7 "Tiers Q2" |
| 16 | tiers, `kit.tls` row, §12.5 text, Q2 and Q8 rows | `backend Open (Q8)`, `is a separate decision (Q8)`, old Q2 and Q8 resolutions | `Decided (Q8)`; "is the Q8 decision (batch 7): `kit.tls` wraps a C library via `std.c` in v0.3."; Q2 and Q8 resolutions replaced with the batch 7 wording | Batch 7 "Tiers Q8", "Tiers Q2" |
| 17 | roadmap v0.3 row | `C library via std.c or curl shell-outs (Q8);` | `kit.tls wraps a C library via std.c (Q8, decided batch 7);` | Batch 7 |
| 18 | round-4 draft §8 table | `Box<T> / pointer` | `Owned<T> / pointer` | Batch 6 "Noted on the way" |
| 19 | SPEC §2.4 reserved list | `actor macro select ensures Gc gc` with the `ensures` and `Gc`, `gc` clauses | `actor macro select` with "`ensures` is unreserved and `Gc`/`gc` are gone: no GC tier, revision 0019, round-4 batch 1" | Batch 1 "`ensures`: unreserved", "GC: no GC tier" |
| 20 | SPEC :127 note | `carried over (GC):` and `` `Gc`/`gc` reserved now`` | both removed | Batch 1 |
| 21 | SPEC §9.8 and its note | `GC (future)`: "A GC, if added, is an allocator plus a Gc<T> handle…"; note "fixes the shape of a future GC" | `No GC tier`: "There is no GC tier and no `Gc<T>` handle (revision 0019, round-4 batch 1)."; note says the shape was withdrawn. BYN0012 stays: it is the reserved-name code (handoff items 4 and 162), not GC-only | Batch 1 "checks whether BYN0012 still guards anything" |
| 22 | SPEC inheritance bullet (§3) | `(revisit only if a GC tier is added)` | `(there is no GC tier)` | Batch 1 |
| 23 | SPEC package capabilities | `(and, in a future GC tier, .gc, §9.8)` and the "`gc` is a capability" note text | removed | Batch 1 "`.gc`" |
| 24 | SPEC overloading intro | `or by parameter type` | `; type-based overloads are not in v0.1 (rule 1, BYN2006)` | Batch 8 row 4 |
| 25 | SPEC §16.1 `Event<T>` | `public struct Event<T>`; `cancel(move this)          // or drop it`; rule 5 "Dropping it unsubscribes" | `struct unique Event<T>`; `dispose(move this)` added with `cancel` as the early form on the same path; rule 5 names `dispose(move this)`; new rule 8 (read-only `subscriberCount`; the subscribed lambda owns its captures or BYN2825) and rule 9 (stored allocator, `std.event` with `uses alloc`) | Batch 9 "Tiers Q7 `Event<T>`" |
| 26 | tiers, Events row and Q7 row | `module Open (Q7)`; "`core` if it needs no allocator; otherwise `std.event`…" | `module std.event (Q7)`; "Decided (batch 9): `std.event` (SPEC `uses alloc`)." | Batch 9 |
| 27 | tiers, markup paragraph and Nov 1 row | `reserved in round 4` | a contextual word (batch 9), not reserved | Batch 9 "Also: `markup`" |
| 28 | tiers, `kit` table | (no row) | `kit.crypto` row: AEAD, Ed25519, X25519, Argon2 | Batch 7 "Tiers Q2" |
| 29 | roadmap std library row | `notably crypto scope (Q2), kit.tls backend (Q8), std.config (Q11)` | `notably std.config (Q11)` | Batch 7 |
| 30 | A001 :29 | `Fixed-format control ({x:F2}) remains deferred…` | same, plus "Superseded by revision 0003 §6 (batch 9) … `{x:.2}` … rewrites to `{x, digits: 2}` (BYN1068)." | Batch 9 "A001 §1 format specifiers" |
| 31 | inventory | ends at batch 8 | the "Batch 9" section copied verbatim from the dev-merge worktree | Batch 9 |
| 32 | `work/todo.md` | (none) | one line for the SPEC §13 `scope`/`spawn` rewrite | R4D §7 |

Batch 9 also records HQ78 (per-root loans kept, closed with a trigger) and Tiers Q6 (batch 2 stands): decision records only, no spec text changes.

### `since:` consumers (batch 8)

A grep of docs, `src/`, `selfhost/`, `tests/`, `examples/` and `work/` for `since:` and `@replaced` found: SPEC (the two edited places), `docs/bayan-handoff-addendum-002.md:51` (`[Retired(since: …)]`, a different attribute), and the inventory. No code consumes `since:` and none implements `@replaced`. So `since:` is not added to `@deprecated`, and `work/todo.md` gets no line for it.

## Sections

SPEC design table, §2.4, §3 (inheritance bullet), §3.11, extern pointers, §5 overloading, §9.8, §14 capabilities, §16.1, packages (attributes), CLI table, toolchain diagram, Appendix A, Appendix B.

## Codes

None added, changed or retired. BYN2006 now also covers same-name functions with different parameter types (golden `diag/duplicate-overload-types`). BYN3213 (overlap through implicit conversion) is unreachable until type overloads exist.

## Sūq files

None affected.

## Effect on work already done

None in the code. New golden `tests/cases/diag/duplicate-overload-types` (generated with `BAYAN_UPDATE=1`).

## Found, not applied

- SPEC §13 (:1541-1577) still specifies `async`/`await`, `Io.group` and `provide timeout:`; R4D §7 plans the rewrite by revision (line added to `work/todo.md`).
- Other uses of "derived cleanup" in SPEC may need the `dispose(move this)` form (batch 5 row 70 supersedes the `drop(ref this)` / `dispose(ref this)` forms); not searched beyond §3.11.
- Overload rules 2-5 (SPEC §5) describe type overloads that do not exist in v0.1; only the intro and rule 1 are marked.
