---
id: L018
title: The Bayan parser never panics and keeps every span inside the input and its parent
status: proposed
kind: property
scope: selfhost/parser/parser.byn
proof: tests/Bayan.Tests/SelfhostParserNoPanicLawTests.cs
check: dotnet test -v q --filter "Law=L018"
timeout: 1800
---

## Statement
For every byte sequence, `bayan-parse` terminates without a panic. Every node span lies within the input, and every child span lies within its parent's.

## Counterexample looks like
`bayan-parse --many` over a directory whose input ends in a dropped value prints `span out of range: errorExpr 267+0 outside 260+6`, or any input on which `bayan-parse` exits 134 with `panic:`.

## Proof notes
Proof: `tests/Bayan.Tests/SelfhostParserNoPanicLawTests.cs`, one `--many` process per corpus (not one
per input), built once via `SelfhostParserTests.Built`. Two corpora, both run through `--many`:

- the corrupted seed corpus (`SelfhostParserGenerator.GenerateCorrupt(Seeds)`), 128 inputs;
- a random-byte corpus (4,000 inputs, XorShift-seeded, parse-golden fragments mixed with invalid UTF-8,
  parser specials, capped nesting and byte mutations).

Every input is also parsed with the C# parser and its tree walked by a reflection span checker mirroring
`spans.byn` (`CSharpSpanViolations`), so the port is asserted against C#, never against a hard-coded list.

The first clause (exit 0 or 1, no `panic:`) and the "node span within the input" half hold over both
corpora: `--many` exits 0 and prints no `span out of range` line without ` outside `. The
child-within-parent half is false of C# itself, so it cannot hold for a port that matches C#:

- 28 of 128 corrupted inputs and 1,056 of 4,000 random inputs have a C# child outside its parent. All
  come from the known C# span bugs: `move`, `fail`, `break`/`continue` with a label, `except` and `?.`
  take their span before the child (Q473, work/todo.md), and a dropped value's `ErrorExpr` is built at
  `Current.Start`, outside its parent statement (work/todo.md). The port's `checkSpans` reports exactly
  those inputs and no others; the test asserts the port's child-outside-parent set is a subset of C#'s.
- 2 random candidates crash the C# parser itself (`Parser.ReportBraceless` throws
  `ArgumentOutOfRangeException`, work/todo.md): `270A6C65742078203D2020800A65207B7D0A20202020696620287320202E20`
  and `7320697320436972636C65207B7D0A20202020696620287320697320436972636C20C3C820`. The port matches C#
  on both (exit 134, `panic: slice out of range` at `lexer/source.byn:80`), so these are not part of the
  no-panic corpus: the generator replaces a candidate that crashes C# with an empty input.

Because both halves of the statement fail on inputs the port shares with C#, the law stays `proposed`
(Q509; enactment is T19's call, Q510).

The generator caps block/expression nesting at `SelfhostParserGenerator.NestingCap` (8); the random
corpus caps its own nesting the same way. Both `Parser.cs` and the port recurse without a limit, so a
deep enough input overflows the stack in both; the proof does not exercise stack overflow (Q508).

Mutation: `selfhost/parser/parser.byn:105` `spanFrom` changed `prevEnd > start ? prevEnd : start` to
`prevEnd > start ? prevEnd + 1 : start` (a one-byte span extension). `BayanParseNeverPanicsAndEverySpanMatchesTheCSharpTree`
failed (`random: exit 134`, `panic: offset out of range` at `lexer/source.byn:42`). Restored from
`tmp/scratch/parser-backup.byn` with `cp`, `cmp` clean, and the rerun passed.
