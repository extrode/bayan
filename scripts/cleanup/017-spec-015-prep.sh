#!/usr/bin/env bash
# Cleanup after the spec 015 prep work (decisions S1-S12/D1-D4, CI chore, parity baselines) and 013 T19, 2026-11-02.
# 1. worktrees of the p015 parity workers (two failed agy runs left empty ones), and their branches, when dev holds them;
# 2. this work's branches that never had a worktree, when dev holds them;
# 3. (--delete-scratch) gitignored scratch in tmp/ from the 015 decisions, review, parity and worker-landing work.
# Dry run by default. --execute / -e acts on worktrees and branches; section 3 also needs --delete-scratch.
# Merged-ness, tracked-ness and existence are asked at run time, never taken from this header.
set -euo pipefail

execute=0; scratch=0
for a in "$@"; do
  case "$a" in
    --execute|-e) execute=1 ;;
    --delete-scratch) scratch=1 ;;
    *) printf 'unknown flag: %s\n' "$a"; exit 2 ;;
  esac
done
repo="$(git rev-parse --show-toplevel)"
cd "$repo"
failed=0

if [ "$(git branch --show-current)" != "dev" ]; then
  printf 'refuse: check out dev first (on %s).\n' "$(git branch --show-current)"; exit 1
fi

branches_file="$(mktemp)"; trap 'rm -f "$branches_file"' EXIT

# remove_worktrees PATTERN...: worktrees whose path (relative) matches a PATTERN and whose branch tip dev holds.
remove_worktrees() {
  local listing wt br rel p hit
  listing="$(git worktree list --porcelain)"
  while IFS=$'\t' read -r wt br; do
    rel="${wt#"$repo"/}"; hit=0
    for p in "$@"; do case "$rel" in $p) hit=1 ;; esac; done
    [ $hit -eq 1 ] || continue
    if [ -z "$br" ] || ! git merge-base --is-ancestor "refs/heads/$br" refs/heads/dev 2>/dev/null; then
      printf 'keep:  %s (%s not in dev)\n' "$wt" "${br:-detached}"; continue
    fi
    printf '%s\n' "$br" >> "$branches_file"
    if [ $execute -eq 1 ]; then
      if git worktree remove "$wt"; then printf 'removed: %s\n' "$wt"; else printf '  refused: %s (git said why above). Left alone.\n' "$wt"; failed=1; fi
    else
      printf 'would: git worktree remove %s\n' "$wt"
    fi
  done < <(printf '%s\n' "$listing" | awk '/^worktree /{w=substr($0,10)} /^branch /{print w "\t" substr($0,19)} /^detached/{print w "\t"}')
  if [ $execute -eq 1 ]; then git worktree prune; else printf 'would: git worktree prune\n'; fi
}
# delete_branches: the branches listed in $branches_file; git branch -d refuses one dev does not hold.
delete_branches() {
  local b
  while read -r b; do
    [ -n "$b" ] || continue
    if ! git rev-parse --verify --quiet "refs/heads/$b" >/dev/null; then printf 'skip:  branch %s no longer exists\n' "$b"
    elif ! git merge-base --is-ancestor "refs/heads/$b" refs/heads/dev; then printf 'keep:  branch %s (not in dev)\n' "$b"
    elif [ $execute -eq 1 ]; then git branch -d "$b" || { printf '  refused: %s. Left alone.\n' "$b"; failed=1; }
    else printf 'would: git branch -d %s\n' "$b"; fi
  done < "$branches_file"
  : > "$branches_file"
}

# 1. Parity worker worktrees.
printf '== 1. p015 worker worktrees and branches\n'
remove_worktrees '.worktrees/worker-p015-*'
delete_branches

# 2. Branches without a worktree.
printf '== 2. branches\n'
printf '%s\n' docs/015-decisions fix/scaffold-checkout-v5 chore/ci-shell-checkout-dotnet docs/013-t19-done \
  fix/review-2026-11-02-7 test/015-parity-baselines chore/worker-p015-briefs-s chore/worker-p015-guard \
  chore/worker-p015-quality-s chore/cleanup-017-spec-015-prep > "$branches_file"
delete_branches

# 3. Scratch under tmp/ (gitignored; refused if git tracks it).
printf '== 3. tmp/ scratch\n'
for rel in tmp/015-parity-criteria.md tmp/015-parity-common.txt tmp/015-decisions.md tmp/s8 tmp/review-5c92-diff.txt \
           tmp/sa.txt tmp/worker-leftovers tmp/parity tmp/parity-test; do
  if [ ! -e "$rel" ]; then printf 'skip:  %s already gone\n' "$rel"; continue; fi
  if [ -n "$(git ls-files -- "$rel" | head -1)" ]; then
    printf 'refuse: %s is tracked by git. Propose it as a commit instead. Left alone.\n' "$rel"; failed=1; continue
  fi
  if [ $execute -eq 1 ] && [ $scratch -eq 1 ]; then rm -rf -- "$rel"; printf 'removed: %s\n' "$rel"
  else printf 'would: rm -rf %s (needs --execute --delete-scratch)\n' "$rel"; fi
done

printf '\nremaining worktrees:\n'
git worktree list
if [ $execute -eq 1 ] && [ $failed -eq 1 ]; then exit 1; fi
exit 0
