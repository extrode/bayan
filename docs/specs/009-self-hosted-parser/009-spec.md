# 009 — Self-hosting step 3: a Bayan parser
Status: done
Created: 2026-10-25

## Problem

The handoff's "Road to self-hosting" puts the parser port next: `selfhost/parser/`, checked against `bayan parse --ast` on every parse golden. Today only the lexer exists in Bayan (spec 001, laws L001/L002). Specs 006–008 delivered what the self-hosting doc's table needs before the parser (interfaces and iterators, lambdas, `Symbol` and composite `Map` keys). Until a Bayan parser exists, the semantics port (010) has no tree to work on. Nobody has checked yet that Bayan can express a recursive-descent parser over a recursive syntax tree at the C# parser's size (about 4,250 lines of parser, 520 of nodes and 840 of dump).

## Goals

- A Bayan program, built by the bootstrap compiler, parses Bayan source with the same results as the C# parser. It produces the same `--ast` dump and the same parser diagnostics, on every parse golden, every diagnostic golden with a parser code, and generated inputs.
- The Bayan parser uses the Bayan lexer from `selfhost/lexer/` as its token source, not a copy of it.
- The normal test run builds the parser and checks it, so a change to either parser that makes them disagree fails the build.
- What the port needed from the language, and did not get, is recorded as Questions or `work/todo.md` lines, not worked around silently.

## Non-goals

- Semantics, lowering, the C emitter or the driver (specs 010–012).
- Replacing the C# parser in the bootstrap compiler. The Bayan parser is a second implementation, checked against the first.
- New language features, except where the plan shows that one is strictly needed and cannot be expressed with what shipped through 008. Any such feature is added under its own Question.
- Changing the C# parser's output format or recovery. Where the two parsers disagree, the C# output is the reference. A C# bug found along the way gets a `work/todo.md` line and stays as is, and the Bayan parser matches it.
- Changing any decision in spec Appendix A.

## User stories

- As a Bayan compiler developer, I want the parser written in Bayan, so that the semantics port has a Bayan syntax tree to consume.
- As a Bayan compiler developer, I want a failing test whenever the C# and Bayan parsers disagree, so that the two cannot drift apart while both exist.
- As a language designer, I want the port to surface every place where Bayan is awkward for a parser, so that the language is fixed before the larger ports.

## Requirements

- **R1 Program.** `selfhost/parser/` is a Bayan program, `bayan-parse`. It reads a source file and prints the same text as `bayan parse --ast` for that file: the tree dump, followed by the diagnostics in the rendered format that `bayan parse` uses. It exits with the same code.
- **R2 Lexer reuse.** The parser takes its tokens from the `selfhost/lexer/` modules, imported rather than copied. Lexer behaviour stays identical, so L001 and L002 still hold.
- **R3 Tree.** Every node kind in the C# AST has a counterpart in the Bayan tree, with the same children and the same spans. The tree is built through an allocator, following the existing region rules.
- **R4 Dump.** The Bayan dump of a tree is byte-identical to the C# `AstDump` for the same input.
- **R5 Diagnostics and recovery.** For every input, the Bayan parser reports the same parser diagnostics as the C# parser: code, span, message, labels, help and fix, in the same order. It also recovers at the same points, so the tree after an error is the same tree.
- **R6 Golden coverage.** The test suite runs `bayan-parse` on every `tests/cases/parse/*.byn` and compares its output with `bayan parse --ast`. It does the same for every `tests/cases/diag/*.byn` whose expected diagnostics include a BYN1xxx code. A disagreement fails the run and names the input and the first differing line.
- **R7 Generated inputs.** A generator produces both valid programs covering every node kind and corrupted ones (tokens dropped, duplicated or swapped). The suite compares the two parsers on a fixed set of seeds.
- **R8 Speed bound.** On a 1 MB generated valid source file, `bayan-parse` built in `fast` mode takes no more than 10 times as long as `bayan parse --ast`. The figure is the median over 5 runs.
- **R9 Port friction.** Every place where the port needed a workaround because the language or the bootstrap compiler fell short gets a handoff Question or a `work/todo.md` line. Examples: a missing feature, a wrong diagnostic, unacceptable C output, or a performance cliff. A compiler bug that blocks the port is fixed in this spec, with a test.
- **R10 Docs.** The handoff's "Road to self-hosting" row 009 is marked done, Questions raised by the port are listed, and the self-hosting readiness section is updated.

## Acceptance criteria

- **R1/R4/R5.** Given any file under `tests/cases/parse/`, the output of `bayan-parse <file>` equals that of `bayan parse --ast <file>`, byte for byte, and both exit with the same code.
- **R5/R6.** Given each `diag/` golden that contains a BYN1xxx code, the diagnostics lines are identical.
- **R2.** Given the lexer law checks (`Law=L001|Law=L002`), they still hold after the parser lands. `selfhost/parser/` contains no copy of a lexer module.
- **R7.** Given the generator's fixed seeds, every comparison is identical. Given a deliberate change in one parser, such as a dropped node field or a different recovery point, at least one seed fails.
- **R8.** Given the 1 MB file, the median ratio is 10 or less.

## Law candidates

- **L017 (parser equivalence).** For every byte sequence, `bayan-parse` and the C# parser produce the same tree dump and the same diagnostics, and exit with the same code.
- **L018 (no parser panic).** For every byte sequence, `bayan-parse` terminates without a panic. Every node span lies within the input, and every child span lies within its parent's.

## Open questions

- None. Inferred, not asked: full parity, including diagnostics and recovery (matching spec 001's lexer standard); the C# output is the reference; tokens come from the Bayan lexer; the speed bound and checks follow spec 001.
