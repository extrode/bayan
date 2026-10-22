# 010 — Plan
Spec: ./010-spec.md
Written 2026-10-22 against `8322db9` (spec 009's docs on top of dev). Line numbers below are at that commit; round-3 steps 5.3a–5.4 move them and add files, and T1 re-reads and re-sizes them.

## Approach

Port `src/Bayan.Semantics` the way specs 001 and 009 ported the lexer and the parser: one Bayan file per C# file or region, one Bayan method per C# method, same names in camel case, same pass order, same diagnostics, and a harness that builds the Bayan program once and diffs its output against the C# reference on every input the repository has plus a generated corpus. The port starts after every round-3 step that changes semantics has landed (Depends-on line), so it ports one front end, not a moving one.

The C# work comes first (T1), as 009's BYN1070 did: make every hash-order iteration that reaches output explicit, and find out whether generic instantiation and deep binary chains need bounds (BYN2840, BYN2841). Without that, "identical on every input" could fail for reasons no port can fix.

The comparison text is the version-1 typed dump followed by the rendered diagnostics (Q153). The dump alone is empty for any input with errors, so the diagnostics carry most of the equivalence; the dump carries what diagnostics cannot show, the type every local resolved to, including the two-pass integer-literal inference.

The port's data is pools and ids (Q155): symbols and types are rows, types are interned so equality is id equality, and everything a C# `Dictionary<Symbol, …>` keyed by reference becomes a `Map` keyed by an id struct or a `Symbol` (008 R5). The binder reads 009's tree through its node ids and never copies it.

The port is large (about 9,400 C# lines before round 3; T1 records the count after). Tasks T3–T15 each port one C# file or one region of at most about 900 lines, in dependency order, and each ends with the `diag/` cases it unlocks removed from the pending list (R9). Self-reach (T16) comes when every pass exists; the full `diag/` set (T17) is the target.

## Tech

- Language: Bayan in round-3 syntax for `selfhost/semantics/`; C# for T1 and the harness.
- Uses from 006: `for x in pool.items` over `Pool<T>` (reused from `selfhost/parser/ast.byn`; built-in `List<T>` iteration), a generic `lookup<T>(name: Symbol, from table: Map<Symbol, T>) where T : …` only where two symbol kinds share code, returning an id or a member result, not `T` by value (BYN2780; the `Symbol`-keyed form is unverified until 008 lands); no `dyn ref` stored in a field.
- Uses from 007: `T -> bool` parameters with `it` lambdas for scope and member searches; no capture of `this` or a `var`; no function value in a field (007 R10), so passes are plain functions over a `Checker` struct, not tables of callbacks.
- Uses from 008: `Symbol` for every name; `Map` keys that are `Symbol`, id structs, `TypeKey` structs and tuples (008 R5); insertion-order iteration (L003) as the port's only order.
- Uses from 009: `Tree`, its pools and id structs, `Name { span, text: Symbol }`, and `parse(source) -> Tree`.
- Build: `Compilation.Create(entryFiles, packageRoot: selfhost/semantics, …)` (`src/Bayan.Semantics/Compilation.cs:94`), then `CliApp.EmitC(c, BuildMode.Fast)` and `Toolchain.Build`, as `tests/Bayan.Tests/SelfhostLexerTests.cs` does; `BAYAN_CACHE=tmp/cache`.
- No new NuGet packages, no runtime (`src/runtime/`) change, no C emitter change.

## Changes by area

### C# front end (T1)

- Order audit: each of the 82 `Dictionary<` / `HashSet<` sites in `src/Bayan.Semantics` (22 in `Compilation.cs`, 11 in `OwnershipAnalysis.cs`, 9 each in `Binder.cs` and `RegionAnalysis.cs`, 7 in `Symbols.cs`, 6 in `FlowAnalysis.cs`, the rest in single digits) is classified as lookup-only or iterated; an iterated one whose order reaches a diagnostic, a related span, a label, a help text or the dump is replaced by an insertion-ordered list or an explicit sort. `RegionAnalysis.cs:581` already sorts its regions; sites like it are recorded as done. The list of sites and verdicts goes into T1's commit message and into a comment at the top of `tests/Bayan.Tests/SelfhostCheckTests.cs`.
- Instantiation bound: a test in `tests/Bayan.Tests/GenericsTests.cs` compiles a generic `grow<T>(x: T)` that calls `grow` with a `List<T>`, under a 30 s timeout. If it does not terminate, `Compilation.InstantiateFunction` and `InstantiateType` (`:995`, `:1021`) carry an instantiation depth (the site's depth plus one) and at 65 report BYN2840 at the call site, return the error function or type, and enqueue nothing; `DiagnosticCodes.cs` gains `InstantiationTooDeep`. If it terminates, the test asserts the diagnostic it gets and BYN2840 stays reserved.
- Expression depth: a test binds a chain of 100,000 `1 +` terms through `Compilation.Create` in a thread with the default 1 MB stack. If it overflows, `Binder.BindExpr` (`BinderExpressions.cs:17`) counts depth and at 4,097 reports BYN2841 at the operand and returns an error expression; the analyses (`FlowAnalysis`, `OwnershipAnalysis`, `RegionAnalysis`, `ConstantFolding`) stop at an error expression already. If it does not, BYN2841 stays reserved.
- Errors with a dump: a test runs `TypedDump.ToJson` on every `diag/` case and asserts it does not throw and is the same over two runs (L009's statement, extended to inputs with errors).
- Size: `wc -l` of `src/Bayan.Semantics/**/*.cs` after round 3 and the files 5.3c and 5.3d add (the effects pass is a new file, round-3 plan §4 5.3d); a region over about 900 lines gets its own task, recorded here by T1.

### Diagnostics in the lexer port (T2)

- `selfhost/lexer/diagnostics.byn`: `Diagnostic` gains `severity` (Q161), `source: i32` (index into the module's file list, `-1` for none) and `related: List<RelatedSpan>`; `render` and `renderAll` print warnings, secondary labels and paths as `src/Bayan.Syntax/Diagnostics/DiagnosticRenderer.cs` (283 lines) does. The lexer and parser keep constructing errors through one helper, so their call sites change by one argument at most.
- L001 and L017 are re-run in the same change.

### Bayan front end (T2–T15)

New directory `selfhost/semantics/`, one file per C# file or region:

| Bayan file | C# source | Task |
|---|---|---|
| `main.byn` | `CliApp.Types`/`Check` entry (`src/Bayan.Cli/CliApp.cs:132-138`), `DiagTests` package rule | T2 |
| `typed-dump.byn` | `Binding/TypedDump.cs` (78), JSON writer matching `Utf8JsonWriter` indented output and `UnsafeRelaxedJsonEscaping` | T2 |
| `messages.byn` | message templates of every semantic code in `DiagnosticCodes.cs` | T2, then each task adds its codes |
| `symbols.byn`, `types.byn` | `Symbols/Symbols.cs` (490), `Symbols/Types.cs` (371), `TypeFacts.cs` (64) | T3 |
| `conversions.byn` | `Conversions.cs` (77) | T3 |
| `compilation.byn` | `Compilation.cs:24-177` (loading, files, imports) | T4 |
| `declare.byn` | `Compilation.cs:178-885` (declaration, attributes, `@cases`, members, signatures, shape and recursion checks) | T4 |
| `naming.byn`, `type-resolver.byn` | `Naming.cs` (187), `TypeResolver.cs` (273) | T4 |
| `builtins.byn` | `Builtins.cs` (207), `Binding/BuiltinMembers.cs` (85) | T5 |
| `bound.byn` | `Binding/BoundNodes.cs` (306), `Binding/BoundTree.cs` (113) | T6 |
| `binder.byn` | `Binding/Binder.cs` (1,057: scopes, bodies, statements, narrowing, `for`, `return`, `defer`, `context`) | T6 |
| `expressions.byn`, `folding.byn` | `BinderExpressions.cs:1-764` (conversion, literals, names, members), `ConstantFolding.cs` (182) | T7 |
| `operators.byn`, `construction.byn` | `BinderExpressions.cs:765-1176` (index, unary, binary, `??`, ranges, `in`, conditional, casts), `:1177-1587` (struct literals, `with`, collections, maps, tuples, `stackalloc`, interpolation) | T8 |
| `calls.byn` | `BinderCalls.cs:1-591` (calls, arguments, particles, inference and `Unify`, construction, `Owned`, `Untrusted`, `move`) | T9 |
| `patterns.byn` | `BinderCalls.cs:592-1405` (`try`, `catch`, switch, patterns, exhaustiveness, assignment, writability) | T10 |
| `generics.byn`, `program.byn` | `Compilation.cs:886-1306` (`BindAll`, instances, two-pass `BindFunction`, error-set inference, mutation and `requires` checks), `Binding/ErrorFlow.cs` (33) | T11 |
| `flow.byn`, `lints.byn` | `FlowAnalysis.cs` (325), `PanicHandlerCheck.cs` (83), `NeverReassignedVarLint.cs` (29), `SpanParameterLint.cs` (78) | T12 |
| one file per round-3 pass | the effects pass (5.3d) and typestate flow (5.3c), named by T1 | T13 |
| `ownership.byn` | `OwnershipAnalysis.cs` (437) | T14 |
| `regions.byn` | `RegionAnalysis.cs` (590) | T15 |

`selfhost/lexer/` changes only as T2 says; `selfhost/parser/` does not change.

### Harness (T2, T16–T21)

- `tests/Bayan.Tests/SelfhostCheckTests.cs` (new), modelled on `SelfhostLexerTests.cs` and 009's `SelfhostParserTests.cs`:
  - a `Lazy` build of `bayan-check` from the lexer and parser library files plus `selfhost/semantics/*.byn`;
  - `Reference(path)`: `Compilation.Create` with `DiagTests`' options, then `TypedDump.ToJson(c) + "\n" + DiagnosticRenderer.RenderAll(c.Diagnostics.Items, includePath)`;
  - `[Theory]` over `Golden.Cases("diag")` and `Golden.Cases("typed")` (the latter also against `.types`);
  - `[Theory]` over every `.byn` under `tests/cases/run`, `tests/cases/parse`, `examples`, `selfhost`, `tools`;
  - a `[Fact]` for self-reach (R13);
  - a `[Fact]` that every semantic code in `DiagnosticCodes.cs` appears in `selfhost/semantics/messages.byn`;
  - the pending list `tests/Bayan.Tests/selfhost-check-pending.txt`: a listed case is expected to differ, and a listed case that matches fails with "remove `<name>` from the pending list";
  - `[Fact, Trait("Law","L018")]` over the corpus (Q162) through `--many`, plus stdin runs for a sample of 200;
  - the timing `[Fact]` honouring `BAYAN_SKIP_TIMING`.
  - On a mismatch the message names the input and prints the first differing line of each side.

### Docs (T1, T22, T23)

- If allocated: `docs/diagnostics/BYN2840.md`, `docs/diagnostics/BYN2841.md`, `tests/cases/diag/instantiation-too-deep.{byn,diag}`, `tests/cases/diag/expression-too-deep.{byn,diag}`.
- `docs/laws/018-binder-equivalence.md` (T22).
- `docs/typed-dump.md`: one line that L018 compares the dump with the C# one on every input, errors included.
- `docs/bayan-handoff.md`: Q152–Q164 under Questions (`:411`), row 010 marked done (`:400`).
- `docs/bayan-self-hosting.md`: phase 3 notes semantics as ported.
- `CHANGELOG.md`: one entry.

## Data and interfaces

### New diagnostic codes

| Code | Name | Message | Label | Help | Fix |
|---|---|---|---|---|---|
| BYN2840 | `InstantiationTooDeep` | instantiating `{0}` nests more than 64 generic instances | instantiated here | call it with the same type arguments, or move the recursive part into a non-generic function | none |
| BYN2841 | `ExpressionTooDeep` | expression is nested more than 4,096 levels deep | too deep | split the expression into named locals | none |

Both are allocated only if T1 shows the failure they bound (R8). BYN2840–2849 is 010's block: dev uses BYN2700–2791, specs 006, 007 and 008 use BYN2800–2819, 2820–2829 and 2830–2839, the round-3 plan allocates BYN3201–3208, 5001–5008, 6001–6007, 7001–7004 and 8001, 009 allocates BYN1070 and keeps 1071–1079, and a search of dev, the 006, 007, 008 and round-3 branches at this commit finds no `BYN284`. BYN2842–2849 stay spare for 010.

### Symbols and types (round-3 syntax)

```bayan
struct TypeId {
    index: i32
}

struct TypeListId {
    index: i32
}

enum TypeKind {
    primitive
    named
    nullable
    span
    readOnlySpan
    function
    typeParameter
    error
}

struct TypeKey {
    kind: TypeKind
    symbol: i32
    args: TypeListId
    name: Symbol
}

struct Types {
    keys: List<TypeKey>
    ids: Map<TypeKey, TypeId>

    intern(ref this, key: TypeKey) -> TypeId {
        if ids.contains(key) {
            return ids[key]
        }
        let id = TypeId { index: (i32)keys.length }
        keys.append(key)
        ids[key] = id
        return id
    }
}

struct MemberKey {
    owner: TypeId
    name: Symbol
}
```

`TypeKind` lists the shapes `Types.cs` has at `8322db9`; T3 adds a case per C# type class, including round 3's state and distinct types. `symbol` is the row of the declaring symbol in its pool (`-1` for none). A display name is computed from the key the way `TypeSymbol.DisplayName` is, so the dump's `type` field and every message match.

### Scopes and side tables

```bayan
struct Scope {
    locals: Map<Symbol, LocalId>
}

lookup(name: Symbol, from scopes: List<Scope>) -> LocalId? {
    var i = scopes.length - 1
    while i >= 0 {
        if scopes[i].locals.contains(name) {
            return scopes[i].locals[name]
        }
        i -= 1
    }
    return null
}
```

Bound expressions are a `Pool<BoundExprNode>` with `struct BoundExprNode { span: TextSpan, type: TypeId, kind: BoundExprKind }`, one `BoundExprKind` case per record in `BoundNodes.cs`. `Map<ExprId, BoundExprId>` links 009's tree to it. Diagnostic suppression (`Binder.Hold`/`Release`, `Compilation._scratch`, `_firstPass`) is a stack of diagnostic lists on the `Checker` struct, pushed and popped where C# swaps bags.

### CLI

`bayan-check [<path>|-] | --many <dir> | --time <path>`; output is always `Style.Plain`; exit 0 / 1 / 2 / 4 as spec R1.

## Risks

- Round-3 drift. 5.3a–5.4 add several thousand lines and new passes. Mitigation: 010 starts after them (T1 depends on 5.4), T1 re-sizes the tasks, and a later semantics change is a C# change first that fails L018 until ported (Q157).
- Size. The port is several times 009's; compiler bugs will show up. Mitigation: each one is fixed in the C# compiler with its own golden before the port continues, and the task that found it names it.
- Order. A hash-order dependence T1 misses shows as a flaky or platform-dependent mismatch. Mitigation: the corpus runs under the fixed test hash seed, and T1's audit list is the place to add a missed site.
- JSON. `Utf8JsonWriter` escapes and indents in ways a hand-written writer can miss (non-ASCII, control characters, `UnsafeRelaxedJsonEscaping`). Mitigation: the corpus includes identifiers with non-ASCII letters and string-typed names; T2's done-when includes them.
- Two-pass inference. `BindFunction` (`Compilation.cs:1103-1135`) binds a body twice and rolls back instances, mutation checks and locals between passes. Mitigation: ported in T11 as one unit, and the typed goldens (`pending-peers-*`, `constrained-*`) are its first checks.
- 007/008 slip. Mitigation: as 009, each fallback is local (one search function per table instead of lambdas, `(i32, i32)` tuple keys instead of struct keys) and recorded in T2; the equivalence target does not change.
- Deep recursion in Bayan. The Bayan binder recurses where C# does; R8's bounds apply to both. Mitigation: T1's depth tests run against `bayan-check` in T8.
- Speed. Map lookups by struct key may cost more than C# reference dictionaries. Mitigation: R11's 10× bound; profile with `--time` before changing the data shape.

## Coverage check

| Req | Tasks |
|---|---|
| R1 | T2, T19, T20 |
| R2 | T2–T15, T18 |
| R3 | T3–T15, T17 |
| R4 | T3, T6, T11 |
| R5 | T3, T6 |
| R6 | T1 |
| R7 | T2, each of T3–T15 for its codes |
| R8 | T1, T8, T11 |
| R9 | T2, T17, T18 |
| R10 | T19, T21 |
| R11 | T20 |
| R12 | T1 |
| R13 | T16 |
| R14 | T1, T17 |
| L018 | T21, T22 |
| Q152–Q164 | T23 |
