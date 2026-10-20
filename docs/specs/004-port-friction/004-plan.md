# 004 — Plan
Spec: ./004-spec.md

## Approach

Nine small language changes plus one new tool surface, each landing where the existing rule lives. Argument names (R1/R2) are a widening of `MustBeNamed` in `BinderCalls.cs` with one new error for a swap. Contextual keywords (R3) are a parser change: the two lexers keep emitting keyword token kinds (so token dumps and L001 are untouched except `law`, which leaves the reserved range in both enums), and the parser accepts the twelve kinds wherever it expects a name, using the one-token lookaheads it already has for `error enum` and `type X =`. Rejected: lexing them as identifiers, which would change every lex golden and force text comparisons in the parser. Literal inference (R4) rebinds a function body once with seeded local types when a first pass finds an unannotated `var` integer literal constrained to another integer type; the common case stays single-pass. Rejected: patching `LocalSymbol.Type` in place mid-bind, which leaves already-bound uses typed `i32`. The typed dump (R5) is a new `bayan types <path>` command writing JSON with `Utf8JsonWriter`, modelled on `bayan parse --ast`; L009 proves determinism over the goldens.

Own-field borrows (R6) add a receiver region to `RegionAnalysis`: a `this`-rooted `Lend` gets depth 0 (the caller's data), `CheckReturn` accepts it only when the function returns a span type, and `Call()` lends the receiver at call sites of such methods, so BYN3004 falls out of the existing loan machinery. Decode (R7) is `string.from(bytes) fails Utf8Error` plus `string.tryFrom` as in spec §3; because `error enum` variants carry no payload and `error union` is not implemented, the offset is exposed as `string.invalidUtf8At(bytes)` returning `i64?` (recorded as a Question). `catch { block }` (R8) is a new `BlockExpr` AST node bound to a `BoundBlockExpr`; the "must not complete normally" check lives in `FlowAnalysis`, which already knows reachability. R9–R11 extend `BindStaticArray`, `BuiltinMembers` and the C runtime along existing patterns (`ListRemoveLast`, `IoReadText`).

## Tech / dependencies

- No new packages. `System.Text.Json` `Utf8JsonWriter` (already used by `DiagnosticJson.cs`), `System.Text.Rune.DecodeFromUtf8` as the L010 reference.
- C runtime: `src/runtime/bayan-rt.h` / `bayan-rt.c` gain `byn_utf8_invalid_at`, `byn_to_hex`; the list helper for `removeRange` is emitted per element type via `_c.Helper` like `bynh_pop_`.
- Goldens: new kind `tests/cases/typed/` (`*.byn` → `*.types`), registered in `tests/cases/README.md`.

## Changes by area

- `src/Bayan.Syntax/Diagnostics/DiagnosticCodes.cs` — add `ArgumentsSwapped` BYN2307, `CatchBlockCompletes` BYN2611; reword BYN2229 to include `string`s — R2, R8, R9
- `src/Bayan.Syntax/Lexing/TokenKind.cs` — move `LawKeyword` above the reserved block so `IsReservedKeyword` (ActorKeyword..KeywordLast) no longer covers it — R3
- `src/Bayan.Syntax/Parsing/Parser.cs` — `TokenKindFacts.IsContextualKeyword` helper; `ExpectName` (128), primary `case TokenKind.Identifier` (1655), member name after `Dot` (1524), parameter/field/type names accept those kinds; declaration dispatch (288–325) keeps keyword meaning only when the next token continues the declaration form; `ParseCatch` (1400) takes `ParseBlock()` on `{` — R3, R8
- `src/Bayan.Syntax/Ast/Nodes.cs`, `AstDump` — `BlockExpr(Span, BlockStmt Block) : Expr`; dumped as `(block …)` — R8
- `selfhost/lexer/tokens.byn` — move `lawKeyword` out of `actorKeyword..ensuresKeyword`; `isReservedKeyword` range unchanged in form — R3
- `src/Bayan.Semantics/Binding/BinderCalls.cs` — `BindArguments`/`MustBeNamed` (209–288): implied names from identifier or member-access tail; swap check → BYN2307; `BindCatch` (541–598) binds `BlockExpr` handlers in a scope holding the binding — R1, R2, R8
- `src/Bayan.Semantics/Binding/Binder.cs` — `BindLocalDecl` (410): untyped `var` integer literals are pending; a seed table overrides `DefaultType`; `BindStaticArray` (213) accepts string literals — R4, R9
- `src/Bayan.Semantics/Binding/BinderExpressions.cs` — `Convert` (50) and `BindBinary` (838) record the first integer type that constrains a pending local; `CheckedIntegerLiteral` (298) re-checks range; static member on `BoundTypeRef` to `string` next to the `Io` case (481); `"Utf8Error"` in the builtin type-name list (359) — R4, R7
- `src/Bayan.Semantics/Compilation.cs` — `BindFunction` runs the two-pass scheme; `DeclareBuiltinVariants` (192) numbers `Utf8Error` after `IoError` — R4, R7
- `src/Bayan.Semantics/Builtins.cs`, `Symbols/Symbols.cs`, `TypeResolver.cs` — `Utf8ErrorSource`, `BuiltinFunction.StringFrom/StringTryFrom/StringInvalidUtf8At`, `MakeIo` takes the error set; resolve name `Utf8Error` (146) — R7
- `src/Bayan.Semantics/Binding/BoundNodes.cs`, `BoundTree` — `BoundBlockExpr`, `BuiltinMember.ListRemoveRange`, `BuiltinMember.IntToHex`; child enumeration — R8, R10, R11
- `src/Bayan.Semantics/Binding/BuiltinMembers.cs` — List `removeRange(start, end)` mutating; integer `toHex()`; `Candidates` names — R10, R11
- `src/Bayan.Semantics/Binding/RegionAnalysis.cs` — receiver region in `Lend`/`AllocatedFrom` (343–363), `CheckReturn` span-only acceptance, `Call()` lends receiver (rule 4, ~333); fresh-result and allocation lists (333, 337) add the string builtins; `Builtin()` (272) → `Mutate` for `ListRemoveRange` — R6, R7, R10
- `src/Bayan.Semantics/Binding/FlowAnalysis.cs` — `BoundCatch` (267–277): block handler reachable at end with a non-void operand → BYN2611 — R8
- `src/Bayan.Semantics/Binding/TypedDump.cs` (new) — `ToJson(Compilation)` — R5
- `src/Bayan.Lowering/Lowerer.cs` — `Catch()` (1068) inlines block handlers; mutating list (964) adds `ListRemoveRange` — R8, R10
- `src/Bayan.Backend.C/CEmitter.cs` — `Builtin()` (1031): `bynh_remove_range_` helper, `byn_to_hex` call with an unsigned cast at type width; builtin functions (957–1002): the three string statics; `StaticArray`/`Constant` (581) emit `BYN_STR` elements — R7, R9, R10, R11
- `src/runtime/bayan-rt.h`, `bayan-rt.c` — `int64_t byn_utf8_invalid_at(const uint8_t*, int64_t)` (-1 when valid, reusing the `byn_utf8_valid` decoder at rt.c:516), `byn_str byn_to_hex(uint64_t, byn_alloc*)` — R7, R11
- `src/Bayan.Cli/CliApp.cs` — `types` command next to `Parse()` (157) — R5
- `docs/diagnostics/BYN2307.md`, `BYN2611.md` (new), `BYN2305.md`, `BYN2229.md`; `docs/typed-dump.md` (new); `docs/bayan-spec.md` §2.3, §2.4, §3, §8; `docs/laws/009-*.md`, `010-*.md`, `README.md`; `docs/bayan-handoff.md` — R3, R4, R5, R7, R12
- `tests/cases/{diag,run,parse,lex,typed}/` goldens; `tests/Bayan.Tests/TypedDumpTests.cs`, `L010Utf8DecodeTests.cs` (new) — R12, L009, L010

## Data / interfaces

- BYN2307 `ArgumentsSwapped` (error): "arguments `{0}` and `{1}` are in the wrong order for `{2}`"; span = the call's argument list; help: name them or swap them. Fires only when every unnamed argument has an implied name and all match parameters (spec open question); otherwise BYN2305 as today.
- BYN2611 `CatchBlockCompletes` (error): "this `catch` block can complete normally; end it with `return`, `break`, `continue` or a panic"; span = the block's closing brace. Not reported for a `void` operand.
- Decode: `string string.from(ReadOnlySpan<u8> bytes) fails Utf8Error`, `string? string.tryFrom(ReadOnlySpan<u8> bytes)`, `i64? string.invalidUtf8At(ReadOnlySpan<u8> bytes)`. `public error enum Utf8Error { invalidSequence }` declared from source like `IoError`, variant code 8. Results are fresh copies from the context allocator (as `Io.readText`).
- `List<T>.removeRange(i64 start, i64 end)`: void, mutating, panics "slice out of range" via `byn_slice_check`. `toHex()` on every integer: `string`, lowercase, no prefix, two's complement at width.
- CLI: `bayan types <file|dir>` prints the dump to stdout, diagnostics to stderr, exit 1 on errors. JSON (`Utf8JsonWriter`, indented, `UnsafeRelaxedJsonEscaping`):
  `{"version":1,"functions":[{"path":"main.byn","name":"Lexer.tokens","span":{"start":n,"end":n},"bindings":[{"name":"i","kind":"param|var|let|const|for|pattern|catch","span":{"start":n,"end":n},"type":"i64"}]}]}`.
  Functions in module file order then span; bindings in bind order (parameters first); generic templates only, no instances; `path` is relative to the compilation root; types via `DisplayName`.
- Golden `tests/cases/typed/*.byn` → `*.types`.

## Risks & mitigations

- R4 two-pass binding re-enqueues generic instantiations and re-declares locals. Mitigation: pass 1 binds into a scratch `DiagnosticBag`; only bodies containing an untyped `var` literal take the path; instantiation queue is drained after all bodies anyway (Compilation 774). Verify with a diag golden mixing generics and a constrained literal.
- R3 ambiguity at statement start (`type = 1;`, `error(x)`, `set.add(1)`). Mitigation: keyword meaning only with the existing lookaheads; parse golden `contextual-keywords.byn` covers each of the twelve in name position and in declaration position.
- R6 unsoundness if receiver borrows leak through `string` returns. Mitigation: span return types only; a `string` view of an own field stays BYN3003 (Question).
- R7 without a payload the offset is a second call. Mitigation: `invalidUtf8At` shares the runtime decoder, L010 ties the three together.
- R8 `JumpExpr`/never typing: a non-void `catch { … }` is typed `never` so the `catch` keeps the operand's value type; FlowAnalysis guarantees the `never` claim or reports BYN2611.
- Lex golden `reserved.tokens` changes if it lists `law`; L001 runs both lexers so the selfhost change is verified, not assumed.
- Allocation for `toHex()`/`string.from` outside an allocator context: follow the `IoReadText` context check at RegionAnalysis 337 so the existing diagnostic applies.

## Coverage check

| Req | Section |
|---|---|
| R1 | Changes: BinderCalls; Task 1 |
| R2 | Data: BYN2307; Task 1 |
| R3 | Changes: TokenKind, Parser, tokens.byn; Tasks 2–3 |
| R4 | Approach; Changes: Binder, BinderExpressions, Compilation; Task 4 |
| R5 | Data: CLI + JSON; Changes: TypedDump, CliApp; Task 5 |
| R6 | Changes: RegionAnalysis; Task 8 |
| R7 | Data: decode; Changes: Builtins, runtime, CEmitter; Tasks 9–10 |
| R8 | Data: BYN2611; Changes: Nodes, Parser, BinderCalls, FlowAnalysis, Lowerer; Task 13 |
| R9 | Changes: Binder.BindStaticArray, CEmitter.StaticArray; Task 14 |
| R10 | Changes: BuiltinMembers, RegionAnalysis, CEmitter; Task 15 |
| R11 | Changes: BuiltinMembers, runtime; Task 15 |
| R12 | Changes: docs/diagnostics, goldens; every task's done-when |
| L009 | Tasks 6–7 |
| L010 | Tasks 11–12 |

## Decisions for handoff Questions

- Q71 Decode naming and offset: `string.from` / `string.tryFrom` per §3; the offset is `string.invalidUtf8At(bytes): i64?` because `error enum` has no payload and `error union` is unimplemented. Fold into the error when payloads exist.
- Q72 R2 scope: BYN2307 only when all unnamed arguments carry implied names matching parameters; mixed calls fall back to BYN2305.
- Q73 Contextual keywords are still lexed as keyword kinds; the parser decides. `law` leaves the reserved range. Statement recovery (`IsStatementKeyword`) keeps `error`/`type`.
- Q74 R4 is two-pass per function body; "first constraining use" is bind order. Only integer targets retype a pending literal; a float or string constraint leaves `i32` and reports as today.
- Q75 R6 applies to span return types only; `string` views of own fields and `return sb.asString()` stay as recorded.
- Q76 Typed dump covers templates, not instances; paths are compilation-root-relative; JSON version field starts at 1 for spec 010.
- Q77 `catch { }` with a non-void operand is typed `never`; a block that completes normally is BYN2611 from FlowAnalysis, not the binder.

## Task order

1. **R1/R2 argument names** — `BinderCalls.cs`, `DiagnosticCodes.cs`; `docs/diagnostics/BYN2307.md`, `BYN2305.md`; diag goldens `argument-implied-name.byn`, `arguments-swapped.byn`. Done when goldens and `DiagnosticCodesTests` pass. No deps.
2. **R3 C# side** — `TokenKind.cs`, `Parser.cs`; parse golden `contextual-keywords.byn`, lex golden update. Done when `ParserTests`, `LexerTests` pass and existing goldens are unchanged except `reserved`. No deps.
3. **R3 selfhost** — `selfhost/lexer/tokens.byn`. Done when `dotnet test -v q --filter "Law=L001"` passes. Depends on 2.
4. **R4 literal inference** — `Binder.cs`, `BinderExpressions.cs`, `Compilation.cs`; diag golden for out-of-range after inference; spec §2.3. Done when run goldens still pass and `var i = 0; while (i < xs.length)` binds `i64`. Depends on 1.
5. **R5 typed dump** — `TypedDump.cs`, `CliApp.cs`, `docs/typed-dump.md`, `tests/cases/typed/` (five spec cases), `TypedDumpTests.cs` golden theory. Done when goldens pass. Depends on 4.
6. **prove L009** — `TypedDumpTests.cs` `[Trait("Law","L009")]`: dump twice and from a relocated copy for every `typed/` and `run/` case. Done when `dotnet test -v q --filter "Law=L009"` passes. Depends on 5.
7. **enact L009** — `docs/laws/009-typed-dump-deterministic.md` (status enacted, check command), `docs/laws/README.md`. Depends on 6.
8. **R6 receiver borrows** — `RegionAnalysis.cs`; diag goldens `borrow-own-field.byn` (accepted) and `borrow-own-field-mutated.byn` (BYN3004); `valid-borrows` update. Done when `DiagTests` pass. No deps.
9. **R7 semantics** — `Builtins.cs`, `Symbols.cs`, `TypeResolver.cs`, `Compilation.cs`, `BinderExpressions.cs`, `RegionAnalysis.cs`. Done when the three statics bind and `Utf8Error` resolves. No deps.
10. **R7 runtime + emit** — `bayan-rt.h`, `bayan-rt.c`, `CEmitter.cs`; run goldens `string-from.byn`, `string-from-invalid.byn`. Done when `RunTests` pass. Depends on 9.
11. **prove L010** — `L010Utf8DecodeTests.cs` `[Trait("Law","L010")]`: a compiled Bayan driver over edge-case and random byte sequences versus `Rune.DecodeFromUtf8`. Done when `dotnet test -v q --filter "Law=L010"` passes. Depends on 10.
12. **enact L010** — `docs/laws/010-utf8-decode-exact.md`, `docs/laws/README.md`. Depends on 11.
13. **R8 catch block** — `Nodes.cs`, `AstDump`, `Parser.cs`, `BoundNodes.cs`, `BinderCalls.cs`, `FlowAnalysis.cs`, `Lowerer.cs`, `DiagnosticCodes.cs`; `docs/diagnostics/BYN2611.md`; parse golden, run golden `errors-catch-block.byn`, diag golden `catch-block-completes.byn`. Done when all pass. Depends on 2.
14. **R9 const string arrays** — `Binder.cs`, `CEmitter.cs`; `docs/diagnostics/BYN2229.md`; run golden `const-string-array.byn`, `invalid-const-array` update. No deps.
15. **R10/R11 removeRange, toHex** — `BuiltinMembers.cs`, `BoundNodes.cs`, `Lowerer.cs`, `RegionAnalysis.cs`, `CEmitter.cs`, `bayan-rt.h/.c`; run goldens `list-remove-range.byn`, `list-remove-range-panics.byn` (`.exitcode`, `.stderr`), `to-hex.byn`; `mutated-while-borrowed` gains a `removeRange` line. Depends on 10 (shared runtime edit).
16. **Docs** — `docs/bayan-spec.md` (§2.3, §2.4, §3, §8, lists), `docs/bayan-handoff.md` (Q71–Q77, road table, progress log), `tests/cases/README.md`. Depends on all.

Noticed, not fixed: `Parser.IsStatementKeyword` lists `ErrorKeyword`/`TypeKeyword`, so recovery after R3 may skip a statement that starts with `error =`.
