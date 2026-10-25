---
id: L015
title: Interning is identity on text
status: enacted
kind: property
scope: src/runtime/bayan-rt.c
proof: tests/Bayan.Tests/SymbolLawTests.cs
check: dotnet test --filter Law=L015
enacted: 2026-10-25
---

## Statement
For every sequence of texts from the symbol generator (ASCII, multi-byte UTF-8, `""`, texts equal to the program's literals, prefixes and extensions of each other, texts built at run time from bytes), `Symbol.intern(a) == Symbol.intern(b)` exactly when `a == b`; `Symbol.intern(s).text == s`; a literal symbol equals `Symbol.intern` of the same text built at run time; `Symbol.intern(a) < Symbol.intern(b)` exactly when `a < b`; and the program's output is identical under two different hash seeds.

## Counterexample looks like
`let l: Symbol = "total"` and `Symbol.intern(s)` of `"total"` decoded from the bytes `74 6F 74 61 6C`, where `l == r` prints `false` because the literal was given the ID of its first appearance in the source while the runtime table holds the literals sorted; or `Symbol.intern("b") < Symbol.intern("a")` printing `true` because the IDs were compared instead of the texts; or the same program printing a different line when run under another hash seed.

## Proof notes
`tests/Bayan.Tests/SymbolLawTests.cs`, tagged `[Trait("Law", "L015")]` on the class. `InterningIsIdentityOnText` generates seeds 1..32 (raised by `BAYAN_L015_SEEDS`). Each program has 5 to 12 texts: the first from a fixed pool (`""`, `a`, `ab`, `abc`, `b`, `B`, `total`, `totals`, `x_1`, `é`, `aé`, `€`, `😀`, `a😀`, `｡`, `名前`, a text with `"` and `\`, `{z}`), each later one from the pool, 0 to 4 random runes (1 to 4 UTF-8 bytes, up to U+10FFFF), a rune prefix of an earlier text (possibly `""`), an earlier text extended by 1 or 2 runes, or a repeat of an earlier text. About half the distinct texts are also declared as symbol literals (`let lK: Symbol = "…"`). Each text is built at run time one of three ways: its UTF-8 bytes appended one by one to a `List<u8>` and decoded with `Utf8.decode`, its runes appended one by one to a `StrBuf` and read through `.view`, or a `string` literal. The texts are interned in shuffled order, so runtime IDs run against text order; in about half the seeds a recursive `grow(n)` first interns 70 to 149 other texts (`g1`, `g2`, …), which takes the table past its first capacity of 64 and rehashes it. The program prints, for every symbol (runtime and literal, a literal paired with its first runtime twin's string), `sym.text == str`, and for every ordered pair of distinct symbol names `a == b`, `a < b` and the strings' `<`; with `grow`, its sum of text lengths last. The expected output is computed in C# from the texts' UTF-8 bytes (equal bytes, and `SequenceCompareTo` for `<`), so both symbol `<` and `string` `<` are checked against byte order, which is code-point order. The literal's twin is checked by the `l == r` pair lines and by `l.text == s`.

For each seed the test requires no error diagnostics in-process, then for the debug and the safe emissions replaces `byn_hash_seed_init(false);` in `out.c` with a read of `BYN_L015_SEED` (hex), builds `out.c` standalone in the `BuildStandalone` pattern of L012 with `-std=c11 -fwrapv -Wall -Werror` (the runtime with `-w`), and runs it under seed `0` and seed `9e3779b97f4a7c15`. Each run must exit 0 and print exactly the expected output, and the two runs must print the same. It also asserts every generator choice (pool, random, prefix, extension, duplicate, empty, ASCII, multi-byte, bytes, `StrBuf`, string, literal, grow) is drawn by some seed. With no C compiler found it writes `skipped: no C compiler found` and returns. `DisagreeingOutputFails` flips the first expected line of seed 1 and requires the comparison to report it.

Generator shapes left out, each for `-Wall -Werror` on emission that is not about symbols: a symbol compared with itself (`r0 == r0` is `-Wtautological-compare`; equality of equal texts is covered by repeats and literal twins, which are two names), a `for` loop to grow the table (every `for` emits an unused `continue_N:` label, `-Wunused-label`; `let _ = …` in it also emits an unused C local), and `if n == 0` in `grow` (emitted as `if ((p_n == INT64_C(0)))`, `-Wparentheses-equality`; `n < 1` is used). No flag narrowing.

Mutation 1 (literals numbered in source order, so the runtime does not find them under their IDs): in `CEmitter.SymbolTable`, `return _symbolTexts.Select(t => sorted.IndexOf(t)).ToArray();` became `return Enumerable.Range(0, _symbolTexts.Count).ToArray();`, so literal N is the N-th distinct literal text first seen while the runtime table stays sorted. The test failed on 30 of 32 seeds, each `Debug output differs from the model under seed 0`, e.g. seed 1 at line 7, expected `true`, got `false` (a literal's `.text` against its twin's string). Seeds 18 and 26 draw no literal. Reverted, the test passes.

Mutation 2 (literal table never registered): in `CEmitter.EmitMain`, the `byn_sym_init(byn_symtab, …)` line became `(void)byn_symtab;`, so runtime interns number from 0 without the literals. The test failed on 29 of 32 seeds, e.g. seed 1 at line 8, expected `true`, got `false`. Reverted, the test passes.

Measured 2026-10-25 on macOS arm64: 32 seeds, all agreeing under both seeds and both modes, about 27 s.
