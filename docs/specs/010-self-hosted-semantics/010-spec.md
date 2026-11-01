# 010 — Self-hosting step 4: semantics in Bayan
Status: done
Created: 2026-10-26

## Problem

The handoff's "Road to self-hosting" puts semantics next: binder, flow and ownership in Bayan, checked against every `diag/` golden and, through spec 004's typed dump, a binder-equivalence law. Spec 009 delivered a Bayan parser whose tree and diagnostics match the C# parser. Nothing in Bayan yet resolves names, checks types, or runs the flow, effect, ownership and region analyses, so lowering (011) has no bound program to consume. The C# semantics are about 14,250 lines (`src/Bayan.Semantics/`), three times the parser; 421 of the 519 `diag/` goldens expect a semantic code (first code BYN2000 or above; some are BYN5xxx–BYN8xxx).

## Goals

- A Bayan program, built by the bootstrap compiler, checks Bayan source with the same results as the C# front end: the same diagnostics as `bayan check`, and the same typed dump as `bayan types`, on every `diag/` and `typed/` golden and on generated programs.
- It consumes the trees of the `selfhost/parser/` modules and the tokens of `selfhost/lexer/`, imported, not copied.
- The normal test run builds it and compares it, so a change to either front end that makes them disagree fails the build.
- What the port needed from the language, and did not get, is recorded as Questions or `work/todo.md` lines.

## Non-goals

- Lowering, the C emitter and the driver (specs 011–012).
- Replacing the C# semantics in the bootstrap compiler; the Bayan checker is a second implementation, checked against the first.
- Diagnostics outside `bayan check` (`--json`, `explain`, the cache, `fmt`, style lints that only `bayan style` runs); they stay in the C# CLI, per the finish line.
- New language features, except where the plan shows one is strictly needed and cannot be expressed with what shipped through 009; each such feature gets its own Question.
- Changing C# diagnostics, messages, ordering or the typed dump format. Where the two disagree, C# is the reference; a C# bug gets a `work/todo.md` line and the Bayan port matches it.
- Changing any decision in spec Appendix A.

## User stories

- As a Bayan compiler developer, I want the checker written in Bayan, so that lowering has a bound Bayan program to consume.
- As a Bayan compiler developer, I want a failing test whenever the C# and Bayan checkers disagree, so that they cannot drift while both exist.
- As a language designer, I want the port to surface every place where Bayan is awkward for a type checker, before the emitter port.

## Requirements

- **R1 Program.** `selfhost/semantics/` is a Bayan program, `bayan-check`. Given the same inputs as `bayan check` (a file, a directory of `.byn` files, or a project with its manifest), it prints the same diagnostics in the same rendered format and order, the same error cap and failure footer, and exits with the same code. The success summary's timing figures are excluded from comparison. Paths are passed absolute (Bayan has no working-directory builtin); rendering of relative arguments is a Question.
- **R2 Typed dump.** `bayan-check --types` prints the same JSON as `bayan types` for the same input, byte for byte (`docs/typed-dump.md`); on errors it prints the capped diagnostics without the failure footer and exits 1, as `bayan types` does.
- **R3 Reuse.** Tokens and trees come from the `selfhost/lexer/` and `selfhost/parser/` modules, imported. Their behaviour is unchanged, so L001, L002, L017 and L018 still hold.
- **R4 Coverage of the analyses.** Name resolution and imports, type resolution and conversions, constant folding, overloads and argument names, interfaces and conformance, lambdas, error flow and `fails`, effects, definite assignment and flow, ownership, regions, the project manifest reader (BYN3100–BYN3103, entry-module selection), and the lints `bayan check` reports are all ported, each producing its C# diagnostics.
- **R5 Golden coverage.** The suite runs `bayan-check` on every `tests/cases/diag/` case (file or directory) compiled the way the C# golden harness compiles it (each case in isolation, `*.test.byn` with tests included) and compares its diagnostics with the C# result for the same compilation; and runs `bayan-check --types` on every `tests/cases/typed/*.byn` and compares with `bayan types`. A disagreement fails the run, naming the input and the first differing line.
- **R6 Generated inputs.** A generator produces well-typed programs and programs with one injected semantic fault (unknown name, wrong type, use after move, missing `fails`, unassigned read). The suite compares the two checkers on a fixed set of seeds.
- **R7 Speed bound.** On a 1 MB generated well-typed program, `bayan-check` built in `fast` mode takes no more than 10 times as long as `bayan check`, median over 5 runs.
- **R8 Port friction.** Every workaround forced by the language or the bootstrap compiler gets a handoff Question or a `work/todo.md` line; a compiler bug that blocks the port is fixed in this spec, with a test.
- **R9 Docs.** The handoff's "Road to self-hosting" row 010 is marked done, Questions raised by the port are listed, and the self-hosting readiness section is updated.

## Acceptance criteria

- **R1/R4/R5.** Given any `diag/` case, `bayan-check` prints the same diagnostics as `bayan check` and exits with the same code.
- **R2/R5.** Given any `typed/` case, `bayan-check --types` output equals `bayan types` output byte for byte.
- **R3.** Given the law checks for L001, L002, L017, L018, they still hold. `selfhost/semantics/` contains no copy of a lexer or parser module.
- **R6.** Given the fixed seeds, every comparison is identical; given a deliberate change in one checker (a dropped diagnostic, a widened conversion), at least one seed fails.
- **R7.** Given the 1 MB program, the median ratio is 10 or less.

## Law candidates
Renumbered 2026-11-01 (user decision): spec 013 enacted L019–L021 first, so these are L022 and L023.

- **L022 (binder equivalence).** For every program that parses without errors, `bayan-check --types` and `bayan types` print the same typed dump, and `bayan-check` and `bayan check` report the same diagnostics and exit with the same code.
- **L023 (no checker panic).** For every input in the stated corpus (every golden, the generator's seeds, and random byte and token mutations of them, nesting bounded as in L018's proof), `bayan-check` terminates without a panic, and every diagnostic span lies within its file.

## Open questions

- None. Gaps found while planning (2026-10-26) are folded in: per-case isolated compilation as reference, footer/cap/exit in R1, `--types` error behaviour, manifest in scope, absolute paths, L023's corpus. Inferred, not asked: full parity with `bayan check` and `bayan types` (the 001/009 standard); C# is the reference; trees come from the Bayan parser; speed bound and checks follow 009. Diagnostics outside `bayan check` stay out (finish line).
