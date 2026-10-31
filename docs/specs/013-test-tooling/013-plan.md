# 013 — Plan
Spec: ./013-spec.md

## Approach
Both features are passes in the C# compiler, inserted on the bound tree after binding and before `Lowerer.Lower` (`src/Bayan.Lowering/Lowerer.cs`), so every site maps to a source span and compiler-generated code (loop counters, desugared `try`, pattern tests) is never mutated or counted. Mutation (R1–R8) rewrites each candidate expression or statement into a `BoundMutantSwitch(id, original, mutants[])` node; the lowerer lowers every arm, and `CEmitter` (`src/Bayan.Backend.C/CEmitter.cs`) emits `(byn_mutant == id ? mutated : original)`, with `byn_mutant` read once at startup from `BAYAN_MUTANT` (0 = no mutant). Coverage (R9–R10) inserts `BoundProbe(id)` statements before each statement and wraps each condition in `BoundBranchProbe(id, cond)`; error paths are counted where a function returns an error value, keyed by (function, variant). Counters are a static `uint64_t` array written to `BAYAN_COVERAGE` at exit (normal exit, `exit()`, panic, test timeout handled by the runner). Neither node exists unless its flag is set, so a normal build emits the same C as today (R11).

Alternatives rejected: mutating the lowered tree (loses source attribution, mutates generated code); mutating C text (cannot know types, so R4 fails); one rebuild per mutant (the problem); an interpreter (non-goal). Speed comes from three things: one build (R2), running only the tests that reach a mutant's site (the mutant-0 run records per-test site reach, the same probe machinery as coverage), and stopping at the first failing test per mutant. A site no test reaches is reported "survived (not reached)" without running anything.

## Tech / dependencies
- C# / .NET 10, NativeAOT constraints unchanged (no reflection) for everything under `src/`.
- C side: `getenv`, `atexit`, `fopen`; no new runtime dependency. Same C compilers as today (`Toolchain.cs`).
- C# reports (R15, spec Q4): `coverlet.collector` 10.1.0 (coverage, Cobertura/JSON output) and `dotnet-stryker` 5.0.0 (mutation, mutation-testing-elements JSON), versions read from `dotnet package search --exact-match` on 2026-10-31. Both run from `tests/`, never in `src/`.

## Changes by area
- `src/Bayan.Syntax` — attributes on statements, for `@unobservable because "…"` (R7). Today attributes parse on declarations, interface members and error variants only (`Parser.cs:433, 946, 1474`). The `because` clause reuses `ParseAttributes`' J6 handling (`Parser.cs:562`), and an empty or missing reason is a diagnostic. An unknown statement attribute stays an error.
- `src/Bayan.Semantics` — bind `@unobservable` to the statement span it covers; record it in `Compilation` for the mutator. No other semantic change.
- `src/Bayan.Testing` (new project, referenced by `Bayan.Cli` and the test harness) — `Mutator` (operator table, site map, R4/R5), `CoverageInstrumenter` (R9), `QualityReport` (R12 JSON, terminal summary, per-line listing), `Gates` (R13).
- `src/Bayan.Lowering` — lower `BoundMutantSwitch`, `BoundProbe`, `BoundBranchProbe` to `LMutantSwitch`/`LProbe` (every arm lowered with the same rules as the original).
- `src/Bayan.Backend.C` — emit the switch and probes; `byn_mutant` and the counter array; the exit writer. `src/runtime/bayan-rt.c` gains the counter flush, guarded by a macro that only instrumented builds define (R11).
- `src/Bayan.Cli` — `bayan test --mutate`, `--coverage`, `--mutate --run '<cmd>'` and `bayan mutate --binary <exe> --sites <map.json> -- <cmd>` (R3, external mode for prebuilt binaries), the default one-line coverage summary (R17), `package.byn` `test: Tests { lines:, branches:, errorPaths:, mutation:, summary: }` (R13, R17). The `diffCoverage` field from spec §19.2 stays parsed and ignored (non-goal).
- `tests/Bayan.Tests` — selfhost harness gains a snapshot mode: build the `.byn` under test once with schemata, compute the C# oracle once, write expected output, then per mutant run the binary and diff (R14). The guard (`scripts/selfhost/guard.sh`) calls `bayan mutate --binary` over every site of the task's files instead of sampling 8.
- `tests/cases/` — `diag/` cases for the new diagnostics, `run/` cases for instrumented programs (R10 law), mutation goldens (site map + report) per operator.
- `docs/bayan-spec.md` §20 — operator table and report format reference (R5, R12); §19.2 package fields. Spec text changes go through the user.
- `scripts/quality/` — C# wrappers: `coverage.sh` (coverlet), `mutate.sh` (Stryker, `--mutate` scoped to the task's changed files), each converting to the R12 JSON (R15).

## Data / interfaces
- Mutation operators v1 (each a separate mutant, chosen by the operand type so every emitted mutant type-checks, R4): relational boundary (`<`↔`<=`, `>`↔`>=`), relational negation (`==`↔`!=`, `<`↔`>=`, `>`↔`<=`), logical (`&&`↔`||`), condition negation (`if c` → `if !c`, also `while`, guard, `stmt if c`), boolean literal flip, arithmetic (`+`↔`-`, `*`↔`/`, `%`→`*`), integer literal ±1, unary minus removal, statement removal (void call, assignment, compound assignment), return of the type's empty value where one exists, error variant swap in `fail E.a` when E has another variant. A candidate whose operator does not apply to the type is counted "not emitted" (R4).
- Site map (`<binary>.sites.json`): `{ version: 1, sites: [{ id, file, line, column, operator, original, mutated, unobservable?: reason }] }`.
- Report (`bayan-quality.json`, one schema for Bayan and C#, R12/R15): `{ version: 1, kind: "mutation"|"coverage", tool, timing: { build_s, mean_mutant_s, max_mutant_s, total_s, mutant_share_of_build }, files: [{ path, lines: {covered, total, missed: [n]}, branches: {...}, errorPaths: [{function, variant, hit}], mutants: [{site, status: killed|survived|timeout|marked|not_reached, killedBy?, suggestion?}] }], totals, operators: { name: {killed, survived, ...} } }`.
- Per-line listing: `<file>.cov.txt` / terminal listing with `+`/`-`/`~` gutter (R12).
- Environment: `BAYAN_MUTANT=<id>`, `BAYAN_COVERAGE=<path>`, `BAYAN_SITE_REACH=<path>` (mutant-0 reach recording).
- Baseline (spec Q5): `tests/quality-baseline.json`, per task file, written by `land.sh` when a task lands and checked by the guard: coverage and mutation score may not drop (spec goal "neither may regress").

## Risks & mitigations
- **Schemata change semantics of the original arm** (evaluation order, double evaluation). Mitigation: each arm is lowered independently and only one runs; law test "mutant 0 behaves as the plain build" runs all `tests/cases/run/` under a schemata build.
- **C compile time of a schemata build** grows with mutant count. Mitigation: measure on `selfhost/semantics` in the first task; if per-mutant share >10% the goal fails visibly (R8 reports it); fallback is per-file schemata builds.
- **Infinite-loop mutants.** Every mutant run has a timeout (max(1 s, 5× the test's own time)); timeout counts as killed, reported on its own line (R3).
- **Counter flush on panic/exit paths** may be missed. Mitigation: flush in the runtime's panic and exit paths, plus `atexit`; a `run/` case covers each exit path.
- **Stryker on .NET 10 and run time over `src/` (~32k lines).** Mitigation: scope to changed files per task; verify Stryker 5.0.0 supports `net10.0` in the first C# task, else report and ask.
- **No external input, auth or network** is involved; reports are local files. `BAYAN_COVERAGE`/`BAYAN_SITE_REACH` are user-supplied output paths, treated like `-o`. `BAYAN_MUTANT` is parsed as an integer; any other value means no mutant.
- **Self-hosted compiler.** These passes live in the C# compiler; the self-hosted compiler (specs 010–012) has no equivalent yet. Needs decision (below).

## Coverage check
| R | Covered by |
|---|---|
| R1 | Cli `--mutate`, Testing.Mutator, report statuses |
| R2 | BoundMutantSwitch + `byn_mutant` |
| R3 | `--mutate --run`, `bayan mutate --binary`, timeout |
| R4 | type-directed operator table, "not emitted" count |
| R5 | operator table in §20, per-operator report totals |
| R6 | site map (file/line/column, original/mutated), `suggestion` per operator |
| R7 | statement attributes, `@unobservable` binding, marked status, unmatched-mark warning |
| R8 | report `timing` |
| R9 | probes, branch probes, error-path counters |
| R10 | counters to file only; law test over `run/` cases |
| R11 | nodes only under flags; runtime macro; byte-identity law test |
| R12 | JSON + terminal + per-line listing |
| R13 | `package.byn` Tests fields, Gates |
| R14 | harness snapshot mode, guard via `bayan mutate --binary` |
| R15 | `scripts/quality/` coverlet + Stryker converters |
| R16 | CI matrix: macOS local, Linux Docker, Windows `ssh wp1` per task |
| R17 | default summary line, `summary: false` opt-out |

## Needs decision
1. Self-hosted compiler parity: port `--mutate`/`--coverage` into spec 011 (lowering + C emitter), or keep them C#-only until after self-hosting.
2. Spec text: add the operator table and report format to §20, and `lines`/`branches`/`summary` to `Tests` in §19.2.
3. Statement attributes are new syntax (only `@unobservable` accepted at first).
