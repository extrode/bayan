# 003 — Ownership verification: `take()`, `switch (move x)`, a runtime ownership checker, and a proof for L008
Status: draft
Created: 2026-10-21

## Problem

Spec 002 made collections `unique` and law L006 proves the compiler rejects every listed way to create a second owner. Nothing checks the other side: that a program the compiler *accepts* never aliases a `unique` value when it runs. Law L008 states that property, but it stays proposed because the two things its proof needs don't exist: a generator of ownership programs and a runtime checker in debug builds (handoff Q66).

Two language forms the spec already promises are also missing, and the self-hosted compiler (specs 009–012) needs both to move values out of syntax trees and symbol tables:
- `x.take()` moves a value out of a field or element and leaves an empty value behind (spec §9.3, Q64). Today a move out of a field is only an error (BYN2782), with no way to do it.
- `switch (move x)` gives pattern bindings ownership of the subject's parts (Q65). Today bindings on a `unique` subject always borrow.

L006's accepted `take()` row was dropped when L006 was enacted, to come back with this spec.

## Goals

- `x.take()` and `switch (move x)` work as spec §9.3 describes, with diagnostics and goldens.
- A debug build of any program catches a second owner of a `unique` value, or a read through a moved-from local, at run time and names both source locations. Safe and fast builds carry no checker code.
- A seeded generator produces ownership programs from a defined grammar, and every generated program that compiles runs clean in a debug build, under the checker and AddressSanitizer.
- L008 is proven and enacted; L006 gets its `take()` row back as an amendment, proven.

## Non-goals

- Deriving `.copy(to:)` for user types (Q68).
- Borrowed struct fields, `borrows` clauses, regions (§9.4 is deferred past bootstrap).
- Disposal/destructors of `unique` values; memory still lives in allocators.
- `HashMap`, `Set`, `Pool` (spec 008 adds `HashMap`; it becomes `unique`, and the checker covers it, when it lands).
- Checking `unsafe` code: L006 and L008 hold outside `unsafe` only.
- AddressSanitizer on hosts whose C compiler has no ASan runtime (the bundled `zig cc` has none; Apple clang does). There the generator still runs under the checker alone, and the proof says so.

## User stories

- As a Bayan programmer, I want `node.children.take()` so I can move a list out of a structure I own without copying it.
- As a Bayan programmer, I want `switch (move result)` so a variant's payload becomes mine instead of a borrow.
- As a compiler maintainer, I want debug builds to stop at the first ownership violation the static analysis missed, with both locations, so a hole in `OwnershipAnalysis` shows up as a panic, not as memory corruption.
- As a compiler maintainer, I want thousands of generated ownership programs run on every test pass, so a change to the ownership rules can't silently open a hole.

## Requirements

- R1 `x.take()` is accepted on a `List`, `Map` or `StrBuf` value reached through a local, a field, an element or a `ref`, and on an optional of a `unique` type. It returns the value and leaves the place holding the type's empty value (an empty collection or `StrBuf`; `null` for an optional). The place stays usable.
- R2 `take()` on any other type, or on a place that can't be written (a `let` local, a read-only parameter, a `ReadOnlySpan` element), is a compile error with its own diagnostic, documented and with a `diag/` golden.
- R3 A move out of a field or element (`move a.items`) stays an error (BYN2782), and its message suggests `take()` when the field's type supports it.
- R4 `switch (move x)` moves `x` into the switch: the pattern bindings own the parts they bind, `x` is moved from afterwards on every path, and bindings may themselves be moved or stored. Only a local or a `move` parameter can be the subject; anything else is a compile error with its own diagnostic and golden.
- R5 Without `move`, bindings on a `unique` subject keep borrowing, unchanged from today (BYN2783).
- R6 Debug builds record, for every `unique` value, which variable or place owns it and where ownership was last taken. A move, a `take()` and a `switch (move x)` transfer that record.
- R7 In a debug build, a second owner of one `unique` value, or a read through a moved-from local, panics before the access. The message names the value's type and two source locations: where the current owner got it and where the bad access happens. The panic exit status and stderr format follow the existing panic format.
- R8 Safe and fast builds contain no checker code and no checker data: their generated C for every existing `run/` golden is byte-identical to what it was before this spec.
- R9 A generator, given a seed and a size, produces a well-formed Bayan program from a documented grammar over `unique` values: `let`/`var`, `move`, `.copy(to:)`, `take()`, `ref` and span borrows, `for` and `while` loops, `if` and `switch` branches (including `switch (move x)`), calls with `move`, `ref` and read-only parameters, and returns. The same seed and size always produce the same program text.
- R10 For every generated program that compiles without errors, its debug build runs to completion with exit status 0, no ownership-checker panic and, where the C compiler supports it, no AddressSanitizer report. A generated program the compiler rejects is counted, not failed; the share rejected is reported, and the test fails if more than half of a run is rejected (the generator then tests little).
- R11 The checker itself is tested: hand-written programs that create a second owner or read a moved-from local, compiled with the static ownership check turned off by a test-only switch (not a CLI flag; `unsafe` has no semantics yet), each showing the checker's panic with both locations.
- R12 L006's statement regains an accepted `take()` row ("…with `take()` in place of a move out of a field…"), and its proof table gains accepted and rejected `take()` and `switch (move x)` rows.

## Acceptance criteria

- Given `var t = Tree(...); let kids = t.children.take();`, when it runs, `kids` holds the old children and `t.children.length == 0`.
- Given `let t = …; t.children.take()`, compile fails with the R2 diagnostic; given `move t.children`, BYN2782 suggests `take()`.
- Given `switch (move r) { .ok(list) => keep(move list), .err(e) => … }`, it compiles, and a later use of `r` is a moved-from error; without `move`, `keep(move list)` is rejected as today.
- Given a program compiled with the static check off that copies a `List` into a second variable and then appends through both, the debug build panics naming both lines; the same program built `--mode fast` has no checker symbol in its C.
- Given seed 1–N for the test run's N, every generated program that compiles runs clean under the checker (and ASan where available), the run reports how many compiled, and rerunning with the same seeds prints the same programs.

## Law candidates

- L008 (already proposed): for every generated program that compiles, the debug build runs with no ASan report and no checker failure — R9, R10. Proven here, enacted with its proof.
- L006 amendment: the `take()` acceptance row and the `switch (move x)` rows — R12. Enacted with its proof under the standing authorization.

## Open questions

None blocking. Decisions taken under the unattended run's conservative rule, recorded as handoff Questions when the plan lands:
- Types with an empty value for `take()` (Q64): `List`, `Map`, `StrBuf` and optionals of `unique` types only. A `unique struct` gets `take()` later, if all its fields have empty values.
- ASan is used where the host C compiler has it; the proof records which half ran.
