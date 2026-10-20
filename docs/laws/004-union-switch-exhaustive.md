---
id: L004
title: A switch over a union compiles only when it covers every variant
status: proposed
kind: guard
scope: src/Bayan.Semantics/Binding/BinderCalls.cs
---

## Statement
For every `union` declaration and every `switch` (statement or expression) whose subject has that union type, compilation reports no BYN2410 if and only if each variant is matched by an irrefutable arm (a variant pattern whose subpatterns are all bindings or `_`) or a default arm exists; when BYN2410 is reported it names every uncovered variant.

## Counterexample looks like
`union S { A, B(i64 x), C }` with arms `.A` and `.B(let x)` and no default compiling cleanly, or arms `.A`, `.B(1)`, `.C` (refutable `.B`) compiling cleanly.
