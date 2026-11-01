# Usage: python3 -I scripts/selfhost/review-brief.py <repo-or-worktree> T13 — writes tmp/010-<t>/review.md, the brief for the
# independent reviewer that must pass a task before scripts/selfhost/land.sh merges it. The reviewer is a fresh agent that
# did not write the code: it gets the task, the C# it ports, the files, and the bar; never the writer's summary.
import os, re, sys

wt = os.path.abspath(sys.argv[1])
spec = os.path.join(wt, 'docs/specs/010-self-hosted-semantics')
tasks = open(os.path.join(spec, '010-tasks.md'), encoding='utf-8').read().split('\n')
api = open(os.path.join(spec, '010-api.md'), encoding='utf-8').read().split('\n')

for t in sys.argv[2:]:
    block, on = [], False
    for line in tasks:
        if re.match(rf'^- \[[ x]\] {t} ', line):
            on = True
        elif on and re.match(r'^- \[', line):
            break
        if on:
            block.append(line)
    assert block, t
    head = re.sub(r'\([^()]*(\([^()]*\)[^()]*)*\)', '', re.search(r'files: (.*?) — covers', block[0]).group(1))
    files = re.findall(r'`([^`]+\.(?:byn|cs))`', head)
    # tests/, docs/ and src/ paths are written from the repo root; selfhost/ paths keep their own directory
    rooted = [f for f in files if f.startswith(('tests/', 'docs/', 'src/', 'work/'))]
    byns = [f if f.startswith('selfhost/') else f'selfhost/semantics/{f}' for f in files if f.endswith('.byn') and f not in rooted]
    tests = [f'tests/Bayan.Tests/{f}' for f in files if f.endswith('.cs') and f not in rooted] + rooted
    own_tests = [f for f in files if f.endswith('.cs') and f not in rooted]
    test_class = 'SelfhostSemantics' if t == 'T28' else own_tests[0][:-3] if own_tests else f'SelfhostSemantics{t}Tests'
    own_api = [l for l in api if re.match(rf'^- {t} ', l)]
    q = re.search(r'Q(\d{3})–Q(\d{3})', ' '.join(block))
    s = f"""You are the independent reviewer for task {t} of spec 010 (the Bayan type checker ported from C# `src/Bayan.Semantics/` into Bayan under `selfhost/semantics/`). You did not write this code. Your job is to find where the port is not the C#. Do not edit any file except the review file below; do not run git commands that change state.

Read: `AGENTS.md`, `scripts/selfhost/bayan-cheatsheet.md`, the task below, the C# it names (whole methods), the port {', '.join('`'+b+'`' for b in byns)}, the tests {', '.join('`'+x+'`' for x in tests)}, and Questions {('Q'+q.group(1)+'–Q'+q.group(2)) if q else 'for this task'} in `docs/bayan-handoff.md`.

## Task (verbatim)
{chr(10).join(block)}

## Entry points
{chr(10).join(own_api) or '(file-private only)'}

## The bar (a task fails if any of these is not met)
1. **Method for method.** Every C# method in the task's range exists, in C# order, with every branch, diagnostic code, message, label, help, fix span and report order. Logic that is not in the C# is a finding, however reasonable it looks.
2. **No stand-ins.** A call to a sibling's entry point goes to that entry point even while it is a stub. A fallback for when the sibling returns a default (`-1`, `false`, empty), a re-implementation of a sibling's function, a fake handle (`body = 0`), or a guard that skips a sibling call is HIGH.
3. **No silent gaps.** A C# field or state with no home in `Ctx`/`Binder` goes in the task's own `// ── {t} state` section of `ctx.byn`. Dropping it, approximating it (scope depth for scope identity, a path prefix for a loading list, a field scan for a dictionary), or leaving a function empty without `(blocked: Qnnn)` is HIGH.
4. **Oracle tests.** Every ported function has a test whose expected lines come from running the C# implementation in-process on the same input (see `SelfhostSemanticsT10Tests.Expected`), including a failure/diagnostic path; diagnostics compare as `code line:col message`. Hand-typed expectations, or expectations that hold only because a sibling is a stub, are MEDIUM each (HIGH if they encode behaviour that differs from the C#).
5. **True docs.** Each Question in the task's range says what the code does; each break-test line citation points at the line it names. A false claim is MEDIUM.

Verify each finding in the code before you write it: name the Bayan `file:line` and the C# `file:line`. Run the task's tests (`dotnet test -v q --filter FullyQualifiedName~{test_class}`) once; you may add a probe under `tmp/review-{t.lower()}/` to confirm a behaviour.

## Write `docs/specs/010-self-hosted-semantics/reviews/{t}.md`, exactly this shape
```
# {t} review
Commit: <git rev-parse HEAD>
Verdict: PASS | FAIL

| # | Severity | Finding (Bayan file:line — C# file:line) | Status |
|---|---|---|---|
| 1 | HIGH | … | open |
```
Status is `open`, `fixed` (only for a re-review that saw the fix) or `resolved: Qnnn` (a Question records a dispatcher decision that accepts it). `Verdict: PASS` only when no HIGH and no MEDIUM row is `open`. End your final message with the verdict and the counts per severity.
"""
    d = os.path.join(wt, 'tmp', f'010-{t.lower()}')
    os.makedirs(d, exist_ok=True)
    open(os.path.join(d, 'review.md'), 'w', encoding='utf-8').write(s)
    print(os.path.join(d, 'review.md'))
