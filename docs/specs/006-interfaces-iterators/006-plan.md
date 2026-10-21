# 006 — Plan
Spec: ./006-spec.md

## Approach

**Interfaces as a new symbol kind, conformance as a checked table, dispatch by rebinding.** `InterfaceSymbol` (not a StructSymbol subclass) holds abstract methods, getters, default methods (a `FunctionSymbol` with a body whose `this` type is a synthetic `TypeParameterType("Self")` constrained to the interface) and associated types. Interfaces are generic the same way structs are (TypeParameters/Template/TypeMap, `Compilation.InstantiateType`). A struct/union/enum lists bases after `:`; a conformance pass after `DeclareMembers` builds a `Conformance` (type, interface instance, member→witness map, associated-type map), reporting missing/mismatched members at the type. A default method is checked **once** on its template (Self is a type parameter, so it has no fields, satisfying R3 for free) and **monomorphized per conformer** through the existing `InstantiateFunction` worklist with `Self→Conformer`. Rationale over "emit once with a void* receiver": per-conformer instances reuse the Q46 machinery unchanged and let the body call the conformer's own methods statically; a shared body would need a second calling convention in the emitter.

**Static dispatch is the instance rebind.** Inside a generic template, `t.area()` with `where T : IShape` binds to the interface member (`BoundCall` whose Function is the interface's abstract symbol). Instances are rebound from syntax with `TypeMap`, so in the instance `t` is `Circle` and `BindValueMember` finds `Circle.area` (or the conformer's default-method instance) directly. No new bound node for constrained calls; no runtime dispatch (R6).

**Constraints are checked at definition, satisfaction at the use site.** `TypeParameterType` gains `Constraints` (resolved interface types). `BindValueMember`'s TypeParameterType case consults the constraints' members with `Self→T` and interface type args substituted; anything else is a diagnostic at the definition (R4). `InstantiateFunction`/`InstantiateType` check each argument against the parameter's constraints **before** enqueuing work and report at the use-site span passed in (R5); this must not go through the `Instantiating` path, which turns instance errors into internal errors (Compilation.cs:798).

**`dyn ref I` is a borrow type with a fat pointer.** New `DynRefType(interface, readOnly)` with `IsBorrow => true`, so BYN3001/3002/3003 and the field rule in `CheckStructShape` (Compilation.cs:687) apply unchanged (R7). C layout: `struct byn_dyn_<I> { void* data; const byn_vt_<I>* vt; }`; `struct byn_vt_<I>` has one function-pointer slot per method and getter, including defaults; one `static const` vtable per (conformer, interface) emitted on first conversion, filled with per-slot thunks that cast `void*` to the conformer pointer and call the mangled method (thunks avoid calling through an incompatible pointer type, which warns under `-Wall` and matters for L012). Conversion is a `BoundConversion` with a new `ConversionKind.ToDynRef`. A call through `dyn ref` is a `BoundCall` whose receiver type is `DynRefType`; the emitter writes `r.vt->m(r.data, …)`. No runtime change.

**`for … in` over user types reuses `BoundFor`.** `BindFor` gains a case for a type conforming to the built-in `IEnumerator` or `IEnumerable` (synthesized `InterfaceSymbol`s in Builtins, recognized by identity), attaching a nullable `UserIteration(enumeratorFn?, moveNext, current, itemType)` to `BoundFor` so the six existing consumers keep pattern-matching `BoundFor`. The lowerer spills the enumerator into a temp (calling `enumerator()` first for `IEnumerable`; for `IEnumerator` the iterable must be a place and is borrowed by address, never copied, R11), then `loop { if (!moveNext(&en)) goto brk; x = current(&en); body; cont: }` using the same `EnterLoop`/`LLoop`/`LLabel` shape as the span case at Lowerer.cs:503. The loop variable is a `let` with `BorrowsSubject = true` (cannot be moved out), and RegionAnalysis adds a loan on the iterable's root for the loop extent, exactly as for List today. `current` returns `Item` by value in this subset (see Q90): Bayan has no ref-returning getters, and adding ref locals is out of scope.

## Tech / dependencies

- No new packages, no lexer change (`where`, `dyn` stay identifiers; L001 untouched). `interface`, `extension`, `type`, `static`, `readonly` are already `TokenKind`s (Keywords.cs).
- Runtime (`bayan-rt.c/.h`) unchanged.
- L012 compiles generated programs through the existing C toolchain invocation used by run goldens; verify it exposes a way to pass `-Wall -Werror` before relying on it (I did not read the runner).

## Changes by area

- `src/Bayan.Syntax/Ast/Nodes.cs`: `InterfaceDecl(Span, Modifiers, Name, Members){TypeParams, Where}`, `InterfaceMethodDecl` (abstract when `Body`/`ExpressionBody` null), `AssociatedTypeDecl(Span, Name)`, `AssociatedTypeBinding(Span, Name, Type)` (struct member), `WhereClause(Span, Param, Bounds)`, `DynType(Span, IsRef, IsReadOnly, Inner)`, `ExtensionDecl(Span, Name, Bases)`; `Bases: NamedType[]` and `Where: WhereClause[]` on StructDecl/UnionDecl/EnumDecl; `Where` on FunctionDecl. R1 R2 R4 R7 R10.
- `src/Bayan.Syntax/Parsing/Parser.cs`: `ParseDeclaration` 280 adds InterfaceKeyword → `ParseInterface`, ExtensionKeyword → `ParseExtension`; `ParseStruct` 406 / `ParseEnum` 553 / `ParseUnion` 575 parse `: I, J` then `where` clauses before `{`; `ParseFunction` 464 parses `where` after `fails` (Q89); `ParseType` 707 and `ScanType` 637 accept `dyn [ref] [readonly] Name` (ScanType matters for `IsTypeThenName` at ParseFor 958); struct member parse accepts `type Item = T;` (reuse the `type` alias path at 313 inside members); interface members accept `static` and a contextual `abstract` so semantics can report R10 (confirm `abstract` is not in Keywords.cs; I did not see it). R1 R2 R4 R7 R9 R10.
- `src/Bayan.Syntax/Ast/AstDump.cs`: dump the new nodes; `parse/` goldens. R1.
- `src/Bayan.Syntax/Diagnostics/DiagnosticCodes.cs`: codes below; update BYN2221 help. R12.
- `src/Bayan.Semantics/Symbols/Types.cs`: `TypeParameterType.Constraints`, `DynRefType`, `TypeParameterType.Occurs` covers DynRef and interface instances. R4 R7.
- `src/Bayan.Semantics/Symbols/Symbols.cs`: `InterfaceSymbol`, `Conformance`, `Bases`/`Conformances` on StructSymbol/UnionSymbol/EnumSymbol, `AssociatedTypes` map, `FunctionKind.InterfaceMember`?—no: keep `Method`/`Getter`, add `FunctionSymbol.IsAbstract` and `Interface` owner. R1 R2.
- `src/Bayan.Semantics/Compilation.cs`: `DeclareFile` 231 declares interfaces (Naming check for the `I` prefix, R1) and reports ExtensionDecl (R10); `DeclareTypeParameters` 355 resolves `where` into Constraints after all types are declared (two-phase: names first, bounds after `AddType`); `DeclareMembers` 551 handles bases, associated-type bindings, interface members (static/abstract → R10 code); new `CheckConformance` called in `BindAll` 752 after shapes, before bodies; `InstantiateFunction` 859 / `InstantiateType` 885 add the constraint check (R5) and accept InterfaceSymbol templates; `TemplateOf` 929. R2 R3 R5.
- `src/Bayan.Semantics/Naming.cs`: `CheckInterface` (I + uppercase, rename fix via `ToPascal`). R1.
- `src/Bayan.Semantics/TypeResolver.cs`: `ResolveCore` 23 handles `DynType` (R10 code if `!IsRef`, object-safety check R8, `DynRefType`), `ResolveNamed` 55 resolves `T.Item` via a two-segment path when the first segment is a type parameter (R4), `Construct` 211 accepts InterfaceSymbol, `CheckVisible` 178 adds InterfaceSymbol. R4 R7 R8.
- `src/Bayan.Semantics/Binding/BinderExpressions.cs`: `BindValueMember` 638: StructSymbol/UnionSymbol/EnumSymbol fall back to conformance default-method instances; TypeParameterType case 684 looks up constraint members before BYN2744; new `DynRefType` case binds interface members (readonly dyn refuses mutating members). R3 R4 R6 R7.
- `src/Bayan.Semantics/Binding/BinderCalls.cs`: `BindFunctionCall` 70 accepts abstract callees on type-parameter/dyn receivers; `BindArgument` 316 / assignment conversion insert `ToDynRef` when the target type is `DynRefType` and the source conforms (by value place or existing borrow; never a move, R11). R7 R11.
- `src/Bayan.Semantics/Binding/BoundNodes.cs`: `ConversionKind.ToDynRef`, `BoundFor.UserIteration`. R7 R9.
- `src/Bayan.Semantics/Binding/Binder.cs`: `BindFor` 536 new case; `NotIterable` 567 help mentions `IEnumerator`/`IEnumerable`. R9.
- `src/Bayan.Semantics/Binding/RegionAnalysis.cs:128` loan on the iterable root for user iteration; `OwnershipAnalysis.cs:171`, `FlowAnalysis.cs:165`, `SpanParameterLint.cs:61` unchanged unless the new fields need visiting (they do not: enumerator calls are synthesized in lowering). R9.
- `src/Bayan.Semantics/Builtins` (file holding `compilation.Builtins.IoError`): synthesize `IEnumerator`, `IEnumerable`. R9.
- `src/Bayan.Lowering/Lowerer.cs:503` `For`: user-iteration branch; `LoweredNodes.cs` needs no new node (calls are `BoundCall`s on temps). R9.
- `src/Bayan.Backend.C/CEmitter.cs`: `EmitTypes` 261 defines `byn_dyn_<I>`/`byn_vt_<I>`; vtables and thunks via `Helper` 335; `Expr` 1069 handles `ToDynRef`; `Call` 1154 handles dyn receivers; `_typeOrder` registration for `DynRefType` in the `CType` switch near 220. R6 R7.
- `tests/cases/{parse,diag,typed,run}` goldens; `docs/diagnostics/BYN28xx.md`; `docs/laws/012-*.md`; `tests/Bayan.Tests/InterfaceGenerator.cs`, `L012InterfaceDispatchTests.cs`; handoff Questions Q89+; `docs/bayan-spec.md` §3.15 note on `current` by value. R12 R13 L012.

## Data / interfaces

- AST: as listed above. Bound: `BoundFor.UserIteration(FunctionSymbol? Enumerator, FunctionSymbol MoveNext, FunctionSymbol Current)`, `ConversionKind.ToDynRef`.
- Symbols: `InterfaceSymbol { Name, Module, File, Access, TypeParameters, Template, TypeArguments, TypeMap, Methods, Properties, AssociatedTypes, Self: TypeParameterType }`; `Conformance { Type, Interface, Witnesses: Dictionary<FunctionSymbol, FunctionSymbol>, AssociatedTypes: Dictionary<string, TypeSymbol> }`; `TypeParameterType.Constraints: IReadOnlyList<InterfaceSymbol>`; `DynRefType(InterfaceSymbol, bool ReadOnly) : TypeSymbol { IsBorrow => true }`.
- Diagnostics (block 2800–2814 is free; 2745–2759 left for generics follow-ups; verify the 005 branch has not taken 28xx before merging):

| Code | Name | R |
|---|---|---|
| 2800 | InterfaceName: `{0}` must start with `I` and an uppercase letter (rename fix) | R1 |
| 2801 | MissingInterfaceMember: `{0}` does not provide `{1}` required by `{2}` (insert-signature fix) | R2 |
| 2802 | InterfaceMemberMismatch: `{0}.{1}` is `{2}` but `{3}` requires `{4}` | R2 |
| 2803 | MissingAssociatedType: `{0}` does not bind `type {1}` of `{2}` | R2 |
| 2804 | NotAnInterface: `{0}` is not an interface (bases and `where` bounds) | R2 R10 |
| 2805 | DuplicateConformance: `{0}` lists `{1}` twice | R2 |
| 2806 | ConstraintMember: `{0}` has no member `{1}` in its constraints ({2}) | R4 |
| 2807 | ConstraintNotSatisfied: `{0}` does not conform to `{1}`: missing `{2}` | R5 |
| 2808 | NotDynSafe: `{0}` cannot be used with `dyn`: `{1}` {2} (reason text) | R8 |
| 2809 | DynByValue: `dyn {0}` is not supported yet; use `dyn ref {0}` | R10 |
| 2810 | StaticAbstract: `static` interface members are not supported yet | R10 |
| 2811 | ExtensionConformance: `extension {0} : {1}` is not supported yet; list `{1}` on the type | R10 |
| 2812 | WhereUnknownParameter: `where` names `{0}`, which is not a type parameter | R4 |
| 2813 | DynReadonlyMutation: `{0}` is mutating; the receiver is `dyn ref readonly` | R7 |
| 2221 | existing NotIterable, help extended | R9 |

- C layout: `typedef struct byn_dyn_<I> { void* data; const byn_vt_<I>* vt; }`; `struct byn_vt_<I> { <ret> (*<m>)(void* self, <params>); … }`; thunks `static <ret> bynh_vt_<T>_<I>_<m>(void* self, …)`; vtable `static const byn_vt_<I> bynh_vtab_<T>_<I> = { … }`; conversion `(byn_dyn_<I>){ &<place>, &bynh_vtab_<T>_<I> }`. Names use the existing `Key`/`TypeArgs` mangling (CEmitter.cs:175-221).

## Risks & mitigations

- **Instance errors become internal errors** (Compilation.cs:798): any constraint failure discovered inside an instance body throws. Mitigation: check conformance at `InstantiateFunction`/`InstantiateType` before enqueuing; a golden where the failing call is inside another generic.
- **Receiver convention for thunks**: I did not confirm whether methods take `T*` or `T` by value in C. Read the `HasThis` emission before writing thunks; thunks adapt either way.
- **`current` by value** breaks R9's "borrow" for `unique` items. Mitigation: conformance rejects a `unique` `Item` with BYN2022 (Q90) and the law doc records it.
- **ScanType speculation**: `dyn ref` in tuple/for scans must be mirrored in `ScanType` 637 or `for (let x in …)` with a `dyn` type annotation misparses; add a parse golden.
- **Two-phase type parameters**: `where T : IOrder<T>` mentions T inside its own bound; resolve bounds after names exist, and guard recursion in `InstantiateType`.
- **Generic interface methods** (R8 acceptance case) must be declarable; today `DeclareMembers` 597 rejects generic methods. Allow them on interfaces only; any call still reports BYN2022 (Q95).
- **BYN2221 help change** touches existing diag goldens; regenerate with `BAYAN_UPDATE=1`.
- **Mutation through `dyn ref`**: `MutationChecks` (Compilation.cs:1070) keys on a concrete callee; a dyn call's callee is the abstract symbol, so mark abstract members `IsMutating` from their declared receiver mutability and let Q78's rule apply to the dyn local's root.

## Coverage check

R1 Parser/AST/Naming/DeclareFile, 2800. R2 bases, CheckConformance, 2801–2805. R3 Self template bind, per-conformer instances. R4 Constraints, BindValueMember 684, TypeResolver `T.Item`, 2806/2812. R5 InstantiateFunction/Type check, 2807. R6 instance rebind, run golden. R7 DynRefType, ToDynRef, CEmitter vtables, 2813. R8 TypeResolver dyn check, 2808. R9 BindFor, RegionAnalysis loan, Lowerer, Builtins. R10 2809/2810/2811/2804. R11 conversion never moves, enumerator by address, run golden with a `unique` conformer. R12 table + pages + goldens. R13 `run/self-hosting-shape.byn`. L012 generator + test + law doc.

## Decisions for handoff Questions

- Q89 `where` placement: functions `… (params) [fails …] where T : I, J { }`; types `struct S<T> : I where T : J {`. Several `where` clauses allowed; one per parameter.
- Q90 `IEnumerator.current` returns `Item` by value in this subset; the loop variable is a `let` copy that cannot be moved out; `unique` items reported with BYN2022 until ref-returning getters exist.
- Q91 Default methods are monomorphized per conformer; body checked once on the interface with `Self` as a constrained type parameter.
- Q92 Vtables have a slot for every method and getter, including defaults; one `static const` table per (conformer, interface) in the emitted translation unit; no sharing.
- Q93 Dyn-safe means: no generic methods, no associated types, no `Self` anywhere but the receiver (return position included, since the caller cannot size it).
- Q94 Enum conformance: if `EnumSymbol` has no method list, enums can conform only through default methods; otherwise same as structs.
- Q95 Generic methods are declarable in interfaces solely so BYN2808 can name them; calling one reports BYN2022.
- Q96 Implicit `ToDynRef` at arguments, `let`/`var` with a declared type, returns and field initializers; no `dyn ref J` → `dyn ref I` conversion.
- Q97 `IEnumerator`/`IEnumerable` are synthesized built-in interfaces, not prelude source; a user type may not redeclare them.
- Q98 Specialization: conformer's own member beats the default; no other ranking.

## Build order

1. **Codes and pages**: DiagnosticCodes.cs, docs/diagnostics/BYN2800–2813.md. Done when `DiagnosticCodesTests` passes except the missing-golden rule. Parallel with 2, 3.
2. **AST + parser + dump**: Nodes.cs, Parser.cs, AstDump.cs, parse goldens for interface, bases, where, dyn, extension. Done when parse goldens match and lex goldens are unchanged.
3. **Symbols**: Types.cs, Symbols.cs (InterfaceSymbol, Conformance, Constraints, DynRefType). Done when it builds.
4. **Declare + Naming + Builtins**: Compilation.cs 231–684, Naming.cs, Builtins. Depends 2, 3. Done when a typed golden dumps an interface and a conforming struct.
5. **TypeResolver**: dyn, object safety, `T.Item`, interface construction. Depends 3, 4. Parallel with 6.
6. **CheckConformance + default instances**: new Conformance.cs, BindAll hook. Depends 4. Done when 2801–2805 diag goldens pass and a default-method run golden binds.
7. **Binder constraints and dyn**: BinderExpressions.cs, BinderCalls.cs, BoundNodes.cs, InstantiateFunction/Type check. Depends 5, 6. Parallel with 8.
8. **BindFor + RegionAnalysis**: Binder.cs, RegionAnalysis.cs. Depends 6.
9. **Lowerer user iteration**. Depends 8. Parallel with 10.
10. **CEmitter dyn ref**: types, thunks, vtables, calls, conversion. Depends 7.
11. **Goldens**: run (static dispatch, default, dyn, iteration, nested, break+defer, unique, R13 shape), remaining diag. Depends 9, 10.
12. **L012**: InterfaceGenerator.cs, L012InterfaceDispatchTests.cs, docs/laws/012, handoff Q89–Q98, spec §3.15 note. Depends 11.
