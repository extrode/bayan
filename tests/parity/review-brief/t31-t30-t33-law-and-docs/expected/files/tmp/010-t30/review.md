You are the independent reviewer for task T30 of spec 010 (the Bayan type checker ported from C# `src/Bayan.Semantics/` into Bayan under `selfhost/semantics/`). You did not write this code. Your job is to find where the port is not the C#. Do not edit any file except the review file below; do not run git commands that change state.

Read: `AGENTS.md`, `scripts/selfhost/bayan-cheatsheet.md`, the task below, the C# it names (whole methods), the port , the tests , and Questions for this task in `docs/bayan-handoff.md`.

## Task (verbatim)
- [x] T30 enact L022 "Binder equivalence" — files: `docs/laws/022-binder-equivalence.md` — covers: L022 — after: T29 — done when:
  - `status: enacted` lands with T29's passing proof (standing authorization, 2026-10-21).
  - Size: one status line and recorded measurements.

## Entry points
(file-private only)

## The bar (a task fails if any of these is not met)
1. **Method for method.** Every C# method in the task's range exists, in C# order, with every branch, diagnostic code, message, label, help, fix span and report order. Logic that is not in the C# is a finding, however reasonable it looks.
2. **No stand-ins.** A call to a sibling's entry point goes to that entry point even while it is a stub. A fallback for when the sibling returns a default (`-1`, `false`, empty), a re-implementation of a sibling's function, a fake handle (`body = 0`), or a guard that skips a sibling call is HIGH.
3. **No silent gaps.** A C# field or state with no home in `Ctx`/`Binder` goes in the task's own `// ── T30 state` section of `ctx.byn`. Dropping it, approximating it (scope depth for scope identity, a path prefix for a loading list, a field scan for a dictionary), or leaving a function empty without `(blocked: Qnnn)` is HIGH.
4. **Oracle tests.** Every ported function has a test whose expected lines come from running the C# implementation in-process on the same input (see `SelfhostSemanticsT10Tests.Expected`), including a failure/diagnostic path; diagnostics compare as `code line:col message`. Hand-typed expectations, or expectations that hold only because a sibling is a stub, are MEDIUM each (HIGH if they encode behaviour that differs from the C#).
5. **True docs.** Each Question in the task's range says what the code does; each break-test line citation points at the line it names. A false claim is MEDIUM.

Verify each finding in the code before you write it: name the Bayan `file:line` and the C# `file:line`. Run the task's tests (`dotnet test -v q --filter "Law=L022"`) once; you may add a probe under `tmp/review-t30/` to confirm a behaviour.

## Write `docs/specs/010-self-hosted-semantics/reviews/T30.md`, exactly this shape
```
# T30 review
Commit: <git rev-parse HEAD>
Verdict: PASS | FAIL

| # | Severity | Finding (Bayan file:line — C# file:line) | Status |
|---|---|---|---|
| 1 | HIGH | … | open |
```
Status is `open`, `fixed` (only for a re-review that saw the fix) or `resolved: Qnnn` (a Question records a dispatcher decision that accepts it). `Verdict: PASS` only when no HIGH and no MEDIUM row is `open`. End your final message with the verdict and the counts per severity.
