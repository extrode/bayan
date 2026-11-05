# 014 — Standard-library `Path` module
Status: done
Created: 2026-11-02
Revised: 2026-11-04 (open questions answered by the user; state after 011 P2; base branch feat/011-self-hosted-emitter)

## Problem
The self-hosted checker (spec 010) needs the path operations the C# compiler takes from .NET `Path.*`
(Compilation.cs:93-116, 187-209; TypedDump.cs:90; Naming.cs:189). A Bayan program has no way to ask which
OS it runs on and no working directory (Q571), so the port treats only `/` as a separator and only a `/`
prefix as rooted. On Windows that gave 76 CI failures (Q743, run 41318084615); by 011 T14 the self-hosted emitter tests raise
the count to 116 (`tests/windows-known-failures.txt`, Q816, Q886, Q888). User decision 2026-11-02:
Q743 (b), a `Path` module in the standard library, implemented per OS, which the port calls. No
`Target.os` query for now.

Already built by 011 P2 (Q753–Q757): `Path.isRooted`, `Path.relative` and `Path.fileName`, in the C#, the C runtime (with
the `_WIN32` branch and per-OS case tables) and `selfhost/semantics/`. 014 adds `combine`, `directoryName` and `fullPath`
and moves the selfhost `/`-only code onto the module.

## Goals
- The selfhost port computes the same paths as the C# on Linux, macOS and Windows, with no OS test in Bayan code.
- Every test in `tests/windows-known-failures.txt` passes on Windows once the port calls the module, and the list is empty.

## Non-goals
- A target/OS query (`Target.os`), path globbing, file-system queries beyond the current directory.
- Changing the C# compiler's own path handling.
- Normalising paths the C# leaves alone (e.g. collapsing `..` where .NET does not).

## User stories
- As the selfhost port, I want `Path.combine`/`fullPath`/`relative`/`directoryName`/`fileName`/`isRooted`, so that the paths I print match the C# on every OS.
- As a Bayan program author, I want path joins that use the host's rules, so that a tool written once runs on Windows too.

## Requirements
- R1 `Path.combine(a, b, …)`, `Path.fileName(p)`, `Path.directoryName(p)` and `Path.isRooted(p)` are pure: no effect, same result for the same input on a given OS.
- R2 `Path.fullPath(p)` reads the current working directory as `Path.relative` does and, like it (Q753), carries `alloc` only, no io/fs/process effect; it returns a fresh string.
- R3 `Path.relative(from, to)` gives the path from `from` to `to`; it compares path segments with the same case rule as .NET's `Path.GetRelativePath` on that OS (case-insensitive on Windows, as the user asked; the macOS and Linux rule is measured from .NET in /plan, not assumed).
- R4 On Windows every function accepts both `\` and `/` as separators and understands drive letters (`C:\x`, `C:x`) and UNC paths (`\\server\share\x`); off Windows only `/` separates.
- R5 For every input, each function returns what the matching .NET `Path.*` call returns on the same OS (`Combine`, `GetFileName`, `GetDirectoryName`, `IsPathRooted`, `GetFullPath`, `GetRelativePath`), including its "no directory" result for a root.
- R6 Each function has `run/` golden cases and, where it can fail, `diag/` cases; Windows-only cases run in the Windows CI job.
- R7 The selfhost `/`-only path code calls the module instead, landing through scripts/selfhost/land.sh: the `t11*` path helpers in `compile.byn` (about lines 1270–1340 on the base branch: directory name, full path, combine, relative), `typed.byn:137` and `json.byn:35`. `typed.byn:139-147` and `selfhost/emitter/cprint/program.byn:317-318` stay: they port C#'s `.Replace('\\', '/')` (`TypedDump.cs:90`, `CEmitter.cs:392`), which runs on every OS. The emitter's Windows panic ("the path is empty", Q757, Q888) comes from the empty package root the `/`-only directory-name helper gives, so it ends with that helper's migration.

## Acceptance criteria
- Given `Path.combine("C:\\a", "b")` on Windows, when run, then it prints `C:\a\b`; on Linux `Path.combine("/a", "b")` prints `/a/b`.
- Given `Path.relative("C:\\Src", "c:\\src\\x.byn")` on Windows, then it prints `x.byn`.
- Given `Path.fullPath` called from a function that declares no `alloc`, then the compiler reports the missing `alloc` effect, as for `Path.relative`.
- Given the Windows CI job after R7, then every test on `tests/windows-known-failures.txt` passes and the list holds no test names.

## Law candidates
- L0xx `Path` agrees with .NET: for every path string (seeded random corpus of separators, drive letters, UNC prefixes, `.`/`..`, empty segments), each `Path` function returns what the matching .NET call returns on the same OS.

## Decisions (user, 2026-11-04)
- Q1 `combine(string, string)` plus `combine(span<string>)`; no variadic parameters.
- Q2 `directoryName` returns `string?`: null for a root, as .NET.
- Q3 `fullPath` carries `alloc` only, matching `Path.relative` (Q753).

## Open questions
- None.
