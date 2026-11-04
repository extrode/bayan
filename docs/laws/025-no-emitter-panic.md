---
id: L025
title: No emitter panic
status: enacted
kind: property
scope: selfhost/emitter/
proof: tests/Bayan.Tests/SelfhostEmitterNoPanicLawTests.cs
check: dotnet test -v q --filter "Law=L025"
timeout: 1200
enacted: 2026-11-04
---

## Statement

For every program in the corpus (every `tests/cases/run/` input, which includes `run/hello.byn`, byte-identical to `examples/hello/main.byn`, the program `CliTests` passes to
`--emit-c`, and the 010 generator's programs on the fixed seeds, well-typed or
corrupted) and every `BuildMode` (`debug`, `safe`, `fast` today), `bayan-emit` terminates and each input exits 0 or 1:
it never panics, never exits through the runtime's trap paths, and no `bayan-emit --many` run (one mode, one batch) takes
longer than 120 s. Exit 2 is a usage error (`selfhost/emitter/main.byn:29` `exitUsage`) and never occurs on file input.
Emitter coverage on valid programs comes from the well-typed seeds and the `run/` inputs; the corrupted copies reach the
emitter only where the C# checker still accepts them.

## Counterexample looks like

An input and mode on which `bayan-emit`'s stderr holds `panic:` or a runtime trap (overflow, bounds,
division by zero, invalid char), or whose exit code is outside {0, 1}, or a `--many` run that does not finish in 120 s:

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

Proof (T14): `SelfhostEmitterNoPanicLawTests.BayanEmitNeverPanicsOnSeedsCorruptionsAndRunInputs` (`Law=L025`) runs
`bayan-emit --many` in the three modes over the tier's valid and faulty generator programs (N = 256 seeds quick,
1,024 full), 1,000 (full: 10,000) corrupted copies of the 8 fixed seeds' programs, and the flat `run/` inputs. It asserts
exit 0, no `panic:` on stderr, only `exit 1` lines, and for the seed programs that `exit 1` is printed exactly for the
inputs the C# checker rejects. The modes come from `Enum.GetValues<BuildMode>()`. Each `--many` run (a mode and a corpus) is waited
for 120 s, killed on a miss and fails naming the corpus and mode (`ARunPastItsBoundIsKilledAndNamed`); exit 2 for a run or an input fails the test.
The corrupted corpus is counted: inputs the C# checker accepts, so that they reach emission, number 51 of 1,000 (quick) and 533 of 10,000
(full), measured 2026-11-04; the test fails below a floor of 45 (quick) and 500 (full). Measured 2026-11-04 the whole test takes 21 s quick and 80 s
full (the C# checker over the corpora included); the 20 s and 58 s first noted were the earlier total. Per run (milliseconds, debug/safe/fast):
quick seeds 7127/3075/3100, corrupted 414/400/398, run 249/225/222; full seeds 17076/12205/12282, corrupted 3896/3772/3795, run 269/232/238,
so the figures are per run, never summed over modes, and the largest is 17 s against the 120 s bound. The worst case is 3 corpora x 3 modes x
120 s = 1080 s, so `timeout:` is 1200, not 300, to keep the whole-check limit above the per-run bounds. `laws-check.js --run` runs the quick tier
(`BAYAN_LAW_TIER` unset).

Enacted by the agent under the standing self-host authorization (2026-10-21; decisions 2026-11-03: the agent enacts L024/L025 after proof; spec 011 T19), after L018 and L023 were enacted, with T14's proof passing: `dotnet test -v q --filter "Law=L025"` 1 passed (`SelfhostEmitterNoPanicLawTests.BayanEmitNeverPanicsOnSeedsCorruptionsAndRunInputs`) on 2026-11-04, 47 s test time (55 s wall; both include rebuilding `bayan-emit`, which the Proof notes' 21 s quick figure does not). The `CacheTests` template app (`CacheTests.cs:44-49, 192`) is not in this law's corpus: `SelfhostEmitterParityTests.CacheAppEmitsTheSameC` and `CacheAppWithAStoringCalleeFailsLikeBuild` run `bayan-emit` on it and on every example project, but carry no `Law=L025` trait, so the Statement names only what the check runs (handoff Q890).
