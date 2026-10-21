---
id: L008
title: A compiled program never aliases a unique value at runtime
status: enacted
kind: property
scope: src/runtime/bayan-rt.c
proof: tests/Bayan.Tests/L008OwnershipRuntimeTests.cs
check: dotnet test --filter Law=L008
enacted: 2026-10-21
---

## Statement
Outside `unsafe` code, for every program produced by the ownership program generator (a defined grammar of `let`/`var`, `move`, `.copy(to:)`, `take()`, borrows, loops, branches, calls and `switch` over `unique` values) that compiles without errors, the debug build runs with no AddressSanitizer report and no failure of the runtime ownership checker. The checker, present in debug builds only, records each `unique` allocation's current owner, updates it on every move, and panics with both source locations on a second owner or on a read through a moved-from local.

## Counterexample looks like
A generated program that compiles and whose debug build reports a heap use-after-free when a copy of a struct holding a `List` grows, or that panics with "second owner" because a loop moved the same `List` into two variables on consecutive iterations.

## Proof notes
`tests/Bayan.Tests/L008OwnershipRuntimeTests.cs`, tagged `[Trait("Law", "L008")]`, generates seeds 1..N with `OwnershipGenerator` (size 16; N = 24, raised by `BAYAN_L008_SEEDS`), compiles each, and for every program without errors builds the debug C (asserting the checker hooks `byn_own_` are present) with AddressSanitizer when the host C compiler has it (`Toolchain.AsanAvailable`; Apple clang does, the bundled `zig cc` does not), then runs it with `ASAN_OPTIONS=detect_leaks=0` (memory lives in allocators). It fails on a non-zero exit, a timeout, or stderr naming a panic, `second owner`, `use of moved` or `AddressSanitizer`, and if more than half the seeds are rejected. Measured 2026-10-21 on macOS arm64 with ASan: 24 seeds, 23 compiled (seed 24 rejected, BYN2780, a deliberate illegal step), all 23 containing `switch (move`, all clean, 10 s; soak at 200 seeds, 179 compiled, all clean, 1 min 7 s. Re-measured after the review fixes, with the generator emitting `when` guards: 24 seeds, 23 compiled; 200 seeds, 179 compiled; all clean (2 s and 10 s with the C build cache warm). The proof's first run found a compiler hole in the other direction: `switch (move x)` bindings moved inside a loop were reported moved on the next iteration (BYN2781 on 21 of 24 seeds), fixed in `OwnershipAnalysis` with L006 rows guarding both sides. `CheckerCatchesAnInjectedAlias` shows the proof can fail: a generated program with an injected alias, compiled with the static check off, panics with `second owner`. The checker is compiled into debug builds only (`-DBYN_NDEBUG` for safe and fast). Limits: the checker claims ownership for `let`/`var` locals, `move` parameters, owning `switch (move x)` bindings (at the selected arm), and places passed by `ref`/`out` or as a method receiver (after the call; element receivers are not tracked); storage inside optionals, unions, arrays and collection elements is not tracked on its own, so an alias of such inner storage would go unseen by the checker (ASan still sees a use after free). Off ASan hosts the proof runs under the checker alone. The panic exit status is 134 on POSIX; Windows is untested.
