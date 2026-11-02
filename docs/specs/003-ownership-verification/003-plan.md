# 003 — Plan
Spec: ./003-spec.md

## Approach

Four deliverables, built in this order: `take()` (binder, lowering, emitter, goldens), `switch (move x)` (binder and OwnershipAnalysis only, the parser already accepts it), the debug-only runtime checker (runtime side table plus emitter hooks, gated by a mode flag threaded into the emitter), then the generator and law proofs. The static-check-off switch is a `CompilationOptions` field. Decisions I would pick, and what changes my mind:

- `take()` is a `Mutating` builtin method row per receiver type, so R2's unwritable-place errors fall out of the existing `CheckWritable` call at `BinderCalls.cs:454`. Lowering is `tmp = move place; place = default; tmp`, reusing `BoundMove` and `BoundDefault` (emits `((T){0})`, `CEmitter.cs:856-857`). Not `len = 0` as `clear()` does (`CEmitter.cs:1094-1095`): that would leave `ptr` shared, which is exactly the alias the checker exists to catch.
- `switch (move x)` needs no parser change: `ParseSwitchStatement` calls `ParseParenCondition` which is `ParseExpression` (`Parser.cs:988-991`, `906-912`), and `move` is a unary prefix (`Parser.cs:1428-1430`), so `SwitchStmt.Value` is already a `MoveExpr`. The binder binds the subject through `BindMove`, binds patterns in an "owning" mode so bindings get `BorrowsSubject = false`, and OwnershipAnalysis already marks the subject moved because `case BoundSwitch` runs `Expr(sw.Value, state)` (`OwnershipAnalysis.cs:185-187`) and `BoundMove` marks `Moved` (`277-281`).
- Runtime checker: owner records in a side table in `bayan-rt.c` keyed by the allocation pointer (`ptr` of a List, `keys` of a Map, the StrBuf buffer), values `{place address or NULL = in flight, file, line, type name}`. Moved-from locals use a per-local shadow slot declared next to the hoisted C local, not the table. Empty values (`NULL` storage) are never recorded; this matches §9.3 "each unique allocation records its current owner" and avoids the fact that every fresh `List` is `{0}`.
- R8 as written (byte-identical safe/fast C, and "no checker symbol in its C") is achievable only by making the emitter mode-aware. `CEmitter.Emit(LoweredProgram)` has no mode (`CEmitter.cs:50`) and `CliApp.EmitC(Compilation)` neither (`CliApp.cs:367`). I pick threading a `bool ownershipChecks` through `Emit` and `EmitC` (call sites `CliApp.cs:367, 410, 444, 491-492`, `RunTests.cs`). The macro alternative (hooks that expand to nothing unless `BYN_DEBUG`) would keep the API but puts checker identifiers in every mode's C text, failing the acceptance line "has no checker symbol in its C". What changes my mind: if the dispatcher forbids changing `EmitC`'s signature, use macros and reword R8 (see end).
- Generator: one C# class with its own splitmix64 PRNG (not `System.Random`, whose seeded sequence is not guaranteed stable across .NET versions), generating by construction from an abstract ownership state so rejection stays low, batching `size` functions per seed into one program.

## Tech / dependencies

- C#/.NET 10, xunit, existing `Toolchain` (`Toolchain.cs:109`, flags `139-145`, cache key `146`, one `cc` invocation compiles `main.c` and `bayan-rt.c` together so a `-D` flag reaches both).
- Debug flags gain `-DBYN_DEBUG` (`Toolchain.cs:139-145`); all runtime checker code sits under `#ifdef BYN_DEBUG` in `bayan-rt.{h,c}`, mirroring the existing `BYN_FAST` guards (`bayan-rt.h:253-279`).
- ASan: `/usr/bin/clang -fsanitize=address` on this mac; zig cc has no ASan runtime. `Toolchain.Build` gets an optional `sanitize` parameter that selects `/usr/bin/clang` when it exists and adds `-fsanitize=address -fno-omit-frame-pointer`; the flags are already part of the cache key. `BAYAN_CC` is process-global so the test must not rely on it.
- New diagnostic codes BYN2788 and BYN2789 (next free after `BYN2787`, `DiagnosticCodes.cs:181-189`; 2762 retired, not reused). Each needs `docs/diagnostics/BYN####.md` starting `# BYN####:` and a `diag/` golden (`DiagnosticCodesTests.cs:17-49`).

## Changes by area (path, change, R#)

take():
- `src/Bayan.Semantics/Binding/BoundNodes.cs:48-71` | add `BuiltinMember.ListTake, MapTake, StrBufTake, OptionalTake` | R1
- `src/Bayan.Semantics/Binding/BuiltinMembers.cs` Method table | rows `(ListType, "take")`, `(MapType, "take")`, `(BuiltinType{Kind: StrBuf}, "take")`, `(OptionalType whose element TypeFacts.IsUnique, "take")`, all `Mutating: true`, `Returns` = receiver type; add `"take"` to `Candidates` (`71-72`). Verify `HasMethod` (`BinderExpressions.cs:623`) agrees and that `OptionalType` receivers reach `BoundBuiltinGroup` (`BinderExpressions.cs:621-624`); the element-type property name on `OptionalType` is unverified | R1
- `src/Bayan.Semantics/Binding/BinderExpressions.cs:621-624` | when `name == "take"` and no row matches, report BYN2788 `TakeUnsupported` instead of BYN2003 | R2
- `src/Bayan.Semantics/Binding/BinderCalls.cs:449-471` | nothing new: `CheckWritable` at `454` yields the existing cannot-mutate diagnostic for `let` locals, read-only params, `ReadOnlySpan` elements | R2
- `src/Bayan.Semantics/Binding/BinderCalls.cs:498-509` | field and element rows of the BYN2782 message table: help becomes "its owner keeps it; take it with `.take()`, or copy it with `.copy(to: alloc)`" when `BuiltinMembers.Method(type, "take")` is non-null, else today's text | R3
- `src/Bayan.Semantics/Binding/OwnershipAnalysis.cs:399-414` | `IsPlace` returns false for the four take members (the result is a fresh value, storing it is not BYN2780) | R1
- `src/Bayan.Lowering/Lowerer.cs` (builtin lowering near `964-966`, `Spill` at `163-169`, `NewTemp` at `85`) | lower take to `LSequence([LAssignExpr(tmp, new BoundMove(place)), LAssignExpr(place, new BoundDefault(place.Type))], tmp)`; follow whatever the mutating path does for `lists[i].clear()` receivers so the place is evaluated once | R1
- `src/Bayan.Backend.C/CEmitter.cs` | no new case needed for take itself; `BoundMove` and `BoundDefault` emission cover it | R1
- `src/Bayan.Syntax/Diagnostics/DiagnosticCodes.cs:181-189` | `TakeUnsupported = Error("BYN2788", "`take()` needs a `List`, `Map`, `StrBuf` or an optional of a unique type, not `{0}`")` | R2
- `docs/diagnostics/BYN2788.md`, `docs/diagnostics/BYN2782.md` (mention take) | R2, R3
- `tests/cases/diag/take-unsupported.diag`, `diag/take-immutable.diag`, update `diag/unique-move-from-field.diag` help line | R2, R3
- `tests/cases/run/take.byn` + `.stdout`: list field, map, StrBuf, optional, element, through `ref`, place reused after | R1

switch (move x):
- `src/Bayan.Semantics/Binding/Binder.cs:660-685` and `BinderCalls.cs:602-640` | if `s.Value is MoveExpr m`: bind `m.Operand`, require `IsMovable`-style operand (local `Let`/`Var` with `BorrowsSubject` false, or `move` parameter) else report BYN2789 `SwitchMoveSubject` and fall back to borrowing; wrap with `BindMove` so the bound value is a `BoundMove`; set a binder field `_patternOwns = true` around `BindPattern` | R4, R5
- `src/Bayan.Semantics/Binding/BinderCalls.cs:655, 827` | `BorrowsSubject = !_patternOwns` | R4, R5
- `src/Bayan.Semantics/Binding/OwnershipAnalysis.cs:395-396` | `IsMovable` also accepts `LocalKind.PatternVariable` with `BorrowsSubject: false` so `keep(move list)` passes; no change at `185-200`/`302` since the subject `BoundMove` already marks `x` moved on every path | R4
- `src/Bayan.Lowering/Lowerer.cs:768-769` | keep the `BoundMove` node (`new BoundMove(m.Syntax, Expr(m.Operand))`) instead of stripping it, so the emitter can see transfers; `Switch` at `578-626` and `Match` at `1096-1167` unchanged | R4, R6
- `src/Bayan.Backend.C/CEmitter.cs` | `case BoundMove m` emits `Expr(m.Operand)` when checks are off (same C as today) | R4, R8
- `DiagnosticCodes.cs` | `SwitchMoveSubject = Error("BYN2789", "`switch (move …)` needs a local or a `move` parameter, not {0}")`; `docs/diagnostics/BYN2789.md`; `tests/cases/diag/switch-move-field.diag` | R4
- `tests/cases/run/switch-move.byn` + `.stdout`; `diag/switch-move-use-after.diag` (BYN2781 on later `r`) | R4
- RegionAnalysis: no reference to `BorrowsSubject` (grep in `RegionAnalysis.cs` found none), so owning bindings keep today's span rules | R5

Static-check switch and mode threading:
- `src/Bayan.Semantics/Compilation.cs:11` | `CompilationOptions(bool IncludeTests = false, bool Library = false, bool SkipOwnershipCheck = false)`; `:782` becomes `if (fn.Body is not null && !fn.IsInstance && !Options.SkipOwnershipCheck)` | R11
- `src/Bayan.Backend.C/CEmitter.cs:50` | `Emit(LoweredProgram program, bool ownershipChecks = false)`; `FunctionEmitter` carries the flag | R6, R8
- `src/Bayan.Cli/CliApp.cs:367, 410, 444, 491-492` | `EmitC(Compilation c, BuildMode mode)` passes `mode == BuildMode.Debug`; `bayan build --emit-c` therefore prints debug hooks only in debug mode | R8
- `tests/Bayan.Tests/RunTests.cs` and other `EmitC` callers | pass the case's mode | R8
- `src/Bayan.Backend.C/Toolchain.cs:139-145` | Debug flags `-O0 -g -DBYN_DEBUG`; optional `sanitize` parameter (compiler `/usr/bin/clang` if present, `-fsanitize=address -fno-omit-frame-pointer`) | R6, R10

Runtime checker:
- `src/runtime/bayan-rt.h` | under `#ifdef BYN_DEBUG`: `typedef struct { const char* file; int32_t line; } byn_own_slot;` and prototypes `byn_own_claim(void* storage, void* place, const char* type, const char* file, int line)`, `byn_own_release(void* storage, byn_own_slot* slot, const char* file, int line)`, `byn_own_read(const byn_own_slot* slot, const char* type, const char* file, int line)`, `byn_own_rekey(void* old_storage, void* new_storage)` | R6, R7
- `src/runtime/bayan-rt.c` | under `#ifdef BYN_DEBUG`: open-addressing table keyed by storage pointer; `claim` panics "second owner of {type}: owned since {file}:{line}" via `byn_panic_begin`/`byn_panic_end` (`bayan-rt.c:28-54`, exit 134) when the record's place is non-NULL and differs from the new place; `release` sets place NULL and fills the slot; `read` panics "use of moved {type}: moved at {file}:{line}" when the slot's line is nonzero; `rekey` called from `byn_list_reserve` (`367-371`), `byn_map_rehash` (`420-438`), writer grow (`115-119`) and the StrBuf grow sites (`633`, `670`) | R6, R7
- `src/Bayan.Backend.C/CEmitter.cs:703-707` | when checks on, declare `byn_own_slot own_<name> = {0};` for each local or parameter whose type has a unique part (`TypeFacts.UniquePart`) and is not `LocalKind.Temp` | R6
- `CEmitter.cs:745-746` (`BoundAssign`) and `912-913` (`LAssignExpr`) | when checks on and the target is a claiming place (local `Let`/`Var`/owning `PatternVariable`/`move` parameter, or a field or element path), after the store emit `byn_own_claim` per unique part path and zero the local's slot; stores into `Temp`, `ForVariable` and `BorrowsSubject` locals are not claims. Targets are emitted through a new `Place()` helper so the read hook below is not applied to lvalues | R6, R7
- `CEmitter.cs` new `case BoundMove m` | checks on: `(byn_own_release(storage, &own_x, Loc), <operand>)` per unique part; operands that are not locals (fields, elements, `switch` subject temps) release with a NULL slot | R6
- `CEmitter.cs:858-859` (`BoundLocal`) | checks on and the local has a slot: `(byn_own_read(&own_x, "type", Loc), x)` | R7
- `tests/cases/run/unique-checker-clean.byn` with `// mode: debug` first line (`RunTests.cs` mode line, as `run/overflow-fast.byn`) exercising moves in loops with reassignment, `return move`, `switch (move)`, `take()`, struct literals with `move`, so hooks run on an accepted program | R6
- `tests/Bayan.Tests/OwnershipCheckerTests.cs` | hand-written programs compiled with `SkipOwnershipCheck: true`, built `BuildMode.Debug` (pattern of `RuntimeNativeTests.cs`), asserting exit 134 and stderr `panic: second owner of List<i64>: owned since c.byn:3\n  at c.byn:5\n` (second variable appends through both), and the moved-from read case; a third test asserts `EmitC(c, BuildMode.Fast)` and `Safe` contain no `byn_own` and are equal | R7, R8, R11

Generator and laws:
- `tests/Bayan.Tests/OwnershipGenerator.cs` | `static string Generate(ulong seed, int size)`; splitmix64 PRNG; grammar in the file's doc comment and in L008 proof notes | R9
- `tests/Bayan.Tests/L008OwnershipRuntimeTests.cs` `[Trait("Law","L008")]` | one `[Fact]`: seeds 1..N (N default 24, `size` 16, `BAYAN_GEN_SEEDS` env raises N), compile each, count rejected, build accepted in parallel with `Toolchain.Build(..., BuildMode.Debug, sanitize: asan)` (warm-up pattern of `RunTests.cs:1-80`, `BAYAN_CACHE` so unchanged programs are free on reruns), run sequentially, assert exit 0 and empty stderr, fail if rejected > 50%, print counts and whether ASan ran; a second `[Fact]` asserts `Generate(1,8)` equals a committed hash and is stable across two calls | R9, R10
- `docs/laws/008-unique-never-aliased-at-runtime.md` | add `proof:`/`check: dotnet test --filter Law=L008` and `## Proof notes` recording which half (checker, ASan) ran; status flip is the user's "enact" | R10
- `docs/laws/006-unique-never-aliases.md` and `tests/Bayan.Tests/L006OwnershipGuardTests.cs` | Statement gains "…with `take()` in place of a move out of a field…"; Accepted rows: `take()` from field, element, optional, through `ref`; `switch (move s)` with `consume(move items)`; Rejected rows: `switch (s)` then `consume(move items)` BYN2783, `switch (move s)` then read of `s` BYN2781, `switch (move ledger.items)` BYN2789, `take()` on `let` local (existing cannot-mutate code), `take()` on `i64` BYN2788. Update proof notes row counts | R12
- `docs/bayan-handoff.md:475-479` | answer Q64, Q65, Q66 | all

## Data / interfaces

- Bayan: `x.take()` on `List<T>`, `Map<K,V>`, `StrBuf`, `U?` where `U` is unique; returns the receiver type, leaves `{0}`/null. `switch (move x) { … }` and `x switch { … }` form with `move`.
- `CompilationOptions.SkipOwnershipCheck` (test-only, no CLI flag).
- `CEmitter.Emit(LoweredProgram, bool ownershipChecks)`, `CliApp.EmitC(Compilation, BuildMode)`, `Toolchain.Build(cSource, mode, output, target, sanitize)`.
- Runtime record: `{void* storage; void* place; const char* type; const char* file; int32_t line;}` in a table under `BYN_DEBUG`; per-local `byn_own_slot`. Locations use the existing `Loc(syntax)` string `"<file const>, <line>"` (`CEmitter.cs:719-721`).
- Panic texts (two locations each, existing format `bayan-rt.c:28-54`):
  ```
  panic: second owner of List<i64>: owned since c.byn:3
    at c.byn:5
  panic: use of moved List<i64>: moved at c.byn:4
    at c.byn:6
  ```
- Generator grammar (abstract state per variable: Owned, Moved, Borrowed; kind let/var; type):
  ```
  program    := prelude helpers fn{size} main          ; main calls every fn
  helpers    := consume(move List<i64>) peek(List<i64>) bump(ref List<i64>) step() fails E
  fn         := "void g<k>() {" "using let arena = Arena();" stmt{size} "}"
  stmt       := decl | move | copy | take | call | borrow | loop | branch | switchMove | reassign | read
  decl       := ("let"|"var") x "=" (List<i64>() | Ledger{items=…} | Slot.Full(…) | List<i64>? …)
  move       := "var y = move x;"            if x Owned and not Borrowed
  copy       := "let y = x.copy(to: arena);"  if x Owned
  take       := "let y = p.take();"           p ∈ {x.items, x[i], o} with x var/Owned
  call       := consume(move x) | peek(x) | bump(ref x)
  borrow     := "{ let v = x.asSpan(); print($\"{v.length}\"); }"
  loop       := for/while whose body moves x only if it reassigns x before the end
  branch     := if/else; join = Moved if moved on either side
  switchMove := "switch (move s) { case .Full(let items): consume(move items); default: }"
  reassign   := "x = List<i64>();"            resets Moved
  read       := print($"{x.length}")           if x Owned
  probe (p = 0.1) := a deliberately illegal step (use after move, implicit copy)
  ```

## Risks & mitigations

- Reallocation changes the storage key: `rekey` hooks at the four growth sites; a missed site shows up as a false "second owner" in `run/unique-checker-clean.byn` and the generator.
- Lowerer temps and borrowed bindings must not claim: rule keyed on `LocalKind`/`BorrowsSubject` above; a wrong rule panics on accepted programs, which the debug run golden catches.
- Read hook on lvalues: `BoundLocal` is emitted for assignment targets and `&local` ref arguments too; the `Place()` split keeps hooks off those. Watch `LFailingCall` results (`CEmitter.cs:775-779`) and `Return` paths.
- Unique parts inside union payloads and container elements are not claimed per element (only released via `move`/`take`); aliasing inside a container with the static check off goes undetected. State this in L008 proof notes.
- `OptionalType` builtin rows do not exist today (`BuiltinMembers.cs` has no `OptionalType` match); member dispatch for optional receivers must be checked before relying on `BoundBuiltinGroup`.
- Time budget: compile cost per generated program is unmeasured; start at 24 seeds x 16 functions, measure, and tune; cache makes reruns cheap. The generator must not iterate a `Dictionary` for output order.
- ASan default exit on report is nonzero and stderr nonempty, so the assertion covers both; set `ASAN_OPTIONS=detect_leaks=0` since values are never freed by design.
- Process: editing L006's Statement and flipping L008 to enacted are outside the agent-editable fields in `docs/laws/README.md`; do them only under the standing authorization cited in `002-tasks.md` T12 or ask for "enact".

## Coverage check

- R1: take() area (BuiltinMembers rows, Lowerer sequence, run/take.byn)
- R2: take() area (CheckWritable at BinderCalls.cs:454, BYN2788, diag goldens)
- R3: take() area (BinderCalls.cs:498-509 help text, BYN2782 doc and golden)
- R4: switch (move x) area (Binder.cs:660-685, BinderCalls.cs:602-640, IsMovable, BYN2789, goldens)
- R5: switch (move x) area (`_patternOwns` default false keeps BorrowsSubject true)
- R6: Runtime checker area (table, slots, claim/release hooks, debug run golden)
- R7: Runtime checker area (read hook, panic texts, OwnershipCheckerTests)
- R8: Static-check switch and mode threading area (Emit flag, EmitC mode, equality test); see rewording note
- R9: Generator and laws area (OwnershipGenerator.cs, grammar, determinism test)
- R10: Generator and laws area (L008OwnershipRuntimeTests.cs, sanitize parameter)
- R11: Static-check switch area (SkipOwnershipCheck at Compilation.cs:782, OwnershipCheckerTests)
- R12: Generator and laws area (L006 statement and rows)

Spec rewordings applied with this plan (R2 unwritable place reuses the mutation diagnostic; R6 "every unique allocation"; R8 kept as byte-identical C, met by threading the build mode into the emitter; R10 N = 24 seeds of 16 functions, `BAYAN_GEN_SEEDS` raises it).

Law process: L006 statement amendment and L008 enactment go in the same change as their passing proofs, under the standing authorization of 2026-10-21 (handoff "Road to self-hosting").
