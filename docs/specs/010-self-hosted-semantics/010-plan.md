# 010 — Plan
Spec: ./010-spec.md

## Approach

Port `src/Bayan.Semantics/` (about 14,250 lines) method by method into one Bayan program, `selfhost/semantics/`, the way 009 ported the parser. The module is a directory, so every file sees every other; parallel writers each own whole files. Every function keeps its C# name in camelCase, its C# order of checks and its C# report sites, and a comment at the top of each file names the C# file and line range it ports.

The shape of the port, decided here so the wave does not have to:

- **Package and import.** `selfhost/semantics/*.byn` imports `parser` and `lexer`. `import parser` resolves only under package root `selfhost/` (`Compilation.ResolveImport` joins `PackageRoot`), and the test harness builds with `Compilation.Create(sources, packageRoot: <repo>/selfhost)` then `Toolchain.Build(CliApp.EmitC(c, BuildMode.Fast), BuildMode.Fast)` with `BAYAN_CACHE=tmp/cache`, exactly `SelfhostParserTests.BuildParser` (Q430 still applies: `bayan build` has no package-root option).
- **The parser must become public.** `selfhost/parser/*.byn` has zero `public` declarations today; a module member used from another module is BYN2005. T1 adds `public` to every parser type, union, enum, const, struct field and entry point the checker reads (`Parser`, `Parser.create`, `parseCompilationUnit`, `CompilationUnit`, every node struct and field), and `uses alloc` to the public functions that allocate. Behaviour is unchanged; the parser suite and L017/L018 are rerun.
- **State is `Ctx` plus `i64` handles.** `struct Ctx` (in `ctx.byn`) owns the module/import tables, the diagnostic bag, and per-kind arenas: `List<StructSym> structs`, `List<FuncSym> functions`, `List<LocalSym> locals`, fields, properties, variants, enums, unions, interfaces, consts, type parameters, and one `List` per constructed-type and bound-node kind. Every handle is an index; `-1` is C#'s `null`. Handles break the symbol/type cycles and make maps and sets legal keys.
- **Types are canonical.** `type Type = i64` encoded as `(kind << 32) | index` over `enum TypeKind` (primitive, struct, interface, errorSet, enum, union, optional, span, textView, dynRef, list, map, owned, untrusted, array, range, tuple, function, result, state, builtin, errorUnion, errorType). Constructed types go through `intern…` functions with per-constructor `Map` caches, so C#'s structural equality becomes handle equality and `Map<Type, …>`/`HashSet<Type>` port directly. `Int128` and `BigInteger` are replaced by `bigint.byn` (two `u64` limbs, Q540).
- **Symbols copy facts, they do not store syntax.** The parsed `CompilationUnit` is retained per file (`SourceFile.unit`). Each symbol stores `file: FileId`, `declIndex` (index in `unit.members`), `memberIndex` for nested members, plus the scalars C# reads through `Decl` (`Name`, `Params`, `Fails`, `Uses`, `Becomes`, `Where`, `Attributes`, `MoveThis`, body spans, `ParamsStart`; audited from `\.Decl[?.]*\.` and listed in `symbols.byn`'s header). Anything needing the syntax re-reads it from the retained tree at the point of use, e.g. `bindParameterDefault` walks to the `Param` and binds its default while borrowed. Synthesized C# nodes (`T.Field`'s enum, a distinct alias's struct) become symbols with copied fields, not syntax.
- **Bound nodes are per-kind arenas with the C# fields in order.** `type BExpr = i64`, `BStmt`, `BPattern` are encoded `(kind << 32) | index`; each C# record is a struct in its own list. Nodes keep `span` and `type`; the few C# `Syntax is …` checks become flags/enums recorded at construction (literal/unary kind, `isDiscard`, arm value span). `BoundTree.Children` is ported in C# order because analysis order decides diagnostic ties.
- **No stored closures.** C#'s `List<Action>` (`_wheres`, `_pendingKeys`, `_instantiationWork`, `_pendingConstraints`) and `Instantiating`/`Quietly`/`AfterMembersDeclared(Action)` become explicit `List<Work>` records plus a dispatch switch (Q570). `Instantiating`'s 13 call sites are inlined as sink push, direct call, sink finish/forward, sink pop.
- **Diagnostics.** The checker has its own `Diag` (code, `FileId`, span, message, label, help, severity, fix, related) and a full port of `DiagnosticRenderer`, including `Words` (backticks hold spaces), severity, related spans, path-then-start stable sort, the 20-error cap (warnings never capped) and the plain-style `✗ build failed: N errors, M warnings` / `explain:` footer. Lexer and parser `Diagnostic`s convert with severity error and no related spans. Manifest diagnostics print first as their own sorted, capped block, as `CliApp.Compile` does.
- **Loading mirrors `Compilation`.** `FromPath` (`wholeDirectory: !IsScript`, so a file argument loads its whole directory), `FromProject` for a directory with `package.byn`, sorted `*.byn` module files, import aliases and cycles, display paths: entry files are absolute for rendering and root-relative for `--types`; imports are root-relative for both. Paths are passed absolute (R1); relative rendering is a Question (Q573).
- **Phase order mirrors `Create`/`Declare`/`BindAll`.** Parse and imports stop before declaring when any syntax error exists; then declare, bind, instantiate work, `InferErrorSets`, `EffectAnalysis`, `CheckMutation`, `CheckRequires`, ownership and lints, flow, region, span lint per function, and `NoMain` last.
- **Wave discipline.** T1–T4 are serial. T5–T25 each own disjoint `.byn` files and add `tests/Bayan.Tests/SelfhostSemanticsTnTests.cs` that builds only the foundation files it needs plus a test `main`. T26–T27 add the CLI, typed-dump and generator harness. A sibling call goes through a `struct T#N { static … }` stand-in block in the caller's own file, with the signatures fixed below; tests that need an unlanded sibling are `[Fact(Skip = "T28 enables …")]`. T28 removes stand-ins and reaches parity. T29–T32 prove and enact L019/L020; T33 writes the docs.

## Tech / dependencies

- Bootstrap compiler on `dev` with specs 006–009 merged. No new language feature is planned; the four gaps below are worked around and recorded as Questions.
- Language features relied on: unions of `Owned<T>`, arenas and `provide allocator`, `List<T>` and `Map<K, V>` with string/tuple/struct keys and insertion order (`selfhost-symbol-tables.byn`), `Symbol`, `StrBuf`, `$"…"`, `ref` parameters, `ref` list/map element mutation, interfaces/dyn refs, non-capturing function values. Not available and worked around: `Int128`/`BigInteger`, stored closures, immutable sets/maps, Unicode casing, a cwd builtin.
- Build and test: `Compilation.Create`, `CliApp.EmitC` + `Toolchain.Build(…, BuildMode.Fast)`, `CliApp.Run` called in-process as the C# reference for `check`/`types`, `Golden.Cases`/`Golden.Compare`/`Golden.RepoRoot`, process helpers copied from `SelfhostParserTests.cs`. No new packages.
- Reference data: 519 `diag/` cases (12 directories, 2 with `package.byn`, 1 `.test.byn`), 18 `typed/` cases.

## Changes by area

`selfhost/parser/` (T1 only, behaviour-neutral): `public` on the declarations and fields the checker reads, `uses alloc` on `parseCompilationUnit` and `Parser.create`. No other edit.

**selfhost/semantics/** (new; one file per C# area, split when over ~1,000 lines; stubs created by T1):

| File | C# source | Owner | Est. Bayan lines |
|---|---|---|---|
| `ctx.byn` | new: handles, `Ctx`, `Binder`, `SourceFile`, `Mod`, `Diag` bag | T1 (extended T3, T4; frozen after) | 500 |
| `codes.byn` | `DiagnosticCodes.cs` BYN2xxx–BYN8xxx descriptors, 5-arg `format` | T2 | 650 |
| `diag.byn` | `Diagnostic.cs`, `DiagnosticRenderer.cs` (severity, related, cap, footer) | T2 | 600 |
| `symbols.byn` | `Symbols/Symbols.cs` (621) | T3 | 750 |
| `types.byn` | `Symbols/Types.cs` (565) + type interning | T3 | 850 |
| `bound.byn` | `BoundNodes.cs` (406), `BoundTree.cs` (123) | T4 | 850 |
| `bigint.byn` | new: signed 128-bit, replacing `Int128`/`BigInteger` | T5 | 500 |
| `conversions.byn` | `Conversions.cs` (100), `TypeFacts.cs` (67) | T6 | 250 |
| `fold.byn` | `Binding/ConstantFolding.cs` (185) | T6 | 350 |
| `naming.byn` | `Naming.cs` (315), `Lexicon.cs` (113), `Particles.cs` (34) | T7 | 550 |
| `typeresolve.byn` | `TypeResolver.cs` (507) | T8 | 650 |
| `builtins.byn` | `Builtins.cs` (474), `Binding/BuiltinMembers.cs` (143) | T9 | 700 |
| `manifest.byn` | `Packages/Manifest.cs` (476) | T10 | 550 |
| `compile.byn` | `Compilation.cs` 1–~600 (load, imports, `Declare` order, `DeclareFile`, signatures) | T11 | 850 |
| `declare.byn` | `Compilation.cs` ~600–1362 (variants, members, interface members, shapes, recursion) | T12 | 900 |
| `generics.byn` | `Compilation.cs` 1364–1952 (`BindAll`, instantiation, error sets, mutation/requires) | T13 | 800 |
| `conformance.byn` | `Conformance.cs` (238) | T14 | 350 |
| `binder.byn` | `Binder.cs` 1–~700 | T15 | 850 |
| `binder2.byn` | `Binder.cs` ~700–1468 | T16 | 850 |
| `express.byn` | `BinderExpressions.cs` 1–~700 | T17 | 850 |
| `express2.byn` | `BinderExpressions.cs` ~700–1450 | T18 | 850 |
| `express3.byn` | `BinderExpressions.cs` ~1450–2158 | T19 | 800 |
| `calls.byn` | `BinderCalls.cs` 1–~960 | T20 | 900 |
| `calls2.byn` | `BinderCalls.cs` ~960–1956 | T21 | 900 |
| `states.byn`, `errorcases.byn` | `BinderStates.cs` (309), `BinderErrors.cs` (112) | T22 | 600 |
| `flow.byn`, `effects.byn` | `FlowAnalysis.cs` (370), `EffectAnalysis.cs` (281), `ErrorFlow.cs` (33) | T23 | 850 |
| `ownership.byn` | `OwnershipAnalysis.cs` (474), three lints (193) | T24 | 750 |
| `region.byn` | `RegionAnalysis.cs` (882) | T25 | 950 |
| `main.byn`, `typed.byn`, `json.byn` | `CliApp.Check/Types/Compile` subset, `TypedDump.cs` (101) | T26 | 650 |

**tests/Bayan.Tests/**: `SelfhostSemanticsTests.cs` (T1 creates the build `Lazy` and a smoke test; T26 adds the diag/typed theories), `SelfhostSemanticsGenerator.cs` and `SelfhostSemanticsGeneratorTests.cs` (T27), `SelfhostSemanticsEquivalenceLawTests.cs` (T29), `SelfhostSemanticsNoPanicLawTests.cs` (T31), and `SelfhostSemanticsT5Tests.cs` … `T25Tests.cs`, one per wave task.

**docs/**: `docs/laws/019-binder-equivalence.md`, `020-no-checker-panic.md` (T29–T32), `docs/bayan-handoff.md` (Questions 520 upward, progress entry, Road row 010, readiness), `CHANGELOG.md`, `010-spec.md` status (T33). `work/todo.md` lines for C# bugs matched.

## Data / interfaces

**Handles** (`ctx.byn`, T1): `type FileId = i64`, `ModId`, `StructId`, `InterfaceId`, `ErrorSetId`, `EnumId`, `UnionId`, `FuncId`, `LocalId`, `ConstId`, `TypeParamId`, `FieldId`, `PropertyId`, `VariantId`, `Type`, `BExpr`, `BStmt`, `BPattern`; `-1` is none; `Type`/bound handles are `(kind << 32) | index`.

**`Ctx`** (T1, extended by T3/T4): `List<SourceFile> files`, `List<Mod> modules`, `List<Diag> diagnostics`, `List<DiagBag> bags`, `i64 sink` (-1 main), `bool quiet`, options (`library`, `capabilities`, `packageName`); T3 adds every symbol/type arena, intern `Map`s, and the shared caches `instances`, `instantiationWork`, `pendingKeys`, `pendingConstraints`, `states`, `checks`, `distinct`, `positional`, `aliases`, `invokeSymbols`, `validationErrors`; T4 adds the bound arenas. Frozen after T4.

**`Binder`** (T1): `FileId file`, `FuncId fn`, `StructId owner`, `i64 nextLocalId`, `List<Map<string, LocalId>> scopes`, `List<JumpTarget> targets`, lambda records/counts, `Map<i64, i64> pending`/`constraints`/`peers` keyed by the `LocalDecl` span start, `List<(i64, LocalId)> nonNull`, state facts, `Map<LocalId, VariantId> errorNarrowed`, held-bag index.

**Diagnostics** (`diag.byn`, T2): `struct Related { TextSpan span; string label }`; `Diag { string code; FileId file; TextSpan span; string message; string label; string help; bool isWarning; bool hasFix; TextSpan fixSpan; string fixReplacement; List<Related> related }`; `void report(ref Ctx c, SemaCode code, FileId f, TextSpan span, by (string a0, string a1, string a2, string a3, string a4) args, string label = "", string help = "", bool hasFix = false, TextSpan fixSpan = TextSpan { start: 0, length: 0 }, string fixReplacement = "")`; `string renderAll(ref Ctx c, in List<Diag> diags, bool includePath, i64 errorLimit)`; `void renderFooter(ref Ctx c, to: ref StrBuf sb)`; `SemaCode` with `codeText`, `messageFormat`, `isWarning`.

**Cross-file entry points** (stand-in signatures; all other helpers stay private to their file and keep the C# signature with these substitutions: `Compilation c` → `ref Ctx c`, `Binder b` → `ref Binder b`, `XSymbol` → `XId`, `TypeSymbol` → `Type`, `SourceText site` → `FileId site`, `Name` → `(FileId, TextSpan)`, `IEnumerable<X>` → `List<X>`):

- T6: `ConversionKind? implicit(Type from, Type to, bool argument)`, `ConversionKind? explicit(Type, Type)`, `bool isWidening(Type, Type)`, `Type common(Type, Type)`, `bool isUnique(Type)`, `Type uniquePart(Type)`, `string? whyUnique(Type)`, `Constant? foldUnary(UnaryOp, Constant, Type)`, `foldBinary(BinaryOp, Constant, Constant, Type)`, `foldCast(Constant, Type, Type)`, `Constant? evaluate(ref Ctx, BExpr, in Map<LocalId, Constant>, in Map<FieldId, Constant>)`.
- T7: `void checkType(ref Ctx c, FileId f, TextSpan name)`, `checkValue`, `checkInterface`, `checkMembers(ref Ctx c, StructId s)`.
- T8: `Type resolve(ref Ctx c, FileId f, StructId container, i64 typeMap, in TypeSyntax t, bool allowVoid)`, `Type resolveBound(…)`, `Type expandAlias(ref Ctx c, i64 alias)`, `List<string> effectsOf(ref Ctx c, in UsesClause uses)`.
- T9: `Type builtinType(BuiltinKind k)`, `FuncId findIo(string name)`, `FuncId? findString(string name)`, `FuncId? findUtf8(string name)`, `FuncId intParse(PrimitiveKind t)`, `bool hasMember(Type, string)`, `(BuiltinMember, Type)? memberProperty(Type, string)`, `BuiltinMethod? memberMethod(Type, string)`.
- T11/T12/T13: `void declare(ref Ctx c)`, `void declareFile(ref Ctx c, FileId f)`, `void declareSignature(ref Ctx c, FuncId fn, FileId f, i64 declIndex, StructId owner)`, `void declareMembers(ref Ctx c, StructId s)`, `void declareVariants(ref Ctx c, ErrorSetId set)`, `void declareInterfaceMembers(ref Ctx c, InterfaceId i)`, `void checkOverloads(ref Ctx c, ModId m)`, `i64 accessOf(u16 modifiers)`, `FuncId? instantiateFunction(ref Ctx c, FuncId template, in List<Type> args, FileId site, TextSpan span)`, `Type instantiateType(…)`, `FuncId invokeSymbol(ref Ctx c, Type functionType)`, `void bindAll(ref Ctx c)`, `void bindFunction(ref Ctx c, FuncId fn)`, `void inferErrorSets(ref Ctx c)`, `void checkConstraints(ref Ctx c, TypeParamId owner, FileId f, i64 declIndex, in List<Type> args)`, `void checkMutation(ref Ctx c)`, `void checkRequires(ref Ctx c)`, `BExpr? fieldDefault(ref Ctx c, StructId s, FieldId f, FileId site, TextSpan span)`.
- T15/T16: `BExpr bindExpr(ref Binder b, in Expr e, Type expected)`, `BExpr bindExprOrType(…)`, `BExpr bindConverted(ref Binder b, in Expr e, Type target, bool argument)`, `BExpr convert(ref Binder b, BExpr value, Type target, TextSpan span, bool argument, bool constrains)`, `BStmt bindStatement(ref Binder b, in Stmt s, i64 labelTarget)`, `BStmt bindBlock(ref Binder b, in BlockStmt s)`, `Type resolveType(ref Binder b, in TypeSyntax t)`, `LocalId declareLocal(ref Binder b, FileId f, TextSpan name, Type t, LocalKind kind)`, `void addLocal(ref Binder b, FileId f, TextSpan name, LocalId l)`, `void pushScope(ref Binder b)`, `void popScope(ref Binder b)`, `void bindFunctionBody(ref Binder b, in FunctionDecl decl)`, `void bindChecks(ref Binder b, in StructChecks checks)`, `void bindMain(ref Binder b, in List<GlobalStatement> statements)`.
- T17–T19: `BExpr bindName(ref Binder b, in NameExpr n)`, `bindMember(ref Binder b, in MemberExpr m)`, `bindTargetMember(ref Binder b, in TargetMemberExpr t, Type expected)`, `bindIndex(ref Binder b, in IndexExpr i)`, `bindUnary`, `bindBinary`, `bindLambda(ref Binder b, in Expr syntax, in List<Name> parameters, in Expr body, in BlockStmt block, Type expected)`, `bindStructLiteral`, `bindWith`, `bindCollection`, `bindTuple`, `bindInterpolated`, `bindFormatSpec`, `string? memberSuggestion(Type, string)`, `string binaryText(BinaryOp)`.
- T20/T21: `BExpr bindCall(ref Binder b, in CallExpr c, Type expected)`, `List<BExpr> bindArguments(ref Binder b, in CallExpr c, in List<LocalId> ps, FuncId fn, bool enforceNames, string displayName)`, `BExpr bindFunctionCall`, `BExpr bindConstruction`, `BExpr bindBuiltinMethodCall`, `BExpr bindMove(ref Binder b, in Expr syntax, BExpr operand)`, `BStmt bindAssign(ref Binder b, in AssignStmt a)`, `BStmt bindIncDec`, `BExpr bindTry`, `bindCatch`, `bindSwitchExpr`, `BPattern bindPattern(ref Binder b, in Pattern p, Type input)`, `void checkExhaustive(ref Binder b, TextSpan span, Type type, in List<BSwitchArm> arms, bool statement)`, `bool checkWritable(ref Binder b, BExpr target, TextSpan span, string why)`, `BExpr built(ref Binder b, BExpr value, in Expr syntax)`.
- T22: `BStmt bindBecome(ref Binder b, in BecomeStmt s)`, `Type receiverStates(ref Ctx c, FuncId fn)`, `Type knownState(ref Binder b, LocalId l)`, `void learnState(ref Binder b, LocalId l, Type t)`, `void forgetState`, `void learnArmState(ref Binder b, BExpr subject, BPattern pattern)`, `BExpr bindPayloadField`, `BPattern bindErrorCasePattern(ref Binder b, in Pattern p, Type input, BExpr bound, in List<Pattern> elements, TextSpan? binding)`, `BExpr errorCase(ref Binder b, in Pattern p, ErrorSetId set, TextSpan name)`, `BExpr bindErrorNew(ref Binder b, in CallExpr c, Type expected)`, `BExpr bindPayload(ref Binder b, in CallExpr c, VariantId variant)`.
- T23–T25: `void analyzeFlow(ref Ctx c, FuncId fn)`, `void analyzeEffects(ref Ctx c)`, `List<ErrorSetId> propagated(ref Ctx c, BStmt body)`, `void analyzeOwnership(ref Ctx c, FuncId fn)`, `void analyzeNeverReassigned`/`analyzeSpanParameters`/`analyzePanicHandlers`, `void analyzeRegions(ref Ctx c, FuncId fn)`.
- T26: `string typedDump(ref Ctx c)`; CLI `bayan-check [--types] [--all] <path>`, `--many <root>` (isolated case dirs, `== <name>` headers, fresh arena each), `--time <path>`, `--spans`, `--codes`.

**Harness** (`SelfhostSemanticsTests.cs`, T26): `Built` = `Lazy<string>` over all `selfhost/semantics/*.byn`; each case copied to `tmp/semantics-cases/<slug>/` and reference produced by `CliApp.Run(["check"|"types", copiedPath], …)` in-process, written under `tmp/semantics-ref/` and compared with `Golden.Compare` (first differing line); exit codes asserted; `--all` for the full list and the default cap checked on `map-copy`; `--types` compared byte for byte. `--many` drives the generated corpora. `OneMegabyte` timing median of 5, `BAYAN_SKIP_TIMING=1`.

## Risks & mitigations

- **No 128-bit integer (T5, Q540–Q542).** C# parses literals with `BigInteger` and folds in `Int128`; Bayan tops out at `u64`. `bigint.byn` implements a signed 128-bit value (two `u64` limbs) with parse, compare, add, sub, mul, div, rem, shifts, bit ops, wrap/saturate to a width and decimal text, used by `parseInteger`, `checkedIntegerLiteral` and `fold.byn`. A literal wider than 128 bits is poisoned and reported out of range from its normalized digit text (C# prints the same text). If a needed operation cannot be expressed, the task records the diagnostic and asks.
- **No stored closures (T11/T13, Q570–Q572).** `List<Action>` becomes `List<Work>` with a dispatch switch; `Instantiating`/`Quietly`/`ReportInto` become sink push/pop at their 13 sites. Work records hold handles, file ids and spans, never syntax. If a deferred check needs syntax, it carries `(FileId, declIndex, memberIndex)` and re-reads the retained tree.
- **No immutable collections (T25, Q640–Q642).** `RegionAnalysis`'s `ImmutableHashSet`/`ImmutableDictionary` borrows become copied `List<…>`/`Map` snapshots (layouts deduped on union). The analysis is flow-insensitive already, so copies preserve the C# result; the two-pass structure and last-use rules are ported unchanged.
- **ASCII-only casing (T7, Q550–Q552).** `Naming` uses Unicode `char.IsUpper/IsLower` and invariant lower/upper; Bayan v0.1 `lowercased`/`uppercased` are ASCII. The port uses ASCII predicates and `lowercased`/`uppercased`; every current diag/typed case is ASCII, so goldens pass. A non-ASCII identifier may get a different casing fix; the Question records it.
- **Parser visibility cascade (T1).** `public` turns on effect checking, so public entry points need `uses alloc`; if a construct cannot be made public (a union payload field, a `ref this` method), T1 stops and records the exact diagnostic. A compiler bug blocking it is fixed here with a test (R8).
- **Symbols must not store syntax (T3).** `Owned` payloads cannot be copied, and a struct cannot hold a borrow. Symbols copy scalars and keep `(FileId, declIndex, memberIndex)`; the T3 header lists the `\.Decl` audit. Anything that needs a syntax subtree re-reads it at use. If a site cannot be expressed, T3 records it and T28 decides.
- **Map is unique and ordered (T3/T11).** Tables are `Map<string, Type>`/`Map<string, List<FuncId>>` (008 preserves insertion order, which matches `Suggest`'s first-wins tie). Copies use `.copy(to: arena)`; if a nested `List` value cannot be mutated through the map, functions map to an index into a `List<List<FuncId>>`.
- **Path and ordering parity (T11/T26, Q564).** The checker takes absolute paths and constructs render and display names from the argument, never from `Path.GetFullPath`; ordinal sorts use `List<string>.sort()` (UTF-8 order). Non-ASCII file names and relative arguments are Questions.
- **Diagnostic ties.** Output is sorted by path then span; equal keys keep report order. Every pass keeps C# call order, and `BoundTree.Children`/descendant order is ported exactly. Any `Dictionary` whose iteration leaks into output becomes an insertion-ordered `Map`.
- **Performance (T26/T27).** The 1 MB bound is 10x; expected costs are `Map` lookup, handle indirection, retained trees and the two-pass analyses. `--time` prints parse/declare/bind/analysis nanoseconds so a miss is attributable. Fixes stay inside `selfhost/semantics/`.
- **C# bugs matched (any task).** Where the reference is wrong, the port matches it and the case goes to `work/todo.md`; T33 consolidates.
- **Wave writers cannot see parity until merge.** Each wave task's done-when is a method-for-method port that builds and answers its own tests; T28 owns the last mile and may edit any file after T4.

## Coverage check

| Requirement / law | Tasks |
|---|---|
| R1 program, cap, footer, exit code | T2 (renderer), T10 (manifest), T11 (loading), T26 (CLI/harness), T28 (parity) |
| R2 typed dump | T3 (`typeDisplay`), T17/T20 (lambda names/captures), T26 (`typed.byn`, bytes), T28 |
| R3 reuse, L001/L002/L017/L018 | T1 (public parser, suites rerun) |
| R4 analyses | T6–T9, T11–T25 |
| R5 golden coverage, first differing line | T26, T28 |
| R6 generator, fixed seeds, sensitivity | T27 |
| R7 speed bound | T26 (`--time`), T27 (`OneMegabyte`), T28 (measured) |
| R8 port friction | every task's Question range; T33 lists them |
| R9 docs | T33 |
| L019 | T29 prove, T30 enact |
| L020 | T26 (`--spans`), T31 prove, T32 enact |
