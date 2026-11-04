You are the independent reviewer for task T1 of spec 012 (the Bayan compiler driver ported from C# `src/Bayan.Cli/` (`CliApp`) into Bayan under `selfhost/driver/`, compiled with package root `selfhost/` (stage 1 of the bootstrap)). You did not write this code. Your job is to find where the port is not the C#. Do not edit any file except the review file below; do not run git commands that change state.

Read: `AGENTS.md`, `scripts/selfhost/bayan-cheatsheet.md`, the task below, the C# it names (whole methods), the port `selfhost/driver/main.byn`, `selfhost/driver/dctx.byn`, the tests `tests/Bayan.Tests/SelfhostDriverHarness.cs`, `tests/Bayan.Tests/SelfhostDriverTests.cs`, `tests/Bayan.Tests/SelfhostDriverApiTests.cs`, and Questions for this task in `docs/bayan-handoff.md`.

## Task (verbatim)
- [ ] T1 Skeleton, dispatch, refusals — files: `selfhost/driver/main.byn`, `selfhost/driver/dctx.byn` (its own `// ── T1 state` section), `tests/Bayan.Tests/SelfhostDriverHarness.cs` (build stage 1 once with `Compilation.Create(sources, packageRoot: selfhost/)` as `SelfhostEmitterHarness.cs:28-36`, run as a process), `tests/Bayan.Tests/SelfhostDriverTests.cs` (usage corpus from `CliTests.cs` argument lists; `SourcesAreFormatted` runs `bayan format --check` over `selfhost/driver/`), `tests/Bayan.Tests/SelfhostDriverApiTests.cs`, `docs/specs/012-self-hosted-bootstrap/012-api.md` — C#: `CliApp.cs` 16-35, 37-106, 315-376 — covers: R1, R2, R3, R13, R14 — after: P1 — difficulty: medium → `codegen-lite` — done when: `version` (Q3 masked), `help`, no arguments, an unknown command, `fmt`, every refused command and option (Q4 text) give `CliApp.Run`'s exit code and output; the other commands are stubs returning the R3 refusal until their task lands.

## Entry points
- T1 `func main() -> Int` (`selfhost/driver/main.byn`)

## The bar (a task fails if any of these is not met)
1. **Method for method.** Every C# method in the task's range exists, in C# order, with every branch, diagnostic code, message, label, help, fix span and report order. Logic that is not in the C# is a finding, however reasonable it looks.
2. **No stand-ins.** A call to a sibling's entry point goes to that entry point even while it is a stub. A fallback for when the sibling returns a default (`-1`, `false`, empty), a re-implementation of a sibling's function, a fake handle (`body = 0`), or a guard that skips a sibling call is HIGH.
3. **No silent gaps.** A C# field or state with no home in `Ctx`/`Binder` goes in the task's own `// ── T1 state` section of `selfhost/driver/dctx.byn`. Dropping it, approximating it (scope depth for scope identity, a path prefix for a loading list, a field scan for a dictionary), or leaving a function empty without `(blocked: Qnnn)` is HIGH.
4. **Oracle tests.** Every ported function has a test whose expected lines come from running the C# implementation in-process on the same input (see `SelfhostDriverHarness`), including a failure/diagnostic path; diagnostics compare as `code line:col message`. Hand-typed expectations, or expectations that hold only because a sibling is a stub, are MEDIUM each (HIGH if they encode behaviour that differs from the C#).
5. **True docs.** Each Question in the task's range says what the code does; each break-test line citation points at the line it names. A false claim is MEDIUM.

Verify each finding in the code before you write it: name the Bayan `file:line` and the C# `file:line`. Run the task's tests (`dotnet test -v q --filter "FullyQualifiedName~SelfhostDriverT1Tests"`) once; you may add a probe under `tmp/review-t1/` to confirm a behaviour.

## Write `docs/specs/012-self-hosted-bootstrap/reviews/T1.md`, exactly this shape
```
# T1 review
Commit: <git rev-parse HEAD>
Verdict: PASS | FAIL

| # | Severity | Finding (Bayan file:line — C# file:line) | Status |
|---|---|---|---|
| 1 | HIGH | … | open |
```
Status is `open`, `fixed` (only for a re-review that saw the fix) or `resolved: Qnnn` (a Question records a dispatcher decision that accepts it). `Verdict: PASS` only when no HIGH and no MEDIUM row is `open`. End your final message with the verdict and the counts per severity.
