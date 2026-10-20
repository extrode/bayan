---
id: L007
title: An Untrusted value never reaches a command-running API unchecked
status: proposed
kind: guard
scope: src/Bayan.Semantics/Binding/BinderCalls.cs
---

## Statement
A call to `Io.runBatch`, `Io.run` or any other API that runs a command, where any argument (the program or path, or an element of the argument list) has type `Untrusted<T>` or holds one, is rejected at compile time with the taint-sink diagnostic (BYN2786). The same call with each such argument validated or wrapped in `trust(…, reason:)` compiles.

## Counterexample looks like
`Io.runBatch(".\\build.cmd", [name])` or `Io.run("git", [name])` that compiles when `name` is `Untrusted<string>`, or `Io.run("git", [trust(name, reason: "checked against the branch list")])` that is rejected.
