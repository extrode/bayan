#!/usr/bin/env bash
# Move Bayān to the Ikhbat Foundation: repo checkout, git remote, Claude state.
# Plan: docs/plans/2026-11-02-003-move-to-ikhbat-org.md (step 2). Dry run by default; --execute acts.
# Test hooks: STOP_AFTER=<step> exits 99 once that step is recorded as done; STOP_AFTER_MV=<file> exits 99
# right after a rewrite's mv onto that file.
set -euo pipefail
export LC_ALL=C GIT_OPTIONAL_LOCKS=0

OLD_REPO=${OLD_REPO:-/Users/sy/Developer/extrode.com/bayan}
NEW_REPO=${NEW_REPO:-/Users/sy/Developer/ikhbat.org/bayan}
CLAUDE_HOME=${CLAUDE_HOME:-$HOME/.claude}
CLAUDE_JSON=${CLAUDE_JSON:-$HOME/.claude.json}
NEW_REMOTE=${NEW_REMOTE:-git@github.com:ikhbat-org/bayan.git}
PGREP=${PGREP:-pgrep}
CLI_NAME=claude
# git never looks for a repository above either checkout's parent
export GIT_CEILING_DIRECTORIES="$(dirname "$OLD_REPO"):$(dirname "$NEW_REPO")"

EXECUTE=0
case "${1:-}" in
  "") ;;
  --execute) EXECUTE=1 ;;
  *) echo "usage: $0 [--execute]" >&2; exit 2 ;;
esac

TAB=$(printf '\t')
key_of() { printf '%s' "$1" | tr '/.' '--'; }
OKEY=$(key_of "$OLD_REPO")
NKEY=$(key_of "$NEW_REPO")
DP=$(( ${#NEW_REPO} - ${#OLD_REPO} ))
DK=$(( ${#NKEY} - ${#OKEY} ))
BACKUP=$CLAUDE_HOME/backups/bayan-move-2026-11-02
MANIFEST=$BACKUP/manifest.txt
# "<sha256> <path>" of each file right after the run wrote it; undo.sh trusts only these
WRITTEN=$BACKUP/written.sha256

say() { printf '%s\n' "$*"; }

# Fixed-string rewrite. mode: fields (values of the named JSON fields), all (every JSON string
# value), text (raw text). Paths end at a char outside [A-Za-z0-9._-], keys outside [A-Za-z0-9._].
# Both start at the string's start or after a char outside [A-Za-z0-9._-] (paths: a run of / counts as
# one char, so file:///O and //O match but /a/O does not); in the
# JSON modes an escape such as \n before the match counts as its decoded char, a boundary.
# Writes the output file only when something changed; prints "<path hits> <key hits>".
read -r -d '' PERL_RW <<'EOF' || true
use strict; use warnings;
my ($mode, $in, $out) = @ARGV;
my $fields = $ENV{RW_FIELDS} // '';
my ($po, $pn, $ko, $kn) = map { $ENV{$_} // '' } qw(RW_OLD RW_NEW RW_OLDK RW_NEWK);
my ($pc, $kc) = (0, 0);
sub left_ok {
  my ($s, $i, $lcls) = @_;
  return 1 if $i == 0;
  if (substr($s, $i - 1, 1) eq '/' && '/' =~ $lcls) {
    my $j = $i - 1;
    $j-- while $j > 0 && substr($s, $j - 1, 1) eq '/';
    return left_ok($s, $j, $lcls);
  }
  return 1 if substr($s, $i - 1, 1) !~ $lcls;
  return 0 if $mode eq 'text' || substr($s, $i - 1, 1) !~ /[bfnrt]/;
  my $k = 0;
  $k++ while $i - 2 - $k >= 0 && substr($s, $i - 2 - $k, 1) eq '\\';
  return $k % 2;
}
sub rw1 {
  my ($s, $o, $n, $cls, $lcls, $cnt) = @_;
  return $s if $o eq '';
  my ($r, $p) = ('', 0);
  while ((my $i = index($s, $o, $p)) >= 0) {
    my $e = $i + length($o);
    my $c = substr($s, $e, 1);
    if (left_ok($s, $i, $lcls) && ($c eq '' || $c !~ $cls)) { $r .= substr($s, $p, $i - $p) . $n; $$cnt++; }
    else { $r .= substr($s, $p, $e - $p); }
    $p = $e;
  }
  return $r . substr($s, $p);
}
sub rw {
  my $s = shift;
  $s = rw1($s, $po, $pn, qr/[A-Za-z0-9._-]/, qr/[A-Za-z0-9._\/-]/, \$pc);
  return rw1($s, $ko, $kn, qr/[A-Za-z0-9._]/, qr/[A-Za-z0-9._-]/, \$kc);
}
open(my $fh, '<', $in) or die "open $in: $!";
binmode $fh;
my $d = do { local $/; <$fh> };
close $fh;
$d = '' unless defined $d;
if ($mode eq 'fields') {
  $d =~ s/("(?:$fields)"\s*:\s*")((?:[^"\\]++|\\.)*+)"/my ($p, $v) = ($1, $2); $p . rw($v) . '"'/ge;
} elsif ($mode eq 'all') {
  $d =~ s/"((?:[^"\\]++|\\.)*+)"/my $v = $1; '"' . rw($v) . '"'/ge;
} else {
  $d = rw($d);
}
if ($out ne '' && $pc + $kc > 0) {
  open(my $oh, '>', $out) or die "open $out: $!";
  binmode $oh;
  print $oh $d;
  close $oh;
  chmod((stat $in)[2] & 07777, $out);
}
print "$pc $kc\n";
EOF

# rw_run MODE FIELDS IN [OUT] [keyonly]
rw_run() {
  local po=$OLD_REPO pn=$NEW_REPO
  if [ "${5:-}" = keyonly ]; then po=; pn=; fi
  RW_FIELDS=$2 RW_OLD=$po RW_NEW=$pn RW_OLDK=$OKEY RW_NEWK=$NKEY \
    perl -e "$PERL_RW" -- "$1" "$3" "${4:-}"
}

# has_hits MODE FIELDS FILE [keyonly]: status 0 when the rewrite would change FILE. Call it
# directly, never inside $(...): an unreadable file stops the whole run.
has_hits() {
  local c
  if ! c=$(rw_run "$1" "$2" "$3" "" "${4:-}" 2>/dev/null); then say "refuse: cannot read $3"; exit 1; fi
  [ $(( ${c% *} + ${c#* } )) -gt 0 ]
}

json_ok() { jq empty "$1" >/dev/null 2>&1; }
jsonl_ok() { jq -nR 'inputs | select(length > 0) | fromjson | empty' "$1" >/dev/null 2>&1; }
sha() { shasum -a 256 < "$1" | cut -d' ' -f1; }

# git only in a dir that is the top of its own checkout, never in an enclosing one
is_top() {
  local p t
  p=$(cd -P "$1" 2>/dev/null && pwd -P) || return 1
  t=$(git -C "$1" rev-parse --show-toplevel 2>/dev/null) || return 1
  [ "$t" = "$p" ]
}
need_top() { is_top "$1" || { say "refuse: $1 is not the top of its own git checkout; git not run there"; exit 1; }; }
g() { need_top "$1"; git -C "$@"; }

# rewrite_file FILE MODE FIELDS JSON(json|"") [keyonly]
rewrite_file() {
  local f=$1 tmp counts pc kc lb la sb sa want pre bak
  tmp=$f.mvtmp.$$
  rm -f "$tmp"
  counts=$(rw_run "$2" "$3" "$f" "$tmp" "${5:-}") || { rm -f "$tmp"; say "refuse: rewrite of $f failed"; exit 1; }
  pc=${counts% *}; kc=${counts#* }
  if [ ! -e "$tmp" ]; then say "skip: $f (nothing to rewrite)"; return 0; fi
  pre=$(sha "$f")
  lb=$(( $(wc -l < "$f") )); la=$(( $(wc -l < "$tmp") ))
  sb=$(( $(wc -c < "$f") )); sa=$(( $(wc -c < "$tmp") ))
  want=$(( sb + pc * DP + kc * DK ))
  if [ "$lb" -ne "$la" ] || [ "$sa" -ne "$want" ]; then
    rm -f "$tmp"; say "refuse: $f: lines $lb->$la, bytes $sb->$sa (expected $want); left unchanged"; exit 1
  fi
  if [ "$4" = json ] && ! jq empty "$tmp" 2>/dev/null; then
    rm -f "$tmp"; say "refuse: $f: rewrite is not valid JSON; left unchanged"; exit 1
  fi
  touch -r "$f" "$tmp"
  bak=$(backup_of "$f")
  if [ -n "$bak" ] && [ "$pre" = "$(sha "$bak")" ]; then record_write "$f" "$tmp"; else changed_note "$f"; fi
  mv "$tmp" "$f"
  stop_after_mv "$f"
  say "run: rewrote $f ($pc path, $kc key replacements)"
}

# record_write FILE TMP: the hash of TMP, about to be moved onto FILE, recorded before the mv, so FILE
# always equals its backup or its recorded hash. A file that changed between its backup and the
# rewrite gets none, so undo.sh lists it as changed since the move instead of restoring over the change.
record_write() { printf '%s %s\n' "$(sha "$2")" "$1" >> "$WRITTEN"; }
stop_after_mv() { if [ "${STOP_AFTER_MV:-}" = "$1" ]; then say "stop: STOP_AFTER_MV=$1"; exit 99; fi; }
changed_note() { say "note: $1 changed after its step 2 backup; no write hash recorded, undo.sh will list it as changed"; }
# backup_of FILE: its step 2 backup, from the manifest "home" line naming it before or after the renames
backup_of() {
  local rel=${1#"$CLAUDE_HOME"/}
  awk -F'\t' -v r="$rel" -v b="$BACKUP/files/" '$1 == "home" && ($2 == r || $3 == r) { p = b $2 } END { if (p != "") print p }' "$MANIFEST"
}

# is_listed FILE: FILE is in the step 2 list (manifest "home" lines: path before and after the renames)
is_listed() {
  local rel=${1#"$CLAUDE_HOME"/}
  awk -F'\t' -v r="$rel" '$1 == "home" && ($2 == r || $3 == r) { f = 1 } END { exit !f }' "$MANIFEST"
}
# rewrite_listed FILE MODE FIELDS JSON [keyonly]: steps 6-8 rewrite only files step 2 backed up
rewrite_listed() {
  if is_listed "$1"; then rewrite_file "$@"; return 0; fi
  if has_hits "$2" "$3" "$1" "${5:-}"; then
    say "refuse: $1 names the old path but is not in step 2's backup list ($MANIFEST); left unchanged"; exit 1
  fi
}

clone_cp() { cp -pc "$1" "$2" 2>/dev/null || cp -p "$1" "$2"; }

# old_files DIR: sets OLDF to the *.jsonl files under DIR naming OLD_REPO (grep exit 1 = none).
# grep exit 2 (a path it could not read) prints a refusal naming the path and returns 1.
old_files() {
  local out rc line
  out=$(grep -rlF --include='*.jsonl' -- "$OLD_REPO" "$1" 2>&1) && rc=0 || rc=$?
  OLDF=
  case $rc in
    0) OLDF=$out ;;
    1) ;;
    *) while IFS= read -r line; do
         case "$line" in "grep: "*) say "refuse: cannot scan ${line#grep: }" ;; esac
       done <<< "$out"
       say "refuse: grep -r under $1 failed (exit $rc)"
       return 1 ;;
  esac
}

# interrupted between the mv and its manifest line: the checkout is already at NEW_REPO
moved_already() { [ -f "$MANIFEST" ] && ! is_done 3 && [ ! -e "$OLD_REPO" ] && [ -d "$NEW_REPO" ]; }
is_done() { [ -f "$MANIFEST" ] && grep -qx "done${TAB}$1" "$MANIFEST"; }
mark_done() {
  printf 'done\t%s\n' "$1" >> "$MANIFEST"
  if [ "${STOP_AFTER:-}" = "$1" ]; then say "stop: STOP_AFTER=$1"; exit 99; fi
}

# any failure once the checkout has moved: the script now lives under NEW_REPO
on_exit() {
  local rc=$?
  if [ "$EXECUTE" -eq 1 ] && [ "$rc" -ne 0 ] && [ "$rc" -ne 99 ] && { is_done 3 || moved_already; }; then
    say "resume: fix the cause above, then run: bash $NEW_REPO/scripts/move/2026-11-02-ikhbat-org.sh --execute"
  fi
}
trap on_exit EXIT

# old<TAB>new for every projects/ and lessons/ dir at the old key
scan_renames() {
  local base d name rest
  for base in projects lessons; do
    for d in "$CLAUDE_HOME/$base/$OKEY"*; do
      [ -d "$d" ] || continue
      name=${d##*/}; rest=${name#"$OKEY"}
      case "${rest:0:1}" in [A-Za-z0-9._]) continue ;; esac
      printf '%s\t%s\n' "$d" "$CLAUDE_HOME/$base/$NKEY$rest"
    done
  done
}
renames() {
  if [ -f "$MANIFEST" ]; then
    grep -E "^rename(-late)?${TAB}" "$MANIFEST" | cut -f2,3 || true
  else
    scan_renames
  fi
}
# only the renames step 2 recorded (adopt_dirs tags its own lines "rename-late")
step2_renames() { grep "^rename${TAB}" "$MANIFEST" | cut -f2,3 || true; }
# late_dirs: old<TAB>new for dirs at the old key missing from the manifest's renames (created after step 2)
late_dirs() {
  local listed o n
  [ -f "$MANIFEST" ] || return 0
  listed=$(renames | cut -f1)
  while IFS="$TAB" read -r o n; do
    [ -n "$o" ] || continue
    grep -qxF -- "$o" <<< "$listed" || printf '%s\t%s\n' "$o" "$n"
  done < <(scan_renames)
}
# post_rel REL RENAMES: REL under its renamed dir
post_rel() {
  local o n p=$CLAUDE_HOME/$1
  while IFS="$TAB" read -r o n; do
    [ -n "$o" ] || continue
    case "$p" in "$o"/*) p=$n${p#"$o"}; break ;; esac
  done <<< "$2"
  printf '%s\n' "${p#"$CLAUDE_HOME"/}"
}
# pre_rel REL: REL under its dir's name before the renames
pre_rel() {
  local o n p=$CLAUDE_HOME/$1
  while IFS="$TAB" read -r o n; do
    [ -n "$o" ] || continue
    case "$p" in "$n"/*) p=$o${p#"$n"}; break ;; esac
  done < <(renames)
  printf '%s\n' "${p#"$CLAUDE_HOME"/}"
}
# adopt_late FILE: a transcript with an old cwd written after step 2 is backed up now and added to the list
adopt_late() {
  local rel=${1#"$CLAUDE_HOME"/} pre
  pre=$(pre_rel "$rel")
  jsonl_ok "$1" || { say "refuse: $1 has a line that is not valid JSON; left unchanged"; exit 1; }
  mkdir -p "$BACKUP/files/$(dirname "$pre")"
  clone_cp "$1" "$BACKUP/files/$pre"
  printf 'home\t%s\t%s\n' "$pre" "$rel" >> "$MANIFEST"
  say "note: $1 has an old cwd but is not in step 2's list (written after it); backed up and added"
}

# ---- 1. preflight -------------------------------------------------------------------------
REF=0
refuse() { say "refuse: $*"; REF=$(( REF + 1 )); }
alive() { case "$1" in ''|*[!0-9]*) return 1 ;; esac; kill -0 "$1" 2>/dev/null; }

# Claude processes (pgrep exit 1 = none, other non-zero = unknown) and live sessions at the old path
proc_check() {
  local pat out rc line pid f
  for pat in "/claude/versions/" "ClaudeCode.app" "bin/$CLI_NAME"; do
    out=$($PGREP -fl "$pat" 2>/dev/null) && rc=0 || rc=$?
    case $rc in
      0) refuse "Claude process matches pgrep -fl $pat:"
         while IFS= read -r line; do say "  $line"; done <<< "$out" ;;
      1) ;;
      *) refuse "pgrep -fl $pat failed (exit $rc): cannot tell whether Claude runs" ;;
    esac
  done
  out=$($PGREP -x "$CLI_NAME" 2>/dev/null) && rc=0 || rc=$?
  case $rc in
    0) refuse "Claude process matches pgrep -x $CLI_NAME:"
       while IFS= read -r line; do
         pid=${line%% *}
         say "  $pid $(ps -o command= -p "$pid" 2>/dev/null || echo '(command line unavailable)')"
       done <<< "$out" ;;
    1) ;;
    *) refuse "pgrep -x $CLI_NAME failed (exit $rc): cannot tell whether Claude runs" ;;
  esac

  for f in "$CLAUDE_HOME"/sessions/*.json; do
    [ -f "$f" ] || continue
    if ! pid=$(jq -r '.pid // empty' "$f" 2>/dev/null); then refuse "cannot read or parse $f"; continue; fi
    [ -n "$pid" ] || continue
    if alive "$pid" && has_hits fields cwd "$f"; then
      refuse "live session $f (pid $pid) has its cwd at the old path"
    fi
  done
  f=$CLAUDE_HOME/daemon.lock
  if [ -f "$f" ]; then
    if ! pid=$(jq -r '.pid // empty' "$f" 2>/dev/null); then refuse "cannot read or parse $f"
    elif alive "$pid"; then refuse "daemon.lock: pid $pid is alive"; fi
  fi
}

# rerun before steps 6, 8 and 10: a Claude started during the run stops it (the EXIT trap prints the resume)
midrun_check() {
  [ "$EXECUTE" -eq 1 ] || return 0
  REF=0
  proc_check
  if [ "$REF" -gt 0 ]; then say "stop: a Claude process appeared before step $1; nothing more changed"; exit 1; fi
}

# workers (roster.json .workers, object or array) naming the old path or key: "pid<TAB>replPid"
JQ_ROSTER='
def nmp: (length == 0) or (.[0:1] | test("[A-Za-z0-9._-]") | not);
def nmk: (length == 0) or (.[0:1] | test("[A-Za-z0-9._]") | not);
def hit($o; nm): . as $s | any(($s | indices($o))[]; $s[(. + ($o | length)):] | nm);
(.workers // {}) | .[] | select(type == "object")
| select([.. | strings | (hit($old; nmp) or hit($okey; nmk))] | any)
| "\(.pid // "")\t\(.replPid // "")"'

preflight() {
  local pat out line f pid rpid st repo e parent base ldev rdev o n d nrn cj
  for pat in jq perl shasum; do command -v "$pat" >/dev/null 2>&1 || { say "refuse: $pat not found"; exit 1; }; done
  if [ -e "$BACKUP/undone" ]; then refuse "$BACKUP/undone exists: the move was undone; remove $BACKUP to run again"; fi

  proc_check

  parent=$(dirname "$OLD_REPO"); base=$(basename "$OLD_REPO")
  for e in "$parent/$base"?*; do
    if [ -e "$e" ] || [ -L "$e" ]; then refuse "sibling $e next to $OLD_REPO would share its Claude key"; fi
  done

  # roster: a live worker naming the old path refuses; ended ones are backed up and rewritten (step 8)
  f=$CLAUDE_HOME/daemon/roster.json
  if [ -f "$f" ]; then
    if ! out=$(jq -r --arg old "$OLD_REPO" --arg okey "$OKEY" "$JQ_ROSTER" "$f" 2>/dev/null); then
      refuse "cannot read or parse $f"
    else
      while IFS="$TAB" read -r pid rpid; do
        if alive "$pid" || alive "$rpid"; then refuse "daemon/roster.json: live worker (pid $pid $rpid) names the old path"; fi
      done <<< "$out"
    fi
  fi
  for f in "$CLAUDE_HOME"/jobs/*/state.json; do
    [ -f "$f" ] || continue
    if ! st=$(jq -r '.state // ""' "$f" 2>/dev/null); then refuse "cannot read or parse $f"; continue; fi
    if [ "$st" != done ] && has_hits all "" "$f"; then
      refuse "job $f (state '$st') names the old path"
    fi
  done

  # every JSON file the run rewrites parses (transcripts: step 2, before any change)
  for f in "$CLAUDE_HOME/workers/registry.json" "$CLAUDE_JSON"; do
    if [ -f "$f" ] && ! json_ok "$f"; then refuse "$f is not valid JSON"; fi
  done
  for f in "$CLAUDE_HOME/history.jsonl" "$CLAUDE_HOME/lessons/queue.jsonl"; do
    if [ -f "$f" ] && ! jsonl_ok "$f"; then refuse "$f has a line that is not valid JSON"; fi
  done
  cj=$CLAUDE_JSON
  if [ -f "$MANIFEST" ] && [ -f "$BACKUP/claude.json" ]; then cj=$BACKUP/claude.json; fi
  if [ -f "$cj" ]; then
    out=$(jq -r --arg new "$NEW_REPO" '(.projects // {}) | keys[] | select(. == $new or startswith($new + "/"))' "$cj" 2>/dev/null || true)
    if [ -n "$out" ]; then
      refuse "$CLAUDE_JSON already has projects keys at or under $NEW_REPO (step 7 would merge entries):"
      while IFS= read -r line; do say "  $line"; done <<< "$out"
    fi
  fi
  # every transcript dir at the old key can be scanned: an unreadable path would hide a file from steps 2 and 6
  while IFS="$TAB" read -r o n; do
    case "$o" in "$CLAUDE_HOME"/projects/*) ;; *) continue ;; esac
    d=$n; [ -e "$d" ] || d=$o
    old_files "$d" || REF=$(( REF + 1 ))
  done < <(renames; late_dirs)

  repo=$OLD_REPO
  if is_done 3 || moved_already; then
    repo=$NEW_REPO
    [ -d "$NEW_REPO" ] || refuse "step 3 is recorded but $NEW_REPO is missing"
    [ ! -e "$OLD_REPO" ] || refuse "step 3 is recorded but $OLD_REPO exists"
  else
    [ -d "$OLD_REPO" ] || refuse "$OLD_REPO is not a directory"
    [ ! -e "$NEW_REPO" ] || refuse "$NEW_REPO exists"
  fi
  if [ -f "$MANIFEST" ]; then
    while IFS="$TAB" read -r o n; do
      if is_done 5 && { [ ! -e "$n" ] || [ -e "$o" ]; }; then refuse "step 5 is recorded but $n is missing or $o exists"; fi
    done < <(step2_renames)
    # after step 5 a source still present with its target absent is a late dir's rename, done by adopt_dirs
    while IFS="$TAB" read -r o n; do
      if [ -e "$o" ] && [ -e "$n" ]; then refuse "both $o and $n exist"; fi
      if [ ! -e "$o" ] && [ ! -e "$n" ]; then refuse "neither $o nor $n exists"; fi
    done < <(renames)
    while IFS="$TAB" read -r o n; do
      if [ -e "$n" ]; then refuse "$o was created after step 2 and its target $n exists"; fi
    done < <(late_dirs)
  else
    while IFS="$TAB" read -r o n; do
      if [ -e "$n" ]; then refuse "target $n exists"; fi
    done < <(renames)
  fi

  if [ -d "$repo" ]; then
    if ! is_top "$repo"; then
      refuse "$repo is not the top of its own git checkout"
    else
      if ! out=$(git -C "$repo" status --porcelain 2>/dev/null); then refuse "cannot check $repo: $(git -C "$repo" status --porcelain 2>&1 >/dev/null)"
      elif [ -n "$out" ]; then refuse "$repo is not clean"; fi
      if ! out=$(git -C "$repo" worktree list --porcelain 2>&1); then refuse "cannot check $repo: $out"
      else
        nrn=$(printf '%s\n' "$out" | grep -c '^worktree ' || true)
        [ "$nrn" -le 1 ] || refuse "$repo has a linked worktree ($nrn worktrees)"
      fi
      ldev=$(git -C "$repo" rev-parse refs/heads/dev 2>/dev/null || true)
      rdev=$(git -C "$repo" ls-remote "$NEW_REMOTE" refs/heads/dev 2>/dev/null | cut -f1 || true)
      if [ -z "$ldev" ] || [ "$ldev" != "$rdev" ]; then
        if [ "$repo" = "$NEW_REPO" ]; then
          say "warn: git ls-remote $NEW_REMOTE dev ('$rdev') differs from local dev ('$ldev'); the checkout has moved, going on"
        else
          refuse "git ls-remote $NEW_REMOTE dev ('$rdev') differs from local dev ('$ldev')"
        fi
      fi
    fi
  fi

  if [ "$REF" -gt 0 ]; then say "preflight failed: $REF refusal(s), nothing changed"; exit 1; fi
  say "ok: preflight"
}

# ---- 2. backup and manifest -----------------------------------------------------------------
# The manifest's "home" lines are the list of every file steps 6-8 may rewrite.
step2() {
  local rel f o n tmp rn list= nfiles nren=0 origin
  if is_done 2; then say "skip: step 2 (backup) already done"; return 0; fi
  rn=$(scan_renames)
  for rel in workers/registry.json lessons/INDEX.md lessons/queue.jsonl history.jsonl; do
    if [ -f "$CLAUDE_HOME/$rel" ]; then list+=$rel$'\n'; fi
  done
  f=$CLAUDE_HOME/daemon/roster.json
  if [ -f "$f" ] && has_hits all "" "$f"; then list+=daemon/roster.json$'\n'; fi
  for f in "$CLAUDE_HOME"/jobs/*/state.json; do
    [ -f "$f" ] || continue
    if has_hits all "" "$f"; then list+=${f#"$CLAUDE_HOME"/}$'\n'; fi
  done
  while IFS="$TAB" read -r o n; do
    [ -n "$o" ] || continue
    nren=$(( nren + 1 ))
    case "$o" in "$CLAUDE_HOME"/projects/*) ;; *) continue ;; esac
    old_files "$o" || exit 1
    while IFS= read -r f; do
      [ -n "$f" ] || continue
      has_hits fields cwd "$f" || continue
      jsonl_ok "$f" || { say "refuse: $f has a line that is not valid JSON; nothing changed"; exit 1; }
      list+=${f#"$CLAUDE_HOME"/}$'\n'
    done <<< "$OLDF"
  done <<< "$rn"
  nfiles=$(printf '%s' "$list" | grep -c '' || true)
  if [ "$EXECUTE" -eq 0 ]; then
    say "would: back up $CLAUDE_JSON and $nfiles files under $CLAUDE_HOME to $BACKUP; record $nren renames"
    return 0
  fi
  need_top "$OLD_REPO"
  mkdir -p "$BACKUP/files"
  tmp=$MANIFEST.tmp
  : > "$tmp"
  while IFS= read -r rel; do
    [ -n "$rel" ] || continue
    mkdir -p "$BACKUP/files/$(dirname "$rel")"
    clone_cp "$CLAUDE_HOME/$rel" "$BACKUP/files/$rel"
    printf 'home\t%s\t%s\n' "$rel" "$(post_rel "$rel" "$rn")" >> "$tmp"
  done <<< "$list"
  if [ -f "$CLAUDE_JSON" ]; then
    clone_cp "$CLAUDE_JSON" "$BACKUP/claude.json"
    printf 'json\n' >> "$tmp"
  fi
  while IFS="$TAB" read -r o n; do
    if [ -n "$o" ]; then printf 'rename\t%s\t%s\n' "$o" "$n" >> "$tmp"; fi
  done <<< "$rn"
  origin=$(git -C "$OLD_REPO" remote get-url origin 2>/dev/null || true)
  if [ -n "$origin" ]; then printf 'origin\t%s\n' "$origin" >> "$tmp"; fi
  gen_undo
  printf 'done\t2\n' >> "$tmp"
  mv "$tmp" "$MANIFEST"
  say "run: backed up $nfiles files and $CLAUDE_JSON to $BACKUP; manifest has $nren renames"
  if [ "${STOP_AFTER:-}" = 2 ]; then say "stop: STOP_AFTER=2"; exit 99; fi
}

# A resumed run: a projects/ or lessons/ dir at the old key created after step 2 joins the manifest's
# renames (so undo.sh reverses it); after step 5 it is renamed here, after step 6 its transcripts with
# an old cwd are adopted (backed up, listed) and rewritten here, as step 6 does for late transcripts.
adopt_dirs() {
  local o n f
  [ -f "$MANIFEST" ] || return 0
  while IFS="$TAB" read -r o n; do
    if [ "$EXECUTE" -eq 0 ]; then say "would: add $o (created after step 2) to the renames"; continue; fi
    printf 'rename-late\t%s\t%s\n' "$o" "$n" >> "$MANIFEST"
    say "note: $o is at the old key but not in step 2's list (created after it); added to the renames"
  done < <(late_dirs)
  if [ "$EXECUTE" -eq 0 ] || ! is_done 5; then return 0; fi
  while IFS="$TAB" read -r o n; do
    if [ -e "$o" ] && [ ! -e "$n" ]; then mv "$o" "$n"; say "run: mv $o $n"; fi
  done < <(renames)
  is_done 6 || return 0
  while IFS="$TAB" read -r o n; do
    case "$o" in "$CLAUDE_HOME"/projects/*) ;; *) continue ;; esac
    old_files "$n" || exit 1
    while IFS= read -r f; do
      [ -n "$f" ] || continue
      has_hits fields cwd "$f" || continue
      is_listed "$f" || adopt_late "$f"
      rewrite_file "$f" fields cwd ""
    done <<< "$OLDF"
  done < <(renames)
  # step 6 already rewrote INDEX.md; a late lessons dir's key in it is rewritten here the same way (read
  # from the manifest, so an interrupted resume still does it on the next one; no hits writes nothing)
  # literal prefix match: $CLAUDE_HOME may hold regex characters ([ * . backslash); ENVIRON, as awk -v unescapes backslashes
  if P="$CLAUDE_HOME/lessons/" awk -F"$TAB" '$1 == "rename-late" && index($2, ENVIRON["P"]) == 1 { f = 1 } END { exit !f }' "$MANIFEST" \
    && [ -f "$CLAUDE_HOME/lessons/INDEX.md" ]; then
    rewrite_listed "$CLAUDE_HOME/lessons/INDEX.md" text "" "" keyonly
  fi
}

# ---- 3. move the checkout -------------------------------------------------------------------
step3() {
  if is_done 3; then say "skip: step 3 (move checkout) already done"; return 0; fi
  if moved_already; then
    say "skip: $OLD_REPO is already at $NEW_REPO"
    if [ "$EXECUTE" -eq 1 ]; then mark_done 3; fi
    return 0
  fi
  if [ "$EXECUTE" -eq 0 ]; then say "would: mv $OLD_REPO $NEW_REPO"; return 0; fi
  mkdir -p "$(dirname "$NEW_REPO")"
  mv "$OLD_REPO" "$NEW_REPO"
  say "run: mv $OLD_REPO $NEW_REPO"
  mark_done 3
}

# ---- 4. remote ------------------------------------------------------------------------------
step4() {
  local cur
  if is_done 4; then say "skip: step 4 (remote) already done"; return 0; fi
  if [ "$EXECUTE" -eq 0 ]; then say "would: git -C $NEW_REPO remote set-url origin $NEW_REMOTE; fetch -q origin"; return 0; fi
  need_top "$NEW_REPO"
  cur=$(git -C "$NEW_REPO" remote get-url origin 2>/dev/null || true)
  if [ "$cur" = "$NEW_REMOTE" ]; then say "skip: origin is already $NEW_REMOTE"
  else g "$NEW_REPO" remote set-url origin "$NEW_REMOTE"; say "run: origin -> $NEW_REMOTE"; fi
  if g "$NEW_REPO" fetch -q origin; then say "run: fetched origin"
  else say "warn: git -C $NEW_REPO fetch -q origin failed; going on, fetch by hand later"; fi
  mark_done 4
}

# ---- 5. rename project and lesson dirs --------------------------------------------------------
step5() {
  local o n cnt=0
  if is_done 5; then say "skip: step 5 (rename dirs) already done"; return 0; fi
  while IFS="$TAB" read -r o n; do
    cnt=$(( cnt + 1 ))
    if [ "$EXECUTE" -eq 0 ]; then say "would: mv $o $n"; continue; fi
    if [ -e "$n" ] && [ ! -e "$o" ]; then say "skip: $n already renamed"
    elif [ -e "$o" ] && [ ! -e "$n" ]; then mv "$o" "$n"; say "run: mv $o $n"
    else say "refuse: step 5: $o / $n in an unexpected state"; exit 1; fi
  done < <(renames)
  if [ "$EXECUTE" -eq 0 ]; then say "would: rename $cnt dirs"; return 0; fi
  mark_done 5
}

# ---- 6. transcripts and lessons index ---------------------------------------------------------
step6() {
  local o n d f cnt=0 other=0
  if is_done 6; then say "skip: step 6 (transcripts, INDEX.md) already done"; return 0; fi
  while IFS="$TAB" read -r o n; do
    case "$o" in "$CLAUDE_HOME"/projects/*) ;; *) continue ;; esac
    d=$n; [ -e "$d" ] || d=$o
    old_files "$d" || exit 1
    while IFS= read -r f; do
      [ -n "$f" ] || continue
      if [ "$EXECUTE" -eq 1 ]; then
        if ! is_listed "$f" && has_hits fields cwd "$f"; then adopt_late "$f"; fi
        rewrite_listed "$f" fields cwd ""
      elif has_hits fields cwd "$f"; then cnt=$(( cnt + 1 ))
      else other=$(( other + 1 )); fi
    done <<< "$OLDF"
  done < <(renames)
  if [ "$EXECUTE" -eq 0 ]; then
    say "would: rewrite \"cwd\" in $cnt transcript files, key in lessons/INDEX.md"
    say "info: $other other transcript files name the old path outside \"cwd\" only; not rewritten"
    return 0
  fi
  if [ -f "$CLAUDE_HOME/lessons/INDEX.md" ]; then rewrite_listed "$CLAUDE_HOME/lessons/INDEX.md" text "" "" keyonly; fi
  mark_done 6
}

# ---- 7. ~/.claude.json ------------------------------------------------------------------------
# githubRepoPaths: the old entry's elements under the path rule, merged after any existing new entry, NEW_REPO once
JQ_CLAUDE_JSON='
def nm: (length == 0) or (.[0:1] | test("[A-Za-z0-9._-]") | not);
def mv: if type == "string" and startswith($old) and (.[($old | length):] | nm) then $new + .[($old | length):] else . end;
(if (.projects | type) == "object" then .projects |= with_entries(.key |= mv) else . end)
| (if (.githubRepoPaths | type) == "object" then .githubRepoPaths |= (
    if has("extrode/bayan") then
      ((.["ikhbat-org/bayan"] // []) + ((.["extrode/bayan"] // []) | map(mv)) + [$new]
        | reduce .[] as $x ([]; if any(.[]; . == $x) then . else . + [$x] end)) as $merged
      | del(.["extrode/bayan"]) | .["ikhbat-org/bayan"] = $merged
    else . end)
  else . end)'
step7() {
  local tmp pre flag=
  if is_done 7; then say "skip: step 7 (.claude.json) already done"; return 0; fi
  if [ ! -f "$CLAUDE_JSON" ]; then say "skip: $CLAUDE_JSON does not exist"; return 0; fi
  if [ "$EXECUTE" -eq 0 ]; then say "would: move projects keys and githubRepoPaths in $CLAUDE_JSON"; return 0; fi
  tmp=$CLAUDE_JSON.mvtmp.$$
  pre=$(sha "$CLAUDE_JSON")
  cp -p "$CLAUDE_JSON" "$tmp"
  if [ "$(wc -l < "$CLAUDE_JSON")" -le 1 ]; then flag=-c; fi
  if ! jq $flag --arg old "$OLD_REPO" --arg new "$NEW_REPO" "$JQ_CLAUDE_JSON" "$CLAUDE_JSON" > "$tmp" \
     || ! jq empty "$tmp" 2>/dev/null \
     || [ "$(jq 'keys | length' "$CLAUDE_JSON")" != "$(jq 'keys | length' "$tmp")" ] \
     || [ "$(jq '(.projects // {}) | length' "$CLAUDE_JSON")" != "$(jq '(.projects // {}) | length' "$tmp")" ]; then
    rm -f "$tmp"; say "refuse: step 7: jq output invalid or key counts changed; $CLAUDE_JSON unchanged"; exit 1
  fi
  if cmp -s "$tmp" "$CLAUDE_JSON"; then rm -f "$tmp"; say "skip: $CLAUDE_JSON already holds the new keys"
  else
    if [ -f "$BACKUP/claude.json" ] && [ "$pre" = "$(sha "$BACKUP/claude.json")" ]; then record_write "$CLAUDE_JSON" "$tmp"
    else changed_note "$CLAUDE_JSON"; fi
    mv "$tmp" "$CLAUDE_JSON"
    stop_after_mv "$CLAUDE_JSON"
    say "run: rewrote $CLAUDE_JSON"
  fi
  mark_done 7
}

# ---- 8. registry, history, queue, roster, job state ---------------------------------------------
step8() {
  local f
  if is_done 8; then say "skip: step 8 (registry, history, queue, roster, jobs) already done"; return 0; fi
  if [ "$EXECUTE" -eq 0 ]; then
    say "would: rewrite workers/registry.json, history.jsonl (project), lessons/queue.jsonl, daemon/roster.json, jobs/*/state.json"
    return 0
  fi
  f=$CLAUDE_HOME/workers/registry.json
  if [ -f "$f" ]; then rewrite_listed "$f" fields 'cwd|repo|worktree|criteria' json; fi
  f=$CLAUDE_HOME/history.jsonl
  if [ -f "$f" ]; then rewrite_listed "$f" fields project json; fi
  f=$CLAUDE_HOME/lessons/queue.jsonl
  if [ -f "$f" ]; then rewrite_listed "$f" all "" json; fi
  f=$CLAUDE_HOME/daemon/roster.json
  if [ -f "$f" ]; then rewrite_listed "$f" all "" json; fi
  for f in "$CLAUDE_HOME"/jobs/*/state.json; do
    [ -f "$f" ] || continue
    rewrite_listed "$f" all "" json
  done
  mark_done 8
}

# ---- 9. undo.sh -------------------------------------------------------------------------------
gen_undo() {
  local u=$BACKUP/undo.sh
  {
    echo '#!/usr/bin/env bash'
    echo '# Reverses steps 3-8 of 2026-11-02-ikhbat-org.sh from the manifest and backups. Idempotent. Close Claude first.'
    echo '# Refuses, changing nothing, when a Claude process or a live session runs, when a rename source and target'
    echo '# both exist, when a git dir is not the top of its own checkout, or (--discard-changes-since-move overrides'
    echo '# these two) when the moved checkout has a linked worktree or a file it would restore changed after the move.'
    echo 'set -euo pipefail'
    printf 'OLD_REPO=%q\nNEW_REPO=%q\nCLAUDE_HOME=%q\nCLAUDE_JSON=%q\nBACKUP=%q\n' \
      "$OLD_REPO" "$NEW_REPO" "$CLAUDE_HOME" "$CLAUDE_JSON" "$BACKUP"
    cat <<'EOF'
DISCARD=0
case "${1:-}" in
  "") ;;
  --discard-changes-since-move) DISCARD=1 ;;
  *) echo "usage: $0 [--discard-changes-since-move]" >&2; exit 2 ;;
esac
export GIT_CEILING_DIRECTORIES="$(dirname "$OLD_REPO"):$(dirname "$NEW_REPO")"
PGREP=${PGREP:-pgrep}
CLI_NAME=claude
TAB=$(printf '\t')
MANIFEST=$BACKUP/manifest.txt
WRITTEN=$BACKUP/written.sha256
[ -f "$MANIFEST" ] || { echo "refuse: no manifest at $MANIFEST"; exit 1; }
is_top() {
  local p t
  p=$(cd -P "$1" 2>/dev/null && pwd -P) || return 1
  t=$(git -C "$1" rev-parse --show-toplevel 2>/dev/null) || return 1
  [ "$t" = "$p" ]
}
sha() { shasum -a 256 < "$1" | cut -d' ' -f1; }
alive() { case "$1" in ''|*[!0-9]*) return 1 ;; esac; kill -0 "$1" 2>/dev/null; }
bad=0
# the move script's process checks: pgrep exit 1 = none, any other non-zero refuses
for pat in "/claude/versions/" "ClaudeCode.app" "bin/$CLI_NAME"; do
  out=$($PGREP -fl "$pat" 2>/dev/null) && rc=0 || rc=$?
  case $rc in
    0) echo "refuse: Claude process matches pgrep -fl $pat:"; bad=1
       while IFS= read -r line; do echo "  $line"; done <<< "$out" ;;
    1) ;;
    *) echo "refuse: pgrep -fl $pat failed (exit $rc): cannot tell whether Claude runs"; bad=1 ;;
  esac
done
out=$($PGREP -x "$CLI_NAME" 2>/dev/null) && rc=0 || rc=$?
case $rc in
  0) echo "refuse: Claude process matches pgrep -x $CLI_NAME:"; bad=1
     while IFS= read -r line; do
       pid=${line%% *}
       echo "  $pid $(ps -o command= -p "$pid" 2>/dev/null || echo '(command line unavailable)')"
     done <<< "$out" ;;
  1) ;;
  *) echo "refuse: pgrep -x $CLI_NAME failed (exit $rc): cannot tell whether Claude runs"; bad=1 ;;
esac
# live sessions with their cwd at either path; a live daemon
for f in "$CLAUDE_HOME"/sessions/*.json; do
  [ -f "$f" ] || continue
  if ! pid=$(jq -r '.pid // empty' "$f" 2>/dev/null); then echo "refuse: cannot read or parse $f"; bad=1; continue; fi
  if alive "$pid" && jq -e --arg o "$OLD_REPO" --arg n "$NEW_REPO" '
      def at($p): (.cwd // "") as $c | ($c | startswith($p)) and ($c[($p | length):] | (length == 0) or (.[0:1] | test("[A-Za-z0-9._-]") | not));
      at($o) or at($n)' "$f" >/dev/null 2>&1; then
    echo "refuse: live session $f (pid $pid) has its cwd at the old or new path"; bad=1
  fi
done
f=$CLAUDE_HOME/daemon.lock
if [ -f "$f" ]; then
  if ! pid=$(jq -r '.pid // empty' "$f" 2>/dev/null); then echo "refuse: cannot read or parse $f"; bad=1
  elif alive "$pid"; then echo "refuse: daemon.lock: pid $pid is alive"; bad=1; fi
fi
while IFS="$TAB" read -r kind a b; do
  if { [ "$kind" = rename ] || [ "$kind" = rename-late ]; } && [ -e "$a" ] && [ -e "$b" ]; then echo "refuse: both $b and $a exist"; bad=1; fi
done < "$MANIFEST"
if [ -e "$NEW_REPO" ] && [ -e "$OLD_REPO" ]; then echo "refuse: both $NEW_REPO and $OLD_REPO exist"; bad=1; fi
if grep -q "^origin$TAB" "$MANIFEST"; then
  repo=$OLD_REPO
  if [ -e "$NEW_REPO" ]; then repo=$NEW_REPO; fi
  if [ -e "$repo" ] && ! is_top "$repo"; then echo "refuse: $repo is not the top of its own git checkout"; bad=1; fi
fi
# linked worktrees added after the move hold the new path; moving the checkout back breaks them
wts=
if [ -d "$NEW_REPO" ] && [ ! -e "$OLD_REPO" ] && is_top "$NEW_REPO"; then
  wts=$(git -C "$NEW_REPO" worktree list --porcelain | grep '^worktree ' | tail -n +2 | cut -c10- || true)
  if [ -n "$wts" ] && [ "$DISCARD" -eq 0 ]; then
    echo "refuse: $NEW_REPO has linked worktrees; remove them (git worktree remove) or pass --discard-changes-since-move:"
    while IFS= read -r line; do echo "  $line"; done <<< "$wts"
    bad=1
  fi
fi
if [ "$bad" -ne 0 ]; then echo "refuse: nothing changed"; exit 1; fi
# each file to restore: unchanged since the move when its hash is its last recorded write (restore it) or
# its backup's (nothing to restore); anything else changed after the move
changed=
skip=$'\n'
while IFS="$TAB" read -r kind a b; do
  case "$kind" in
    home) cur=$CLAUDE_HOME/$b; [ -e "$cur" ] || cur=$CLAUDE_HOME/$a; bak=$BACKUP/files/$a ;;
    json) cur=$CLAUDE_JSON; bak=$BACKUP/claude.json ;;
    *) continue ;;
  esac
  s=$(sha "$cur" 2>/dev/null || echo missing)
  w=$(awk -v p="$cur" 'substr($0, index($0, " ") + 1) == p { h = $1 } END { print h }' "$WRITTEN" 2>/dev/null || true)
  if [ "$s" = "$(sha "$bak")" ]; then skip=$skip$kind$TAB$a$'\n'
  elif [ -z "$w" ] || [ "$s" != "$w" ]; then changed=$changed"  $cur"$'\n'; fi
done < "$MANIFEST"
if [ -n "$changed" ] && [ "$DISCARD" -eq 0 ]; then
  echo "refuse: these files changed after the move; restoring the backups would discard those changes:"
  printf '%s' "$changed"
  echo "refuse: nothing restored. Run with --discard-changes-since-move to restore the backups anyway."
  exit 1
fi
while IFS="$TAB" read -r kind a b; do
  if { [ "$kind" = rename ] || [ "$kind" = rename-late ]; } && [ -e "$b" ] && [ ! -e "$a" ]; then mv "$b" "$a"; echo "run: mv $b $a"; fi
done < "$MANIFEST"
while IFS="$TAB" read -r kind a b; do
  case "$skip" in *$'\n'"$kind$TAB$a"$'\n'*) continue ;; esac
  case "$kind" in
    home) mkdir -p "$(dirname "$CLAUDE_HOME/$a")"; cp -p "$BACKUP/files/$a" "$CLAUDE_HOME/$a"; echo "run: restored $a" ;;
    json) cp -p "$BACKUP/claude.json" "$CLAUDE_JSON"; echo "run: restored $CLAUDE_JSON" ;;
  esac
done < "$MANIFEST"
if [ -d "$NEW_REPO" ] && [ ! -e "$OLD_REPO" ]; then
  mkdir -p "$(dirname "$OLD_REPO")"; mv "$NEW_REPO" "$OLD_REPO"; echo "run: mv $NEW_REPO $OLD_REPO"
  if [ -n "$wts" ]; then
    is_top "$OLD_REPO" || { echo "refuse: $OLD_REPO is not the top of its own git checkout"; exit 1; }
    if out=$(git -C "$OLD_REPO" worktree repair 2>&1); then echo "run: git worktree repair: ok${out:+ ($out)}"
    else echo "warn: git -C $OLD_REPO worktree repair failed: $out"; fi
  fi
fi
while IFS="$TAB" read -r kind a b; do
  if [ "$kind" = origin ] && [ -d "$OLD_REPO" ]; then
    is_top "$OLD_REPO" || { echo "refuse: $OLD_REPO is not the top of its own git checkout"; exit 1; }
    git -C "$OLD_REPO" remote set-url origin "$a"; echo "run: origin -> $a"
  fi
done < "$MANIFEST"
touch "$BACKUP/undone"
echo "ok: undone"
EOF
  } > "$u"
  chmod +x "$u"
}
# undo.sh is written in step 2 already and reads the manifest when it runs; step 9 writes the same text
# again (a no-op unless the file was removed or edited)
step9() {
  if is_done 9; then say "skip: step 9 (undo.sh) already done"; return 0; fi
  if [ "$EXECUTE" -eq 0 ]; then say "would: write $BACKUP/undo.sh"; return 0; fi
  gen_undo
  say "run: wrote $BACKUP/undo.sh"
  mark_done 9
}

# ---- 10. verify -------------------------------------------------------------------------------
step10() {
  local o n f bad=0 left=0 want=0 got=0 c
  if [ "$EXECUTE" -eq 0 ]; then say "would: verify dirs, cwd lines, .claude.json keys, git status, remote"; return 0; fi
  left=$(scan_renames | wc -l | tr -d ' ')
  [ "$left" -eq 0 ] || { say "FAIL: $left dirs still at the old key"; bad=1; }
  while IFS="$TAB" read -r o n; do
    want=$(( want + 1 )); [ -d "$n" ] && got=$(( got + 1 ))
    case "$n" in "$CLAUDE_HOME"/projects/*)
      old_files "$n" || exit 1
      while IFS= read -r f; do
        [ -n "$f" ] || continue
        c=$(rw_run fields cwd "$f"); c=${c% *}
        [ "$c" -eq 0 ] || { say "FAIL: $f has $c old-path cwd values"; bad=1; }
      done <<< "$OLDF" ;;
    esac
  done < <(renames)
  [ "$want" -eq "$got" ] || { say "FAIL: $got of $want renamed dirs exist"; bad=1; }
  if [ -f "$CLAUDE_JSON" ]; then
    jq -e --arg old "$OLD_REPO" '
      def nm: (length == 0) or (.[0:1] | test("[A-Za-z0-9._-]") | not);
      [(.projects // {}) | keys[] | select(startswith($old) and (.[($old | length):] | nm))] | length == 0' \
      "$CLAUDE_JSON" >/dev/null || { say "FAIL: $CLAUDE_JSON still has a projects key at the old path"; bad=1; }
    if [ -f "$BACKUP/claude.json" ] && jq -e --arg old "$OLD_REPO" '(.projects // {}) | has($old)' "$BACKUP/claude.json" >/dev/null; then
      jq -e --arg new "$NEW_REPO" '.projects | has($new)' "$CLAUDE_JSON" >/dev/null \
        || { say "FAIL: projects key $NEW_REPO missing"; bad=1; }
    fi
    jq -e --arg new "$NEW_REPO" '(.githubRepoPaths // {}) as $g | ($g | has("extrode/bayan") | not) and (($g | has("ikhbat-org/bayan") | not) or any($g["ikhbat-org/bayan"][]; . == $new))' \
      "$CLAUDE_JSON" >/dev/null || { say "FAIL: githubRepoPaths not rewritten"; bad=1; }
    say "info: projects[$NEW_REPO] = $(jq -c --arg new "$NEW_REPO" '.projects[$new]' "$CLAUDE_JSON")"
    say "info: githubRepoPaths[ikhbat-org/bayan] = $(jq -c '.githubRepoPaths["ikhbat-org/bayan"]' "$CLAUDE_JSON")"
  fi
  [ ! -e "$OLD_REPO" ] || { say "FAIL: $OLD_REPO still exists"; bad=1; }
  g "$NEW_REPO" status -sb
  g "$NEW_REPO" remote -v
  if [ "$bad" -ne 0 ]; then exit 1; fi
  say "ok: verified"
}

# ---- main -------------------------------------------------------------------------------------
if [ "$EXECUTE" -eq 1 ]; then say "mode: execute"; else say "mode: dry run (use --execute to act)"; fi
preflight
step2
adopt_dirs
step3
step4
step5
midrun_check 6
step6
step7
midrun_check 8
step8
step9
midrun_check 10
step10
