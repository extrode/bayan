#!/usr/bin/env bash
# Dry run of 016 on a fixture repo with stub ssh/docker and HOME pointing at a fixture: a merged worker worktree (path with
# a space-free key) is listed, an unmerged one is kept, a worktree outside the patterns is ignored, scratch and the image
# need their own flags, an unreachable host is skipped, and nothing changes.
set -euo pipefail
root="$(cd "$(dirname "$0")/../.." && pwd)"
r="$root/tmp/cleanup-016-test"
rm -rf "$r" && mkdir -p "$r/bin" "$r/home" "$r/repo" && cd "$r/repo"
export GIT_ALLOW_PROTECTED=1  # fixture repo: its dev branch is the subject under test
git init -q -b dev && git config user.email t@t && git config user.name t
git commit -q --allow-empty -m base
git branch chore/worker-w-013-t1 && git switch -q chore/worker-w-013-t1 && git commit -q --allow-empty -m m && git switch -q dev
git merge -q --no-ff chore/worker-w-013-t1 -m mm
git branch chore/worker-w-013-t27 && git switch -q chore/worker-w-013-t27 && git commit -q --allow-empty -m o && git switch -q dev
git branch other
mkdir -p .worktrees tmp/review-013
git worktree add -q .worktrees/worker-w-013-t1 chore/worker-w-013-t1
git worktree add -q .worktrees/worker-w-013-t27 chore/worker-w-013-t27
git worktree add -q .worktrees/other other
printf '#!/bin/sh\nexit 255\n' > "$r/bin/ssh"
printf '#!/bin/sh\n[ "$1 $2" = "image inspect" ] && exit 0\necho "docker $*" >> "%s/docker.log"\n' "$r" > "$r/bin/docker"
chmod +x "$r/bin/ssh" "$r/bin/docker"
out="$(PATH="$r/bin:$PATH" HOME="$r/home" bash "$root/scripts/cleanup/016-spec-013-and-011-draft.sh")"
left="$(git worktree list | wc -l | tr -d ' ')"; scratch_left=$([ -d tmp/review-013 ] && echo 1 || echo 0)
dockered=$([ -f "$r/docker.log" ] && echo 1 || echo 0)
cd "$root"; rm -rf "$r"
fail() { echo "FAIL: $1"; echo "$out"; exit 1; }
echo "$out" | grep -q "^would: git -C .* worktree remove .*/.worktrees/worker-w-013-t1$" || fail "merged worker worktree not listed"
echo "$out" | grep -q "^would: git -C .* branch -d chore/worker-w-013-t1$" || fail "merged worker branch not listed"
echo "$out" | grep -q "^keep:  .*/.worktrees/worker-w-013-t27 (chore/worker-w-013-t27 not in dev)$" || fail "unmerged worker worktree not kept"
echo "$out" | sed '/^remaining worktrees:/,$d' | grep -q "worktrees/other" && fail "a worktree outside the patterns was considered"
echo "$out" | grep -q "^would: rm -rf tmp/review-013 (needs --execute --delete-scratch)$" || fail "scratch not listed behind its flag"
echo "$out" | grep -q "^would: docker image rm bayan-t19 (needs --execute --remove-docker-image)$" || fail "image not listed behind its flag"
echo "$out" | grep -q "^skip:  wp1 unreachable$" && echo "$out" | grep -q "^skip:  wlt1 unreachable$" || fail "unreachable hosts not skipped"
echo "$out" | grep -q "^skip:  .*/home/.claude is not a git repo$" || fail "missing ~/.claude repo not skipped"
[ "$left" = 4 ] && [ "$scratch_left" = 1 ] && [ "$dockered" = 0 ] || fail "dry run changed something (worktrees $left, scratch $scratch_left, docker $dockered)"
echo PASS
