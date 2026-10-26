---
id: L017
title: Parser equivalence
status: proposed
kind: property
scope: selfhost/parser/main.byn
proof: tests/Bayan.Tests/SelfhostParserEquivalenceLawTests.cs
check: dotnet test -v q --filter "Law=L017"
timeout: 1800
---

## Statement
For every byte sequence, `bayan-parse` and the C# parser produce the same tree dump and the same diagnostics, and exit with the same code.

## Counterexample looks like
An input on which the two dumps differ in one line (a node kind, a span, the text or the order of a diagnostic, or an operator's spelling), on which the two diagnostics differ by more than line wrapping, or on which `bayan-parse <file>` exits 0 while the C# parser would exit 1 (or the reverse).

## Proof notes
`tests/Bayan.Tests/SelfhostParserEquivalenceLawTests.cs`, fact `BayanParseEqualsTheCParserOnEveryInput` tagged `[Trait("Law", "L017")]`. Six corpora: the 59 `parse/` goldens, the 85 `diag/` cases whose `.diag` holds `BYN1`, the 128 valid and 128 corrupted programs from `SelfhostParserGenerator.GenerateValid`/`GenerateCorrupt`, and a 2048-input random-byte corpus under each of two xorshift seeds. Inputs are written as `00000.byn`, …, in comparison order, so `--many`'s `Io.listDir` byte order is that order.

Dumps: one `bayan-parse --many <dir>` process per corpus, not one per input. Each dump is compared byte-for-byte with `AstDump.Dump(SyntaxTree.Parse(new SourceText(name, bytes)))` up to the first `span out of range:` line (`--many` appends L018's `checkSpans` output, which `AstDump` has no counterpart for). Diagnostics are part of the compared dump, so a differing code, span, label, help or fix fails.

Exit codes, independent of the dump comparison: every input is also run once as `bayan-parse <file>`, and that process's exit code is compared with the C# expectation `Diagnostics.Any(d => d.IsError) ? 1 : 0` (every parse diagnostic is an error), not with anything derived from the dump. On Unix the single-file spawns are batched in one POSIX shell per corpus (a .NET `Process.Start` costs tens of milliseconds here, so 4,496 of them would dominate the proof); Windows uses the same per-input loop in managed code. `--many`'s own exit is also checked to be 0.

The random corpus reuses the L002 lexer corpus shape: lines and byte slices of the `parse/` and `diag/` inputs mixed with invalid UTF-8, bidi and combining-character traps, unbalanced interpolation and one to three raw byte mutations, xorshift64-seeded. It runs under seeds `0x9E3779B97F4A7C15` and `0x0123456789ABCDEF`; both pass and the two corpora are asserted to differ, so the check is not seed-specific.

Random inputs are not dropped wholesale. A dump that differs only in line wrapping (same tree, and equal once every line break and its indentation are folded back to the space it replaced) where the C# diagnostic holds a space inside backticks is counted as Q505's known renderer bug rather than compared; any other difference fails. Measured on 2026-10-26: random-a 2047 of 2048 compared (1 wrapping-only, `random-9E3779B97F4A7C15-01150`), random-b 2047 of 2048 (1, `random-0123456789ABCDEF-01378`); the fixed corpora are 59/59, 85/85, 128/128 and 128/128. The test asserts at least 90% of each random corpus is compared; 99.95% is, so Q505 is a bug to fix (`wrap` in `selfhost/lexer/diagnostics.byn:205` splits at every space, including inside backticks, while C#'s `Words` keeps the code one word), not a coverage hole.

Q506: 4 candidates (1 seed A, 3 seed B) make `SyntaxTree.Parse`/`AstDump.Dump` throw `ArgumentOutOfRangeException` from `SourceText.GetText`, via `Parser.ReportBraceless` (the Q486 bug, `work/todo.md:25`) or `DiagnosticRenderer.Render`, so there is no reference dump; they are replaced by an empty input, as T18 replaces the same candidates for L018. On the same inputs `bayan-parse` exits 1 on three and panics `slice out of range` at `selfhost/lexer/source.byn:80` on `0123456789ABCDEF-01074` (exit 134); Q506 records both sides.

Run time: 11.3 s of test time (16 s wall including the build) on 2026-10-26.

Mutation: `selfhost/parser/dump2.byn:32` (the gate's zero-based index; line 33 one-based), the `.add` arm of `binaryText`. `.add => "+"` changed to `.add => "-"` (one `BinaryOp` text swapped with `.sub`'s, which stays on the next line). `Law=L017` failed: `BayanParseEqualsTheCParserOnEveryInput`, on `parse/contextual-keywords.byn` (written as `tmp/l017/parse/00010.byn`), first difference at dump line 49, expected `(+ …)` and actual `(- …)`. Restored with `cp tmp/l017/dump2-backup.byn selfhost/parser/dump2.byn`; `cmp` was clean and the rerun passed. `selfhost/parser/dump2.byn` is unchanged in the tree.
