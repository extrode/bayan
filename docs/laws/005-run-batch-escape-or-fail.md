---
id: L005
title: Io.runBatch passes every argument unchanged or runs nothing
status: proposed
kind: property
scope: src/runtime/bayan-rt.c
---

## Statement
For every batch file and every list of argument strings, `Io.runBatch(path, args)` either starts the batch file such that each `%N` it sees equals `args[N-1]` byte for byte (after `cmd.exe` parsing), or fails with an `IoError` without starting any process. A call whose arguments include an `Untrusted<T>` value does not compile.

## Counterexample looks like
`Io.runBatch("echo-args.cmd", ["a\"&calc"])` that starts `calc`, or a batch file that receives `a"` and `calc` as two arguments, or one that runs with `%1` equal to `a^&calc`.
