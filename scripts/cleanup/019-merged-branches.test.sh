#!/usr/bin/env bash
# 019 on a fixture repo with a bare origin: merged branches go remote-first then local, leaving dev and main; an unmerged,
# a recent and a worktree branch stay; -e alone touches no remote; a local branch ahead of its remote copy needs the
# remote gone first.
set -euo pipefail
root="$(cd "$(dirname "$0")/../.." && pwd)"
script="$root/scripts/cleanup/019-merged-branches.sh"
r="$root/tmp/cleanup-019-test"
rm -rf "$r" && mkdir -p "$r" && cd "$r"
export GIT_ALLOW_PROTECTED=1  # fixture repo: its dev branch is the subject under test
old="2020-01-01T00:00:00"
g() { GIT_COMMITTER_DATE="$old" GIT_AUTHOR_DATE="$old" git -c user.email=t@t -c user.name=t "$@"; }
gnow() { git -c user.email=t@t -c user.name=t "$@"; }
git init -q --bare origin.git
git init -q -b dev w && cd w && git config core.hooksPath /dev/null
g commit -q --allow-empty -m base && g branch main
for b in merged ahead unmerged wt tagged; do g branch "$b"; done
g tag tagged
g switch -q ahead && g commit -q --allow-empty -m a1 && g switch -q dev
g switch -q unmerged && g commit -q --allow-empty -m u && g switch -q dev
git remote add origin ../origin.git
git push -q origin dev main merged ahead unmerged 2>/dev/null
git branch -q -u origin/ahead ahead
g switch -q ahead && g commit -q --allow-empty -m a2 && g switch -q dev && g merge -q --no-ff ahead -m ma
git push -q origin dev 2>/dev/null
gnow branch recent
git push -q origin recent wt 2>/dev/null
g worktree add -q ../wt wt
fail() { echo "FAIL: $1"; echo "$out"; cd "$root"; exit 1; }
remote_has() { git ls-remote --heads origin "$1" | grep -q .; }

out="$(bash "$script")"
echo "$out" | grep -q "^would: git push origin --delete merged " || fail "remote merged not listed"
echo "$out" | grep -q "^keep:  origin/unmerged (not in dev)$" || fail "remote unmerged not kept"
echo "$out" | grep -q "^would: git branch -d ahead$" || fail "local ahead not listed"
echo "$out" | grep -q "^keep:  recent (moved in the last 1h)$" || fail "recent not kept"
echo "$out" | grep -q "^keep:  wt (checked out in a worktree)$" || fail "worktree branch not kept"
echo "$out" | grep -q "^keep:  origin/wt (its local branch is checked out" || fail "remote copy of a worktree branch not kept"
echo "$out" | grep -q "^keep:  origin/recent (its local branch is checked out or moved" || fail "remote copy of a recent branch not kept"
echo "$out" | grep -q "^would: git branch -d tagged$" || fail "a branch sharing a tag's name not listed"
remote_has merged && git rev-parse -q --verify merged >/dev/null || fail "dry run deleted something"

out="$(bash "$script" -e)" && fail "-e alone deleted a branch ahead of its remote copy"
remote_has merged && remote_has ahead || fail "-e alone touched origin"
! git rev-parse -q --verify merged >/dev/null || fail "local merged not deleted"
git rev-parse -q --verify ahead >/dev/null || fail "ahead deleted while its remote copy was behind"

out="$(bash "$script" -e --delete-remote-branches)" || fail "remote-then-local run failed"
! remote_has merged && ! remote_has ahead || fail "merged remote branches left"
! git rev-parse -q --verify ahead >/dev/null || fail "ahead not deleted after its remote copy went"
remote_has unmerged && remote_has dev && remote_has main && remote_has wt && remote_has recent || fail "unmerged, dev, main, wt or recent removed from origin"
for b in unmerged recent wt dev main; do git rev-parse -q --verify "$b" >/dev/null || fail "$b deleted locally"; done

g commit -q --allow-empty -m local-only
out="$(bash "$script")" && fail "ran with dev ahead of origin/dev"
echo "$out" | grep -q "^refuse: dev and origin/dev differ" || fail "dev drift not refused"
cd "$root"; echo ok
