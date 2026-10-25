---
id: L014
title: Capture by copy never aliases the source
status: proposed
kind: guard
scope: src/Bayan.Semantics/Binding/BinderExpressions.cs
proof: tests/Bayan.Tests/L014CaptureByCopyTests.cs
check: dotnet test --filter Law=L014
---

## Statement
A table of capture sites and the verdict each must get. A `var`, a `var` pattern binding, a `ref`/`out`/`in` parameter and `this` (explicit, or implicit through an unqualified field or method) are BYN2821; a `unique` value is BYN2780; a borrow of a `var` that is then modified while the lambda lives is BYN3004; a `let` of a copyable type or a plain parameter is accepted, and the program prints the value the capture had when the lambda was created, whatever happens to the source afterwards (a later `let` shadow, dropping the source's scope, reassigning the `var` the `let` was copied from).

## Counterexample looks like
`fill(out i64 total)` with `apply(x => x + total)` in its body compiling with no diagnostic; or `let c = source` captured by `held = () => c` inside a block, then `source = 3` after the block, and `held()` printing `3` instead of `1`.

## Proof notes
`tests/Bayan.Tests/L014CaptureByCopyTests.cs`, tagged `[Trait("Law", "L014")]` on the class. `RejectedCaptureGetsItsCode` compiles one small program per rejected row in-process and requires the diagnostic codes to be exactly `[code]`: a `var`, a `var` pattern binding in a `switch` arm, `ref`, `out` and `in` parameters, `this.count`, an unqualified field `count` and an unqualified method call `twice()` (BYN2821); a `List<i64>` (BYN2780); a `let` view of a `var StrBuf` captured and then `buf.clear()` while the lambda is used later (BYN3004); a capturing lambda created in one `for` iteration and held in an outer `var` into the next (BYN3002, plan risk *Environment temp per evaluation*).

`AcceptedCapturesPrintTheirCreationValue` compiles `tests/cases/run/lambda-capture-by-copy.byn` (also a run golden), requires no diagnostics at all, builds `out.c` standalone for the debug and safe emissions in the `BuildStandalone` pattern of L012 with `-std=c11 -fwrapv -Wall -Werror` (runtime with `-w`), runs it, and checks every row and scenario. One row per copyable kind: `i64`, `i32`, `u8`, `f64`, `bool`, `rune`, `string`, a struct, an enum, a union, `i64?`, `(i64, string)` and `i64 -> i64`. Each row copies a `var source` into `let c` in a block, assigns `held = () => c` to a `var` outside the block, then prints `held()` after `let c = "shadow"` (a same-scope shadow of another type), after the block ends and a sibling block declares `let c` with another value, and after `source` is reassigned; each line must start with the creation value. A plain parameter row captures `p` in a block and calls it after the block and through a function, for two arguments. With no C compiler found the build half writes `skipped: no C compiler found` and returns. `ReadingTheSourceFails` shows the comparison can fail.

Deviation from the task line (handoff Q370): a nested-scope `let` shadow is BYN2007 in Bayan (a local never hides another), so the shadow scenario uses a same-scope redeclaration of a different type (the same type would be warning BYN2028), and the nested scope declares the name only after the source's scope ended. A parameter cannot be shadowed or have its scope end before the lambda, and the caller's `var` cannot change during the call, so the parameter row checks only the read after a block.

Mutations:
- Captures by pointer instead of by value (`CEmitter`: the environment became `(void*[]){&l2_c}` and the lambda read `*(T*)((void**)env)[i]`): the test passed, every row printed its creation value. Not killable here: the emitter hoists every local, temp and environment tuple to its own function-scope C slot, a captured `let` or parameter is never written after the lambda is created, and the only way to re-run a declaration while a lambda lives (a loop iteration) is BYN3002 (a lambda held from one iteration into the next), so a pointer to the source always reads the creation value. The by-copy property is therefore held by the checker's rules (BYN2821, BYN3002, BYN3004), not by the copy alone; this mutation shows the copy is not observable today, not that the law is broken.
- Environment never filled (`Lowerer`: the `EmitAssign` of the captures tuple removed): `AcceptedCapturesPrintTheirCreationValue` failed (`Debug C does not build under -Wall -Werror: variable 'l2_c' set but not used`); built with the toolchain's `-w`, the program exits 133 with no output.
- `out`/`in` parameters dropped from BYN2821 (`BinderExpressions.Capture`: `Modifier: not ParamModifier.None` narrowed to `ParamModifier.Ref`): the `out parameter` and `in parameter` rows failed with `expected exactly BYN2821, got:` and no diagnostics.

Reverted, the test passes. No flag narrowing: `-Wall -Werror` holds in full on `out.c`.

Measured 2026-10-25 on macOS arm64: 12 tests, about 25 s.
