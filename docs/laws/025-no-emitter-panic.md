---
id: L025
title: No emitter panic
status: proposed
kind: property
scope: selfhost/emitter/
proof: tests/Bayan.Tests/SelfhostEmitterNoPanicLawTests.cs
check: dotnet test -v q --filter "Law=L025"
timeout: 300
enacted:
---

## Statement

For every program in the corpus (every `tests/cases/run/` input, the inputs `CliTests` and
`CacheTests` pass to `--emit-c`, and the 010 generator's programs on the fixed seeds, well-typed or
corrupted) and every build mode, `bayan-emit` terminates and exits 0, 1 or 2: it never panics,
never exits through the runtime's trap paths, and never exceeds the harness time limit.

## Counterexample looks like

An input and mode on which `bayan-emit`'s stderr holds `panic:` or a runtime trap (overflow, bounds,
division by zero, invalid char), or whose exit code is outside {0, 1, 2}, or that does not finish:

```
seed 1187 (debug): exit 134
  panic: index 7 out of range for list of length 7 at selfhost/emitter/cprint/expr.byn:410
```

## Proof notes

The same corpus runner as L024, checking only termination, exit code and the absence of a panic or
trap line on stderr; an input the checker rejects must exit 1 with bayan-check's diagnostics, which
L024 does not cover. Corrupted seeds come from the 010 no-panic test's corruption step so the two
laws share a generator. Quick and full tiers as L024 (`BAYAN_LAW_TIER`). Where a counterexample is
the C# front end's (a parser or binder panic reproduced by the imported checker), it is recorded as
L023's and does not count against this law, as L018/L023 do today (handoff Q662, Q663). Enacted only after L018 and L023.
