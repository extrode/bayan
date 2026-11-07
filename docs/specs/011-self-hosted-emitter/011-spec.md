# 011 — Self-hosting step 5: lowering and the C emitter in Bayan
Status: draft (rev 3)
Created: 2026-11-02

## Problem

The handoff's "Road to self-hosting" puts lowering and the C emitter next (row 011): checked against `--emit-c` on every `run/` golden. Spec 010 delivered `bayan-check`, a Bayan checker whose diagnostics and typed dump match the C# front end, but nothing in Bayan turns a bound program into C, so the driver and bootstrap (012) have no backend to call. The C# backend is about 4,580 lines: the lowerer (`src/Bayan.Lowering/`, 2,131) and the C emitter (`src/Bayan.Backend.C/CEmitter.cs`, 2,449). The `run/` goldens are 269 inputs, each built in the mode its `// mode:` line names (default `safe`). Addendum A1 (`docs/revisions/0016-addendum-a1-midflight.md`) adds two constraints that cost nothing now and a lot later: C text in one module (K1), and an inventory of the compiler-specific C the output depends on (K2).

## Goals

- A Bayan program, built by the bootstrap compiler, lowers and emits C for Bayan source with the same output as the C# backend: the same C as `--emit-c`, byte for byte, on every `run/` golden and on generated programs, in each build mode a golden uses.
- It consumes the bound program of the `selfhost/semantics/` modules, imported, not copied.
- The normal test run builds it and compares it, so a change to either backend that makes them disagree fails the build.
- Only one module writes C text (K1), and the C the output relies on beyond C11 is listed (K2), so 012 and later toolchain work start from a known surface.
- What the port needed from the language, and did not get, is recorded as Questions or `work/todo.md` lines.

## Non-goals

- The driver (`check`/`build`/`run`), the C compiler interface (K3), libc through `rt` (K4), `bootstrap/bayan.c` (K5) and reproducibility work beyond matching C# (K6): spec 012.
- Any change to emitted C, K2's macros included (A1: "any change to emitted C … before 012 passes" waits for round 4). Where C# output has a defect, it gets a `work/todo.md` line and the port matches it.
- Instrumented output: the coverage counters and mutant schemata of `bayan test --coverage` and `--mutate` (spec 013) stay in the C# backend. Inferred, not asked: they are test tooling, not the compiler's output.
- Replacing the C# backend in the bootstrap compiler; the Bayan emitter is a second implementation, checked against the first.
- New language features, except where the plan shows one is strictly needed and cannot be expressed with what shipped through 010; each such feature gets its own Question.
- Changing any decision in spec Appendix A, or anything A1 lists under "Not mid-flight".

## User stories

- As a Bayan compiler developer, I want the emitter written in Bayan, so that the driver (012) can build the compiler with no C# in the loop.
- As a Bayan compiler developer, I want a failing test whenever the C# and Bayan backends disagree, so that they cannot drift while both exist.
- As a toolchain maintainer, I want C text confined to one module and the compiler-specific C listed, so that a second C dialect, a TinyCC quirk or a native backend touches one place.

## Requirements

- **R1 Program.** `selfhost/emitter/` (lowering included) is a Bayan program, `bayan-emit`. Given the same input and build mode as `bayan build --emit-c`, it prints the same C, byte for byte (for `*.test.byn`, the C the C# test run builds), and for an input with errors the same diagnostics and exit code as `bayan-check`. Paths are passed absolute, as in 010.
- **R2 Reuse.** Tokens, trees and the bound program come from the `selfhost/lexer/`, `selfhost/parser/` and `selfhost/semantics/` modules, imported. Their behaviour is unchanged except the Q745 fix (a borrow escaping through a `ref`/`out` argument or a `ref this` receiver), landed in both checkers before T4; L001, L002, L017, L018, L022 and L023 hold.
- **R3 Lowered tree, one printer (K1).** Lowering produces a lowered tree, mirroring the C# `LoweredNodes`; only one module of the Bayan emitter builds C text. A test fails if C text is built anywhere else in `selfhost/emitter/`.
- **R4 C inventory (K2).** A document lists every compiler builtin, attribute and extension beyond C11 that the emitted C and `src/runtime/bayan-rt.{h,c}` use, each with where it is emitted and what a portable C11 replacement would be. A test fails when the emitter or the runtime uses one the list does not name. Output does not change.
- **R5 Golden coverage.** The suite runs `bayan-emit` on every `tests/cases/run/` input, in the mode the C# run harness builds it (`// mode:` line, default `safe`; `*.test.byn` with tests included), and on every input `CliTests`/`CacheTests` passes to `--emit-c`, and compares with the C# output for the same compilation. A disagreement fails the run, naming the input and the first differing line.
- **R6 Generated inputs.** The 010 generator's well-typed programs are emitted by both backends on a fixed set of seeds, in every build mode, and compared.
- **R7 Speed bound.** On a 1 MB generated well-typed program, `bayan-emit` built in `fast` mode takes no more than 10 times as long as the C# lower-and-emit step, median over 5 runs.
- **R8 Quality bar.** The new `.byn` files meet spec 013's rules as 010 does: the guard's coverage gate at landing, batch mutation scores in `tests/quality-baseline.json`, landing only through `scripts/selfhost/land.sh` with an independent review against the C#.
- **R9 Port friction.** Every workaround forced by the language or the bootstrap compiler gets a handoff Question or a `work/todo.md` line; a compiler bug that blocks the port is fixed in this spec, with a test.
- **R10 Docs.** The handoff's row 011 is marked done, Questions raised by the port are listed, and the self-hosting readiness section is updated.

## Acceptance criteria

- **R1/R5.** Given any `run/` golden, `bayan-emit` output equals the C# backend's output for the same compilation byte for byte in that golden's mode: `bayan build --emit-c`, or for `*.test.byn` the C the test run builds.
- **R1 (errors).** Given an input with errors, `bayan-emit`'s diagnostics and exit code equal `bayan-check`'s.
- **R2.** Given the law checks for L001, L002, L017, L018, L022, L023, they still hold. Given the Q745 goldens, both checkers report them, and no other diagnostic changes. `selfhost/emitter/` contains no copy of a lexer, parser or checker module.
- **R3.** Given a C string built outside the printer module (a break-test), the R3 test fails.
- **R4.** Given a builtin or attribute added to the emitter or runtime and not to the list, including one inside a multi-item `__attribute__((a, b))` (a break-test), the R4 test fails. Given the list, every entry names a use site.
- **R6.** Given the fixed seeds, every comparison is identical; given a deliberate change in one backend (a dropped cast, a reordered field), at least one seed fails.
- **R7.** Given the 1 MB program, the median ratio is 10 or less.

## Law candidates

- **L024 (emitter equivalence).** For every program `bayan check` accepts and every build mode, `bayan-emit` and the C# backend (`bayan build --emit-c`; the test build for `*.test.byn`) print the same C.
- **L025 (no emitter panic).** For every program in the stated corpus (every `run/` golden, the generator's seeds), `bayan-emit` terminates without a panic in every build mode. Enacted only after L018 and L023.

## Open questions

- **Q1 Round-4 lowering decisions (A1 blocker).** A1 says: settle the closure escape rule and the payload layout in round 4 before 011 is drafted, or accept a second port. Found while drafting: the payload layout is the tag plus a union of payloads (`CEmitter.cs:495-568`), which A1 keeps. Function values already lower as a code pointer plus an environment (`Lowerer.cs:1064-1074`, `CEmitter.cs:1717-1720` and `:1920` passes `.env`), which A1 names as the case where the escape rule is "a checker addition only"; but the environment is a tuple in the creating frame, so a `move`-stored closure that outlives that frame may need a heap environment, which is a lowering change. Decide: (a) round 4 settles the escape rule's lowering before 011's plan; (b) 011 ports today's lowering and round 4 accepts a second port of the closure parts; (c) 011 stops before the lambda lowering until round 4 settles it. *Resolved 2026-11-02 (user): (b). 011 ports today's lowering; round 4 redoes the closure parts (65 lines of C# lowering and printing, measured; about 35 more in the checker; the plan's Risks list every range). Until then a closure that would outlive its environment is a compile error: `move` on a lambda (BYN2782, `diag/lambda-move`), a function type as a field, return type or container element (BYN2822, `diag/function-type-placement`, `function-type-placement-declarations`), a lambda stored past its loop iteration or arena (BYN3002, `diag/lambda-escapes-region`, `lambda-capture-borrow-through-call`) and a lambda returned from a lambda (BYN3003, `diag/lambda-escapes-region`). Exception, found 2026-11-02: a borrow, a capturing lambda included, escapes through a `ref`/`out` argument that the callee assigns, or into the receiver of a `ref this` method (handoff Question 745), to be closed in both checkers before T4.*
- **Q2 Paths (handoff Q743).** The printer prints file paths in `#line` directives and file-name statics, which C# derives with .NET path functions, and `std`'s path module (spec 014) is still a draft. *Resolved 2026-11-02 (user): a minimal slice of 014 lands in `std` before 011's wave, holding only the path functions 011 calls, with 014's Windows rules for those functions (drive letters, UNC paths, both separators, case-insensitive `relative`). The rest of 014 is unchanged and lands later. 011 adds no path logic of its own.*
- Inferred, not asked: a separate program `bayan-emit` (as `bayan-check` in 010); C# is the reference; the speed bound and checks follow 009/010; L024/L025 are numbered after L023.
