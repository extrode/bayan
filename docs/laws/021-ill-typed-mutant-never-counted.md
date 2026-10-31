---
id: L021
title: A mutant that does not type-check is never counted as killed or survived
status: enacted
kind: property
scope: src/Bayan.Testing
proof: none yet (T25, /prove after T4 and T6)
check: none yet
enacted: 2026-10-31
---

## Statement
For every run, a mutant that does not type-check is never counted as killed or survived (spec 013 R4). Such a mutant, including one whose enclosing statement fails re-binding or the ownership check, is counted "not emitted".

## Counterexample looks like
A mutation report that lists a mutant as killed or survived whose mutated statement, compiled on its own, gives a binding or ownership diagnostic.

## Proof notes
Enacted by the agent on the user's instruction ("Enact on my behalf", 2026-10-31). Proof owed by spec 013 T25.
