# coverage-gate.py — the per-task coverage gate of guard.sh (spec 013 T26).
#   python3 -I scripts/selfhost/coverage-gate.py <merge-base> <bayan-quality.json> <functions.json> [<tasks.md> <T<n>> <dir>]
# Run from the repo root. functions.json, written beside the report by the selfhost harness, gives each counted file's
# functions: their line span and their own covered/total lines and branches (a counter counts in the innermost function
# holding its line). A function is touched when its span holds a line the work tree adds or changes against <merge-base>
# (git diff -U0; a deletion touches the lines on either side of it; a file new since <merge-base> is touched throughout).
# Every line and branch of a touched function must be covered: each missed or unknown one is printed as a FAIL item
# ("- …"), except a missed one under a `// coverage-exempt: Qnnn` marker: it exempts the next line (`… through N`: the next
# line to N) when Question Qnnn of docs/bayan-handoff.md cites <file>:L (or a range) inside that span, for code that is dead in
# the C# too (the Question cites the C# line). A marker whose Question cites none of its span is a FAIL item and exempts nothing;
# an unknown line is never exempt.
# `// coverage-blocked: Qnnn T<k> <fn>` (`… through N` as above; user decision 2026-11-04) exempts a missed line that only runs once
# sibling task T<k> ports its stub <fn> (not dead in the C#: the Question cites the line and the stub). It holds only while T<k> is
# unticked in <tasks.md> and is not the task under guard (<T<n>>): every marker in <dir>'s .byn files naming the guarded task or a
# ticked one is a FAIL item, so the stub's owner covers the lines and removes the marker before it lands, and integration sees none.
# Without the three optional arguments a coverage-blocked marker is a FAIL item.
# A file outside <dir> that exists at <merge-base> is another spec's port (an 011 task editing selfhost/semantics): only the
# lines the work tree adds or changes in it are gated, since the rest of its functions is the owning spec's coverage (Q871). The other functions of the file are one info line (covered/total), never a FAIL: the task's --filter tests are
# not meant to cover other tasks' functions. A source file absent from the work tree has no markers. An unreadable report, function or source file, or a failing git, exits 1 naming it.
import json, os, re, subprocess, sys

WHY = {'missed': 'no test under --filter runs it; add a C# oracle case that does',
       'unknown': 'a counter file was unreadable; run the guard again'}


def changed(base, path):
    """The lines of path the work tree adds or changes against base; None when path is new since base."""
    if subprocess.run(['git', 'cat-file', '-e', f'{base}:{path}'], capture_output=True).returncode != 0:
        return None
    out = subprocess.run(['git', 'diff', '-U0', base, '--', path], capture_output=True, text=True, check=True).stdout
    lines = set()
    for m in re.finditer(r'^@@ -\S+ \+(\d+)(?:,(\d+))? @@', out, re.M):
        start, count = int(m.group(1)), int(m.group(2) or 1)
        lines.update(range(start, start + count) if count else (start, start + 1))
    return lines


BLOCKED = r'// coverage-blocked: Q(\d{3,4}) (T\d+) (\w+)(?: through (\d+))?'


def open_tasks(tasks):
    """The task ids not yet ticked in tasks (a `- [ ] T<n> ` line); None without a tasks file."""
    if tasks is None or not os.path.exists(tasks): return None
    return set(re.findall(r'^- \[ \] (T\d+) ', open(tasks, encoding='utf-8').read(), re.M))


def stale(root, open_, task):
    """A FAIL message per coverage-blocked marker under root whose task is the guarded one or no longer open."""
    bad = []
    for d, _, names in sorted(os.walk(root)):
        for n in sorted(names):
            if not n.endswith('.byn'): continue
            p = os.path.join(d, n)
            for i, text in enumerate(open(p, encoding='utf-8').read().split('\n'), 1):
                m = re.search(BLOCKED, text)
                if not m: continue
                t, fn = m.group(2), m.group(3)
                if t == task:
                    bad.append(f'coverage: {p}:{i} is coverage-blocked on {t} {fn}, the task under guard; cover the lines and remove the marker')
                elif t not in open_:
                    bad.append(f'coverage: {p}:{i} is coverage-blocked on {t} {fn}, but {t} is ticked or not a task; cover the lines and remove the marker')
    return bad


def exempt(path, handoff='docs/bayan-handoff.md', open_=None, task=None):
    """The lines of path its coverage-exempt and coverage-blocked markers exempt (two sets), and a FAIL message per marker that
    exempts nothing."""
    if not os.path.exists(path):
        return set(), set(), []
    lines = open(path, encoding='utf-8').read().split('\n')
    if not any('// coverage-exempt:' in t or '// coverage-blocked:' in t for t in lines):
        return set(), set(), []
    questions = {}
    for q in (open(handoff, encoding='utf-8').read().split('\n') if os.path.exists(handoff) else []):
        m = re.match(r'(\d{3,4})\. ', q)
        if m: questions[m.group(1)] = q
    base, skip, blocked, bad = os.path.basename(path), set(), set(), []
    for i, text in enumerate(lines, 1):
        m, into = re.search(r'// coverage-exempt: Q(\d{3,4})(?: through (\d+))?', text), skip
        if m:
            q, last = m.group(1), m.group(2)
        else:
            m, into = re.search(BLOCKED, text), blocked
            if not m:
                if '// coverage-blocked:' in text:
                    bad.append(f'coverage: {path}:{i} has a coverage-blocked marker not of the form `// coverage-blocked: Qnnn T<k> <fn>[ through N]`')
                continue
            q, t, fn, last = m.groups()
            if open_ is None:
                bad.append(f'coverage: {path}:{i} is coverage-blocked on {t} {fn}, but the gate was given no tasks file'); continue
            if t == task or t not in open_: continue  # stale(): reported once per marker
        last = int(last or i + 1)
        if last <= i or last > len(lines):
            bad.append(f'coverage: {path}:{i} exempts through {last}, which is not after the marker and inside the file'); continue
        cited = [(int(a), int(b or a)) for a, b in re.findall(re.escape(base) + r':(\d+)(?:-(\d+))?', questions.get(q, ''))]
        if not any(a <= last and b >= i + 1 for a, b in cited):
            bad.append(f'coverage: {path}:{i} exempts {i + 1}-{last} for Q{q}, but Q{q} in {handoff} cites no {base} line in that span'); continue
        into.update(range(i + 1, last + 1))
    return skip, blocked, bad


def load(name):
    try:
        with open(name, encoding='utf-8') as f:
            return json.load(f)
    except (OSError, ValueError) as e:
        sys.exit(f'coverage-gate.py: {name} is unreadable: {e}')


def main():
    base, report, functions = sys.argv[1], load(sys.argv[2]), load(sys.argv[3])
    tasks, task, root = (sys.argv[4:7] + [None] * 3)[:3]
    try:
        open_ = open_tasks(tasks)
        if open_ is not None:
            for b in stale(root, open_, task): print('- ' + b)
        files, spans = report['files'], functions['files']
        for f in files:
            p = os.path.relpath(f['path'])
            lines = changed(base, p)
            fns = spans.get(f['path'], [])
            skip, blocked, bad = exempt(p, open_=open_, task=task)
            for b in bad: print('- ' + b)
            touched = [fn for fn in fns if lines is None or any(fn['span'][0] <= n <= fn['span'][1] for n in lines)]
            foreign = root is not None and lines is not None and not (p + '/').startswith(os.path.normpath(root) + '/')
            if foreign: print('coverage: %s: outside %s, so only its changed lines are gated (Q871)' % (p, root))

            def gated(n):
                if foreign: return n in lines
                holding = [fn for fn in fns if fn['span'][0] <= n <= fn['span'][1]]
                return bool(holding) and min(holding, key=lambda fn: fn['span'][1] - fn['span'][0]) in touched

            for group, text in ((touched, 'touched functions'), ([fn for fn in fns if fn not in touched], 'other functions, not gated (no line of theirs changed)')):
                if group:
                    t = [sum(fn[k][i] for fn in group) for k in ('lines', 'branches') for i in (0, 1)]
                    print('coverage: %s: %s: lines %d/%d, branches %d/%d (listing: tmp/guard-run/coverage/%s.cov.txt)' % (p, text, *t, p))
            n_ex, n_bl = 0, 0
            for s in ('missed', 'unknown'):
                for n in f['lines'][s]:
                    if s == 'missed' and n in skip: n_ex += 1; continue
                    if s == 'missed' and n in blocked: n_bl += 1; continue
                    if gated(n): print('- coverage: %s line %s:%d; %s' % (s, p, n, WHY[s]))
                for x in f['branches'][s]:
                    if s == 'missed' and x['line'] in skip: n_ex += 1; continue
                    if s == 'missed' and x['line'] in blocked: n_bl += 1; continue
                    if gated(x['line']):
                        print('- coverage: %s branch %s:%d:%d never %s; %s' % (s, p, x['line'], x['column'], 'true' if x['outcome'] else 'false', WHY[s]))
            if n_ex: print('coverage: %s: %d missed lines and branches under coverage-exempt markers' % (p, n_ex))
            if n_bl: print('coverage: %s: %d missed lines and branches under coverage-blocked markers' % (p, n_bl))
    except OSError as e:
        sys.exit(f'coverage-gate.py: {e}')
    except (KeyError, TypeError, IndexError) as e:
        sys.exit(f'coverage-gate.py: {sys.argv[2]} or {sys.argv[3]} is malformed: {e!r}')
    except subprocess.CalledProcessError as e:
        sys.exit(f'coverage-gate.py: {" ".join(e.cmd)} failed: {e.stderr.strip()}')


main()
