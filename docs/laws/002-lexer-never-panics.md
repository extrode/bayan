---
id: L002
title: The Bayan lexer never panics and keeps spans inside the input
status: enacted
kind: property
scope: selfhost/lexer/lexer.byn
proof: tests/Bayan.Tests/SelfhostLexerTests.cs
check: dotnet test -v q --filter "Law=L002"
timeout: 1800
enacted: 2026-10-20
---

## Statement
For every byte sequence supplied on stdin, `bayan-lex` terminates with exit code 0 or 1 (never a panic, exit 134), and every token span, doc-comment span, diagnostic span and fix span it produces lies within `[0, input length]`. I/O failures (an unreadable input, exit code 4) are outside this law.

## Counterexample looks like
Stdin ending in `$"{` followed by an invalid UTF-8 byte on which `bayan-lex` prints `panic: index out of range`, or a token with span start 10 in a 9-byte input.
