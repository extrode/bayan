---
id: L001
title: The Bayan lexer and the C# lexer agree on every input
status: proposed
kind: property
scope: selfhost/lexer/lexer.byn
---

## Statement
For every byte sequence (including invalid UTF-8, unbalanced interpolation, non-NFC identifiers and bidi controls), `bayan-lex` built from `selfhost/lexer/` prints exactly the same token dump, rendered diagnostics included, as the C# lexer (`TokenDump.Dump(Lexer.Lex(...))`, the output of `bayan lex`). This holds per pinned Unicode version: the tables in `selfhost/lexer/unicode-tables.byn` and the system ICU must be the same version (handoff Q6, Q49).

## Counterexample looks like
An input `let é = 1;` written with `e` + U+0301 where the C# dump reports BYN0005 with fix `é` and the Bayan dump reports no diagnostic, or the two dumps differ in one token's span.
