You are the independent reviewer for task T12 of spec 010 (the Bayan type checker ported from C# `src/Bayan.Semantics/` into Bayan under `selfhost/semantics/`). You did not write this code. Your job is to find where the port is not the C#. Do not edit any file except the review file below; do not run git commands that change state.

Read: `AGENTS.md`, `scripts/selfhost/bayan-cheatsheet.md`, the task below, the C# it names (whole methods), the port `selfhost/semantics/declare.byn`, the tests `tests/Bayan.Tests/SelfhostSemanticsT12Tests.cs`, and Questions Q575–Q579 in `docs/bayan-handoff.md`.

## Task (verbatim)
- [x] T12 Declaration B — files: `declare.byn` (`Compilation.cs` ~655–1037: variants, payloads, enums, unions, struct members, overloads, `overlap`), `SelfhostSemanticsT12Tests.cs` — covers: R4 — after: T4; parallel with: T6–T11, T13–T27, T34–T39 — done when:
  - method-for-method port with copied symbol facts and no synthesized syntax; Q575–Q579 as needed.
  - Size: about 555 lines.

## Entry points
- T12 declare.byn: void declareMembers(ref Ctx c, for StructId s)  ← Compilation.cs:922 DeclareMembers
- T12 declare.byn: void declareVariants(ref Ctx c, for ErrorSetId set)  ← Compilation.cs:718 DeclareVariants
- T12 declare.byn: bool overlap(ref Ctx c, for FuncId a, as FuncId b)  ← Compilation.cs:671 Overlap
- T12 declare.byn: Location loc(FileId file, at TextSpan span)  ← Compilation.cs:712 Loc

## The bar (a task fails if any of these is not met)
1. **Method for method.** Every C# method in the task's range exists, in C# order, with every branch, diagnostic code, message, label, help, fix span and report order. Logic that is not in the C# is a finding, however reasonable it looks.
2. **No stand-ins.** A call to a sibling's entry point goes to that entry point even while it is a stub. A fallback for when the sibling returns a default (`-1`, `false`, empty), a re-implementation of a sibling's function, a fake handle (`body = 0`), or a guard that skips a sibling call is HIGH.
3. **No silent gaps.** A C# field or state with no home in `Ctx`/`Binder` goes in the task's own `// ── T12 state` section of `ctx.byn`. Dropping it, approximating it (scope depth for scope identity, a path prefix for a loading list, a field scan for a dictionary), or leaving a function empty without `(blocked: Qnnn)` is HIGH.
4. **Oracle tests.** Every ported function has a test whose expected lines come from running the C# implementation in-process on the same input (see `SelfhostSemanticsT10Tests.Expected`), including a failure/diagnostic path; diagnostics compare as `code line:col message`. Hand-typed expectations, or expectations that hold only because a sibling is a stub, are MEDIUM each (HIGH if they encode behaviour that differs from the C#).
5. **True docs.** Each Question in the task's range says what the code does; each break-test line citation points at the line it names. A false claim is MEDIUM.

Verify each finding in the code before you write it: name the Bayan `file:line` and the C# `file:line`. Run the task's tests (`dotnet test -v q --filter "FullyQualifiedName~SelfhostSemanticsT12Tests"`) once; you may add a probe under `tmp/review-t12/` to confirm a behaviour.

## Write `docs/specs/010-self-hosted-semantics/reviews/T12.md`, exactly this shape
```
# T12 review
Commit: <git rev-parse HEAD>
Verdict: PASS | FAIL

| # | Severity | Finding (Bayan file:line — C# file:line) | Status |
|---|---|---|---|
| 1 | HIGH | … | open |
```
Status is `open`, `fixed` (only for a re-review that saw the fix) or `resolved: Qnnn` (a Question records a dispatcher decision that accepts it). `Verdict: PASS` only when no HIGH and no MEDIUM row is `open`. End your final message with the verdict and the counts per severity.
