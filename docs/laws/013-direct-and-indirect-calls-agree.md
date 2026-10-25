---
id: L013
title: Direct and indirect calls agree
status: enacted
kind: property
scope: src/Bayan.Backend.C/CEmitter.cs
proof: tests/Bayan.Tests/L013DirectAndIndirectCallsAgreeTests.cs
check: dotnet test --filter Law=L013
enacted: 2026-10-25
---

## Statement
For every program in the lambda program generator's grammar (named functions and lambdas of zero to two parameters over `i64`, `bool` and `string`, non-capturing or capturing `let` locals and plain parameters, called directly, through a function value in a local, and through a function value passed through one or two user functions), the direct and the indirect calls print the same output; and every generated program the checker accepts compiles with the C compiler without errors or warnings under `-Wall -Werror` (compiled standalone, since the build toolchain silences warnings).

## Counterexample looks like
A lambda `p0 => p0 + k0` capturing `let k0: i64 = 9`, where the direct section prints `c0 12` for `p0 = 3` while `f(3)` through a local prints `c0 3`, because the environment pointer points at a temp the captures were never copied into; or emitted C for an accepted program that `cc -Wall -Werror -c out.c` rejects.

## Proof notes
`tests/Bayan.Tests/L013DirectAndIndirectCallsAgreeTests.cs`, tagged `[Trait("Law", "L013")]`, generates seeds 1..48 with `LambdaGenerator` (raised by `BAYAN_L013_SEEDS`). Grammar (full form in the generator's doc comment): 3 to 6 cases, each a function type of 0 to 2 parameters over `i64`, `bool` and `string` with a result of one of them, and a value of that type that is a named function (`R fn0(P p0, to P p1)`), a non-capturing lambda, or a lambda capturing plain parameters `q0`, `q1` of its host function and `let k0: P = …` locals of it (at least one read by the body); expression or block (`{ return … }`) bodies, `it` for a one-parameter lambda at an argument. Each case has `call0(…, by (T) f) => f(…)` and `relay0(…, by (T) f) => call0(…, by: f)`, and four host functions that evaluate it four ways and print `c<i> <result>`: `direct` (the named function called by name, or the lambda's body with its parameters bound by `let`, since a lambda has no name to call), `local` (`let f: T [uses alloc] = value` then `f(args)`), `via1` (`call0(args, by: value)`) and `via2` (`relay0(args, by: value)`, or by a local holding it). `main` prints the four sections, calling each host. For each seed the test requires no diagnostics in-process, then for both the debug and the safe emission builds `out.c` standalone in the `BuildStandalone` pattern of `OwnershipCheckerTests` with `-std=c11 -fwrapv -Wall -Werror` (the runtime `bayan-rt.c` is compiled separately with `-w`, since the law is about the emitted program), runs it, and requires exit 0 and the four sections to be the same text. It also asserts every grammar choice appears in some seed. With no C compiler found it writes `skipped: no C compiler found` and returns, since xunit 2 has no runtime skip. `DisagreeingSectionsFail` shows the comparison can fail.

No flag narrowing: `-Wall -Werror` holds in full on `out.c`, and no `CEmitter.cs` change was needed for lambda emission. Three generator shapes are left out, each recorded in the generator: an unread `let` (it emits an unread C local, `-Wunused-but-set-variable`; pre-existing emission, not lambda emission), the same operand on both sides of a comparison or a `&&`/`||` (`-Wtautological-compare` on `p0 == p0`, `-Wtautological-negation-compare` on `!k1 && k1`, the second found by the 200-seed soak), and a `T uses alloc` local passed to a `by (T) f` parameter (BYN2202 `expected () -> string, found () -> string uses alloc`: R16 relaxes effects for a function or lambda passed to a parameter, not for a value of a bounded type; via2 passes a local only when its type has no `uses`).

Mutation 1 (stale environment): in `Lowerer.Expr`'s `BoundLambda` case, a second tuple temp was assigned the environment temp before the captures were stored in it, and the `LFunctionValue` pointed at that copy. The test failed on 41 of 48 seeds, each `local section differs from direct (Debug)`, e.g. seed 1: `--- direct` `c0 12`, `c1 true`, `c3 13-14` and `--- local` `c0 3`, `c1 false`, `c3 0-0`. Pointing at a fresh, never-assigned temp instead also fails, through `-Wall` (`variable 't2' set but not used`, the real environment is never read). Reverted, the test passes.

Mutation 2 (skipped capture field): in `CEmitter`'s function emitter, each capture after the first was mapped to `item1` instead of `item<N+1>`. The test failed on 12 of 48 seeds (only a lambda with two or more captures is affected): 7 `local section differs from direct (Debug)`, e.g. seed 3: `c3 true-false` against `c3 true-true`, and 5 `Debug C does not build under -Wall -Werror`, e.g. seed 1: `invalid operands to binary expression ('bool' and 'byn_str')`. Reverted, the test passes.

Measured 2026-10-25 on macOS arm64 with `zig cc`: 48 seeds, all accepted and agreeing, 40 s; soak at 200 seeds, all accepted and agreeing, 3 min 30 s.
