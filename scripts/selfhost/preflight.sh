#!/bin/bash
# preflight.sh — the mechanical checks a task branch passes before its independent review; land.sh runs it first and refuses on FAIL.
#   bash scripts/selfhost/preflight.sh 012 T17 chore/worker-012-t17
# Prints `ok <check>` or `FAIL <check>: <why>` (one line per failing item) and exits 1 on any FAIL. The task line and its range
# come from the base branch (BAYAN_BASE, else the specs.tsv row's), as land.sh's guard reads them.
#   cites      every `path:N` / `path:N-M` on a line the branch adds to docs/bayan-handoff.md, reviews/<T>.md or the spec's plan names
#              an existing file and range (a bare name: any tracked file ending in it), and the backticked symbol the citation
#              quotes in the same clause (the next one after it, else the last before it) is on those lines (a single-line cite: on it or the 2 after it)
#   files      every file the branch changes is in the task's files list or one the guard allows; prints the addition
#   questions  every Question number the branch adds is new to the handoff (the branch's and the base's) and inside the task's
#              range (its task line, else $BAYAN_QRANGES or <main checkout>/tmp/q-ranges.tsv, written by wave-brief.py)
#   tests      the classes declared in the task's tests/Bayan.Tests/*.cs and CInventoryTests, SelfhostDriverT8Tests,
#              SelfhostDriverRuntimeTests pass, each running at least one test; scripts/plan-strings-check.js passes (deno)
#   wp1        tmp/wp1/<sha>.out (wp1-test.sh) exists for the branch's HEAD, ends in `exit 0`, ran tests, and its filter names every class above
# On a checkout of <branch> the working tree is checked (a writer before its commit), else the branch's commit in a temporary worktree.
# Logs go to tmp/preflight/<t>/.
set -u
[ $# = 3 ] || { echo "usage: preflight.sh <spec> T<n> <branch>"; exit 2; }
SPEC=$1; T=$2; BR=$3; t=$(echo "$T" | tr '[:upper:]' '[:lower:]')
HERE=$(cd "$(dirname "$0")" && pwd)
ROW=$(awk -F'\t' -v s="$SPEC" 'NR>1 && $1==s' "$HERE/specs.tsv")
[ -n "$ROW" ] || { echo "FAIL spec: no row for spec $SPEC in scripts/selfhost/specs.tsv"; exit 1; }
IFS=$'\t' read -r _ DIR TASKS PLAN _ SHARED BYDIR _ SBASE _ <<< "$ROW"
BASE=${BAYAN_BASE:-$SBASE}
SHA=$(git rev-parse --verify -q "$BR^{commit}") || { echo "FAIL branch: no branch $BR"; exit 1; }
git rev-parse --verify -q "$BASE^{commit}" > /dev/null || { echo "FAIL branch: no base branch $BASE"; exit 1; }
QR=${BAYAN_QRANGES:-$(dirname "$(git rev-parse --path-format=absolute --git-common-dir)")/tmp/q-ranges.tsv}
OUT=$PWD; L=$OUT/tmp/preflight/$t; rm -rf "$L"; mkdir -p "$L"
CROSS="CInventoryTests SelfhostDriverT8Tests SelfhostDriverRuntimeTests"
F=0; ok() { echo "ok $1"; }; fail() { echo "FAIL $1"; F=1; }

if [ "$(git rev-parse --abbrev-ref HEAD)" = "$BR" ]; then WT=$OUT; else
  WT=$OUT/tmp/preflight-$t; git worktree remove --force "$WT" 2> /dev/null; rm -rf "$WT"; git worktree prune
  git worktree add -q --detach "$WT" "$SHA" || { echo "FAIL branch: cannot check out $BR in $WT"; exit 1; }
  trap 'cd "$OUT"; git worktree remove --force "$WT" 2> /dev/null || true' EXIT
fi
cd "$WT"
MB=$(git merge-base "$BASE" HEAD) || { echo "FAIL branch: $BR has no merge base with $BASE"; exit 1; }

# the task line on the base, and its files list as wave-brief.py reads it
BLOCK=$(git show "$BASE:$DIR/$TASKS" 2> /dev/null | awk -v t="$T" '$0 ~ "^- \\[[ x]\\] " t " " { on = 1; print; next } on && /^- \[/ { exit } on')
[ -n "$BLOCK" ] || { echo "FAIL task: no $T in $DIR/$TASKS on $BASE"; exit 1; }
HL=$(head -1 <<< "$BLOCK"); FL=""
if [[ $HL == *"files: "* ]]; then
  FL=${HL#*files: }; FL=${FL%% — covers*}; FL=${FL%% — after*}; FL=${FL%% — done when*}; FL=${FL%% — C#:*}
  FL=$(sed -E 's/\([^()]*(\([^()]*\)[^()]*)*\)//g' <<< "$FL")
fi
ALLOW=(docs/bayan-handoff.md "$DIR/$TASKS"); TESTF=()
# a brace list in a name (`selfhost/semantics/{compile,types}.byn`, 012 T18) is one name per alternative
BR_RE='^([^{]*)\{([^{}]*)\}(.*)$'
braces() { local pre post a A; if [[ $1 =~ $BR_RE ]]; then pre=${BASH_REMATCH[1]}; post=${BASH_REMATCH[3]}
  IFS=',' read -ra A <<< "${BASH_REMATCH[2]}"; for a in "${A[@]}"; do braces "$pre$a$post"; done; else echo "$1"; fi; }
while IFS= read -r f; do
  [ -n "$f" ] || continue
  [[ $f =~ ^$SPEC-[a-z]+\.md$ ]] && f=$DIR/$f
  if [[ $f == tests/* || $f == docs/* || $f == src/* || $f == work/* || $f == bootstrap/* || $f =~ ^[A-Z]+\.md$ ]]; then
    ALLOW+=("$f"); [[ $f =~ ^tests/Bayan\.Tests/[A-Za-z0-9]+Tests\.cs$ ]] && TESTF+=("$f")
  elif [[ $f == *.byn ]]; then [[ $f == selfhost/* ]] && ALLOW+=("$f") || ALLOW+=("$BYDIR/$f")
  elif [[ $f == *.cs ]]; then ALLOW+=("tests/Bayan.Tests/$f"); TESTF+=("tests/Bayan.Tests/$f"); fi
done < <(grep -oE '`[^`]+\.(byn|cs|md|c|h|diag|txt|stdout|stderr|exitcode|sha256)`' <<< "$FL" | tr -d '`' | while IFS= read -r x; do braces "$x"; done)

CHANGED=$( { git diff --name-only "$BASE...HEAD"; git diff --name-only; git ls-files --others --exclude-standard; } | sort -u)
UNTRACKED=$(git ls-files --others --exclude-standard)
# added lines of a file against the merge base, working tree included (an untracked file: all of it)
added() { if grep -qxF -- "$1" <<< "$UNTRACKED"; then cat "$1"; else git diff "$MB" -- "$1" | sed -n 's/^+\([^+]\)/\1/p; s/^+$//p'; fi; }

# cites
{ git ls-files; echo "$UNTRACKED"; } | sort -u > "$L/files.txt"
for s in docs/bayan-handoff.md "$DIR/reviews/$T.md" "$DIR/$PLAN"; do added "$s" | sed "s|^|$s	|"; done > "$L/added.txt"
LC_ALL=C awk -F'\t' -f /dev/stdin "$L/added.txt" > "$L/cites.txt" <<'AWK'
function rep(c, n,  s) { s = ""; while (n-- > 0) s = s c; return s }
function pathlike(x) { return x ~ /^Q[0-9]+$/ || x ~ /^[A-Za-z0-9_.\/-]*\.(byn|cs|c|h|md|txt|tsv|py|sh|js|json)(:[0-9]+(-[0-9]+)?)?$/ }
{
  src = $1; line = substr($0, length($1) + 2); n = length(line)
  q = ""; if (match(line, /^[0-9][0-9][0-9][0-9]?\. /)) q = "Q" substr(line, 1, RLENGTH - 2) " "
  ns = 0; op = 0
  for (i = 1; i <= n; i++) if (substr(line, i, 1) == "`") { if (!op) op = i; else { ns++; ss[ns] = op; se[ns] = i; op = 0 } }
  # clause boundaries, outside backtick spans: ; ( ) and , : . followed by a blank, and " — "
  mask = ""; k = 1
  for (j = 1; j <= ns; j++) { mask = mask substr(line, k, ss[j] - k + 1) rep("x", se[j] - ss[j] - 1) "`"; k = se[j] + 1 }
  mask = mask substr(line, k)
  for (i = 1; i <= n; i++) {
    c = substr(mask, i, 1); d = substr(mask, i + 1, 1)
    bnd[i] = (c == ";" || c == "(" || c == ")" || ((c == "," || c == ":" || c == ".") && (d == " " || d == "")) || substr(mask, i, 5) == " \342\200\224 ")
  }
  off = 1
  while (match(substr(line, off), /[A-Za-z0-9_.\/-]*[A-Za-z0-9_-]\.[A-Za-z][A-Za-z0-9]*:[0-9]+(-[0-9]+)?/)) {
    p = off + RSTART - 1; e = p + RLENGTH; cite = substr(line, p, RLENGTH); off = e
    cs = 0; for (i = p - 1; i >= 1; i--) if (bnd[i]) { cs = i; break }
    ce = n + 1; for (i = e; i <= n; i++) if (bnd[i]) { ce = i; break }
    sym = ""
    for (j = 1; j <= ns && sym == ""; j++) if (ss[j] >= e && se[j] < ce && !pathlike(substr(line, ss[j] + 1, se[j] - ss[j] - 1))) sym = substr(line, ss[j] + 1, se[j] - ss[j] - 1)
    if (sym == "") for (j = ns; j >= 1 && sym == ""; j--) if (ss[j] > cs && se[j] < p && !pathlike(substr(line, ss[j] + 1, se[j] - ss[j] - 1))) sym = substr(line, ss[j] + 1, se[j] - ss[j] - 1)
    split(cite, a, ":"); split(a[2], r, "-"); hi = (r[2] == "") ? r[1] : r[2]
    print src "\037" q "\037" cite "\037" a[1] "\037" r[1] "\037" hi "\037" sym
  }
  for (i = 1; i <= n; i++) delete bnd[i]
}
AWK
nc=0; bad=0
while IFS=$'\037' read -r src q cite path lo hi sym; do
  nc=$((nc + 1))
  if [ -f "$path" ]; then cands=$path; else cands=$(awk -v s="/$path" 'length($0) > length(s) && substr($0, length($0) - length(s) + 1) == s' "$L/files.txt"); fi
  why="no such file"; good=0
  while IFS= read -r c; do
    [ -n "$c" ] && [ -f "$c" ] || continue
    k=$(awk 'END { print NR }' "$c")
    if [ "$lo" -lt 1 ] || [ "$hi" -lt "$lo" ] || [ "$hi" -gt "$k" ]; then why="$c has $k lines"
    elif [ "$hi" = "$lo" ] && [ -n "$sym" ] && ! sed -n "${lo},$((lo + 2))p" "$c" | grep -qF -- "$sym"; then why="\`$sym\` is not on $c:$lo or the 2 lines after it"
    elif [ "$hi" != "$lo" ] && [ -n "$sym" ] && ! sed -n "${lo},${hi}p" "$c" | grep -qF -- "$sym"; then why="\`$sym\` is not on $c:$lo-$hi"
    else good=1; break; fi
  done <<< "$cands"
  [ $good = 1 ] || { fail "cites: $src ${q}\`$cite\`: $why"; bad=1; }
done < "$L/cites.txt"
[ $bad = 1 ] || ok "cites ($nc checked)"

# files
need=()
while IFS= read -r f; do
  [ -n "$f" ] || continue
  case "$f" in docs/bayan-handoff.md|work/todo.md|docs/specs/*/*-tasks.md|docs/specs/*/reviews/*.md) continue;; esac
  in=0; IFS=',' read -ra SH <<< "$SHARED"; for p in "${SH[@]}"; do [ "$f" = "$p" ] && in=1; done
  for p in "${ALLOW[@]}"; do [[ "$f" == $p ]] && in=1; done
  [ $in = 1 ] || need+=("$f")
done <<< "$CHANGED"
if [ ${#need[@]} = 0 ]; then ok "files ($(grep -c . <<< "$CHANGED") changed)"
else fail "files: ${#need[@]} changed outside $T's files list; append to its files: in $DIR/$TASKS:$(printf ' `%s`,' "${need[@]}" | sed 's/,$//')"; fi

# questions
RANGE=$(grep -oE 'Q[0-9]{3,4}–Q[0-9]{3,4}' <<< "$BLOCK" | head -1 | tr -d Q | sed 's/–/ /')
[ -n "$RANGE" ] || RANGE=$(awk -F'\t' -v s="$SPEC" -v t="$T" '$1 == s && $2 == t { print $3, $4; exit }' "$QR" 2> /dev/null)
OLDQ=$(git show "$MB:docs/bayan-handoff.md" 2> /dev/null | grep -oE '^[0-9]{3,4}\. ' | tr -d '. ')
BASEQ=$(git show "$BASE:docs/bayan-handoff.md" 2> /dev/null | grep -oE '^[0-9]{3,4}\. ' | tr -d '. ')
# a number the branch adds: on an added line, and more often in the handoff than at the merge base (an edited Question is not one)
NEWQ=$(for n in $(added docs/bayan-handoff.md | grep -oE '^[0-9]{3,4}\. ' | tr -d '. ' | sort -un); do
  [ "$(grep -cE "^$n\. " docs/bayan-handoff.md)" -gt "$(grep -cxF "$n" <<< "$OLDQ")" ] && echo "$n"; done)
bad=0
for n in $NEWQ; do
  c=$(grep -cE "^$n\. " docs/bayan-handoff.md)
  [ "$c" -le 1 ] || { fail "questions: Q$n is in docs/bayan-handoff.md $c times"; bad=1; }
  [ "$c" -le 1 ] || continue
  grep -qxF "$n" <<< "$BASEQ" && { fail "questions: Q$n is already on $BASE (another task took it)"; bad=1; }
  if [ -z "$RANGE" ]; then fail "questions: Q$n added, but $T has no Question range (none in its task line or $QR; wave-brief.py assigns one)"; bad=1
  else read -r a b <<< "$RANGE"; [ "$n" -ge "$a" ] && [ "$n" -le "$b" ] || { fail "questions: Q$n is outside $T's range Q${a}–Q${b}"; bad=1; }; fi
done
[ $bad = 1 ] || ok "questions ($(echo $NEWQ | wc -w | tr -d ' ') added${RANGE:+, range Q${RANGE% *}–Q${RANGE#* }})"

# tests
CLASSES=()
for f in "${TESTF[@]+"${TESTF[@]}"}"; do
  cs=$( [ -f "$f" ] && grep -E '(^|[^A-Za-z0-9_])class +[A-Za-z0-9_]+Tests([^A-Za-z0-9_]|$)' "$f" | sed -E 's/.*class +([A-Za-z0-9_]+Tests).*/\1/')
  [ -n "$cs" ] || cs=$(basename "$f" .cs)
  CLASSES+=($cs)
done
CLASSES+=($CROSS)
CLASSES=($(printf '%s\n' "${CLASSES[@]}" | awk '!seen[$0]++'))
FILT=$(printf 'FullyQualifiedName~%s|' "${CLASSES[@]}"); FILT=${FILT%|}
if ! dotnet build -v q > "$L/build.txt" 2>&1; then fail "tests: build failed — $(grep -m3 -E ' error [A-Z]+[0-9]+' "$L/build.txt" | sed 's/.*\///' | tr '\n' ' ')"
else
  dotnet test --no-build --filter "$FILT" --logger "console;verbosity=normal" > "$L/test.txt" 2>&1; TX=$?
  bad=0
  for c in "${CLASSES[@]}"; do
    grep -qE "^ *(Passed|Failed|Skipped) ([A-Za-z0-9_]+\.)*$c\." "$L/test.txt" || { fail "tests: FullyQualifiedName~$c matched no tests"; bad=1; }
  done
  grep -E '^ *Failed [A-Za-z0-9_.]+' "$L/test.txt" | sed -E 's/^ *Failed ([^ []+).*/\1/' | while read -r x; do echo "FAIL tests: $x failed"; done
  if grep -qE '^ *Failed [A-Za-z0-9_.]+' "$L/test.txt" || [ $TX != 0 ]; then F=1; bad=1; [ $TX = 0 ] || echo "FAIL tests: dotnet test exited $TX (tmp/preflight/$t/test.txt)"; fi
  [ $bad = 1 ] || ok "tests ($(grep -cE '^ *Passed ' "$L/test.txt") passed: ${CLASSES[*]})"
fi
if ! command -v deno > /dev/null; then fail "tests: deno is not installed, so scripts/plan-strings-check.js cannot run"
elif deno run -A scripts/plan-strings-check.js --base "$BASE" > "$L/plan-strings.txt" 2>&1; then ok "plan-strings"
else fail "tests: scripts/plan-strings-check.js --base $BASE failed — $(tail -1 "$L/plan-strings.txt")"; fi

# wp1
W=$OUT/tmp/wp1/$SHA.out; RUN="bash scripts/selfhost/wp1-test.sh $BR '$FILT'"
if [ -n "$(git status --porcelain)" ]; then fail "wp1: the working tree has uncommitted changes; after the commit, run: $RUN"
elif [ ! -f "$W" ]; then fail "wp1: no tmp/wp1/$SHA.out for $BR's HEAD; run: $RUN"
elif [ "$(tail -1 "$W")" != "exit 0" ]; then fail "wp1: tmp/wp1/$SHA.out ends in '$(tail -1 "$W")', not 'exit 0'"
elif ! grep -qE 'Total: *[1-9]' "$W"; then fail "wp1: tmp/wp1/$SHA.out ran no tests"
else
  wf=$(sed -n 's/^filter: //p' "$W" | head -1); miss=()
  for c in "${CLASSES[@]}"; do [[ "|$wf|" == *"|FullyQualifiedName~$c|"* ]] || miss+=("$c"); done
  if [ ${#miss[@]} = 0 ]; then ok "wp1 ($(grep -m1 -oE 'Passed: *[0-9]+' "$W"))"
  else fail "wp1: tmp/wp1/$SHA.out ran '$wf', without ${miss[*]}; run: $RUN"; fi
fi
exit $F
