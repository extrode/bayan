# 011 — Plan
Spec: ./011-spec.md
Status: draft (2026-11-02)

Line counts below are `wc -l` on this worktree. C# ranges name methods in `src/Bayan.Lowering/` and `src/Bayan.Backend.C/`; locate by method when they drift. Sizes assume 010's measured 1.45 Bayan lines per C# line and the ~1,000-line task cap.

## Approach

`bayan-emit` is a second Bayan program next to `bayan-check`, laid out as 010 was: the entry module `selfhost/emitter/` holds `main.byn`, the lowering (a port of `Lowerer.cs` and `PowerAssert.cs`, split by its section comments) and the lowered-tree state file `lctx.byn` (a port of `LoweredNodes.cs` in 010's arena-and-handle style); the C printer is the nested module `selfhost/emitter/cprint/` (a port of `CEmitter.cs`, split at its class and section boundaries), the only place that builds C text (K1). The checker is imported, not copied: `import semantics` gives the `Ctx` that `create(from:, at:, by:)` (`selfhost/semantics/compile.byn:77`) returns, and the lowering reads its bound arenas and symbol tables through handles exactly as the C# lowerer reads `Compilation`. C# stays the reference. The C# `Lowerer.Lower(c, null, null)` and `CEmitter.Emit(program, ownershipChecks)` paths are ported method for method, in the same order and with the same names where Bayan allows; the mutant and coverage paths (`Lowerer.cs:1089-1172`, `CEmitter.cs:179-287` and the `LMutantIs`/`LCount` nodes) are not ported, since `CliApp.EmitC` (`CliApp.cs:409`) never passes them and the spec names them a non-goal. The test suite builds `bayan-emit` once in `fast` mode, runs it as a process on every `run/` input in that input's mode, and compares its stdout with `CliApp.EmitC` for the same compilation; the laws L024 and L025 are the same comparison over the generator's seeds.

Rejected: (1) one flat `selfhost/emitter/` module with the printer as "the files named `cprint*.byn`". A file is not a module boundary Bayan can see, and R3's test would rest on a naming convention; a nested module (`import emitter.cprint`, the form `tests/cases` already uses as `import app.users`) is a boundary the compiler enforces. (2) A lowered tree as a separate copy of the bound tree (every node re-created in `lctx.byn`). C# reuses `BoundExpr` nodes unchanged inside lowered output (`LoweredNodes.cs` reuses the simple bound nodes; `Lowerer.cs:919-923` returns the input node), so a copy would double the arena code and every printer case; instead one `i64` handle space holds both, with the lowered kinds numbered above `BoundKind` (Data / interfaces). (3) Replacing the C# backend or sharing a runtime-text file with it: the spec keeps C# as the oracle, and byte parity is the whole test. (4) Porting the mutant schemata and coverage probes "while we are there": they are spec 013's, add about 300 lines of lowering and runtime text to every task's parity surface, and cannot be checked by `--emit-c`.

### Decisions recorded (where the spec left room, the conservative option)

- **D1 Layout.** `selfhost/emitter/` is the entry module (`main.byn`, `lctx.byn`, `lower*.byn`); `selfhost/emitter/cprint/` is the printer module, imported as `emitter.cprint`. Nested import paths resolve through `Compilation.cs:187` (`Path.Combine([PackageRoot, .. import.Path])`) and the corpus has one such import today; T1's smoke test is the first check that a two-level path builds from `packageRoot: selfhost/`.
- **D2 Semantics is made `public`.** `selfhost/semantics/*.byn` (39 files) has no `public` declaration today; the parser got about 600 in 010 T1. T1 adds `public` to every type, enum, union, struct field, constructor and entry point the lowering and printer read, with no other change, so L022/L023 and the 1,068 parity rows prove R2's "behaviour unchanged". The CLI pieces bayan-emit must share with bayan-check (`t26Compile`, option parsing, `Report`, `Failed`: `selfhost/semantics/main.byn:77-290`) move into a new public `selfhost/semantics/cli.byn`, and `main.byn` calls them; the alternative, a second copy in the emitter's `main.byn`, is the duplication R2 forbids for the checker. Importing a module whose directory holds a `main` is untested; if the compiler rejects it, T1 records a Question and the fallback is the copy, marked as such.
- **D3 Mutants and coverage are out.** Not ported: `MutantSwitch`/`MutantExpr`/`MutantStatement` (`Lowerer.cs:1091-1144`), `BranchProbe`/`CountError` (`1148-1172`), the `_plain`/`_probing` sets (`Lowerer.cs:37-59`), `LMutantIs`/`LCount`, `MutantSelector`/`CoverageCounters`/`CoverageStart` (`CEmitter.cs:179-287`) and the mutant/coverage lines of `Run()` and `EmitMain`. The port behaves as C# does with `mutants == null && coverage == null`.
- **D4 Ownership checks are in.** `ownershipChecks` is the only mode-dependent emitter input (`CliApp.cs:409`: `mode == BuildMode.Debug`), three `run/` goldens say `// mode: debug`, and `OwnershipCheckerTests` reads the `byn_own` output, so the ownership checker (`CEmitter.cs:682-778`, `1254-1340`) is ported in full.
- **D5 Closures in the C# become explicit output switching.** `Capture(Action)` (`Lowerer.cs:116`), `CaptureExpr` (`131`), `CaptureGuard` (`296`), `AddEach(Action)` (`1305`) and `Helper(Func<string>)` (`CEmitter.cs:591`) take delegates. The port replaces each with a push/pop of the current output list on the lowering state (`captureBegin`/`captureEnd` over `_out`) and, for `Helper`, a reserve-then-fill of `_helpers`/`_helperOrder` in the same order C# registers them; T7 reads `Helper` first and matches that order exactly, since helper order is visible in the output. Stored closures are not used (010 rule).
- **D6 Handles over records with reference identity.** `Dictionary<JumpTarget, …>` (`_targets`), `Dictionary<BoundExpr, …>` (`_shown`, `_ran`) and the `HashSet<BoundStmt>` sets are keyed by object reference in C#. Bound nodes are already `i64` handles in 010 (`bound.byn:3`), so these become `Map<BNode, …>` keyed by the handle; `JumpTarget` is 010's `ctx.byn:160` handle.
- **D7 Landing scripts are parameterised on the base branch before the wave, by the dispatcher.** `land.sh` names 010 paths at lines 20, 33, 47 and 69; `guard.sh:32` whitelists `selfhost/semantics/ctx.byn` and `guard.sh:79` derives the task from `SelfhostSemanticsT<n>Tests`; `guard-checks.py:28,67-76,104` and `wave-brief.py:6-12,56-72,108` do the same. A task branch may not change `scripts/selfhost/` (`land.sh:35`), so the dispatcher lands one base commit that takes the spec number, the shared-state files (`selfhost/emitter/lctx.byn`, `selfhost/emitter/cprint/cctx.byn`) and the test-class prefix from arguments or a small table. Rejected: a copied `land-011.sh`, which drifts from the 010 one. Task names stay `T<n>`, in `docs/specs/011-self-hosted-emitter/011-tasks.md`; reviews in `…/reviews/T<n>.md`; test classes `SelfhostEmitterT<n>Tests`.
- **D8 K2 inventory lives at `docs/specs/011-self-hosted-emitter/011-c-inventory.md`** and is checked by `tests/Bayan.Tests/CInventoryTests.cs` (R4). 012 moves it if the toolchain work wants it elsewhere.
- **D9 R3's test is a literal scan.** `SelfhostEmitterPrinterBoundaryTests.cs` reads every `.byn` under `selfhost/emitter/` outside `cprint/` and fails on any string literal containing one of: `#include`, `#line`, `#define`, `typedef`, `int64_t`, `uint8_t`, `byn_`, `BYN_`, `bynt_`, `(void)`, `->`, `goto `, `static `, `;\n`, `{\n`. Panic messages and label names (`"L" + n`) contain none of these. The break-test in the acceptance criteria is a one-line change to a lowering file.
- **D10 Modes.** `bayan-emit --mode debug|safe|fast` with default `safe` (`RunTests.cs:76`); `bayan-emit` passes `mode == debug` as `ownershipChecks`. R6 runs the three modes even though `safe` and `fast` print the same C, because the spec says "every build mode" and the cost is one extra process per seed.
- **D11 Law tier from day one.** Per handoff Question 744 (user, 2026-11-02: enact the tier at 011 start, before L025), the L024 and L025 proof tests read `BAYAN_LAW_TIER` (unset = quick, a fixed seeded subset sized to about 3 minutes; `full` = the whole corpus), and the law files carry `timeout: 300`. N is measured at T14, not guessed.
- **D12 Questions.** Numbering continues after the handoff's last Question (Q744 at drafting); the dispatcher fixes each task's five-number range in `011-tasks.md` the way `010-tasks.md:3` does.

## Tech / dependencies

- Bayan as shipped through 010 (bootstrap compiler, no new language features; a strictly needed one gets a Question first).
- `selfhost/lexer/` (7 files), `selfhost/parser/` (17 files), `selfhost/semantics/` (39 files; `ctx.byn` 690, `bound.byn` 1,718, `compile.byn` 1,355, `main.byn` 437 lines), imported with `packageRoot: selfhost/` as `SelfhostSemanticsHarness.Build` does.
- C# reference: `src/Bayan.Lowering/` (`Lowerer.cs` 1,926, `LoweredNodes.cs` 127, `PowerAssert.cs` 78), `src/Bayan.Backend.C/CEmitter.cs` 2,449 (`Targets.cs` 48 and `Toolchain.cs` 441 are 012's), `src/runtime/bayan-rt.h` 506 and `bayan-rt.c` 2,551 (read by R4 only; unchanged).
- Corpus: `tests/cases/run/` 269 `.byn` inputs (4 `*.test.byn`; `// mode:` lines: 3 `debug`, 2 `fast`, the rest `safe`); `CliTests.cs:279` (`build --emit-c` on the `hello` example) and `CacheTests.cs:144-146` (`build <app> --emit-c` on a generated project); the 010 generator (`SelfhostSemanticsGenerator.cs`, 214 lines).
- Test plumbing: `SelfhostSemanticsHarness.cs` (368 lines: `Build`, `BuildFast`, `RunSections`, `AssertSection`, `BAYAN_CACHE=tmp/cache`), `RunTests.cs:70-83` (mode parsing, `CliApp.EmitC(c, mode)`), `SelfhostSemanticsApiTests.cs` (the `010-api.md` index check), law tests `SelfhostSemantics{Equivalence,NoPanic}LawTests.cs` with `[Trait("Law", "L02x")]`.
- Scripts: `scripts/selfhost/{guard.sh,land.sh,guard-checks.py,coverage-gate.py,wave-brief.py,review-brief.py,mutate-batch.sh,quality-baseline.py}` after D7; `tests/quality-baseline.json` (currently `{ "version": 1, "files": {} }`, written only by `land.sh` and the batch run).
- Laws: `docs/laws/022-binder-equivalence.md` and `023-no-checker-panic.md` as the format for L024/L025; `~/.claude/tools/laws-check.js`.

## Changes by area

Serial order: T1, T2. Wave (T3–T12 and T15, 4–6 writers, disjoint files, siblings reached through `struct Tn { static … }` stand-ins with the signatures in `011-api.md`, tests needing an unlanded sibling `[Fact(Skip = "T13 enables …")]`). Shared state and helpers follow 010's rule: a helper more than one task calls is pre-ported in its owner's file by T1 or T2 (the `lower.byn` helpers of `Lowerer.cs:95-351`, the `lctx.byn`/`cctx.byn` state, and the printer's `CType`/`Key`/`Named`/`Helper`/`Line`/`Loc` once T7/T9 land), listed in `011-api.md` with its owner, and a sibling calls it and does not rewrite it (`010-tasks.md:110`, Q674); only a not-yet-landed sibling's entry point gets a `struct Tn { static … }` stand-in, replaced at T13. Then T13 (integration, the only task that may edit any emitter file), T14, T16.

| # | Task | C# source (lines) | Bayan target | Depends on | Est. lines |
|---|---|---|---|---|---|
| 1 | T1 Skeleton and public semantics | `LoweredNodes.cs` 1-127; `Lowerer.cs` 37-76 (state); `CEmitter.cs` 15-60, 1150-1171 (state) | `selfhost/semantics/*.byn` (`public`), `selfhost/semantics/cli.byn` (moved from `main.byn:77-290`), `selfhost/emitter/{lctx,main}.byn`, `selfhost/emitter/cprint/cctx.byn`, header-only `lower*.byn` and `cprint/*.byn`, `tests/Bayan.Tests/SelfhostEmitterHarness.cs`, `SelfhostEmitterTests.cs` (smoke), `SelfhostEmitterApiTests.cs`, `SelfhostEmitterPrinterBoundaryTests.cs` (D9), `011-api.md` | none | ~650 new, ~700 mechanical |
| 2 | T2 Lowering entry, helpers, cleanup | `Lowerer.cs` 18-94 (`Lower`), 95-292 (Helpers: `Emit`, `Note`, `NewTemp`, `NewLabel`, `Capture`, `CaptureExpr`, `IsTrivial`, `IsRepeatable`, `IsLvalue`, `HasEffects`, `Spill`, `Become`, `EmitAssign`, `EmitEffect`, `Operands`, `Any`, `Moves`), 293-351 (Cleanup: `CaptureGuard`, `EmitCleanups`, `EmitCleanup`, `LowerOutside`) | `selfhost/emitter/lower.byn`, `SelfhostEmitterT2Tests.cs` | T1 | ~510 |
| 3 | T3 Statements | `Lowerer.cs` 352-898 (`Statement`, `Provide`, `Assign`, `Return`, `If`, `EnterLoop`, `ExitUnless`, `While`, `DoWhile`, `For` 598, `UserFor` 735, `ForItem` 797, `Switch` 808, `Lvalue` 862) | `selfhost/emitter/lowerstmt.byn`, `SelfhostEmitterT3Tests.cs` | T2 | ~790 |
| 4 | T4 Expressions, failure, patterns | `Lowerer.cs` 899-1088 (`Expr`, `LowerExpr`), 1173-1375 (`ShortCircuit`, `Conditional`, `Coalesce`, `Collection`, `Expanded`, `AddEach`, `Slice`, `Call`), 1729-1926 (`FailingCall`, `Failed`, `Propagate`, `Try`, `Catch`, `Match` 1799, `Claim` 1889, `Step`, `Equal`); 1089-1172 skipped (D3) | `selfhost/emitter/lowerexpr.byn`, `selfhost/emitter/lowerpat.byn`, `SelfhostEmitterT4Tests.cs` | T2 | ~860 |
| 5 | T5 Assert, writes, switch expressions, try-from | `Lowerer.cs` 1376-1728 (`Printable`, `Assert` 1384, `AssertFails` 1465, `Shown`, `IsLiteral`, `CallParts`, `BuiltinCall` 1561, `Stable`, `Write`, `WriteParts`, `SwitchExpr` 1652, `TryFrom` 1692); `PowerAssert.cs` 1-78 | `selfhost/emitter/lowerassert.byn`, `selfhost/emitter/powerassert.byn`, `SelfhostEmitterT5Tests.cs` | T2 | ~625 |
| 6 | T6 Printer program: run order, symbol table, main, names, static data, literals | `CEmitter.cs` 61-178 (`Emit`, `Run`, `SymbolTable`, `SymbolLiteral`, `SymbolText`, `SymbolIntern`, `ResolveResets`), 288-396 (`EmitMain`, `FunctionName`, `FileName`, `FilePath`), 1047-1147 (`Static`, `StaticArray`, `StringLiteral`, `Escape`, `Constant`, `FloatLiteral`); 179-287 skipped (D3) | `selfhost/emitter/cprint/program.byn`, `selfhost/emitter/cprint/data.byn`, `SelfhostEmitterT6Tests.cs` | T1 | ~545 |
| 7 | T7 Types, helpers, dyn ref, ownership helpers | `CEmitter.cs` 397-587 (`CType`, `ErrorCType`, `Key`, `Named`, `EmitTypes` with the payload layout 495-568), 588-681 (`Helper`, `SlotSignature`, `FunctionSignature`, `FunctionThunk`, `Vtable`), 682-778 (`StorageParts`, `OwnsStorage`, `OwnStatements`, `OwnClaim`, `OwnSet`, `OwnMove`, `OwnReturn`) | `selfhost/emitter/cprint/types.byn`, `selfhost/emitter/cprint/own.byn`, `SelfhostEmitterT7Tests.cs` | T1 | ~555 |
| 8 | T8 Equality, keys, map methods, format | `CEmitter.cs` 779-1046 (`Equal`, `KeyHash` 861, `KeyEq` 885, `MapMethod` 894, `Format` 946) | `selfhost/emitter/cprint/helpers.byn`, `SelfhostEmitterT8Tests.cs` | T1 | ~390 |
| 9 | T9 Function emitter: frame, lines, ownership checks, statements | `CEmitter.cs` 1173-1616 (ctor, `Signature`, `Emit`, `Line`, `LineOf`, `Loc`, `MapLine`, `CheckedStore` 1294, `ClaimAfterCall`, `ClaimMapEntry`, `FindUnsequenced` 1324, `Block`, `Statement` 1351, `AfterCalls`, `Return` 1484, `Write` 1503, `FormatWithSpec` 1542, `Debug` 1587) | `selfhost/emitter/cprint/func.byn`, `SelfhostEmitterT9Tests.cs` | T1 | ~680 |
| 10 | T10 Function emitter: expressions and calls | `CEmitter.cs` 1617-1939 (`Expr`, `Call` 1726) | `selfhost/emitter/cprint/expr.byn`, `SelfhostEmitterT10Tests.cs` | T1 | ~470 |
| 11 | T11 Function emitter: builtins, operators, conversions, index, slice | `CEmitter.cs` 1940-2449 (`Builtin` 1940, `Unary` 2290, `Binary` 2311, `Conversion` 2365, `Index` 2409, `Slice` 2426) | `selfhost/emitter/cprint/builtin.byn`, `SelfhostEmitterT11Tests.cs` | T1 | ~740 |
| 12 | T12 CLI and parity harness | `CliApp.cs:409` (`EmitC`), `RunTests.cs:70-83` and `ExecuteTests` (`:79`), `CliTests.cs:276-283`, `CacheTests.cs:135-153` | `selfhost/emitter/main.byn` (`--mode`, `--tests`, `--package <dir>`, `--many <dir>`, `--time <path>`, absolute paths, exit codes as bayan-check), `SelfhostEmitterHarness.cs` (build once in `fast`, run as a process per input, first differing line), `SelfhostEmitterParityTests.cs` (every `run/` input in its mode, the `CliTests`/`CacheTests` inputs; skipped until T13) | T1 | ~300 Bayan, ~350 C# |
| 13 | T13 Integration | all of the above | every `selfhost/emitter/**` file (stand-ins replaced), un-skip T12's parity, fix blocking compiler bugs with a test | T2–T12 | small, long |
| 14 | T14 Laws, generator seeds, speed | `SelfhostSemantics{Equivalence,NoPanic}LawTests.cs`, `SelfhostSemanticsGenerator.cs`, 010's `--time` test | `docs/laws/024-emitter-equivalence.md`, `025-no-emitter-panic.md` (drafts below), `SelfhostEmitterEquivalenceLawTests.cs` (L024: seeds × 3 modes, R6), `SelfhostEmitterNoPanicLawTests.cs` (L025), `SelfhostEmitterSpeedTests.cs` (R7: 1 MB generated program, median of 5, C# side `Stopwatch` around `Lowerer.Lower` + `CEmitter.Emit`), `BAYAN_LAW_TIER` (D11) | T13 | ~500 C# |
| 15 | T15 K2 inventory | `bayan-rt.h:17-25` (`__GNUC__`/`__clang__`, `__attribute__((noreturn))`, `__attribute__((cold))`, `__builtin_expect`), `461-495` (`__builtin_{add,sub,mul}_overflow`), `CEmitter.cs:82` (`__attribute__((unused))`); `alloca` has no use in the runtime or `CEmitter.cs` (`grep -nw alloca` is empty), so it is a regex token with no row; platform guards `_WIN32` (15 in `bayan-rt.c`), `__wasi__`, `__APPLE__`, `__linux__` | `docs/specs/011-self-hosted-emitter/011-c-inventory.md` (one row per item: token, use sites, C11 replacement), `tests/Bayan.Tests/CInventoryTests.cs` (regex `__builtin_\w+|__attribute__\(\(\w+\)\)|\balloca\b|__typeof__|__int128|__extension__|#pragma \w+|__asm__|__has_builtin` over the runtime, `CEmitter.cs` and `cprint/*.byn`; every hit must be a row; every row must have a hit) | none | ~120 doc, ~80 C# |
| 16 | T16 Docs and batch mutation | `docs/bayan-handoff.md:441` (row 011), `:375` (readiness), Questions; `mutate-batch.sh` | handoff, `work/todo.md`, `tests/quality-baseline.json` through the batch run (R8) | T14 | docs |

Lowering total about 2,800 Bayan lines, printer about 3,400, state and main about 900: about 7,100 against 4,580 C#, in line with 010's ratio.

Sizing notes. T4 is the largest lowering task because `LowerExpr` (190 lines) and the failure/pattern section (198) share the `Operands`/`IsNever` idiom; if a writer reports it over the cap, `lowerpat.byn` splits off as T4b. T11's `Builtin` is one 350-line switch in C#; it stays one task because splitting a switch across files costs a dispatch table the C# does not have. T9 carries the line-directive bookkeeping (`_linePath`, `_lastLine`, `_lastLineEnd`, `MapLine`, `ResolveResets` with T6) that `LineDirectiveTests` pins; its tests use those goldens.

## Data / interfaces

**Lowered tree (`selfhost/emitter/lctx.byn`).** One handle space with the bound tree. `type LNode = i64`; a lowered node is `(LKind << 32) | index` into a per-kind arena on `LCtx`, `-1` is null, and `LKind`'s ordinals start at 100, above `BoundKind`'s 77 members (`bound.byn:19-97`), so a `BStmt`/`BExpr` handle from 010 and a lowered handle tell apart by kind without a tag. `LKind` in `LoweredNodes.cs` order, minus D3: `label, gotoStmt, loop, returnStmt, returnError, failingCall, write, panic, setContext, freeArena, pushPanicHandler, popPanicHandler, claim, optionalHas, optionalValue, unionTag, unionField, errorCode, errorField, assignExpr, sequence, indexUnchecked, nextChar, never, functionValue, voidValue`. One struct per record, fields in C# order (`LWrite(sink, target, parts, guards)`, `LFailingCall(error, call, result)`, `LFunctionValue(function, env, functionType)`, …), `Node Syntax` as `TextSpan span`, symbols as 010 handles, `T[]` as `List<T>`; `WriteSink` is an enum `print, panic, strBuf, log`. Where C# builds new bound nodes during lowering (`b with { Left = … }`, `new BoundTuple`, `new BoundAssign`, `new BoundCall(… StrBufGrow …)`) the port appends to the semantics `Ctx`'s bound arenas through `bound.byn`'s constructors, made public by T1; `Constant` stays in `Ctx.constants` keyed by handle (`bound.byn:7-8`), so the printer's `e.Constant` check (`CEmitter.cs:1619`) is one map lookup. `LoweredFunction` is `(FuncId symbol, List<LocalId> locals, i64 contextSlots, i64 panicSlots, BStmt body)`; `LoweredProgram` is `(List<LoweredFunction> functions, FuncId main, List<TestId> tests)` over a `Ctx` passed alongside, with no `mutants`/`coverage` fields.

**Lowering state (`LCtx`, the `// ── Tn state` sections of `lctx.byn`).** `FunctionLowerer`'s fields (`Lowerer.cs:37-59`): `_out` and the capture stack as `List<List<BStmt>>`; `_locals`, `_seen` as `List<LocalId>`/`Map<LocalId, bool>`; `_scopes` as a `List<Scope>` whose `Cleanup` union mirrors `DeferCleanup`, `ErrdeferCleanup`, `RestoreContext`, `FreeArena`, `PopPanicHandler` (`Lowerer.cs:66-76`); `_targets` as `Map<JumpTarget, (string, string?, i64)>`; `_labels`, `_contextSlots`, `_panicSlots` counters; `_shown`/`_ran` as `Map<BExpr, …>` (D6). All on one struct, no globals, each task's fields in its own section as 010's `ctx.byn` rule requires (guard content check).

**Consuming the bound program.** `main.byn` parses the flags as `selfhost/semantics/main.byn:77` does (through `cli.byn` after D2), calls `create(from:, at:, by:)` with tests included when `--tests` is given, and on errors prints and exits as bayan-check (R1). With no errors it calls `lower(ctx) -> LoweredProgram`, which walks `ctx.mods[m].allFunctions` (`ctx.byn:87`) in module order, skipping functions without a body, tests and generic templates as `Lowerer.Lower` (`Lowerer.cs:18-77`) does, then the module tests, then `entryModule` (`ctx.byn:473`) for `main`; error variants (`ctx.byn:295`), exit codes (`:448`), test bodies (`:454`) and lambdas (`:623`) are read from the same fields the C# emitter reads on `Compilation`. The printer gets `emit(ctx, program, ownershipChecks: bool) -> string` and `main.byn` prints it; nothing else in `selfhost/emitter/` returns C.

**Printer module boundary (K1).** `selfhost/emitter/cprint/` is the only module with C in string literals (D9). Its public surface is `emit` plus the `CEmitter` facts the lowering never needs; `cctx.byn` holds the `CEmitter` state (`_types/_typeOrder`, `_helpers/_helperOrder`, `_statics/_staticOrder`, `_files`, `_hashes`, `_symbolTexts/_symbolIds/_symbols`, `SymbolMark`, `_testIndex`, `CKeywords`: `CEmitter.cs:15-60`) and the `FunctionEmitter` state (`_names`, `_sb`, `_file`, `_linePath`, `_lastLine`, `_lastLineEnd`, `_indent`, `_reads/_moves/_unsequenced/_moved`, `_afterCalls`: `1150-1171`). Output is accumulated in a `StrBuf` per C# `StringBuilder`; `Run()`'s section order (`CEmitter.cs:63-120`: header, `#ifndef BYN_DEBUG` for ownership checks, includes, file-name statics, enums, types, statics, the `byn_errors`/`byn_w_error`/`byn_exit_codes` tables, protos, helper protos, vtables, helper bodies, bodies, main, then the symbol-mark substitution and `ResolveResets`) is one function in `program.byn` so a reader can diff it against `Run()` line by line.

**Entry-point index.** `011-api.md` uses `010-api.md`'s line form (`- T<n> <file>.byn: <signature>  ← <C# file:line> <Name>`), checked verbatim by `SelfhostEmitterApiTests` as `SelfhostSemanticsApiTests.EveryIndexedEntryPointExists` does; T1 seeds it with the cross-file signatures of this plan, and each task edits only its own lines.

**Test harness.** `SelfhostEmitterHarness.Build` compiles `selfhost/emitter/**/*.byn` with `Compilation.Create(sources, packageRoot: selfhost/)` under `BAYAN_CACHE=tmp/cache` in `fast` mode once per run (a `Lazy`, as 010), then `Run(input, mode, tests)` starts the binary and returns stdout/stderr/exit; `AssertSame(name, expected, actual)` reports the first differing line and its number. The C# side of a comparison is `CliApp.EmitC(Compilation.Create([source], dir), mode)` as `RunTests.cs:80-82`, and for `*.test.byn` whatever `ExecuteTests` (`RunTests.cs:79`) builds. Unit tests of a wave task run the task's functions through a generated `main` (010's `RunSections` form) with the C# as oracle.

## Risks & mitigations

- **Importing `semantics` with its `main.byn` in the directory (D2).** Unknown until T1 builds. Mitigation: T1's smoke test is the first thing landed; fallback is a Question and a marked copy of the CLI helpers.
- **Helper and type registration order.** `_typeOrder`/`_helperOrder`/`_staticOrder` decide the output's order and are filled by side effects of `CType`/`Helper`/`Static` during emission; one reordered lookup changes bytes. Mitigation: T6/T7 port the registration points in the same statement order, and the T6 tests compare the type-order list itself against C# on the `run/` corpus before any body is emitted.
- **Floating-point and string literal text.** `FloatLiteral` (`CEmitter.cs:1137`) and `Escape` (`1100`) depend on C# formatting (`R`-style round-trip, invariant culture). Mitigation: T6 tests enumerate every float and string constant in the corpus through both; a Bayan formatting gap becomes a Question, not a workaround that changes bytes.
- **Rune and column arithmetic in power-assert.** `PowerAssert.Column` counts runes; `Shown` spills values in evaluation order. Mitigation: `tests/cases/run/` assert goldens cover it; T5 tests take the diagram text from C#.
- **Line directives.** `#line` bookkeeping (`MapLine`, `ResolveResets`) is byte-visible and depends on where C# chooses not to map (`Statement` restores `_lastLine` when a statement emits nothing: `CEmitter.cs:1465-1470`). Mitigation: `LineDirectiveTests` goldens in T9's tests; parity on the whole corpus in T13.
- **Task cap.** `Builtin` (T11) and `Statement`+ownership (T9) are the two long switches. Mitigation: sizes above are under 1,000; the split rule for T4 is stated.
- **Scripts hard-coded to 010 (D7).** Mitigation: the dispatcher's base commit lands before T1's branch is cut, and T1's guard run is its test.
- **Round 4 (Q1).** Closure lowering (`Lowerer.cs:1064-1074`, `CEmitter.cs:1717-1720`, the `env` tuple) will be redone. Mitigation: those lines sit in T4 and T10 only, with a `// Q1: round 4 redoes this` comment, so the second port is about 100 lines.
- **Speed (R7).** The printer appends to one growable buffer; 010 measured 1.42 on the checker. Risk is `Map` lookups per node for `Constant` and `_names`. Mitigation: T14 measures; a miss over 10 is a Question with the profile, not a rewrite.
- **Review cost.** 010's guard took 20–40 minutes per task. Mitigation: parity tests of a wave task run only that task's class; the whole-corpus parity runs at T13 and in the law tests.

## Coverage check

| Requirement | Where |
|---|---|
| R1 Program | Approach; D1, D2, D10; T12 (`main.byn`, exit codes as bayan-check); Data / interfaces (consuming the bound program) |
| R2 Reuse | D2 (`public` only, `cli.byn` move); T1; Risks (import) ; L001/L002/L017/L018/L022/L023 unchanged because no checker line changes |
| R3 Lowered tree, one printer | D1, D9; T1 (`lctx.byn`, boundary test); Data / interfaces (lowered tree, printer boundary) |
| R4 C inventory | D8; T15 (doc, `CInventoryTests`, the token list); Tech / dependencies (runtime sizes) |
| R5 Golden coverage | T12 (every `run/` input in its mode, `*.test.byn` with `--tests`, `CliTests`/`CacheTests` inputs, first differing line); T13 un-skips |
| R6 Generated inputs | T14 (seeds × 3 modes, D10); L024 draft |
| R7 Speed bound | T14 (`SelfhostEmitterSpeedTests`, 1 MB, median of 5); Risks (speed) |
| R8 Quality bar | D7 (guard/land parameterised); T16 (batch mutation into `tests/quality-baseline.json`); each task's `SelfhostEmitterT<n>Tests` with the C# oracle |
| R9 Port friction | D12 (Question ranges); every task's "done when" carries its range; Risks name the expected gaps (formatting, import) |
| R10 Docs | T16 (handoff row 441, readiness section at 375, Questions) |

## Draft law files (to be created by T14 under `docs/laws/`, `status: proposed` until the user enacts)

### `docs/laws/024-emitter-equivalence.md`

```
---
id: L024
title: Emitter equivalence
status: proposed
kind: property
scope: selfhost/emitter/, src/Bayan.Lowering/, src/Bayan.Backend.C/CEmitter.cs
proof: tests/Bayan.Tests/SelfhostEmitterEquivalenceLawTests.cs
check: dotnet test -v q --filter "Law=L024"
timeout: 300
enacted:
---

## Statement

For every program `bayan check` accepts and every build mode (`debug`, `safe`, `fast`), the C that
`bayan-emit` prints is byte for byte the C that `bayan build --emit-c` prints for the same input and
mode. The corpus is every `tests/cases/run/` input in the mode its `// mode:` line names, the inputs
`CliTests` and `CacheTests` pass to `--emit-c`, and the 010 generator's well-typed programs on the
fixed seeds, each in all three modes.

## Counterexample looks like

An input, a mode and the first line where the two outputs differ:

```
run/closures.byn (safe): line 212
  C#:    bynt_tup2_i64_str t3 = ((bynt_tup2_i64_str){a, b});
  Bayan: bynt_tup2_i64_str t3 = ((bynt_tup2_i64_str){ a, b });
```

Or a generated program (seed and mode) on which one backend prints and the other reports an error.

## Proof notes

The test builds `bayan-emit` once in `fast` mode (`SelfhostEmitterHarness.Build`), runs it as a
process on each corpus input, and compares with `CliApp.EmitC(Compilation.Create([source], dir), mode)`
in memory. `BAYAN_LAW_TIER` unset runs the fixed quick subset (every `run/` input plus the first N
seeds, N measured at enactment to fit about 3 minutes); `BAYAN_LAW_TIER=full` runs every seed. A
difference fails naming the input, the mode and the first differing line. Mutant and coverage output
(`--mutate`, `--coverage`) is outside the law: only the C# backend produces it (spec 011, non-goals).
```

### `docs/laws/025-no-emitter-panic.md`

```
---
id: L025
title: No emitter panic
status: proposed
kind: property
scope: selfhost/emitter/
proof: tests/Bayan.Tests/SelfhostEmitterNoPanicLawTests.cs
check: dotnet test -v q --filter "Law=L025"
timeout: 300
enacted:
---

## Statement

For every program in the corpus (every `tests/cases/run/` input, the inputs `CliTests` and
`CacheTests` pass to `--emit-c`, and the 010 generator's programs on the fixed seeds, well-typed or
corrupted) and every build mode, `bayan-emit` terminates and exits 0, 1 or 2: it never panics,
never exits through the runtime's trap paths, and never exceeds the harness time limit.

## Counterexample looks like

An input and mode on which `bayan-emit`'s stderr holds `panic:` or a runtime trap (overflow, bounds,
division by zero, invalid char), or whose exit code is outside {0, 1, 2}, or that does not finish:

```
seed 1187 (debug): exit 134
  panic: index 7 out of range for list of length 7 at selfhost/emitter/cprint/expr.byn:410
```

## Proof notes

The same corpus runner as L024, checking only termination, exit code and the absence of a panic or
trap line on stderr; an input the checker rejects must exit 1 with bayan-check's diagnostics, which
L024 does not cover. Corrupted seeds come from the 010 no-panic test's corruption step so the two
laws share a generator. Quick and full tiers as L024 (`BAYAN_LAW_TIER`). Where a counterexample is
the C# front end's (a parser or binder panic reproduced by the imported checker), it is recorded as
L023's and does not count against this law, as L018/L023 do today (handoff Q662, Q663).
```
