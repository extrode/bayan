---
id: L023
title: The Bayan checker never panics and keeps every diagnostic span inside its file
status: proposed
kind: property
scope: selfhost/semantics/
proof: tests/Bayan.Tests/SelfhostSemanticsNoPanicLawTests.cs
check: dotnet test -v q --filter "Law=L023"
timeout: 1800
---

## Statement
For every input in the stated corpus (every golden, the generator's seeds, and random byte and token mutations of them, nesting bounded as in L018's proof), `bayan-check` terminates without a panic, and every diagnostic span lies within its file.

## Counterexample looks like
`bayan-check --many` over a corpus directory exits 134 with `panic: index out of range` and `at semantics/<file>.byn:<line>` on stderr after the `== <input>` header of the input it died on, or prints `span out of range: BYN2014 05535.byn 0..1 (size 0)` for a diagnostic whose span ends past the file.

## Proof notes
Proof: `tests/Bayan.Tests/SelfhostSemanticsNoPanicLawTests.cs`, one `bayan-check --many` process per corpus
(not one per input). The binary is built as `SelfhostSemanticsCorpusParityTests` builds it
(`SelfhostSemanticsHarness.Build` of the real `main.byn`). `--many` compiles each `*.byn` alone as
`--golden --package <dir> --spans` does and prints `t26CheckSpans`'s lines (main.byn:274), which walk each
diagnostic's span, related spans and fix span. Corpora, all written under `tmp/semantics-nopanic/`:

- golden: the 800 top-level `*.byn` of `tests/cases/diag`, `typed` and `run`, copied as `<kind>-<file>` so
  the `.test.byn` suffix (and `--tests`) survives; and each of the 11 directory cases of `diag` copied
  whole, one `--many` each, with its `package.byn` (13 inputs);
- seeds: `SelfhostSemanticsGeneratorTests.Corpus(Seeds)`, the generator's 79 valid and faulty programs;
- random: 10,000 inputs (XorShift seed `0x9E3779B97F4A7C15`), each a golden or seed with 1 to 4 mutations:
  a byte inserted, deleted or replaced, invalid UTF-8 inserted, a token deleted, duplicated, swapped with
  the next or replaced by a checker-shaped token, a token replaced by another token of the same input
  (the most frequent, so that inputs still parse), or a line wrapped in `if true { … }` up to
  `SelfhostSemanticsGenerator.NestingCap` (8) deep. The generator is asserted deterministic for one seed
  and different for the next seed.

The expected side is the C# checker in-process: every input is compiled by `Compilation.Create` with
the options `--golden` uses (file alone, the directory as root, tests for `*.test.byn`, the manifest's
capabilities and name, as `SelfhostSemanticsTests.GoldenSame`), and its diagnostics' spans are walked as
`t26CheckSpans` walks the port's. Per input the test asserts that `--spans` prints a line exactly when
the C# diagnostics have a span outside the file. A panic aborts `--many` after the panicking input's
header; the test records the input, renames every input up to it out of `*.byn`, and resumes `--many` on
the rest, so one panic does not hide the inputs after it. The recorded panics must equal `KnownPanics`.

Measured 2026-11-01 (about 20 s, 10,892 inputs):

| | inputs | compared | no lexer/parser diagnostic | panicked | C# throws (replaced) | C# span outside file | rendered as C# |
|---|---|---|---|---|---|---|---|
| golden (flat + 11 directory cases) | 813 | 812 | 722 | 1 | 0 | 0 | 812 |
| seeds | 79 | 79 | 79 | 0 | 0 | 0 | 79 |
| random | 10,000 | 10,000 | 2,035 | 0 | 3 | 0 | 9,997 |
| total | 10,892 | 10,891 | 2,836 | 1 | 3 | 0 | 10,888 |

"Rendered as C#" counts the inputs whose `--many` section (span lines removed) equals
`DiagnosticRenderer.RenderAll`; it is reported, not asserted (L022 owns parity).

The law does not hold on this corpus, so it stays `proposed`:

- Port gap (Q661): `golden/run-suq-money.test.byn` (`tests/cases/run/suq-money.test.byn` with `--tests`)
  makes `bayan-check` exit 134 with `panic: index out of range` at `semantics/symbols.byn:605`
  (`errorSetDisplayName`); the C# checker does not throw on it. It is the one `KnownPanics` entry, and is
  not compared.
- C# counterexample (Q662): 3 random inputs make the C# parser throw `ArgumentOutOfRangeException` in
  `SourceText.GetText` from `Parser.ReportBraceless` (the L018 crash, work/todo.md): `05535.byn`,
  `07570.byn`, `08650.byn`. As in L018 each is replaced by an empty input, counted, and listed with its hex
  bytes in the test output. No C# diagnostic span lies outside its file on any compared input.

Neither checker bounds its recursion, so the proof does not exercise stack overflow (as Q508 for L018).

Mutation: `selfhost/semantics/diag.byn:22` in `report`, `span: span,` changed to
`span: TextSpan { start: span.start, length: span.length + 1 },` (a one-byte span extension).
`BayanCheckNeverPanicsAndEveryDiagnosticSpanLiesInItsFile` failed on the random corpus:
`tmp/semantics-nopanic/random/05535.byn: --spans reports a span C# keeps inside the file`, first line
`span out of range: BYN2014 05535.byn 0..1 (size 0)` (39 s, the golden and seed corpora passed). Restored
from `tmp/t31bak/diag.byn` with `cp`, `cmp` clean, and the rerun passed.
