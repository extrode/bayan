# 001 — Self-hosting step 1: the compiler-writing subset and a Bayan lexer
Status: draft
Created: 2026-10-20

## Problem

Context: `docs/bayan-self-hosting.md` sets out five phases. This spec covers phase 1 (dogfood with tools) and phase 2 (port the lexer), plus the language features phase 2 needs.

Bayan is meant to self-host (handoff, *What v0.1 is*), but v0.1 can't express a compiler. A program can't read its arguments or a file. Strings can only be built into fixed caller buffers. There are no tagged unions for tokens and syntax trees, no user generics, no `Map`, and no way to build a recursive tree. Until those exist, every part of Bayan has to be written in C#. The language is never exercised on a program the size and shape of its own compiler, so design problems in it surface late.

## Goals

- A program written in Bayan can be a command-line tool that processes files. It reads its arguments, reads and writes files, prints to stdout and stderr, and exits with a chosen code.
- The bootstrap compiler accepts the language features a compiler is written with: tagged unions, enums carrying data, user generics, `Map<K, V>`, strings that grow in an allocator, and recursive data built through an allocator.
- The Bayan lexer, written in Bayan and built by the bootstrap compiler, produces the same output as the C# lexer: the same tokens, kinds, spans and diagnostics. This holds for every golden lex case and for generated inputs.
- At least one tool used to develop Bayan is written in Bayan (phase 1), and the test suite uses it.
- The handoff states which features self-hosting needs and which spec delivers each one, so later work is scoped to make the next phases possible.
- The Bayan lexer runs as part of the normal test suite, so a change to either lexer that makes them disagree fails the build.

## Non-goals

- Interfaces and `dyn`, iterators, `HashMap` and `Symbol` interning, and `property`/`fuzz` test blocks: the self-hosting doc's table needs them before the parser port, so the next spec covers them.
- Comptime and the DIR interpreter, units, `async`/`Io.Threaded`/`Io.Evented`, JSON/CSV, derivations beyond automatic equality, borrowed struct fields with full region inference.
- Porting the parser, semantics, lowering or C backend; later specs cover each.
- Replacing the C# lexer inside the bootstrap compiler. The Bayan lexer is a second implementation, checked against the first.
- Performance parity with the C# lexer beyond the bound in R10.
- `Set`, `Pool`/`Handle`, `EnumSet`/`EnumMap`, `Ring`, `NonEmpty` (spec §3.5, §9.6), except where the plan finds one strictly needed.
- Changing any decision in spec Appendix A.

## User stories

- As a Bayan compiler developer, I want to write compiler phases in Bayan, so that the language is shaped by the hardest program it has to support.
- As a Bayan compiler developer, I want a failing test whenever the C# and Bayan lexers disagree, so that the two can't drift apart while both exist.
- As a Bayan user, I want small file-processing tools to be writable in Bayan, so that the language is useful before the compiler self-hosts.

## Requirements

- **R1 Program entry.** `main` can receive the command-line arguments as strings and can end the program with a chosen exit code, including through `[ExitCode]` on errors (spec §8; handoff Question 35 notes it isn't implemented).
- **R2 Files and streams.** A program can:
  - read a whole file as bytes or as UTF-8 text;
  - write bytes or text to a file;
  - write text to stdout and to stderr without a trailing newline.
  
  Failures (a missing file, no permission, invalid UTF-8 when reading as text) are errors in the function's error set, not panics. All I/O goes through `Io.Blocking` (spec §13.1).
- **R3 Growable strings.** Text can be built without a caller-sized buffer. The result is a `string` owned by an allocator: an arena or the default allocator. `$"…"` interpolation can produce such a string, which lifts the print-only restriction of handoff Question 16 where an allocator is available.
- **R4 Tagged unions.** `union` (spec §3.12) works:
  - variants may carry payloads;
  - unions are stored inline, constructed by variant, matched by `switch` and `is` patterns with payload binding;
  - a non-exhaustive `switch` is a compile error;
  - unions format, and compare with automatic equality.
- **R5 Enums with data.** Java-style enums (spec §3.13) work: constant associated data, `count`, `values`, `name`, `ordinal`, `fromOrdinal`, and `parse`. Plain enums without data are a subset.
- **R6 User generics.** User-defined generic functions and generic structs/unions work, with type arguments inferred from arguments where possible (spec §11). Each instantiation is compiled separately (monomorphized). Constraints are limited to what this subset needs; interface constraints are deferred along with interfaces.
- **R7 `Map<K, V>`.**
  - An allocator-backed map that preserves insertion order (spec §3.5).
  - Supports insert, lookup returning `V?`, update, remove, count, and iteration in insertion order.
  - Keys are at least integers, `string` and enums; hashing is seeded with a fixed seed under `bayan test`.
- **R8 Recursive data.** A union or struct can refer to itself (directly or through other types) through an allocator-owned indirection, so a syntax tree can be built and walked. The indirection's lifetime follows its allocator, and the region rules (spec §9.4) apply.
- **R9 Bayan lexer.** A Bayan program lexes Bayan source with the same behavior as the C# lexer (`bayan lex`). On every input:
  - the same token sequence (kind, span, trivia and doc-comment attachment);
  - the same diagnostics (code, span, message, help, fix);
  - the same token dump text.
  
  This includes Unicode identifier classes, the NFC check and normalization (BYN0005), bidi rejection, interpolation nesting, and every lexer diagnostic BYN0001–0016.
- **R10 Speed bound.** Lexing a 1 MB source file with the Bayan lexer, built in `fast` mode, takes no more than 10 times as long as the C# lexer on the same machine.
- **R11 Checks in the suite.** The normal test run builds the Bayan lexer with the bootstrap compiler and checks it against every `tests/cases/lex` golden and against generated inputs. A disagreement fails the run and names the input and the first differing token or diagnostic.
- **R13 Processes and directories.** A program can:
  - list the entries of a directory;
  - start another program with arguments, wait for it, and read its exit code, stdout and stderr.
  
  Failures, such as a missing program or directory, are errors in the error set, as in R2.
- **R14 A phase-1 tool in Bayan.** `lex-diff` is a Bayan program, built by the bootstrap compiler. Given two lexer commands and a directory, it runs both on every `.byn` file there and reports the first differing line per file. It exits 0 when all files agree and 1 otherwise. It is usable from day one with `bayan lex` against itself or against the golden `.tokens` files, and the R11 checks use it once the Bayan lexer exists.
- **R15 Self-hosting readiness.** The handoff gains a "self-hosting readiness" section, as `docs/bayan-self-hosting.md` asks. It lists every row of that doc's feature table with its status: done, this spec, or a later spec.
- **R12 Diagnostics and docs.** Every new diagnostic introduced for R1–R8 has a code in the right range, a `docs/diagnostics/BYN####.md` page, and a golden case (project AGENTS.md). Every new runtime feature has a `run/` case. The spec and handoff record each gap-filling choice as a handoff Question, with the conservative option.

## Acceptance criteria

- **R1.** Given a program whose `main` prints its arguments, when run as `bayan run prog.byn -- a "b c"`, then it prints `a` and `b c`. Given a `main` that exits with code 7, then the process exits 7.
- **R2.**
  - Given a program that copies file A to file B, when A exists, then B is byte-identical to A.
  - When A is missing, then the program's error handler sees the not-found error and the program does not panic.
  - Given a file with invalid UTF-8, when it is read as text, then an error is returned; when it is read as bytes, then it succeeds.
- **R3.** Given a loop that appends 100,000 short pieces to a string in an arena, then the final length is the sum of the pieces' lengths. No buffer size appears in the source.
- **R4.** Given a `switch` over a union that misses a variant, then compilation fails with a diagnostic naming the missing variant. Given a union value, when it is printed, then it shows its variant and payload.
- **R5.** Given an enum with associated data, then `values`, `name`, `ordinal`, `fromOrdinal` (in range and out of range) and `parse` (known and unknown name) behave as spec §3.13 says, shown by `run/` goldens.
- **R6.** Given a generic function used with three different type arguments, then each call returns the right result, and the emitted C has one instantiation per type argument.
- **R7.**
  - Given 10,000 inserts with some keys repeated, then lookup returns the last value for each key and iteration visits keys in first-insertion order.
  - After removals, `count` and iteration agree.
- **R8.** Given an expression-tree union built recursively in an arena, when it is evaluated, then the result is correct. Storing a node past its arena's lifetime is a compile error.
- **R9/R11.**
  - Given every `.byn` file in `tests/cases/lex`, then the Bayan lexer's dump equals the golden `.tokens` file.
  - Given a generated corpus of at least 10,000 random and mutated inputs, including invalid UTF-8, unbalanced interpolation and non-NFC identifiers, then both lexers produce identical dumps.
- **R13.**
  - Given a program that runs `bayan --version` as a child, then it reads exit code 0 and stdout starting with `bayan `.
  - Given a missing program name, then the program's error handler sees the error and does not panic.
  - Given a directory with three files, then the listing has three entries.
- **R14.** Given `lex-diff` run with `bayan lex` on both sides over `tests/cases/lex`, then it exits 0. Given one side replaced by a command whose output differs on one file, then it exits 1 and names that file and the first differing line.
- **R15.** Given the handoff, then every row of the self-hosting doc's feature table appears in the readiness section with a status.
- **R10.** Given a 1 MB generated source file, then the Bayan lexer's median time over 5 runs is ≤ 10× the C# lexer's.

## Law candidates

- **L1 (lexer equivalence).** For every byte sequence, the Bayan lexer and the C# lexer produce the same token dump and the same diagnostics.
- **L2 (no lexer panic).** For every byte sequence, the Bayan lexer terminates without a panic, and every token span lies within the input.
- **L3 (map order).** After any sequence of inserts, updates and removes, a `Map`'s iteration order is the order in which its live keys were first inserted, and lookups agree with a reference model.
- **L4 (union exhaustiveness).** For every `switch` over a union, compilation succeeds only if every variant is handled or a default arm exists.

## Open questions

*(none; spec gaps are resolved in the plan and recorded as handoff Questions with the conservative option)*
