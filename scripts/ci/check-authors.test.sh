#!/usr/bin/env bash
# Tests check-authors.sh on throwaway repos under tmp/. Prints PASS or the failures.
set -u
HERE=$(cd "$(dirname "$0")" && pwd -P)
ROOT=$(cd "$HERE/../.." && pwd -P)
FX=$ROOT/tmp/check-authors-test
export GIT_CEILING_DIRECTORIES=$ROOT/tmp
fails=0
fail() { echo "FAIL: $*"; fails=$((fails + 1)); }
person() { GIT_AUTHOR_NAME=Person GIT_AUTHOR_EMAIL=p@example.org GIT_COMMITTER_NAME=Person GIT_COMMITTER_EMAIL=p@example.org "$@"; }
fresh() { rm -rf "$FX"; mkdir -p "$FX"; git -C "$FX" init -q -b fixture; git -C "$FX" config core.hooksPath /dev/null; person git -C "$FX" commit -q --allow-empty -m "feat: first"; }
check() { OUT=$(cd "$FX" && bash "$HERE/check-authors.sh" "$@" 2>&1); RC=$?; }

fresh; check
[ "$RC" -eq 0 ] || fail "clean: exit $RC: $OUT"
case "$OUT" in *"clean (1 commits)"*) ;; *) fail "clean: $OUT" ;; esac

fresh; GIT_AUTHOR_NAME=Claude GIT_AUTHOR_EMAIL=noreply@anthropic.com GIT_COMMITTER_NAME=Person GIT_COMMITTER_EMAIL=p@example.org git -C "$FX" commit -q --allow-empty -m "fix: a"; check
[ "$RC" -eq 1 ] || fail "author: exit $RC: $OUT"
case "$OUT" in *"author Claude <noreply@anthropic.com>"*"1 offending"*) ;; *) fail "author: $OUT" ;; esac

fresh; GIT_AUTHOR_NAME=Person GIT_AUTHOR_EMAIL=p@example.org GIT_COMMITTER_NAME=Claude GIT_COMMITTER_EMAIL=noreply@anthropic.com git -C "$FX" commit -q --allow-empty -m "fix: b"; check
[ "$RC" -eq 1 ] || fail "committer: exit $RC: $OUT"

fresh; person git -C "$FX" commit -q --allow-empty -m "fix: c" -m "Co-Authored-By: Claude Opus <noreply@anthropic.com>"; check
[ "$RC" -eq 1 ] || fail "trailer: exit $RC: $OUT"
case "$OUT" in *"message: AI attribution line"*) ;; *) fail "trailer: $OUT" ;; esac

fresh; person git -C "$FX" commit -q --allow-empty -m "docs: mention the Claude Code docs"; check
[ "$RC" -eq 0 ] || fail "plain mention in a message refused: $OUT"

fresh; check no-such-rev
[ "$RC" -eq 2 ] || fail "bad rev: exit $RC: $OUT"

rm -rf "$FX"
[ "$fails" -eq 0 ] && echo PASS || { echo "$fails failure(s)"; exit 1; }
