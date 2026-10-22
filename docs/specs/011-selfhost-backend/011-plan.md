# 011 — Plan
Spec: ./011-spec.md
Written 2026-10-22 against `d462122` (spec 010's docs on top of dev). Line numbers below are at that commit; round-3 steps 5.3a–5.4 move them and add methods, and T1 re-reads and re-sizes them.

## Approach

Port `src/Bayan.Lowering` and `src/Bayan.Backend.C/CEmitter.cs` the way specs 001, 009 and 010 ported the front end: one Bayan file per C# region, one Bayan method per C# method, same names in camel case, same order of work, and a harness that builds the Bayan program once and diffs its output against the C# reference on every input the repository has plus a generated corpus. The port starts after every round-3 step that changes lowering or emission has landed (Depends-on line), and after 010, whose pools it reads.

The C# work comes first (T1), as in 009 and 010: make the one iterated `Dictionary` in the emitter explicit, show that `EmitC` is deterministic, find out whether the back end overflows on deep inputs the checker accepts (BYN2850), and turn every C# throw on an error-free input into a fixed bug. Without that, "byte-identical on every input" could fail for reasons no port can fix.

The comparison text is the C translation unit (Q165), in debug and in safe, because the two differ only in the ownership checker (`CEmitter.OwnershipChecks`). The C compiler and the runtime stay out of the comparison: `run/` already checks what the C# output does, and identical text does the same.

The lowered program is its own pools (Q166). The C# lowerer returns bound records unchanged where it has nothing to rewrite and adds 23 lowered records; the Bayan lowerer copies into `LExprKind` / `LStmtKind`, whose cases are the bound kinds that reach `FunctionEmitter.Statement` and `Expr` plus the lowered ones. 010's pools are read by id and only its local pool grows, by the temps `NewTemp` makes.

The emitter's C text is built as C# builds it: per-section buffers (`bodies`, `protos`, `errors`, `statics`, `types`) joined in `run`'s order. Every C# `StringBuilder` becomes a `StrBuf`, every `string.Join` a loop with a separator, every `Dictionary` a `Map` with the order list C# already keeps beside it.

Lowering and emission are interleaved in C# only at the program level (`Lowerer.Lower`, then `CEmitter.Emit`), so the port has no end-to-end output until both the lowerer (T4, T5) and the function emitter (T6) exist. T3 ports the emitter's type-level half first, checked through a `--types` listing, so T6 starts on a tested base.

## Tech

- Language: Bayan in round-3 syntax for `selfhost/backend/`; C# for T1 and the harness.
- Uses from 006: `for x in pool.items` on 009's `Pool<T>`; the 010 helpers constrained by interfaces, unchanged.
- Uses from 007: `BoundNode -> bool` predicates with `it` for `Any` / `Moves` (`Lowerer.cs:242-257`) and variant filters (`CEmitter.cs:311`, `:459`); no capture of `this` or a `var`; no function value in a field, so `Capture(Action)` (`Lowerer.cs:103-116`) is a push and pop of the output list.
- Uses from 008: `Symbol` for names; `Map<string, …>` for the emitter's tables, `Map<LocalId, string>` for C names, `Map<FunctionId, i32>` for test indices (008 R5); insertion order (L003) as the only order.
- Uses from 010: `Checker` (modules, functions, tests, enums, error variants, builtins, package root), `TypeId` and its display names, the bound pools, `LocalId` and the local pool.
- Build: `Compilation.Create(entryFiles, packageRoot: selfhost/backend, …)`, then `CliApp.EmitC(c, BuildMode.Fast)` and `Toolchain.Build`, as `tests/Bayan.Tests/SelfhostLexerTests.cs:21-28` does; `BAYAN_CACHE=tmp/cache`.
- No new NuGet packages, no runtime (`src/runtime/`) change, no change to `selfhost/lexer/`, `selfhost/parser/` or `selfhost/semantics/` (except R8's conditional bound).

## Changes by area

### C# back end (T1)

- Order: `CEmitter._files` (`CEmitter.cs:31`, iterated at `:95`) gets a `_fileOrder` list filled in `FileName` (`:185-197`), as `_typeOrder` and `_helperOrder` are; `:95` iterates the list. The other 17 `Dictionary`/`HashSet`/`SortedDictionary` sites in `Lowerer.cs` (6) and `CEmitter.cs` (11) are lookup-only or sorted (`Lowerer.cs:1123` is a `SortedDictionary`); the list with verdicts goes into T1's commit message and a comment at the top of `tests/Bayan.Tests/SelfhostEmitTests.cs`.
- Determinism: a test in `tests/Bayan.Tests/EmitDeterminismTests.cs` (new) runs `EmitC` twice, in debug and safe, on every error-free `.byn` of R10 and every `run/` case, and asserts identical text.
- Depth: a test emits a function whose body is a chain of 100,000 `1 +` terms and one of 100,000 nested conditional expressions, each through `Compilation.Create` and `EmitC` in a thread with the default 1 MB stack. If either overflows in `Lowerer` or `CEmitter` but checks without error, R8 applies: with 010's BYN2841 allocated, its threshold in `Binder.BindExpr` (`BinderExpressions.cs:17`) and in `selfhost/semantics/expressions.byn` drops to the deepest level the back end survives, rounded down to a power of two; without it, BYN2850 is added at the same entry, ported to `selfhost/semantics/`, and L018 is re-run. If neither overflows, BYN2850 stays reserved.
- Throws: the determinism test fails on any exception; each one found is fixed with a `run/` golden (R14).
- Literals: a test feeds `FloatLiteral` (`CEmitter.cs:738-745`) the table of R7's acceptance values plus 1,000 doubles and floats from a fixed seed and records the outputs as `tests/Bayan.Tests/float-literals.txt` (a plain-text golden the Bayan side is checked against in T3).
- Size: `wc -l` of `src/Bayan.Lowering/*.cs` and `src/Bayan.Backend.C/CEmitter.cs` after round 3; a region over about 900 lines gets its own task, and the methods round 3 added are named for T8.

### Bayan back end (T2–T8)

New directory `selfhost/backend/`, one file per C# region:

| Bayan file | C# source | Task |
|---|---|---|
| `main.byn` | `CliApp.EmitC` (`src/Bayan.Cli/CliApp.cs:371`), the test path (`:561-562`), `RunTests`' mode line (`tests/Bayan.Tests/RunTests.cs:72-74`) | T2 |
| `lowered.byn` | `LoweredNodes.cs` (108) | T2 |
| `program.byn` | `Lowerer.Lower` (`Lowerer.cs:13-30`), `CEmitter` fields, `Run`, `EmitMain` (`CEmitter.cs:14-159`) | T2 |
| `names.byn`, `ctypes.byn` | `CEmitter.cs:160-344` (names, file names, C types, keys, the type table, helpers) | T3 |
| `ownership-c.byn` | `CEmitter.cs:345-430` (storage parts, own claim, set, move, return) | T3 |
| `equality.byn`, `maps.byn`, `format.byn` | `CEmitter.cs:431-647` (int suffixes, `Equal`, key hash and equality, `MapMethod`, `Format`) | T3 |
| `statics.byn`, `literals.byn` | `CEmitter.cs:648-746` (enum tables, const arrays, string escapes, constants, `FloatLiteral`) | T3 |
| `lower.byn` | `Lowerer.cs:32-312` (state, temps, labels, `Capture`, spills, operand ordering, cleanups) | T4 |
| `lower-statements.byn` | `Lowerer.cs:313-703` (statements, assignment, return, `if`, loops, `for`, switch) | T4 |
| `lower-expressions.byn` | `Lowerer.cs:704-1100` and `:1201-1342` (lvalues, expressions, short circuit, conditional, coalesce, collections, spreads, slices, calls, builtin calls, writes, switch expressions) | T5 |
| `lower-failure.byn` | `Lowerer.cs:1343-1509` (failing calls, `try`, `catch`, patterns, claims) | T5 |
| `function-emitter.byn` | `CEmitter.cs:749-1092` (`FunctionEmitter` state, signature, locals, ownership slots and the dry run, statements, return, write) | T6 |
| `emit-expressions.byn` | `CEmitter.cs:1093-1637` (expressions, calls, builtins, unary, binary, conversions, index, slice) | T6 |
| `power-assert.byn` | `Lowerer.cs:1101-1200` (`Assert`, `Shown`, `IsLiteral`), `PowerAssert.cs` (78) | T7 |
| methods round 3 added | named by T1 (typestate tag layout, checked-struct construction, `provide`, `defer` guard, `except` prologue, 5.4 builtins), each in the file of its region | T8 |

### Harness (T2, T9–T14)

- `tests/Bayan.Tests/SelfhostEmitTests.cs` (new), modelled on 010's `SelfhostCheckTests.cs`:
  - a `Lazy` build of `bayan-emit` from the lexer, parser and semantics library files plus `selfhost/backend/*.byn`;
  - `Reference(path, mode)`: `Compilation.Create` as `RunTests.Execute` does (`.test.byn` with tests), then `CliApp.EmitC(c, mode)`, or `CEmitter.Emit(Lowerer.Lower(c), debug)` on the test path; a C# exception fails the row as R14 says;
  - `[Theory]` over `Golden.Cases("run")` × {the case's mode, the other ownership setting};
  - `[Theory]` over every error-free `.byn` under `tests/cases/typed`, `examples`, `selfhost`, `tools` and the `tests/cases/new` scaffolds, × {debug, safe};
  - a `[Fact]` for self-reach (R13);
  - a `[Fact]` comparing `bayan-emit --types` with `CType` and `Key` over the types of every `run/` case (T3);
  - a `[Fact]` comparing the Bayan float formatter with `tests/Bayan.Tests/float-literals.txt`;
  - the pending list `tests/Bayan.Tests/selfhost-emit-pending.txt`, rows `<case> <mode>`: a listed row is expected to differ, and a listed row that matches fails with "remove `<row>` from the pending list";
  - `[Fact, Trait("Law","L019")]` over the corpus (Q174) through `--many`, plus stdin runs for a sample of 200;
  - the timing `[Fact]` honouring `BAYAN_SKIP_TIMING`.
  - On a mismatch the message names the input and mode and prints the first differing line of each side with its line number.

### Docs (T1, T15, T16)

- If allocated: `docs/diagnostics/BYN2850.md`, `tests/cases/diag/expression-too-deep-to-emit.{byn,diag}`.
- `docs/laws/019-emitter-equivalence.md` (T15).
- `docs/bayan-handoff.md`: Q165–Q177 under Questions, row 011 marked done.
- `docs/bayan-self-hosting.md`: phase 3 notes the C emitter as ported.
- `CHANGELOG.md`: one entry.

## Data and interfaces

### New diagnostic codes

| Code | Name | Message | Label | Help | Fix |
|---|---|---|---|---|---|
| BYN2850 | `ExpressionTooDeepToEmit` | expression is nested more than {0} levels deep | too deep to compile | split the expression into named locals | none |

Allocated only if T1 shows a back-end overflow and 010 did not allocate BYN2841 (R8); otherwise reserved. BYN2850–2859 is 011's block: dev uses BYN2700–2791; specs 006, 007 and 008 use BYN2800–2819, 2820–2829 and 2830–2839; 009 allocates BYN1070 and keeps 1071–1079; 010 keeps BYN2840–2849; the round-3 plan allocates BYN3201–3209, 5001–5008, 6001–6007, 7001–7004 and 8001 and its input forms in BYN1040–1062. A search of every worktree's `docs/` and `src/` at this commit finds no `BYN285`. BYN2851–2859 stay spare.

### Lowered nodes (round-3 syntax)

```bayan
struct LExprId {
    index: i32
}

struct LStmtId {
    index: i32
}

enum WriteSink {
    print
    panic
    strBuf
}

union LStmtKind {
    block(statements: IdRange)
    local(local: LocalId, value: LExprId)
    expr(value: LExprId)
    label(name: string)
    goto(label: string)
    loop(body: LStmtId)
    ret(value: LExprId?)
    returnError(code: LExprId)
    failingCall(error: LocalId, call: LExprId, result: LocalId?)
    write(sink: WriteSink, target: LExprId?, parts: IdRange, guards: IdRange)
    panic(message: string)
    claim(local: LocalId)
}

struct LStmtNode {
    span: TextSpan
    kind: LStmtKind
}

struct LoweredFunction {
    function: FunctionId
    locals: List<LocalId>
    contextSlots: i32
    panicSlots: i32
    body: LStmtId
}
```

`LStmtKind` shows the shape, not the list: T2 adds one case per `LoweredNodes.cs` statement record and per bound statement `FunctionEmitter.Statement` (`CEmitter.cs:924-1031`) accepts, and `LExprKind` likewise for `Expr` (`:1095-1178`). `IdRange` is 009's. The lowered pools live on a `Lowered` struct beside 010's `Checker`, which the lowerer takes by `ref` only to append temps.

### Emitter state

```bayan
struct Emitter {
    ownershipChecks: bool
    packageRoot: string
    types: Map<string, TypeId>
    typeOrder: List<string>
    helpers: Map<string, Helper>
    helperOrder: List<string>
    statics: Map<string, string>
    staticOrder: List<string>
    files: Map<string, string>
    fileOrder: List<string>
    hashes: bool
    testIndex: Map<FunctionId, i32>

    fileName(ref this, path: string) -> string {
        let relative = relativeTo(packageRoot, path)
        if files.contains(relative) {
            return files[relative]
        }
        let name = $"byn_file_{files.length}"
        files[relative] = name
        fileOrder.append(relative)
        return name
    }
}

struct Helper {
    proto: string
    body: string
}
```

`relativeTo` is Q170's reduction of `Path.GetRelativePath`. `FunctionEmitter` is a second struct holding `names: Map<LocalId, string>`, the indent, the current `StrBuf` and the dry-run sets (`reads`, `moves`, `unsequenced`, `moved` as `Map<LocalId, bool>` used as sets, lookup-only as in C#).

### CLI

`bayan-emit [--mode debug|safe|fast] [<path>|-] | --many <dir> | --time <path> | --types <path>`; diagnostics are always `Style.Plain`; exit 0 / 1 / 2 / 4 as spec R1. `--types` prints, for every type 010 interned in reaching order, its display name, `CType` and `Key`, one per line; it is a harness aid and is not documented outside this plan.

## Risks

- Round-3 drift. 5.3a–5.4 add tag layouts, construction functions and `provide` to both files. Mitigation: 011 starts after them (T1 depends on 5.4), T1 re-sizes the tasks and names the new methods for T8, and a later back-end change is a C# change first that fails L019 until ported (Q173).
- Float text. .NET's `"R"` and a shortest-digit search in Bayan can disagree on ties or subnormals. Mitigation: T1's 1,000-value table and the corpus constants; a disagreement is resolved on the C# side first (Q169).
- Paths. `GetRelativePath` handles cases Q170's reduction does not (symlinks, case-insensitive volumes, Windows roots). Mitigation: the harness passes the same absolute path and root to both sides and runs on macOS and Linux; a miss shows as an L019 mismatch in the `byn_file_` table.
- Shared mutable state. `NewTemp` appends to 010's local pool; a stale `LocalId` or a pool read during append would misname a temp. Mitigation: temps are appended only through one `Lowered.newTemp`, and the dry-run emitter reads names only through `FunctionEmitter.names`.
- Size. About 3,300 C# lines before round 3, with dense string building. Mitigation: each task ports one region; compiler bugs found on the way are fixed in C# with their own golden first and named by the task.
- 007/008/010 slip. Mitigation: as 009 and 010, each fallback is local (one search function per predicate, tuple keys) and recorded in T2; the equivalence target does not change.
- Deep recursion in Bayan. The Bayan lowerer and emitter recurse where C# does; R8's bound applies to both. Mitigation: T1's depth tests run against `bayan-emit` in T6.
- Speed. String building through `StrBuf` and `Map<string, …>` lookups for helpers may cost more than C#. Mitigation: R12's 10× bound; profile with `--time` before changing the data shape.

## Coverage check

| Req | Tasks |
|---|---|
| R1 | T2, T12, T13 |
| R2 | T2–T8, T11 |
| R3 | T3–T8, T10 |
| R4 | T2, T4, T6 |
| R5 | T4, T5 |
| R6 | T2, T6, T10 |
| R7 | T1, T3 |
| R8 | T1, T6 |
| R9 | T2, T10 |
| R10 | T11 |
| R11 | T12, T14 |
| R12 | T13 |
| R13 | T9 |
| R14 | T1, T10 |
| R15 | T1 |
| L019 | T14, T15 |
| Q165–Q177 | T16 |
