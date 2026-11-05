# Usage: python3 -I scripts/selfhost/review-brief.py <repo-or-worktree> T13 [--spec 011] — writes tmp/010-<t>/review.md, the brief for the
# independent reviewer that must pass a task before scripts/selfhost/land.sh merges it. The reviewer is a fresh agent that
# did not write the code: it gets the task, the C# it ports, the files, and the bar; never the writer's summary.
# The spec is a row of specs.tsv (without --spec, 010).
import os, re, subprocess, sys
args = sys.argv[1:]
SPEC = '010'
if '--spec' in args:
    i = args.index('--spec')
    SPEC = args[i + 1]
    del args[i:i + 2]
BRIEF = {'010': dict(desc='the Bayan type checker ported from C# `src/Bayan.Semantics/` into Bayan under `selfhost/semantics/`', oracle='SelfhostSemanticsT10Tests.Expected'),
         '011': dict(desc='the Bayan lowering and C emitter ported from C# `src/Bayan.Lowering/` and `src/Bayan.Backend.C/` into Bayan under `selfhost/emitter/`', oracle='SelfhostEmitterHarness.AssertSame'),
         '012': dict(desc='the Bayan compiler driver ported from C# `src/Bayan.Cli/` (`CliApp`) into Bayan under `selfhost/driver/`, compiled with package root `selfhost/` (stage 1 of the bootstrap)', oracle='SelfhostDriverHarness')}
if SPEC not in BRIEF: sys.exit(f'review-brief: no brief text for spec {SPEC}')
HEAD, *TABLE = [l.split('\t') for l in open(os.path.join(os.path.dirname(os.path.abspath(__file__)), 'specs.tsv'), encoding='utf-8').read().splitlines()]
row = dict(zip(HEAD, next(r for r in TABLE if r[0] == SPEC)))
wt = os.path.abspath(args[0]); spec = os.path.join(wt, row['dir'])
tasks = open(os.path.join(spec, row['tasks']), encoding='utf-8').read().split('\n')
# the Question ranges wave-brief.py assigned: $BAYAN_QRANGES, else tmp/q-ranges.tsv of the main checkout (as preflight.sh reads them)
common = subprocess.run(['git', '-C', wt, 'rev-parse', '--path-format=absolute', '--git-common-dir'], capture_output=True).stdout.decode().strip()
QRANGES = os.environ.get('BAYAN_QRANGES') or os.path.join(os.path.dirname(common) if common else wt, 'tmp', 'q-ranges.tsv')
qrows = [l.split('\t') for l in open(QRANGES, encoding='utf-8').read().splitlines()[1:]] if os.path.exists(QRANGES) else []
api_path = os.path.join(spec, row['api']); api = open(api_path, encoding='utf-8').read().split('\n') if os.path.exists(api_path) else []
for t in args[1:]:
    block, on = [], False
    for line in tasks:
        if re.match(rf'^- \[[ x]\] {t} ', line):
            on = True
        elif on and re.match(r'^- \[', line):
            break
        if on:
            block.append(line)
    assert block, t
    head = re.sub(r'\([^()]*(\([^()]*\)[^()]*)*\)', '', re.search(r'files: (.*?) — covers', block[0]).group(1).split(' — C#:')[0])
    files = re.findall(r'`([^`]+\.(?:byn|cs))`', head)
    # tests/, docs/ and src/ paths are written from the repo root; selfhost/ paths keep their own directory
    rooted = [f for f in files if f.startswith(('tests/', 'docs/', 'src/', 'work/'))]
    byns = [f if f.startswith('selfhost/') else f"{row['bydir']}/{f}" for f in files if f.endswith('.byn') and f not in rooted]
    tests = [f'tests/Bayan.Tests/{f}' for f in files if f.endswith('.cs') and f not in rooted] + rooted
    own_tests = [f for f in files if f.endswith('.cs') and f not in rooted]
    test_class = 'SelfhostSemantics' if SPEC == '010' and t == 'T28' else own_tests[0][:-3] if own_tests else f"{row['prefix']}{t[1:]}Tests"
    law = re.findall(r'`docs/laws/(\d{3})-[^`]*\.md`', head)  # a law-enact task's gate is its law's proof
    filt = f'Law=L{law[0]}' if law and not own_tests else f'FullyQualifiedName~{test_class}'
    if SPEC == '010' and not law and not own_tests and not byns: filt = 'FullyQualifiedName~SelfhostSemanticsApiTests'  # a docs-only task
    # 011: every test class the task owns gates it (a task with a law page and tests too, T14), and T21 also counts the SelfhostSemantics* tests (Q867, Q870)
    # the classes are those the task's tests/Bayan.Tests/*.cs files declare, bare names or with their directory (SelfhostDriverInventoryTests.cs
    # holds SelfhostDriverT8Tests); a file not written yet counts by its name; never a `<prefix><n>Tests` class the task does not have (012 T17).
    # As in wave-brief.py, a task with bare test names keeps them alone (011 T14 re-pins the rooted ones)
    test_paths = [f'tests/Bayan.Tests/{f}' for f in own_tests] or [f for f in rooted if re.fullmatch(r'tests/Bayan\.Tests/[A-Za-z0-9]+Tests\.cs', f)]
    if SPEC != '010' and (test_paths or not law):
        classes = [c for p in test_paths for c in (re.findall(r'\bclass\s+(\w+Tests)\b', open(os.path.join(wt, p), encoding='utf-8').read()) if os.path.exists(os.path.join(wt, p)) else []) or [os.path.basename(p)[:-3]]]
        filt = '|'.join(f'FullyQualifiedName~{c}' for c in dict.fromkeys(classes)) or filt
        if SPEC == '011' and t == 'T21': filt += '|FullyQualifiedName~SelfhostSemantics'
        if SPEC == '011' and t == 'T13': filt += '|FullyQualifiedName~SelfhostEmitter'  # integration: every emitter class (Q886)
    shared = '`ctx.byn`' if SPEC == '010' else ' and '.join('`'+c+'`' for c in row['shared'].split(','))
    own_api = [l for l in api if re.match(rf'^- {t} ', l)]
    q = re.search(r'Q(\d{3,4})–Q(\d{3,4})', ' '.join(block))
    q = (q.group(1), q.group(2)) if q else next((r[2:4] for r in qrows if r[:2] == [SPEC, t]), None)  # else the range wave-brief.py assigned
    s = f"""You are the independent reviewer for task {t} of spec {SPEC} ({BRIEF[SPEC]['desc']}). You did not write this code. Your job is to find where the port is not the C#. Do not edit any file except the review file below; do not run git commands that change state.

Read: `AGENTS.md`, `scripts/selfhost/bayan-cheatsheet.md`, the task below, the C# it names (whole methods), the port {', '.join('`'+b+'`' for b in byns)}, the tests {', '.join('`'+x+'`' for x in tests)}, and Questions {('Q'+q[0]+'–Q'+q[1]) if q else 'for this task'} in `docs/bayan-handoff.md`.

## Task (verbatim)
{chr(10).join(block)}

## Entry points
{chr(10).join(own_api) or '(file-private only)'}

## The bar (a task fails if any of these is not met)
1. **Method for method.** Every C# method in the task's range exists, in C# order, with every branch, diagnostic code, message, label, help, fix span and report order. Logic that is not in the C# is a finding, however reasonable it looks.
2. **No stand-ins.** A call to a sibling's entry point goes to that entry point even while it is a stub. A fallback for when the sibling returns a default (`-1`, `false`, empty), a re-implementation of a sibling's function, a fake handle (`body = 0`), or a guard that skips a sibling call is HIGH.
3. **No silent gaps.** A C# field or state with no home in `Ctx`/`Binder` goes in the task's own `// ── {t} state` section of {shared}. Dropping it, approximating it (scope depth for scope identity, a path prefix for a loading list, a field scan for a dictionary), or leaving a function empty without `(blocked: Qnnn)` is HIGH.
4. **Oracle tests.** Every ported function has a test whose expected lines come from running the C# implementation in-process on the same input (see `{BRIEF[SPEC]['oracle']}`), including a failure/diagnostic path; diagnostics compare as `code line:col message`. Hand-typed expectations, or expectations that hold only because a sibling is a stub, are MEDIUM each (HIGH if they encode behaviour that differs from the C#).
5. **True docs.** Each Question in the task's range says what the code does; each break-test line citation points at the line it names. A false claim is MEDIUM.

Verify each finding in the code before you write it: name the Bayan `file:line` and the C# `file:line`. Run the task's tests (`dotnet test -v q --filter "{filt}"`) once; you may add a probe under `tmp/review-{t.lower()}/` to confirm a behaviour.

## Write `{row['dir']}/reviews/{t}.md`, exactly this shape
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
    d = os.path.join(wt, row['tmp'] + t.lower())
    os.makedirs(d, exist_ok=True)
    open(os.path.join(d, 'review.md'), 'w', encoding='utf-8').write(s)
    print(os.path.join(d, 'review.md'))
