You are the independent reviewer for task T10 of spec 010 (the Bayan type checker ported from C# `src/Bayan.Semantics/` into Bayan under `selfhost/semantics/`). You did not write this code. Your job is to find where the port is not the C#. Do not edit any file except the review file below; do not run git commands that change state.

Read: `AGENTS.md`, `scripts/selfhost/bayan-cheatsheet.md`, the task below, the C# it names (whole methods), the port `selfhost/semantics/manifest.byn`, the tests `tests/Bayan.Tests/SelfhostSemanticsT10Tests.cs`, and Questions Q565–Q569 in `docs/bayan-handoff.md`.

## Task (verbatim)
- [x] T10 Manifest reader — files: `manifest.byn` (`src/Bayan.Syntax/Packages/Manifest.cs`: data grammar, BYN3100–BYN3103, capabilities, grammar, `camelOf`/`kebabOf`), `SelfhostSemanticsT10Tests.cs` — covers: R1, R4 — after: T4; parallel with: T6–T9, T11–T27, T34–T39 — done when:
  - both `package.byn` diag cases parse and report identically; entry-module names match `CliApp.EntryModule`; Q565–Q569 as needed.
  - Size: about 690 lines.

## Entry points
- T10 manifest.byn: Manifest manifestRead(string path)  ← Manifest.cs:75 Read
- T10 manifest.byn: Manifest manifestParse(move lexer.SourceText source)  ← Manifest.cs:77 Parse
- T10 manifest.byn: i64 manifestErrorCount(in Manifest m)  ← Manifest.cs:73 HasErrors
- T10 manifest.byn: string camelOf(string kebab)  ← Manifest.cs:262 CamelOf
- T10 manifest.byn: string kebabOf(string camel)  ← Manifest.cs:250 KebabOf

## The bar (a task fails if any of these is not met)
1. **Method for method.** Every C# method in the task's range exists, in C# order, with every branch, diagnostic code, message, label, help, fix span and report order. Logic that is not in the C# is a finding, however reasonable it looks.
2. **No stand-ins.** A call to a sibling's entry point goes to that entry point even while it is a stub. A fallback for when the sibling returns a default (`-1`, `false`, empty), a re-implementation of a sibling's function, a fake handle (`body = 0`), or a guard that skips a sibling call is HIGH.
3. **No silent gaps.** A C# field or state with no home in `Ctx`/`Binder` goes in the task's own `// ── T10 state` section of `ctx.byn`. Dropping it, approximating it (scope depth for scope identity, a path prefix for a loading list, a field scan for a dictionary), or leaving a function empty without `(blocked: Qnnn)` is HIGH.
4. **Oracle tests.** Every ported function has a test whose expected lines come from running the C# implementation in-process on the same input (see `SelfhostSemanticsT10Tests.Expected`), including a failure/diagnostic path; diagnostics compare as `code line:col message`. Hand-typed expectations, or expectations that hold only because a sibling is a stub, are MEDIUM each (HIGH if they encode behaviour that differs from the C#).
5. **True docs.** Each Question in the task's range says what the code does; each break-test line citation points at the line it names. A false claim is MEDIUM.

Verify each finding in the code before you write it: name the Bayan `file:line` and the C# `file:line`. Run the task's tests (`dotnet test -v q --filter "FullyQualifiedName~SelfhostSemanticsT10Tests"`) once; you may add a probe under `tmp/review-t10/` to confirm a behaviour.

## Write `docs/specs/010-self-hosted-semantics/reviews/T10.md`, exactly this shape
```
# T10 review
Commit: <git rev-parse HEAD>
Verdict: PASS | FAIL

| # | Severity | Finding (Bayan file:line — C# file:line) | Status |
|---|---|---|---|
| 1 | HIGH | … | open |
```
Status is `open`, `fixed` (only for a re-review that saw the fix) or `resolved: Qnnn` (a Question records a dispatcher decision that accepts it). `Verdict: PASS` only when no HIGH and no MEDIUM row is `open`. End your final message with the verdict and the counts per severity.
