# Usage: python3 -I scripts/selfhost/wave-brief.py <repo-or-worktree> T10 [T11 ...] [--spec 011] — writes tmp/010-<t>/prompt.md for each task:
# a self-contained writer brief for one spec 010 task (task text, plan rows, its 010-api.md entry points, guard command).
# The spec is a row of specs.tsv (paths, shared-state files, test-class prefix, base branch); without --spec it is 010.
import os, re, sys

args = sys.argv[1:]
SPEC = '010'
if '--spec' in args:
    i = args.index('--spec')
    SPEC = args[i + 1]
    del args[i:i + 2]
HERE = os.path.dirname(os.path.abspath(__file__))
HEAD, *TABLE = [l.split('\t') for l in open(os.path.join(HERE, 'specs.tsv'), encoding='utf-8').read().splitlines()]
row = dict(zip(HEAD, next(r for r in TABLE if r[0] == SPEC)))
SHARED = row['shared'].split(',')
wt = os.path.abspath(args[0])
BASE = os.environ.get('BAYAN_BASE', row['base'])
ROOT = wt
spec = os.path.join(wt, row['dir'])
tasks = open(os.path.join(spec, row['tasks']), encoding='utf-8').read().split('\n')
plan = open(os.path.join(spec, row['plan']), encoding='utf-8').read().split('\n')
api_path = os.path.join(spec, row['api'])
api = open(api_path, encoding='utf-8').read().split('\n') if os.path.exists(api_path) else []
protocol = ['', ''] + open(os.path.join(HERE, 'writer-protocol.md'), encoding='utf-8').read().split('\n')
cheat = open(os.path.join(HERE, 'bayan-cheatsheet.md'), encoding='utf-8').read()


def task_block(t):
    out, on = [], False
    for line in tasks:
        if re.match(rf'^- \[[ x]\] {t} ', line):
            on = True
        elif on and re.match(r'^- \[', line):
            break
        if on:
            out.append(line)
    return out


def owned_files(block):
    head = block[0]
    files = re.search(r'files: (.*?) — covers', head).group(1).split(' — C#:')[0]  # 011 names the C# range after the files
    files = re.sub(r'\([^()]*(\([^()]*\)[^()]*)*\)', '', files)
    return re.findall(r'`([^`]+\.(?:byn|cs|md|diag|txt|stdout|stderr|exitcode|sha256))`', files)  # golden expected files too (014 T6)


def plan_rows(byns):
    rows = []
    for line in plan:
        m = re.match(r'^\| (`[^|]+`) \|', line)
        if m and any(f'`{b}`' in m.group(1) for b in byns):
            rows.append(line)
    return rows


def qrange(block):
    m = re.search(r'Q(\d{3})–Q(\d{3})', ' '.join(block))
    return (m.group(1), m.group(2)) if m else None


LANDED11 = f"Landed and callable: see `{row['dir']}/{row['api']}` and the task's `after:` list; the 010 checker modules under `selfhost/semantics/` are public to you."
LANDED = '''Landed and callable: `ctx.byn` (T1), `codes.byn`/`diag.byn` (T2: `report`, `addDiag`, `pushBag`, `popBag`), `symbols.byn`/`types.byn` (T3), `bound.byn` (T4: `add<Kind>`, `children`, `exprType`, `Constant`), `bigint.byn` (T5: labels `bigCmp(a, as: b)`, `bigAdd(a, by: b)`, `bigFits(n, to: bits, signed: s)`; see Q540).'''
HARNESS = '''`tests/Bayan.Tests/SelfhostSemanticsHarness.cs`: `SelfhostSemanticsHarness.RunSections(mainSource)` builds `selfhost/semantics` with your generated `main.byn` (a C# string) and returns its output split at lines `== <section>`; `SelfhostSemanticsHarness.AssertSection(sections, "<section>", expectedLines)` names the first differing line. Build once per class (`static readonly Lazy<…>`). The oracle is the C# implementation: run the same input through `Bayan.Semantics` in-process (`Compilation.Create`, or the C# class directly when it is public/internal-visible) and print the same lines. Every ported function gets a compared case including a failure/diagnostic path; diagnostics compare as `code line:col message`.'''
HARNESS11 = f'''`tests/Bayan.Tests/SelfhostEmitterHarness.cs`: `SelfhostEmitterHarness.Build` compiles `selfhost/emitter/**/*.byn` once per run in `fast` mode and `Run(input, mode, tests)` starts the binary and returns stdout/stderr/exit; `AssertSame(name, expected, actual)` reports the first differing line. Unit tests of a wave task run the task's functions through a generated `main` with the C# as oracle (`CliApp.EmitC(Compilation.Create([source], dir), mode)`). Every ported function gets a compared case including a failure path. If the harness file is not landed yet, read `{row['dir']}/{row['plan']}` (Test harness).'''

for t in args[1:]:
    block = task_block(t)
    assert block, t
    files = owned_files(block)
    # a root file (CHANGELOG.md) and a bare spec file (010-spec.md) are docs this spec owns too
    files = [f"{row['dir']}/{f}" if re.fullmatch(rf'{SPEC}-[a-z]+\.md', f) else f for f in files]
    rooted = [f for f in files if f.startswith(('tests/', 'docs/', 'src/', 'work/')) or re.fullmatch(r'[A-Z]+\.md', f)]
    byns = [f for f in files if f.endswith('.byn') and f not in rooted]
    tests = [f for f in files if f.endswith('.cs') and f not in rooted]
    paths = [f if f.startswith('selfhost/') else f"{row['bydir']}/{f}" for f in byns] + [f'tests/Bayan.Tests/{f}' for f in tests] + rooted
    allow = ','.join(dict.fromkeys(paths + ['docs/bayan-handoff.md', f"{row['dir']}/{row['tasks']}"]))
    own_api = [l for l in api if re.match(rf'^- {t} ', l)]
    q = qrange(block)
    # T28 integrates: its gate is every SelfhostSemantics* class, parity and corpus parity included
    test_class = 'SelfhostSemantics' if SPEC == '010' and t == 'T28' else tests[0][:-3] if tests else f"{row['prefix']}{t[1:]}Tests"
    # a law-enact task owns only its law page: its gate is that law's proof
    law = [m.group(1) for f in rooted if (m := re.match(r'docs/laws/(\d{3})-', f))]
    filt = f'Law=L{law[0]}' if law and not tests else f'FullyQualifiedName~{test_class}'
    # a docs-only task: its gate is the test that keeps 010-api.md and the code in step
    if SPEC == '010' and not law and not tests and not byns: filt = 'FullyQualifiedName~SelfhostSemanticsApiTests'
    # 011: every test class the task owns gates it (a task with a law page and tests too, T14), and T21 also counts the SelfhostSemantics* tests (Q867, Q870)
    if SPEC != '010' and (tests or not law):
        filt = '|'.join(f'FullyQualifiedName~{c[:-3]}' for c in tests) or filt
        if SPEC == '011' and t == 'T21': filt += '|FullyQualifiedName~SelfhostSemantics'
        if SPEC == '011' and t == 'T13': filt += '|FullyQualifiedName~SelfhostEmitter'  # integration: every emitter class (Q886)
    sharedx = ' and '.join('`'+c+'`' for c in SHARED)
    proto = '\n'.join(protocol[2:])
    if SPEC != '010': proto = proto.replace('`selfhost/semantics/ctx.byn`', sharedx).replace('nothing else in ctx.byn', 'nothing else in those files').replace('SelfhostSemanticsT10Tests.Expected', 'SelfhostEmitterHarness.AssertSame')
    proto = proto.replace('SelfhostSemanticsT3Tests', test_class).replace('restore with `git checkout -- <file>`', 'restore by copying back a copy you took first (`cp f tmp/f.orig` … `cp tmp/f.orig f`; never `git checkout`, which reverts to the stub and loses your work)').replace('Skeleton by turn 12', 'Skeleton by turn 15')
    s = f"""You are doing task {t} of spec {SPEC} ({'the Bayan type checker ported from C# `src/Bayan.Semantics/` into Bayan under `selfhost/semantics/`' if SPEC == '010' else 'the Bayan lowering and C emitter ported from C# `src/Bayan.Lowering/` and `src/Bayan.Backend.C/` into Bayan under `selfhost/emitter/`'}). Other writers port the sibling files in parallel; every cross-file function already exists as a compiling stub with its final signature, so you call real names and never write stand-ins. A separate reviewer compares your port with the C# line by line before it lands; invented logic, sibling fallbacks and hand-typed test expectations fail that review.

{proto}

Guard (run before you finish):
```
bash scripts/selfhost/guard.sh --base {BASE} --allow '{allow}' --filter '{filt}' --task {t}{'' if SPEC == '010' else ' --spec ' + SPEC}
```
Do not commit, merge, checkout, reset or stash; the dispatcher commits for you.

**Order of work:** turns 1–6 read; by turn 15 your file(s) compile with every function stubbed and the test file has one passing test; then port in C# order, ~80 lines per compile; tests for each function as you go; guard; docs. If you reach turn 100 with tests unwritten, stop porting and write tests: the dispatcher resumes you for the rest.

## Task (verbatim, {row['dir']}/{row['tasks']})
{chr(10).join(block)}

## What you port (plan file table)
{chr(10).join(plan_rows(byns))}
Port method for method in C# order, keeping C# names lowerCamel. Read the C# ranges named above whole, once. Line ranges marked `~` are approximate: start and stop at the method boundary nearest to them, and say in your Question where you cut.

## Your entry points (exact; already stubbed in your file with `// stub:` comments; replace each body, keep each signature byte for byte)
{chr(10).join(own_api) if own_api else '(none in the cross-file index; your functions are file-private)'}

## Calling siblings
`{row['dir']}/{row['api']}` lists every cross-file function with its exact signature and owner. Call those names as written. A sibling still a stub returns a default (`-1`, `errorType()`, empty list), so **test your functions on inputs that do not go through an unported sibling**, or build the sibling's output by hand (arenas and structs are module-visible). Never edit a sibling's file: if you need a function that is not in the index, write it as a private helper in your own file with a `{t}`-prefixed name (e.g. `{t.lower()}FindX`) and record it in a Question so integration (T28) can merge duplicates.
{LANDED if SPEC == '010' else LANDED11}

## Tests: use the shared harness, do not write another build helper
{HARNESS if SPEC == '010' else HARNESS11}

## Files you own
{', '.join('`'+p+'`' for p in paths)}, `docs/bayan-handoff.md` (Questions {'Q'+q[0]+'–Q'+q[1] if q else 'in your range'} only, inserted in number order), your own `// ── {t} state` sections of {' and '.join('`'+c+'`' for c in SHARED)} (new fields with defaults only). Do not tick the task: after your guard passes, an independent reviewer checks the port against the C# (`scripts/selfhost/review-brief.py`) and `scripts/selfhost/land.sh` merges and ticks it only on `Verdict: PASS`.

{cheat}
## End your final message with
Files changed; test count; the guard's last 3 lines; break-tests (file:line → failing test); Questions added; done / not done / exact next step.
"""
    d = f"{ROOT}/{row['tmp']}{t.lower()}"
    os.makedirs(d, exist_ok=True)
    open(f'{d}/prompt.md', 'w', encoding='utf-8').write(s)
    print(t, len(s), files, q, len(own_api))
