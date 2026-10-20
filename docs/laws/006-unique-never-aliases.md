---
id: L006
title: A unique value's storage is never shared by two owners
status: proposed
kind: property
scope: src/Bayan.Semantics/Binding/OwnershipAnalysis.cs
---

## Statement
For every program that compiles without errors, at every point of execution, the heap storage of a `unique` value (a `List`, `Map`, `StrBuf`, `Owned<T>`, a `unique struct`, or any type containing one) is reachable from at most one live owner. Any other access to it is a borrow (`ref`, a span, a read-only parameter, a loop or pattern binding) whose lifetime ends within the owner's. A local that was moved from is never read again before it is assigned.

## Counterexample looks like
A program that compiles where `var b = move a;` is followed on some path by `a.add(1)`, or where `var c = ledger;` copies a struct holding a `List`, after which `c.items.add(1)` reallocates storage that `ledger.items` still points to (an ASan use-after-free).
