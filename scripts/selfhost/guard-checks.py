# guard-checks.py — the guard's content checks for one spec 010 task (called by guard.sh; prints one failure per line).
#   python3 -I scripts/selfhost/guard-checks.py <base> <task, e.g. T13> <allow globs, comma-separated>
# A  oracle: the task's test class runs the C# implementation and compares with it; no hand-typed expected lines.
# B  stubs: no `// stub: <task>` marker left in an owned file unless it names its blocking Question; no empty bodies.
# C  ctx.byn: changes only inside the task's own `// ── <task> state` section.
# D  gates: no Question that gates this task ("before <task>") is still Open.
# E  docs: a Progress log entry naming the task; every `file.byn:N` a new Question cites exists and shows the quoted code.
import fnmatch, os, re, subprocess, sys

base, task, allow = sys.argv[1], sys.argv[2], sys.argv[3].split(',')
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
owned_byn = [p for p in allow if p.endswith('.byn') and p != 'selfhost/semantics/ctx.byn']
test_files = [p for p in allow if p.endswith('Tests.cs')]

# A — oracle
ORACLE = re.compile(r'\b(Compilation\.(Create|FromPath|FromProject)|Manifest\.(Read|Parse)|DiagnosticRenderer\.|new (Binder|Compilation|Parser)\b'
                    r'|typeof\((Compilation|Binder|Conversions|Conformance)\)|(Conversions|ConstantFolding|TypeFacts|Conformance|BinderExpressions|Lexicon)\.[A-Z]'
                    r'|\.GetType\("Bayan\.Semantics)')
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

# C — ctx.byn sections
if 'selfhost/semantics/ctx.byn' in changed:
    new = read('selfhost/semantics/ctx.byn').split('\n')
    spans, start = [], None
    for i, line in enumerate(new, 1):
        if re.match(rf'^\s*// ── {task} state\b', line):
            start = i
        elif start and re.match(r'^\s*(// ──|\})', line):
            spans.append((start, i)); start = None
    diff = git('diff', '-U0', f'{base}...HEAD', '--', 'selfhost/semantics/ctx.byn') + git('diff', '-U0', '--', 'selfhost/semantics/ctx.byn')
    for m in re.finditer(r'^@@ -\d+(?:,(\d+))? \+(\d+)(?:,(\d+))? @@', diff, re.M):
        old_n, at, cnt = int(m.group(1) or 1), int(m.group(2)), int(m.group(3) or 1)
        lo, hi = at, at + max(cnt, 1) - 1
        if old_n and not cnt:
            fails.append(f'ctx: ctx.byn deletes lines near :{at}; sections only grow'); continue
        if not any(s < lo and hi < e for s, e in spans):
            fails.append(f'ctx: ctx.byn:{lo}-{hi} is outside `// ── {task} state` sections; add fields only there')

# D — gates
hand = read('docs/bayan-handoff.md')
for m in re.finditer(r'^(\d{3})\. (.*)$', hand, re.M):
    text = m.group(2)
    if re.search(rf'\bbefore {task}\b', text) and re.search(r'\bOpen\.\s*$', text):
        fails.append(f'gate: Q{m.group(1)} must be decided before {task} and is still Open; stop and ask the dispatcher')

# E — docs
hdiff = git('diff', f'{base}...HEAD', '--', 'docs/bayan-handoff.md') + git('diff', '--', 'docs/bayan-handoff.md')
added = [l[1:] for l in hdiff.split('\n') if l.startswith('+') and not l.startswith('+++')]
if not any(l.startswith('- **') and re.search(rf'\b{task}\b', l) for l in added):
    fails.append(f'docs: add a Progress log entry for {task} (what landed, what is left, test count)')
for l in added:
    if not re.match(r'^\d{3}\. ', l):
        continue
    for m in re.finditer(r'`?([a-z0-9_]+\.byn)`?\s*:(\d+)(?:\s*`([^`]+)`)?', l):
        name, ln, code = m.group(1), int(m.group(2)), m.group(3)
        path = next((p for p in owned_byn if p.endswith('/' + name)), f'selfhost/semantics/{name}')
        src = read(path).split('\n')
        if ln < 1 or ln > len(src) or src[ln - 1].strip() in ('', '}', '{'):
            fails.append(f'docs: Q{l[:3]} cites {name}:{ln}, which is blank, a brace or past the end; cite the line as it stands now')
        elif code and not any(code.strip() in s for s in src[max(0, ln - 3):ln + 2]):
            fails.append(f'docs: Q{l[:3]} cites {name}:{ln} `{code[:40]}`, which is not on or near that line')

for f in fails:
    print(f)
