#!/bin/bash
# land.sh — the only way a spec 010 task reaches the base branch. Run by the dispatcher, from the repo root, on the base branch.
#   bash scripts/selfhost/land.sh T13 feat/010-t13
# Refuses unless: (1) the guard passes on the task branch, (2) docs/specs/010-self-hosted-semantics/reviews/<T>.md exists on
# that branch, names the branch's head commit (or its parent, when the review file is the last commit), says
# `Verdict: PASS`, and lists no open HIGH finding. Then merges with --no-ff and ticks the task.
set -eu
T=$1; BR=$2; N=${T#T}
BASE=$(git rev-parse --abbrev-ref HEAD)
[ -z "$(git status --porcelain --untracked-files=no)" ] || { echo "LAND REFUSED: the base worktree has uncommitted changes"; exit 1; }
python3 -I scripts/selfhost/wave-brief.py . "$T" > /dev/null
GUARD=$(grep -m1 '^bash scripts/selfhost/guard.sh' "tmp/010-${T,,}/prompt.md" | sed "s/--base [^ ]*/--base $BASE/")
REVIEW=docs/specs/010-self-hosted-semantics/reviews/$T.md

WT=tmp/land-${T,,}; rm -rf "$WT"; git worktree prune
git worktree add -q --detach "$WT" "$BR"
cleanup() { git worktree remove --force "$WT" 2>/dev/null || true; }
trap cleanup EXIT

( cd "$WT"
  [ -f "$REVIEW" ] || { echo "LAND REFUSED: $REVIEW is missing; run the reviewer (scripts/selfhost/review-brief.py) first"; exit 1; }
  head=$(git rev-parse HEAD); parent=$(git rev-parse HEAD^)
  rc=$(grep -m1 -oE '^Commit: [0-9a-f]{7,40}' "$REVIEW" | cut -d' ' -f2 || true)
  [ -n "$rc" ] && { [[ $head == $rc* ]] || { [[ $parent == $rc* ]] && [ "$(git diff --name-only HEAD^ HEAD)" = "$REVIEW" ]; }; } \
    || { echo "LAND REFUSED: $REVIEW reviews ${rc:-no commit}, not the branch head $head; re-review after the last change"; exit 1; }
  grep -q '^Verdict: PASS' "$REVIEW" || { echo "LAND REFUSED: $REVIEW does not say 'Verdict: PASS'"; exit 1; }
  if grep -E '^\| *[0-9]+ *\| *HIGH *\|' "$REVIEW" | grep -vqE '\| *(fixed|resolved: Q[0-9]{3}) *\|? *$'; then
    echo "LAND REFUSED: $REVIEW lists a HIGH finding that is neither fixed nor resolved by a Question"; exit 1; fi
  eval "$GUARD" | tail -25 | tee "../guard-land-${T,,}.txt"
  grep -q '^GUARD PASS' "../guard-land-${T,,}.txt" || { echo "LAND REFUSED: guard failed"; exit 1; }
)

git merge --no-ff -m "chore(010): land $T" "$BR"
sed -i -E "s/^- \[ \] $T /- [x] $T /" docs/specs/010-self-hosted-semantics/010-tasks.md
git commit -qam "chore(010): tick $T" && echo "LANDED $T"
