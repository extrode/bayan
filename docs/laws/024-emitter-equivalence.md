---
id: L024
title: Emitter equivalence
status: enacted
kind: property
scope: selfhost/emitter/, src/Bayan.Lowering/, src/Bayan.Backend.C/CEmitter.cs
proof: tests/Bayan.Tests/SelfhostEmitterEquivalenceLawTests.cs
check: dotnet test -v q --filter "Law=L024"
timeout: 300
enacted: 2026-11-04
---

## Statement

For every program `bayan check` accepts and every `BuildMode` (`debug`, `safe`, `fast` today), the C that
`bayan-emit` prints is byte for byte the C that the C# backend prints for the same input and mode
(`bayan build --emit-c`; for `*.test.byn`, the C the test run builds). The corpus is every `tests/cases/run/` input in the mode its `// mode:` line names, which includes `run/hello.byn`, byte-identical to
`examples/hello/main.byn`, the program `CliTests` passes to `--emit-c`, and the 010 generator's well-typed programs on the
fixed seeds, each in every `BuildMode`.

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

Proof (T14): `SelfhostEmitterEquivalenceLawTests.BayanEmitEqualsTheCSharpBackendOnSeedsAndRunInputs` (`Law=L024`) takes its modes from `Enum.GetValues<BuildMode>()` and runs
`bayan-emit --mode m --many <dir>` once per corpus and mode and compares each `== name` section with the C# text.
Seeds: the generator's 8 fixed seeds then a xorshift64 chain from the first, N = 256 (quick; `GenerateValid` writes 15 feature programs and 3 per seed, so 2,632 comparisons in about 15 s) or 1,024 (`BAYAN_LAW_TIER=full`, 9,544 comparisons in
65 s on 2026-11-04). The flat `run/` inputs are in the mode of their `// mode:` line, the `*.test.byn` ones in `safe`
through `TestRunC`. The R6 break-test is `ADeliberateChangeToTheCSharpTextFailsAtLeastOneSeed` (a dropped
`(int64_t)` cast and two swapped struct field lines in the C# text); `BAYAN_L024_BREAK=cast|fields` applies the same
change inside the law test, which then fails.

Enacted by the agent under the standing self-host authorization (2026-10-21; decisions 2026-11-03: the agent enacts L024/L025 after proof; spec 011 T17), with T14's proof passing: `dotnet test -v q --filter "Law=L024"` 1 passed (`SelfhostEmitterEquivalenceLawTests.BayanEmitEqualsTheCSharpBackendOnSeedsAndRunInputs`) on 2026-11-04, 47 s test time (55 s wall; both include rebuilding `bayan-emit`, quick tier). The `CacheTests` template app (`CacheTests.cs:44-49, 192`) is not in this law's corpus: `SelfhostEmitterParityTests.CacheAppEmitsTheSameC` and `CacheAppWithAStoringCalleeFailsLikeBuild` run `bayan-emit` on it and on every example project, but carry no `Law=L024` trait, so the Statement names only what the check runs (handoff Q890).
