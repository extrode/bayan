# 002 — Plan
Spec: ./002-spec.md

## Approach

Six independent strands, ordered so each lands green:

1. Map API.
2. L004 coverage.
3. Shared Unicode tables, the JSON dump and stdin.
4. Explicit `copy`.
5. Ownership.
6. `Untrusted<T>` and `runBatch`.

Laws are proved as each strand lands.

**Ownership** is a check layered on the existing bound tree, not a new IR:
- `TypeFacts.IsUnique(type)` is a recursive predicate with a `seen` set.
- A new `OwnershipAnalysis` pass runs per function after `FlowAnalysis`, using the same path-sensitive `State` shape. It holds a set of *moved* locals with a union join; loops iterate to a fixpoint.

It replaces `RegionAnalysis.CheckCopy` (BYN2762). The alternative, extending `RegionAnalysis`, mixes two lattices in one walker that is already 630 lines.

Over-strictness is preferred to holes. Every rule below that rejects valid programs is recorded as a handoff Question with the strict option implemented.

**Unicode:** `Nfc.cs` already exists as the C# reference algorithm. It moves into `Bayan.Syntax`, and `Bayan.UnicodeGen` emits `UnicodeTables.g.cs` beside the `.byn` tables from one UCD read. The generator's ICU cross-check stays in the generator, which is a dev tool, not the lexer.

## Tech / dependencies

- .NET 10, xUnit and `zig cc`, as today.
- No new packages.
- Unicode 16.0.0, pinned (handoff Q49).

## Changes by area

### Map API (R5, L003)

- `src/Bayan.Semantics/Binding/BuiltinMembers.cs`:
  - drop `get`, `set` and `count`;
  - add `getOr(key, default)`, `slot(key)` (mutating, returns a `ref V`), `length` and `contains(key)` (renamed from `containsKey`);
  - `remove` and `clear` stay.
- Binder:
  - `map[k]` read binds to `MapIndex`;
  - `map[k] = v` and compound `map[k] += v` bind to `MapIndexSet`;
  - `slot` on a non-defaultable `V` reports **BYN2785**. Defaultable means numeric, bool, char, string, optional, or a struct whose fields are all defaultable or have initializers. That is the existing default-value rule if one exists; otherwise this strict one.
- `src/runtime/bayan-rt.{h,c}`:
  - `byn_map_index` panics `key not found in map`;
  - add `byn_map_get_or` and `byn_map_slot` (insert default, return a pointer).
  - The C names may change freely.
- Lowering and `CEmitter`:
  - emit the new members;
  - `ref map.slot(k)` lowers to a pointer.
- Migrate `tests/cases/run/{map-many,map-model,map-basics,map-copy-allowed,generics-types}.byn`, `MapModelTests.cs` and any `diag/` cases that use the old names.
- New `run/map-index-panic`, `run/map-slot` and `diag/map-slot-no-default`.
- `docs/diagnostics/BYN2785.md`.
- Spec §16 already describes the API.

### L004 (R9)

- Binder switch exhaustiveness (BYN2410), wherever the coverage set is computed in `src/Bayan.Semantics/Binding/`:
  - an arm with `when` adds nothing;
  - an `or` pattern adds each alternative;
  - a variant pattern adds its variant only if every subpattern is a `let`/`var` binding or `_`;
  - `default:` / `_ =>` covers all.
- The message lists every uncovered variant in declaration order.
- Fix whatever differs, and add a `diag/` case per gap found.

### Unicode tables (R6, L001)

- `tools/Bayan.UnicodeGen`:
  - add `--cs-out <path>` writing `src/Bayan.Syntax/Lexing/UnicodeTables.g.cs`, with the same arrays as the `.byn` file as `static ReadOnlySpan<uint>` properties (data in the binary, no allocation, AOT-safe);
  - the README documents both outputs and one command regenerating both.
- `src/Bayan.Syntax/Lexing/Nfc.cs`:
  - `Nfc.cs` moves here from the generator, driven by `UnicodeTables`;
  - the generator references this file through a `<Compile Include>` link, so there is one copy.
- `Lexer.cs`:
  - `IsIdentifierStart`/`Continue` use binary search over the range tables;
  - the NFC check uses `Nfc.IsNormalized`/`Nfc.Normalize`;
  - `using System.Globalization` goes away if unused.
- Test `LexerTests.NoSystemUnicode` reads `src/Bayan.Syntax/Lexing/*.cs` (except the generated file) and fails on `Normalize(`, `IsNormalized(` or `GetUnicodeCategory`.

### JSON dump and stdin (R7, R8)

The format is one JSON object:

```json
{"tokens":[{"kind":"Identifier","start":0,"end":3,"text":"abc","doc":false}],
 "diagnostics":[{"code":"BYN0005","severity":"error","start":0,"end":3,"message":"…","label":"…","help":"…","fixes":[{"start":0,"end":3,"replacement":"…"}]}]}
```

- Keys appear in that order, with no whitespace.
- Strings are escaped per RFC 8259: `\"`, `\\`, `\n`, `\r`, `\t`, `\u00XX` for other controls; non-ASCII stays raw UTF-8. Invalid UTF-8 in token text is emitted as `�` per maximal subpart; both lexers already agree on spans, and the text is derived from spans.
- Absent `label`/`help` are written as `null`.
- `kind` is the C# `TokenKind` name. The selfhost lexer already prints the same names in its text dump.
- `doc` is true for a token with an attached doc comment.

Implementation:

- C#:
  - `TokenDump.ToJson` in `src/Bayan.Syntax/Lexing/TokenDump.cs`;
  - `bayan lex --json <file>` in `src/Bayan.Cli/CliApp.cs`, and `bayan lex -` reads stdin.
- Selfhost:
  - `selfhost/lexer/dump.byn` gets `dumpJson`;
  - `selfhost/lexer/main.byn` takes `--json`, and with no path or `-` reads stdin through `Io.stdin`/`readAll`. If stdin reading does not exist in the runtime, add `Io.stdin.readAll() -> Result<string, IoError>` (a runtime feature, so it gets a `run/` case).
- A read failure exits 4.

### Proofs L001/L002

- `tests/Bayan.Tests/LexDiffTests.cs`, or a new `LexerLawTests.cs`:
  - the corpus generator from spec 001 (10,000 inputs, fixed seed) pipes each input to `bayan-lex --json` on stdin;
  - it compares the output with `TokenDump.ToJson` in-process;
  - it asserts exit 0 or 1 and every span in `[0, len]`;
  - it asserts an unreadable path exits 4 and is excluded from L002.
- Tag with `[Trait("Law","L001")]` and `[Trait("Law","L002")]`.
- Record `proof:` in the law files.

### Explicit copy (R3)

- `BuiltinMembers`:
  - `List<T>.copy(to: Arena)`, `Map<K,V>.copy(to: Arena)` and `StrBuf.copy(to: Arena)` return the same type;
  - **BYN2784** when the element, key or value type is unique.
- Runtime: `byn_list_copy`, `byn_map_copy` and `byn_strbuf_copy` deep-copy into the arena's allocator; strings are values already.
- New `run/collection-copy`: copy, mutate both 100 times, print both.

### Ownership (R1, R2, R4)

- `src/Bayan.Semantics/TypeFacts.cs` (or beside the existing type helpers): `IsUnique(TypeSymbol)`.
  - Unique: `ListType`, `MapType`, `BuiltinKind.StrBuf`, `Owned<T>`, a struct marked `unique` or with a unique field, a union with a unique payload, and a tuple, array, optional or `Result` of a unique type.
  - A `TypeParameterType` in a generic body is unique. This is conservative; Question.
- Parser:
  - `move <expr>` as a prefix expression (`MoveExpression`), alongside the existing `ArgModifier.Move`;
  - a `move` argument modifier keeps parsing and binds the same way.
- Binder:
  - `BoundMove(local)`; the operand must be a local or a `move` parameter, else **BYN2782** (`cannot move out of …`);
  - `move` parameters stop reporting Unsupported and own their value;
  - plain parameters of unique type are borrows.
- `OwnershipAnalysis` (new file `src/Bayan.Semantics/Binding/OwnershipAnalysis.cs`), per function:
  - **Copy check (BYN2780).** A *place* expression of unique type in a storing position is an error. Storing positions: local initializer, assignment right-hand side, field initializer in construction or `with`, collection element (`add`, `map[k] = v`), `move`-parameter argument, `return` of a non-local, tuple, array and optional construction.
    - `return x` of a local or `move` parameter is an implicit move.
    - The diagnostic carries fixes `move x` (when `x` is movable), `x.copy(to: alloc)` (when the type has `copy`) and `ref`, using the existing fix machinery.
  - **Use after move (BYN2781).** Moved locals are tracked per path. A read of a moved local is an error naming the move site. Assignment clears it. Loops reach a fixpoint, so a move in a loop body of a local declared outside is reported.
  - **Borrowed bindings (BYN2783).** A `for` loop variable or switch pattern binding of unique type borrows from the subject. A `Mutating` member call or assignment through it is an error. This holds for `let` and `var` bindings alike; Question.
  - Fresh values (calls, constructors, `copy`, literals) are never copies.
- Remove `RegionAnalysis.CheckCopy`, `ReportCopy`, `SharedHandle` and `StoredOperands` if unused.
  - BYN2762 stays in `DiagnosticCodes` as retired (never reported), so codes are not reused.
  - `docs/diagnostics/BYN2762.md` gets "Retired in spec 002: replaced by BYN2780."
  - The `diag/map-copy` golden becomes a BYN2780 case; `run/map-copy-allowed` is migrated to `move`/`copy`.
- Migrate to `move`/`copy`/`ref` until the suite is green:
  - `selfhost/lexer/*.byn`, `tools/lex-diff/main.byn`, `examples/tally-lite/**`;
  - every `tests/cases/run|diag` program that now errors.
  - Diag goldens that change only because of new errors are re-reviewed, not blindly updated.
- New `diag/` cases:
  - `unique-implicit-copy`, `unique-use-after-move`, `unique-move-in-branch`, `unique-move-in-loop`, `unique-move-from-field`, `unique-struct-containment`, `unique-borrowed-mutation`, `collection-copy-unique-element`.
- New `run/` cases: `unique-move` and `unique-return`.
- `docs/diagnostics/BYN2780.md` … `BYN2784.md`.

### Untrusted (R10)

- `TypeResolver`: a builtin generic `Untrusted<T>` with a value layout identical to `T`. The C emitter treats it as `T`, with no wrapper struct.
- Binder:
  - `Untrusted(x)` constructs it;
  - `trust(x, reason: "…")` unwraps it. The reason must be a non-empty string literal, else **BYN2787**;
  - no implicit conversion to `T`, so the existing type-mismatch diagnostic applies.
- Untrusted is unique iff `T` is.
- New `run/untrusted-trust` and `diag/trust-reason`.
- `docs/diagnostics/BYN2787.md`.

### runBatch (R11, L005)

- `src/runtime/bayan-rt.c`: `byn_batch_cmdline(path, args, out)` is compiled on every host. It builds `cmd.exe /d /e:ON /v:OFF /s /c ""<path>" "<a1>" "<a2>""`.
  - It fails (returns an `IoError` kind `invalidInput`, or the closest existing kind) when the path or any argument contains NUL, CR, LF, `"` or `%`, or when the path does not end in `.bat`/`.cmd` (case-insensitive).
  - Inside quotes, `& | < > ^ ( )` and spaces are literal to `cmd` with `/v:OFF`.
  - Backslashes pass through.
  - The batch file reads `%~N` == `args[N-1]`.
- `byn_io_run_batch`:
  - on Windows it resolves `%SystemRoot%\System32\cmd.exe` and calls `CreateProcessW` with that application name and the built command line;
  - elsewhere it returns `IoError.unsupported` (or the closest existing kind).
- Binder: `Io.runBatch(string path, ReadOnlySpan<string> args) -> Result<ProcessResult, IoError>`. **BYN2786** for any argument expression whose type is or contains `Untrusted<T>`, checked on the argument's static type and on list or array literal elements.
- Tests:
  - `tests/native/batch-cmdline.c` is a driver calling `byn_batch_cmdline` over a table;
  - `RuntimeNativeTests` compiles it with `zig cc` against `bayan-rt.c` and compares its output on every host;
  - a Windows-only `run/` case round-trips arguments through an echo `.cmd` file, skipped elsewhere through the runner's existing platform skip, or a new one if none exists.
- `diag/run-batch-untrusted`, `docs/diagnostics/BYN2786.md`.
- Spec R11 wording: `Io.run` returns `permissionDenied` for batch files. The pointer to `runBatch` lives in the spec and the docs, since `IoError` carries no message.
- L005 statement, revised (still proposed): each `%~N` equals `args[N-1]`.

### Docs and bookkeeping (R13)

- `docs/bayan-handoff.md`: Q55 and Q56 marked answered (spec 002).
- New Questions, with the strict option implemented:
  - type parameters are unique in generic bodies;
  - borrowed loop and pattern bindings can't be mutated;
  - taint sources are not wrapped;
  - `%`/`"` are refused by `runBatch`;
  - `slot` defaultability rule.
- Progress log entry.
- `CHANGELOG.md`, `work/status/*`, and `work/todo.md` for out-of-scope finds.

## Data / interfaces

- New diagnostic codes, all semantics:
  - BYN2780 implicit copy of unique;
  - BYN2781 use after move;
  - BYN2782 cannot move out of a place;
  - BYN2783 mutation through a borrowed binding;
  - BYN2784 copy of a collection with unique elements;
  - BYN2785 `slot` needs a default;
  - BYN2786 untrusted argument to `runBatch`;
  - BYN2787 `trust` needs a reason.
- BYN2762 is retired.
- `bayan lex --json [file|-]` and `bayan-lex [--json] [file|-]`, plus the existing `--many` and `--time`.

## Risks & mitigations

- **Input handling: `runBatch` (security).**
  - The escape rules are a refuse-list on top of an always-quote rule, and the refuse-list covers every character `cmd` treats specially inside quotes (`%`, `"`, CR/LF/NUL).
  - The tests run on every host through the pure function.
  - L005 stays proposed until a Windows proof exists.
  - The code review gets a `review` worker lens on this file.
- **Ownership rejects real programs.** The migration is part of the task; the selfhost lexer and tally-lite are the stress test. The strict choices are recorded as Questions.
- **Map migration breaks goldens broadly.** It's done in one task with `BAYAN_UPDATE=1`, and the diffs are inspected.
- **The JSON dumps disagree on invalid UTF-8 text.** Token text is derived from spans with one shared replacement rule; the corpus test catches drift.
- **The generated C# tables bloat compile time.** `ReadOnlySpan<uint>` over constant data is compiled as an RVA blob, so cost is negligible.

## Coverage check

| Req | Section |
|---|---|
| R1, R2 | Ownership |
| R3 | Explicit copy |
| R4 | Ownership (BYN2762 retired) |
| R5 | Map API |
| R6 | Unicode tables |
| R7, R8 | JSON dump and stdin |
| R9 | L004 |
| R10 | Untrusted |
| R11 | runBatch |
| R12 | Proofs L001/L002, Map API (L003), L004 |
| R13 | Docs and bookkeeping, plus each section's docs pages |

None uncovered.
