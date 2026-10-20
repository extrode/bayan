---
id: L008
title: A compiled program never aliases a unique value at runtime
status: proposed
kind: property
scope: src/runtime/bayan-rt.c
---

## Statement
Outside `unsafe` code, for every program produced by the ownership program generator (a defined grammar of `let`/`var`, `move`, `.copy(to:)`, `take()`, borrows, loops, branches, calls and `switch` over `unique` values) that compiles without errors, the debug build runs with no AddressSanitizer report and no failure of the runtime ownership checker. The checker, present in debug builds only, records each `unique` allocation's current owner, updates it on every move, and panics with both source locations on a second owner or on a read through a moved-from local.

## Counterexample looks like
A generated program that compiles and whose debug build reports a heap use-after-free when a copy of a struct holding a `List` grows, or that panics with "second owner" because a loop moved the same `List` into two variables on consecutive iterations.
