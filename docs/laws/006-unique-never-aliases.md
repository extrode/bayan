---
id: L006
title: The compiler rejects every way to create a second owner of a unique value
status: enacted
kind: guard
scope: src/Bayan.Semantics/Binding/OwnershipAnalysis.cs
proof: tests/Bayan.Tests/L006OwnershipGuardTests.cs
check: dotnet test --filter Law=L006
enacted: 2026-10-21
---

## Statement
Outside `unsafe` code, every program in the table gets its listed verdict. Rejected, each with its ownership diagnostic: a use of a moved local on any path (straight-line, in a loop body on a later iteration, after an `if` or `switch` where one branch moved it, at scope exit through a `defer` or, on a failing exit, an `errdefer`); an implicit copy of a `unique` type or of any type containing a `unique` part; a borrow (`ref`, span, slice, borrowed loop or pattern binding) stored somewhere that outlives its owner; a move out of a field, element or plain parameter. Accepted: the same programs written with an explicit `.copy(to: alloc)`, with the moved local reassigned before the use, and with the borrow ending before its owner does.

## Counterexample looks like
A program that compiles where `var b = move a;` is followed on some path by `a.add(1)`; where `var c = ledger;` copies a struct holding a `List`; where `let items = move ledger.items;` leaves `ledger` half-moved; or a rejection of `var b = move a; a = List<i64>(); a.add(1);`.

## Proof notes
`tests/Bayan.Tests/L006OwnershipGuardTests.cs`, tagged `[Trait("Law", "L006")]`, compiles a table through `Compilation.Create` with a shared prelude. 30 rows must be rejected with their ownership diagnostic: moved-local uses on every listed path, implicit copies of unique types and types containing them, escaping borrows, and moves out of a field, element or plain parameter. 13 rows must be accepted: explicit `.copy(to:)`, reassignment before use, and borrows that end before their owner. `take()` and `switch (move x)` rows return with spec 003.
