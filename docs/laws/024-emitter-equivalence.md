---
id: L024
title: Emitter equivalence
status: proposed
kind: property
scope: selfhost/emitter/
proof: pending (011 T18)
check: dotnet test -v q --filter "Law=L024"
timeout: 1800
---

## Statement
For every program `bayan check` accepts and every build mode (`debug`, `safe`, `fast`), `bayan-emit` and the C# backend (`bayan build --emit-c`; the test build for `*.test.byn`) print the same C.

## Counterexample looks like
A program that `bayan check` accepts on which `bayan-emit --mode <m>` and the C# backend's C for the same mode differ in one line (a local named `t3` against `t4`, a helper emitted in another order, a `#line` directive with another path or line), or on which `bayan-emit` exits non-zero while the C# backend prints C.

## Proof notes
Pending: 011 T14 writes the proof, T18 runs `/prove`. Corpus (spec 011 R6): every `run/` golden and the generator's seeds, each in the three modes. The agent may set `status: enacted` once the proof holds and the quick-tier N is measured (user, 2026-11-03).
