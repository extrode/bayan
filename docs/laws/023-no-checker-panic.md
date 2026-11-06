---
id: L023
title: The Bayan checker never panics and keeps every diagnostic span inside its file
status: enacted
kind: property
scope: selfhost/semantics/
proof: tests/Bayan.Tests/SelfhostSemanticsNoPanicLawTests.cs
check: dotnet test -v q --filter "Law=L023"
timeout: 300
---

## Statement
For every input in the stated corpus (every golden, the generator's seeds, and random byte and token mutations of them, nesting bounded as in L018's proof), `bayan-check` terminates without a panic, and every diagnostic span lies within its file.

## Counterexample looks like
`bayan-check --many` over a corpus directory exits 134 with `panic: index out of range` and `at semantics/<file>.byn:<line>` on stderr after the `== <input>` header of the input it died on (as `run/suq-money.test.byn` did before Q661's fix), prints `exit 2` after an input, or prints `span out of range: BYN2101 00628.byn 5..11 (size 10)` for a diagnostic whose span ends past the file.

## Proof notes
Proof: `tests/Bayan.Tests/SelfhostSemanticsNoPanicLawTests.cs`, two facts tagged `Law=L023`. The binary is
built as `SelfhostSemanticsCorpusParityTests` builds it (`SelfhostSemanticsHarness.Build` of the real
`main.byn`).

`BayanCheckNeverPanicsAndEveryDiagnosticSpanLiesInItsFile` runs one `bayan-check --many` process per
corpus. `--many` compiles each `*.byn` alone as `--golden --package <dir> --spans` does, prints
`t26CheckSpans`'s lines (each diagnostic's span, related spans and fix span), and prints `exit <n>` after an
input whose own exit is not 0. Corpora, written by the test under `tmp/semantics-nopanic/`:

- golden: every flat `*.byn` golden (`diag` 541, `typed` 18, `run` 277, `parse` 59, `lex` 21 on 2026-11-03), copied as
  `<kind>-<file>` so the `.test.byn` suffix (and `--tests`) survives; and each of the 12 directory cases of
  `diag` copied whole, one `--many` each, with its `package.byn` (14 inputs);
- seeds: `SelfhostSemanticsGeneratorTests.Corpus(Seeds)`, the generator's 79 valid and faulty programs;
- random: 10,000 inputs (XorShift seed `0x9E3779B97F4A7C15`), each a flat golden or seed with 1 to 4
  mutations: a byte inserted, deleted or replaced, invalid UTF-8 inserted, a token deleted, duplicated,
  swapped with the next or replaced by a checker-shaped token, a token replaced by another token of the same
  input (the most frequent, so that inputs still parse), or a line wrapped in `if true { … }` up to
  `SelfhostSemanticsGenerator.NestingCap` (8) deep. The generator is asserted deterministic for one seed
  and different for the next seed.

The expected side is the C# checker in-process: every input is compiled by `Compilation.Create` with the
options `--golden` uses (file alone, the directory as root, tests for `*.test.byn`, the manifest's
capabilities and name, as `SelfhostSemanticsTests.GoldenSame`). Per input the test asserts that no C#
diagnostic span lies outside the file, that `--spans` prints no line, and that every `exit <n>` line is
`exit 1`. A panic aborts `--many` after the panicking input's header; the test records the input, renames
every input up to it out of `*.byn`, resumes on the rest, and finally asserts that nothing panicked. An
input on which C# throws is replaced by an empty input; the replaced names must equal `KnownCSharpThrows`.
The test then runs the default CLI path, `bayan-check --spans <path>`, over the golden paths (928 on 2026-11-03) (each flat
golden alone in its own directory, since `check` on a file reads its whole directory, and each directory
case's `main.byn`), asserting exit 0 or 1, no `panic:` and no span line.

`BareReturnInTestCatchMatchesCSharp` checks Q661's reproducer (a bare `return` in a test's `catch` block)
with `--golden --tests` against the C# rendering of the same file compiled with tests (CliApp.cs:598).

Measured 2026-11-03, after 010 T45 (Q874) (the two facts 71 s; the corpus part 69.0 s, the CLI part 2.8 s):

| | inputs | compared | no lexer/parser diagnostic | panicked | C# throws (replaced) | C# span outside file | rendered as C# |
|---|---|---|---|---|---|---|---|
| golden (flat + 12 directory cases) | 930 | 930 | 811 | 0 | 0 | 0 | 930 |
| seeds | 79 | 79 | 79 | 0 | 0 | 0 | 79 |
| random | 10,000 | 10,000 | 1,991 | 0 | 0 | 0 | 9,998 |
| total | 11,009 | 11,009 | 2,881 | 0 | 0 | 0 | 11,007 |
| CLI path (goldens) | 928 | 928 | | 0 | | | |

On 2026-11-01 (T32) the same proof replaced 5 random inputs on which C# threw; see below.

"Rendered as C#" counts the inputs whose `--many` section (span and exit lines removed) equals
`DiagnosticRenderer.RenderAll`; it is reported, not asserted (L022 owns parity).

On this corpus the self-hosted checker never panics and keeps every diagnostic span inside its file, and since
010 T45 (Q874) nothing is replaced: `KnownCSharpThrows` is empty. The law is `enacted` (2026-11-02, user-approved,
spec 011 plan D11; it stayed `proposed` after T32, Q663). Until T45 it had one C# counterexample (Q662):
random inputs made the C# parser throw `ArgumentOutOfRangeException` in `SourceText.GetText` from
`Parser.ReportBraceless` (the L018 crash, Q486), 5 of them when T32 measured (`01145.byn`, `01358.byn`,
`02510.byn`, `03889.byn`, `07234.byn`) and 4 as the corpus last stood (`00800.byn`, `04393.byn`, `05382.byn`,
`07933.byn`); each was replaced by an empty input and pinned. C# was fixed in 517d1c9 and the port in 010 T45.

T32 (2026-11-01) re-ran the proof on the 010 base after T29–T31 landed: `dotnet test -v q --filter "Law=L023"` 2 passed, 56 s test time (64 s wall, `bayan-check` rebuilt first, shared machine). Enacting then waited on Q663, whose C# fix has since landed (above).

Tiers (`BAYAN_LAW_TIER`, Q744, Q748): unset is the quick tier, every golden, the seeds and the CLI path, plus the first
N = 1,000 of the 10,000 random inputs (the seeded generator writes the same prefix whatever the corpus size, and
`KnownCSharpThrows` is asserted for the random inputs below N only); `full` is the whole corpus. Measured 2026-11-03 on
the Mac: quick 32 s and full 54 s together with L018's fact (L023 alone, full: 74 s). N is one tenth of the corpus, from
Q744's 30-minute full proof and 3-minute quick target; on the Mac the full tier is already under 3 minutes, so N is not
time-limited here (Q748).

Neither checker bounds its recursion, so the proof does not exercise stack overflow (as Q508 for L018).

Break-tests (each file copied to `tmp/t31bak/` first, one line changed, `--filter "Law=L023"` run,
restored with `cp`; `cmp` or `git diff --quiet` clean afterwards; the worker hook blocks `git checkout`):

| line as it stands | change | fails | first difference |
|---|---|---|---|
| `selfhost/semantics/diag.byn:22` `span: span,` | `span: TextSpan { start: span.start, length: span.length + 1 },` | `BayanCheckNeverPanicsAndEveryDiagnosticSpanLiesInItsFile` (42 s) | `random/00628.byn` (a compared input, not a replacement): `span out of range: BYN2101 00628.byn 5..11 (size 10)` |
| `selfhost/semantics/compile.byn:690` `…, declIndex: declIndex, returnType: prim(.void) })` | drop `returnType: prim(.void)` | both facts (44 s) | reproducer exit 134 `panic: index out of range at semantics/symbols.byn:605`; corpus: `golden/run-suq-money.test.byn` panics |
| `selfhost/semantics/main.byn:336` `return exitOk` (end of `golden`) | `return exitUsage` | both facts (23 s) | corpus: `golden: exit 2 after typed-unconstrained-i32.byn`; reproducer: exit 2 |
