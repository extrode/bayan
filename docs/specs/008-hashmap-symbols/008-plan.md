# 008 — Plan
Spec: ./008-spec.md
Written 2026-10-22 against dev at `b538763` (round 2 applied). Re-located 2026-10-25: depends on feat/007-lambdas at cc96838 (dev, round 3, spec 006 and 007 merged); merges into dev after 007. File and method names below were checked on that commit; line numbers are dropped, so locate by method name. Round 3 is complete there (`docs/bayan-design-round-3.md`, plan `docs/plans/2026-10-22-002-design-round-3.md`, revisions 0002–0011) and wins on conflict. `rune` is still `PrimitiveKind.Char` / `PrimitiveType.Char` in the C#, so the key checks below meet it under that name.

## Approach

**Extend `Map`; `HashMap` is a fix-it, not a type.** `MapType` (`Types.cs`) is already a seeded open-addressed hash index over dense, insertion-ordered entries (`byn_map_hdr` in `bayan-rt.h`; `byn_map_find`/`insert`/`remove` in `bayan-rt.c`). Lookup is O(1) today; what an unordered `HashMap` would save is the dense array and tombstones, and what it would cost is iteration order that depends on the seed, which `byn_hash_seed_init` (emitted in `main` by `CEmitter` when `_hashes` is set) takes from the run unless `--test` is passed. Stage 2 and stage 3 of the bootstrap must emit byte-identical C, and the binder port iterates its tables, so the self-hosted compiler needs ordered maps. `TypeResolver` resolves the name `HashMap` to `MapType` after reporting BYN2830 with a fix, so programs written to spec §3.5 compile with one diagnostic and applying the fix turns them into `Map` (Q371).

**`Symbol` is a `BuiltinType` holding a `uint32_t` ID; literals are interned by the compiler, everything else by the runtime.** `BuiltinKind.Symbol` joins `Arena`, `StrBuf` and the rest (`Types.cs`). It is not `unique` (`TypeFacts.IsUnique` stays as it is), not a borrow, and defaultable (`BinderCalls.IsDefaultable`). The binder turns a string literal into `BoundSymbolLiteral(Syntax, Text)` where the expected type is `Symbol`. Conversions are type-based (`Conversions.Implicit`), so the conversion happens where literals are bound against an expected type (`BindLiteral` in `BinderExpressions.cs`), and `Convert` reports BYN2832 for any other `string` → `Symbol`. At emission, `CEmitter` collects every `BoundSymbolLiteral` text in the program, sorts the texts by UTF-8 bytes with `""` first, and emits them as one `static const byn_str byn_symtab[]` in `.rodata`; a literal becomes the constant `(byn_sym){N}`. `main` calls `byn_sym_init(byn_symtab, N)` before user code, next to `byn_hash_seed_init`. `Symbol.intern` and `.text` become calls to `byn_sym_intern` and `byn_sym_text`. Equality is `a.id == b.id`. Ordering compares `byn_sym_text` with the same byte comparison `string` uses (code-point order, revision 0003). Literal IDs depend only on the source, and no ID is observable (Q372), so the seed and intern order cannot reach the output (L015).

**The runtime table owns its text for the whole run.** `byn_sym_intern` hashes the text with `byn_hash_bytes`, probes an open-addressed index of IDs, and on a miss copies the text into a growing chunk and appends `(ptr, len)` to the ID array. Everything comes from the general allocator, as `byn_str_copy` does, never from `context.allocator`, and nothing is freed (Q373). That is why `sym.text` borrows nothing: `RegionAnalysis` treats `Symbol.intern` and `.text` like the builtins whose results borrow nothing, and `Symbol.intern` (`BuiltinFunction.SymbolIntern`) is listed with the allocations a panic handler may not make (`PanicHandlerCheck.cs`, the `BoundCall { Function.Builtin: … }` case that reports "allocation").

**Composite keys hash field by field.** `MapType.IsValidKey` becomes recursive: tuples and structs qualify when every part does and the struct is not `@noEquality`. A new `MapType.InvalidKeyPart(t)` returns the first offending part's path and type for BYN2831. In C, `CEmitter.KeyEq` already reuses `Equal`, which handles `StructSymbol` and `TupleType`. `CEmitter.KeyHash` today runs `byn_hash_bytes(&k, sizeof k)` for every non-`string` key, which would read padding. It becomes a per-key-type helper `bynh_hash_<key>` built through `Helper`: primitives hash their bytes, `string` its `ptr, len`, `Symbol` its `id`, and composites fold the parts in declaration order through a new `byn_hash_mix(h, part)`. `byn_map_*` still receives a finished 64-bit hash, so the runtime map is unchanged (Q377).

**`for`, literals, slots and regions need no new rules.** Map iteration (`Lowerer.For`), map literals (`Lowerer.Collection`, `BinderExpressions.BindMapLiteral`) and slots (`BuiltinMember.MapSlot` in `Lowerer.cs`) are keyed on `MapType`, not on the key kind. `RegionAnalysis` already finds every `string` inside a value through its root, so a composite key holding a `string` borrows as a `string` key does today. A `run/` golden for a `(string name, i32 scope)` key pins this; if it fails, T6 extends the borrow walk to tuple and struct keys.

## Tech / dependencies

- No new packages. No lexer or parser change: `Symbol` and `HashMap` are type names resolved in `TypeResolver` (the name switch with `case "Map"` and `case "StrBuf"`, and the `Suggest` list), and `Symbol.intern` is a `FunctionSymbol` made with `Make` in `Builtins.cs` (`BuiltinFunction.SymbolIntern`, enum in `Symbols/Symbols.cs`) and reached through `BindMember`'s `BoundTypeRef { Referenced: BuiltinType { Kind: … } }` cases, the path `Hex.encode` and `Utf8.decode` use (5.4b renamed `string.from` to those codecs, revision 0003).
- Runtime (`src/runtime/bayan-rt.c/.h`): new `byn_sym`, `byn_sym_init`, `byn_sym_intern`, `byn_sym_text`, `byn_hash_mix`. They are emitted and linked only when the program uses `Symbol`, behind a flag like `CEmitter._hashes`. Seeded FNV-1a stays (`byn_hash_bytes`).
- Existing pieces reused, not rewritten: `byn_map_*` and `CEmitter.MapMethod`, `Equal`/`KeyEq` for composite equality, `BinderExpressions.IsEquatable`, the `string` ordering operators in `BindBinary` (emitted through `byn_str_cmp`), `CEmitter.Helper`, the `BoundTypeRef` statics, `BindMapLiteral`, the BYN2760 instantiation forwarding in `Compilation.cs` (the `InvalidMapKey` code check).
- Round-3 work 008 sits on, all landed:
  - 5.1a–5.1e: syntax for every golden (type-first declarations, J54; no semicolons; `rune`; `uses`; particles; `void main()`, exempt from BYN6002 per revision 0002).
  - 5.2 and revision 0009: `bayan format` (`src/Bayan.Cli/Format.cs`). It applies only the parse-level input forms in its `InputForms` set, so it does not apply BYN2830's fix (a `TypeResolver` diagnostic); T2 applies the fix with `Format.ApplyFixes` over the diagnostic's `Fix`.
  - 5.3d: the effect pass (`EffectAnalysis.cs`, J13). `BuiltinFunction.SymbolIntern` joins its `alloc` builtins (beside `Utf8Decode` and `HexEncode`) in T5.
  - 5.4a/5.4b: the stdlib and particle sweep, which shipped the `Map` members in `BuiltinMembers.cs` (`tryGet(at:)` replaces `getOr`, J24; Q381).
- Spec 006 (BYN2800–2816, L012, Q89–Q113) and spec 007 (BYN2820–2824 and BYN2029, L013–L014, Q345–Q362, formerly Q114–Q131) are merged. 008 uses neither: 006 built no equality or hash interface (only `IEnumerator` and `IIndexable` in `Builtins.cs`), so keys stay a closed set (Q376), and no 008 member takes a function. 008 edits files both touched (`Types.cs`, `TypeResolver.cs`, `BinderExpressions.cs`, `CEmitter.cs`, `DiagnosticCodes.cs`) and builds on them as merged. What 008's users would want from 007's deferred parts is under Risks.
- Code ranges checked on cc96838: `DiagnosticCodes.cs` has 006's BYN2800–2816 and 007's BYN2820–2824; no `BYN283x` appears outside 008's own docs, so BYN2830–2832 are free and 2833–2839 spare. Every other code 008 names keeps the meaning 008 uses (BYN2229 `InvalidConstArray`, BYN2612 `PanicHandlerRestricted`, BYN2760 `InvalidMapKey`, BYN3003 `BorrowReturned`, BYN3004 `ModifiedWhileBorrowed`, BYN6002 `EffectNotDeclared`). Laws L001–L012 are in `docs/laws/` and L013–L014 are 007's, so 008 uses L015–L016. 008's Questions are 371–381.
- L016's C check compiles `out.c` standalone with `-Wall -Werror` in the `BuildStandalone` pattern (`OwnershipCheckerTests.cs`, `L012InterfaceDispatchTests.cs`), as L012 does (its warning cleanup is done, Q102) and 007's L013 does.
- .NET 10 NativeAOT, no reflection.

## Changes by area

- `src/Bayan.Syntax/Diagnostics/DiagnosticCodes.cs`: BYN2830–2832 (below), each registered in the task that first raises it. Rewrite the BYN2760 help in `TypeResolver` and `BinderExpressions` to "use an integer type, `rune`, `bool`, `string`, `Symbol`, a plain enum, or a tuple or struct of those as the key". R5 R8 R11.
- `src/Bayan.Semantics/Symbols/Types.cs`: `BuiltinKind.Symbol` plus its `BuiltinType` instance and `DisplayName` `Symbol`. `MapType.IsValidKey` accepts `Symbol`, `TupleType` with all elements valid, and `StructSymbol` with all fields valid and no `@noEquality`. Add `MapType.InvalidKeyPart(TypeSymbol) -> (string Part, TypeSymbol Type)?`. A struct that contains itself cannot exist (`unique` and size rules), so the recursion terminates. R1 R5 R9.
- `src/Bayan.Semantics/TypeResolver.cs`: `case "Map"` reports BYN2831 when `InvalidKeyPart` names a part, else BYN2760. `case "HashMap"` reports BYN2830 with fix `Map<…>` and falls through to `Map`. `Symbol` and `HashMap` join the name switch and the `Suggest` list; a user type named `Symbol` behaves as a user type named `StrBuf` does today. R1 R5 R8.
- `src/Bayan.Semantics/Builtins.cs`: `Symbol Symbol.intern(string text)` as a `Make`d `FunctionSymbol` with `BuiltinFunction.SymbolIntern` (enum in `Symbols/Symbols.cs`), beside `HexEncode`. R3.
- `src/Bayan.Semantics/Binding/BoundNodes.cs`: `BoundSymbolLiteral(Syntax, string Text) : BoundExpr` with `Type = Symbol`. `BuiltinMember.SymbolText`. R2 R4.
- `src/Bayan.Semantics/Binding/BinderExpressions.cs`:
  - A string literal bound against expected type `Symbol` (or `Symbol?`) becomes `BoundSymbolLiteral` (`BindLiteral`); an interpolated one does not.
  - `Convert` reports BYN2832 for `string` → `Symbol`, with fix `Symbol.intern(<expr>)`.
  - In `==`, `!=` and the ordering operators (`BindBinary`), a string literal facing a `Symbol` operand binds as a symbol, and `Symbol` joins the ordering types.
  - `IsEquatable` accepts `Symbol`.
  - Map literal keys (`BindMapLiteral`) take the key type as expected type, so `"let": .letKeyword` converts.
  - BYN2831 is reported alongside BYN2760 (the `InvalidMapKey` report in `BindMapLiteral`).
  - Static `Symbol.intern` binds in `BindMember` on the `BoundTypeRef` path, beside `Hex.encode`.
  
  R1 R2 R3 R5.
- `src/Bayan.Semantics/Binding/BuiltinMembers.cs`: `text` on `Symbol` → `SymbolText`, returns `string`. No `Map` member changes (5.4 shipped them, Q381). R4 R7.
- `src/Bayan.Semantics/Binding/BinderCalls.cs`: `IsDefaultable` accepts `Symbol`, so `slot` and struct defaults work. R1 R7.
- `src/Bayan.Semantics/Compilation.cs`: the instantiation diagnostic forwarding (the `InvalidMapKey` code check) forwards BYN2831 as it does BYN2760. R9.
- `src/Bayan.Semantics/Binding/RegionAnalysis.cs`: `SymbolIntern` and `SymbolText` join the builtin results that borrow nothing. A `Symbol` value has no root. Composite keys with `string` parts are checked by the golden in T6 (Approach). R3 R4 R7.
- `src/Bayan.Semantics/Binding/PanicHandlerCheck.cs`: `BuiltinFunction.SymbolIntern` joins the restricted allocations (BYN2612). R3.
- `src/Bayan.Semantics/Binding/TypedDump.cs`: no change expected. The dump lists each function's locals with their types, so a `Symbol` local shows type `Symbol`; it prints no expressions and no IDs, and L009 stays byte-stable. R1 R12.
- `src/Bayan.Semantics/Binding/EffectAnalysis.cs`: `BuiltinFunction.SymbolIntern` is an `alloc` builtin. R3.
- `src/Bayan.Lowering/Lowerer.cs`: `BoundSymbolLiteral` passes through. Interpolation of a `Symbol` (`BoundInterpolated`) lowers to its text, the way it lowers `string`. Map iteration (`For`) and literals (`Collection`) are unchanged. R4 R7.
- `src/Bayan.Backend.C/CEmitter.cs`:
  - `byn_sym` in the type mapping (`CType`).
  - Literal table collection and the `byn_symtab` emission. `byn_sym_init` in `main` next to `byn_hash_seed_init`, guarded by a `_symbols` flag like `_hashes`.
  - `KeyHash` becomes `bynh_hash_<key>` built through `Helper`, field-wise for composites and `id` for `Symbol`. `KeyEq` is unchanged apart from a `Symbol` case in `Equal`.
  - The `Key` of `Symbol` is `sym`.
  - Ordering emits `byn_str_cmp` (`bayan-rt.h`, as the `string` ordering operators do) on the two `byn_sym_text` results.
  
  R2 R4 R6 R10.
- `src/runtime/bayan-rt.h`, `src/runtime/bayan-rt.c`: `typedef struct { uint32_t id; } byn_sym;`, `void byn_sym_init(const byn_str* lits, uint32_t n)`, `byn_sym byn_sym_intern(byn_str s)`, `byn_str byn_sym_text(byn_sym s)`, `uint64_t byn_hash_mix(uint64_t h, uint64_t part)`. The table is static in `bayan-rt.c`, allocated with the general allocator, and never freed. R3 R6 R10.
- `docs/diagnostics/BYN2830.md`–`BYN2832.md` are new pages. `BYN2760.md` gets the new help. R11.
- `tests/cases/run/`, `tests/cases/diag/`, `tests/cases/typed/` goldens: the ones named in the tasks. R11 R12.
- `tests/Bayan.Tests/SymbolLawTests.cs` (new), `tests/Bayan.Tests/MapKeyLawTests.cs` (new), `tests/Bayan.Tests/MapModelTests.cs` (extended with `Symbol` and composite keys). L015 L016.
- `docs/laws/015-*.md`, `docs/laws/016-*.md`: new laws, status `proposed` → `enacted`.

## Data / interfaces

- Symbols: `BuiltinKind.Symbol`, `BuiltinType Symbol`; `MapType.IsValidKey` (recursive), `MapType.InvalidKeyPart`; `BuiltinFunction.SymbolIntern`.
- Bound: `BoundSymbolLiteral(Syntax, Text)`, `BuiltinMember.SymbolText`.
- C: `byn_sym` (4 bytes), `static const byn_str byn_symtab[]` (sorted literal texts, `""` at 0), `bynh_hash_<key>(K k) -> uint64_t` per key type.
- Runtime: `byn_sym_init`, `byn_sym_intern`, `byn_sym_text`, `byn_hash_mix`.
- Bayan surface (round-3 syntax: type-first declarations, particles, `void main()`):

```bayan
struct MemberKey {
    i32 owner
    Symbol name
}

Symbol readName(string src, from i64 start, until i64 end) uses alloc {
    return Symbol.intern(src[start..end])
}

void main() {
    let keywords: Map<Symbol, i32> = ["let": 1, "var": 2]
    var members: Map<MemberKey, i32> = []
    let name = readName("let x", from: 0, until: 3)
    if keywords.contains(name) {
        members[MemberKey { owner: 1, name: name }] = keywords[name]
    }
    for (key, value) in members {
        print($"{key.name} {value}")
    }
}
```

- Diagnostics (BYN2830–2832 contiguous, 2833–2839 spare; each help starts with a verb from `HelpLanguageTests`' list, revision 0006; `{n}` are message arguments):

| Code | Name and message | Help | R | Task |
|---|---|---|---|---|
| BYN2830 | `HashMapNotSupported` "`HashMap` is not supported yet: `Map` hashes the same way and keeps insertion order" (label at `HashMap`) | "write `Map<{0}, {1}>`"; fix yes | R8 | T2 |
| BYN2831 | `KeyPartNotHashable` "`{0}` cannot be a `Map` key: {1} `{2}` is a `{3}`" ({1} is `field` or `element`) | "use a field that identifies the value as the key, such as an id or a `Symbol`"; fix no | R5 R9 | T3 |
| BYN2832 | `StringNotSymbol` "a `string` value is not a `Symbol`; only a string literal converts" | "convert it with `Symbol.intern({0})`"; fix yes | R2 | T2 |
| BYN2760 | existing `InvalidMapKey`, help rewritten | "use an integer type, `rune`, `bool`, `string`, `Symbol`, a plain enum, or a tuple or struct of those as the key" | R5 | T3 |

## Risks & mitigations

- **Padding in struct keys.** Today `KeyHash` hashes `sizeof k` raw bytes. Struct temporaries are not zeroed, so two equal keys could hash differently and both be stored. Mitigation: field-wise hashing (Q377), the `run/map-key-struct-padding` golden (a struct `Pad` with fields `bool flag` and `i64 id`) and L016.
- **Literal IDs vs runtime IDs.** A literal must equal `Symbol.intern` of the same text. Mitigation: `byn_sym_init` inserts the literal table first and in order, so literal text `N` gets ID `N` and a later intern finds it. Tested by L015's "literal equals its runtime twin" property and `run/symbol-literal-runtime`.
- **Literal positions the binder does not see as "expected `Symbol`".** Generic arguments and `T?` contexts are examples. Mitigation: BYN2832 fires whenever a `string` meets `Symbol` without the conversion, so a gap is a diagnostic with a fix, never wrong C. The `diag/symbol-from-string` golden covers `Symbol?` and a generic call.
- **The interned table is unbounded.** A program that interns untrusted text keeps it forever. Mitigation: documented in the spec text and Q373. The compiler's own input is bounded by its sources.
- **Ordering cost.** `<` on symbols compares text. Mitigation: documented; `==` and hashing, which symbol tables need, stay O(1).
- **`bayan format` does not apply BYN2830's fix.** It rewrites only the parse-level input forms in `Format.cs`'s `InputForms`, and BYN2830 is reported by `TypeResolver`. Mitigation: T2 tests the fix with `Format.ApplyFixes`; making `bayan format` apply binder fixes is outside 008.
- **006 lands an equality or hash interface.** Resolved: 006 as built has only `IEnumerator` and `IIndexable`, and primitives never conform. R5 stays a closed set, and an interface-based key extends `IsValidKey` later without changing any accepted program (Q376).
- **What 008's users would want from 007's deferred parts.** 008 itself needs no function value, but symbol-table code in the binder port meets three of 007's limits. (a) A lambda cannot capture a `Map`: captures are copies and a `Map` is `unique` (BYN2780), and capturing a `var` by reference is deferred; pass the table as a parameter. (b) A dispatch table such as `Map<Symbol, () -> void>` is BYN2822 (a function type as a type argument), and stored lambdas need `dyn (A -> B)`, which is deferred; key a `Map<Symbol, i32>` and switch on the value. (c) Escaping closures are deferred, so a lookup cannot be returned as a closure over a table. A `Symbol` is a copyable 4-byte value, so a lambda capturing one, and a `Symbol -> bool` parameter, work as 007 built them; a lambda that calls `Symbol.intern` charges `alloc` to the call that passes it (007 R16).

## Coverage check

| Requirement | Tasks | What covers it |
|---|---|---|
| R1 | T1, T2 | type, defaults, equality |
| R2 | T2, T5 | literal conversion, BYN2832, sorted `.rodata` table |
| R3 | T2, T5 | `Symbol.intern`, runtime intern, panic handler, `alloc` |
| R4 | T4, T5 | `.text`, interpolation |
| R5 | T3 | key set, BYN2831, BYN2760 help |
| R6 | T6 | field-wise hash, padding |
| R7 | T6 | map behaviour with new keys, regions |
| R8 | T2 | BYN2830 and fix |
| R9 | T3 | instantiation check |
| R10 | T5 | runtime table, emitted only when used |
| R11 | T2, T3, T5, T6 | pages and goldens |
| R12 | T7 | self-hosting golden, `bayan types` determinism |

| Law | Prove | Enact |
|---|---|---|
| L015 | T8 | T9 |
| L016 | T10 | T11 |

Q371–Q381 are in the spec. T12 copies them into the handoff.
