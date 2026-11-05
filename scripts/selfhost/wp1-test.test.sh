#!/bin/bash
# wp1-test.sh on a fixture repo under tmp/, with a fake scp and ssh on PATH that record their arguments (never wp1 itself): the bundle
# goes to wp1's tmp/, ssh clones it into the short-sha directory and runs the filtered dotnet test; tmp/wp1/<sha>.out holds the
# header, the output and `exit <code>`; a run of no tests fails, a remote failure keeps its code, and a failed scp skips ssh.
set -e
root="$(cd "$(dirname "$0")/../.." && pwd)"
r="$root/tmp/wp1-test-test"; aux="$root/tmp/wp1-test-test-aux"
rm -rf "$r" "$aux" && mkdir -p "$r" "$aux/bin" && cd "$r"
git init -q -b base && git config user.email t@t && git config user.name t
printf 'tmp/\n' > .gitignore; printf 'x\n' > a.txt; git add -A && git commit -qm "test: base"
git branch feat/x
printf '#!/bin/bash\necho "$*" > "$AUX/scp-args"; [ -f "$AUX/scp-fail" ] && { echo "scp: lost"; exit 3; }; exit 0\n' > "$aux/bin/scp"
printf '#!/bin/bash\necho "$*" > "$AUX/ssh-args"; cat "$AUX/ssh-out"; exit "$(cat "$AUX/ssh-rc")"\n' > "$aux/bin/ssh"
chmod +x "$aux/bin/scp" "$aux/bin/ssh"; export AUX=$aux
w() { PATH="$aux/bin:$PATH" /bin/bash "$root/scripts/selfhost/wp1-test.sh" "$@" 2>&1 && echo "rc=0" || echo "rc=$?"; }
sha=$(git rev-parse feat/x); s=${sha:0:8}; R=C:/home/code/extrode.com/bayan/tmp
F='FullyQualifiedName~ATests|FullyQualifiedName~BTests'
printf 'Passed!  - Failed:     0, Passed:     3, Skipped:     0, Total:     3\n' > "$aux/ssh-out"; echo 0 > "$aux/ssh-rc"
good=$(w feat/x "$F"); gout=$(cat "tmp/wp1/$sha.out"); scpa=$(cat "$aux/scp-args"); ssha=$(cat "$aux/ssh-args")
bundled=$(git bundle list-heads "tmp/wp1/$s.bundle")
: > "$aux/ssh-out"; none=$(w feat/x "$F"); nout=$(cat "tmp/wp1/$sha.out")
printf 'Failed!  - Failed:     1, Passed:     2, Skipped:     0, Total:     3\n' > "$aux/ssh-out"; echo 1 > "$aux/ssh-rc"; red=$(w feat/x "$F"); rout=$(cat "tmp/wp1/$sha.out")
rm "$aux/ssh-args"; touch "$aux/scp-fail"; lost=$(w feat/x "$F"); lout=$(cat "tmp/wp1/$sha.out"); [ ! -f "$aux/ssh-args" ] && noss=1 || noss=0
usage=$(w feat/x); nobr=$(w feat/none "$F"); quote=$(w feat/x "A'B")
cd "$root"; rm -rf "$r" "$aux"
fail() { echo "FAIL: $1"; echo "$2"; exit 1; }
has() { grep -qxF -- "$2" <<< "$1"; }
[ "$scpa" = "-q tmp/wp1/$s.bundle wp1:/$R/" ] || fail "scp ran '$scpa'" "$good"
CMD="cd $R && git clone -q -b feat/x $s.bundle $s && cd $s && dotnet test tests/Bayan.Tests -c Release -p:NuGetAudit=false -v q --filter '$F'"
[ "$ssha" = "wp1 $CMD" ] || fail "ssh ran '$ssha'" "$good"
grep -q "^$sha refs/heads/feat/x$" <<< "$bundled" || fail "the bundle does not hold feat/x" "$bundled"
[ "$(head -4 <<< "$gout")" = "$(printf 'branch: feat/x\nsha: %s\nfilter: %s\ncommand: ssh wp1 "%s"' "$sha" "$F" "$CMD")" ] || fail "the .out header is wrong" "$gout"
[ "$(tail -2 <<< "$gout")" = "$(printf 'Passed!  - Failed:     0, Passed:     3, Skipped:     0, Total:     3\nexit 0')" ] || fail "the .out does not end in the output and 'exit 0'" "$gout"
has "$good" "rc=0" || fail "a passing run failed" "$good"
has "$none" "wp1-test: the run matched no tests (tmp/wp1/$sha.out)" && has "$none" "rc=1" && [ "$(tail -1 <<< "$nout")" = "exit 0" ] || fail "a run of no tests passed" "$none"
has "$red" "rc=1" && [ "$(tail -1 <<< "$rout")" = "exit 1" ] || fail "a remote failure did not keep its code" "$red"
has "$lost" "rc=3" && [ "$(tail -2 <<< "$lout")" = "$(printf 'scp: lost\nexit 3')" ] && [ $noss = 1 ] || fail "a failed scp did not stop before ssh" "$lost"
has "$usage" "usage: wp1-test.sh <branch> <filter>" && has "$usage" "rc=2" || fail "one argument did not print the usage" "$usage"
has "$nobr" "wp1-test: no branch feat/none" && has "$nobr" "rc=2" || fail "an unknown branch did not fail" "$nobr"
has "$quote" "wp1-test: the filter holds a single quote" && has "$quote" "rc=2" || fail "a quote in the filter was not refused" "$quote"
echo PASS
