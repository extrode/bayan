---
id: L002
title: The Bayan lexer never panics and keeps spans inside the input
status: proposed
kind: property
scope: selfhost/lexer/lexer.byn
---

## Statement
For every byte sequence, `bayan-lex` terminates with exit code 0 or 1 (never a panic, exit 134), and every token span, doc-comment span, diagnostic span and fix span it produces lies within `[0, input length]`.

## Counterexample looks like
A file ending in `$"{` followed by an invalid UTF-8 byte on which `bayan-lex` prints `panic: index out of range`, or a token with span start 10 in a 9-byte file.
