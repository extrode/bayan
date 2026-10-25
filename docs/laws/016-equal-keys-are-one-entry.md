---
id: L016
title: Equal keys are one entry
status: enacted
kind: property
scope: src/Bayan.Backend.C/CEmitter.cs
proof: tests/Bayan.Tests/MapKeyLawTests.cs
check: dotnet test -v q --filter "Law=L003|Law=L016"
enacted: 2026-10-25
---

## Statement
For every key type from the key generator (tuples and structs mixing `bool`, `i8`, `i64`, `rune`, `string`, `Symbol`, plain enums and nested structs, in field orders that leave padding), two keys that are `==` but built separately are one entry (`length` counts them once and the last write wins), two keys that differ are two entries, and L003's model (MapModelTests) holds over these key types; every generated program the checker accepts compiles with the C compiler without errors or warnings under `-Wall -Werror` (compiled standalone).

## Counterexample looks like
`m[Named { flag: true, name: "n7", kind: "k7" }] = 1` then `m[named(7)] = 2`, with `named` building the same value with an interpolated `$"n{id}"`, and `m.length` printing `2`; or two keys that differ only in their last field sharing one entry.

## Proof notes
`tests/Bayan.Tests/MapKeyLawTests.cs`, tagged `[Trait("Law", "L016")]` on the class, and `MapModelTests.L003GeneratedKeyTypeMatchesModel`, tagged L003. Both use `MapModelTests.KeyGen`. It generates a struct or tuple of `bool`, `i8`, `i64`, `rune`, `string`, `Symbol` and the plain enum `Tint`, with one level of nested structs and tuples. Every composite has a small part before a large one, so its layout has padding. A struct nests only structs, because a tuple-typed struct field does not parse. A key value is a mixed-radix code over the leaves. `Literal(code)` writes the key in place, and `make(i64 code)` builds the same value in another function, with `string` and `Symbol` parts made by interpolation and `Symbol.intern`. Each program uses 8 codes: one random code, its neighbours that differ from it in exactly one leaf, then more random codes.

- `EqualKeysAreOneEntry` (seeds 1–12): map `m` gets each key as a literal and then through `make`, and map `n` the other way round. Both must end with 8 entries, the second write must win, and `contains` must be true.
- `PaddedKeysBuiltApartAreOneEntry`: fixed padded layouts `Pad(bool, i64)`, `Small(i8, i64)`, `Tail(i64, rune)`, `Named(bool, string, Symbol)`, `Outer(bool, Small)`, `(bool, i64)` and `(i8, string)`. Each key is written once as a literal and once built field by field in another function after a stack smear (`smear`).
- Every L016 program must compile with no diagnostics. Each is built as `out.c` standalone with `-std=c11 -fwrapv -Wall -Werror`, in debug (`-O0 -DBYN_DEBUG`) and safe (`-O2 -DBYN_NDEBUG`) mode (runtime with `-w`), and then run. With no C compiler found, the test writes `skipped: no C compiler found` and returns.
- `L003GeneratedKeyTypeMatchesModel` (seeds 1–16, every fourth a plain `Symbol` key) runs L003's operations against L003's model. The operations are set, remove, `slot +=`, `bump(ref slot)` and clear, plus a `length`, iteration order and `tryGet` check at each phase. Each key is written at random as a literal or through `make`.

Mutations (`CEmitter`, restored after each run, `git diff` empty):
- A: `Equal` and `KeyHash` skip the last field of a struct or tuple. All 12 `EqualKeysAreOneEntry` seeds failed (fewer than 8 entries), and 12 `L003GeneratedKeyTypeMatchesModel` seeds failed (e.g. `length 3` where the model has 6).
- B: an unused `int unused = 0;` in the composite hash body. All 13 L016 tests failed with `-Wunused-variable` under `-Werror`. The L003 tests passed, because the toolchain build uses `-w`.
- C: composites hash `byn_hash_bytes(&k, sizeof k)` again. 9 of 12 `EqualKeysAreOneEntry` seeds, 7 L003 seeds and `PaddedKeysBuiltApartAreOneEntry` failed. All of these failures come from the `string` parts: raw bytes hash the string's pointer. Under this mutation the padding program prints `1 1 1 2 1 1 2`, so the padding-only maps keep one entry. On zig cc and Apple clang (macOS arm64), no Bayan program I wrote gives equal keys different padding bytes, in debug or safe mode (handoff Q415). So the padding half of the claim holds today by construction rather than being shown by a failing mutation.

Measured 2026-10-25 on macOS arm64: 61 tests under the L003|L016 filter, about 30 s.
