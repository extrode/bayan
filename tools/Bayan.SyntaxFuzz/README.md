# Bayan.SyntaxFuzz

Feeds the C# lexer and parser generated inputs: every golden case under `tests/cases/` as is, then
splices of golden cases, grammar fragments (`$"`, `/*`, `\u{`, bidi controls, `unless (`, …) and
random bytes. Seeded, so a seed and count always produce the same inputs.

For every input it checks that

- the lexer and the parser never throw, and neither takes over 500 ms / 1 s;
- tokens are in order and in bounds, start and end on code-point boundaries (valid UTF-8 input),
  and exactly one end-of-file token sits at the end;
- `NewlineBefore` has a newline in the gap before the token, `TriviaBefore` a non-empty gap, and a
  doc comment ends before its token;
- diagnostic and fix spans lie in the file;
- lexing twice gives the same tokens and diagnostics;
- applying a lexer fix does not throw and leaves fewer diagnostics with that code.

## Run

From the repository root:

```
dotnet run -c Release --project tools/Bayan.SyntaxFuzz -- --seed 1 --count 20000
```

It prints each distinct failure once, with the input in base64, and exits 1 when any were found
(2 on bad arguments). 50000 inputs take about 10 s. Fix a crash with a case in `ParserTests` or
`LexerTests`; the selfhost law tests drop inputs the C# parser throws on, so a C# crash fix exposes
those inputs to the port (see the 2026-11-03 progress log entry).

## Known failure

`fix does not fix BYN0010`: on `0x_` the BYN0010 fix (`_` must sit between digits) deletes the `_`,
leaving `0x`, which is BYN0010 again (expected digits after `0x`). Every seed finds it.
