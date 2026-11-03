#!/usr/bin/env bash
# merged-worktree-backlog on a fixture repo: a merged worktree goes with its branch, an unmerged, a recent, a dirty one
# stay; a merged one holding ignored scratch waits for --delete-worktree-scratch while build output alone does not; a
# detached worktree in dev goes, one with an unreadable HEAD stays; a path with spaces goes; the dry run changes nothing.
set -euo pipefail
root="$(cd "$(dirname "$0")/../.." && pwd)"
script="$root/scripts/cleanup/merged-worktree-backlog.sh"
r="$root/tmp/cleanup-mwb-test"
rm -rf "$r" && mkdir -p "$r" && cd "$r"
export GIT_ALLOW_PROTECTED=1  # fixture repo: its dev branch is the subject under test
old="2020-01-01T00:00:00"
g() { GIT_COMMITTER_DATE="$old" GIT_AUTHOR_DATE="$old" git -c user.email=t@t -c user.name=t "$@"; }
git init -q -b dev && git config core.hooksPath /dev/null
printf 'tmp/\nbin/\n' > .gitignore && git add .gitignore && g commit -q -m base
gnow() { git -c user.email=t@t -c user.name=t "$@"; }
for b in merged scratch build unmerged dirty locked readded spaced; do g branch "$b"; done
gnow branch recent
g switch -q merged && g commit -q --allow-empty -m m && g switch -q dev && g merge -q --no-ff merged -m mm
g switch -q unmerged && g commit -q --allow-empty -m u && g switch -q dev
for b in merged scratch build unmerged dirty recent; do g worktree add -q ".worktrees/$b" "$b"; done
g worktree add -q --detach .worktrees/detached dev
g worktree add -q .worktrees/locked locked && git worktree lock .worktrees/locked
gnow worktree add -q .worktrees/readded readded
g branch broken && g worktree add -q .worktrees/broken broken && echo "gitdir: $r/nowhere" > .worktrees/broken/.git
g worktree add -q ".worktrees/with space" spaced
g worktree add -q --detach .worktrees/nohead dev && echo garbage > .git/worktrees/nohead/HEAD
mkdir -p .worktrees/scratch/tmp .worktrees/build/bin && echo x > .worktrees/scratch/tmp/run.log && echo x > .worktrees/build/bin/a.dll
echo x > .worktrees/dirty/new.txt
fail() { echo "FAIL: $1"; echo "$out"; cd "$root"; exit 1; }

out="$(bash "$script")"
for w in merged build detached; do echo "$out" | grep -q "^would: git worktree remove .worktrees/$w$" || fail "$w not listed"; done
echo "$out" | grep -q "^would: git branch -d merged$" || fail "merged branch not listed"
echo "$out" | grep -q "^would: git worktree remove .worktrees/with space$" || fail "a path with spaces not listed"
echo "$out" | grep -q "^keep:  .worktrees/nohead (HEAD unreadable)$" || fail "a detached worktree with an unreadable HEAD not kept"
echo "$out" | grep -q "^keep:  .worktrees/unmerged (unmerged not in dev)$" || fail "unmerged not kept"
echo "$out" | grep -q "^keep:  .worktrees/recent (recent moved in the last 24h)$" || fail "recent not kept"
echo "$out" | grep -q "^keep:  .worktrees/dirty (modified or untracked files)$" || fail "dirty not kept"
echo "$out" | grep -q "^hold:  .worktrees/scratch (ignored files beyond build output: tmp/ ;" || fail "scratch not held"
echo "$out" | grep -q "^keep:  .worktrees/locked (locked)$" || fail "locked not kept"
echo "$out" | grep -q "^keep:  .worktrees/readded (readded moved in the last 24h)$" || fail "a fresh worktree of an old branch not kept"
echo "$out" | grep -q "^keep:  .worktrees/broken (when it last moved is unreadable)$" || fail "a worktree with unreadable times not kept"
[ "$(git worktree list | wc -l | tr -d ' ')" = 13 ] || fail "dry run changed something"

out="$(bash "$script" -e)"
for w in merged build detached "with space"; do [ ! -d ".worktrees/$w" ] || fail "$w not removed"; done
! git rev-parse -q --verify spaced >/dev/null || fail "the branch of the path with spaces not deleted"
for w in scratch unmerged dirty recent; do [ -d ".worktrees/$w" ] || fail "$w removed"; done
! git rev-parse -q --verify merged >/dev/null && git rev-parse -q --verify unmerged >/dev/null || fail "branch handling wrong"

out="$(bash "$script" -e --delete-worktree-scratch)"
[ ! -d .worktrees/scratch ] && ! git rev-parse -q --verify scratch >/dev/null || fail "scratch not removed with its flag"
for w in unmerged dirty recent; do [ -d ".worktrees/$w" ] || fail "$w removed on the second run"; done
for w in locked readded broken nohead; do [ -d ".worktrees/$w" ] || fail "$w removed"; done
out="$(bash "$script" -e)" || fail "a run with nothing left to do failed (a locked worktree is not a failure)"
# Run from a linked worktree holding dev (the main one on another branch): neither it nor dev is a target.
g switch -q -c side && g worktree add -q .worktrees/devwt dev
out="$(cd .worktrees/devwt && bash "$script" -e)" || fail "run from a linked dev worktree failed"
[ -d .worktrees/devwt ] && git rev-parse -q --verify dev >/dev/null || fail "the worktree it ran from, or dev, was removed"
if echo "$out" | grep -q "devwt"; then fail "the worktree it ran from was considered"; fi
cd "$root"; echo ok
