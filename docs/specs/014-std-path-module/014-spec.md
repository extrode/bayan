# 014 — Standard-library `Path` module
Status: draft
Created: 2026-11-02

## Problem
The self-hosted checker (spec 010) needs the path operations the C# compiler takes from .NET `Path.*`
(Compilation.cs:93-116, 187-209; TypedDump.cs:90; Naming.cs:189). A Bayan program has no way to ask which
OS it runs on and no working directory (Q571), so the port treats only `/` as a separator and only a `/`
prefix as rooted. On Windows that gives 76 CI failures (Q743, run 41318084615). User decision 2026-11-02:
Q743 (b), a `Path` module in the standard library, implemented per OS, which the port calls. No
`Target.os` query for now.

## Goals
- The selfhost port computes the same paths as the C# on Linux, macOS and Windows, with no OS test in Bayan code.
- The 76 Q743 Windows failures pass once the port calls the module.

## Non-goals
- A target/OS query (`Target.os`), path globbing, file-system queries beyond the current directory.
- Changing the C# compiler's own path handling.
- Normalising paths the C# leaves alone (e.g. collapsing `..` where .NET does not).

## User stories
- As the selfhost port, I want `Path.combine`/`fullPath`/`relative`/`directoryName`/`fileName`/`isRooted`, so that the paths I print match the C# on every OS.
- As a Bayan program author, I want path joins that use the host's rules, so that a tool written once runs on Windows too.

## Requirements
- R1 `Path.combine(a, b, …)`, `Path.fileName(p)`, `Path.directoryName(p)` and `Path.isRooted(p)` are pure: no effect, same result for the same input on a given OS.
- R2 `Path.fullPath(p)` reads the current working directory, so it carries the process/fs effect (a call from a function without that effect is a compile error, as for other `Io` reads).
- R3 `Path.relative(from, to)` gives the path from `from` to `to`; it compares path segments with the same case rule as .NET's `Path.GetRelativePath` on that OS (case-insensitive on Windows, as the user asked; the macOS and Linux rule is measured from .NET in /plan, not assumed).
- R4 On Windows every function accepts both `\` and `/` as separators and understands drive letters (`C:\x`, `C:x`) and UNC paths (`\\server\share\x`); off Windows only `/` separates.
- R5 For every input, each function returns what the matching .NET `Path.*` call returns on the same OS (`Combine`, `GetFileName`, `GetDirectoryName`, `IsPathRooted`, `GetFullPath`, `GetRelativePath`), including its "no directory" result for a root.
- R6 Each function has `run/` golden cases and, where it can fail, `diag/` cases; Windows-only cases run in the Windows CI job.
- R7 The selfhost sites listed in Q743 (compile.byn:1271, :1287, :1330, :1337; typed.byn:137; json.byn:35) call the module and lose their `/`-only code, landing through scripts/selfhost/land.sh.

## Acceptance criteria
- Given `Path.combine("C:\\a", "b")` on Windows, when run, then it prints `C:\a\b`; on Linux `Path.combine("/a", "b")` prints `/a/b`.
- Given `Path.relative("C:\\Src", "c:\\src\\x.byn")` on Windows, then it prints `x.byn`.
- Given `Path.fullPath` called from a function without the process/fs effect, then the compiler reports the missing effect.
- Given the Windows CI job after R7, then the 76 Q743 tests pass and the known-failures list drops them.

## Law candidates
- L0xx `Path` agrees with .NET: for every path string (seeded random corpus of separators, drive letters, UNC prefixes, `.`/`..`, empty segments), each `Path` function returns what the matching .NET call returns on the same OS.

## Open questions
- Q1 Function names and the variadic shape of `combine` (C# `Combine(params string[])`; the port calls it with 2 parts and with a list at Compilation.cs:187). Conservative default meanwhile: `combine(string, string)` plus `combine(span<string>)`.
- Q2 The "no directory" result of `directoryName` for a root (.NET returns null): `string?` or an empty string. Conservative default: `string?`, matching .NET.
- Q3 Effect name for `fullPath`: the existing process/fs effect used by `Io.readFile`, or a narrower one. Conservative default: the same effect as `Io.readFile`.
