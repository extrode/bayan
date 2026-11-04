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
