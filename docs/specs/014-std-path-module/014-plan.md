# 014 — Plan
Spec: ./014-spec.md
Status: draft, rev 1 (2026-11-04)

## Approach

011 P2 (Q753–Q757) built the `Path` builtin module with three functions: `isRooted`, `relative` and `fileName`. It added C# plumbing, a C runtime with a `_WIN32` branch, and the selfhost checker rows. 014 adds the other three functions the spec names, `combine` (two overloads), `directoryName` and `fullPath`, along the same route. The route runs C# builtin, then effects/regions/panic lists, then `CEmitter.Call`, then `bayan-rt.{h,c}`, then the selfhost checker rows, then the selfhost emitter printer. 014 then moves the selfhost `/`-only path helpers onto the module, which removes the Windows failures listed in `tests/windows-known-failures.txt`. The oracle is .NET `Path.*` on the running OS. It is checked by the seeded corpus in `tests/Bayan.Tests/StdPathTests.cs` on macOS and on wp1, and is never typed by hand.

Order is serial where files overlap:
- C# and runtime, one function per task: `directoryName`, then `combine`, then `fullPath`. All three edit the same `Builtins.cs`/`Symbols.cs`/`CEmitter.cs`/`bayan-rt.c` lines.
- The selfhost checker rows.
- The selfhost emitter printing.
- The run/ and diag/ goldens (D5).
- The R7 site migration: first `compile.byn`, then `typed.byn`/`json.byn`.
- Emptying the Windows list on wp1.
- The law.
- Docs.

R3 (the case rule of `relative`) was met by 011 P2 (Q756, `StdPathTests.CaseTablesMatchDotNet`). 014 only re-checks it through the corpus and L026.

Rejected:
- **One task for all three functions.** It touches ~12 files across C#, C and selfhost. A failing wp1 run then cannot be pinned to one function.
- **Rewriting the selfhost helpers' callers to call `Path.*` directly.** That is about 20 call sites in five files, plus the `unit fullpath` fixture. D7 keeps the helpers and swaps their bodies.
- **Hand-written Windows goldens.** Q757 forbids them.

### Decisions recorded (where the spec left room, the conservative option)

**D1 Names.**
- `BuiltinFunction` gains `PathCombine`, `PathCombineSpan`, `PathDirectoryName` and `PathFullPath`, after `PathFileName` (`Symbols.cs:375-377`, `symbols.byn:94-96`).
- The selfhost keys are `"Path.combine"`, `"Path.combine(span)"`, `"Path.directoryName"` and `"Path.fullPath"`, in the style of the `"Utf8.decode(invalid)"` key.
- Parameter names follow .NET: `path1`/`path2`, `paths` (a read-only `span<string>`), and `path`.
- The tests check that the positional call `Path.combine("/a", "b")` binds, which is how the spec's acceptance line writes it. If it does not bind, that is a Question, not a rename.

**D2 Overload pick.** `Overload()` (`BinderCalls.cs:106-114`) looks only at struct methods and module functions, so builtins get no overload resolution. A single-argument `Path.combine` is therefore rerouted to `PathCombineSpan`. This is a special case in `BinderCalls.cs`, next to the `Utf8DecodeReplace` reroute at `BinderCalls.cs:73-75`. The selfhost twin goes next to `calls.byn:103-104`.

**D3 Effects.** R1 says "pure". The conservative reading is Q753's: no `io`/`fs`/`process`.
- `combine` and `directoryName` return fresh strings from `context.allocator`, so they carry `alloc` like `fileName` and `relative` (Q753).
- `fullPath` carries `alloc` only (spec Q3).
- Each of the three goes into these lists: `EffectAnalysis.cs:267`, the allocated list `RegionAnalysis.cs:565-567`, `BuiltinResults` `RegionAnalysis.cs:613-614` (no borrowed arguments), `PanicHandlerCheck.cs:68`, and the member/Suggest list `BinderExpressions.cs:708-711`.

**D4 Runtime functions.** These are proposals; the writer may rename them and records the final names in its Question.
- `byn_str byn_path_combine(byn_str a, byn_str b, byn_alloc* to)`.
- `byn_str byn_path_combine_many(const byn_str* p, size_t n, byn_alloc* to)`.
- `bool byn_path_directory_name(byn_str p, byn_str* out, byn_alloc* to)`. The emitter wraps it in a `bynh_` helper that returns the `string?` struct, as the `bynh_utf8_try_decode` helper does (`CEmitter.cs:1826-1833`).
- `byn_str byn_path_full_path(byn_str p, byn_alloc* to, const char* file, int line)`. It reuses the static `byn_path_full` (`bayan-rt.c:2044-2047`) and the empty/NUL checks of `byn_path_relative` (`bayan-rt.c:2223`), and panics where .NET throws (Q757).
- `byn_path_full` hard-codes the text "Path.relative: the path cannot be made full". It gains a name parameter, so the `path-relative-*` goldens stay byte-identical.

**D5 Goldens after the selfhost tasks.**
- `SelfhostSemanticsT41Tests.cs:627-628` enumerates every `diag/` and `run/` golden, and `SelfhostEmitterParityTests` runs every `run/` input. A golden that calls `Path.combine` before the selfhost checker and printer know it therefore fails the full suite.
- So T1–T3 test through the `StdPathTests` corpus against live .NET only, and the goldens land in T6, right after T5.
- This departs from the plan criteria's position for goldens (with the C# tasks) only in when they land, not in what they hold.

**D6 Windows-only goldens.** `RunTests.cs:14` and `:46` gain `// os: windows`, which skips a case off Windows, as the mirror of `// os: unix`.
- `run/path-windows-*.byn` holds the spec's acceptance cases (`Path.combine("C:\\a","b")` and `Path.relative("C:\\Src","c:\\src\\x.byn")`).
- Its `.stdout` comes from `BAYAN_UPDATE=1` on wp1, is copied back byte for byte, and is never typed (Q757).
- The Unix acceptance case (`Path.combine("/a","b")`) is `// os: unix`.

**D7 Keep the selfhost helper names.** `t11DirectoryName`, `t11FullPath`, `t11Combine`, `t11RelativePath` (`compile.byn:1269-1342`) and `t26FileName` (`json.byn:34-43`) keep their names and signatures. Only each body changes, to one `Path` call.
- This leaves their callers unchanged: `compile.byn:35-253`, `cli.byn:121-128`, `main.byn:65,91`, `naming.byn:342`, `json.byn:28,30`, and the `unit fullpath` fixture `SelfhostSemanticsT11Tests.cs:212-213,639`.
- A null `directoryName` makes `t11DirectoryName` panic, as C#'s `!` at `Compilation.cs:95` throws.
- `t11ListFiles` (`compile.byn:1264`) is not path arithmetic and is not touched.

**D8 Separator replaces stay at their C# sites.**
- `compile.byn:70` (`Compilation.cs:116`) and `compile.byn:253` (`Compilation.cs:209`) gain `.replaced("\\", by: "/")`, as the C# `.Replace('\\', '/')` does.
- `compile.byn:221`, the segment loop, becomes the `combine(span)` form, as `Compilation.cs:187` does.

**D9 `typed.byn`.**
- `typed.byn:137` becomes `Path.isRooted(path) ? Path.relative(c.displayRoot, to: path) : path`, which is `TypedDump.cs:90`.
- `typed.byn:139-147`, including `:144` `sb.append("/")`, is the port of C#'s `.Replace('\\', '/')` and is not `/`-only code. It stays.

**D10 `ProgramName` trailing separator.** `CliApp.cs:535-540` uses `TrimEnd(Path.DirectorySeparatorChar)`, which has no module function. `json.byn:24-32` therefore keeps `t26TrimEndSlash` (`json.byn:58-63`) but trims the OS separator instead of a fixed `/`.
- Bayan has no OS query (Q571). The separator is taken as the middle byte of `Path.combine("a", "b")`, because .NET `Combine` joins with `DirectorySeparatorChar`. So the function trims `\` on Windows and `/` elsewhere, as C# does.
- If that cannot be expressed exactly, it becomes a Question, never a guess. It is measured against C# `ProgramName` on macOS and wp1.

**D11 `program.byn` is not edited.** `cprint/program.byn:317-318` already calls `Path.isRooted`/`Path.relative`. It panics on Windows only because `t11DirectoryName` hands it an empty `packageRoot` (Q886), and D7 fixes that.
- T9 checks the fix on wp1 with no edit.
- So the 011 mutation baseline entry for `program.byn` (the only entry in `tests/quality-baseline.json`) is untouched.

**D12 List edits ride with the fix.** `scripts/known-failures.py` fails on a listed test that now passes. Each R7 task therefore removes, in its own landing, exactly the names that pass for it on wp1.
- T7 and T8 both edit `tests/windows-known-failures.txt`, so T8 runs after T7.
- T9 removes the rest. A name that still fails is a Question that blocks T9.
- The file keeps its comment header, because `known-failures.py` ignores `#` lines.

**D13 The 014 row of `scripts/selfhost/specs.tsv`.** The row is:
```
014	docs/specs/014-std-path-module	014-tasks.md	014-plan.md	014-api.md	selfhost/semantics/ctx.byn	selfhost/semantics	SelfhostPathT	feat/014-std-path-module	docs(014):	chore(014): land	chore(014): tick	tmp/014-
```
- `014-api.md` does not exist. `wave-brief.py:22-23` and `review-brief.py:18-19` tolerate a missing api file.
- `shared` reuses 010's `ctx.byn` because no field may be empty: `read` with a tab IFS collapses empty fields (`guard.sh:30`). 014 adds no state.
- `bydir` is `selfhost/semantics`, so bare `.byn` names in a task resolve there and emitter files are written in full.
- The base `feat/014-std-path-module` is cut from `feat/011-self-hosted-emitter` (the spec's base).

**D14 Question ranges.** The spec cites Q888, but `docs/bayan-handoff.md` on this branch ends at Q887. The dispatcher therefore assigns each task's five-number range when it cuts the branch, and writes it into the task line as `Q###–Q###` (`wave-brief.py:56-58`).

## Tech / dependencies

- C#:
  - `src/Bayan.Semantics/`: `Builtins.cs:117-119,253-268`, `Symbols.cs:375-377`, `BinderCalls.cs:73-75`, `BinderExpressions.cs:708-711`, `EffectAnalysis.cs:267`, `RegionAnalysis.cs:565-567,613-614`, `PanicHandlerCheck.cs:68`.
  - `src/Bayan.Backend.C/CEmitter.cs:1867-1872`, inside `Call` (`:1726-1944`), not `Builtin` (`:1946`).
- C runtime: `src/runtime/bayan-rt.h:318-323` and `src/runtime/bayan-rt.c`:
  - `byn_path_sep` `:1624`/`:1627`
  - `byn_path_root_length` `:1813`/`:1836` (.NET `GetRootLength`, Q755)
  - `byn_path_full` `:2044` (`GetFullPathNameW` plus `byn_path_expand_short`, Q755)
  - `byn_path_effectively_empty` `:2121`
  - `byn_path_relative` `:2223`
- Selfhost checker (`selfhost/semantics/`, the files Q754 names):
  - `builtins.byn:136-138` (`t9Make`) and `:280-285` (`findPath`)
  - `symbols.byn:94-96`
  - `express2.byn:219-222`
  - `calls.byn:103-104`
  - `effects.byn:533`, `region2.byn:218,285`, `ownership.byn:979`
  - `types.byn:94,175` and `express.byn:892` already handle the `Path` kind and need no change
- Selfhost emitter: `selfhost/emitter/cprint/expr.byn:382-389` (the Path cases). `selfhost/emitter/cprint/builtin.byn` has no Path code. The criteria name it, so T5 checks it and records the finding.
- Tests:
  - `tests/Bayan.Tests/StdPathTests.cs`: `Corpus`, `EachFunctionMatchesDotNet` `:100`, `InvalidUtf8MatchesDotNet` with `tests/native/path-relative-cases.c`, `ShortNamesExpandAsDotNet`, `CaseTablesMatchDotNet`
  - `SelfhostSemanticsT9Tests.cs:54-56`
  - `RunTests.cs:14,46`
  - `tests/cases/emit/run-c.sha256` (`run/path-*` lines at `:187-189`)
- Landing scripts:
  - `scripts/selfhost/land.sh`: the review file must say `Verdict: PASS`, and the script refuses a branch that changes `scripts/selfhost/` or `tests/quality-baseline.json`.
  - `guard.sh`, with the coverage gate on touched selfhost functions.
  - `scripts/known-failures.py` (CI `ci.yml:64`).
- Windows: wp1, the only machine where Windows output and the Windows list are produced.

## Changes by area

Serial order: P1, T1, T2, T3, T4, T5, T6, then T7, T8 and T9 in order. T10 can start after T6; T11 runs after T9 and T10; T12 runs last.
- T1–T3 and T6 are C#/runtime/test tasks. Each lands by the worker gate, the full suite, and a wp1 `known-failures.py` run with 0 new failures.
- T4, T5, T7 and T8 change selfhost `.byn` files and land only through `bash scripts/selfhost/land.sh 014 T<n> feat/014-t<n>` with an independent review.
- T9 edits only the list and lands by a wp1 run.

| # | Task | C# source (lines) | Bayan target | Depends on | Est. lines |
|---|---|---|---|---|---|
| P1 | 014 row in `specs.tsv` | — | `scripts/selfhost/specs.tsv` | — | 1 |
| T1 | `directoryName` (C#, runtime, effects) | .NET `GetDirectoryName`; `Builtins.cs:117-119,253-268`; `CEmitter.cs:1826-1833,1867-1872` | `bayan-rt.{h,c}` | P1 | ~160 |
| T2 | `combine` both overloads | .NET `Combine`; `BinderCalls.cs:73-75` | `bayan-rt.{h,c}` | T1 | ~180 |
| T3 | `fullPath` | .NET `GetFullPath`; `bayan-rt.c:2044-2047,2223` | `bayan-rt.{h,c}` | T2 | ~90 |
| T4 | selfhost checker rows | `Builtins.cs`, `BinderCalls.cs`, `EffectAnalysis.cs`, `RegionAnalysis.cs`, `PanicHandlerCheck.cs` as T1–T3 left them | `builtins.byn`, `symbols.byn`, `express2.byn`, `calls.byn`, `effects.byn`, `region2.byn`, `ownership.byn` | T3 | ~60 |
| T5 | selfhost emitter printing | `CEmitter.cs` Path cases after T3 | `selfhost/emitter/cprint/expr.byn` | T4 | ~40 |
| T6 | run/ and diag/ goldens, `// os: windows` | `RunTests.cs:14,46` | `tests/cases/{run,diag}/path-*` | T5 | ~120 |
| T7 | `compile.byn` helpers on `Path` | `Compilation.cs:93,95,110,111,113,116,187,209`; `Naming.cs:189` | `compile.byn:1269-1342`, `:70`, `:221`, `:253` | T4, T6 | ~-60 |
| T8 | `typed.byn`, `json.byn` | `TypedDump.cs:89-90`; `CliApp.cs:535-540` | `typed.byn:137`, `json.byn:24-63` | T7 | ~-20 |
| T9 | Empty the Windows list | — | `tests/windows-known-failures.txt` | T8 | ~-115 |
| T10 | Enact L026 | — | `docs/laws/026-path-agrees-with-dotnet.md` | T6 | ~40 |
| T11 | Prove L026 | — | `tests/Bayan.Tests/StdPathLawTests.cs` | T9, T10 | ~150 |
| T12 | Docs | — | handoff, `CHANGELOG.md`, `work/todo.md`, 011 docs line cites | T11 | ~40 |

Each new function's line in the 011 P2 plumbing:

| File | `combine` | `directoryName` | `fullPath` |
|---|---|---|---|
| `Builtins.cs` | two `Make` rows (`path1`/`path2` → string; `paths: span<string>` read-only → string), properties, `FindPath` case | `Make` row → `new OptionalType(PrimitiveType.String)`, property, `FindPath` case | `Make` row (`path`) → string, property, `FindPath` case |
| `Symbols.cs` | `PathCombine`, `PathCombineSpan` | `PathDirectoryName` | `PathFullPath` |
| `BinderCalls.cs` | one-argument reroute (D2) | — | — |
| `BinderExpressions.cs:708-711` | member lookup plus Suggest entry | same | same |
| `EffectAnalysis.cs:267` | `alloc` (both) | `alloc` | `alloc` only (spec Q3) |
| `RegionAnalysis.cs:565-567`, `:613-614` | allocated; `[]` borrowed | same | same |
| `PanicHandlerCheck.cs:68` | allocation | allocation | allocation |
| `CEmitter.cs` `Call` | `byn_path_combine(a, b, byn_context)`; span form passes `ptr, len` | `bynh_` optional helper over `byn_path_directory_name` | `byn_path_full_path(x, byn_context, {Loc})` |
| `bayan-rt.{h,c}` | `_WIN32` and POSIX bodies over `byn_path_sep`/`byn_path_root_length` | same, .NET `GetDirectoryName` rules (root returns null; Windows normalizes separators) | `byn_path_full` reused (Q755 `GetFullPathNameW`, short-name expansion), name-parameterized panic |
| `selfhost/semantics/` (Q754 files) | `t9Make` rows `"Path.combine"`, `"Path.combine(span)"`; enum; `findPath`; `express2.byn` suggest; `calls.byn` reroute; `effects.byn`/`region2.byn`/`ownership.byn` lists | rows with `internOptional(ref c, by: prim(.string))` | rows |
| `selfhost/emitter/cprint/expr.byn:382-389` (criteria: `builtin.byn`, which has no Path code) | the two calls | helper call | call with location |

## Data / interfaces

**Bayan surface:**
- `Path.combine(string path1, string path2) -> string uses alloc`
- `Path.combine(span<string> paths) -> string uses alloc`
- `Path.directoryName(string path) -> string? uses alloc` (null for a root or an empty input, as .NET returns null)
- `Path.fullPath(string path) -> string uses alloc`, which panics `Path.fullPath: …` where .NET throws (an empty path, or a NUL byte)

Each returned string is a fresh copy from `context.allocator`, and none borrows an argument.

**C:** the D4 declarations go into `bayan-rt.h` after `:323`, in the same order as `Symbols.cs`.

**Selfhost:** there are no new public functions. The `t11*`/`t26FileName` signatures are unchanged (D7), so `011-api.md` is not affected.

**Tests:** `StdPathTests.EachFunctionMatchesDotNet` gains the four entry points over `Corpus` and `Pairs`. A span corpus of 0–4 `Atoms` is added. Windows rows run on wp1 in the same test.

## Risks & mitigations

- **Windows output cannot be produced on macOS (Q757).** Windows goldens (D6) and every Windows list edit (D12) come only from a wp1 run with `BAYAN_UPDATE=1` or `known-failures.py`. They are copied byte for byte and never typed. T1–T3 compare with live .NET on wp1.
- **`GetFullPathNameW` is already ported (Q755).** `fullPath` reuses `byn_path_full` and `byn_path_expand_short` and does not write a second resolver. The only change is the panic name (D4), and `path-relative-empty`/`-nul` stay byte-identical as the check that the old text survives.
- **Line drift in docs that cite lines.**
  - `CEmitter.cs` and `Builtins.cs` grow, so `011-api.md` (for example `CEmitter.cs:388`, `:1946`) and `011-plan.md` cites shift.
  - Edits to `expr.byn` shift the rows in `011-c-inventory.md`. `CInventoryTests` checks file:line hits, so it fails loudly.
  - T1–T3 and T5 re-point every cite they move, and T12 sweeps the rest.
- **011 mutation baseline.** The only emitter file 014 edits is `cprint/expr.byn` (T5). It has no entry in `tests/quality-baseline.json` yet; 011 T16's batch run sets one.
  - If T16 has run, T5 reruns `scripts/selfhost/mutate-batch.sh` on `expr.byn` and does not land below it.
  - If T16 has not run, it measures the new lines too.
  - `program.byn` is not edited (D11).
- **.NET `GetDirectoryName` on Windows normalizes `/` to `\` and keeps UNC roots.** The corpus, not the writer, decides each case, with drive-relative `C:x` and `\\server\share` in `Atoms`.
- **Public selfhost functions may need effect clauses.** The helpers gain `alloc` through `Path`. `t26Compile` already declares `uses alloc, clock, console, fs` (`cli.byn:119`), but a private helper reached from a function without `alloc` reports BYN6002, and the guard's build catches it.
- **`land.sh` has no wp1 step (Q886).** The dispatcher runs wp1 `known-failures.py` before each landing of T4–T9.
- **011 tasks still open (T14 and later)** may add names to the list, or cite Q888 from another branch. T9 empties only the names in the file at its base, and a name added later goes back to 011.
- **Coverage gate.** Every touched selfhost function needs 100% line and branch coverage (`guard.sh`). The swapped helper bodies are one line each, and in `t26TrimEndSlash` (D10) the separator is a value read at run time, not a branch per OS, so the macOS runs cover every line.

## Coverage check

| Requirement | Where |
|---|---|
| R1 combine, fileName, directoryName, isRooted pure (no io/fs/process) | T1, T2 (D3); `fileName`/`isRooted` from 011 P2 (Q753); re-checked by T6 diag and L026 |
| R2 fullPath reads cwd like relative, alloc only, fresh string | T3 (D3, D4), T6 `path-full-path*`, diag `path-needs-alloc` |
| R3 relative case rule per OS | 011 P2 (Q756); re-checked by T1–T3 corpus and L026 (T11) |
| R4 Windows separators, drive letters, UNC; `/` only elsewhere | T1–T3 runtime `_WIN32` branch; wp1 corpus; T6 `// os: windows` goldens (D6) |
| R5 each function matches its .NET call | T1–T3 `StdPathTests`; L026 (T10, T11) |
| R6 run/ goldens per function, diag/ where it can fail, Windows cases in the Windows job | T6 (D5, D6) |
| R7 selfhost `/`-only sites call the module, via land.sh | T4, T5, T7, T8 (D7–D11); list emptied in T9 (D12) |

## Draft law

```
---
id: L026
title: Path agrees with .NET
status: proposed
kind: property
scope: src/runtime/bayan-rt.c (byn_path_*), src/Bayan.Semantics/Builtins.cs (Path members), src/Bayan.Backend.C/CEmitter.cs (Path calls)
proof: tests/Bayan.Tests/StdPathLawTests.cs
check: dotnet test -v q --filter "Law=L026"
timeout: 300
enacted:
---

## Statement
For every input in a seeded corpus of separators (`/`, and `\` on Windows), drive letters (`C:\x`, `C:x`), UNC roots, `.`/`..` and empty segments, a compiled Bayan program calling `Path.combine` (both forms), `Path.fileName`, `Path.directoryName`, `Path.isRooted`, `Path.fullPath` and `Path.relative` prints what .NET's `Path.Combine`, `GetFileName`, `GetDirectoryName`, `IsPathRooted`, `GetFullPath` and `GetRelativePath` return on the same OS. Where .NET throws, the program panics with a nonzero exit.

## Counterexample looks like
An input and function name, the Bayan output, and the .NET result (or exception) on the same OS, for example `directoryName("C:\\")` printing `C:\` where .NET returns null.

## Proof notes
`BAYAN_LAW_TIER`: unset runs a fixed seeded subset sized to about 3 minutes; `full` runs the whole corpus. The proof runs on macOS and on wp1. It reuses the `StdPathTests` corpus generator and runs every function's whole corpus in one program per seed.
```
