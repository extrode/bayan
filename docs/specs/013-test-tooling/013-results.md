# 013 — Measurements (T18)
Spec: ./013-spec.md · Plan risk: ./013-plan.md "C compile time of a schemata build"

Machine: macOS dev machine, shared and loaded, runs under `nice`. Date: 2026-11-02.

## Guard mutation step over one file: `selfhost/semantics/ctx.byn`

Command: `bash scripts/selfhost/mutate-batch.sh --files selfhost/semantics/ctx.byn` (T28). It calls the same `mutation_step` as guard.sh check 6 (`scripts/selfhost/mutation-step.sh`). The filter derived from the 010 tasks is six `SelfhostSemantics*` classes.

| Phase | Wall time |
|---|---|
| `dotnet build` (warm) | 16 s |
| BAYAN_SNAPSHOT recording (tests + schemata builds) | 4 min 35 s (02:46:30 → 02:51:05) |
| `bayan mutate --binary`, 7 builds × 31 sites = 217 mutants | 92 s (02:51:05 → 02:52:37) |
| Total | 6 min 8 s |

- **Per mutant: 0.42 s,** which is 2.5% of the ~17 s per-mutant rebuild this spec replaces. That is inside the 10% bound, so the per-file schemata fallback in the plan's risks is not triggered.
- **Every site tried:** 57 killed, 160 survived, 0 timeout, 0 marked, 0 not reached, 0 not emitted. The score is 0.2627.
- The survivors are a test-quality finding, not a tooling one: builds 1, 5 and 7 kill 0 of 31 (see `work/todo.md`).

## Recording cost: the slow part

Most of the time goes to the recording (one schemata build per distinct `main.byn`, plus the filtered tests), not to the mutants.

| Recording | Files | Wall time |
|---|---|---|
| one CheckParity case (T27 C4) | `generics.byn` | 19 min 27 s |
| one CheckParity case (T27 C4) | the 10 T27 files | > 25 min (timed out); rerun with a 90 min cap |
| ctx.byn filter (above) | `ctx.byn` | 4 min 35 s |

CheckParity compiles every selfhost semantics file into one checker. Instrumenting more files makes that one C compile much larger. This is why landing runs no mutation (user decision 2026-11-01) and why CI shards it (`.github/workflows/mutation.yml`, 4 shards).
