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
| one CheckParity case (T27 C4) | the 10 T27 files | > 90 min (timed out twice, at 25 and 90 min) |
| site map only, no tests, no C compile | the 10 T27 files (8087 sites, 536 marked) | 2 h 50 min measured (1.27 s per site) |
| ctx.byn filter (above) | `ctx.byn` | 4 min 35 s |

**The cost is in `Schemata.Build`, not the C compile** (stack dump of the site-map run, 2026-11-02). For every site, `Schemata.Rebinds` applies the mutation to the source and calls `Compilation.WithFile`, which re-parses and re-binds the whole package (user decision 2026-10-31: a mutant that does not compile is not emitted). The build is therefore sites × one full compile: about 1.5 s per site over `selfhost/semantics/`, i.e. 763 sites ≈ 19 min and 8087 sites = 2 h 50 min (measured). Finding the 8087 sites alone takes 1 s. The fix (re-bind only the mutated module and its dependents, or cache across sites) changes a user decision, so it is left open.

This is why landing runs no mutation (user decision 2026-11-01) and why CI shards it (`.github/workflows/mutation.yml`, 4 shards).

## T19 cross-platform (R16), tree dfbb161

| Platform | `dotnet test -v q` | `scripts/selfhost/*.test.sh` | Notes |
|---|---|---|---|
| Linux, Debian arm64 in Docker (`mcr.microsoft.com/dotnet/sdk:10.0` + gcc + zig 0.17.0), non-root user, tree copied into the container | 6043/6043 pass, 16 m 58 s | 8/8 pass | Root on a bind mount fails `FormatTests.AnUnreadableFileIsReportedAndTheRestAreFormatted` (root ignores `chmod 000`); not a defect |
| macOS (dev machine, loaded, `nice`) | 6042/6043, 16 m 56 s; the 1 failure, `RunGolden(run/assert-fails.test.byn)`, passes on rerun (RunGolden 269/269, 28 s) | 8/8 pass | Known load flake, `work/todo.md` (run/ goldens under load) |
| Windows 10.0.26200 (`ssh wp1`, dotnet 10.0.402, zig 0.16.0-dev, Git Bash), tree be54c0a | 5960/6043, 1 skipped, 82 fail, 12 m 30 s. `InstrumentedRunLawTests` and every non-selfhost class pass except `CacheTests.ParallelBuildsNeverCorruptTheCache` | 4/8 pass (guard-checks, mutant-outcome, mutation-exempt, wave-brief); guard, land, quality-baseline FAIL 1, mutate-batch FAIL 2 | 76 `SelfhostSemantics*` failures: the self-hosted checker prints the full `C:\…` path where the C# prints the relative one (`checked hello` vs `checked C:\…\clean\hello`), the .byn path-join issue (Q743). Cache test: `Toolchain.ReplaceFile` (`Toolchain.cs:236`, `File.Move` overwrite) gets access denied under parallel builds. 5 `SelfhostSnapshotTests`: fixtures run `/bin/echo` and `/usr/bin/false`. Shell tests: mixed `C:/` and `/c/` path forms (mutate-batch `sort: cannot read /c/…/record.txt`, quality-baseline drop message path); the guard and land single failures were not inspected further. R16 unmet for Windows |
| Windows (`ssh wlt1`, dotnet 10.0.403, no zig, no C compiler, python3 is the Store stub) | 2013/6043, 4029 fail, 1 skipped, 3 m 57 s | 1/8 pass | Missing toolchain; not a usable T19 host |

The suite includes `InstrumentedRunLawTests` (the `run/` cases under `--mutate` and `--coverage`) and the guard's own tests (`guard.test.sh`, `guard-checks.test.sh`).
