#!/usr/bin/env bash
# Move Bayān to the Ikhbat Foundation: repo checkout, git remote, Claude state.
# Plan: docs/plans/2026-11-02-003-move-to-ikhbat-org.md (step 2). Dry run by default; --execute acts.
# Test hook: STOP_AFTER=<step> exits 99 once that step is recorded as done.
set -euo pipefail
export LC_ALL=C GIT_OPTIONAL_LOCKS=0

OLD_REPO=${OLD_REPO:-/Users/sy/Developer/extrode.com/bayan}
NEW_REPO=${NEW_REPO:-/Users/sy/Developer/ikhbat.org/bayan}
CLAUDE_HOME=${CLAUDE_HOME:-$HOME/.claude}
CLAUDE_JSON=${CLAUDE_JSON:-$HOME/.claude.json}
NEW_REMOTE=${NEW_REMOTE:-git@github.com:ikhbat-org/bayan.git}
PGREP=${PGREP:-pgrep}
CLI_NAME=claude

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

say() { printf '%s\n' "$*"; }

# Fixed-string rewrite. mode: fields (values of the named JSON fields), all (every JSON string
# value), text (raw text). Paths end at a char outside [A-Za-z0-9._-], keys outside [A-Za-z0-9._].
# Writes the output file only when something changed; prints "<path hits> <key hits>".
read -r -d '' PERL_RW <<'EOF' || true
use strict; use warnings;
my ($mode, $in, $out) = @ARGV;
my $fields = $ENV{RW_FIELDS} // '';
my ($po, $pn, $ko, $kn) = map { $ENV{$_} // '' } qw(RW_OLD RW_NEW RW_OLDK RW_NEWK);
my ($pc, $kc) = (0, 0);
sub rw1 {
  my ($s, $o, $n, $cls, $cnt) = @_;
  return $s if $o eq '';
  my ($r, $p) = ('', 0);
  while ((my $i = index($s, $o, $p)) >= 0) {
    my $e = $i + length($o);
    my $c = substr($s, $e, 1);
    if ($c eq '' || $c !~ $cls) { $r .= substr($s, $p, $i - $p) . $n; $$cnt++; }
    else { $r .= substr($s, $p, $e - $p); }
    $p = $e;
  }
  return $r . substr($s, $p);
}
sub rw {
  my $s = shift;
  $s = rw1($s, $po, $pn, qr/[A-Za-z0-9._-]/, \$pc);
  return rw1($s, $ko, $kn, qr/[A-Za-z0-9._]/, \$kc);
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

# hits MODE FIELDS FILE: total replacements it would make
hits() {
  local c
  c=$(rw_run "$1" "$2" "$3") || { say "refuse: cannot scan $3"; exit 1; }
  echo $(( ${c% *} + ${c#* } ))
}

# rewrite_file FILE MODE FIELDS JSON(json|"") [keyonly]
rewrite_file() {
  local f=$1 tmp counts pc kc lb la sb sa want
  tmp=$f.mvtmp.$$
  rm -f "$tmp"
  counts=$(rw_run "$2" "$3" "$f" "$tmp" "${5:-}") || { rm -f "$tmp"; say "refuse: rewrite of $f failed"; exit 1; }
  pc=${counts% *}; kc=${counts#* }
  if [ ! -e "$tmp" ]; then say "skip: $f (nothing to rewrite)"; return 0; fi
  lb=$(( $(wc -l < "$f") )); la=$(( $(wc -l < "$tmp") ))
  sb=$(( $(wc -c < "$f") )); sa=$(( $(wc -c < "$tmp") ))
  want=$(( sb + pc * DP + kc * DK ))
  if [ "$lb" -ne "$la" ] || [ "$sa" -ne "$want" ]; then
    rm -f "$tmp"; say "refuse: $f: lines $lb->$la, bytes $sb->$sa (expected $want); left unchanged"; exit 1
  fi
  if [ "$4" = json ] && ! jq empty "$tmp" 2>/dev/null; then
    rm -f "$tmp"; say "refuse: $f: rewrite is not valid JSON; left unchanged"; exit 1
  fi
  mv "$tmp" "$f"
  say "run: rewrote $f ($pc path, $kc key replacements)"
}

clone_cp() { cp -pc "$1" "$2" 2>/dev/null || cp -p "$1" "$2"; }

# interrupted between the mv and its manifest line: the checkout is already at NEW_REPO
moved_already() { [ -f "$MANIFEST" ] && ! is_done 3 && [ ! -e "$OLD_REPO" ] && [ -d "$NEW_REPO" ]; }
is_done() { [ -f "$MANIFEST" ] && grep -qx "done${TAB}$1" "$MANIFEST"; }
mark_done() {
  printf 'done\t%s\n' "$1" >> "$MANIFEST"
  if [ "${STOP_AFTER:-}" = "$1" ]; then say "stop: STOP_AFTER=$1"; exit 99; fi
}

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
    grep "^rename${TAB}" "$MANIFEST" | cut -f2,3 || true
  else
    scan_renames
  fi
}

# ---- 1. preflight -------------------------------------------------------------------------
REF=0
refuse() { say "refuse: $*"; REF=$(( REF + 1 )); }
alive() { case "$1" in ''|*[!0-9]*) return 1 ;; esac; kill -0 "$1" 2>/dev/null; }

preflight() {
  local pat out line f pid st repo e parent base ldev rdev o n nrn
  for pat in jq perl; do command -v "$pat" >/dev/null 2>&1 || { say "refuse: $pat not found"; exit 1; }; done
  if [ -e "$BACKUP/undone" ]; then refuse "$BACKUP/undone exists: the move was undone; remove $BACKUP to run again"; fi

  for pat in "/claude/versions/" "ClaudeCode.app" "bin/$CLI_NAME"; do
    out=$($PGREP -fl "$pat" 2>/dev/null || true)
    if [ -n "$out" ]; then
      refuse "Claude process matches pgrep -fl $pat:"
      while IFS= read -r line; do say "  $line"; done <<< "$out"
    fi
  done
  out=$($PGREP -x "$CLI_NAME" 2>/dev/null || true)
  if [ -n "$out" ]; then
    refuse "Claude process matches pgrep -x $CLI_NAME:"
    while IFS= read -r line; do
      pid=${line%% *}
      say "  $pid $(ps -o command= -p "$pid" 2>/dev/null || echo '(command line unavailable)')"
    done <<< "$out"
  fi

  for f in "$CLAUDE_HOME"/sessions/*.json; do
    [ -f "$f" ] || continue
    pid=$(jq -r '.pid // empty' "$f" 2>/dev/null || true)
    [ -n "$pid" ] || continue
    if alive "$pid" && [ "$(hits fields cwd "$f")" -gt 0 ]; then
      refuse "live session $f (pid $pid) has its cwd at the old path"
    fi
  done
  if [ -f "$CLAUDE_HOME/daemon.lock" ]; then
    pid=$(jq -r '.pid // empty' "$CLAUDE_HOME/daemon.lock" 2>/dev/null || true)
    if alive "$pid"; then refuse "daemon.lock: pid $pid is alive"; fi
  fi

  parent=$(dirname "$OLD_REPO"); base=$(basename "$OLD_REPO")
  for e in "$parent/$base"?*; do
    if [ -e "$e" ] || [ -L "$e" ]; then refuse "sibling $e next to $OLD_REPO would share its Claude key"; fi
  done

  if [ -f "$CLAUDE_HOME/daemon/roster.json" ] && [ "$(hits all "" "$CLAUDE_HOME/daemon/roster.json")" -gt 0 ]; then
    refuse "daemon/roster.json names the old path"
  fi
  for f in "$CLAUDE_HOME"/jobs/*/state.json; do
    [ -f "$f" ] || continue
    st=$(jq -r '.state // ""' "$f" 2>/dev/null || true)
    if [ "$st" != done ] && [ "$(hits all "" "$f")" -gt 0 ]; then
      refuse "job $f (state '$st') names the old path"
    fi
  done

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
      if is_done 5; then
        { [ -e "$n" ] && [ ! -e "$o" ]; } || refuse "step 5 is recorded but $n is missing or $o exists"
      else
        if [ -e "$o" ] && [ -e "$n" ]; then refuse "both $o and $n exist"; fi
        if [ ! -e "$o" ] && [ ! -e "$n" ]; then refuse "neither $o nor $n exists"; fi
      fi
    done < <(renames)
  else
    while IFS="$TAB" read -r o n; do
      if [ -e "$n" ]; then refuse "target $n exists"; fi
    done < <(renames)
  fi

  if [ -d "$repo" ]; then
    [ -z "$(git -C "$repo" status --porcelain 2>/dev/null)" ] || refuse "$repo is not clean"
    nrn=$(git -C "$repo" worktree list --porcelain 2>/dev/null | grep -c '^worktree ' || true)
    [ "$nrn" -le 1 ] || refuse "$repo has a linked worktree ($nrn worktrees)"
    ldev=$(git -C "$repo" rev-parse refs/heads/dev 2>/dev/null || true)
    rdev=$(git ls-remote "$NEW_REMOTE" refs/heads/dev 2>/dev/null | cut -f1 || true)
    if [ -z "$ldev" ] || [ "$ldev" != "$rdev" ]; then
      refuse "git ls-remote $NEW_REMOTE dev ('$rdev') differs from local dev ('$ldev')"
    fi
  fi

  if [ "$REF" -gt 0 ]; then say "preflight failed: $REF refusal(s), nothing changed"; exit 1; fi
  say "ok: preflight"
}

# ---- 2. backup and manifest -----------------------------------------------------------------
step2() {
  local rel f o n tmp nfiles=0 nren=0 origin
  if is_done 2; then say "skip: step 2 (backup) already done"; return 0; fi
  local list; list=$(mktemp "${TMPDIR:-/tmp}/bayan-move.XXXXXX")
  for rel in workers/registry.json lessons/INDEX.md lessons/queue.jsonl history.jsonl; do
    [ -f "$CLAUDE_HOME/$rel" ] && echo "$rel" >> "$list"
  done
  for f in "$CLAUDE_HOME"/jobs/*/state.json; do
    [ -f "$f" ] || continue
    if [ "$(hits all "" "$f")" -gt 0 ]; then echo "${f#"$CLAUDE_HOME"/}" >> "$list"; fi
  done
  while IFS="$TAB" read -r o n; do
    nren=$(( nren + 1 ))
    case "$o" in "$CLAUDE_HOME"/projects/*)
      grep -rlF --include='*.jsonl' -- "$OLD_REPO" "$o" 2>/dev/null | while IFS= read -r f; do echo "${f#"$CLAUDE_HOME"/}"; done >> "$list" || true ;;
    esac
  done < <(scan_renames)
  nfiles=$(wc -l < "$list" | tr -d ' ')
  if [ "$EXECUTE" -eq 0 ]; then
    say "would: back up $CLAUDE_JSON and $nfiles files under $CLAUDE_HOME to $BACKUP; record $nren renames"
    rm -f "$list"; return 0
  fi
  mkdir -p "$BACKUP/files"
  tmp=$MANIFEST.tmp
  : > "$tmp"
  while IFS= read -r rel; do
    mkdir -p "$BACKUP/files/$(dirname "$rel")"
    clone_cp "$CLAUDE_HOME/$rel" "$BACKUP/files/$rel"
    printf 'home\t%s\n' "$rel" >> "$tmp"
  done < "$list"
  if [ -f "$CLAUDE_JSON" ]; then
    clone_cp "$CLAUDE_JSON" "$BACKUP/claude.json"
    printf 'json\n' >> "$tmp"
  fi
  while IFS="$TAB" read -r o n; do printf 'rename\t%s\t%s\n' "$o" "$n" >> "$tmp"; done < <(scan_renames)
  origin=$(git -C "$OLD_REPO" remote get-url origin 2>/dev/null || true)
  if [ -n "$origin" ]; then printf 'origin\t%s\n' "$origin" >> "$tmp"; fi
  rm -f "$list"
  gen_undo
  printf 'done\t2\n' >> "$tmp"
  mv "$tmp" "$MANIFEST"
  say "run: backed up $nfiles files and $CLAUDE_JSON to $BACKUP; manifest has $nren renames"
  if [ "${STOP_AFTER:-}" = 2 ]; then say "stop: STOP_AFTER=2"; exit 99; fi
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
  cur=$(git -C "$NEW_REPO" remote get-url origin 2>/dev/null || true)
  if [ "$cur" = "$NEW_REMOTE" ]; then say "skip: origin is already $NEW_REMOTE"
  else git -C "$NEW_REPO" remote set-url origin "$NEW_REMOTE"; say "run: origin -> $NEW_REMOTE"; fi
  git -C "$NEW_REPO" fetch -q origin
  say "run: fetched origin"
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
  local o n d f cnt=0
  if is_done 6; then say "skip: step 6 (transcripts, INDEX.md) already done"; return 0; fi
  while IFS="$TAB" read -r o n; do
    case "$o" in "$CLAUDE_HOME"/projects/*) ;; *) continue ;; esac
    d=$n; [ -e "$d" ] || d=$o
    while IFS= read -r f; do
      [ -n "$f" ] || continue
      cnt=$(( cnt + 1 ))
      if [ "$EXECUTE" -eq 1 ]; then rewrite_file "$f" fields cwd ""; fi
    done < <(grep -rlF --include='*.jsonl' -- "$OLD_REPO" "$d" 2>/dev/null || true)
  done < <(renames)
  if [ "$EXECUTE" -eq 0 ]; then
    say "would: rewrite \"cwd\" in $cnt transcript files, key in lessons/INDEX.md"
    return 0
  fi
  if [ -f "$CLAUDE_HOME/lessons/INDEX.md" ]; then rewrite_file "$CLAUDE_HOME/lessons/INDEX.md" text "" "" keyonly; fi
  mark_done 6
}

# ---- 7. ~/.claude.json ------------------------------------------------------------------------
JQ_CLAUDE_JSON='
def nm: (length == 0) or (.[0:1] | test("[A-Za-z0-9._-]") | not);
(if (.projects | type) == "object" then .projects |= with_entries(
    .key as $k
    | if ($k | startswith($old)) and ($k[($old | length):] | nm)
      then .key = $new + $k[($old | length):] else . end)
  else . end)
| (if (.githubRepoPaths | type) == "object" then .githubRepoPaths |= (
    if has("extrode/bayan") then del(.["extrode/bayan"]) | .["ikhbat-org/bayan"] = [$new] else . end)
  else . end)'
step7() {
  local tmp flag=
  if is_done 7; then say "skip: step 7 (.claude.json) already done"; return 0; fi
  if [ ! -f "$CLAUDE_JSON" ]; then say "skip: $CLAUDE_JSON does not exist"; return 0; fi
  if [ "$EXECUTE" -eq 0 ]; then say "would: move projects keys and githubRepoPaths in $CLAUDE_JSON"; return 0; fi
  tmp=$CLAUDE_JSON.mvtmp.$$
  cp -p "$CLAUDE_JSON" "$tmp"
  if [ "$(wc -l < "$CLAUDE_JSON")" -le 1 ]; then flag=-c; fi
  if ! jq $flag --arg old "$OLD_REPO" --arg new "$NEW_REPO" "$JQ_CLAUDE_JSON" "$CLAUDE_JSON" > "$tmp" \
     || ! jq empty "$tmp" 2>/dev/null \
     || [ "$(jq 'keys | length' "$CLAUDE_JSON")" != "$(jq 'keys | length' "$tmp")" ] \
     || [ "$(jq '(.projects // {}) | length' "$CLAUDE_JSON")" != "$(jq '(.projects // {}) | length' "$tmp")" ]; then
    rm -f "$tmp"; say "refuse: step 7: jq output invalid or key counts changed; $CLAUDE_JSON unchanged"; exit 1
  fi
  if cmp -s "$tmp" "$CLAUDE_JSON"; then rm -f "$tmp"; say "skip: $CLAUDE_JSON already holds the new keys"
  else mv "$tmp" "$CLAUDE_JSON"; say "run: rewrote $CLAUDE_JSON"; fi
  mark_done 7
}

# ---- 8. registry, history, queue, job state -----------------------------------------------------
step8() {
  local f
  if is_done 8; then say "skip: step 8 (registry, history, queue, jobs) already done"; return 0; fi
  if [ "$EXECUTE" -eq 0 ]; then
    say "would: rewrite workers/registry.json, history.jsonl (project), lessons/queue.jsonl, jobs/*/state.json"
    return 0
  fi
  if [ -f "$CLAUDE_HOME/workers/registry.json" ]; then
    rewrite_file "$CLAUDE_HOME/workers/registry.json" fields 'cwd|repo|worktree|criteria' json
  fi
  if [ -f "$CLAUDE_HOME/history.jsonl" ]; then rewrite_file "$CLAUDE_HOME/history.jsonl" fields project json; fi
  if [ -f "$CLAUDE_HOME/lessons/queue.jsonl" ]; then
    rewrite_file "$CLAUDE_HOME/lessons/queue.jsonl" fields 'transcript|cwd|project' json
  fi
  for f in "$CLAUDE_HOME"/jobs/*/state.json; do
    [ -f "$f" ] || continue
    rewrite_file "$f" all "" json
  done
  mark_done 8
}

# ---- 9. undo.sh -------------------------------------------------------------------------------
gen_undo() {
  local u=$BACKUP/undo.sh
  {
    echo '#!/usr/bin/env bash'
    echo '# Reverses steps 3-8 of 2026-11-02-ikhbat-org.sh from the manifest and backups. Idempotent. Close Claude first.'
    echo 'set -euo pipefail'
    printf 'OLD_REPO=%q\nNEW_REPO=%q\nCLAUDE_HOME=%q\nCLAUDE_JSON=%q\nBACKUP=%q\n' \
      "$OLD_REPO" "$NEW_REPO" "$CLAUDE_HOME" "$CLAUDE_JSON" "$BACKUP"
    cat <<'EOF'
TAB=$(printf '\t')
MANIFEST=$BACKUP/manifest.txt
[ -f "$MANIFEST" ] || { echo "refuse: no manifest at $MANIFEST"; exit 1; }
while IFS="$TAB" read -r kind a b; do
  if [ "$kind" = rename ] && [ -e "$b" ] && [ ! -e "$a" ]; then mv "$b" "$a"; echo "run: mv $b $a"; fi
done < "$MANIFEST"
while IFS="$TAB" read -r kind a b; do
  case "$kind" in
    home) mkdir -p "$(dirname "$CLAUDE_HOME/$a")"; cp -p "$BACKUP/files/$a" "$CLAUDE_HOME/$a"; echo "run: restored $a" ;;
    json) cp -p "$BACKUP/claude.json" "$CLAUDE_JSON"; echo "run: restored $CLAUDE_JSON" ;;
  esac
done < "$MANIFEST"
if [ -d "$NEW_REPO" ] && [ ! -e "$OLD_REPO" ]; then
  mkdir -p "$(dirname "$OLD_REPO")"; mv "$NEW_REPO" "$OLD_REPO"; echo "run: mv $NEW_REPO $OLD_REPO"
fi
while IFS="$TAB" read -r kind a b; do
  if [ "$kind" = origin ] && [ -d "$OLD_REPO" ]; then git -C "$OLD_REPO" remote set-url origin "$a"; echo "run: origin -> $a"; fi
done < "$MANIFEST"
touch "$BACKUP/undone"
echo "ok: undone"
EOF
  } > "$u"
  chmod +x "$u"
}
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
      while IFS= read -r f; do
        [ -n "$f" ] || continue
        c=$(rw_run fields cwd "$f"); c=${c% *}
        [ "$c" -eq 0 ] || { say "FAIL: $f has $c old-path cwd values"; bad=1; }
      done < <(grep -rlF --include='*.jsonl' -- "$OLD_REPO" "$n" 2>/dev/null || true) ;;
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
    jq -e --arg new "$NEW_REPO" '(.githubRepoPaths // {}) as $g | ($g | has("extrode/bayan") | not) and (($g | has("ikhbat-org/bayan") | not) or $g["ikhbat-org/bayan"] == [$new])' \
      "$CLAUDE_JSON" >/dev/null || { say "FAIL: githubRepoPaths not rewritten"; bad=1; }
    say "info: projects[$NEW_REPO] = $(jq -c --arg new "$NEW_REPO" '.projects[$new]' "$CLAUDE_JSON")"
    say "info: githubRepoPaths[ikhbat-org/bayan] = $(jq -c '.githubRepoPaths["ikhbat-org/bayan"]' "$CLAUDE_JSON")"
  fi
  [ ! -e "$OLD_REPO" ] || { say "FAIL: $OLD_REPO still exists"; bad=1; }
  git -C "$NEW_REPO" status -sb
  git -C "$NEW_REPO" remote -v
  if [ "$bad" -ne 0 ]; then exit 1; fi
  say "ok: verified"
}

# ---- main -------------------------------------------------------------------------------------
if [ "$EXECUTE" -eq 1 ]; then say "mode: execute"; else say "mode: dry run (use --execute to act)"; fi
preflight
step2
step3
step4
step5
step6
step7
step8
step9
step10
