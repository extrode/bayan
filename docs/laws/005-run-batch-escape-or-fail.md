---
id: L005
title: Io.runBatch passes every argument unchanged or runs nothing
status: proposed
kind: property
scope: src/runtime/bayan-rt.c
---

## Statement
On Windows, for every batch file and every list of argument strings, `Io.runBatch(path, args)` either starts the batch file with exactly `args.length` arguments, each equal as a Unicode string to `args[N-1]` when the batch file reads them with `%~1` and a `shift` loop under code page 65001, or fails with an `IoError` without starting any process. This holds regardless of the user's `cmd.exe` AutoRun and DelayedExpansion registry settings. An argument containing CR, LF or NUL always fails. On other platforms, `Io.runBatch` always fails with `IoError.unsupported`.

## Counterexample looks like
`Io.runBatch(".\\echo-args.cmd", ["a\"&calc"])` that starts `calc`; a batch file that receives `a"` and `calc` as two arguments, or whose `%~1` is `a^&calc`; `["%PATH%"]` or, with DelayedExpansion enabled in the registry, `["!PATH!"]` whose `%~1` is the expanded path; a tenth argument that arrives changed; `["é"]` that arrives as `Ã©`; `["a\nb"]` that runs; or `Io.runBatch` on Linux that returns anything but `unsupported`.
