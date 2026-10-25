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
An input on which the two dumps differ in one line (a node kind, a span, the text or the order of a diagnostic, or an operator's spelling), or on which one exits 0 and the other exits 1.

## Proof notes
`tests/Bayan.Tests/SelfhostParserEquivalenceLawTests.cs`, fact `BayanParseEqualsTheCParserOnEveryInput` tagged `[Trait("Law", "L017")]`. Six corpora, one `bayan-parse --many <dir>` process each, never one process per input: the 59 `parse/` goldens, the 85 `diag/` cases whose `.diag` holds `BYN1`, the 128 valid and 128 corrupted programs from `SelfhostParserGenerator.GenerateValid`/`GenerateCorrupt`, and a 2048-input random-byte corpus under each of two xorshift seeds. Inputs are written as `00000.byn`, …, in the order they are compared, so `--many`'s `Io.listDir` byte order is the comparison order. Each dump is compared byte-for-byte with `AstDump.Dump(SyntaxTree.Parse(new SourceText(name, bytes)))` up to the first `span out of range:` line (`--many` appends L018's `checkSpans` output, which `AstDump` has no counterpart for). Diagnostics are part of the compared dump (`AstDump.Dump` appends `DiagnosticRenderer.RenderAll`), so a differing code, span, label, help or fix fails. The per-input exit code is read back from that dump: a diagnostics section means 1 and its absence means 0, which is exactly C#'s `Diagnostics.Any(d => d.IsError) ? 1 : 0` (every parse diagnostic is an error) and single-file `bayan-parse`'s `errorCount() == 0 ? 0 : 1`; `--many` itself exits 0 for a corpus.

The random corpus reuses the L002 lexer corpus shape: lines and byte slices of the `parse/` and `diag/` inputs mixed with invalid UTF-8, bidi and combining-character traps, unbalanced interpolation and one to three raw byte mutations, xorshift64-seeded (`SelfhostParserGenerator`'s `XorShift` shape). It runs under seeds `0x9E3779B97F4A7C15` and `0x0123456789ABCDEF`; both pass and the two corpora are asserted to differ, so the check is not seed-specific. Warm-cache run of `dotnet test -v q --filter "Law=L017"`: 3.3 s (7.6 s including the parser rebuild).

Two candidate kinds are replaced by an empty input before the process, because no reference dump exists or the dump cannot be equal:
- **Q505, the diagnostic word-wrap bug.** 2518 candidates (1265 seed A, 1253 seed B) have a C# diagnostic with a space inside backticks. C#'s `DiagnosticRenderer.Wrap` (`src/Bayan.Syntax/Diagnostics/DiagnosticRenderer.cs:206`) keeps backticked code one word, but `wrap` in `selfhost/lexer/diagnostics.byn:205` splits at every space, so it can break the code across lines (e.g. `… write \`(i64 -> bool fails ReadError)x\` is an input form; write \`i64 -> bool fails ReadError\`` wraps inside the second code span). The filter is on the input property, not on an observed mismatch. Q505 records the bug.
- **Q506, the C# reference throws.** 4 candidates (1 seed A, 3 seed B) make `SyntaxTree.Parse`/`AstDump.Dump` throw `ArgumentOutOfRangeException` from `SourceText.GetText`, via `Parser.ReportBraceless` (the Q486 bug, `work/todo.md:25`) or `DiagnosticRenderer.Render`; there is no reference dump. On the same inputs `bayan-parse` exits 1 on three and panics `slice out of range` at `selfhost/lexer/source.byn:80` on one (exit 134). T18 replaces the same candidates for L018. Q506 records both sides.

Mutation: `selfhost/parser/dump2.byn:33`, `.add => "+"` changed to `.add => "-"` (one `BinaryOp` text swapped with `.sub`'s). `Law=L017` failed: `BayanParseEqualsTheCParserOnEveryInput`, on `parse/contextual-keywords.byn` (written as `tmp/l017/parse/00010.byn`), first difference at dump line 49, expected `(+ …)` and actual `(- …)`. Restored with `cp tmp/l017/dump2-backup.byn selfhost/parser/dump2.byn`; `cmp` was clean and the rerun passed. `selfhost/parser/dump2.byn` is unchanged in the tree.
