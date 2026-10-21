---
id: L001
title: The Bayan lexer and the C# lexer agree on every input
status: enacted
kind: property
scope: selfhost/lexer/lexer.byn
proof: tests/Bayan.Tests/SelfhostLexerTests.cs
check: dotnet test -v q --filter "Law=L001"
timeout: 1800
enacted: 2026-10-20
---

## Statement
For every byte sequence (including invalid UTF-8, unbalanced interpolation, non-NFC identifiers and bidi controls), `bayan-lex` and the C# lexer produce identical structured token dumps (`--json`: kinds, spans, values, diagnostics with codes, spans and fixes). Both lexers read Unicode data from the same generated tables in the repository; neither consults the system's ICU.

## Counterexample looks like
An input `let é = 1;` written with `e` + U+0301 where the C# JSON dump has a BYN0005 diagnostic with fix `é` and the Bayan JSON dump has none, or the two dumps differ in one token's span.
