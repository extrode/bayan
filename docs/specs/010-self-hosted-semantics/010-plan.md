# 010 — Plan
Spec: ./010-spec.md

## Approach

Port `src/Bayan.Semantics/` (about 14,250 lines) method by method into one Bayan program, `selfhost/semantics/`, the way 009 ported the parser. The module is a directory, so every file sees every other; parallel writers each own whole files. Every function keeps its C# name in camelCase, its C# order of checks and its C# report sites, and a comment at the top of each file names the C# file and line range it ports.

The shape of the port, decided here so the wave does not have to:

- **Package and import.** `selfhost/semantics/*.byn` imports `parser` and `lexer`. `import parser` resolves only under package root `selfhost/` (`Compilation.ResolveImport` joins `PackageRoot`), and the test harness builds with `Compilation.Create(sources, packageRoot: <repo>/selfhost)` then `Toolchain.Build(CliApp.EmitC(c, BuildMode.Fast), BuildMode.Fast)` with `BAYAN_CACHE=tmp/cache`, exactly `SelfhostParserTests.BuildParser` (Q430 still applies: `bayan build` has no package-root option).
- **The parser must become public.** `selfhost/parser/*.byn` has zero `public` declarations today; a module member used from another module is BYN2005. T1 adds `public` to every parser type, union, enum, const, struct field and entry point the checker reads (`Parser`, `Parser.create`, `parseCompilationUnit`, `CompilationUnit`, every node struct and field), and `uses alloc` to the public functions that allocate. Behaviour is unchanged; the parser suite and L017/L018 are rerun.
- **State is `Ctx` plus `i64` handles.** `struct Ctx` (in `ctx.byn`) owns the module/import tables, the diagnostic bag, and per-kind arenas: `List<StructSym> structs`, `List<FuncSym> functions`, `List<LocalSym> locals`, fields, properties, variants, enums, unions, interfaces, consts, type parameters, and one `List` per constructed-type and bound-node kind. Every handle is an index; `-1` is C#'s `null`. Handles break the symbol/type cycles and make maps and sets legal keys. `SourceFile` also represents a parsed `"<builtin>"` source and `"package.byn"` (T9, T10).
- **Types are canonical, and the 128-bit type comes first.** `type Type = i64` encoded as `(kind << 32) | index` over `enum TypeKind`. Constructed types go through `intern…` functions with per-constructor `Map` caches, so C#'s structural equality becomes handle equality. Bayan has no `i128`: `bigint.byn` (T5) is a hand-written `struct Big { u64 lo; u64 hi }` with the operations below, and it lands serially before `types.byn` because `PrimitiveType.Range` (`Types.cs:107-118`) returns it (Q540).
- **Symbols copy facts, they do not store syntax.** The parsed `CompilationUnit` is retained per file (`SourceFile.unit`). Each symbol stores `file: FileId`, `declIndex` (index in `unit.members`), `memberIndex` for nested members, plus the scalars C# reads through `Decl` (`Name`, `Params`, `Fails`, `Uses`, `Becomes`, `Where`, `Attributes`, `MoveThis`, body spans, `ParamsStart`; audited from `\.Decl[?.]*\.` and listed in `symbols.byn`'s header). Anything needing the syntax re-reads it from the retained tree at the point of use. Synthesized C# nodes (`T.Field`'s enum, a distinct alias's struct) become symbols with copied fields, not syntax.
- **Bound nodes are per-kind arenas with the C# fields in order.** `type BExpr = i64`, `BStmt`, `BPattern` are encoded `(kind << 32) | index`; each C# record is a struct in its own list. Nodes keep `span` and `type`; the few C# `Syntax is …` checks become flags/enums recorded at construction. `BoundTree.Children` is ported in C# order because analysis order decides diagnostic ties.
- **No stored closures.** C#'s `List<Action>` (`_wheres`, `_pendingKeys`, `_pendingConstraints`, `_instantiationWork`) and `Instantiating`/`Quietly`/`AfterMembersDeclared(Action)` become explicit `Work` records plus a dispatch switch. Callers are T8 (`AfterMembersDeclared`), T11, T13 and T14; `Instantiating`'s 12 call sites are inlined as sink push, direct call, sink finish/forward, sink pop (Q570).
- **Diagnostics.** T2 owns `Diag` (code, `FileId`, span, message, label, help, severity, fix, related), `removeRepeats`, and a full port of `DiagnosticRenderer`: `Words` (backticks hold spaces), severity, related spans, path-then-start stable sort, and `renderAll` with an `errorLimit` and omitted count (warnings never capped). T26 owns the CLI policy around it: `ErrorLimit = 20`, the `… and N more errors` cap line, the plain-style `✗ build failed: N errors, M warnings` / `explain:` footer, the success summary (timing excluded) and `programName`. Lexer and parser `Diagnostic`s convert with severity error and no related spans. Manifest diagnostics print first as their own sorted, capped block.
- **Loading mirrors `Compilation`.** `FromPath` (`wholeDirectory: !IsScript`, so a file argument loads its whole directory), `FromProject` for a directory with `package.byn`, sorted `*.byn` module files, import aliases and cycles, and display paths: entry files are absolute for rendering and root-relative for `--types`; imports are root-relative for both. The golden mode (T26) instead compiles one entry file with its directory as package root, exactly `DiagTests.cs:20-26`, and uses `includePath = Path.GetFileName(dir) != "diag"`. Paths are passed absolute (R1); relative rendering is Q573.
- **Phase order mirrors `Create`/`Declare`/`BindAll`.** Parse and imports stop before declaring when any syntax error exists; then declare, bind, instantiate work, `InferErrorSets`, `EffectAnalysis`, `CheckMutation`, `CheckRequires`, ownership and lints, flow, region, span lint per function, and `NoMain` last. `create` calls `removeRepeats` when test cases exist (`Compilation.cs:111-112`).
- **Wave discipline and size.** T1, T2, T5, T3, T4 land serially in that order (bigint before symbols/types). T6–T25 and T34–T39 are the wave, 4–6 writers; T26–T27 add the CLI/typed-dump and generator harness. Every file is sized at 1.45 Bayan lines per C# line and no wave task exceeds about 1,000 Bayan lines. Each wave task owns disjoint `.byn` files and `tests/Bayan.Tests/SelfhostSemanticsTnTests.cs`, and calls siblings only through a `struct Tn { static … }` stand-in with the signatures below. Tests needing an unlanded sibling are `[Fact(Skip = "T28 enables …")]`. T28 removes stand-ins and reaches parity; T29–T32 prove and enact L019/L020; T33 writes the docs.

## Tech / dependencies

- Bootstrap compiler on `dev` with specs 006–009 merged. No new language feature is planned; the four gaps below are worked around and recorded as Questions.
- Language features relied on: unions of `Owned<T>`, arenas and `provide allocator`, `List<T>` and `Map<K, V>` with string/tuple/struct keys and insertion order (`selfhost-symbol-tables.byn`), `Symbol`, `StrBuf`, `$"…"`, `ref` parameters, `ref` list/map element mutation, interfaces/dyn refs, non-capturing function values. Not available and worked around: `i128`/`BigInteger`, stored closures, immutable sets/maps, Unicode casing, a cwd builtin.
- Build and test: `Compilation.Create`, `CliApp.EmitC` + `Toolchain.Build(…, BuildMode.Fast)`, `CliApp.Run` for the R1/R2 process comparison, the in-process `Compilation.Create` golden reference, `Golden.Cases`/`Golden.Compare`/`Golden.RepoRoot`, helpers copied from `SelfhostParserTests.cs`. No new packages.
- Reference data: 519 `diag/` cases (11 directories, 2 with `package.byn`, 1 `.test.byn`), 18 `typed/` cases. The C# sources are `src/Bayan.Semantics/` plus `src/Bayan.Syntax/Diagnostics/{Diagnostic,DiagnosticCodes,DiagnosticRenderer}.cs` and `src/Bayan.Syntax/Packages/Manifest.cs`.

## Changes by area

`selfhost/parser/` (T1 only, behaviour-neutral): `public` on the declarations and fields the checker reads, `uses alloc` on `parseCompilationUnit` and `Parser.create`. No other edit.

**selfhost/semantics/** (new; one file per C# area, split when over ~1,000 Bayan lines; stubs created by T1):

| File | C# source | Owner | Est. Bayan lines |
|---|---|---|---|
| `ctx.byn` | new: handles, `Ctx`, `Binder`, `SourceFile`, `Mod`, `Diag` bag | T1 (extended T3, T4; frozen after) | 500 |
| `codes.byn` | `Bayan.Syntax/Diagnostics/DiagnosticCodes.cs` BYN2xxx–BYN8xxx plus BYN1016/1054/1056 | T2 | 650 |
| `diag.byn` | `Bayan.Syntax/Diagnostics/{Diagnostic,DiagnosticRenderer}.cs`; `removeRepeats`; render, sort, `errorLimit` | T2 | 600 |
| `symbols.byn` | `Symbols/Symbols.cs` (621) | T3 | 900 |
| `types.byn` | `Symbols/Types.cs` (565) + interning; `range` uses `Big` | T3 | 820 |
| `bound.byn` | `BoundNodes.cs` (406), `BoundTree.cs` (123) | T4 | 770 |
| `bigint.byn` | new: `struct Big`, signed two-limb 128-bit | T5 | 500 |
| `conversions.byn` | `Conversions.cs` (100), `TypeFacts.cs` (67) | T6 | 250 |
| `fold.byn` | `Binding/ConstantFolding.cs` (185) | T6 | 270 |
| `naming.byn` | `Naming.cs` (315), `Lexicon.cs` (113), `Particles.cs` (34) | T7 | 670 |
| `typeresolve.byn` | `TypeResolver.cs` (507) | T8 | 735 |
| `builtins.byn` | `Builtins.cs` (474), `Binding/BuiltinMembers.cs` (143) | T9 | 895 |
| `manifest.byn` | `Bayan.Syntax/Packages/Manifest.cs` (476) | T10 | 690 |
| `compile.byn` | `Compilation.cs` 1–~654 (load, imports, `Declare` order, `DeclareFile`, `addType`/`addFunction`; `Work`, sinks) | T11 | 940 |
| `declare.byn` | `Compilation.cs` ~655–1037 (variants, payloads, enums, unions, struct members, overloads) | T12 | 555 |
| `declare2.byn` | `Compilation.cs` ~1038–1362 (interface members, signatures, parameters, shapes, states, recursion) | T38 | 471 |
| `generics.byn` | `Compilation.cs` 1363–1952 (`BindAll`, instantiation, error sets, mutation/requires) | T13 | 856 |
| `conformance.byn` | `Conformance.cs` (238) | T14 | 345 |
| `binder.byn` | `Binder.cs` 1–~499 (state, scopes, locals, entry points) | T15 | 725 |
| `binder2.byn` | `Binder.cs` ~500–979 (blocks, statement dispatch, assert, narrowing, while-let, for head) | T16 | 696 |
| `binder3.byn` | `Binder.cs` ~980–1468 (for bodies/iterators, return/fail, defer, context, switch statement) | T36 | 708 |
| `express.byn` | `BinderExpressions.cs` 1–~599 (conversions, literals, names) | T17 | 845 |
| `express2.byn` | `BinderExpressions.cs` ~600–1155 (members, index, slice) | T18 | 806 |
| `express3.byn` | `BinderExpressions.cs` ~1156–1640 (unary/binary/folding, coalesce, ranges, conditional, casts, struct/with) | T19 | 703 |
| `express4.byn` | `BinderExpressions.cs` ~1641–2158 (collections, map/tuple, spread, lambdas, stackalloc, interpolation) | T35 | 751 |
| `calls.byn` | `BinderCalls.cs` 1–~587 (calls, overloads, arguments) | T20 | 825 |
| `calls2.byn` | `BinderCalls.cs` ~588–998 (construction, builtins, move, switch subject) | T21 | 596 |
| `calls3.byn` | `BinderCalls.cs` ~999–1577 (try/catch, switch expressions, patterns, guard, resolve) | T34 | 840 |
| `calls4.byn` | `BinderCalls.cs` ~1578–1956 (exhaustiveness, assignment, inc/dec, writability) | T39 | 550 |
| `states.byn`, `errorcases.byn` | `BinderStates.cs` (309), `BinderErrors.cs` (112) | T22 | 610 |
| `flow.byn`, `effects.byn` | `FlowAnalysis.cs` (370), `EffectAnalysis.cs` (281), `ErrorFlow.cs` (33) | T23 | 992 |
| `ownership.byn` | `OwnershipAnalysis.cs` (474), three lints (193) | T24 | 967 |
| `region.byn` | `RegionAnalysis.cs` 1–~417 (regions, borrows, loans, Stmt/Expr/Compute) | T25 | 605 |
| `region2.byn` | `RegionAnalysis.cs` ~418–882 (calls, stores, mutations, last-use, reporting) | T37 | 674 |
| `main.byn`, `typed.byn`, `json.byn` | `CliApp.Check/Types/Compile` subset, `TypedDump.cs` (101) | T26 | 508 |

Serial foundation tasks T2 (1,250) and T3 (1,720) exceed the wave budget; accepted because each is one serial writer, and recorded in Q525 and Q530.

**tests/Bayan.Tests/**: `SelfhostSemanticsTests.cs` (T1 build `Lazy` and smoke test; T26 golden and CLI theories), `SelfhostSemanticsGenerator.cs`/`GeneratorTests.cs` (T27), `SelfhostSemanticsEquivalenceLawTests.cs` (T29), `SelfhostSemanticsNoPanicLawTests.cs` (T31), and `SelfhostSemanticsTnTests.cs` for T6–T25 and T34–T39.

**docs/**: `docs/laws/019-binder-equivalence.md`, `020-no-checker-panic.md` (T29–T32), `docs/bayan-handoff.md` (Questions 520 upward, progress entry, Road row 010, readiness), `CHANGELOG.md`, `010-spec.md` status (T33). `work/todo.md` lines for C# bugs matched.

## Data / interfaces

**Handles** (`ctx.byn`, T1): `type FileId = i64`, `ModId`, `StructId`, `InterfaceId`, `ErrorSetId`, `EnumId`, `UnionId`, `FuncId`, `LocalId`, `ConstId`, `TypeParamId`, `FieldId`, `PropertyId`, `VariantId`, `Type`, `BExpr`, `BStmt`, `BPattern`; `-1` is none; `Type`/bound handles are `(kind << 32) | index`.

**`Ctx`** (T1, extended by T3/T4): `List<SourceFile> files`, `List<Mod> modules`, `List<Diag> diagnostics`, `List<DiagBag> bags`, `i64 sink` (-1 main), `bool quiet`, options (`library`, `capabilities`, `packageName`); T3 adds symbol/type arenas, intern maps, and the shared caches `instances`, `instantiationWork`, `pendingKeys`, `pendingConstraints`, `states`, `checks`, `distinct`, `positional`, `aliases`, `invokeSymbols`, `validationErrors`, `conformancesChecked`; T4 adds the bound arenas. Frozen after T4; one owner per field.

**`Binder`** (T1): `FileId file`, `FuncId fn`, `StructId owner`, `i64 nextLocalId`, `List<Map<string, LocalId>> scopes`, `List<JumpTarget> targets`, lambda records/counts, `Map<i64, i64> pending`/`constraints`/`peers` keyed by the `LocalDecl` span start, `List<(i64, LocalId)> nonNull`, state facts, `Map<LocalId, VariantId> errorNarrowed`, held-bag index.

**Diagnostics and sinks** (`diag.byn`, T2): `struct Related { TextSpan span; string label }`; `Diag { string code; FileId file; TextSpan span; string message; string label; string help; bool isWarning; bool hasFix; TextSpan fixSpan; string fixReplacement; List<Related> related }`; `void report(ref Ctx c, SemaCode code, FileId f, TextSpan span, by (string a0, string a1, string a2, string a3, string a4) args, string label = "", string help = "", bool hasFix = false, TextSpan fixSpan = TextSpan { start: 0, length: 0 }, string fixReplacement = "")`; `i64 pushBag(ref Ctx c)`; `void popBag(ref Ctx c, i64 previous)`; `void removeRepeats(ref Ctx c)`; `string render(ref Ctx c, in Diag d, bool includePath)`; `string renderAll(ref Ctx c, in List<Diag> diags, bool includePath, i64 errorLimit, by ref i64 omitted)`; `SemaCode` with `codeText`, `messageFormat`, `isWarning`, including BYN1016, BYN1054 and BYN1056.

**Deferred work** (T11 owns the type and dispatch): `enum WorkKind { where, keyCheck, constraint, interfaceMembers, members, unionVariants, conform, fieldDefault, bindFunction }`; `struct Work { WorkKind kind; Type subject; List<Type> args; FuncId instance; FileId file; TextSpan span; i64 declIndex; i64 memberIndex }`. Drains: `_wheres`/`_pendingKeys` in `declare` (T11); `_instantiationWork` in `bindAll` (T13); `_pendingConstraints` in `checkConformances` (T14).

**Cross-file entry points.** This is the complete index: every method one owned file calls in another, generated by grepping each C# owner. A caller adds a `struct Tn { static … }` stand-in with the C# signature under these substitutions: `Compilation c` -> `ref Ctx c`, `Binder b` -> `ref Binder b`, `XSymbol` -> `XId`, `TypeSymbol` -> `Type`, `SourceText site` -> `FileId site`, `Name` -> `(FileId, TextSpan)`, `IEnumerable<X>` -> `List<X>`, `T?` on a handle -> `-1`. Structs, enums and arenas are built directly (fields are module-visible), so C# record constructors are not entry points. Each name has exactly one owner.

- T3: `displayNameAmong`, `findField`, `findProperty`, `findMethods`, `indexOf`, `isValidKey`, `invalidKeyPart`, `occurs`, `of`.
- T4: `children`, `descendants`.
- T5: `bigFromI64`, `bigParse`, `bigAdd`, `bigSub`, `bigMul`, `bigDiv`, `bigRem`, `bigShl`, `bigShr`, `bigAnd`, `bigOr`, `bigXor`, `bigNot`, `bigCmp`, `bigFits`, `bigWrap`, `bigSaturate`, `bigMin`, `bigMax`, `bigText`, `bigToI64`.
- T6: `implicit`, `explicit`, `isWidening`, `common`, `isUnique`, `uniquePart`, `whyUnique`, `foldUnary`, `foldBinary`, `foldCast`, `evaluate`.
- T7: `checkType`, `checkValue`, `checkInterface`, `checkMembers`.
- T8: `resolve`, `resolveBound`, `expand`, `expandAlias`, `effectsOf`.
- T9: `builtinType`, `find`, `findInterface`, `findIo`, `findString`, `findUtf8`, `intParse`, `hasMethod`, `method`, `names`, `property`.
- T10: `manifestRead`, `manifestParse`, `manifestErrorCount`, `camelOf`, `kebabOf`.
- T11: `declare`, `declareFile`, `loadModule`, `resolveImport`, `addType`, `addFunction`, `declareTypeParameters`, `declareWhere`, `resolveInterface`, `associatedType`, `afterMembersDeclared`, `reportInto`, `runWork`.
- T12: `declareMembers`, `declareVariants`, `declarePayloads`, `declareEnumMembers`, `declareEnumFields`, `declareUnionVariants`, `checkComposition`, `checkOverloads`, `checkUnique`, `reportDuplicate`, `overlap`, `loc`, `accessOf`.
- T13: `instantiateFunction`, `instantiateType`, `instantiatingBegin`, `instantiatingEnd`, `invokeSymbol`, `bindAll`, `bindFunction`, `inferErrorSets`, `checkConstraints`, `checkMutation`, `checkRequires`, `fieldDefault`, `errorSetsOf`, `templateOf`, `bindConstant`, `parameterDefault`, `reportHeld`.
- T14: `checkConformances`, `conform`, `associatedBinding`, `conformancesChecked`, `quietlyBegin`, `quietlyEnd`.
- T15: `hold`, `release`, `bindFunctionBody`, `bindChecks`, `bindParameterDefault`, `bindExpressionBody`, `bindGetter`, `bindConstructor`, `bindMain`, `bindTest`, `bindFieldDefault`, `bindEnumArguments`, `bindModuleConstant`, `bindArraySize`, `checkValueName`, `declareLocal`, `addLocal`, `pushScope`, `popScope`, `resolveType`.
- T16: `bindBlock`, `bindStatement`, `bindLocalDecl`, `bindCondition`, `bindPresent`, `bindExitCode`, `checkUniqueConst`, `defaultType`, `isNonNull`, `narrowed`.
- T17: `bindExpr`, `bindExprOrType`, `bindExprFor`, `bindConverted`, `convert`, `asValue`, `bindLiteral`, `bindStatementExpression`, `constrain`, `findPeer`, `fits`, `isPending`, `isUntypedLiteral`, `literal`, `parseInteger`, `suggest`.
- T18: `bindName`, `bindMember`, `bindTargetMember`, `bindValueMember`, `bindImplicitThisMember`, `bindIndex`, `bindSlice`, `checkAccess`, `constantRef`, `constantLiteral`, `lookupMember`, `returnTypeThrough`, `reportRenamed`, `memberSuggestion`, `interpolationSource`.
- T19: `bindUnary`, `bindBinary`, `fold`, `bindCoalesce`, `bindRange`, `bindIn`, `bindConditional`, `bindCast`, `bindConversion`, `bindStructLiteral`, `bindWith`, `binaryText`, `fact`, `operatorApplies`, `isEquatable`.
- T20: `bindCall`, `bindArguments`, `bindArgumentsForRecovery`, `bindFunctionCall`, `bindTryFrom`, `bindTryParse`, `bindParse`, `label`, `stripParens`.
- T21: `bindConstruction`, `bindIntegerText`, `bindOwned`, `bindUntrusted`, `bindTrust`, `bindTrustUnchecked`, `bindBuiltinMethodCall`, `bindMove`, `bindSwitchSubject`, `checked`, `handles`, `reportUnhandled`.
- T22: `bindBecome`, `bindErrorCasePattern`, `bindErrorNew`, `bindPayload`, `bindPayloadField`, `built`, `checkBecomesCall`, `checkPayloadGiven`, `checkStatesExhaustive`, `errorCase`, `forgetChangingStates`, `forgetState`, `knownState`, `learnArmState`, `learnBecomes`, `learnState`, `learnStates`, `receiverStates`, `reportWrongState`, `stateStruct`, `statesUnion`, `subject`, `unlearnStates`.
- T23: `analyzeFlow`, `analyzeEffects`, `propagated`, `uncaught`, `reportUnknown`.
- T24: `analyzeOwnership`, `lintNeverReassignedVar`, `lintSpanParameter`, `checkPanicHandler`.
- T25: `analyzeRegions`, `runRegions`, `stmtRegions`, `exprRegions`, `computeBorrow`, `declareBorrow`.
- T26: `typedDump`, `capLine`, `buildFailed`, `buildExplain`, `checkSummary`, `programName`.
- T34: `bindTry`, `bindCatch`, `bindSwitchExpr`, `bindPattern`, `checkGuardWrite`, `checkOwningOverlap`, `guardBindings`, `unwrap`, `resolveStep`.
- T35: `bindTuple`, `bindCollection`, `bindSpread`, `bindMapLiteral`, `bindLambda`, `capture`, `inLambda`, `bindStackalloc`, `bindInterpolated`, `bindFormatSpec`, `whyNotFormattable`, `propertyType`.
- T36: `bindReturn`, `bindFail`, `bindBreak`, `bindContinue`, `bindErrdefer`, `bindPanicDefer`, `bindOnPanic`, `bindContext`, `bindSwitchStatement`, `userElement`, `declaredElement`, `notIterable`, `bindForBody`, `bindForTry`, `inPanicHandler`, `findTarget`, `tryBindFailure`.
- T37: `callBorrow`, `storeBorrow`, `storeIntoBorrow`, `checkOutlives`, `checkReturn`, `lendBorrow`, `allocatedFrom`, `mutateBorrow`, `mutateOwners`, `referBorrow`, `rootLocal`, `thisField`, `checkMutations`, `mayBorrow`, `lineOf`.
- T38: `declareInterfaceMembers`, `declareSignature`, `declareParameters`, `declareCheck`, `checkStateNames`, `statesOf`, `validationErrorOf`, `checkStructShape`, `checkUnionShape`, `checkRecursion`, `functionTypeNotHere`, `refReceiverFix`, `rejectStatic`, `rejectModifiers`.
- T39: `checkExhaustive`, `bindAssign`, `bindIncDec`, `checkWritable`, `collect`, `irrefutable`, `mapWriteTarget`, `reportMutatesWithoutRefThis`, `writtenLocal`, `compoundOperator`.

**Full signatures for the generated list:** `i64 hold(ref Binder b)`, `List<(LocalId, Type)> release(ref Binder b)`, `void bindTest(ref Binder b, in TestDecl d, in List<Expr> values)`, `void bindGetter(ref Binder b, in PropertyDecl d)`, `void bindConstructor(ref Binder b, in ConstructorDecl d)`, `BExpr bindFieldDefault(ref Binder b, FieldId f)`, `void bindEnumArguments(ref Binder b, EnumId e)`, `void bindModuleConstant(ref Binder b, ConstId k)`, `BExpr bindParameterDefault(ref Binder b, LocalId p)`, `i32 bindExitCode(ref Binder b, in AttributeSyntax a)`, `i64 bindArraySize(ref Binder b, in Expr size)`, `TypeParamId associatedType(ref Ctx c, TypeParamId owner, string name)`, `bool overlap(ref Ctx c, FuncId a, FuncId b)`, `FuncId? templateOf(ref Ctx c, FuncId f)`, `void bindConstant(ref Ctx c, ConstId k)`, `BExpr? parameterDefault(ref Ctx c, FuncId f, LocalId p)`, `void reportHeld(ref Ctx c, in List<Diag> held)`, `void afterMembersDeclared(ref Ctx c, in Work work)`, `void reportInto(ref Ctx c, i64 bag)`, `string? suggest(string name, in List<string> candidates)`, `Big parseInteger(string text)`, `bool isEquatable(Type t)`, `bool checkGuardWrite(ref Binder b, BExpr target, TextSpan span, string why)`, `void reportMutatesWithoutRefThis(ref Ctx c, FuncId fn, TextSpan span)`, `FuncId? find(ref Ctx c, string name)`, `List<string> names(Type t)`, `void reportUnknown(ref Ctx c, FileId f, TextSpan name)`. `afterMembersDeclared` and `reportInto` are owned by T11 although their C# lines (1549, 1922) fall in T13's range; T8, T15, T16, T14 and T38 are callers only.

**Harness** (`SelfhostSemanticsTests.cs`, T26): `Built` = `Lazy<string>` over all `selfhost/semantics/*.byn`. Two theories. **Golden (R5):** copy each `diag/` case to `tmp/semantics-cases/<slug>/`; reference is in-process `Compilation.Create([SourceText of the case file], caseDir, new CompilationOptions(IncludeTests: name ends `.test.byn`, Capabilities: manifest?.Capabilities, PackageName: manifest?.Name))` with `manifest = package.byn` in `caseDir`, rendered by `DiagnosticRenderer.RenderAll(c.Diagnostics.Items, includePath: Path.GetFileName(caseDir) != "diag")`; `bayan-check --golden --package <caseDir> --include-path` reproduces it, with `--tests` for `.test.byn`. **CLI (R1/R2):** for cases where `bayan check` is meaningful, both via `CliApp.Run(["check"|"types", absoluteCopy])`, comparing stderr/exit and `--types` bytes. `Golden.Compare` names the input and first differing line. `--many` drives corpora; `OneMegabyte` timing median of 5, `BAYAN_SKIP_TIMING=1`. Until T28, golden/CLI parity theories are `Skip = "T28 enables …"`.

## Risks & mitigations

- **No 128-bit integer (T5, Q540–Q544).** C# parses literals with `BigInteger` and folds in `Int128`; Bayan has no `i128`. `bigint.byn` is a hand-written `struct Big { u64 lo; u64 hi }` (two's complement) with parse, compare, add, sub, mul, div, rem, shifts, bit ops, wrap/saturate, `bigMin`/`bigMax` for `PrimitiveType.Range`, and decimal text. It lands before T3. A literal wider than 128 bits is reported out of range from its normalized digit text; boundary cases for every integer type are tested against a C# `Int128`/`BigInteger` oracle.
- **No stored closures (T8/T11/T13/T14, Q570–Q574).** `List<Action>` becomes `List<Work>` with a dispatch switch owned by T11; `AfterMembersDeclared` from T8 queues a key check; `Instantiating`'s 12 call sites and `Quietly` become sink push/pop. Work records hold handles, file ids and spans, never syntax. `Holding<T>`/`Narrowed<T>` become save/restore around direct calls.
- **No immutable collections (T25/T37, Q640–Q642).** `RegionAnalysis`'s `ImmutableHashSet`/`ImmutableDictionary` borrows become copied `List<…>`/`Map` snapshots; the flow-insensitive analysis and last-use rules are ported unchanged.
- **ASCII-only casing (T7/T8/T17, Q550–Q552).** `Naming`, `TypeResolver` (`char.IsLower`) and `Suggest` (`ToLowerInvariant`) are casing-sensitive; Bayan v0.1 `lowercased`/`uppercased` are ASCII. Every current diag/typed case is ASCII; a non-ASCII identifier may get a different fix and the Question records it.
- **Parser visibility cascade (T1).** `public` turns on effect checking, so public entry points need `uses alloc`; if a construct cannot be made public, T1 stops and records the exact diagnostic. A blocking compiler bug is fixed in T28 (R8) with a test.
- **Symbols must not store syntax (T3).** `Owned` payloads cannot be copied and a struct cannot hold a borrow. Symbols copy scalars and keep `(FileId, declIndex, memberIndex)`; the T3 header lists the `\.Decl` audit. If a site cannot be expressed, T3 records it and T28 decides.
- **Builtins are parsed Bayan source (T9).** `Builtins.cs` builds its types from `SyntaxTree.Parse(SourceText.FromString("<builtin>", …))`; T9 registers parsed `"<builtin>"` `SourceFile`s with T1's `Ctx.files`, and `declare` reads their variants like any other file.
- **Map is unique and ordered (T3/T11).** Tables are `Map<string, Type>`/`Map<string, List<FuncId>>`; copies use `.copy(to: arena)`; if a nested list cannot be mutated through the map, map to an index into a `List<List<FuncId>>`.
- **Path and ordering parity (T11/T26, Q573).** Golden mode compiles one entry file with its directory as root and uses the `Path.GetFileName(dir) != "diag"` rule for `includePath`; the CLI mode takes absolute paths. Ordinal sorts use `List<string>.sort()` (UTF-8 order). Relative arguments and non-ASCII names are Questions.
- **Diagnostic ties.** Output is sorted by path then span; equal keys keep report order. Every pass keeps C# call order, and `BoundTree.Children` order is ported exactly. Any `Dictionary` whose iteration leaks into output becomes an insertion-ordered `Map`.
- **Performance (T26/T27/T28).** The 1 MB bound is 10x; `--time` prints parse/declare/bind/analysis nanoseconds. Fixes stay inside `selfhost/semantics/`.
- **C# bugs matched (any task).** Where the reference is wrong, the port matches it and the case goes to `work/todo.md`; T33 consolidates.
- **Wave writers cannot see parity until merge.** Every wave done-when is checkable in its own slot; T28 owns parity, the full suite and the measured ratio.

## Coverage check

| Requirement / law | Tasks |
|---|---|
| R1 program, cap line, footer, summary, exit code | T2 (renderer/cap application), T26 (cap line, footer, summary, CLI), T10 (manifest), T11 (loading), T28 (parity) |
| R2 typed dump | T3 (`typeDisplay`), T35 (lambda names/captures), T26 (`typed.byn`, bytes), T28 |
| R3 reuse, L001/L002/L017/L018 | T1 (public parser, suites rerun) |
| R4 analyses | T6–T27, T34–T39 |
| R5 golden coverage, first differing line | T26 (golden mode), T28 |
| R6 generator, fixed seeds, sensitivity | T27, T28 |
| R7 speed bound | T26 (`--time`), T27 (`OneMegabyte`), T28 (measured) |
| R8 port friction, blocking compiler bug | every task's Question range; T28 (compiler fix with test); T33 |
| R9 docs | T33 |
| L019 | T29 prove, T30 enact |
| L020 | T26 (`--spans`), T31 prove, T32 enact |
