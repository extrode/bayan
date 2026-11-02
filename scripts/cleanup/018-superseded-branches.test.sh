#!/usr/bin/env bash
# 018 on a fixture repo: the dry run changes nothing; --execute alone leaves the leftovers; with
# --discard-worktree-changes the six untracked files go and the T28 un-skips are reverted, and the worktrees then go with
# their branches; a t28survey holding any other change is refused.
set -euo pipefail
root="$(cd "$(dirname "$0")/../.." && pwd)"
r="$root/tmp/cleanup-018-test"
rm -rf "$r" && mkdir -p "$r" && cd "$r"
export GIT_ALLOW_PROTECTED=1  # fixture repo: its dev branch is the subject under test
old="2020-01-01T00:00:00"
g() { GIT_COMMITTER_DATE="$old" GIT_AUTHOR_DATE="$old" git -c user.email=t@t -c user.name=t "$@"; }
git init -q -b dev && git config core.hooksPath /dev/null
mkdir -p scripts/cleanup tests/Bayan.Tests
cp "$root/scripts/cleanup/018-superseded-branches.sh" "$root/scripts/cleanup/merged-worktree-backlog.sh" scripts/cleanup/
printf '    [Fact(Skip = "T28 enables it")]\n    public void A() {}\n' > tests/Bayan.Tests/SelfhostSemanticsTests.cs
printf 'tmp/\n.worktrees/\n' > .gitignore && git add . && g commit -q -m base
gnow() { git -c user.email=t@t -c user.name=t "$@"; }
ago3h="$(( $(date +%s) - 10800 )) +0000"
g3h() { GIT_COMMITTER_DATE="$ago3h" GIT_AUTHOR_DATE="$ago3h" git -c user.email=t@t -c user.name=t "$@"; }
# Moved 3h ago: the default 24h window would keep it, 018's 1h window does not. One cut just now stays.
g3h branch feat/010-self-hosted-semantics && g3h worktree add -q .worktrees/feat-010-self-hosted-semantics feat/010-self-hosted-semantics
gnow branch fresh && gnow worktree add -q .worktrees/fresh fresh
g worktree add -q --detach tmp/wt/t28survey dev
for f in a.txt b.txt C.txt copy.bin empty.bin input.bin; do echo x > ".worktrees/feat-010-self-hosted-semantics/$f"; done
s=tmp/wt/t28survey/tests/Bayan.Tests/SelfhostSemanticsTests.cs
printf '    [Fact]\n    public void A() {}\n' > "$s"
fail() { echo "FAIL: $1"; echo "$out"; cd "$root"; exit 1; }

out="$(bash scripts/cleanup/018-superseded-branches.sh)"
echo "$out" | grep -q "^would: rm .worktrees/feat-010-self-hosted-semantics/input.bin (needs --execute --discard-worktree-changes)$" || fail "untracked file not listed"
echo "$out" | grep -q "^would: git -C tmp/wt/t28survey checkout -- tests/Bayan.Tests" || fail "un-skip revert not listed"
[ -f .worktrees/feat-010-self-hosted-semantics/a.txt ] && grep -q '\[Fact\]' "$s" || fail "dry run changed something"

out="$(bash scripts/cleanup/018-superseded-branches.sh -e)"
[ -f .worktrees/feat-010-self-hosted-semantics/a.txt ] && [ -d tmp/wt/t28survey ] || fail "-e alone discarded changes"

echo '    // more' >> "$s"
out="$(bash scripts/cleanup/018-superseded-branches.sh -e --discard-worktree-changes)" && fail "a foreign change was not a failure"
echo "$out" | grep -q "^refuse: tmp/wt/t28survey holds changes other than the T28 un-skips" || fail "foreign change not refused"
[ -d tmp/wt/t28survey ] || fail "t28survey removed with a foreign change"
[ ! -d .worktrees/feat-010-self-hosted-semantics ] && ! git rev-parse -q --verify feat/010-self-hosted-semantics >/dev/null \
  || fail "feat-010 worktree or branch left"

printf '    [Fact]\n    public void A() {}\n' > "$s"
out="$(bash scripts/cleanup/018-superseded-branches.sh -e --discard-worktree-changes)" || fail "clean run failed"
[ ! -d tmp/wt/t28survey ] || fail "t28survey not removed"
out="$(bash scripts/cleanup/018-superseded-branches.sh -e --discard-worktree-changes)" || fail "a run with nothing left to do failed"
# A listed name that git tracks and that was changed is not scratch.
g switch -q -c side && echo x > a.txt && git add a.txt && g commit -q -m tracked && g switch -q dev && g merge -q --no-ff side -m ms
g worktree add -q .worktrees/feat-010-self-hosted-semantics side && echo changed > .worktrees/feat-010-self-hosted-semantics/a.txt
out="$(bash scripts/cleanup/018-superseded-branches.sh -e --discard-worktree-changes)" && fail "a tracked change was not a failure"
echo "$out" | grep -q "^refuse: .worktrees/feat-010-self-hosted-semantics/a.txt is not untracked" || fail "tracked change not refused"
grep -q changed .worktrees/feat-010-self-hosted-semantics/a.txt || fail "a tracked change was discarded"
[ -d .worktrees/fresh ] && git rev-parse -q --verify fresh >/dev/null || fail "a worktree cut in the last hour was removed"
# On another branch, or bigger than the run cases write: not the leftover.
echo x > .worktrees/feat-010-self-hosted-semantics/b.txt
out="$(bash scripts/cleanup/018-superseded-branches.sh -e --discard-worktree-changes)" && fail "a file on another branch was not a failure"
echo "$out" | grep -q "^refuse: .worktrees/feat-010-self-hosted-semantics/b.txt is not the run-case leftover (branch side" || fail "other branch not refused"
[ -f .worktrees/feat-010-self-hosted-semantics/b.txt ] || fail "a file on another branch was removed"
git worktree remove --force .worktrees/feat-010-self-hosted-semantics
g branch feat/010-self-hosted-semantics && g worktree add -q .worktrees/feat-010-self-hosted-semantics feat/010-self-hosted-semantics
echo 'more than six bytes' > .worktrees/feat-010-self-hosted-semantics/C.txt
out="$(bash scripts/cleanup/018-superseded-branches.sh -e --discard-worktree-changes)" && fail "a large file was not a failure"
echo "$out" | grep -q "^refuse: .worktrees/feat-010-self-hosted-semantics/C.txt is not the run-case leftover" || fail "large file not refused"
[ -f .worktrees/feat-010-self-hosted-semantics/C.txt ] || fail "a large file was removed"
cd "$root"; echo ok
