---
id: L022
title: Binder equivalence
status: proposed
kind: property
scope: selfhost/semantics/main.byn
proof: tests/Bayan.Tests/SelfhostSemanticsEquivalenceLawTests.cs
check: dotnet test -v q --filter "Law=L022"
timeout: 1800
---

## Statement
For every program that parses without errors, `bayan-check --types` and `bayan types` print the same typed dump, and `bayan-check` and `bayan check` report the same diagnostics and exit with the same code.

## Counterexample looks like
A program that parses without errors on which `bayan-check` and `bayan check` differ in one stderr line (a missing, extra or reordered diagnostic, or one whose code, location, message, label or help differs), on which `bayan-check --types` and `bayan types` differ in one line of the typed dump (a binding's kind, name, span or type, or a function's order), or on which either pair exits with different codes.

## Proof notes
`tests/Bayan.Tests/SelfhostSemanticsEquivalenceLawTests.cs`, fact `BayanCheckEqualsTheCCheckerOnEveryCleanParse` tagged `[Trait("Law", "L022")]`. Three corpora: the diag cases (`Golden.Cases("diag")`) that parse without errors, every typed case (`Golden.Cases("typed")`), and the generator's 39 valid and 40 faulty seed programs (`SelfhostSemanticsGeneratorTests.Corpus(SelfhostSemanticsGenerator.Seeds)`, five fault kinds over eight seeds). Each program is copied alone to `tmp/l022/<corpus>/<NNNNN>/` (a single-file case as itself, a directory case as its whole directory with its `package.byn`, a generated program as `main.byn`), because a check reads the whole directory.

"Parses without errors" is decided by the C# parser: no error in `SyntaxTree.Diagnostics` of any `*.byn` under the program's directory, nor of any tree `Compilation.FromPath` loads (imports included), `package.byn` (the manifest) excepted; L017 makes it the Bayan parser's answer too. The diag cases outside that domain are listed by name in the test output, not dropped silently; the test asserts at least three quarters of the diag cases are in it, and that every typed and generated program is.

Each input is run in both modes, `bayan-check <path>` against `CliApp.Run(["check", path])` and `bayan-check --types <path>` against `CliApp.Run(["types", path])`. The comparison is stderr, stdout and exit code; stdout drops only the timed `◆ checked …` success line (CliApp.cs:447) on both sides. A failure names the input, its copy, the mode, the stream and the first differing line (`SelfhostSemanticsTests.AssertSameLines`). The self-hosted runs go through one POSIX shell per corpus, eight processes at a time, writing `NNNNN.<mode>.{out,err,exit}` under `tmp/l022/<corpus>-out/`. A .NET `Process.Start` per run would dominate the proof. Windows uses the same loop in managed code. The generated corpus is asserted to hold distinct programs, at least one valid program and every fault kind.

Measured on 2026-11-01: diag 434 of 524 cases parse clean and all 434 are compared in both modes. The other 90 hold a `BYN1` parse error and are outside the law. Typed 18 of 18, generated 79 of 79. No input is incomparable, so no known-gap list exists, and every in-domain input is compared (100%). Run time: 5.4 s of test time with the checker cached, 24 s when `bayan-check` is rebuilt first.

Mutation 1 (widened conversion): `selfhost/semantics/conversions.byn:110`, in `isWidening`, `return isUnsignedInteger(a) && isSignedInteger(t) && ab < tb` changed to `ab <= tb`, so `u64` widens implicitly to `i64`. `Law=L022` failed on `diag/bad-binary-operands.byn` (copy `tmp/l022/diag/00013/`), `check`, stderr line 1. Expected ``error BYN2208: `+` cannot be applied to `i64` and `u64` ``, actual ``error BYN2208: `+` cannot be applied to `bool` and `i32` `` (the first diagnostic disappeared).

Mutation 2 (dropped diagnostic): `selfhost/semantics/ownership.byn:423`, the `t24Report(… for: .useAfterMove …)` call for a read after a move, commented out. `Law=L022` failed on `diag/move-narrowed-use-after.byn` (copy `tmp/l022/diag/00272/`), `check`, stderr line 1. Expected ``error BYN2781: `b` is used after it was moved``, actual an empty line.

Each file was copied to `tmp/l022-bt/<file>.orig` first and restored with `cp`. `cmp` was clean after each restore, and the rerun passed.
