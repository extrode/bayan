# 008 — Plan
Spec: ./008-spec.md
Written 2026-10-22 against dev at `7cff9b9` (round 2 applied). Line citations are to that commit. Design round 3 (`docs/bayan-design-round-3.md`, plan `docs/plans/2026-10-22-001-design-round-3.md`) wins on conflict: steps 5.1a–5.1d rewrite the parser and goldens before 008 T1, 5.1c renames `char` to `rune` (`PrimitiveType.Char` and the BYN2760 help text move with it), and 5.4 rewrites the `Map` members in `BuiltinMembers.cs` before 008 T4; re-locate by method name where lines have moved.

## Approach

**Extend `Map`; `HashMap` is a fix-it, not a type.** `MapType` (Types.cs:207-225) is already a seeded open-addressed hash index over dense, insertion-ordered entries (`byn_map_hdr`, bayan-rt.h:207-241; `byn_map_find`/`insert`/`remove`, bayan-rt.c:648 on). Lookup is O(1) today; what an unordered `HashMap` would save is the dense array and tombstones, and what it would cost is iteration order that depends on the seed, which `byn_hash_seed_init` (CEmitter.cs:111-114) takes from the run unless `--test` is passed. Stage 2 and stage 3 of the bootstrap must emit byte-identical C, and the binder port iterates its tables, so the self-hosted compiler needs ordered maps. `TypeResolver` resolves the name `HashMap` to `MapType` after reporting BYN2830 with a fix, so programs written to spec §3.5 compile with one diagnostic and the fix applier turns them into `Map` (Q132).

**`Symbol` is a `BuiltinType` holding a `uint32_t` ID; literals are interned by the compiler, everything else by the runtime.** `BuiltinKind.Symbol` joins `Arena`, `StrBuf` and the rest (Types.cs:315-356). It is not `unique` (TypeFacts.cs:27 stays as it is), not a borrow, and defaultable (BinderCalls.cs:511 `IsDefaultable`). The binder turns a string literal into `BoundSymbolLiteral(Syntax, Text)` where the expected type is `Symbol`. Conversions are type-based (`Conversions.Implicit`), so the conversion happens where literals are bound against an expected type in `BinderExpressions.cs`, and `Convert` reports BYN2832 for any other `string` → `Symbol`. At emission, `CEmitter` collects every `BoundSymbolLiteral` text in the program, sorts the texts by UTF-8 bytes with `""` first, and emits them as one `static const byn_str byn_symtab[]` in `.rodata`; a literal becomes the constant `(byn_sym){N}`. `main` calls `byn_sym_init(byn_symtab, N)` before user code, next to `byn_hash_seed_init`. `Symbol.from` and `.text` become calls to `byn_sym_intern` and `byn_sym_text`. Equality is `a.id == b.id`. Ordering compares `byn_sym_text` with the same byte comparison `string` uses. Literal IDs depend only on the source, and no ID is observable (Q133), so the seed and intern order cannot reach the output (L015).

**The runtime table owns its text for the whole run.** `byn_sym_intern` hashes the text with `byn_hash_bytes`, probes an open-addressed index of IDs, and on a miss copies the text into a growing chunk and appends `(ptr, len)` to the ID array. Everything comes from the general allocator, as `byn_str_copy` does, never from `context.allocator`, and nothing is freed (Q134). That is why `sym.text` borrows nothing: `RegionAnalysis` treats `Symbol.from` and `.text` like the builtins whose results borrow nothing (RegionAnalysis.cs:347-357), and `Symbol.from` is listed with the allocations a panic handler may not make (PanicHandlerCheck.cs:60-72).

**Composite keys hash field by field.** `MapType.IsValidKey` becomes recursive: tuples and structs qualify when every part does and the struct is not `@noEquality`. A new `MapType.InvalidKeyPart(t)` returns the first offending part's path and type for BYN2831. In C, `KeyEq` (CEmitter.cs:492) already reuses `Equal` (~:470), which handles `StructSymbol` and `TupleType`. `KeyHash` (:481-489) today runs `byn_hash_bytes(&k, sizeof k)`, which would read padding. It becomes a per-key-type helper `bynh_hash_<key>` built through `Helper` (:335): primitives hash their bytes, `string` its `ptr, len`, `Symbol` its `id`, and composites fold the parts in declaration order through a new `byn_hash_mix(h, part)`. `byn_map_*` still receives a finished 64-bit hash, so the runtime map is unchanged (Q138).

**`for`, literals, slots and regions need no new rules.** Map iteration (Lowerer.cs:599), map literals (Lowerer.cs:969, BinderExpressions.cs:1409-1437) and slots (Lowerer.cs:435, :728) are keyed on `MapType`, not on the key kind. RegionAnalysis already finds every `string` inside a value through its root, so a composite key holding a `string` borrows as a `string` key does today (RegionAnalysis.cs:138, :237, :554, :568). A `run/` golden for a `(name: string, scope: i32)` key pins this; if it fails, T6 extends the borrow walk to tuple and struct keys.

## Tech / dependencies

- No new packages. No lexer or parser change: `Symbol` and `HashMap` are type names resolved in `TypeResolver` (built-in names list at TypeResolver.cs:263), and `Symbol.from` binds through the `BoundTypeRef` path that `string.from`/`tryFrom` use (Builtins.cs:47-51, BinderExpressions.cs:559-564).
- Runtime (`src/runtime/bayan-rt.c/.h`): new `byn_sym`, `byn_sym_init`, `byn_sym_intern`, `byn_sym_text`, `byn_hash_mix`. They are emitted and linked only when the program uses `Symbol`, behind a flag like `_hashes` (CEmitter.cs:32). Seeded FNV-1a stays (`byn_hash_bytes`).
- Existing pieces reused, not rewritten: `byn_map_*` and `MapMethod` (CEmitter.cs:501-558), `Equal`/`KeyEq` for composite equality, `IsEquatable` (BinderExpressions.cs:1026-1041), the string ordering operators (:1021), `Helper` (CEmitter.cs:335), `BoundTypeRef` statics, the map-literal binder, the BYN2760 instantiation forwarding (Compilation.cs:987).
- Round-3 steps 008 sits on:
  - 5.1a–5.1d: syntax for every golden. 5.1c's `rune` is used in key lists and help text.
  - 5.2a: the input-form fix applier and `Fmt.cs`, which apply BYN2830's fix.
  - 5.3d: the effects pass (new file, J13). `Symbol.from` is registered as an `alloc` source there; if 008 T5 lands first, the registration lands with 5.3d instead.
  - 5.4: the stdlib and particle sweep, which owns `BuiltinMembers.cs`'s `Map` members and J24 (`getOr` → `at(k) ?? f`).
- Spec 006 (`feat/006-interfaces-iterators`, BYN2800–2819, L012, Q89–Q113) and spec 007 (`feat/007-lambdas`, BYN2820–2829, L013–L014, Q114–Q131) are not used: 008 adds no interface constraint on keys (Q137) and takes no function argument. Both edit `Types.cs`, `TypeResolver.cs`, `BinderExpressions.cs`, `CEmitter.cs` and `DiagnosticCodes.cs`. Whichever of 008 T2/T3 and 006/007 T1–T3 lands second rebases and re-runs the suite.
- Code ranges checked: the spec, the round-3 plan (BYN1040–1062, 3201–3211, 5004–5006, 5103, 6001–6007, 7001–7005, 8001–8004), 006 (2800–2819) and 007 (2820–2829, plus 1028, 1050, 2029) leave BYN2830–2839 free; a grep for `BYN283` finds nothing. Laws L001–L011 are on dev, and L012–L014 are taken by 006 and 007, so 008 uses L015–L016. Questions continue after Q131.
- L016's C check compiles `out.c` standalone with `-Wall -Werror` in the `BuildStandalone` pattern (OwnershipCheckerTests.cs), as L012 and L013 do. Whichever of those laws lands first does the warning cleanup.
- .NET 10 NativeAOT, no reflection.

## Changes by area

- `src/Bayan.Syntax/Diagnostics/DiagnosticCodes.cs`: BYN2830–2832 (below), each registered in the task that first raises it. Rewrite the BYN2760 help in `TypeResolver` and `BinderExpressions` to "use an integer type, `rune`, `bool`, `string`, `Symbol`, a plain enum, or a tuple or struct of those as the key". R5 R8 R11.
- `src/Bayan.Semantics/Symbols/Types.cs`: `BuiltinKind.Symbol` plus its `BuiltinType` instance and `DisplayName` `Symbol`. `MapType.IsValidKey` (:223) accepts `Symbol`, `TupleType` with all elements valid, and `StructSymbol` with all fields valid and no `@noEquality`. Add `MapType.InvalidKeyPart(TypeSymbol) -> (string Part, TypeSymbol Type)?`. A struct that contains itself cannot exist (`unique` and size rules), so the recursion terminates. R1 R5 R9.
- `src/Bayan.Semantics/TypeResolver.cs`: `case "Map"` (:113-130) reports BYN2831 when `InvalidKeyPart` names a part, else BYN2760. `case "HashMap"` reports BYN2830 with fix `Map<…>` and falls through to `Map`. `Symbol` and `HashMap` are added to the built-in names (:263) so a user type of that name is the existing redeclaration diagnostic. R1 R5 R8.
- `src/Bayan.Semantics/Builtins.cs`: `Symbol.from(text: string) -> Symbol` as a static next to `string.from` (:47-51). R3.
- `src/Bayan.Semantics/Binding/BoundNodes.cs`: `BoundSymbolLiteral(Syntax, string Text) : BoundExpression` with `Type = Symbol`. `BuiltinMember.SymbolFrom`, `SymbolText`. R2 R3 R4.
- `src/Bayan.Semantics/Binding/BinderExpressions.cs`:
  - A string literal bound against expected type `Symbol` (or `Symbol?`) becomes `BoundSymbolLiteral`; an interpolated one does not.
  - `Convert` reports BYN2832 for `string` → `Symbol`, with fix `Symbol.from(<expr>)`.
  - In `==`, `!=` and the ordering operators (:1021), a string literal facing a `Symbol` operand binds as a symbol, and `Symbol` joins the ordering types.
  - `IsEquatable` (:1026) accepts `Symbol`.
  - Map literal keys (:1409-1437) take the key type as expected type, so `"let": .letKeyword` converts.
  - BYN2831 is reported alongside BYN2760 (:1423).
  - Static `Symbol.from` binds on the `BoundTypeRef` path (:559-564).
  
  R1 R2 R3 R5.
- `src/Bayan.Semantics/Binding/BuiltinMembers.cs`: `text` on `Symbol` → `SymbolText`, returns `string`. No `Map` member changes (5.4 owns them, Q142). R4 R7.
- `src/Bayan.Semantics/Binding/BinderCalls.cs`: `IsDefaultable` (:511) accepts `Symbol`, so `slot` (:492) and struct defaults work. R1 R7.
- `src/Bayan.Semantics/Compilation.cs`: the instantiation check (:987) forwards BYN2831 as it does BYN2760. R9.
- `src/Bayan.Semantics/Binding/RegionAnalysis.cs`: `SymbolFrom` and `SymbolText` join the no-borrow results (:347-357). A `Symbol` value has no root. Composite keys with `string` parts are checked by the golden in T6 (Approach). R3 R4 R7.
- `src/Bayan.Semantics/Binding/PanicHandlerCheck.cs`: `SymbolFrom` joins the restricted allocations (:60-72). R3.
- `src/Bayan.Semantics/Binding/TypedDump.cs`: dump `BoundSymbolLiteral` as `symbol "text"`. Do not print IDs; L009 stays byte-stable. R2 R12.
- Effects pass (round-3 5.3d's new file): `SymbolFrom` is an `alloc` source. R3.
- `src/Bayan.Lowering/Lowerer.cs`: `BoundSymbolLiteral` passes through. Interpolation of a `Symbol` lowers to its text, the way it lowers `string`. Map iteration (:599) and literals (:969) are unchanged. R4 R7.
- `src/Bayan.Backend.C/CEmitter.cs`:
  - `byn_sym` in the type mapping (`Named`, :254-306).
  - Literal table collection and the `byn_symtab` emission. `byn_sym_init` in `main` next to `byn_hash_seed_init` (:111-114), guarded by a `_symbols` flag like `_hashes` (:32).
  - `KeyHash` (:481-489) becomes `bynh_hash_<key>` built through `Helper`, field-wise for composites and `id` for `Symbol`. `KeyEq` (:492) is unchanged apart from a `Symbol` case in `Equal` (~:470).
  - The `Key` (:248) of `Symbol` is `sym`.
  - Ordering emits `byn_str_cmp` (bayan-rt.h:149, as for `string` at CEmitter.cs:1537) on the two `byn_sym_text` results.
  
  R2 R4 R6 R10.
- `src/runtime/bayan-rt.h`, `src/runtime/bayan-rt.c`: `typedef struct { uint32_t id; } byn_sym;`, `void byn_sym_init(const byn_str* lits, uint32_t n)`, `byn_sym byn_sym_intern(byn_str s)`, `byn_str byn_sym_text(byn_sym s)`, `uint64_t byn_hash_mix(uint64_t h, uint64_t part)`. The table is static in `bayan-rt.c`, allocated with the general allocator, and never freed. R3 R6 R10.
- `docs/diagnostics/BYN2830.md`–`BYN2832.md` are new pages. `BYN2760.md` gets the new help. R11.
- `tests/cases/run/`, `tests/cases/diag/`, `tests/cases/types/` goldens: the ones named in the tasks. R11 R12.
- `tests/Bayan.Tests/SymbolLawTests.cs` (new), `tests/Bayan.Tests/MapKeyLawTests.cs` (new), `tests/Bayan.Tests/MapModelTests.cs` (extended with `Symbol` and composite keys). L015 L016.
- `docs/laws/015-*.md`, `docs/laws/016-*.md`: new laws, status `proposed` → `enacted`.

## Data / interfaces

- Symbols: `BuiltinKind.Symbol`, `BuiltinType Symbol`; `MapType.IsValidKey` (recursive), `MapType.InvalidKeyPart`.
- Bound: `BoundSymbolLiteral(Syntax, Text)`, `BuiltinMember.SymbolFrom`, `BuiltinMember.SymbolText`.
- C: `byn_sym` (4 bytes), `static const byn_str byn_symtab[]` (sorted literal texts, `""` at 0), `bynh_hash_<key>(K k) -> uint64_t` per key type.
- Runtime: `byn_sym_init`, `byn_sym_intern`, `byn_sym_text`, `byn_hash_mix`.
- Bayan surface (round-3 syntax):

```bayan
struct MemberKey {
    owner: i32
    name: Symbol
}

intern(src: string, start: i64, end: i64) -> Symbol uses alloc {
    return Symbol.from(src[start..end])
}

main() uses alloc {
    let keywords: Map<Symbol, i32> = ["let": 1, "var": 2]
    var members: Map<MemberKey, i32> = []
    let name = intern("let x", 0, 3)
    if keywords.contains(name) {
        members[MemberKey { owner: 1, name: name }] = keywords[name]
    }
    for (key, value) in members {
        print($"{key.name} {value}")
    }
}
```

- Diagnostics (BYN2830–2832 contiguous, 2833–2839 spare; help is verb-first per spec 005 R15; `{n}` are message arguments):

| Code | Name and message | Help | R | Task |
|---|---|---|---|---|
| BYN2830 | `HashMapNotSupported` "`HashMap` is not supported yet: `Map` hashes the same way and keeps insertion order" (label at `HashMap`) | "write `Map<{0}, {1}>`"; fix yes | R8 | T2 |
| BYN2831 | `KeyPartNotHashable` "`{0}` cannot be a `Map` key: {1} `{2}` is a `{3}`" ({1} is `field` or `element`) | "use a field that identifies the value as the key, such as an id or a `Symbol`"; fix no | R5 R9 | T3 |
| BYN2832 | `StringNotSymbol` "a `string` value is not a `Symbol`; only a string literal converts" | "convert it with `Symbol.from({0})`"; fix yes | R2 | T2 |
| BYN2760 | existing `InvalidMapKey`, help rewritten | "use an integer type, `rune`, `bool`, `string`, `Symbol`, a plain enum, or a tuple or struct of those as the key" | R5 | T3 |

## Risks & mitigations

- **Padding in struct keys.** Today `KeyHash` hashes `sizeof k` raw bytes. Struct temporaries are not zeroed, so two equal keys could hash differently and both be stored. Mitigation: field-wise hashing (Q138), the `run/map-key-struct-padding` golden (`struct Pad { flag: bool, id: i64 }`) and L016.
- **Literal IDs vs runtime IDs.** A literal must equal `Symbol.from` of the same text. Mitigation: `byn_sym_init` inserts the literal table first and in order, so literal text `N` gets ID `N` and a later intern finds it. Tested by L015's "literal equals its runtime twin" property and `run/symbol-literal-runtime`.
- **Literal positions the binder does not see as "expected `Symbol`".** Generic arguments and `T?` contexts are examples. Mitigation: BYN2832 fires whenever a `string` meets `Symbol` without the conversion, so a gap is a diagnostic with a fix, never wrong C. The `diag/symbol-from-string` golden covers `Symbol?` and a generic call.
- **The interned table is unbounded.** A program that interns untrusted text keeps it forever. Mitigation: documented in the spec text and Q134. The compiler's own input is bounded by its sources.
- **Ordering cost.** `<` on symbols compares text. Mitigation: documented; `==` and hashing, which symbol tables need, stay O(1).
- **Collision with round-3 5.4 in `BuiltinMembers.cs`.** Mitigation: T4 comes after 5.4 and adds only `text` on `Symbol`. Examples avoid `getOr` (Q142).
- **006 lands an equality or hash interface.** Mitigation: R5 is a closed set, and an interface-based key extends `IsValidKey` later without changing any accepted program (Q137).

## Coverage check

| Requirement | Tasks | What covers it |
|---|---|---|
| R1 | T1, T2 | type, defaults, equality |
| R2 | T2, T5 | literal conversion, BYN2832, sorted `.rodata` table |
| R3 | T2, T5 | `Symbol.from`, runtime intern, panic handler, `alloc` |
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

Q132–Q142 are in the spec. T12 copies them into the handoff.
