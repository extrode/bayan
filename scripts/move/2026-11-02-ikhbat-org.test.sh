#!/usr/bin/env bash
# Test for 2026-11-02-ikhbat-org.sh on a fixture HOME under <repo>/tmp/move-test. Prints PASS at the end.
set -euo pipefail

HERE=$(cd "$(dirname "$0")" && pwd -P)
ROOT=$(cd "$HERE/../.." && pwd -P)
SCRIPT=$HERE/2026-11-02-ikhbat-org.sh
FX=$ROOT/tmp/move-test
HOME_FX=$FX/home

# the enclosing repo's config, hashed now and at the end: a git command that escaped the fixture shows there
if [ -d "$ROOT/.git" ]; then REAL_GIT=$ROOT/.git
else
  REAL_GIT=$(sed -n 's/^gitdir: //p' "$ROOT/.git")
  if [ -f "$REAL_GIT/commondir" ]; then
    c=$(cat "$REAL_GIT/commondir"); case "$c" in /*) ;; *) c=$REAL_GIT/$c ;; esac
    REAL_GIT=$(cd "$c" && pwd -P)
  fi
fi
REAL_CFG_SUM=$(shasum -a 256 < "$REAL_GIT/config")
mkdir -p "$ROOT/tmp"
export GIT_CEILING_DIRECTORIES=$ROOT/tmp

export HOME=$HOME_FX
export OLD_REPO=$HOME_FX/Developer/extrode.com/bayan
export NEW_REPO=$HOME_FX/Developer/ikhbat.org/bayan
export CLAUDE_HOME=$HOME_FX/.claude
export CLAUDE_JSON=$HOME_FX/.claude.json
export NEW_REMOTE=$FX/remote.git
export PGREP=$FX/pgrep-stub
export STUB_COUNT=$ROOT/tmp/move-pgrep-count
export LC_ALL=C
export GIT_AUTHOR_NAME=t GIT_AUTHOR_EMAIL=t@example.com GIT_COMMITTER_NAME=t GIT_COMMITTER_EMAIL=t@example.com
export GIT_AUTHOR_DATE='2026-01-01T00:00:00Z' GIT_COMMITTER_DATE='2026-01-01T00:00:00Z'
REF2020=$ROOT/tmp/move-2020
touch -t 202001010000 "$REF2020"

OKEY=$(printf '%s' "$OLD_REPO" | tr '/.' '--')
NKEY=$(printf '%s' "$NEW_REPO" | tr '/.' '--')
UNDO=$CLAUDE_HOME/backups/bayan-move-2026-11-02/undo.sh
FAILS=0
fail() { echo "FAIL: $*"; FAILS=$(( FAILS + 1 )); }
ok_if() { local desc=$1; shift; if "$@"; then :; else fail "$desc"; fi; }
fstat() { stat -f '%Lp %m' "$1" 2>/dev/null || stat -c '%a %Y' "$1"; }

sleep 600 & LIVE_PID=$!
trap 'kill "$LIVE_PID" 2>/dev/null || true' EXIT
sh -c 'exit 0' & DEAD_PID=$!; wait "$DEAD_PID" || true

mk_transcripts() { # dir-suffix-key P ; writes projects/<key>... files
  local K=$1 P=$2 d
  d=$CLAUDE_HOME/projects/$K; mkdir -p "$d/memory" "$d/s1/subagents"
  cat > "$d/s1.jsonl" <<EOF
{"type":"user","cwd":"$P","message":{"content":"see $OLD_REPO/docs and \"cwd\":\"$OLD_REPO\" ok"}}
{"cwd":"$P/sub","n":1}
{"cwd":"${OLD_REPO}-x"}
{"cwd":"${OLD_REPO}.x"}
{"cwd":"${OLD_REPO}_x"}
EOF
  printf '{"cwd":"%s"}\n{"cwd":"%s/x"}' "$P" "$P" > "$d/s2.jsonl"
  printf '{"cwd":"/elsewhere","message":"%s"}\n' "$OLD_REPO" > "$d/s3.jsonl"
  printf '{"cwd":"%s","agent":1}\n' "$P" > "$d/s1/subagents/agent.jsonl"
  echo "memory mentions $OLD_REPO" > "$d/memory/m.md"
}
mk_claude_state() { # P K ; writes the whole fixture ~/.claude (rewritable parts use P and K)
  local P=$1 K=$2 O=$OLD_REPO OK=$OKEY
  mk_transcripts "$K" "$P"
  mk_transcripts "$K--worktrees-w1" "$P/.worktrees/w1"
  mk_transcripts "$K-tmp-wt-t" "$P/tmp/wt/t"
  mkdir -p "$CLAUDE_HOME/projects/${OK}x"
  printf '{"cwd":"%sx"}\n' "$O" > "$CLAUDE_HOME/projects/${OK}x/c.jsonl"
  mkdir -p "$CLAUDE_HOME/lessons/$K" "$CLAUDE_HOME/lessons/$K--worktrees-w1" "$CLAUDE_HOME/lessons/${OK}x"
  echo "lesson a" > "$CLAUDE_HOME/lessons/$K/a.md"
  echo "lesson b" > "$CLAUDE_HOME/lessons/$K--worktrees-w1/b.md"
  echo "lesson c" > "$CLAUDE_HOME/lessons/${OK}x/c.md"
  cat > "$CLAUDE_HOME/lessons/INDEX.md" <<EOF
- [a]($K/a.md) first
- see ($K)
- wt $K--worktrees-w1/b.md
- other ${OK}x/c.md and ${OK}_x/c.md
- quoted "$K" end
- $K trailing
- path $O/docs stays
EOF
  cat > "$CLAUDE_HOME/lessons/queue.jsonl" <<EOF
{"transcript":"$CLAUDE_HOME/projects/$K/s1.jsonl","cwd":"$P","project":"$K","repoTop":"$P"}
{"transcript":"$CLAUDE_HOME/projects/$K--worktrees-w1/s1.jsonl","cwd":"$P/.worktrees/w1","project":"$K extra"}
{"transcript":"x","cwd":"$O-x","project":"${OK}x","note":"$P"}
EOF
  cat > "$CLAUDE_HOME/history.jsonl" <<EOF
{"display":"fix $O/foo","project":"$P","ts":1}
{"display":"x","project":"$P/sub","ts":2}
{"display":"y","project":"$O-x","ts":3}
EOF
  mkdir -p "$CLAUDE_HOME/workers" "$CLAUDE_HOME/jobs/aaa" "$CLAUDE_HOME/jobs/bbb" "$CLAUDE_HOME/daemon" "$CLAUDE_HOME/sessions"
  jq -n --arg p "$P" --arg o "$O" '{w1:{cwd:$p,repo:$p,worktree:($p+"/.worktrees/w1"),criteria:("read "+$p+"/docs and "+$o+"-x/foo"),note:$o}}' > "$CLAUDE_HOME/workers/registry.json"
  jq -n --arg p "$P" --arg k "$K" --arg o "$O" '{state:"done",cwd:$p,linkScanPath:($p+"/x"),fan:[{label:($k+"--worktrees-w1 fine")}],keep:($o+"-x")}' > "$CLAUDE_HOME/jobs/aaa/state.json"
  echo '{"state":"working","cwd":"/elsewhere"}' > "$CLAUDE_HOME/jobs/bbb/state.json"
  # roster shape as in the real daemon/roster.json: workers keyed by id, each with pid, replPid, cwd, dispatch.cwd
  jq -n --arg p "$P" --argjson d "$DEAD_PID" '{proto:1,workers:{w9:{pid:$d,replPid:$d,cwd:$p,dispatch:{cwd:($p+"/.worktrees/w9")}},w7:{pid:$d,cwd:"/elsewhere"}}}' > "$CLAUDE_HOME/daemon/roster.json"
  printf '{"pid":%s,"cwd":"%s"}\n' "$DEAD_PID" "$O" > "$CLAUDE_HOME/sessions/dead.json"
  printf '{"cwd":"%s"}\n' "$O" > "$CLAUDE_HOME/sessions/nopid.json"
}
mk_claude_json() { # file P gh-key
  jq -n --arg p "$2" --arg o "$OLD_REPO" --arg g "$3" \
    '{numStartups:3,theme:"dark",projects:{($p):{trust:true},($p+"/sub"):{trust:false},($o+"x"):{a:1},($o+"-x"):{b:2},"/other":{c:3}},githubRepoPaths:({"other/repo":["/x"]} + {($g):[$p]})}' > "$1"
}

build_fixture() {
  case "$FX" in "$ROOT"/tmp/move-test) ;; *) echo "bad fixture path $FX"; exit 1 ;; esac
  rm -rf "$FX" "$STUB_COUNT"; mkdir -p "$HOME_FX"
  mkdir -p "$OLD_REPO"
  git -C "$OLD_REPO" init -q -b dev
  echo hi > "$OLD_REPO/README.md"
  git -C "$OLD_REPO" add README.md
  git -C "$OLD_REPO" commit -q -m init
  git clone -q --bare "$OLD_REPO" "$NEW_REMOTE"
  git -C "$OLD_REPO" remote add origin file:///nonexistent/extrode-bayan.git
  cat > "$PGREP" <<'EOF'
#!/bin/sh
if [ -n "${STUB_RC:-}" ]; then exit "$STUB_RC"; fi
if [ -n "${STUB_FROM:-}" ]; then
  n=$(( $(cat "$STUB_COUNT" 2>/dev/null || echo 0) + 1 )); echo "$n" > "$STUB_COUNT"
  [ "$n" -ge "$STUB_FROM" ] || exit 1
fi
if [ "$*" = "${STUB_ARGS:-}" ]; then echo "${STUB_OUT:-}"; exit 0; fi
exit 1
EOF
  chmod +x "$PGREP"
  mk_claude_state "$OLD_REPO" "$OKEY"
  mk_claude_json "$CLAUDE_JSON" "$OLD_REPO" extrode/bayan
  find "$CLAUDE_HOME" -type f -exec touch -t 202001010000 {} +
}

tree_hash() { # [all] : files only, .git excluded, backups excluded unless "all"; claude home files outside backups add mode and mtime
  local skip=./home/.claude/backups/*
  [ "${1:-}" = all ] && skip=./nonexistent/*
  (cd "$FX" && find . -type f -not -path '*/.git/*' -not -name .git -not -path "$skip" \
    | sort | while IFS= read -r f; do
        case "$f" in ./home/.claude/backups/*) ;; ./home/.claude/*) echo "$f $(fstat "$f")" ;; esac
        shasum "$f" 2>/dev/null || echo "unreadable $f"
      done | shasum | cut -d' ' -f1)
}
run_mv() { OUT=$(bash "$SCRIPT" "$@" 2>&1) && RC=0 || RC=$?; }
run_undo() { OUT=$(bash "$UNDO" "$@" 2>&1) && RC=0 || RC=$?; }
expect_refuse() { # desc keyword : runs --execute, must refuse and change nothing
  local desc=$1 kw=$2 before
  before=$(tree_hash all)
  run_mv --execute
  [ "$RC" -ne 0 ] || fail "$desc: expected a refusal, got exit 0"
  case "$OUT" in *"refuse:"*"$kw"*) ;; *) fail "$desc: no 'refuse: ... $kw' in output: $OUT" ;; esac
  [ "$(tree_hash all)" = "$before" ] || fail "$desc: tree changed"
  [ -d "$OLD_REPO" ] || fail "$desc: repo moved"
}

# --- dry run changes nothing; done job naming the old path and a working job naming nothing do not refuse
build_fixture
H0=$(tree_hash all)
run_mv
[ "$RC" -eq 0 ] || fail "dry run exit $RC: $OUT"
case "$OUT" in *"would:"*) ;; *) fail "dry run printed no would:" ;; esac
[ "$(tree_hash all)" = "$H0" ] || fail "dry run changed the tree"
[ -d "$OLD_REPO" ] && [ ! -e "$NEW_REPO" ] || fail "dry run moved the repo"
# F15: the count is files with an old cwd; files naming the old path elsewhere are counted apart
case "$OUT" in *'rewrite "cwd" in 9 transcript files'*) ;; *) fail "dry run cwd count: $OUT" ;; esac
case "$OUT" in *"info: 3 other transcript files"*) ;; *) fail "dry run other count: $OUT" ;; esac

# --- preflight refusals
for form in "-fl /claude/versions/|/claude/versions/" "-fl ClaudeCode.app|ClaudeCode.app" "-fl bin/claude|bin/claude"; do
  build_fixture
  STUB_ARGS=${form%%|*} STUB_OUT="4242 /some/claude cmd" expect_refuse "process form $form" "${form##*|}"
done
build_fixture
STUB_ARGS="-x claude" STUB_OUT="$LIVE_PID" expect_refuse "bare argv form" "-x claude"
case "$OUT" in *"$LIVE_PID sleep 600"*) ;; *) fail "bare argv form: pid and command line not printed: $OUT" ;; esac
# F13: pgrep failing is not "no process"
build_fixture
STUB_RC=2 expect_refuse "pgrep exit 2" "exit 2"
build_fixture
printf '{"pid":%s,"cwd":"%s/sub"}\n' "$LIVE_PID" "$OLD_REPO" > "$CLAUDE_HOME/sessions/live.json"
expect_refuse "live session" "live session"
build_fixture
printf '{"pid":%s,"cwd":"/elsewhere"}\n' "$LIVE_PID" > "$CLAUDE_HOME/sessions/live.json"
run_mv; [ "$RC" -eq 0 ] || fail "live session with another cwd must not refuse: $OUT"
build_fixture
printf '{"pid":%s}\n' "$LIVE_PID" > "$CLAUDE_HOME/daemon.lock"
expect_refuse "live daemon.lock" "daemon.lock"
build_fixture
echo "{\"state\":\"working\",\"cwd\":\"$OLD_REPO\"}" > "$CLAUDE_HOME/jobs/bbb/state.json"
expect_refuse "working job" "job"
# F4: an unreadable working job naming the old path refuses, naming the file
build_fixture
echo "{\"state\":\"working\",\"cwd\":\"$OLD_REPO\"}" > "$CLAUDE_HOME/jobs/bbb/state.json"
chmod 000 "$CLAUDE_HOME/jobs/bbb/state.json"
expect_refuse "unreadable job" "jobs/bbb/state.json"
chmod 644 "$CLAUDE_HOME/jobs/bbb/state.json"
# F10: a live roster worker at the old path refuses (the ended one in the fixture is rewritten, below)
build_fixture
jq --arg o "$OLD_REPO" --argjson l "$LIVE_PID" '.workers.w8 = {pid:$l, cwd:$o}' "$CLAUDE_HOME/daemon/roster.json" > "$FX/r.tmp"
mv "$FX/r.tmp" "$CLAUDE_HOME/daemon/roster.json"
expect_refuse "live roster worker" "roster"
# F5: a bad history line and an existing new projects key refuse before any change
build_fixture
echo '{bad' >> "$CLAUDE_HOME/history.jsonl"
expect_refuse "bad history line" "history.jsonl"
build_fixture
jq --arg n "$NEW_REPO" '.projects[$n] = {trust:true}' "$CLAUDE_JSON" > "$FX/c.tmp" && mv "$FX/c.tmp" "$CLAUDE_JSON"
expect_refuse "new projects key exists" "projects"
build_fixture
echo untracked > "$OLD_REPO/new.txt"
expect_refuse "dirty repo" "not clean"
build_fixture
git -C "$OLD_REPO" worktree add -q -b wt "$FX/linked"
expect_refuse "linked worktree" "linked worktree"
build_fixture
echo more >> "$OLD_REPO/README.md"; git -C "$OLD_REPO" commit -q -am more
expect_refuse "ls-remote mismatch" "ls-remote"
build_fixture
mkdir -p "$NEW_REPO"
expect_refuse "NEW_REPO exists" "exists"
build_fixture
mkdir -p "$CLAUDE_HOME/projects/$NKEY"
expect_refuse "target project dir exists" "exists"
build_fixture
mkdir -p "$(dirname "$OLD_REPO")/bayan-x"
expect_refuse "sibling bayan-x" "sibling"

# --- full execute (boundary cases are inside the fixture and the expected tree)
build_fixture
run_mv --execute
[ "$RC" -eq 0 ] || fail "execute exit $RC: $OUT"
case "$OUT" in *"ok: verified"*) ;; *) fail "no 'ok: verified': $OUT" ;; esac
[ -d "$NEW_REPO" ] && [ ! -e "$OLD_REPO" ] || fail "repo not moved"
[ "$(git -C "$NEW_REPO" remote get-url origin)" = "$NEW_REMOTE" ] || fail "origin not set"
[ "$(git -C "$NEW_REPO" rev-parse origin/dev)" = "$(git -C "$NEW_REPO" rev-parse dev)" ] || fail "origin/dev not fetched"
EXP=$ROOT/tmp/move-expected
rm -rf "$EXP"; mkdir -p "$EXP"
SAVE_CH=$CLAUDE_HOME
CLAUDE_HOME=$EXP mk_claude_state "$NEW_REPO" "$NKEY"
# the queue transcript paths name CLAUDE_HOME, which the builder wrote as $EXP: normalize
sed "s|$EXP|$SAVE_CH|g" "$EXP/lessons/queue.jsonl" > "$EXP/q.tmp" && mv "$EXP/q.tmp" "$EXP/lessons/queue.jsonl"
diff -r -x backups "$CLAUDE_HOME" "$EXP" > "$ROOT/tmp/move-diff.out" 2>&1 || { fail "claude home differs from expected:"; head -30 "$ROOT/tmp/move-diff.out"; }
mk_claude_json "$EXP.json" "$NEW_REPO" ikhbat-org/bayan
cmp -s "$EXP.json" "$CLAUDE_JSON" || fail ".claude.json differs from expected"
[ -f "$UNDO" ] || fail "no undo.sh"
# F6: rewrites keep the mtime
[ "$(fstat "$CLAUDE_HOME/projects/$NKEY/s1.jsonl")" = "$(fstat "$REF2020")" ] || fail "rewritten transcript lost its 2020-01-01 mtime"
# F7: queue rows rewrite every string value
[ "$(jq -r 'select(.repoTop) | .repoTop' "$CLAUDE_HOME/lessons/queue.jsonl")" = "$NEW_REPO" ] || fail "queue repoTop not rewritten"
# F10: the ended roster worker is rewritten
[ "$(jq -r '.workers.w9.cwd' "$CLAUDE_HOME/daemon/roster.json")" = "$NEW_REPO" ] || fail "ended roster worker not rewritten"
HREF=$(tree_hash all)
# a rerun after completion changes nothing
run_mv --execute
[ "$RC" -eq 0 ] || fail "rerun exit $RC: $OUT"
[ "$(tree_hash all)" = "$HREF" ] || fail "rerun changed the tree"

# --- F8: githubRepoPaths keeps other entries and merges an existing new one
build_fixture
jq --arg o "$OLD_REPO" '.githubRepoPaths["extrode/bayan"] = [$o, "/elsewhere/bayan"] | .githubRepoPaths["ikhbat-org/bayan"] = ["/pre"]' \
  "$CLAUDE_JSON" > "$FX/c.tmp" && mv "$FX/c.tmp" "$CLAUDE_JSON"
run_mv --execute
[ "$RC" -eq 0 ] || fail "githubRepoPaths run exit $RC: $OUT"
GOT=$(jq -c '.githubRepoPaths["ikhbat-org/bayan"]' "$CLAUDE_JSON")
WANT=$(jq -cn --arg n "$NEW_REPO" '["/pre", $n, "/elsewhere/bayan"]')
[ "$GOT" = "$WANT" ] || fail "githubRepoPaths: got $GOT, want $WANT"

# --- interrupted runs resume to the same tree (F16: 4, 7, 8 too)
for n in 3 4 5 6 7 8; do
  build_fixture
  STOP_AFTER=$n run_mv --execute
  [ "$RC" -eq 99 ] || fail "stop after $n: exit $RC: $OUT"
  run_mv --execute
  [ "$RC" -eq 0 ] || fail "resume after $n: exit $RC: $OUT"
  [ "$(tree_hash all)" = "$HREF" ] || fail "resume after $n ends in a different tree"
done

# --- interrupted after the mv, before its manifest line
build_fixture
STOP_AFTER=3 run_mv --execute
[ "$RC" -eq 99 ] || fail "stop after 3 (no marker): exit $RC: $OUT"
M=$CLAUDE_HOME/backups/bayan-move-2026-11-02/manifest.txt
grep -v "^done$(printf '\t')3\$" "$M" > "$M.new" && mv "$M.new" "$M"
run_mv --execute
[ "$RC" -eq 0 ] || fail "resume after mv without marker: exit $RC: $OUT"
case "$OUT" in *"skip: "*"already at"*) ;; *) fail "step 3 was not a skip: $OUT" ;; esac
[ "$(tree_hash all)" = "$HREF" ] || fail "resume after mv without marker ends in a different tree"

# --- F9: a Claude process appearing mid-run stops before step 6; a rerun resumes
build_fixture
STUB_FROM=5 STUB_ARGS="-fl /claude/versions/" STUB_OUT="4242 /x/claude/versions/2.1" run_mv --execute
[ "$RC" -ne 0 ] && [ "$RC" -ne 99 ] || fail "mid-run process: exit $RC: $OUT"
case "$OUT" in *"before step 6"*"resume: "*"$NEW_REPO/scripts/move/2026-11-02-ikhbat-org.sh --execute"*) ;; *) fail "mid-run process: no stop and resume: $OUT" ;; esac
grep -q "^done$(printf '\t')5\$" "$M" && ! grep -q "^done$(printf '\t')6\$" "$M" || fail "mid-run process: not stopped between steps 5 and 6"
rm -f "$STUB_COUNT"
run_mv --execute
[ "$RC" -eq 0 ] || fail "resume after mid-run process: exit $RC: $OUT"
[ "$(tree_hash all)" = "$HREF" ] || fail "resume after mid-run process ends in a different tree"

# --- F11: a failed fetch after the move is a warning; later steps still run
build_fixture
STOP_AFTER=3 run_mv --execute
[ "$RC" -eq 99 ] || fail "stop after 3 (fetch): exit $RC: $OUT"
NEW_REMOTE=$FX/missing.git run_mv --execute
[ "$RC" -eq 0 ] || fail "fetch failure aborted: exit $RC: $OUT"
case "$OUT" in *"warn: "*"fetch"*"ok: verified"*) ;; *) fail "fetch failure: no warn: line or no ok: verified: $OUT" ;; esac

# --- G6: a transcript gaining an old cwd after step 2 is backed up, listed and rewritten; undo restores it
build_fixture
STOP_AFTER=2 run_mv --execute
[ "$RC" -eq 99 ] || fail "stop after 2: exit $RC: $OUT"
printf '{"cwd":"%s"}\n' "$OLD_REPO" > "$CLAUDE_HOME/projects/$OKEY/late.jsonl"
LATE0=$(shasum < "$CLAUDE_HOME/projects/$OKEY/late.jsonl")
run_mv --execute
[ "$RC" -eq 0 ] || fail "late transcript: exit $RC: $OUT"
case "$OUT" in *"note: "*"late.jsonl"*"ok: verified"*) ;; *) fail "late transcript: no note: line: $OUT" ;; esac
grep -qF "\"cwd\":\"$NEW_REPO\"" "$CLAUDE_HOME/projects/$NKEY/late.jsonl" || fail "late transcript not rewritten"
[ -f "$CLAUDE_HOME/backups/bayan-move-2026-11-02/files/projects/$OKEY/late.jsonl" ] || fail "late transcript not backed up"
grep -qF "$CLAUDE_HOME/projects/$NKEY/late.jsonl" "$CLAUDE_HOME/backups/bayan-move-2026-11-02/written.sha256" || fail "late transcript: no write hash"
run_undo
[ "$RC" -eq 0 ] || fail "undo after late transcript: exit $RC: $OUT"
[ "$(shasum < "$CLAUDE_HOME/projects/$OKEY/late.jsonl" 2>/dev/null)" = "$LATE0" ] || fail "undo did not restore the late transcript"

# --- G6: an unreadable transcript under the old key refuses at preflight, naming it
build_fixture
chmod 000 "$CLAUDE_HOME/projects/$OKEY/s2.jsonl"
expect_refuse "unreadable transcript" "s2.jsonl"
case "$OUT" in *"preflight failed"*) ;; *) fail "unreadable transcript: not refused at preflight: $OUT" ;; esac
chmod 644 "$CLAUDE_HOME/projects/$OKEY/s2.jsonl"

# --- G3: a projects key under NEW_REPO refuses at preflight
build_fixture
jq --arg n "$NEW_REPO/sub" '.projects[$n] = {trust:true}' "$CLAUDE_JSON" > "$FX/c.tmp" && mv "$FX/c.tmp" "$CLAUDE_JSON"
expect_refuse "projects key under NEW_REPO" "under $NEW_REPO"
case "$OUT" in *"preflight failed"*) ;; *) fail "projects key under NEW_REPO: not refused at preflight: $OUT" ;; esac

# --- G1 (a): undo before step 9 keeps a history row appended after step 6
build_fixture
STOP_AFTER=6 run_mv --execute
[ "$RC" -eq 99 ] || fail "stop after 6 (G1): exit $RC: $OUT"
printf '{"display":"later","project":"%s","ts":9}\n' "$NEW_REPO" >> "$CLAUDE_HOME/history.jsonl"
run_undo
[ "$RC" -ne 0 ] || fail "undo after stop 6 and a later row: expected a refusal"
case "$OUT" in *"refuse: "*"history.jsonl"*) ;; *) fail "undo after stop 6: history.jsonl not listed: $OUT" ;; esac
grep -q '"later"' "$CLAUDE_HOME/history.jsonl" || fail "undo after stop 6: row lost"
[ -d "$NEW_REPO" ] && [ ! -e "$OLD_REPO" ] || fail "undo after stop 6: repo moved"

# --- G2 (b): a row appended between a mid-run stop and the resume makes undo refuse
build_fixture
STUB_FROM=5 STUB_ARGS="-fl /claude/versions/" STUB_OUT="4242 /x/claude/versions/2.1" run_mv --execute
[ "$RC" -ne 0 ] && [ "$RC" -ne 99 ] || fail "G2 mid-run process: exit $RC: $OUT"
rm -f "$STUB_COUNT"
printf '{"display":"later","project":"%s","ts":9}\n' "$OLD_REPO" >> "$CLAUDE_HOME/history.jsonl"
run_mv --execute
[ "$RC" -eq 0 ] || fail "G2 resume: exit $RC: $OUT"
run_undo
[ "$RC" -ne 0 ] || fail "undo after a row before the resume: expected a refusal"
case "$OUT" in *"refuse: "*"history.jsonl"*) ;; *) fail "undo after a row before the resume: history.jsonl not listed: $OUT" ;; esac
grep -q '"later"' "$CLAUDE_HOME/history.jsonl" || fail "undo after a row before the resume: row lost"

# --- G4: undo refuses while a Claude process runs
build_fixture
run_mv --execute
[ "$RC" -eq 0 ] || fail "execute before G4 undo: $OUT"
HB=$(tree_hash all)
STUB_ARGS="-fl bin/claude" STUB_OUT="4242 /x/bin/claude" run_undo
[ "$RC" -ne 0 ] || fail "undo with a Claude process: expected a refusal"
case "$OUT" in *"refuse: "*"bin/claude"*) ;; *) fail "undo with a Claude process: no refusal: $OUT" ;; esac
[ "$(tree_hash all)" = "$HB" ] || fail "undo with a Claude process changed the tree"

# --- G5: undo refuses when the moved checkout has a linked worktree
git -C "$NEW_REPO" worktree add -q -b wt2 "$FX/linked2"
HB=$(tree_hash all)
run_undo
[ "$RC" -ne 0 ] || fail "undo with a linked worktree: expected a refusal"
case "$OUT" in *"refuse: "*"linked worktree"*"linked2"*) ;; *) fail "undo with a linked worktree: not named: $OUT" ;; esac
[ "$(tree_hash all)" = "$HB" ] && [ -d "$NEW_REPO" ] || fail "undo with a linked worktree changed the tree"

# --- F1: undo refuses to run git in a dir that is not a checkout's top
build_fixture
STOP_AFTER=2 run_mv --execute
[ "$RC" -eq 99 ] || fail "stop after 2 (undo): exit $RC: $OUT"
rm -rf "$OLD_REPO"; mkdir "$OLD_REPO"
run_undo
[ "$RC" -ne 0 ] || fail "undo with an empty OLD_REPO: expected a refusal"
case "$OUT" in *"refuse: "*"not the top"*) ;; *) fail "undo with an empty OLD_REPO: no refusal: $OUT" ;; esac
[ ! -e "$CLAUDE_HOME/backups/bayan-move-2026-11-02/undone" ] || fail "undo with an empty OLD_REPO: marked undone"

# --- F2: undo keeps changes made after the move unless told to discard them
build_fixture
run_mv --execute
[ "$RC" -eq 0 ] || fail "execute before F2 undo: $OUT"
printf '{"display":"later","project":"%s","ts":9}\n' "$NEW_REPO" >> "$CLAUDE_HOME/history.jsonl"
run_undo
[ "$RC" -ne 0 ] || fail "undo after a later change: expected a refusal"
case "$OUT" in *"refuse: "*"history.jsonl"*) ;; *) fail "undo after a later change: file not listed: $OUT" ;; esac
grep -q '"later"' "$CLAUDE_HOME/history.jsonl" || fail "undo after a later change: row lost"
[ -d "$NEW_REPO" ] && [ ! -e "$OLD_REPO" ] || fail "undo after a later change: repo moved"
run_undo --discard-changes-since-move
[ "$RC" -eq 0 ] || fail "undo --discard-changes-since-move: exit $RC: $OUT"
[ -d "$OLD_REPO" ] && ! grep -q '"later"' "$CLAUDE_HOME/history.jsonl" || fail "undo --discard-changes-since-move did not restore"

# --- F3: undo refuses when a rename source and target both exist
build_fixture
run_mv --execute
[ "$RC" -eq 0 ] || fail "execute before F3 undo: $OUT"
mkdir -p "$CLAUDE_HOME/projects/$OKEY"
HB=$(tree_hash all)
run_undo
[ "$RC" -ne 0 ] || fail "undo with both dirs: expected a refusal"
case "$OUT" in *"refuse: both "*"$OKEY"*) ;; *) fail "undo with both dirs: no refusal naming them: $OUT" ;; esac
case "$OUT" in *"ok: undone"*) fail "undo with both dirs printed ok: undone" ;; esac
[ ! -e "$CLAUDE_HOME/backups/bayan-move-2026-11-02/undone" ] || fail "undo with both dirs: marked undone"
[ "$(tree_hash all)" = "$HB" ] || fail "undo with both dirs changed the tree"

# --- undo restores the starting tree, twice
build_fixture
H0=$(tree_hash)
run_mv --execute
[ "$RC" -eq 0 ] || fail "execute before undo: $OUT"
for i in 1 2; do
  bash "$UNDO" > "$ROOT/tmp/move-undo.out" 2>&1 || { fail "undo $i failed"; cat "$ROOT/tmp/move-undo.out"; }
  [ "$(tree_hash)" = "$H0" ] || fail "undo $i: tree differs from the start"
  [ -d "$OLD_REPO" ] && [ ! -e "$NEW_REPO" ] || fail "undo $i: repo not back"
  [ "$(git -C "$OLD_REPO" remote get-url origin)" = file:///nonexistent/extrode-bayan.git ] || fail "undo $i: origin not restored"
done

rm -rf "$FX" "$EXP" "$EXP.json" "$STUB_COUNT" "$REF2020"
# F1: no git command reached the enclosing repo
[ "$(shasum -a 256 < "$REAL_GIT/config")" = "$REAL_CFG_SUM" ] || fail "the enclosing repo's .git/config changed"
if [ "$FAILS" -ne 0 ]; then echo "$FAILS failure(s)"; exit 1; fi
echo PASS
