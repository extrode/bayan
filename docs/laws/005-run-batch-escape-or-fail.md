---
id: L005
title: Io.runBatch passes every argument unchanged or runs nothing
status: enacted
kind: property
scope: src/runtime/bayan-rt.c
proof: tests/Bayan.Tests/L005RunBatchTests.cs
check: dotnet test --filter Law=L005
enacted: 2026-10-21
---

## Statement
On Windows, for every batch file and every list of argument strings, `Io.runBatch(path, args)` either starts the batch file with exactly `args.length` arguments, each equal as a Unicode string to `args[N-1]` when the batch file reads them with `%~1` and a `shift` loop under code page 65001, or fails with an `IoError` without starting any process. This holds regardless of the user's `cmd.exe` AutoRun and DelayedExpansion registry settings. An argument containing CR, LF or NUL always fails. On other platforms, `Io.runBatch` always fails with `IoError.unsupported`.

## Counterexample looks like
`Io.runBatch(".\\echo-args.cmd", ["a\"&calc"])` that starts `calc`; a batch file that receives `a"` and `calc` as two arguments, or whose `%~1` is `a^&calc`; `["%PATH%"]` or, with DelayedExpansion enabled in the registry, `["!PATH!"]` whose `%~1` is the expanded path; a tenth argument that arrives changed; `["é"]` that arrives as `Ã©`; `["a\nb"]` that runs; or `Io.runBatch` on Linux that returns anything but `unsupported`.

## Proof notes
`tests/Bayan.Tests/L005RunBatchTests.cs`, three tests tagged `[Trait("Law", "L005")]`:
- `EveryCaseIsRefusedOrReadBackUnchanged` runs 33 hand-written edge cases plus 4,000 argument lists generated from `Random(5005)` through a native driver over `byn_batch_cmdline`. Each case is either refused, or its command line is parsed by a model of `cmd.exe` and the batch file's `%~1`/`shift` reads, and every argument comes back unchanged.
- `ModelReadsKnownCommandLines` checks the model itself against 21 known command lines.
- `RunBatchIsUnsupportedOffWindows` checks that `Io.runBatch` returns `unsupported` off Windows.

Limits: AutoRun and DelayedExpansion are covered only through the `/d` and `/v:OFF` prefix the runtime always passes. No test starts a real `cmd.exe`, and code page 65001 and the UTF-16 conversion are not exercised. The driver caps at 16 arguments, and the generator stays under that at 12. Break-test: removing `%` from the refused set fails the property.
