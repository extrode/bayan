#!/bin/bash
# land.sh — the only way a spec 010 (or 011) task reaches the base branch. Run by the dispatcher, from the repo root, on the base branch.
#   bash scripts/selfhost/land.sh T13 feat/010-t13            (spec 010)
#   bash scripts/selfhost/land.sh 011 T3 feat/011-t3          (a row of specs.tsv, named, never guessed from the branch)
#   bash scripts/selfhost/land.sh --check-scope [spec] T13 feat/010-t13   (only the scope check: no review, no guard run, no merge)
# The paths below are those of spec 010; spec 011 reads its own from its specs.tsv row (dir, tasks file, review/land/tick subjects, tmp dir).
# Refuses unless: (1) the branch leaves scripts/selfhost/ and tests/quality-baseline.json (written only by land.sh and the batch mutation run, never by a task branch) alone and the base checkout's guard passes on it, (2)
# docs/specs/010-self-hosted-semantics/reviews/<T>.md exists on that branch, the branch's last commit touches only it and is
# titled `docs(010): <T> review (PASS, …`, the file names that commit's parent, says `Verdict: PASS`, and lists no open HIGH
# finding (earlier commits may hold earlier review rounds). Then merges the
# commit it checked (refusing if the branch moved meanwhile) with --no-ff and ticks the task. When the guard run left a coverage
# report (tmp/guard-run/coverage/bayan-quality.json, written whenever the branch changed a selfhost .byn), after the merge the
# tick commit raises the baseline's coverage and branches (update; a lower value never replaces an entry): a filtered per-task run
# can only raise the recorded coverage, and the drop check waits for a full-suite measurement (work/todo.md).
# No mutation runs here (--mutate is dropped): the mutation scores come from the batch run (013 T28).
set -eu
CHECK=0; [ "${1:-}" = --check-scope ] && { CHECK=1; shift; }
if [ $# -eq 3 ]; then SPEC=$1; shift; else SPEC=010; fi
T=$1; BR=$2; N=${T#T}; t=$(echo "$T" | tr '[:upper:]' '[:lower:]')
ROW=$(awk -F'\t' -v s="$SPEC" 'NR>1 && $1==s' "$(dirname "$0")/specs.tsv")
[ -n "$ROW" ] || { echo "LAND REFUSED: no row for spec $SPEC in scripts/selfhost/specs.tsv"; exit 1; }
IFS=$'\t' read -r _ DIR TASKS _ _ _ _ _ _ REVSUB LANDSUB TICKSUB TMPP <<< "$ROW"
BASE=$(git rev-parse --abbrev-ref HEAD)
SHA=$(git rev-parse "$BR")
[ -z "$(git status --porcelain --untracked-files=no)" ] || { echo "LAND REFUSED: the base worktree has uncommitted changes"; exit 1; }
PATH="/usr/bin:$PATH" python3 -I scripts/selfhost/wave-brief.py . "$T" --spec "$SPEC" > /dev/null
# the base checkout's guard, by absolute path: a branch cannot run a guard it rewrote
LINE=$(grep -m1 '^bash scripts/selfhost/guard.sh' "$TMPP$t/prompt.md") || {
  echo "LAND REFUSED: $TMPP$t/prompt.md has no line starting 'bash scripts/selfhost/guard.sh' (wave-brief.py writes it from $T's guard line in $TASKS)"; exit 1; }
# the prompt's words (single quotes stripped) become array elements; --mutate/--mutations/--seed and their values are dropped
# (per-task landing runs no mutation, user decision 2026-11-01), --base is the base branch; nothing is re-parsed by a shell, so a path with & | ' runs the right guard
read -ra W <<< "${LINE#bash scripts/selfhost/guard.sh}"
GUARD=(bash "$PWD/scripts/selfhost/guard.sh"); i=0
while [ $i -lt ${#W[@]} ]; do
  case "${W[$i]}" in
    --base) GUARD+=(--base "$BASE"); i=$((i+2));;
    --mutate|--mutations|--seed) i=$((i+2));;
    *) w=${W[$i]}; w=${w#\'}; GUARD+=("${w%\'}"); i=$((i+1));;
  esac
done
REVIEW=$DIR/reviews/$T.md
CHANGED=$(git diff --name-only "$BASE...$SHA" -- scripts/selfhost/) || { echo "LAND REFUSED: cannot diff $BR against $BASE (no merge base?)"; exit 1; }
[ -z "$CHANGED" ] || { echo "LAND REFUSED: $BR changes scripts/selfhost/; the guard and the landing scripts change only on the base"; exit 1; }
[ -z "$(git diff --name-only "$BASE...$SHA" -- tests/quality-baseline.json)" ] \
  || { echo "LAND REFUSED: $BR edits tests/quality-baseline.json; only land.sh and the batch mutation run write it (revert the edit on the branch)"; exit 1; }

WT=tmp/land-$t; rm -rf "$WT"; git worktree prune
git worktree add -q --detach "$WT" "$SHA"
cleanup() { git worktree remove --force "$WT" 2>/dev/null || true; }
trap cleanup EXIT

if [ $CHECK = 1 ]; then
  out=$(cd "$WT" && "${GUARD[@]}" --scope-only) || { echo "$out"; echo "LAND REFUSED: $BR is outside the scope of spec $SPEC"; exit 1; }
  echo "SCOPE OK $T"; exit 0
fi
( cd "$WT"
  [ -f "$REVIEW" ] || { echo "LAND REFUSED: $REVIEW is missing; run the reviewer (scripts/selfhost/review-brief.py) first"; exit 1; }
  # earlier review rounds may touch the file; the last commit is the PASS round alone (writer auto-commits are chore(worker))
  [ "$(git diff --name-only "$SHA^" "$SHA")" = "$REVIEW" ] && case "$(git log -1 --format=%s "$SHA")" in "$REVSUB $T review (PASS"*) ;; *) false;; esac \
    || { echo "LAND REFUSED: the branch's last commit must touch only $REVIEW, with the subject '$REVSUB $T review (PASS, …)'"; exit 1; }
  parent=$(git rev-parse "$SHA^")
  rc=$(grep -m1 -oE '^Commit: [0-9a-f]{7,40}' "$REVIEW" | cut -d' ' -f2 || true)
  [ -n "$rc" ] && [[ $parent == $rc* ]] \
    || { echo "LAND REFUSED: $REVIEW reviews ${rc:-no commit}, not $parent, the commit under the review; re-review after the last change"; exit 1; }
  grep -q '^Verdict: PASS' "$REVIEW" || { echo "LAND REFUSED: $REVIEW does not say 'Verdict: PASS'"; exit 1; }
  if grep -E '^\| *[0-9]+ *\| *HIGH *\|' "$REVIEW" | grep -vqE '\| *(fixed|resolved: Q[0-9]{3}) *\|? *$'; then
    echo "LAND REFUSED: $REVIEW lists a HIGH finding that is neither fixed nor resolved by a Question"; exit 1; fi
  "${GUARD[@]}" | tail -25 | tee "../guard-land-$t.txt"
  grep -q '^GUARD PASS' "../guard-land-$t.txt" || { echo "LAND REFUSED: guard failed"; exit 1; }
)

COVREPORT=$WT/tmp/guard-run/coverage/bayan-quality.json
# the update after the merge must not fail half-way (merge done, tick uncommitted): try it on a copy first
if [ -f "$COVREPORT" ]; then
  mkdir -p tmp; cp tests/quality-baseline.json "tmp/land-$t-baseline.json"
  python3 -I "$(dirname "$0")/quality-baseline.py" update "tmp/land-$t-baseline.json" "$WT" "$COVREPORT" 2> "tmp/land-$t-baseline.txt" \
    || { echo "LAND REFUSED: quality-baseline.py failed on $COVREPORT — $(tail -1 "tmp/land-$t-baseline.txt")"; exit 1; }
fi
[ "$(git rev-parse "$BR")" = "$SHA" ] || { echo "LAND REFUSED: $BR moved from $SHA while it was checked; land again"; exit 1; }
git merge --no-ff -m "$LANDSUB $T" "$SHA"
perl -i -pe "s/^- \[ \] $T /- [x] $T /" "$DIR/$TASKS"
[ ! -f "$COVREPORT" ] || python3 -I "$(dirname "$0")/quality-baseline.py" update tests/quality-baseline.json "$WT" "$COVREPORT"
git commit -qam "$TICKSUB $T" && echo "LANDED $T"
