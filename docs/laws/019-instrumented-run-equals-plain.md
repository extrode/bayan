---
id: L019
title: An instrumented run's output and exit code equal the plain run's
status: enacted
kind: property
scope: src/Bayan.Backend.C, src/Bayan.Lowering (coverage counters, mutant schemata)
proof: none yet (T21, /prove after T9)
check: none yet
enacted: 2026-10-31
---

## Statement
For every program, the instrumented run's output and exit code equal the plain run's (spec 013 R10). Instrumented means built with `--coverage` or `--mutate` and run with no mutant selected.

## Counterexample looks like
A `tests/cases/run/` program whose stdout, stderr or exit code differs between `bayan run` and the same program built with coverage counters or mutant schemata and run with `BAYAN_MUTANT` unset or `0`.

## Proof notes
Enacted by the agent on the user's instruction ("Enact on my behalf", 2026-10-31). Proof owed by spec 013 T21.
