#!/bin/bash
# mutate-batch.sh — the batch mutation run (spec 013 T28, R14): guard.sh's mutation step (mutation-step.sh) over every
# selfhost/**/*.byn with its test filter, then the scores into tests/quality-baseline.json. Run from the repo root after `dotnet build`.
#   bash scripts/selfhost/mutate-batch.sh [--list] [--files a.byn,b.byn] [--shard k/n] [--tasks <tasks.md>]
# A file's filter is derived from --tasks (default docs/specs/010-self-hosted-semantics/010-tasks.md): every task line whose
# `files:` list names the file contributes the *Tests.cs classes of that same list, as FullyQualifiedName~<Class> joined by `|`;
# a glob class name such as SelfhostSemanticsT*Tests.cs is skipped (no FQN substring), and only classes of the file's own family are kept
# (selfhost/semantics: SelfhostSemantics*, parser: SelfhostParser*, lexer: SelfhostLexer*, emitter and its subdirectories cprint/, ltree/: SelfhostEmitter*: another build does not contain the file);
# a file with none left uses SelfhostSemantics, SelfhostParser, SelfhostLexer or SelfhostEmitter; another directory fails. One group per family and shard (Q910):
# its filter is the union of its files' filters, so the tests are recorded once per (shard, family); selfhost/emitter/ and its subdirectories are one family.
# --list prints file -> group filter and runs nothing; --shard k/n gives file i of each family to shard (i mod n)+1.
# Per group: reports and logs in tmp/mutate-batch/<group>/ (quality-<n>-<binary>.json), bayan mutate's summary printed. A group
# whose CLI, recording or mutant 0 fails is named and fails the run. Then quality-baseline.py check over the reports: a score
# below the baseline names the file with old and new score and exits 1 with the baseline untouched; else `update` raises it.
# Each copied report's `path`s are rewritten relative to the repo root (a path outside it fails the group, naming the report);
# CI shards (.github/workflows/mutation.yml) upload only the reports, and a final job runs one `update` over all of them.
set -u
export MSBUILDDISABLENODEREUSE=1 DOTNET_CLI_USE_MSBUILD_SERVER=0  # no MSBuild worker or build server outlives the run
TASKS=docs/specs/010-self-hosted-semantics/010-tasks.md; LIST=0; FILES=""; SHARD=""
while [ $# -gt 0 ]; do case "$1" in
  --list) LIST=1; shift;; --files) FILES=$2; shift 2;; --shard) SHARD=$2; shift 2;; --tasks) TASKS=$2; shift 2;;
  *) echo "unknown arg $1"; exit 2;; esac; done
HERE=$(dirname "$0"); OUT=$PWD/tmp/mutate-batch; BASELINE=tests/quality-baseline.json

# group<TAB>file<TAB>filter per file, in file order, after --files and --shard
PLAN=$(python3 -I - "$TASKS" "$FILES" "$SHARD" <<'PY'
import fnmatch, glob, os, re, sys
tasks, only, shard = sys.argv[1:4]
files = sorted(glob.glob('selfhost/**/*.byn', recursive=True))
defaults = {'selfhost/semantics': 'SelfhostSemantics', 'selfhost/parser': 'SelfhostParser', 'selfhost/lexer': 'SelfhostLexer'}
def family(d):
    return 'SelfhostEmitter' if d == 'selfhost/emitter' or d.startswith('selfhost/emitter/') else defaults.get(d)
def expand(p):
    m = re.search(r'\{([^{}]*)\}', p)
    return [q for o in m.group(1).split(',') for q in expand(p[:m.start()] + o + p[m.end():])] if m else [p]
entries = []  # (patterns, test classes) per task line
try:
    lines = open(tasks, encoding='utf-8').read().splitlines()
except OSError as e:
    sys.exit(f'mutate-batch: cannot read the tasks file: {e}')
for line in lines:
    m = re.match(r'- \[.\] .*?files:(.*?)(?: — covers:|$)', line)
    if not m:
        continue
    toks = re.findall(r'`([^`]+)`', m.group(1))
    pats = [q for t in toks if t.endswith('.byn') for q in expand(t)]
    classes = [os.path.basename(t)[:-3] for t in toks if t.endswith('Tests.cs') and '*' not in t]
    entries.append((pats, classes))
def names(f, p):
    if '/' in p:
        return fnmatch.fnmatchcase(f, p)
    return fnmatch.fnmatchcase(os.path.basename(f), p) and sum(os.path.basename(g) == os.path.basename(f) for g in files) == 1
rows = []
for f in files:
    d = os.path.dirname(f)
    fam = family(d)
    if not fam:
        sys.exit(f'mutate-batch: {f}: the directory {d}/ has no test family (selfhost/semantics, parser, lexer, emitter)')
    cls = {c for pats, classes in entries if any(names(f, p) for p in pats) for c in classes if c.startswith(fam)} or {fam}
    rows.append((f, '|'.join('FullyQualifiedName~' + c for c in sorted(cls))))
fams = {}  # family dir (selfhost/<family>) -> files, in file order
for f, flt in rows:
    fams.setdefault('/'.join(f.split('/')[:2]), []).append((f, flt))
n = 1
if shard:
    m = re.fullmatch(r'(\d+)/(\d+)', shard)
    if not m or not 1 <= int(m.group(1)) <= int(m.group(2)):
        sys.exit(f'mutate-batch: --shard {shard}: want k/n with 1 <= k <= n')
    k, n = int(m.group(1)), int(m.group(2))
if only:
    want = only.split(',')
    for w in want:
        if w not in files:
            sys.exit(f'mutate-batch: --files {w}: not a selfhost .byn file')
for d, fl in fams.items():
    mine = fl[k - 1::n] if shard else fl  # file i goes to shard (i mod n)+1
    mine = [x for x in mine if not only or x[0] in want]  # --files narrows the group before its union filter is built
    if not mine:
        continue
    flt = '|'.join(sorted({c for _, x in mine for c in x.split('|')}))
    name = d.split('/')[-1] + (f'-shard{k}of{n}' if shard else '')
    for f, _ in mine:
        print(f'{name}\t{f}\t{flt}')
PY
) || { echo "$PLAN"; exit 2; }

if [ $LIST = 1 ]; then while IFS=$'\t' read -r g f flt; do echo "$f -> $flt"; done <<< "$PLAN"; exit 0; fi
[ -n "$PLAN" ] || { echo "mutate-batch: no file selected"; exit 2; }
FAIL=(); note() { FAIL+=("$1"); }
. "$HERE/mutation-step.sh"
BAD=0; OK=()
while IFS= read -r g <&3; do
  flt=$(awk -F'\t' -v g="$g" '$1==g{print $3; exit}' <<< "$PLAN")
  mut=$(awk -F'\t' -v g="$g" '$1==g{print $2}' <<< "$PLAN" | paste -sd, -)
  LOG="$OUT/$g"; rm -rf "$LOG"; mkdir -p "$LOG"; FAIL=()
  echo "== group $g ($flt): $mut"
  mutation_step "$LOG" "$flt" "$mut" 0
  # bayan mutate writes absolute source paths: make each report's paths repo-root-relative, so any machine can read it
  if [ ${#FAIL[@]} = 0 ]; then for rp in "$LOG"/quality-*.json; do
    [ -f "$rp" ] && { python3 -I - "$rp" "$PWD" <<'PY' 2> "$LOG/relpath.txt" || note "report: $(tail -1 "$LOG/relpath.txt")"
import json, os, sys
rp, root = sys.argv[1:3]
root = os.path.realpath(root)
report = json.load(open(rp, encoding='utf-8'))
for e in report['files']:
    rel = os.path.relpath(os.path.realpath(os.path.join(root, e['path'])), root)
    if rel == '..' or rel.startswith('../'):
        sys.exit(f'{rp}: path {e["path"]} is not under the repo root {root}')
    e['path'] = rel
open(rp, 'w', encoding='utf-8').write(json.dumps(report, indent=2) + '\n')
PY
    }; done; fi
  if [ ${#FAIL[@]} = 0 ]; then
    OK+=("$g")
  else
    BAD=1
    for x in "${FAIL[@]}"; do echo "group $g: $x"; done
    mkdir -p "$LOG/rejected"
    for rp in "$LOG"/quality-*.json; do
      [ -f "$rp" ] && mv "$rp" "$LOG/rejected/rejected-$(basename "$rp")"
    done
  fi
done 3< <(cut -f1 <<< "$PLAN" | awk '!s[$0]++')
REPORTS=(); for g in ${OK[@]+"${OK[@]}"}; do REPORTS+=("$OUT/$g"/quality-*.json); done
if [ ${#REPORTS[@]} -gt 0 ]; then
  out=$(python3 -I "$HERE/quality-baseline.py" check "$BASELINE" . "${REPORTS[@]}" 2> "$OUT/baseline.txt"); BX=$?
  if [ $BX != 0 ]; then
    [ -n "$out" ] && echo "$out" || echo "baseline: quality-baseline.py check failed on $BASELINE — $(tail -1 "$OUT/baseline.txt")"
    # a refused shard uploads no accepted report: every report it would have applied moves to its group's rejected/
    for g in "${OK[@]}"; do mkdir -p "$OUT/$g/rejected"
      for rp in "$OUT/$g"/quality-*.json; do [ -f "$rp" ] && mv "$rp" "$OUT/$g/rejected/rejected-$(basename "$rp")"; done; done
    echo "mutate-batch: baseline not updated"; exit 1; fi
  python3 -I "$HERE/quality-baseline.py" update "$BASELINE" . "${REPORTS[@]}" || { echo "mutate-batch: baseline update failed"; exit 1; }
  echo "baseline: $BASELINE updated from ${#REPORTS[@]} report(s)"
fi
exit $BAD
