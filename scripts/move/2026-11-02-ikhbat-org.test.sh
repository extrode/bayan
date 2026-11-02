#!/usr/bin/env bash
# Test for 2026-11-02-ikhbat-org.sh on a fixture HOME under <repo>/tmp/move-test. Prints PASS at the end.
set -euo pipefail

HERE=$(cd "$(dirname "$0")" && pwd)
ROOT=$(cd "$HERE/../.." && pwd)
SCRIPT=$HERE/2026-11-02-ikhbat-org.sh
FX=$ROOT/tmp/move-test
HOME_FX=$FX/home

export HOME=$HOME_FX
export OLD_REPO=$HOME_FX/Developer/extrode.com/bayan
export NEW_REPO=$HOME_FX/Developer/ikhbat.org/bayan
export CLAUDE_HOME=$HOME_FX/.claude
export CLAUDE_JSON=$HOME_FX/.claude.json
export NEW_REMOTE=$FX/remote.git
export PGREP=$FX/pgrep-stub
export LC_ALL=C
export GIT_AUTHOR_NAME=t GIT_AUTHOR_EMAIL=t@example.com GIT_COMMITTER_NAME=t GIT_COMMITTER_EMAIL=t@example.com
export GIT_AUTHOR_DATE='2026-01-01T00:00:00Z' GIT_COMMITTER_DATE='2026-01-01T00:00:00Z'

OKEY=$(printf '%s' "$OLD_REPO" | tr '/.' '--')
NKEY=$(printf '%s' "$NEW_REPO" | tr '/.' '--')
FAILS=0
fail() { echo "FAIL: $*"; FAILS=$(( FAILS + 1 )); }
ok_if() { local desc=$1; shift; if "$@"; then :; else fail "$desc"; fi; }

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
{"transcript":"$CLAUDE_HOME/projects/$K/s1.jsonl","cwd":"$P","project":"$K"}
{"transcript":"$CLAUDE_HOME/projects/$K--worktrees-w1/s1.jsonl","cwd":"$P/.worktrees/w1","project":"$K extra"}
{"transcript":"x","cwd":"$O-x","project":"${OK}x","note":"$O"}
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
  echo '{"workers":[]}' > "$CLAUDE_HOME/daemon/roster.json"
  printf '{"pid":%s,"cwd":"%s"}\n' "$DEAD_PID" "$O" > "$CLAUDE_HOME/sessions/dead.json"
  printf '{"cwd":"%s"}\n' "$O" > "$CLAUDE_HOME/sessions/nopid.json"
}
mk_claude_json() { # file P gh-key
  jq -n --arg p "$2" --arg o "$OLD_REPO" --arg g "$3" \
    '{numStartups:3,theme:"dark",projects:{($p):{trust:true},($p+"/sub"):{trust:false},($o+"x"):{a:1},($o+"-x"):{b:2},"/other":{c:3}},githubRepoPaths:({"other/repo":["/x"]} + {($g):[$p]})}' > "$1"
}

build_fixture() {
  case "$FX" in "$ROOT"/tmp/move-test) ;; *) echo "bad fixture path $FX"; exit 1 ;; esac
  rm -rf "$FX"; mkdir -p "$HOME_FX"
  mkdir -p "$OLD_REPO"
  git -C "$OLD_REPO" init -q -b dev
  echo hi > "$OLD_REPO/README.md"
  git -C "$OLD_REPO" add README.md
  git -C "$OLD_REPO" commit -q -m init
  git clone -q --bare "$OLD_REPO" "$NEW_REMOTE"
  git -C "$OLD_REPO" remote add origin file:///nonexistent/extrode-bayan.git
  cat > "$PGREP" <<'EOF'
#!/bin/sh
if [ "$*" = "${STUB_ARGS:-}" ]; then echo "${STUB_OUT:-}"; exit 0; fi
exit 1
EOF
  chmod +x "$PGREP"
  mk_claude_state "$OLD_REPO" "$OKEY"
  mk_claude_json "$CLAUDE_JSON" "$OLD_REPO" extrode/bayan
}

tree_hash() { # [all] : files only, .git excluded, backups excluded unless "all"
  local skip=./home/.claude/backups/*
  [ "${1:-}" = all ] && skip=./nonexistent/*
  (cd "$FX" && find . -type f -not -path '*/.git/*' -not -name .git -not -path "$skip" \
    | sort | while IFS= read -r f; do shasum "$f"; done | shasum | cut -d' ' -f1)
}
run_mv() { OUT=$(bash "$SCRIPT" "$@" 2>&1) && RC=0 || RC=$?; }
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

# --- preflight refusals
for form in "-fl /claude/versions/|/claude/versions/" "-fl ClaudeCode.app|ClaudeCode.app" "-fl bin/claude|bin/claude"; do
  build_fixture
  STUB_ARGS=${form%%|*} STUB_OUT="4242 /some/claude cmd" expect_refuse "process form $form" "${form##*|}"
done
build_fixture
STUB_ARGS="-x claude" STUB_OUT="$LIVE_PID" expect_refuse "bare argv form" "-x claude"
case "$OUT" in *"$LIVE_PID sleep 600"*) ;; *) fail "bare argv form: pid and command line not printed: $OUT" ;; esac
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
[ -f "$CLAUDE_HOME/backups/bayan-move-2026-11-02/undo.sh" ] || fail "no undo.sh"
HREF=$(tree_hash all)
# a rerun after completion changes nothing
run_mv --execute
[ "$RC" -eq 0 ] || fail "rerun exit $RC: $OUT"
[ "$(tree_hash all)" = "$HREF" ] || fail "rerun changed the tree"

# --- interrupted runs resume to the same tree
for n in 3 5 6; do
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

# --- undo restores the starting tree, twice
build_fixture
H0=$(tree_hash)
run_mv --execute
[ "$RC" -eq 0 ] || fail "execute before undo: $OUT"
for i in 1 2; do
  bash "$CLAUDE_HOME/backups/bayan-move-2026-11-02/undo.sh" > "$ROOT/tmp/move-undo.out" 2>&1 || { fail "undo $i failed"; cat "$ROOT/tmp/move-undo.out"; }
  [ "$(tree_hash)" = "$H0" ] || fail "undo $i: tree differs from the start"
  [ -d "$OLD_REPO" ] && [ ! -e "$NEW_REPO" ] || fail "undo $i: repo not back"
  [ "$(git -C "$OLD_REPO" remote get-url origin)" = file:///nonexistent/extrode-bayan.git ] || fail "undo $i: origin not restored"
done

rm -rf "$FX" "$EXP" "$EXP.json"
if [ "$FAILS" -ne 0 ]; then echo "$FAILS failure(s)"; exit 1; fi
echo PASS
