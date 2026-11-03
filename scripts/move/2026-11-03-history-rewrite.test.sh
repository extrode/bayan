#!/usr/bin/env bash
# Tests 2026-11-03-history-rewrite.sh on throwaway repos under tmp/: the real run end to end, the printed undo,
# and a refusal after the rewrite began (exit 3 with the undo). Prints PASS or the failures.
set -u
HERE=$(cd "$(dirname "$0")" && pwd -P)
ROOT=$(cd "$HERE/../.." && pwd -P)
FX=$ROOT/tmp/history-rewrite-test
export GIT_CEILING_DIRECTORIES=$ROOT/tmp GIT_ALLOW_PROTECTED=1
fails=0
fail() { echo "FAIL: $*"; fails=$((fails + 1)); }
me() { GIT_AUTHOR_NAME=User GIT_AUTHOR_EMAIL=syed@beparey.com GIT_COMMITTER_NAME=User GIT_COMMITTER_EMAIL=syed@beparey.com "$@"; }
bot() { GIT_AUTHOR_NAME=Claude GIT_AUTHOR_EMAIL=noreply@anthropic.com GIT_COMMITTER_NAME=Claude GIT_COMMITTER_EMAIL=noreply@anthropic.com "$@"; }

# fixture: user commit with a phrase, a Claude commit, a doc citing the first commit's SHA
fresh() {
  rm -rf "$FX"; mkdir -p "$FX/repo/scripts/move" "$FX/repo/scripts/ci" "$FX/hooks"
  R=$FX/repo
  git -C "$R" init -q -b dev; git -C "$R" config core.hooksPath "$FX/hooks"
  git -C "$R" remote add origin git@example.invalid:fixture/repo.git
  cp "$HERE/2026-11-03-history-rewrite.sh" "$R/scripts/move/"; cp "$ROOT/scripts/ci/check-authors.sh" "$R/scripts/ci/"
  printf 'tmp/\n' > "$R/.gitignore"; printf 'an oldword here\n' > "$R/a.txt"
  git -C "$R" add -A; me git -C "$R" commit -q -m "feat: first"
  OLD1=$(git -C "$R" rev-parse --short=12 HEAD)
  printf 'b\n' > "$R/b.txt"; git -C "$R" add -A; bot git -C "$R" commit -q -m "fix: by the bot"
  mkdir -p "$R/docs"; printf 'See %s.\n' "$OLD1" > "$R/docs/x.md"; git -C "$R" add -A; me git -C "$R" commit -q -m "docs: cite"
  OLDDEV=$(git -C "$R" rev-parse dev)
  printf 'oldword==>newword\n' > "$FX/repl.txt"
}
run() { OUT=$(cd "$R" && bash scripts/move/2026-11-03-history-rewrite.sh --replacements "$FX/repl.txt" --backup "$FX/backup.git" "$@" 2>&1); RC=$?; }

# A. real run, then its printed undo
fresh; run --execute --rewrite-history
[ "$RC" -eq 0 ] || fail "real run: exit $RC: $OUT"
(cd "$R" && bash scripts/ci/check-authors.sh dev >/dev/null 2>&1) || fail "real run: Claude identity left on dev"
git -C "$R" log --all -p | grep -q oldword && fail "real run: phrase left in the history"
grep -q "$OLD1" "$R/docs/x.md" && fail "real run: docs/x.md still cites the old SHA"
[ "$(git -C "$R" remote get-url origin)" = git@example.invalid:fixture/repo.git ] || fail "real run: origin not restored"
case "$OUT" in *"undo (before pushing): "*--prune*) ;; *) fail "real run: undo without --prune: $OUT" ;; esac
printf '%s\n' "$OUT" | grep 'git push' | grep -qv '^  GIT_ALLOW_PROTECTED=1 git push --force' && fail "real run: a printed push line lacks GIT_ALLOW_PROTECTED=1: $OUT"
printf '%s\n' "$OUT" | grep -q '^  GIT_ALLOW_PROTECTED=1 git push --force' || fail "real run: no push line printed: $OUT"
UNDO=$(printf '%s\n' "$OUT" | sed -n 's/^undo (before pushing): //p')
(cd "$R" && eval "$UNDO") >/dev/null 2>&1 || fail "undo: failed"
[ "$(git -C "$R" rev-parse dev)" = "$OLDDEV" ] || fail "undo: dev not restored"
git -C "$R" rev-parse -q --verify refs/heads/chore/history-sha-refs >/dev/null && fail "undo: chore/history-sha-refs left behind"

# B. the SHA-reference commit is refused after the rewrite: exit 3, origin back, the undo restores dev
fresh
printf '#!/bin/sh\ngrep -q "^docs: point SHA" "$1" && exit 1\nexit 0\n' > "$FX/hooks/commit-msg"; chmod +x "$FX/hooks/commit-msg"
run --execute --rewrite-history
[ "$RC" -eq 3 ] || fail "refused commit: exit $RC: $OUT"
case "$OUT" in *"ok: history rewritten"*) fail "refused commit: reported success" ;; esac
case "$OUT" in *"fail: commit of the SHA references"*"Undo: git checkout -f dev"*) ;; *) fail "refused commit: $OUT" ;; esac
git -C "$R" remote get-url origin >/dev/null 2>&1 || fail "refused commit: origin missing"
UNDO=$(printf '%s\n' "$OUT" | sed -n '/^the repository is changed/{n;s/^  //p;}')
(cd "$R" && git checkout -q -f dev && eval "$UNDO") >/dev/null 2>&1 || fail "refused commit: undo failed"
[ "$(git -C "$R" rev-parse dev)" = "$OLDDEV" ] || fail "refused commit: undo left dev rewritten"

# C. a regex: line is refused before anything runs
fresh; printf 'regex:old.*==>new\n' >> "$FX/repl.txt"; run
[ "$RC" -eq 1 ] || fail "regex line: exit $RC"
case "$OUT" in *"regex:/glob: line"*) ;; *) fail "regex line: $OUT" ;; esac

# D. filter-repo skips a blob with a NUL byte; the leftover scan still finds the phrase in it
fresh; printf 'x\000oldword\n' > "$R/bin.dat"; git -C "$R" add -A; me git -C "$R" commit -q -m "feat: binary"; run
[ "$RC" -eq 1 ] || fail "binary blob: exit $RC"
case "$OUT" in *"still in the history"*) ;; *) fail "binary blob: $OUT" ;; esac

rm -rf "$FX"
[ "$fails" -eq 0 ] && echo PASS || { echo "$fails failure(s)"; exit 1; }
