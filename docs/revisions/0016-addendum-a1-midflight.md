# Bayān — Mid-flight addendum A1 (during 010–013)

Nov 1, 2026

Erratum (2026-11-05): K2's example named the runtime header `bayan_rt.h`; the file in the tree is `src/runtime/bayan-rt.h` (addendum A2, `docs/revisions/a2-lessons-mid-flight.md`, Errata for A1).

## Ground rules

Only items that change no language behavior and no emitted output may land while 010, 013 and the self-hosting steps run. Everything else waits for round 4, which is a separate document.

An item qualifies only if all of these hold:

1. The golden suite, `lex-diff` and the 010 parity checks give the same results before and after.
2. It touches only docs, the diagnostic registry, or the 011 and 012 specs, which are not drafted yet.
3. It lands as one small commit between task merges, so no running writer rebases over it.

Hand it to the agent as `docs/revisions/` addendum A1, or fold it into the 011 spec brief when that spec is drafted.

## Two "decide now" items are already past their window

The earlier triage marked two items as decisions to make before steps they depended on. Both steps have landed, so both move to round 4.

| Item | Was due before | What landed | Round-4 action |
| --- | --- | --- | --- |
| Closure escape rule (parameters borrowed by default, `move` to store) | 007 lambdas | 007 is done | Ask the agent how 007 lowers a function-typed parameter. If it is already `{fn, env*}` or monomorphized, the rule is a checker addition only. |
| Variant-sized payload layout | 5.3h | Tag plus largest payload | Keep it. Add the size-ratio lint in round 4; it is additive and changes no layout. |

One consequence matters for 011. The Bayān emitter ports the C# emitter's lowering, so any later layout or closure-lowering change must be made twice. Settle both questions in round 4 before 011 is drafted, or accept a second port.

## Safe to land now

These are documentation and registry changes only.

- [ ] Mark 006 and 007 as Done in the handoff's spec table.
- [ ] Check that review merges `56133ca` and `f572298` cover the round-3 range, then close the note at `work/todo.md:230`.
- [ ] Write the missing pages for BYN1056, BYN1057 and BYN7005 (`work/todo.md:141-144`).
- [ ] Reserve, without implementing, the round-4 diagnostics in the registry, each at the next free number in its category: `FinalStateNotReached`, `SwitchExpressionNeedsElse`, `NegatedIsInput`, `BorrowedClosureStored`. This keeps the self-hosted diagnostic tables from colliding later.
- [ ] Naming: change the title line of README, SPEC, GRAMMAR, STYLE and HANDOFF to "Bayān", and add one line to README and STYLE: *Bayān (ASCII: Bayan). The CLI, package names, URLs and identifiers use `bayan`.* Leave the prose sweep for after 012, since agents edit those files now.

## Constraints for the 011 and 012 specs

These steps are not drafted yet, so shaping them costs nothing now. Each constraint serves the long-term goal: compile like `zig cc` (cross-target, hermetic, one binary) without depending on Zig.

**K1. Keep C text in one module (011).** Lowering produces a lowered tree, and only a printer module writes C text. A later TinyCC quirk, a second C dialect or a native backend then touches one module, not the emitter.

**K2. Inventory the C the emitter depends on (011).** List every compiler builtin and extension the emitted C uses today, such as `__builtin_add_overflow`. Do not change output during parity. After 012, wrap each one in a macro in one runtime header, with a portable fallback:

```c
// bayan-rt.h — the only place compiler-specific C appears
#if defined(__GNUC__) || defined(__clang__)
  #define BY_ADD_OVF(a, b, r) __builtin_add_overflow(a, b, r)
  #define BY_COLD __attribute__((cold, noreturn))
#else
  #define BY_ADD_OVF(a, b, r) by_add_ovf_i64(a, b, r)   // portable C11 fallback
  #define BY_COLD _Noreturn
#endif
```

**K3. Make the compiler a driver interface (012).** The driver calls a `CCompiler` with a target triple and a sysroot. `zig cc` is the default implementation for now; clang, gcc and TinyCC are siblings. The driver never invokes `zig build` and never relies on Zig-only flags.

```cs
interface ICCompiler {
    void compile(ref this, Path source, to Path object, for Target target) fails BuildError uses process
    void link(ref this, ReadOnlySpan<Path> objects, to Path output, for Target target) fails BuildError uses process
}
```

**K4. Route libc through the runtime (012).** Emitted code calls `rt` functions, never libc directly, and the runtime keeps a list of the libc functions it uses. That list is the size of the job when Bayān later ships its own sysroot or a no-libc target.

**K5. Make `bootstrap/bayan.c` build with any C11 compiler (012).** Stage 1 must build without Zig with `cc -fwrapv -ffp-contract=off bootstrap/bayan.c bootstrap/bayan-rt.c -lm`, where `bootstrap/bayan-rt.{h,c}` are copies of `src/runtime/bayan-rt.{h,c}` that a CI check keeps byte-identical (wording changed by the user 2026-11-04, spec 012 Q6). This is the first concrete step to toolchain independence.

**K6. Keep output reproducible (012).** No timestamps or absolute paths in emitted C or `#line` directives. Stage 2 and stage 3 must already be byte-identical, so this mostly writes down what 012 needs anyway.

## Not mid-flight

These wait for round 4 because each changes parsing, checking or emitted C that 010 or 011 must match:

- subjectless `switch` and `when` guards
- `final` states and BYN7010
- `dbg(x)`
- the closure escape rule
- any change to emitted C (K2's macros included) before 012 passes
- the Bayān prose sweep across all docs

## Applied (2026-11-01)

Filed from `addendum-A1-midflight.md`; the text above is unchanged. "Safe to land now" landed on `docs/a1-midflight`:

- 006 (done 2026-10-23) and 007 (done 2026-10-25) marked Done in the handoff's spec table.
- The round-3 range is covered: the review fixes merged at `56133ca` cover `4c58b67bdda0..c21cc795` (round 3 merged at `c21cc79`) and `c21cc795..5011c014` (revision 0011); `f572298` re-reviews `5011c014..56133ca`. The `work/todo.md` note is removed.
- The BYN1056, BYN1057 and BYN7005 pages already existed (added 2026-10-24); the stale `work/todo.md` line is removed.
- Codes reserved as comments in `DiagnosticCodes.cs`, not as descriptors: a registered descriptor must have a golden case, and the self-hosted tables are compared with `DiagnosticCodes.All` (`SelfhostSemanticsT2Tests`, `SelfhostParserTests`), so a descriptor would change test results. Numbers are the next free ones with spec 013's BYN1069 counted:

| Name | Code | Note |
|---|---|---|
| `NegatedIsInput` | BYN1070 | round 4's draft says BYN1069, taken by `StatementAttribute` (spec 013) |
| `SwitchExpressionNeedsElse` | BYN1071 | parser range, as an input-form check |
| `BorrowedClosureStored` | BYN2825 | after the lambda codes BYN2820–BYN2824 |
| `FinalStateNotReached` | BYN7013 | round 4's draft says BYN7010, which is `BecomesNeedsRef` |

- Titles of README, SPEC, GRAMMAR, STYLE and HANDOFF read "Bayān"; README and STYLE carry the ASCII line.
