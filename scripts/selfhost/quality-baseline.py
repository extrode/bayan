# quality-baseline.py — the per-file quality baseline of the selfhost tests (spec 013 R14), shared by guard.sh and land.sh.
#   python3 -I scripts/selfhost/quality-baseline.py check  <baseline.json> <root> <bayan-quality.json>...
#   python3 -I scripts/selfhost/quality-baseline.py update <baseline.json> <root> <bayan-quality.json>...
# The baseline is { "version": 1, "files": { "<path relative to root>": { "mutation": <score or null>, "coverage": <ratio or null> } } }.
# A file's mutation score is (killed + timeout) / (killed + timeout + survived) over every report naming it (Gates.Mutation's
# score; marked and not-reached mutants are left out), its coverage the covered / total lines of a coverage report.
# check: prints one line per file whose score fell below its baseline entry and exits 1 if any did; a null on either side
# never fails. update: writes each reported file's score when it has none or it is equal or higher; a lower one is kept.
import json, os, sys


def scores(root, reports):
    counts = {}  # path -> [killed + timeout, survived, covered lines, total lines, has coverage]
    for name in reports:
        with open(name, encoding='utf-8') as f:
            report = json.load(f)
        for entry in report['files']:
            path = os.path.relpath(os.path.realpath(os.path.join(root, entry['path'])), os.path.realpath(root))
            c = counts.setdefault(path, [0, 0, 0, 0, False])
            for m in entry.get('mutants', []):
                if m['status'] in ('killed', 'timeout'):
                    c[0] += 1
                elif m['status'] == 'survived':
                    c[1] += 1
            if report['kind'] == 'coverage':
                c[2] += entry['lines']['covered']
                c[3] += entry['lines']['total']
                c[4] = True
    out = {}
    for path, (k, s, cov, tot, has) in counts.items():
        out[path] = {'mutation': round(k / (k + s), 4) if k + s else None,
                     'coverage': (round(cov / tot, 4) if tot else None) if has else None}
    return out


def main():
    if len(sys.argv) < 4 or sys.argv[1] not in ('check', 'update'):
        print('usage: quality-baseline.py check|update <baseline.json> <root> <bayan-quality.json>...', file=sys.stderr)
        return 2
    command, path, root, reports = sys.argv[1], sys.argv[2], sys.argv[3], sys.argv[4:]
    baseline = {'version': 1, 'files': {}}
    if os.path.exists(path):
        with open(path, encoding='utf-8') as f:
            baseline = json.load(f)
    new = scores(root, reports)
    if command == 'check':
        fell = 0
        for file, s in sorted(new.items()):
            old = baseline['files'].get(file, {})
            for kind in ('mutation', 'coverage'):
                if old.get(kind) is not None and s[kind] is not None and s[kind] < old[kind]:
                    print(f'baseline: {kind} score of {file} fell from {old[kind]} to {s[kind]} ({path})')
                    fell = 1
        return fell
    for file, s in new.items():
        entry = baseline['files'].setdefault(file, {'mutation': None, 'coverage': None})
        for kind in ('mutation', 'coverage'):
            if s[kind] is not None and (entry.get(kind) is None or s[kind] >= entry[kind]):
                entry[kind] = s[kind]
    baseline['files'] = dict(sorted(baseline['files'].items()))
    with open(path, 'w', encoding='utf-8') as f:
        f.write(json.dumps(baseline, indent=2) + '\n')
    return 0


sys.exit(main())
