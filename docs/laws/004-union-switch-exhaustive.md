---
id: L004
title: A switch over a union compiles only when it covers every variant
status: enacted
kind: guard
scope: src/Bayan.Semantics/Binding/BinderCalls.cs
proof: tests/Bayan.Tests/L004UnionSwitchTests.cs
check: dotnet test --filter Law=L004
enacted: 2026-10-20
---

## Statement
For every `union` and every `switch` statement or expression whose subject has that union type, BYN2410 is reported if and only if some variant is not covered, where a variant is covered by an arm with no `when` guard whose pattern (or one alternative of an `or` pattern) is that variant with only bindings or `_` as subpatterns, or by an arm with no `when` guard whose whole pattern is irrefutable (`_`, `let x`, `var x`, or `default:`), which covers every variant. When reported, BYN2410 names every uncovered variant. (Nested refutable subpatterns never count as covering; this is deliberately conservative.)

## Counterexample looks like
`union S { a, b(i64 x), c }` with arms `.a`, `.b(var x) when x > 0` and `.c` compiling cleanly, or arms `.a or .c` and `.b(var x)` reporting BYN2410, or arms `.a` and `let s =>` reporting BYN2410.
