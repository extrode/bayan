---
id: L009
title: The typed dump is deterministic
status: enacted
kind: property
scope: src/Bayan.Semantics/Binding/TypedDump.cs
proof: tests/Bayan.Tests/TypedDumpTests.cs
check: dotnet test --filter Law=L009
enacted: 2026-10-21
---

## Statement
For every program, the typed dump (`bayan types`) is byte-identical across two runs, and across a run on a copy of the program in another directory with the same relative path. Spec 010 builds the binder-equivalence law on it.

## Counterexample looks like
Two runs of `bayan types` on the same file whose dumps differ in the order of functions or bindings (a hash-set iteration leaking into the output), or a dump of a copy under another directory whose `path` fields carry the absolute directory.

## Proof notes
`tests/Bayan.Tests/TypedDumpTests.cs`, theory `DumpIsDeterministic` tagged `[Trait("Law", "L009")]`, runs once per case in `tests/cases/typed/*.byn` and `tests/cases/run/*.byn` (77 cases on 2026-10-21: 5 typed, 72 run). Each dump is a fresh `Compilation.FromPath` of the file by absolute path, as `bayan types file.byn` does for a script, so source paths are absolute and reach `TypedDump.PathOf`; the case is dumped twice in place and once from a copy at `tmp/l009/<guid>/<same repo-relative path>`, and all three must be equal. A multi-module case (a subdirectory with `main.byn`) is compiled as a whole directory and copied with its directory; none exist today. A case with errors would be compared all the same, since `TypedDump.ToJson` accepts any compilation; measured 2026-10-21, all 77 compile without errors. Mutation: writing `fn.File!.Source.Path` instead of `PathOf(c, ...)` in `TypedDump.cs` made all 77 cases fail; restored. Limits: the cases are the golden corpus, not generated programs; imports of sibling directories outside the case directory are not copied.
