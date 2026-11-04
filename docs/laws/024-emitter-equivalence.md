---
id: L024
title: Emitter equivalence
status: proposed
kind: property
scope: selfhost/emitter/, src/Bayan.Lowering/, src/Bayan.Backend.C/CEmitter.cs
proof: tests/Bayan.Tests/SelfhostEmitterEquivalenceLawTests.cs
check: dotnet test -v q --filter "Law=L024"
timeout: 300
enacted:
---

## Statement

For every program `bayan check` accepts and every build mode (`debug`, `safe`, `fast`), the C that
`bayan-emit` prints is byte for byte the C that the C# backend prints for the same input and mode
(`bayan build --emit-c`; for `*.test.byn`, the C the test run builds). The corpus is every `tests/cases/run/` input in the mode its `// mode:` line names, the inputs
`CliTests` and `CacheTests` pass to `--emit-c`, and the 010 generator's well-typed programs on the
fixed seeds, each in all three modes.

## Counterexample looks like

An input, a mode and the first line where the two outputs differ:

```
run/closures.byn (safe): line 212
  C#:    bynt_tup2_i64_str t3 = ((bynt_tup2_i64_str){a, b});
  Bayan: bynt_tup2_i64_str t3 = ((bynt_tup2_i64_str){ a, b });
```

Or a generated program (seed and mode) on which one backend prints and the other reports an error.

## Proof notes

The test builds `bayan-emit` once in `fast` mode (`SelfhostEmitterHarness.Build`), runs it as a
process on each corpus input, and compares with `CliApp.EmitC(Compilation.Create([source], dir), mode)`
in memory, or for `*.test.byn` with what `ExecuteTests` builds. `BAYAN_LAW_TIER` unset runs the fixed quick subset (every `run/` input plus the first N
seeds, N measured at T14 to fit about 3 minutes and written here); `BAYAN_LAW_TIER=full` runs every seed. A
difference fails naming the input, the mode and the first differing line. Mutant and coverage output
(`--mutate`, `--coverage`) is outside the law: only the C# backend produces it (spec 011, non-goals).
