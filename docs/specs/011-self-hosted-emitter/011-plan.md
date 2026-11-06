# 011 — Plan
Spec: ./011-spec.md
Status: draft, rev 4 (2026-11-02)

Line counts below are `wc -l` on this worktree. C# ranges name methods in `src/Bayan.Lowering/` and `src/Bayan.Backend.C/`; locate by method when they drift. Sizes assume 010's measured 1.45 Bayan lines per C# line and the ~1,000-line task cap.

## Approach

`bayan-emit` is a second Bayan program next to `bayan-check`, laid out as 010 was: the entry module `selfhost/emitter/` holds `main.byn`, the lowering (a port of `Lowerer.cs` and `PowerAssert.cs`, split by its section comments) and the lowered-tree state file `lctx.byn` (a port of `LoweredNodes.cs` in 010's arena-and-handle style); the C printer is the nested module `selfhost/emitter/cprint/` (a port of `CEmitter.cs`, split at its class and section boundaries), the only place that builds C text (K1). The checker is imported, not copied: `import semantics` gives the `Ctx` that `create(from:, at:, by:)` (`selfhost/semantics/compile.byn:77`) returns, and the lowering reads its bound arenas and symbol tables through handles exactly as the C# lowerer reads `Compilation`. C# stays the reference. The C# `Lowerer.Lower(c, null, null)` and `CEmitter.Emit(program, ownershipChecks)` paths are ported method for method, in the same order and with the same names where Bayan allows; the mutant and coverage paths (`Lowerer.cs:1089-1172`, `CEmitter.cs:179-287` and the `LMutantIs`/`LCount` nodes) are not ported, since `CliApp.EmitC` (`CliApp.cs:409`) never passes them and the spec names them a non-goal. The test suite builds `bayan-emit` once in `fast` mode, runs it as a process on every `run/` input in that input's mode, and compares its stdout with `CliApp.EmitC` for the same compilation; the laws L024 and L025 are the same comparison over the generator's seeds.

Rejected: (1) one flat `selfhost/emitter/` module with the printer as "the files named `cprint*.byn`". A file is not a module boundary Bayan can see, and R3's test would rest on a naming convention; a nested module (`import emitter.cprint`, the form `tests/cases` already uses as `import app.users`) is a boundary the compiler enforces. (2) A lowered tree as a separate copy of the bound tree (every node re-created in `lctx.byn`). C# reuses `BoundExpr` nodes unchanged inside lowered output (`LoweredNodes.cs` reuses the simple bound nodes; `Lowerer.cs:919-923` returns the input node), so a copy would double the arena code and every printer case; instead one `i64` handle space holds both, with the lowered kinds numbered above `BoundKind` (Data / interfaces). (3) Replacing the C# backend or sharing a runtime-text file with it: the spec keeps C# as the oracle, and byte parity is the whole test. (4) Porting the mutant schemata and coverage probes "while we are there": they are spec 013's, add about 300 lines of lowering and runtime text to every task's parity surface, and cannot be checked by `--emit-c`.

### Decisions recorded (where the spec left room, the conservative option)

- **D1 Layout.** `selfhost/emitter/` is the entry module (`main.byn`, `lctx.byn`, `lower*.byn`); `selfhost/emitter/cprint/` is the printer module, imported as `emitter.cprint`. Nested import paths resolve through `Compilation.cs:187` (`Path.Combine([PackageRoot, .. import.Path])`) and the corpus has one such import today; T1's smoke test is the first check that a two-level path builds from `packageRoot: selfhost/`. *Revised 2026-11-03 (user, T1 finding: `emitter` ↔ `emitter.cprint` is an import cycle, BYN2010):* the lowered-tree types (the `LoweredNodes.cs` port) live in a third nested module `selfhost/emitter/ltree/`, imported by both `emitter` and `emitter.cprint`; the lowering state (`LCtx`, scopes, cleanups) stays in `emitter`.
- **D2 Semantics is made `public`.** `selfhost/semantics/*.byn` (39 files) has no `public` declaration today; the parser got about 600 in 010 T1. T1 adds `public` to every type, enum, union, struct field, constructor and entry point the lowering and printer read, with no other change, so L022/L023 and the 1,068 parity rows prove R2's "behaviour unchanged". The CLI pieces bayan-emit must share with bayan-check (`t26Compile`, option parsing, `Report`, `Failed`: `selfhost/semantics/main.byn:77-290`) move into a new public `selfhost/semantics/cli.byn`, and `main.byn` calls them; the alternative, a second copy in the emitter's `main.byn`, is the duplication R2 forbids for the checker. Importing a module whose directory holds a `main` is untested; if the compiler rejects it, T1 records a Question and the fallback is the copy, marked as such. *Revised 2026-11-03 (user, T1 finding: all-public gives 697 errors, 660 × BYN6002 undeclared effects):* only the types, fields and functions the emitter reads are made `public`, each listed in `011-api.md`; a public function gets an effect clause equal to the one the checker infers today (private callers keep inferring). A wave task that needs another semantics name adds `public` (and its effect clause) for that name only.
- **D3 Mutants and coverage are out.** Not ported: `MutantSwitch`/`MutantExpr`/`MutantStatement` (`Lowerer.cs:1091-1144`), `BranchProbe`/`CountError` (`1148-1172`), the `_mutants`/`_plain`/`_coverage`/`_probing` fields (`Lowerer.cs:42-47`, one block between the ported `_fn`/`_builtins` at 40-41 and `_locals` at 48) and the `mutants`/`coverage` parameters of `Lowerer.Lower` (`:18`, passed on at `:28`, `:31`, `:33`) and of `FunctionLowerer`'s header (`:37-38`, whose `fn`/`builtins` parameters are ported), `LMutantIs`/`LCount`, `MutantSelector`/`CoverageCounters`/`CoverageStart` (`CEmitter.cs:179-287`), and these lines inside ported methods, which the owning task leaves out: `Lowerer.cs:356-362` (T3, `Statement`'s probe and mutant switch), `:399` (T3, `CountError` call), `:910-911` (T4, `LowerExpr`), `:1760` (T4, `Propagate`); `CEmitter.cs:106-107` (T6, `Run`), `:292-293` (T6, `EmitMain`), `:1481-1482` (T9, `Count`), `:1696-1699` (T10, the `LMutantIs`/`LCount` cases of `Expr`). The port behaves as C# does with `mutants == null && coverage == null`. Each owning task's brief in `011-tasks.md` lists its skipped lines (the task table carries them as "skip (D3)"), so a writer does not port them and the guard's coverage gate does not count them.
- **D4 Ownership checks are in.** `ownershipChecks` is the only mode-dependent emitter input (`CliApp.cs:409`: `mode == BuildMode.Debug`), three `run/` goldens say `// mode: debug`, and `OwnershipCheckerTests` reads the `byn_own` output, so the ownership checker (`CEmitter.cs:682-778`, `1254-1340`) is ported in full.
- **D5 Closures in the C# become explicit output switching.** `Capture(Action)` (`Lowerer.cs:116`), `CaptureExpr` (`131`), `CaptureGuard` (`296`), `AddEach(Action)` (`1305`) and `Helper(Func<string>)` (`CEmitter.cs:591`) take delegates. The port replaces each with a push/pop of the current output list on the lowering state (`captureBegin`/`captureEnd` over `_out`) and, for `Helper`, a reserve-then-fill of `_helpers`/`_helperOrder` in the same order C# registers them; T2 (which pre-ports `Helper` into `cprint/common.byn`) reads `Helper` first and matches that order exactly, since helper order is visible in the output. Stored closures are not used (010 rule).
- **D6 Handles over records with reference identity.** `Dictionary<JumpTarget, …>` (`_targets`), `Dictionary<BoundExpr, …>` (`_shown`, `_ran`) and the `HashSet<BoundStmt>` sets are keyed by object reference in C#. Bound nodes are already `i64` handles in 010 (`bound.byn:3`), so these become `Map<BNode, …>` keyed by the handle; `JumpTarget` is 010's `ctx.byn:160` handle.
- **D7 Landing scripts are parameterised on the base branch before the wave, by the dispatcher.** `land.sh` names 010 paths at lines 20, 33, 47 and 69; `guard.sh:32` whitelists `selfhost/semantics/ctx.byn` and `guard.sh:79` derives the task from `SelfhostSemanticsT<n>Tests`; `guard-checks.py:28,67-76,104` and `wave-brief.py:6-12,56-72,108` do the same. A task branch may not change `scripts/selfhost/` (`land.sh:35`), so the dispatcher lands one base commit that takes the spec number, the shared-state files (`selfhost/emitter/lctx.byn`, `selfhost/emitter/cprint/cctx.byn`) and the test-class prefix from arguments or a small table. Rejected: a copied `land-011.sh`, which drifts from the 010 one. Task names stay `T<n>`, in `docs/specs/011-self-hosted-emitter/011-tasks.md`; reviews in `…/reviews/T<n>.md`; test classes `SelfhostEmitterT<n>Tests`. The table keeps a 010 row, so a 010-module change still lands after the base commit (Q745's selfhost half is one, D13); the base commit's test is a `land.sh` dry run for each row (010 and 011) on a throwaway branch, refusing a file outside the row's area.
- **D8 K2 inventory lives at `docs/specs/011-self-hosted-emitter/011-c-inventory.md`** and is checked by `tests/Bayan.Tests/CInventoryTests.cs` (R4). 012 moves it if the toolchain work wants it elsewhere.
- **D9 R3's test is a literal scan.** `SelfhostEmitterPrinterBoundaryTests.cs` reads every `.byn` under `selfhost/emitter/` outside `cprint/` and fails on any string literal containing one of: `#include`, `#line`, `#define`, `typedef`, `int64_t`, `uint8_t`, `byn_`, `BYN_`, `bynt_`, `(void)`, `->`, `goto `, `static `, `;\n`, `{\n`. Panic messages and label names (`"L" + n`) contain none of these. The break-test in the acceptance criteria is a one-line change to a lowering file.
- **D10 Modes.** `bayan-emit --mode debug|safe|fast` with default `safe` (`RunTests.cs:76`); `bayan-emit` passes `mode == debug` as `ownershipChecks`. R6 runs the three modes even though `safe` and `fast` print the same C, because the spec says "every build mode" and the cost is one extra process per seed.
- **D11 Law tier from day one.** Per handoff Question 744 (user, 2026-11-02: enact the tier at 011 start, before L025), the D7 base commit (1) adds `BAYAN_LAW_TIER` to the L018 and L023 proof tests (unset = quick, a fixed seeded subset; `full` = the whole corpus), measures N for each so the quick tier fits about 3 minutes, writes N into `docs/laws/018-*.md` and `023-*.md` and sets both to `status: enacted` (user-approved 2026-11-02); (2) adds `scripts/selfhost/pre-push-laws.sh` (the full tier of every enacted law) and a nightly scheduled workflow `.github/workflows/laws-nightly.yml` running `BAYAN_LAW_TIER=full`, whose steps are shell, as `ci.yml:25-32` already does: `git clone` of the exact commit at depth 1 and `scripts/ci/install-dotnet.sh`, no `actions/checkout` or `actions/setup-dotnet`. The only nightly job today is `.github/workflows/mutation.yml:9-10`. L024 and L025 are drafted by T14 as `proposed`, read `BAYAN_LAW_TIER` from the start, carry `timeout: 300`, have N measured at T14 and are enacted only after L018 and L023.
- **D12 Questions.** Numbering continues after the handoff's last Question (Q744 at drafting); the dispatcher fixes each task's five-number range in `011-tasks.md` the way `010-tasks.md:3` does.
- **D13 Q745 before T4.** Handoff Question 745 (user-approved 2026-11-02): a borrow that escapes through a `ref`/`out` argument or into a `ref this` receiver is closed in the C# checker first, then in `selfhost/semantics/region*.byn` through `land.sh` as a 010-module change (D7's 010 row), both before T4's branch is cut. It is the one behaviour change R2 allows; L022/L023 hold across it because both checkers change together and the parity rows compare them.

## Tech / dependencies

- Bayan as shipped through 010 (bootstrap compiler, no new language features; a strictly needed one gets a Question first), plus the spec 014 path slice (P0 below).
- Q742 has landed: the runtime sets binary-mode stdio (`byn_stdio_binary`, `src/runtime/bayan-rt.c:41`), covered by `LfOutputTests.cs`.
- `selfhost/lexer/` (7 files), `selfhost/parser/` (17 files), `selfhost/semantics/` (39 files; `ctx.byn` 690, `bound.byn` 1,718, `compile.byn` 1,355, `main.byn` 437 lines), imported with `packageRoot: selfhost/` as `SelfhostSemanticsHarness.Build` does.
- C# reference: `src/Bayan.Lowering/` (`Lowerer.cs` 1,926, `LoweredNodes.cs` 127, `PowerAssert.cs` 78), `src/Bayan.Backend.C/CEmitter.cs` 2,449 (`Targets.cs` 48 and `Toolchain.cs` 441 are 012's), `src/runtime/bayan-rt.h` 506 and `bayan-rt.c` 2,551 (read by R4 only; unchanged).
- Corpus: `tests/cases/run/` 269 `.byn` inputs (4 `*.test.byn`; `// mode:` lines: 3 `debug`, 2 `fast`, the rest `safe`); `CliTests.cs:279` (`build --emit-c` on the `hello` example) and `CacheTests.cs:144-146` (`build <app> --emit-c` on a generated project); the 010 generator (`SelfhostSemanticsGenerator.cs`, 214 lines).
- Test plumbing: `SelfhostSemanticsHarness.cs` (368 lines: `Build`, `BuildFast`, `RunSections`, `AssertSection`, `BAYAN_CACHE=tmp/cache`), `RunTests.cs:70-83` (mode parsing, `CliApp.EmitC(c, mode)`), `SelfhostSemanticsApiTests.cs` (the `010-api.md` index check), law tests `SelfhostSemantics{Equivalence,NoPanic}LawTests.cs` with `[Trait("Law", "L02x")]`.
- Scripts: `scripts/selfhost/{guard.sh,land.sh,guard-checks.py,coverage-gate.py,wave-brief.py,review-brief.py,mutate-batch.sh,quality-baseline.py}` after D7; `tests/quality-baseline.json` (currently `{ "version": 1, "files": {} }`, written only by `land.sh` and the batch run).
- Laws: `docs/laws/022-binder-equivalence.md` and `023-no-checker-panic.md` as the format for L024/L025; `~/.claude/tools/laws-check.js`.

## Changes by area

Serial order: P0, T1, T2. P0 is spec 014's work, not an 011 task: a minimal slice of the `std` path module, landed under 014's spec and tests before T1's branch is cut, holding only the path functions `FileName`, `FilePath`, `MapLine` and T12's path handling call (T1 copies the list into `011-api.md`). The C# calls, measured 2026-11-02: `FilePath` (`CEmitter.cs:388-395`) calls `Path.IsPathRooted`, `Path.GetRelativePath(_packageRoot, path)` and `Path.GetFileName`; `FileName` and `MapLine` call none (they use `FilePath`); `EmitC` (`CliApp.cs:409`) calls none; the package root comes from `Compilation.PackageRoot` (`Compilation.cs:55`), which 010's `create` already computes. So P0 holds `isRooted`, `relative` (returning a `..`-prefixed path for one outside the root, as .NET does, since `FilePath` tests for `..`) and `fileName`. T12's `main.byn` path handling goes through `cli.byn` (D2), which today uses 010's `/`-only copies of `GetFullPath`, `GetDirectoryName`, `Combine`, `GetRelativePath` and `GetFileName` (`selfhost/semantics/compile.byn:1269-1340`, `json.byn:34`); P0 adds `fullPath`, `directoryName` and `combine` only if T12 needs them outside `cli.byn`, and replacing 010's copies is 014's work. `tests/Bayan.Tests/RunTests.cs` stays C# and needs no P0 function. Each follows 014's rules: pure functions take no effect, `fullPath` needs the `process`/`fs` effect, and on Windows they handle drive letters, UNC paths and both separators, with `relative` comparing case-insensitively. The rest of 014 lands later and does not change these functions. Wave (T3–T12 and T15, 4–6 writers, disjoint files, siblings reached through `struct Tn { static … }` stand-ins with the signatures in `011-api.md`, tests needing an unlanded sibling `[Fact(Skip = "T13 enables …")]`). Shared state and helpers follow 010's rule: a helper more than one task calls is pre-ported in its owner's file by T1 or T2 (the `lower.byn` helpers of `Lowerer.cs:95-351`, the `lctx.byn`/`cctx.byn` state, and the printer's `CType`/`ErrorCType`/`Key`/`Named`/`Helper`/`Line`/`LineOf`/`Loc`/`MapLine` in `cprint/common.byn`, all ported by T2 before the wave starts, as the handoff's rule says: a C# private helper needed outside its owner task is pre-ported by a chore), listed in `011-api.md` with its owner, and a sibling calls it and does not rewrite it (`010-tasks.md:110`, Q674); only a not-yet-landed sibling's entry point gets a `struct Tn { static … }` stand-in, replaced at T13. Then T13 (integration, the only task that may edit any emitter file), T14, T16.

| # | Task | C# source (lines) | Bayan target | Depends on | Est. lines |
|---|---|---|---|---|---|
| 1 | T1 Skeleton and public semantics | `LoweredNodes.cs` 1-127; `Lowerer.cs` 37-41, 48-76 (state: class header, fields, `Scope`, `Cleanup` records; 42-47 skipped, D3); `CEmitter.cs` 15-60, 1148-1171 (state, with the `FunctionEmitter` header) | `selfhost/semantics/*.byn` (`public`), `selfhost/semantics/cli.byn` (moved from `main.byn:77-290`), `selfhost/emitter/{lctx,main}.byn`, `selfhost/emitter/cprint/cctx.byn`, header-only `lower*.byn` and `cprint/*.byn`, `tests/Bayan.Tests/SelfhostEmitterHarness.cs`, `SelfhostEmitterTests.cs` (smoke), `SelfhostEmitterApiTests.cs`, `SelfhostEmitterPrinterBoundaryTests.cs` (D9), `011-api.md` | none | ~650 new, ~700 mechanical |
| 2 | T2 Lowering entry, helpers, cleanup | `Lowerer.cs` 11-35 (`Lowerer.Lower`), 78-93 (`FunctionLowerer.Lower`), 95-292 (Helpers: `Emit`, `Note`, `NewTemp`, `NewLabel`, `Capture`, `CaptureExpr`, `IsTrivial`, `IsRepeatable`, `IsLvalue`, `HasEffects`, `Spill`, `Become`, `EmitAssign`, `EmitEffect`, `Operands`, `Any`, `Moves`), 293-351 (Cleanup: `CaptureGuard`, `EmitCleanups`, `EmitCleanup`, `LowerOutside`); printer helpers every printer task calls, pre-ported here: `CEmitter.cs` 397-476 (`CType`, `ErrorCType`, `Key`, `Named`), 588-599 (`Helper`), 1237-1253 (`Line`, `LineOf`, `Loc`, `MapLine`) | `selfhost/emitter/lower.byn`, `selfhost/emitter/cprint/common.byn`, `SelfhostEmitterT2Tests.cs` | T1 | ~655 |
| 3 | T3 Statements | `Lowerer.cs` 352-898 (`Statement`, `Provide`, `Assign`, `Return`, `If`, `EnterLoop`, `ExitUnless`, `While`, `DoWhile`, `For` 598, `UserFor` 735, `ForItem` 797, `Switch` 808, `Lvalue` 862); skip (D3): `:356-362` (`Statement`'s probe and mutant switch), `:399` (`CountError` call) | `selfhost/emitter/lowerstmt.byn`, `SelfhostEmitterT3Tests.cs` | T2 | ~790 |
| 4 | T4 Expressions, failure, patterns | `Lowerer.cs` 899-1088 (`Expr`, `LowerExpr`), 1173-1375 (`ShortCircuit`, `Conditional`, `Coalesce`, `Collection`, `Expanded`, `AddEach`, `Slice`, `Call`), 1729-1926 (`FailingCall`, `Failed`, `Propagate`, `Try`, `Catch`, `Match` 1799, `Claim` 1889, `Step`, `Equal`); 1089-1172 skipped (D3); skip (D3) inside ported methods: `:910-911` (`LowerExpr`), `:1760` (`Propagate`) | `selfhost/emitter/lowerexpr.byn`, `selfhost/emitter/lowerpat.byn`, `SelfhostEmitterT4Tests.cs` | T2 | ~860 |
| 5 | T5 Assert, writes, switch expressions, try-from | `Lowerer.cs` 1376-1728 (`Printable`, `Assert` 1384, `AssertFails` 1465, `Shown`, `IsLiteral`, `CallParts`, `BuiltinCall` 1561, `Stable`, `Write`, `WriteParts`, `SwitchExpr` 1652, `TryFrom` 1692); `PowerAssert.cs` 1-78 | `selfhost/emitter/lowerassert.byn`, `selfhost/emitter/powerassert.byn`, `SelfhostEmitterT5Tests.cs` | T2 | ~625 |
| 6 | T6 Printer program: run order, symbol table, main, names, static data, literals | `CEmitter.cs` 61-178 (`Emit`, `Run`, `SymbolTable`, `SymbolLiteral`, `SymbolText`, `SymbolIntern`, `ResolveResets`), 288-396 (`EmitMain`, `FunctionName`, `FileName`, `FilePath`), 1047-1147 (`Static`, `StaticArray`, `StringLiteral`, `Escape`, `Constant`, `FloatLiteral`); 179-287 skipped (D3); skip (D3): `:106-107` (`Run`), `:292-293` (`EmitMain`) | `selfhost/emitter/cprint/program.byn`, `selfhost/emitter/cprint/data.byn`, `SelfhostEmitterT6Tests.cs` | T2, P0 | ~545 |
| 7 | T7 Types, helpers, dyn ref, ownership helpers | `CEmitter.cs` 477-587 (`EmitTypes` with the payload layout 495-568), 600-681 (`SlotSignature`, `FunctionSignature`, `FunctionThunk`, `Vtable`), 682-778 (`StorageParts`, `OwnsStorage`, `OwnStatements`, `OwnClaim`, `OwnSet`, `OwnMove`, `OwnReturn`) | `selfhost/emitter/cprint/types.byn`, `selfhost/emitter/cprint/own.byn`, `SelfhostEmitterT7Tests.cs` | T2 | ~420 |
| 8 | T8 Equality, keys, map methods, format | `CEmitter.cs` 779-1046 (`Equal`, `KeyHash` 861, `KeyEq` 885, `MapMethod` 894, `Format` 946) | `selfhost/emitter/cprint/helpers.byn`, `SelfhostEmitterT8Tests.cs` | T2 | ~390 |
| 9 | T9 Function emitter: frame, lines, ownership checks, statements | `CEmitter.cs` 1173-1236 (ctor, `Signature`, `Emit`), 1254-1616 (`CheckedStore` 1294, `ClaimAfterCall`, `ClaimMapEntry`, `FindUnsequenced` 1324, `Block`, `Statement` 1351, `AfterCalls`, `Return` 1484, `Write` 1503, `FormatWithSpec` 1542, `Debug` 1587); skip (D3): `:1481-1482` (`Count`) | `selfhost/emitter/cprint/func.byn`, `SelfhostEmitterT9Tests.cs` | T2 | ~655 |
| 10 | T10 Function emitter: expressions and calls | `CEmitter.cs` 1617-1939 (`Expr`, `Call` 1726); skip (D3): `:1696-1699` (the `LMutantIs`/`LCount` cases) | `selfhost/emitter/cprint/expr.byn`, `SelfhostEmitterT10Tests.cs` | T2 | ~470 |
| 11 | T11 Function emitter: builtins, operators, conversions, index, slice | `CEmitter.cs` 1940-2449 (`Builtin` 1940, `Unary` 2290, `Binary` 2311, `Conversion` 2365, `Index` 2409, `Slice` 2426) | `selfhost/emitter/cprint/builtin.byn`, `SelfhostEmitterT11Tests.cs` | T2 | ~740 |
| 12 | T12 CLI and parity harness | `CliApp.cs:409` (`EmitC`), `RunTests.cs:70-83` and `ExecuteTests` (`:79`), `CliTests.cs:276-283`, `CacheTests.cs:135-153` | `selfhost/emitter/main.byn` (`--mode`, `--tests`, `--package <dir>`, `--many <dir>`, `--time <path>`, absolute paths, exit codes as bayan-check; paths through the P0 functions), `SelfhostEmitterHarness.cs` (build once in `fast`, run as a process per input, first differing line), `SelfhostEmitterParityTests.cs` (every `run/` input in its mode, the `CliTests`/`CacheTests` inputs, and the inputs with errors that 010's bayan-check parity uses, compared on stderr and exit code (R1 errors); skipped until T13) | T1, P0 | ~300 Bayan, ~350 C# |
| 13 | T13 Integration | all of the above | every `selfhost/emitter/**` file (stand-ins replaced), un-skip T12's parity, fix blocking compiler bugs with a test | T2–T12 | small, long |
| 14 | T14 Laws, generator seeds, speed | `SelfhostSemantics{Equivalence,NoPanic}LawTests.cs`, `SelfhostSemanticsGenerator.cs`, 010's `--time` test | `docs/laws/024-emitter-equivalence.md`, `025-no-emitter-panic.md` (drafts below), `SelfhostEmitterEquivalenceLawTests.cs` (L024: seeds × 3 modes, R6), `SelfhostEmitterNoPanicLawTests.cs` (L025), a break-test for R6 (a dropped cast or a reordered field in one backend, behind a test-only switch, makes at least one seed fail), `SelfhostEmitterSpeedTests.cs` (R7: 1 MB generated program, median of 5; Bayan side the lower and emit phases from `--time`, not the whole process; C# side `Stopwatch` around `Lowerer.Lower` + `CEmitter.Emit`), `BAYAN_LAW_TIER` (D11) | T13 | ~500 C# |
| 15 | T15 K2 inventory | `bayan-rt.h:17-25` (`__GNUC__`/`__clang__`, `__attribute__((noreturn))`, `__attribute__((cold))`, `__builtin_expect`), `461-495` (`__builtin_{add,sub,mul}_overflow`), `CEmitter.cs:82` (`__attribute__((unused))`); `alloca` has no use in the runtime or `CEmitter.cs` (`grep -nw alloca` is empty), so it is a regex token with no row; platform guards `_WIN32` (15 in `bayan-rt.c`), `__wasi__`, `__APPLE__`, `__linux__`, `__GNUC__`, `__clang__` (a separate section: predefined macros, not extensions, listed for 012 and not checked by the regex) | `docs/specs/011-self-hosted-emitter/011-c-inventory.md` (one row per item: token, use sites, C11 replacement), `tests/Bayan.Tests/CInventoryTests.cs` (regex `__builtin_\w+|__attribute__\s*\(\(([^()]|\([^()]*\))*\)\)|__declspec|\balloca\b|__typeof__|\btypeof\b|__auto_type|__thread\b|__int128|__extension__|__restrict\w*|#pragma \w+|_Pragma|__asm__|\basm\b|__has_builtin|__PRETTY_FUNCTION__|__COUNTER__|\(\{` over the runtime, `CEmitter.cs` and `cprint/*.byn`; an `__attribute__` list is split on commas so `((noreturn, cold))` (J89) yields two tokens; `\(\{` catches statement expressions; every hit must be a row; every row must have a hit; break-test: a new token in a `cprint` literal fails it) | T1 | ~120 doc, ~80 C# |
| 16 | T16 Docs and batch mutation | `docs/bayan-handoff.md:441` (row 011), `:375` (readiness), Questions; `mutate-batch.sh` | handoff, `work/todo.md`, `tests/quality-baseline.json` through the batch run (R8) | T14, T15 | docs |
| 21 | T21 Bound-node syntax (Q849) | `BoundNodes.cs:20-24` (`BoundNode(Node Syntax)`); every `BoundExpr` construction in `src/Bayan.Semantics/Binding/` | `selfhost/semantics/{ctx,bound}.byn`, the binder files that build bound expressions, `SelfhostEmitterT21Tests.cs` | T2 | ~600 |

Lowering total about 2,800 Bayan lines, printer about 3,400, state and main about 900: about 7,100 against 4,580 C# (about 4,380 after D3), a ratio of about 1.6, above 010's 1.45 because state, CLI and the `cli.byn` move are counted here.

Sizing notes. T4 is the largest lowering task because `LowerExpr` (190 lines) and the failure/pattern section (198) share the `Operands`/`IsNever` idiom; if a writer reports it over the cap, `lowerpat.byn` splits off as T4b. T11's `Builtin` is one 350-line switch in C#; it stays one task because splitting a switch across files costs a dispatch table the C# does not have. T2 pre-ports `Line`, `LineOf`, `Loc` and `MapLine` in `cprint/common.byn`; T9 owns the rest of the line-directive bookkeeping (the `_lastLine` restore in `Statement`, with T6's `ResolveResets`) that `LineDirectiveTests` pins, calls T2's functions without rewriting them, and its tests use those goldens.

## Data / interfaces

**Lowered tree (`selfhost/emitter/lctx.byn`).** One handle space with the bound tree. `type LNode = i64`; a lowered node is `(LKind << 32) | index` into a per-kind arena on `LCtx`, `-1` is null, and `LKind`'s ordinals start at 100, above `BoundKind`'s 77 members (`bound.byn:19-97`), so a `BStmt`/`BExpr` handle from 010 and a lowered handle tell apart by kind without a tag. `LKind` in `LoweredNodes.cs` order, minus D3: `label, gotoStmt, loop, returnStmt, returnError, failingCall, write, panic, setContext, freeArena, pushPanicHandler, popPanicHandler, claim, optionalHas, optionalValue, unionTag, unionField, errorCode, errorField, assignExpr, sequence, indexUnchecked, nextChar, never, functionValue, voidValue`. One struct per record, fields in C# order (`LWrite(sink, target, parts, guards)`, `LFailingCall(error, call, result)`, `LFunctionValue(function, env, functionType)`, …), `Node Syntax` as `TextSpan span`, symbols as 010 handles, `T[]` as `List<T>`; `WriteSink` is an enum `print, panic, strBuf, log`. Where C# builds new bound nodes during lowering (`b with { Left = … }`, `new BoundTuple`, `new BoundAssign`, `new BoundCall(… StrBufGrow …)`) the port appends to the semantics `Ctx`'s bound arenas through `bound.byn`'s constructors, made public by T1; `Constant` stays in `Ctx.constants` keyed by handle (`bound.byn:7-8`), so the printer's `e.Constant` check (`CEmitter.cs:1619`) is one map lookup. `LoweredFunction` is `(FuncId symbol, List<LocalId> locals, i64 contextSlots, i64 panicSlots, BStmt body)`; `LoweredProgram` is `(List<LoweredFunction> functions, FuncId main, List<TestId> tests)` over a `Ctx` passed alongside, with no `mutants`/`coverage` fields.

**Lowering state (`LCtx`, the `// ── Tn state` sections of `lctx.byn`).** `FunctionLowerer`'s fields (`Lowerer.cs:40-41`, `48-59`; 42-47 are D3's): `_out` and the capture stack as `List<List<BStmt>>`; `_locals`, `_seen` as `List<LocalId>`/`Map<LocalId, bool>`; `_scopes` as a `List<Scope>` whose `Cleanup` union mirrors `DeferCleanup`, `ErrdeferCleanup`, `RestoreContext`, `FreeArena`, `PopPanicHandler` (`Lowerer.cs:66-76`); `_targets` as `Map<JumpTarget, (string, string?, i64)>`; `_labels`, `_contextSlots`, `_panicSlots` counters; `_shown`/`_ran` as `Map<BExpr, …>` (D6). All on one struct, no globals, each task's fields in its own section as 010's `ctx.byn` rule requires (guard content check).

**Consuming the bound program.** `main.byn` parses the flags as `selfhost/semantics/main.byn:77` does (through `cli.byn` after D2), calls `create(from:, at:, by:)` with tests included when `--tests` is given, and on errors prints and exits as bayan-check (R1). With no errors it calls `lower(ctx) -> LoweredProgram`, which walks `ctx.mods[m].allFunctions` (`ctx.byn:87`) in module order, skipping functions without a body, tests and generic templates as `Lowerer.Lower` (`Lowerer.cs:18-34`) does, then the module tests, then `entryModule` (`ctx.byn:473`) for `main`; error variants (`ctx.byn:295`), exit codes (`:448`), test bodies (`:454`) and lambdas (`:623`) are read from the same fields the C# emitter reads on `Compilation`. The printer gets `emit(ctx, program, ownershipChecks: bool) -> string` and `main.byn` prints it; nothing else in `selfhost/emitter/` returns C.

**Printer module boundary (K1).** `selfhost/emitter/cprint/` is the only module with C in string literals (D9). Its public surface is `emit` plus the `CEmitter` facts the lowering never needs; `cctx.byn` holds the `CEmitter` state (`_types/_typeOrder`, `_helpers/_helperOrder`, `_statics/_staticOrder`, `_files`, `_hashes`, `_symbolTexts/_symbolIds/_symbols`, `SymbolMark`, `_testIndex`, `CKeywords`: `CEmitter.cs:15-60`) and the `FunctionEmitter` state (`_names`, `_sb`, `_file`, `_linePath`, `_lastLine`, `_lastLineEnd`, `_indent`, `_reads/_moves/_unsequenced/_moved`, `_afterCalls`: `1150-1171`). Output is accumulated in a `StrBuf` per C# `StringBuilder`; `Run()`'s section order (`CEmitter.cs:63-120`: header, `#ifndef BYN_DEBUG` for ownership checks, includes, file-name statics, enums, types, statics, the `byn_errors`/`byn_w_error`/`byn_exit_codes` tables, protos, helper protos, vtables, helper bodies, bodies, main, then the symbol-mark substitution and `ResolveResets`) is one function in `program.byn` so a reader can diff it against `Run()` line by line.

**Entry-point index.** `011-api.md` uses `010-api.md`'s line form (`- T<n> <file>.byn: <signature>  ← <C# file:line> <Name>`), checked verbatim by `SelfhostEmitterApiTests` as `SelfhostSemanticsApiTests.EveryIndexedEntryPointExists` does; T1 seeds it with the cross-file signatures of this plan, and each task edits only its own lines.

**Test harness.** `SelfhostEmitterHarness.Build` compiles `selfhost/emitter/**/*.byn` with `Compilation.Create(sources, packageRoot: selfhost/)` under `BAYAN_CACHE=tmp/cache` in `fast` mode once per run (a `Lazy`, as 010), then `Run(input, mode, tests)` starts the binary and returns stdout/stderr/exit; `AssertSame(name, expected, actual)` reports the first differing line and its number. The C# side of a comparison is `CliApp.EmitC(Compilation.Create([source], dir), mode)` as `RunTests.cs:80-82`, and for `*.test.byn` whatever `ExecuteTests` (`RunTests.cs:79`) builds. Unit tests of a wave task run the task's functions through a generated `main` (010's `RunSections` form) with the C# as oracle.

## Risks & mitigations

- **Importing `semantics` with its `main.byn` in the directory (D2).** Unknown until T1 builds. Mitigation: T1's smoke test is the first thing landed; fallback is a Question and a marked copy of the CLI helpers.
- **Helper and type registration order.** `_typeOrder`/`_helperOrder`/`_staticOrder` decide the output's order and are filled by side effects of `CType`/`Helper`/`Static` during emission; one reordered lookup changes bytes. Mitigation: T2 pre-ports `CType` and `Helper` (`cprint/common.byn`) and T6 ports `Static`, each with its registration points in the same statement order; and the T6 tests compare the type-order list itself against C# on the `run/` corpus before any body is emitted.
- **Floating-point and string literal text.** `FloatLiteral` (`CEmitter.cs:1137`) and `Escape` (`1100`) depend on C# formatting (`R`-style round-trip, invariant culture). Mitigation: T6 tests enumerate every float and string constant in the corpus through both; a Bayan formatting gap becomes a Question, not a workaround that changes bytes.
- **Rune and column arithmetic in power-assert.** `PowerAssert.Column` counts runes; `Shown` spills values in evaluation order. Mitigation: `tests/cases/run/` assert goldens cover it; T5 tests take the diagram text from C#.
- **Line directives.** `#line` bookkeeping (`MapLine`, `ResolveResets`) is byte-visible and depends on where C# chooses not to map (`Statement` restores `_lastLine` when a statement emits nothing: `CEmitter.cs:1465-1470`). Mitigation: `LineDirectiveTests` goldens in T9's tests; parity on the whole corpus in T13.
- **Task cap.** `Builtin` (T11) and `Statement`+ownership (T9) are the two long switches. Mitigation: sizes above are under 1,000; the split rule for T4 is stated.
- **Scripts hard-coded to 010 (D7).** Mitigation: the dispatcher's base commit lands before T1's branch is cut, and T1's guard run is its test.
- **Round 4 (Q1).** The closure parts will be redone, and with them the checker's closure and `ref`/`out` escape rules (Q1's exception, Q745). Every range the redo touches, measured 2026-11-02. Each task that owns a range (T1, T2, T4, T7, T9, T10) marks it with a `// Q1: round 4 redoes this` comment, its brief in `011-tasks.md` names the ranges, and T1 copies this table into `011-api.md`. T11 owns none: `CEmitter.cs:1940-2449` has no `env`, `FunctionType` or `Lambda` reference (`awk` over the range, 2026-11-02), so it carries no marker. Lowering and printing (ported by 011):

  | Range | Method | What it does | Lines | Task |
  |---|---|---|---|---|
  | `LoweredNodes.cs:118` | `LFunctionValue` | the `Env` field | 1 | T1 |
  | `Lowerer.cs:102` | `Note` | a capture is not a frame local | 1 | T2 |
  | `Lowerer.cs:1064-1075` | `LowerExpr` (`BoundLambda` cases) | env tuple built in the creating frame from the captures, then `LFunctionValue` with it | 12 | T4 |
  | `CEmitter.cs:557-560` | `EmitTypes` (`FunctionType` case) | the `{fn, void* env}` struct | 4 | T7 |
  | `CEmitter.cs:627-644` | `FunctionSignature` | `void* env` as first parameter | 18 | T7 |
  | `CEmitter.cs:646-654` | `FunctionThunk` | a named function as a value, ignoring `env` | 9 | T7 |
  | `CEmitter.cs:1181-1186` | `FunctionEmitter` constructor | captures named through the env cast, `((T*)env)->itemN` | 6 | T9 |
  | `CEmitter.cs:1201` | `Signature` | a lambda's `void* env` parameter | 1 | T9 |
  | `CEmitter.cs:1227` | `Emit` | `(void)env;` for a capture-free lambda | 1 | T9 |
  | `CEmitter.cs:1716-1720` | `Expr` (`LFunctionValue` cases) | the `{code, &env}` literal; thunk with `NULL` | 5 | T10 |
  | `CEmitter.cs:1919`, `1924-1927`, `1943` | `Call` | `.env` as first argument, `.fn` as callee | 7 | T10 |

  Lowering and printing total 65 lines (14 lowering, 51 printer), each redone twice in round 4: once in C#, once in the Bayan port. The checker side, which 011 does not port but round 4 also redoes in both checkers (C# and the 010 selfhost checker):

  | C# range | What it is | Lines | Selfhost counterpart (010) |
  |---|---|---|---|
  | `Binder.cs:31`, `BinderExpressions.cs:1922`, `:1932` | the lambda stack, push and pop | 3 | `selfhost/semantics/ctx.byn:623` (`lambdas`), `express4.byn:366` (push), `:399-404` (captures collected, pop), `:415-426` (`inLambda` and the lambda-of helper) |
  | `BinderExpressions.cs:1961-1983` | `Capture` | 23 | `express4.byn:430-473` (`capture`, 44) |
  | `BoundNodes.cs:246-247` | `BoundLambda` with its captures | 2 | `bound.byn:670` (field), `:1631` (children) |
  | `Symbols.cs:245` | `FunctionSymbol.Captures` | 1 | `symbols.byn:182` |
  | `RegionAnalysis.cs:351-352`, `:458`, `:463` | lambda environment region, rule 4's environment exclusion | 4 | `region.byn:52` (environments), `:490-503` (lambda borrow and environment), `region2.byn:131` (exclusion) |
  | `OwnershipAnalysis.cs:383` | captures in ownership | 1 | `ownership.byn:533-537` |
  | `RegionAnalysis.cs` `Call` (after Q745) | written `ref`/`out` arguments and `ref this` receivers take borrows | measured when Q745 lands | `region*.byn`, same change (D13) |

  About 35 C# checker lines and about 75 selfhost ones. The full cost of the second port is the 65 lowering and printing lines in two languages, plus the checker lines in two languages: about 240 lines touched across four implementations. The spec's "about 100" counts the C# side only.
- **Line endings on Windows.** Settled by Q742 (binary-mode stdio in the runtime, `LfOutputTests.cs`). T12's harness compares raw bytes with no newline normalisation; a CRLF in the C# reference string (`AppendLine`, `Console.WriteLine`) is a C# defect: a `work/todo.md` line, then fixed on the C# side, since output text must match. None exists today: `src/Bayan.Backend.C/` and `src/Bayan.Lowering/` have no `AppendLine`, `Environment.NewLine` or `Console.WriteLine`, every line ends in an explicit `'\n'` (`CEmitter.cs:1237`, `:1250`), and `bayan build --emit-c` writes with `stdout.Write(source)` (`CliApp.cs:470`).
- **Paths in `#line` and file statics (Q743, K6).** `FileName`/`FilePath` (T6), `MapLine` (T2) and T12 print paths that C# derives with .NET path functions. Mitigation: P0 lands 014's path slice in `std` before the wave, and the port calls it; `selfhost/emitter/` ports `FilePath`'s own steps (`\` to `/`, a `..` result replaced by the file name) and adds no path logic beyond them. If P0 has not landed when T1's branch is cut, the wave waits, since T2, T6 and T12 depend on it. The 76 known Windows path failures on wp1 stay marked as known, so a Windows parity failure is a new one; each one P0 fixes is removed from the known list in P0's commit. bayan-emit is the exception: T12's `main.byn` takes its package root and input paths through `cli.byn`, which uses 010's `/`-only copies (`compile.byn:1269-1340`: `t11FullPath` returns a `C:\` path unchanged at `:1286-1287`, `t11DirectoryName` returns `/` at `:1278`, `t11RelativePath` prefixes `/` at `:1337-1339`; `json.byn:34`). So on Windows every `#line` and file-static path bayan-emit prints can differ from C#, which fails every input of `SelfhostEmitterParityTests` and the L024/L025 law tests: new test names, beyond the 76. T1 measures them on wp1, not estimated: its smoke build of `bayan-emit` runs every `run/` input on wp1 against `CliApp.EmitC` there, records the failing inputs (count and names) in `011-api.md`, and adds the parity test names to `tests/windows-known-failures.txt` when T12 creates them, each pointing at the 014 `work/todo.md` line that replaces those copies with `std` functions; that 014 change removes them.
- **Speed (R7).** The printer appends to one growable buffer; 010 measured 1.42 on the checker. Risk is `Map` lookups per node for `Constant` and `_names`. Mitigation: T14 measures; a miss over 10 is a Question with the profile, not a rewrite.
- **Review cost.** 010's guard took 20–40 minutes per task. Mitigation: parity tests of a wave task run only that task's class; the whole-corpus parity runs at T13 and in the law tests.

## Coverage check

| Requirement | Where |
|---|---|
| R1 Program | Approach; D1, D2, D10; T12 (`main.byn`, exit codes as bayan-check, inputs with errors compared on stderr and exit code); Data / interfaces (consuming the bound program) |
| R2 Reuse | P0 (paths come from `std`, not a local copy); D2 (`public` only, `cli.byn` move); T1; Risks (import) ; L001/L002/L017/L018/L022/L023 unchanged because no checker line changes |
| R3 Lowered tree, one printer | D1, D9; T1 (`lctx.byn`, boundary test); Data / interfaces (lowered tree, printer boundary) |
| R4 C inventory | D8; T15 (doc, `CInventoryTests`, the token list, multi-item attributes, break-test); Tech / dependencies (runtime sizes) |
| R5 Golden coverage | T12 (every `run/` input in its mode, `*.test.byn` with `--tests`, `CliTests`/`CacheTests` inputs, first differing line); T13 un-skips |
| R6 Generated inputs | T14 (seeds × 3 modes, D10, break-test); L024 draft |
| R7 Speed bound | T14 (`SelfhostEmitterSpeedTests`, 1 MB, median of 5, lower and emit phases only); Risks (speed) |
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
`bayan-emit` prints is byte for byte the C that the C# backend prints for the same input and mode
(`bayan build --emit-c`; for `*.test.byn`, the C the test run builds). The corpus is every `tests/cases/run/` input in the mode its `// mode:` line names, the inputs
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
in memory, or for `*.test.byn` with what `ExecuteTests` builds. `BAYAN_LAW_TIER` unset runs the fixed quick subset (every `run/` input plus the first N
seeds, N measured at T14 to fit about 3 minutes and written here); `BAYAN_LAW_TIER=full` runs every seed. A
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
L023's and does not count against this law, as L018/L023 do today (handoff Q662, Q663). Enacted only after L018 and L023.
```
