# 001 — Tasks
Plan: ./001-plan.md

Each milestone task ends with `dotnet test -v q` green, a docs page + `diag/` case per new diagnostic and a `run/` case per runtime feature (R12).

## M13 Program entry and I/O
- [x] T1 Self-hosting readiness section — files: `docs/bayan-handoff.md` — covers: R15 — done when: every row of `docs/bayan-self-hosting.md`'s table appears with status (v0.1 / M13–M23 / next spec) and the file-I/O/args correction is noted.
- [ ] T2 Attributes on declarations and error variants — files: `src/Bayan.Syntax/Parsing/Parser.cs`, `Nodes.cs` — covers: R1 — done when: `[ExitCode(3)]` parses; unknown attribute name is a diagnostic with `diag/` case.
- [ ] T3 `main` signatures + argv + exit codes — files: `Binder.cs`, `CEmitter.cs`, `bayan-rt.{h,c}` — covers: R1 — done when: `run/` args echo prints `a` / `b c`; `main` returning 7 exits 7; failing `main` exits its `[ExitCode]` or 1; non-UTF-8 argv exits 1 (Q41).
- [ ] T4 Builtin `Io` + `IoError` — files: `Builtins.cs`, `BuiltinMembers.cs`, `CEmitter.cs`, `bayan-rt.{h,c}` — covers: R2 — done when: `run/` cases copy file (byte-identical), missing file handled, invalid UTF-8 text fails / bytes succeed, stdout/stderr write without newline.

## M14 Growable strings
- [ ] T5 Growable `StrBuf()` — files: `Builtins.cs`, `RegionAnalysis.cs`, `CEmitter.cs`, `bayan-rt.{h,c}` — covers: R3 — done when: `run/` 100,000 appends in an arena gives the summed length; no buffer size in source.
- [ ] T6 `$"…"` as a `string` expression anywhere — files: `BinderExpressions.cs`, `Lowerer.cs` — covers: R3 — done when: BYN2224 lifted where an allocator exists; `run/` case; region diag case for escape.

## M15 Processes, directories, lex-diff
- [ ] T7 `Io.listDir` + `Io.run` — files: `Builtins.cs`, `CEmitter.cs`, `bayan-rt.{h,c}` — covers: R13 — done when: `run/` cases spawn `bayan --version` (exit 0, stdout `bayan `), missing program → error handled, three-file dir lists 3.
- [ ] T8 `tools/lex-diff/main.byn` + CLI test — files: `tools/lex-diff/main.byn`, `tests/Bayan.Tests/LexDiffTests.cs` — covers: R14 — done when: `bayan lex` vs goldens exits 0; tampered copy exits 1 naming file and line.

## M16–M17 Enums
- [ ] T9 Plain enums + members + exhaustive switch — files: `Parser.cs`, `Nodes.cs`, `Symbols.cs`, binder, `CEmitter.cs` — covers: R5 — done when: `run/` goldens for `count/values/name/ordinal/fromOrdinal/parse`; missing-member `diag/`.
- [ ] T10 Module-level `const T[N]` static data — files: binder, `CEmitter.cs` — covers: R9 (tables) — done when: `run/` reads a static array; non-literal const array is a diagnostic (Q51).
- [ ] T11 Enums with associated data — files: `Parser.cs`, `ConstantFolding.cs`, `CEmitter.cs` — covers: R5 — done when: `run/` Planet case; non-constant argument `diag/`.

## M18 Unions
- [ ] T12 Union declarations, construction, format, equality — files: `Parser.cs`, `Nodes.cs`, `Symbols.cs`, `Types.cs`, `CEmitter.cs` — covers: R4 — done when: `run/` prints `Circle(r: 1)`, equality by tag+payload.
- [ ] T13 Union patterns in `switch`/`is` + exhaustiveness — files: `BoundNodes.cs`, binder, `Lowerer.cs` — covers: R4, L4 — done when: payload binding `run/` case; missing variant `diag/` names the variant.

## M19 Generics
- [ ] T14 Generic functions (inference, monomorphization, mangling) — files: `Types.cs`, `Symbols.cs`, `BinderCalls.cs`, `Compilation.cs`, `CEmitter.cs` — covers: R6 — done when: one function at three types correct; emitted C has exactly three instantiations; illegal template ops `diag/`.
- [ ] T15 Generic structs/unions — files: `TypeResolver.cs`, binder, `CEmitter.cs` — covers: R6 — done when: `Pair<A,B>` and a generic union `run/` cases.

## M20–M21 Map and Owned
- [ ] T16 `Map<K,V>` — files: `Types.cs`, `TypeResolver.cs`, `BuiltinMembers.cs`, `CEmitter.cs`, `bayan-rt.{h,c}` — covers: R7, L3 — done when: 10,000 inserts with repeats + removals `run/` case; bad key type `diag/`.
- [ ] T17 `Owned<T>` and recursive types — files: `Types.cs`, binder, `RegionAnalysis.cs`, `CEmitter.cs` — covers: R8 — done when: expression-tree `run/`; node escaping arena `diag/`; direct recursion `diag/`.

## M22 Bayan lexer
- [x] T18 `tools/Bayan.UnicodeGen` + `selfhost/lexer/unicode-tables.byn` — covers: R9 — done when: generator asserts pass and the table file is checked in.
- [ ] T19 Port lexer to `selfhost/lexer/*.byn` — covers: R9 — done when: builds with the bootstrap compiler; `lex-diff --golden .tokens tests/cases/lex` exits 0.
- [ ] T20 `SelfhostLexerTests` differential harness — files: `tests/Bayan.Tests/SelfhostLexerTests.cs` — covers: R11 — done when: a theory over every `lex/` golden passes; disagreement names file + first line.

## M23 Corpus and timing
- [ ] T21 10,000-input corpus via `--many` — covers: R11, L1, L2 — done when: corpus test passes; no panic; spans inside input.
- [ ] T22 Timing check — covers: R10 — done when: median of 5 Bayan ≤ 10× C#; `BAYAN_SKIP_TIMING` opt-out.
- [ ] T23 Handoff Questions 40–54, progress log, spec sections touched — covers: R12 — done when: handoff updated.

## Laws
- [ ] T24 enact L001 lexer equivalence (drafted proposed; user enacts)
- [ ] T25 prove L001 via /prove — depends: T21
- [ ] T26 enact L002 no lexer panic
- [ ] T27 prove L002 via /prove — depends: T21
- [ ] T28 enact L003 map order
- [ ] T29 prove L003 via /prove — depends: T16
- [ ] T30 enact L004 union exhaustiveness
- [ ] T31 prove L004 via /prove — depends: T13
