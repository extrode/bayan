# Usage: python3 -I scripts/selfhost/wave-brief.py <repo-or-worktree> T10 [T11 ...] [--spec 011] — writes tmp/010-<t>/prompt.md for each task:
# a self-contained writer brief for one spec 010 task (task text, plan rows, its 010-api.md entry points, guard command).
# The spec is a row of specs.tsv (paths, shared-state files, test-class prefix, base branch); without --spec it is 010.
import fcntl, os, re, subprocess, sys

args = sys.argv[1:]
SPEC = '010'
if '--spec' in args:
    i = args.index('--spec')
    SPEC = args[i + 1]
    del args[i:i + 2]
BRIEF = {'010': dict(desc='the Bayan type checker ported from C# `src/Bayan.Semantics/` into Bayan under `selfhost/semantics/`', landed='''Landed and callable: `ctx.byn` (T1), `codes.byn`/`diag.byn` (T2: `report`, `addDiag`, `pushBag`, `popBag`), `symbols.byn`/`types.byn` (T3), `bound.byn` (T4: `add<Kind>`, `children`, `exprType`, `Constant`), `bigint.byn` (T5: labels `bigCmp(a, as: b)`, `bigAdd(a, by: b)`, `bigFits(n, to: bits, signed: s)`; see Q540).''', harness='''`tests/Bayan.Tests/SelfhostSemanticsHarness.cs`: `SelfhostSemanticsHarness.RunSections(mainSource)` builds `selfhost/semantics` with your generated `main.byn` (a C# string) and returns its output split at lines `== <section>`; `SelfhostSemanticsHarness.AssertSection(sections, "<section>", expectedLines)` names the first differing line. Build once per class (`static readonly Lazy<…>`). The oracle is the C# implementation: run the same input through `Bayan.Semantics` in-process (`Compilation.Create`, or the C# class directly when it is public/internal-visible) and print the same lines. Every ported function gets a compared case including a failure/diagnostic path; diagnostics compare as `code line:col message`.''', oracle='SelfhostSemanticsT10Tests.Expected'),
         '011': dict(desc='the Bayan lowering and C emitter ported from C# `src/Bayan.Lowering/` and `src/Bayan.Backend.C/` into Bayan under `selfhost/emitter/`', landed="Landed and callable: see `docs/specs/011-self-hosted-emitter/011-api.md` and the task's `after:` list; the 010 checker modules under `selfhost/semantics/` are public to you.", harness='''`tests/Bayan.Tests/SelfhostEmitterHarness.cs`: `SelfhostEmitterHarness.Build` compiles `selfhost/emitter/**/*.byn` once per run in `fast` mode and `Run(input, mode, tests)` starts the binary and returns stdout/stderr/exit; `AssertSame(name, expected, actual)` reports the first differing line. Unit tests of a wave task run the task's functions through a generated `main` with the C# as oracle (`CliApp.EmitC(Compilation.Create([source], dir), mode)`). Every ported function gets a compared case including a failure path. If the harness file is not landed yet, read `docs/specs/011-self-hosted-emitter/011-plan.md` (Test harness).''', oracle='SelfhostEmitterHarness.AssertSame'),
         '012': dict(desc='the Bayan compiler driver ported from C# `src/Bayan.Cli/` (`CliApp`) into Bayan under `selfhost/driver/`, compiled with package root `selfhost/` (stage 1 of the bootstrap)', landed='Landed and callable: see `docs/specs/012-self-hosted-bootstrap/012-api.md` and the task\'s `after:` list; the lexer, parser, semantics and emitter modules under `selfhost/` are public to you as `012-plan.md` D2 says.', harness='Not landed yet: `tests/Bayan.Tests/SelfhostDriverHarness.cs` is created by T1, as `012-tasks.md` T1 and `012-plan.md` define it. It builds stage 1 once with `Compilation.Create(sources, packageRoot: selfhost/)` like `SelfhostEmitterHarness.cs:28-36` and runs it as a process. Expected values come from `CliApp.Run`/`CliApp.EmitC` or the C# golden tests. Every ported function gets a compared case including a failure path.', oracle='SelfhostDriverHarness')}
if SPEC not in BRIEF: sys.exit(f'wave-brief: no brief text for spec {SPEC}')

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
    files = re.search(r'files: (.*?) — (?:covers|after|done when)', head).group(1).split(' — C#:')[0]  # 011 names the C# range after the files
    files = re.sub(r'\([^()]*(\([^()]*\)[^()]*)*\)', '', files)
    return re.findall(r'`([^`]+\.(?:byn|cs|md|c|h|diag|txt|stdout|stderr|exitcode|sha256))`', files)  # golden expected files too (014 T6)


def plan_rows(byns):
    rows = []
    for line in plan:
        m = re.match(r'^\| (`[^|]+`) \|', line)
        if m and any(f'`{b}`' in m.group(1) for b in byns):
            rows.append(line)
    return rows


def qrange(block):
    m = re.search(r'Q(\d{3,4})–Q(\d{3,4})', ' '.join(block))
    return (m.group(1), m.group(2)) if m else None


def git(*a):
    return subprocess.run(['git', '-C', wt, *a], capture_output=True).stdout


# Question ranges (plan D6): an open task with none in its line gets the next five numbers after every Question in this
# checkout's handoff, every local branch's handoff, every range in a tasks file and every range already assigned, recorded in
# tmp/q-ranges.tsv of the main checkout (BAYAN_QRANGES overrides) under a lock, so briefs written at once do not overlap
QRANGES = os.environ.get('BAYAN_QRANGES') or os.path.join(os.path.dirname(git('rev-parse', '--path-format=absolute', '--git-common-dir').decode().strip() or os.path.join(wt, '.git')), 'tmp', 'q-ranges.tsv')


def taken():
    texts = [open(os.path.join(wt, 'docs/bayan-handoff.md'), encoding='utf-8').read() if os.path.exists(os.path.join(wt, 'docs/bayan-handoff.md')) else '']
    refs = git('for-each-ref', 'refs/heads', '--format=%(refname:short)').decode().split()
    out = subprocess.run(['git', '-C', wt, 'cat-file', '--batch'], input=''.join(f'{r}:docs/bayan-handoff.md\n' for r in refs).encode(), capture_output=True).stdout
    while out:
        head, _, out = out.partition(b'\n')
        if head.endswith(b' missing') or not head:
            continue
        size = int(head.split()[2]); texts.append(out[:size].decode('utf-8', 'replace')); out = out[size + 1:]
    nums = [int(n) for x in texts for n in re.findall(r'^(\d{3,4})\. ', x, re.M)]
    sd = os.path.join(wt, 'docs/specs')
    for d in os.listdir(sd) if os.path.isdir(sd) else []:
        for f in os.listdir(os.path.join(sd, d)):
            if f.endswith('-tasks.md'):
                nums += [int(n) for pair in re.findall(r'Q(\d{3,4})–Q(\d{3,4})', open(os.path.join(sd, d, f), encoding='utf-8').read()) for n in pair]
    return nums


def assign(t):
    os.makedirs(os.path.dirname(QRANGES), exist_ok=True)
    with open(QRANGES, 'a+', encoding='utf-8') as f:
        fcntl.flock(f, fcntl.LOCK_EX)
        f.seek(0)
        rows = [l.split('\t') for l in f.read().splitlines()[1:] if l]
        mine = next((r for r in rows if r[0] == SPEC and r[1] == t), None)
        if mine: return (mine[2], mine[3])
        top = max(taken() + [int(x) for r in rows for x in r[2:4]] + [0])
        if os.fstat(f.fileno()).st_size == 0: f.write('spec\ttask\tfirst\tlast\n')
        f.write(f'{SPEC}\t{t}\t{top + 1}\t{top + 5}\n')
        return (str(top + 1), str(top + 5))


for t in args[1:]:
    block = task_block(t)
    assert block, t
    files = owned_files(block)
    # a root file (CHANGELOG.md) and a bare spec file (010-spec.md) are docs this spec owns too
    files = [f"{row['dir']}/{f}" if re.fullmatch(rf'{SPEC}-[a-z]+\.md', f) else f for f in files]
    rooted = [f for f in files if f.startswith(('tests/', 'docs/', 'src/', 'work/')) or re.fullmatch(r'[A-Z]+\.md', f)]
    byns = [f for f in files if f.endswith('.byn') and f not in rooted]
    tests = [f for f in files if f.endswith('.cs') and f not in rooted]
    # a task naming its test files only with their directory (`tests/Bayan.Tests/X.cs`, 012 T4) is gated by those classes,
    # not by a `<prefix><n>Tests` class it does not have (Q945); a task with bare test names keeps them alone (011 T14 re-pins)
    if not tests: tests = [f.removeprefix('tests/Bayan.Tests/') for f in rooted if re.fullmatch(r'tests/Bayan\.Tests/[A-Za-z0-9]+Tests\.cs', f)]
    paths = [f if f.startswith('selfhost/') else f"{row['bydir']}/{f}" for f in byns] + [f'tests/Bayan.Tests/{f}' for f in tests] + rooted
    allow = ','.join(dict.fromkeys(paths + ['docs/bayan-handoff.md', f"{row['dir']}/{row['tasks']}"]))
    own_api = [l for l in api if re.match(rf'^- {t} ', l)]
    q = qrange(block)
    if not q and SPEC != '010' and block[0].startswith('- [ ]'): q = assign(t)
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
    if SPEC != '010': proto = proto.replace('`selfhost/semantics/ctx.byn`', sharedx).replace('nothing else in ctx.byn', 'nothing else in those files').replace('SelfhostSemanticsT10Tests.Expected', BRIEF[SPEC]['oracle'])
    proto = proto.replace('SelfhostSemanticsT3Tests', test_class).replace('restore with `git checkout -- <file>`', 'restore by copying back a copy you took first (`cp f tmp/f.orig` … `cp tmp/f.orig f`; never `git checkout`, which reverts to the stub and loses your work)').replace('Skeleton by turn 12', 'Skeleton by turn 15')
    pre = '' if SPEC == '010' else f"""Preflight (after the guard passes, before you return; `land.sh` runs it again and refuses on any FAIL):
```
bash scripts/selfhost/preflight.sh {SPEC} {t} "$(git rev-parse --abbrev-ref HEAD)"
```
Every line must say `ok` except `wp1`, which the dispatcher runs after your commit. It checks that each `file:line` you added cites the line as it stands after your last edit and that the backticked code you quote with it is on that line, that every file you changed is in your files list (if not, put the addition it prints in your final message; do not edit the tasks file), that your Questions are new numbers inside {'Q'+q[0]+'–Q'+q[1] if q else 'your range'}, and that your test classes and the cross-cutting ones (`CInventoryTests`, `SelfhostDriverT8Tests`, `SelfhostDriverRuntimeTests`, `plan-strings-check.js`) pass, none matching zero tests. Paste its output into your summary.
"""
    s = f"""You are doing task {t} of spec {SPEC} ({BRIEF[SPEC]['desc']}). Other writers port the sibling files in parallel; every cross-file function already exists as a compiling stub with its final signature, so you call real names and never write stand-ins. A separate reviewer compares your port with the C# line by line before it lands; invented logic, sibling fallbacks and hand-typed test expectations fail that review.

{proto}

Guard (run before you finish):
```
bash scripts/selfhost/guard.sh --base {BASE} --allow '{allow}' --filter '{filt}' --task {t}{'' if SPEC == '010' else ' --spec ' + SPEC}
```
{pre}Do not commit, merge, checkout, reset or stash; the dispatcher commits for you.

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
{BRIEF[SPEC]['landed']}

## Tests: use the shared harness, do not write another build helper
{BRIEF[SPEC]['harness']}

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
