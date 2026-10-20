---
id: L007
title: An Untrusted value never reaches a command-running API unchecked
status: enacted
kind: guard
scope: src/Bayan.Semantics/Binding/BinderCalls.cs
proof: tests/Bayan.Tests/L007UntrustedSinkTests.cs
check: dotnet test --filter Law=L007
enacted: 2026-10-21
---

## Statement
A call to `Io.runBatch`, `Io.run` or any other API that runs a command, where any argument (the program or path, or an element of the argument list) has type `Untrusted<T>` or holds one, is rejected at compile time with the taint-sink diagnostic (BYN2786). The same call with each such argument validated or wrapped in `trust(…, reason:)` compiles.

## Counterexample looks like
`Io.runBatch(".\\build.cmd", [name])` or `Io.run("git", [name])` that compiles when `name` is `Untrusted<string>`, or `Io.run("git", [trust(name, reason: "checked against the branch list")])` that is rejected.

## Proof notes
`tests/Bayan.Tests/L007UntrustedSinkTests.cs`, tagged `[Trait("Law", "L007")]`, runs 12 rows against both `Io.run` and `Io.runBatch` (24 cases) through `Compilation.Create`. Rejected with BYN2786 naming the API: an untrusted program or path, an untrusted element in a list literal, a `List<Untrusted<string>>` variable as the arguments, and a span of them. Accepted: each of those with `trust(…, reason:)`, and plain values. A struct holding an `Untrusted` field is rejected with BYN2202 rather than BYN2786, because a struct never converts to `string`; the table asserts that. Break-test: dropping `Io.run` from the sinks fails its 6 BYN2786 rows.
