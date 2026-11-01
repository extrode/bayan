---
id: L020
title: A normal build's generated C is byte-identical with or without the test tooling
status: enacted
kind: property
scope: src/Bayan.Backend.C, src/Bayan.Lowering
proof: tests/Bayan.Tests/MutantSchemataTests.cs (PlainBuildEmitsTheSameC)
check: dotnet test -v q --filter "Law=L020"
enacted: 2026-10-31
---

## Statement
For every program, a normal build's generated C is byte-identical with or without the tooling (spec 013 R11). A normal build is one without `--coverage` and without `--mutate`.

## Counterexample looks like
A program whose generated C from a build without `--coverage`/`--mutate` differs by any byte from the C the compiler generated before the tooling was added (the pre-013 golden).

## Proof notes
Enacted by the agent on the user's instruction ("Enact on my behalf", 2026-10-31). Proof owed by spec 013 T23.
Proof 2026-11-01: broke it by adding a space to the generated C header comment in CEmitter; the test failed with `golden mismatch ... run-c.sha256, first difference at line 1` (run/arithmetic.byn). The golden is compared whole, so a run/ case missing from it also fails.
