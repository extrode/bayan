# 013 — Test tooling: mutation and coverage
Status: draft
Created: 2026-10-31

## Problem
Bayan promises coverage and mutation testing (spec §20), but neither exists. The self-hosting port (spec 010) checks its tests with a shell guard that rebuilds the whole program for every mutant. Measured on 2026-10-31, that costs about 17 s per mutant for `selfhost/semantics` (17k lines), so the guard samples 8 sites per file. Until that day it also counted mutants the compiler rejected as killed. No `.byn` code has any coverage measure, so "every line and error path is tested" cannot be checked. Developers writing Bayan get no help seeing what their tests miss. The language is meant to make that easy.

## Goals
- Running one mutant costs at most 10% of a full rebuild of the code under test (≈1.7 s for `selfhost/semantics` today). Each run adds one build, and both numbers are measured and reported every run.
- Coverage of lines, branches and error paths for any Bayan package, on macOS, Linux and Windows.
- A report for every mutation run and every coverage run, readable by people and by tools.
- At self-host integration (spec 010 T28), `selfhost/*.byn` and the C# `src/` have 100% coverage and no unmarked surviving mutant. From now on every task reports both, and neither may regress.

## Non-goals
- An interpreter. Mutants run as compiled code.
- Property tests, fuzzing, doctests, benchmarks (§20 lists them separately).
- Diff-coverage gates.
- Mutating C# with Bayan's tool. The C# side gets equivalent reports from .NET tooling chosen in /plan.

## User stories
- As a Bayan developer, I want `bayan test --mutate` to tell me which mutant survived and what case is missing, so that I can strengthen my tests in minutes, not hours.
- As a Bayan developer, I want `bayan test --coverage` to show the lines, branches and `fails` variants my tests never reach.
- As the spec 010 dispatcher, I want the guard to mutate every site in a task's files within a few minutes, and never count an uncompiled mutant.
- As a maintainer, I want one report format for Bayan and C#, so that one gate can enforce "100%, no regression".

## Requirements
- R1 `bayan test --mutate` runs the package's tests against every mutant of the code under test and reports, per mutant: killed, survived, timed out, or marked.
- R2 One build per run contains every mutant. Choosing a mutant at run time needs no rebuild.
- R3 External mode: build the mutant binary once, then run a command the user gives for each mutant. Exit 0 means survived; any other exit means killed; a timeout is reported on its own line.
- R4 A mutation that would not type-check is never emitted and never counted as killed or survived. The report counts these separately.
- R5 The mutation operators are documented, and the report counts each operator on its own.
- R6 A surviving mutant is reported with its file, line and column, the original and mutated text, and a suggested missing case.
- R7 A developer can mark a mutant that no test can observe, using an attribute that requires a written reason. Marked mutants are listed with their reasons and are not survivors. A mark that matches no surviving mutant is a warning.
- R8 Every mutation report gives: build time, mean and maximum time per mutant, total time, and per-mutant time as a share of a plain build.
- R9 `bayan test --coverage` reports, per file and in total, line coverage, branch coverage (each outcome of each condition) and error-path coverage (each `fails` variant of each function triggered at least once).
- R10 A coverage-instrumented program gives the same output and exit code as the plain program.
- R11 A normal build is byte-identical whether these features exist or not: no counters, no mutant switch.
- R12 Each report is a terminal summary, a machine-readable file, and a per-line listing showing what was missed.
- R13 A package can set minimum coverage and mutation scores. A run below them fails.
- R14 The spec 010 guard uses the external mode (R3) over every mutation site in a task's files.
- R15 The C# `src/` gets coverage and mutation reports per task, in the same machine-readable format as R12.
- R16 Everything is verified on macOS, on Linux (Docker) and on Windows (`ssh wp1`).

## Acceptance criteria
- Given `selfhost/semantics`, when the guard runs over a task file, then the report shows per-mutant time ≤10% of a full rebuild, and every site was tried.
- Given a mutant that does not type-check, when a run completes, then it appears only in the "not emitted" count (R4).
- Given a test suite that never triggers `E.b` of `f() fails E`, when `--coverage` runs, then the report names `f` and `E.b` as an uncovered error path.
- Given a marked mutant whose reason is empty, when compiling, then a diagnostic reports the missing reason.
- Given a package with a minimum coverage of 100% and one uncovered branch, when `bayan test --coverage` runs, then it exits non-zero and names the branch.

## Law candidates
- For every program, the instrumented run's output and exit code equal the plain run's (R10).
- For every program, a normal build is byte-identical with or without the tooling (R11).
- For every run, a mutant that does not type-check is never counted as killed or survived (R4).
- For every mutant binary, selecting "no mutant" behaves exactly as the plain build.

## Open questions
- Q1 (user): the name of the marking attribute (R7). Candidates: `@unobservable(because: "…")`, `@sameBehavior(because: "…")`, `@mutantIgnored(because: "…")`, `@noKill(because: "…")`.
- Q2 (user): should plain `bayan test` print a one-line coverage summary by default, so that quality is visible without a flag?
- Q3 (user): spec §20 says mutants run "in the interpreter", and the compilation pipeline (§24) routes mutation to it. Should both change to "compiled mutant schemata, one build per run"?
- Q4 (/plan): which .NET tools produce the C# reports (R15), and how they map to the shared format.
- Q5 (/plan): where the guard's per-task baseline lives, so that "no regression" can be checked.
