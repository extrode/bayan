---
id: L012
title: Static and dynamic dispatch agree
status: enacted
kind: property
scope: src/Bayan.Backend.C/CEmitter.cs
proof: tests/Bayan.Tests/L012InterfaceDispatchTests.cs
check: dotnet test --filter Law=L012
enacted: 2026-10-22
---

## Statement
For every program in the interface program generator's grammar (interfaces with abstract and default methods, conforming structs, calls through constraints and through `dyn ref`), calling a method through a constraint and through `dyn ref` on the same value gives the same observable output as calling it directly on the value; and every generated program the checker accepts compiles with the C compiler without errors or warnings under `-Wall -Werror` (compiled standalone, since the build toolchain silences warnings).

## Counterexample looks like
Two structs `S00` and `S01` conforming to `IGen0`, where `rwDyn0(v)` on an `S00` prints `m2 14` (the value of `S01.m2`) while `rwS00(ref v)` prints `m2 9`, because the `S00` vtable's slot for `m2` points at `S01`'s method; or emitted C for an accepted program that `cc -Wall -Werror -c out.c` rejects.

## Proof notes
`tests/Bayan.Tests/L012InterfaceDispatchTests.cs`, tagged `[Trait("Law", "L012")]`, generates seeds 1..48 with `InterfaceGenerator` (raised by `BAYAN_L012_SEEDS`). Grammar (full form in the generator's doc comment): 1 or 2 interfaces, each with 2 to 4 abstract members (`i64 m(i64 x)`, `i64 m { get; }`, `string m()`, `void m(ref this, i64 by)`) and 1 or 2 default methods (a `string` or `i64` body calling the abstract members by bare name); 2 or 3 conforming structs per interface with two `i64` fields, arithmetic witnesses, mutators, and an override of each default with p = 0.4. One call sequence of 4 to 8 steps per interface runs on every conformer six ways: by value through the concrete type, `where T : I` and `dyn ref readonly I` (the sequence without `ref this` calls), and by reference through `ref S`, `ref T where T : I` and `dyn ref I` (the whole sequence), then prints the fields. `main` prints the direct, constraint and dyn sections from a fresh value each time. For each seed the test requires no diagnostics in-process, then for both the debug and the safe emission builds `out.c` standalone in the `BuildStandalone` pattern of `OwnershipCheckerTests` with `-std=c11 -fwrapv -Wall -Werror` (the runtime `bayan-rt.c` is compiled separately with `-w`, since the law is about the emitted program), runs it, and requires exit 0 and the three sections to be the same text. It also asserts every grammar choice appears in some seed. With no C compiler found it writes `skipped: no C compiler found` and returns, since xunit 2 has no runtime skip. `DisagreeingSectionsFail` shows the comparison can fail.

No flag narrowing: `-Wall -Werror` holds in full on `out.c`. The first run failed every seed on one pre-existing warning, `unused function 'byn_w_error' [-Wunused-function]` (the error-name writer is emitted into every program and used only when one prints an error); fixed locally in `CEmitter.cs` by declaring it `static __attribute__((unused))` (`static inline` is not enough, clang warns for unused inline functions in the main file). No other warning appeared.

Mutation: in `CEmitter.Vtable`, every slot of a conformer's vtable was pointed at the next conformer's witness (with a matching cast, so the C still builds). The test failed on all 48 seeds, e.g. seed 1: `dyn section differs from direct (Debug)` with `--- direct` starting `m2 9`, `m0 8`, `m0 8`, `m3 17` and `--- dyn` starting `m2 14`, `m0 -5`, `m0 -8`, `m3 8`. Reverted, the test passes.

Measured 2026-10-22 on macOS arm64 with `zig cc`: 48 seeds, all accepted and agreeing, 46 s.
