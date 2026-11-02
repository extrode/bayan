#!/usr/bin/env bash
# merged-worktree-backlog on a fixture repo: a merged worktree goes with its branch, an unmerged, a recent, a dirty one
# stay; a merged one holding ignored scratch waits for --delete-worktree-scratch while build output alone does not; a
# detached worktree in dev goes; the dry run changes nothing.
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
for b in merged scratch build unmerged dirty; do g branch "$b"; done
git -c user.email=t@t -c user.name=t branch recent
g switch -q merged && g commit -q --allow-empty -m m && g switch -q dev && g merge -q --no-ff merged -m mm
g switch -q unmerged && g commit -q --allow-empty -m u && g switch -q dev
for b in merged scratch build unmerged dirty recent; do g worktree add -q ".worktrees/$b" "$b"; done
g worktree add -q --detach .worktrees/detached dev
mkdir -p .worktrees/scratch/tmp .worktrees/build/bin && echo x > .worktrees/scratch/tmp/run.log && echo x > .worktrees/build/bin/a.dll
echo x > .worktrees/dirty/new.txt
fail() { echo "FAIL: $1"; echo "$out"; cd "$root"; exit 1; }

out="$(bash "$script")"
for w in merged build detached; do echo "$out" | grep -q "^would: git worktree remove .worktrees/$w$" || fail "$w not listed"; done
echo "$out" | grep -q "^would: git branch -d merged$" || fail "merged branch not listed"
echo "$out" | grep -q "^keep:  .worktrees/unmerged (unmerged not in dev)$" || fail "unmerged not kept"
echo "$out" | grep -q "^keep:  .worktrees/recent (recent moved in the last 24h)$" || fail "recent not kept"
echo "$out" | grep -q "^keep:  .worktrees/dirty (modified or untracked files)$" || fail "dirty not kept"
echo "$out" | grep -q "^hold:  .worktrees/scratch (ignored files beyond build output: tmp/ ;" || fail "scratch not held"
[ "$(git worktree list | wc -l | tr -d ' ')" = 8 ] || fail "dry run changed something"

out="$(bash "$script" -e)"
for w in merged build detached; do [ ! -d ".worktrees/$w" ] || fail "$w not removed"; done
for w in scratch unmerged dirty recent; do [ -d ".worktrees/$w" ] || fail "$w removed"; done
! git rev-parse -q --verify merged >/dev/null && git rev-parse -q --verify unmerged >/dev/null || fail "branch handling wrong"

out="$(bash "$script" -e --delete-worktree-scratch)"
[ ! -d .worktrees/scratch ] && ! git rev-parse -q --verify scratch >/dev/null || fail "scratch not removed with its flag"
for w in unmerged dirty recent; do [ -d ".worktrees/$w" ] || fail "$w removed on the second run"; done
out="$(bash "$script" -e)" || fail "a run with nothing left to do failed"
cd "$root"; echo ok
