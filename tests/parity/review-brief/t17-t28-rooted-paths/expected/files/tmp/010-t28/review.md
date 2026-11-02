You are the independent reviewer for task T28 of spec 010 (the Bayan type checker ported from C# `src/Bayan.Semantics/` into Bayan under `selfhost/semantics/`). You did not write this code. Your job is to find where the port is not the C#. Do not edit any file except the review file below; do not run git commands that change state.

Read: `AGENTS.md`, `scripts/selfhost/bayan-cheatsheet.md`, the task below, the C# it names (whole methods), the port `selfhost/semantics/*.byn`, the tests `tests/Bayan.Tests/SelfhostSemanticsTests.cs`, `tests/Bayan.Tests/SelfhostSemanticsGeneratorTests.cs`, `tests/Bayan.Tests/SelfhostSemanticsT*Tests.cs`, `tests/cases/diag/generic-guard-mutate-rebind.byn`, and Questions Q655–Q657 in `docs/bayan-handoff.md`.

## Task (verbatim)
- [x] T28 Integration: parity — files: any `selfhost/semantics/*.byn` (stand-ins removed, fixes only), `src/Bayan.*` (only a blocking compiler bug, with a test), `work/todo.md` (removes `t17Host`, `t18Host` and the `t11/t15/t16/t17ExprSpan` copies, Q674), `SelfhostSemanticsTests.cs` and `SelfhostSemanticsGeneratorTests.cs` (the `T28 enables` skips removed), `SelfhostSemanticsT*Tests.cs` (only harness stand-ins that a real port now replaces, removed), `docs/specs/010-self-hosted-semantics/010-api.md` (signatures a T28 fix widens), `tests/cases/diag/generic-guard-mutate-rebind.byn` and `tests/cases/diag/generic-guard-mutate-rebind.diag` (covers the generic `checkGuardWrite` branch and the `bindFunction` rollback, expected file from the C#) — covers: R1, R2, R4, R5, R6, R7, R8 — after: T6–T27, T34–T39; parallel with: none — done when:
  - every diag (golden and CLI), typed and generated row passes with equal stderr, `--types` bytes and exit code; the 1 MB median ratio is at most 10 and recorded; the full suite including L001/L002/L017/L018 passes; any blocking compiler bug is fixed here with a test; Q655–Q657.
  - Size: fixes only, no new files.

## Entry points
- T28 calls.byn: void bindArgumentListForRecovery(ref Binder b, through ref Ctx c, for in List<parser.Argument> args)  ← BinderCalls.cs:174 BindArgumentsFor

## The bar (a task fails if any of these is not met)
1. **Method for method.** Every C# method in the task's range exists, in C# order, with every branch, diagnostic code, message, label, help, fix span and report order. Logic that is not in the C# is a finding, however reasonable it looks.
2. **No stand-ins.** A call to a sibling's entry point goes to that entry point even while it is a stub. A fallback for when the sibling returns a default (`-1`, `false`, empty), a re-implementation of a sibling's function, a fake handle (`body = 0`), or a guard that skips a sibling call is HIGH.
3. **No silent gaps.** A C# field or state with no home in `Ctx`/`Binder` goes in the task's own `// ── T28 state` section of `ctx.byn`. Dropping it, approximating it (scope depth for scope identity, a path prefix for a loading list, a field scan for a dictionary), or leaving a function empty without `(blocked: Qnnn)` is HIGH.
4. **Oracle tests.** Every ported function has a test whose expected lines come from running the C# implementation in-process on the same input (see `SelfhostSemanticsT10Tests.Expected`), including a failure/diagnostic path; diagnostics compare as `code line:col message`. Hand-typed expectations, or expectations that hold only because a sibling is a stub, are MEDIUM each (HIGH if they encode behaviour that differs from the C#).
5. **True docs.** Each Question in the task's range says what the code does; each break-test line citation points at the line it names. A false claim is MEDIUM.

Verify each finding in the code before you write it: name the Bayan `file:line` and the C# `file:line`. Run the task's tests (`dotnet test -v q --filter "FullyQualifiedName~SelfhostSemantics"`) once; you may add a probe under `tmp/review-t28/` to confirm a behaviour.

## Write `docs/specs/010-self-hosted-semantics/reviews/T28.md`, exactly this shape
```
# T28 review
Commit: <git rev-parse HEAD>
Verdict: PASS | FAIL

| # | Severity | Finding (Bayan file:line — C# file:line) | Status |
|---|---|---|---|
| 1 | HIGH | … | open |
```
Status is `open`, `fixed` (only for a re-review that saw the fix) or `resolved: Qnnn` (a Question records a dispatcher decision that accepts it). `Verdict: PASS` only when no HIGH and no MEDIUM row is `open`. End your final message with the verdict and the counts per severity.
