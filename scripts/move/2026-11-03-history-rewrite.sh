#!/usr/bin/env bash
# One-time history rewrite before the ikhbat.org transfer (plan docs/plans/2026-11-02-003-move-to-ikhbat-org.md,
# user decisions 2026-11-03): every commit authored or committed by a person (the 28 Claude commits of
# 2026-10-28 become the user's), and the phrases listed in --replacements replaced in every file of every
# commit and in every message. No squashing: each commit keeps its place, only its SHA changes.
#
#   2026-11-03-history-rewrite.sh --replacements <file> [--backup <dir>]          rehearse on a mirror (dry run)
#   GIT_ALLOW_PROTECTED=1 2026-11-03-history-rewrite.sh --replacements <file> [--backup <dir>] --execute --rewrite-history
#
# <file>: git filter-repo --replace-text lines, `old==>new`, kept outside the repository (it names what it
# removes). The dry run changes nothing in the repository: it mirrors it to tmp/history-rewrite/trial.git,
# rewrites the mirror, and checks the result. The real run (both flags) first mirrors the repository to
# --backup (default ~/Developer/backups/bayan-pre-rewrite-2026-11-03.git; refuses if it exists), rewrites
# every ref in place, restores the origin URL, maps review.reviewedthrough, rewrites old SHAs cited in
# tracked .md files with one commit on chore/history-sha-refs merged into dev, and prints the force-push
# commands; it pushes nothing. Undo: the backup is a full mirror (the run prints the command).
# Exit 0 done, 1 refused or a check failed (nothing changed in the repository), 2 usage, 3 failed after the
# rewrite began (the repository is changed: run the printed undo).
set -u
HERE=$(cd "$(dirname "$0")" && pwd -P)
REPO=$(cd "$HERE/../.." && pwd -P)
REPL="" BACKUP=$HOME/Developer/backups/bayan-pre-rewrite-2026-11-03.git EXEC=0 REWRITE=0
while [ $# -gt 0 ]; do
  case "$1" in
    --replacements) REPL=${2:-}; shift 2 ;;
    --backup) BACKUP=${2:-}; shift 2 ;;
    --execute) EXEC=1; shift ;;
    --rewrite-history) REWRITE=1; shift ;;
    *) echo "usage: $0 --replacements <file> [--backup <dir>] [--execute --rewrite-history]" >&2; exit 2 ;;
  esac
done
[ -n "$REPL" ] || { echo "usage: --replacements <file> is required" >&2; exit 2; }
if [ "$EXEC" -ne "$REWRITE" ]; then echo "refuse: the real run needs both --execute and --rewrite-history" >&2; exit 2; fi
TRIAL=$REPO/tmp/history-rewrite/trial.git
MAILMAP=$REPO/tmp/history-rewrite/mailmap
CHECK=$REPO/scripts/ci/check-authors.sh
bad=0
say() { printf '%s\n' "$*"; }
refuse() { say "refuse: $*"; bad=1; }

# ---- preflight (both modes) -----------------------------------------------------------------
git filter-repo --version >/dev/null 2>&1 || refuse "git filter-repo not found (brew install git-filter-repo)"
[ -f "$REPL" ] || refuse "no replacements file: $REPL"
if [ -f "$REPL" ]; then
  n=$(grep -c '==>' "$REPL"); [ "$n" -gt 0 ] || refuse "no old==>new lines in $REPL"
  if grep -v '==>' "$REPL" | grep -q '[^[:space:]]'; then refuse "$REPL has a line without ==>"; fi
  if grep -qE '^(regex|glob):' "$REPL"; then refuse "$REPL has a regex:/glob: line; the leftover scan checks literal phrases only"; fi
  case "$(cd "$(dirname "$REPL")" && pwd -P)/" in "$REPO"/tmp/*) ;; "$REPO"/*) refuse "$REPL is inside the repository's tracked tree" ;; esac
fi
[ -x "$CHECK" ] || [ -f "$CHECK" ] || refuse "missing $CHECK"
cd "$REPO" || exit 1
[ -z "$(git status --porcelain --untracked-files=no)" ] || refuse "tracked changes in $REPO"
git symbolic-ref -q HEAD >/dev/null || refuse "HEAD is detached"
git rev-parse -q --verify refs/heads/dev >/dev/null || refuse "no dev branch"
WT=$(git worktree list --porcelain | grep -c '^worktree ')
if [ "$WT" -gt 1 ]; then
  if [ "$EXEC" -eq 1 ]; then refuse "$((WT - 1)) linked worktree(s); remove them first (their checkouts would keep the old history):"
  else say "note: $((WT - 1)) linked worktree(s); the real run refuses until they are removed:"; fi
  git worktree list | tail -n +2 | sed 's/^/  /'
fi
if [ "$EXEC" -eq 1 ] && [ -e "$BACKUP" ]; then refuse "backup $BACKUP exists"; fi
if [ "$EXEC" -eq 1 ] && [ "${GIT_ALLOW_PROTECTED:-}" != 1 ]; then refuse "the protected-branch hooks refuse rewriting dev/main: run with GIT_ALLOW_PROTECTED=1"; fi
ORIGIN=$(git remote get-url origin 2>/dev/null || true)
[ "$bad" -eq 0 ] || { say "preflight failed: nothing changed"; exit 1; }

mkdir -p "$REPO/tmp/history-rewrite"
ME="$(git log -1 --format='%an <%ae>' --author=syed@beparey.com dev)"
[ -n "$ME" ] || { say "refuse: no commit by the user on dev to take the identity from"; exit 1; }
printf '%s Claude <noreply@anthropic.com>\n' "$ME" > "$MAILMAP"
FILTER=(--mailmap "$MAILMAP" --replace-text "$REPL" --replace-message "$REPL")

# lhs phrases, for the leftover scan
LHS=$REPO/tmp/history-rewrite/lhs.txt
grep '==>' "$REPL" | sed 's/==>.*//; s/^literal://' > "$LHS"

# verify <gitdir>: no Claude identity on any ref, no lhs phrase in any version of any file
verify() {
  local g=$1 fails=0 r
  for r in $(git --git-dir="$g" for-each-ref --format='%(refname)' refs/heads refs/tags); do
    out=$(GIT_DIR=$g bash "$CHECK" "$r" 2>&1) || { say "fail: $r: ${out##*$'\n'}"; fails=1; }
  done
  # --text: filter-repo leaves blobs with a NUL byte untouched, so binary files are scanned too
  if git --git-dir="$g" log --all -p --text --format= | grep -aF -f "$LHS" | head -3 | grep -q .; then
    say "fail: a replaced phrase is still in the history:"
    git --git-dir="$g" log --all -p --text --format= | grep -aF -f "$LHS" | head -3 | tr -d '\000' | cut -c1-160 | sed 's/^/  /'
    fails=1
  fi
  if git --git-dir="$g" log --all --format=%B | grep -F -f "$LHS" | grep -q .; then say "fail: a replaced phrase is in a message"; fails=1; fi
  return $fails
}

# remap <commit-map> <file>...: old SHAs (7-40 hex, with a letter or 10+ long, one unique old commit) -> new, same length
remap() {
  local map=$1; shift
  awk -v MAP="$map" '
    BEGIN {
      while ((getline l < MAP) > 0) { split(l, a, " "); if (a[1] !~ /^[0-9a-f]+$/ || a[1] == a[2]) continue
        n++; old[n] = a[1]; new[a[1]] = a[2]; for (k = 7; k <= 12; k++) { p = substr(a[1], 1, k); cnt[p]++; who[p] = a[1] } }
    }
    function sub1(t,   p, o, i, hit, m) {
      if (length(t) < 7 || length(t) > 40) return t
      if (t !~ /[a-f]/ && length(t) < 10) return t
      if (length(t) <= 12) { if (cnt[t] == 1) { changed++; return substr(new[who[t]], 1, length(t)) } return t }
      p = substr(t, 1, 12); if (!(p in cnt)) return t
      m = 0; for (i = 1; i <= n; i++) if (index(old[i], t) == 1) { m++; hit = old[i] }
      if (m == 1) { changed++; return substr(new[hit], 1, length(t)) }
      return t
    }
    # a file is written (as FILENAME.remap) only when a reference in it changed, so nothing else moves
    function flush(   i, out) {
      if (file == "" || fchanged == changed) return
      out = file ".remap"; for (i = 1; i <= nl; i++) print buf[i] > out; close(out)
    }
    FNR == 1 { flush(); file = FILENAME; fchanged = changed; nl = 0 }
    {
      line = $0; res = ""
      while (match(line, /[0-9a-fA-F][0-9a-fA-F][0-9a-fA-F][0-9a-fA-F][0-9a-fA-F][0-9a-fA-F][0-9a-fA-F][0-9a-fA-F]*/)) {
        tok = substr(line, RSTART, RLENGTH)
        res = res substr(line, 1, RSTART - 1) (tok ~ /[A-F]/ ? tok : sub1(tok))
        line = substr(line, RSTART + RLENGTH)
      }
      buf[++nl] = res line
    }
    END { flush(); print changed + 0 }
  ' "$@"
}

# ---- rehearsal (both modes) -----------------------------------------------------------------
say "rehearse: mirror $REPO -> $TRIAL"
rm -rf "$TRIAL"
git clone -q --mirror "$REPO" "$TRIAL" || { say "fail: mirror clone"; exit 1; }
# the throwaway mirror only: the global protected-branch hooks refuse its rewritten dev/main
git --git-dir="$TRIAL" config core.hooksPath /dev/null
BEFORE=$(git --git-dir="$TRIAL" rev-list --all | wc -l | tr -d ' ')
(cd "$TRIAL" && git filter-repo --force --quiet "${FILTER[@]}") || { say "fail: filter-repo on the mirror"; exit 1; }
AFTER=$(git --git-dir="$TRIAL" rev-list --all | wc -l | tr -d ' ')
CHANGED=$(awk 'NR > 1 && $1 != $2' "$TRIAL/filter-repo/commit-map" | wc -l | tr -d ' ')
say "rehearse: $BEFORE commits before, $AFTER after, $CHANGED with a new SHA"
[ "$BEFORE" = "$AFTER" ] || { say "fail: commit count changed"; exit 1; }
verify "$TRIAL" || { say "rehearsal failed: nothing changed in $REPO"; exit 1; }
TIPDIR=$REPO/tmp/history-rewrite/tip
rm -rf "$TIPDIR"; mkdir -p "$TIPDIR"
git --git-dir="$TRIAL" archive dev -- $(git --git-dir="$TRIAL" ls-tree -r --name-only dev | grep '\.md$') | tar -x -C "$TIPDIR"
MDS=$(cd "$TIPDIR" && find . -name '*.md' | sed 's|^\./||')
NREF=$( (cd "$TIPDIR" && remap "$TRIAL/filter-repo/commit-map" $MDS) | tail -1)
say "rehearse: $NREF old SHA reference(s) in dev's .md files would be rewritten"
say "ok: rehearsal clean (identities and phrases)"
if [ "$EXEC" -eq 0 ]; then say "dry run: nothing changed in $REPO; run again with --execute --rewrite-history"; exit 0; fi

# ---- real run -------------------------------------------------------------------------------
say "run: backup mirror -> $BACKUP"
mkdir -p "$(dirname "$BACKUP")"
git clone -q --mirror "$REPO" "$BACKUP" || { say "fail: backup clone; nothing changed"; exit 1; }
git config --get review.reviewedthrough > "$BACKUP/review-reviewedthrough" 2>/dev/null || true
[ "$(git --git-dir="$BACKUP" rev-list --all | wc -l | tr -d ' ')" = "$BEFORE" ] || { say "fail: backup incomplete; nothing changed"; exit 1; }
BR=$(git symbolic-ref --short HEAD)
UNDO="GIT_ALLOW_PROTECTED=1 git fetch --force --prune --update-head-ok $BACKUP '+refs/*:refs/*' && git reset --hard"
W0=$(cat "$BACKUP/review-reviewedthrough")
[ -z "$W0" ] || UNDO="$UNDO && git config review.reviewedthrough $W0"
restore_origin() {
  if [ -n "$ORIGIN" ] && ! git remote get-url origin >/dev/null 2>&1; then git remote add origin "$ORIGIN"; say "run: origin restored ($ORIGIN)"; fi
}
# after filter-repo starts the repository is changed: restore origin, print the undo, exit 3
changed_fail() {
  say "fail: $*"; restore_origin
  say "the repository is changed and nothing is pushed. Undo: git checkout -f $BR, then"
  say "  $UNDO"; exit 3
}
say "run: git filter-repo --force (every ref)"
git filter-repo --force --quiet "${FILTER[@]}" || changed_fail "filter-repo"
GD=$(git rev-parse --git-dir)
MAP=$GD/filter-repo/commit-map
restore_origin
W=$(git config --get review.reviewedthrough 2>/dev/null || true)
if [ -n "$W" ]; then
  NW=$(awk -v o="$W" '$1 == o { print $2 }' "$MAP")
  [ -n "$NW" ] && git config review.reviewedthrough "$NW" && say "run: review.reviewedthrough $W -> $NW"
fi
verify "$GD" || changed_fail "rewritten repository does not verify"
git checkout -q dev && git checkout -q -b chore/history-sha-refs || changed_fail "cannot create chore/history-sha-refs off dev (exists?); history is rewritten, SHA references not"
FILES=$(git ls-files '*.md')
N=$(remap "$MAP" $FILES | tail -1)
for f in $FILES; do [ -f "$f.remap" ] || continue; if cmp -s "$f" "$f.remap"; then rm -f "$f.remap"; else mv "$f.remap" "$f"; fi; done
if [ -n "$(git status --porcelain --untracked-files=no)" ]; then
  git commit -qam "docs: point SHA references at the rewritten history ($N references)" || changed_fail "commit of the SHA references on chore/history-sha-refs refused"
  { git checkout -q dev && git merge -q --no-ff chore/history-sha-refs -m "Merge branch 'chore/history-sha-refs' into dev"; } || changed_fail "merge of chore/history-sha-refs into dev"
  say "run: $N SHA reference(s) rewritten, merged into dev"
else git checkout -q dev; say "run: no SHA references to rewrite"; fi
git checkout -q "$BR" 2>/dev/null || true
say "ok: history rewritten; $CHANGED commits have new SHAs. Nothing pushed. Push (yours):"
say "  GIT_ALLOW_PROTECTED=1 git push --force origin $(git for-each-ref --format='%(refname:short)' refs/heads | grep -xE 'dev|main' | tr '\n' ' ')"
say "  GIT_ALLOW_PROTECTED=1 git push --force --tags origin"
say "  git ls-remote --heads origin   # any other remote branch still has the old history: push it with --force or delete it"
say "undo (before pushing): $UNDO"
