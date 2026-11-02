# coverage-gate.py — the per-task coverage gate of guard.sh (spec 013 T26).
#   python3 -I scripts/selfhost/coverage-gate.py <merge-base> <bayan-quality.json> <functions.json>
# Run from the repo root. functions.json, written beside the report by the selfhost harness, gives each counted file's
# functions: their line span and their own covered/total lines and branches (a counter counts in the innermost function
# holding its line). A function is touched when its span holds a line the work tree adds or changes against <merge-base>
# (git diff -U0; a deletion touches the lines on either side of it; a file new since <merge-base> is touched throughout).
# Every line and branch of a touched function must be covered: each missed or unknown one is printed as a FAIL item
# ("- …"). The other functions of the file are one info line (covered/total), never a FAIL: the task's --filter tests are
# not meant to cover other tasks' functions. An unreadable report or function file, or a failing git, exits 1 naming it.
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


def load(name):
    try:
        with open(name, encoding='utf-8') as f:
            return json.load(f)
    except (OSError, ValueError) as e:
        sys.exit(f'coverage-gate.py: {name} is unreadable: {e}')


def main():
    base, report, functions = sys.argv[1], load(sys.argv[2]), load(sys.argv[3])
    try:
        files, spans = report['files'], functions['files']
        for f in files:
            p = os.path.relpath(f['path'])
            lines = changed(base, p)
            fns = spans.get(f['path'], [])
            touched = [fn for fn in fns if lines is None or any(fn['span'][0] <= n <= fn['span'][1] for n in lines)]

            def gated(n):
                holding = [fn for fn in fns if fn['span'][0] <= n <= fn['span'][1]]
                return bool(holding) and min(holding, key=lambda fn: fn['span'][1] - fn['span'][0]) in touched

            for group, text in ((touched, 'touched functions'), ([fn for fn in fns if fn not in touched], 'other functions, not gated (no line of theirs changed)')):
                if group:
                    t = [sum(fn[k][i] for fn in group) for k in ('lines', 'branches') for i in (0, 1)]
                    print('coverage: %s: %s: lines %d/%d, branches %d/%d (listing: tmp/guard-run/coverage/%s.cov.txt)' % (p, text, *t, p))
            for s in ('missed', 'unknown'):
                for n in f['lines'][s]:
                    if gated(n): print('- coverage: %s line %s:%d; %s' % (s, p, n, WHY[s]))
                for x in f['branches'][s]:
                    if gated(x['line']):
                        print('- coverage: %s branch %s:%d:%d never %s; %s' % (s, p, x['line'], x['column'], 'true' if x['outcome'] else 'false', WHY[s]))
    except (KeyError, TypeError, IndexError) as e:
        sys.exit(f'coverage-gate.py: {sys.argv[2]} or {sys.argv[3]} is malformed: {e!r}')
    except subprocess.CalledProcessError as e:
        sys.exit(f'coverage-gate.py: {" ".join(e.cmd)} failed: {e.stderr.strip()}')


main()
