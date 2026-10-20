---
id: L004
title: A switch over a union compiles only when it covers every variant
status: enacted
kind: guard
scope: src/Bayan.Semantics/Binding/BinderCalls.cs
enacted: 2026-10-20
---

## Statement
For every `union` and every `switch` statement or expression whose subject has that union type, BYN2410 is reported if and only if some variant is not covered, where a variant is covered by an arm with no `when` guard whose pattern (or one alternative of an `or` pattern) is that variant with only bindings or `_` as subpatterns, or by a default arm (`default:` or `_ =>`). When reported, BYN2410 names every uncovered variant. (Nested refutable subpatterns never count as covering; this is deliberately conservative.)

## Counterexample looks like
`union S { A, B(i64 x), C }` with arms `.A`, `.B(var x) when x > 0` and `.C` compiling cleanly, or arms `.A or .C` and `.B(var x)` reporting BYN2410.
