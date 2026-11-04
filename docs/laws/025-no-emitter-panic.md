---
id: L025
title: The Bayan emitter never panics
status: proposed
kind: property
scope: selfhost/emitter/
proof: pending (011 T20)
check: dotnet test -v q --filter "Law=L025"
timeout: 600
---

## Statement
For every program in the stated corpus (every `run/` golden, the generator's seeds), `bayan-emit` terminates without a panic in every build mode (`debug`, `safe`, `fast`).

## Counterexample looks like
`bayan-emit --mode <m>` over a corpus input exits 134 with `panic: …` and `at emitter/<file>.byn:<line>` on stderr (an unported sibling's `panic("internal error: …")` stub, an index out of range in a lowering table), or exits with any code other than 0 on a program `bayan check` accepts.

## Proof notes
Pending: 011 T14 writes the proof, T20 runs `/prove`. Enacted only after L018 and L023 (both enacted). The agent may set `status: enacted` once the proof holds and the quick-tier N is measured (user, 2026-11-03).
