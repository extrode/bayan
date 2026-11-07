# guard-checks.py — the guard's content checks for one spec 010 task (called by guard.sh; prints one failure per line).
#   python3 -I scripts/selfhost/guard-checks.py <base> <task, e.g. T13> <allow globs, comma-separated> [spec, default 010]
# The spec is a row of specs.tsv: its shared-state files (section check C) and the directory a bare .byn name resolves in.
# A  oracle: the task's test class runs the C# implementation and compares with it; no hand-typed expected lines.
# B  stubs: no `// stub: <task>` marker left in an owned file unless it names its blocking Question; no empty bodies.
# C  ctx.byn: changes only inside the task's own `// ── <task> state` section (its header line counts as inside).
# D  gates: no Question that gates this task ("before <task>") is still Open.
# E  docs: a Progress log entry naming the task; every `[dir/]file.byn:N` a new Question cites exists and shows the quoted code.
import fnmatch, os, re, subprocess, sys

base, task, allow = sys.argv[1], sys.argv[2], sys.argv[3].split(',')
HEAD, *TABLE = [l.split('\t') for l in open(os.path.join(os.path.dirname(os.path.abspath(__file__)), 'specs.tsv'), encoding='utf-8').read().splitlines()]
row = dict(zip(HEAD, next(r for r in TABLE if r[0] == (sys.argv[4] if len(sys.argv) > 4 else '010'))))
shared = row['shared'].split(',')
n = task[1:]
fails = []


def git(*args):
    return subprocess.run(['git', *args], capture_output=True, text=True).stdout


def read(path):
    try:
        return open(path, encoding='utf-8').read()
    except OSError:
        return ''


changed = sorted(set(git('diff', '--name-only', f'{base}...HEAD').split() + git('diff', '--name-only').split()
                     + git('ls-files', '--others', '--exclude-standard').split()))
owned_byn = [p for p in allow if p.endswith('.byn') and p not in shared]
test_files = [p for p in allow if p.endswith('Tests.cs')]

# A — oracle
ORACLE = re.compile(r'\b(Compilation\.(Create|FromPath|FromProject)|Manifest\.(Read|Parse)|DiagnosticRenderer\.|new (Binder|Compilation|Parser)\b'
                    r'|typeof\((Compilation|Binder|Conversions|Conformance)\)|(Conversions|ConstantFolding|TypeFacts|Conformance|BinderExpressions|Lexicon)\.[A-Z]'
                    r'|\.GetType\("Bayan\.Semantics'
                    r'|CliApp\.(Run|EmitC)\(|SyntaxTree\.Parse\(|SelfhostEmitterHarness\.SameAsCli\()')  # 011: the C# CLI and parser, and the harness over the CLI
HAND = re.compile(r'(\bexpected\w*\s*=\s*(new\s*(List<string>)?\s*(\(\))?\s*\{|\[)\s*")|(AssertSection\([^;]*,\s*(new\s*(List<string>)?\s*\{|\[)\s*")')
for t in test_files:
    src = read(t)
    if not src:
        continue
    body = '\n'.join(l for l in src.split('\n') if not l.lstrip().startswith(('using ', '///', '//')))
    if not ORACLE.search(body):
        fails.append(f'oracle: {t} never runs the C# implementation; build each expected line from `Bayan.Semantics` (see SelfhostSemanticsT10Tests.Expected) instead of typing it')
    exempt = 0
    tl = src.split('\n')
    for i, line in enumerate(tl, 1):
        nxt = tl[i].lstrip() if i < len(tl) else ''
        opens = re.search(r'(\bexpected\w*\s*=\s*new\s*(List<string>)?\s*(\(\))?|AssertSection\([^;]*,\s*new\s*(List<string>)?)\s*\{?\s*$', line)
        if HAND.search(line) or (opens and nxt.startswith(('"', '{ "'))):
            if re.search(r'// no-oracle: Q\d{3}', line) and exempt < 2:
                exempt += 1
                continue
            fails.append(f'oracle: {t}:{i} hand-typed expected lines; compute them from the C# oracle (at most 2 exemptions, each `// no-oracle: Qnnn`)')

# B — stubs left behind, empty bodies
for f in owned_byn:
    lines = read(f).split('\n')
    for i, line in enumerate(lines, 1):
        if line.startswith(f'// stub: {task} ') and not re.search(r'blocked: Q\d{3}', line):
            fails.append(f'stub: {f}:{i} still marked `{line[:60]}`; port it and delete the marker, or append `(blocked: Qnnn)` naming the Question that blocks it')
    for i in range(len(lines) - 1):
        if re.match(r'^[A-Za-z].*\)\s*(->\s*\S+\s*)?\{\s*$', lines[i]) and lines[i + 1].strip() == '}':
            prev = lines[i - 1] if i > 0 else ''
            if 'blocked: Q' not in prev:
                fails.append(f'stub: {f}:{i + 1} has an empty body; port it, or mark it `// stub: {task} … (blocked: Qnnn)`')

# C — ctx.byn sections (each shared-state file of the spec)
for ctx in shared:
    if ctx not in changed:
        continue
    name = os.path.basename(ctx)
    new = read(ctx).split('\n')
    spans, start = [], None
    for i, line in enumerate(new, 1):
        if re.match(rf'^\s*// ── {task} state\b', line):
            start = i
        elif start and re.match(r'^\s*(// ──|\})', line):
            spans.append((start, i)); start = None
    # one diff from the merge base to the working tree, numbered as the spans are (as in E)
    diff = git('diff', '-U0', git('merge-base', base, 'HEAD').strip(), '--', ctx)
    for m in re.finditer(r'^@@ -\d+(?:,(\d+))? \+(\d+)(?:,(\d+))? @@', diff, re.M):
        old_n, at, cnt = int(m.group(1) or 1), int(m.group(2)), int(m.group(3) or 1)
        lo, hi = at, at + max(cnt, 1) - 1
        if old_n and not cnt:
            fails.append(f'ctx: {name} deletes lines near :{at}; sections only grow'); continue
        # a hunk may start on its section's header: a task adds its own `// ── Tn state` line with its first field
        if not any(s <= lo and hi < e for s, e in spans):
            fails.append(f'ctx: {name}:{lo}-{hi} is outside `// ── {task} state` sections; add fields only there')

# D — gates
hand = read('docs/bayan-handoff.md')
for m in re.finditer(r'^(\d{3})\. (.*)$', hand, re.M):
    text = m.group(2)
    if re.search(rf'\bbefore {task}\b', text) and re.search(r'\bOpen\.\s*$', text):
        fails.append(f'gate: Q{m.group(1)} must be decided before {task} and is still Open; stop and ask the dispatcher')

# E — docs
# One diff from the merge base to the working tree: a line fixed since the last commit is checked as it stands now.
hdiff = git('diff', git('merge-base', base, 'HEAD').strip(), '--', 'docs/bayan-handoff.md')
added = [l[1:] for l in hdiff.split('\n') if l.startswith('+') and not l.startswith('+++')]
if not any(l.startswith('- **') and re.search(rf'\b{task}\b', l) for l in added):
    fails.append(f'docs: add a Progress log entry for {task} (what landed, what is left, test count)')
for l in added:
    if not re.match(r'^\d{3}\. ', l):
        continue
    for m in re.finditer(r'`?((?:[A-Za-z0-9_.-]+/)*[a-z0-9_]+\.byn)`?\s*:(\d+)(?:\s*`([^`]+)`)?', l):
        name, ln, code = m.group(1), int(m.group(2)), m.group(3)
        # a cited directory path is exact; a bare basename is looked up in the owned files, then the spec's directory
        path = name if '/' in name else next((p for p in owned_byn if p.endswith('/' + name)), f"{row['bydir']}/{name}")
        if not os.path.exists(path):
            fails.append(f'docs: Q{l[:3]} cites {name}:{ln}, but {path} does not exist; cite an existing file')
            continue
        src = read(path).split('\n')
        if ln < 1 or ln > len(src) or src[ln - 1].strip() in ('', '}', '{'):
            fails.append(f'docs: Q{l[:3]} cites {name}:{ln}, which is blank, a brace or past the end; cite the line as it stands now')
        elif code and not any(code.strip() in s for s in src[max(0, ln - 3):ln + 2]):
            fails.append(f'docs: Q{l[:3]} cites {name}:{ln} `{code[:40]}`, which is not on or near that line')

for f in fails:
    print(f)
