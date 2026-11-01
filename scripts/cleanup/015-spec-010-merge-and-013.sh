#!/usr/bin/env bash
# Cleanup after spec 010 merged into dev (52e2ae0, 2026-11-01) and the 013 tasks merged so far: worktrees under
# .worktrees/ and tmp/wt/ whose branch tip is already in dev, then those branches, and (second flag) the run-case
# files a full `dotnet test` left in the 010 base checkout root.
# Dry run by default. --execute / -e acts on worktrees and branches; --delete-scratch also removes the stray files.
# Merged-ness is asked of git per worktree (tip is an ancestor of dev), never taken from this header.
# Not handled: feat/013-test-tooling and any worktree whose branch dev does not contain (013 work in flight).
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

# Gate: run from dev, so no branch below is the checked-out one.
if [ "$(git branch --show-current)" != "dev" ]; then
  printf 'refuse: check out dev first (on %s).\n' "$(git branch --show-current)"; exit 1
fi

# 1. Stray run-case files in the 010 base checkout (needs --delete-scratch; refused if git tracks them there).
base=.worktrees/feat-010-self-hosted-semantics
for f in C.txt a.txt b.txt copy.bin empty.bin input.bin; do
  rel="$base/$f"
  if [ ! -e "$rel" ]; then printf 'skip:  %s already gone\n' "$rel"; continue; fi
  if git -C "$base" ls-files --error-unmatch -- "$f" >/dev/null 2>&1; then
    printf 'refuse: %s is tracked by git. Propose it as a commit instead. Left alone.\n' "$rel"; failed=1; continue
  fi
  if [ $execute -eq 1 ] && [ $scratch -eq 1 ]; then rm -f -- "$rel"; printf 'removed: %s\n' "$rel"
  else printf 'would: rm %s (needs --execute --delete-scratch)\n' "$rel"; fi
done

# 2. Worktrees whose branch has commits of its own, all of them in dev. A branch whose tip is dev's tip has none yet
#    (a writer just cut from dev), so it is kept. git worktree remove refuses a dirty or locked tree.
devtip="$(git rev-parse refs/heads/dev)"
listing="$(git worktree list --porcelain)"
branches=""
while IFS=$'\t' read -r wt br; do
  rel="${wt#"$repo"/}"
  case "$rel" in .worktrees/*|tmp/wt/*) ;; *) continue ;; esac
  [ "$br" = "feat/013-test-tooling" ] && { printf 'keep:  %s (013 in flight)\n' "$rel"; continue; }
  if [ -z "$br" ] || ! git merge-base --is-ancestor "refs/heads/$br" refs/heads/dev 2>/dev/null; then
    printf 'keep:  %s (%s not in dev)\n' "$rel" "${br:-detached}"; continue
  fi
  if [ "$(git rev-parse "refs/heads/$br")" = "$devtip" ]; then
    printf 'keep:  %s (%s is at dev, no commits of its own)\n' "$rel" "$br"; continue
  fi
  branches="$branches $br"
  if [ $execute -eq 1 ]; then
    if git worktree remove "$rel"; then printf 'removed: %s\n' "$rel"; else printf '  refused: %s (git said why above). Left alone.\n' "$rel"; failed=1; fi
  else
    printf 'would: git worktree remove %s\n' "$rel"
  fi
done < <(printf '%s\n' "$listing" | awk '/^worktree /{w=substr($0,10)} /^branch /{print w "\t" substr($0,19)} /^detached/{print w "\t"}')
if [ $execute -eq 1 ]; then git worktree prune; else printf 'would: git worktree prune\n'; fi

# 3. Their branches (git branch -d refuses anything unmerged or still checked out).
for b in $branches; do
  if ! git rev-parse --verify --quiet "refs/heads/$b" >/dev/null; then
    printf 'skip:  branch %s no longer exists\n' "$b"
  elif [ $execute -eq 1 ]; then
    if git branch -d "$b"; then :; else printf '  refused: %s. Left alone.\n' "$b"; failed=1; fi
  else
    printf 'would: git branch -d %s\n' "$b"
  fi
done

printf '\nremaining worktrees:\n'
git worktree list
if [ $execute -eq 1 ] && [ $failed -eq 1 ]; then exit 1; fi
