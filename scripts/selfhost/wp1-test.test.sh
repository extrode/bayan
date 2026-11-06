#!/bin/bash
# wp1-test.sh on a fixture repo under tmp/, with a fake scp and ssh on PATH that record their arguments (never wp1 itself): the bundle
# goes to wp1's tmp/, ssh clones it into the short-sha directory and runs the filtered dotnet test; tmp/wp1/<sha>.out holds the
# header, the output and `exit <code>`; a run of no tests fails, a remote failure keeps its code, and a failed scp skips ssh.
# Then pwsh runs the remote command on a local stand-in for wp1's tmp/: a re-run replaces its own clone, never another directory.
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
# the remote command itself, run by pwsh with wp1's tmp/ at $aux/remote and a fake dotnet: a re-run replaces the clone it made,
# a directory it did not make is left alone (exit 3)
rm "$aux/scp-fail"; mkdir "$aux/remote"; printf 'Passed!  - Failed:     0, Passed:     3, Skipped:     0, Total:     3\n' > "$aux/ssh-out"
cat > "$aux/bin/scp" <<'EOF'
#!/bin/bash
cp "$2" "$AUX/remote/"
EOF
cat > "$aux/bin/ssh" <<'EOF'
#!/bin/bash
R=C:/home/code/extrode.com/bayan/tmp; c=${2//"$R"/$AUX/remote}
exec pwsh -NoProfile -NonInteractive -Command "$c"
EOF
printf '#!/bin/bash\ncat "$AUX/ssh-out"\n' > "$aux/bin/dotnet"; chmod +x "$aux/bin/dotnet"
first=$(w feat/x "$F"); [ -f "$aux/remote/$s/a.txt" ] && [ -f "$aux/remote/$s.wp1-test" ] && cloned=1 || cloned=0
touch "$aux/remote/$s/stale"; again=$(w feat/x "$F"); [ -f "$aux/remote/$s/a.txt" ] && [ ! -e "$aux/remote/$s/stale" ] && replaced=1 || replaced=0
rm "$aux/remote/$s.wp1-test"; touch "$aux/remote/$s/stale"; foreign=$(w feat/x "$F"); [ -e "$aux/remote/$s/stale" ] && kept=1 || kept=0
# a commit landing on the branch between the sha lookup and the bundle: the remote clone is not the sha, so the run exits 4
realgit=$(command -v git)
printf '#!/bin/bash\nif [ "$1" = bundle ]; then n=$(%s commit-tree -p feat/x -m moved "feat/x^{tree}"); %s update-ref refs/heads/feat/x "$n"; fi\nexec %s "$@"\n' "$realgit" "$realgit" "$realgit" > "$aux/bin/git"; chmod +x "$aux/bin/git"
rm -rf "$aux/remote/$s"; moved=$(w feat/x "$F"); mout=$(cat "tmp/wp1/$sha.out")
cd "$root"; rm -rf "$r" "$aux"
fail() { echo "FAIL: $1"; echo "$2"; exit 1; }
has() { grep -qxF -- "$2" <<< "$1"; }
[ "$scpa" = "-q tmp/wp1/$s.bundle wp1:/$R/" ] || fail "scp ran '$scpa'" "$good"
CMD=${ssha#wp1 }
for p in "Set-Location '$R';" "git clone -q -b feat/x $s.bundle $s;" "Set-Location '$s'; if ((git rev-parse HEAD) -ne '$sha') { Write-Output 'wp1-test: feat/x moved after $sha was resolved; the clone is not $sha'; exit 4 }; dotnet test tests/Bayan.Tests -c Release -p:NuGetAudit=false -v q --filter '$F'; exit \$LASTEXITCODE"; do
  [[ $ssha == "wp1 "*"$p"* ]] || fail "ssh ran '$ssha', without '$p'" "$good"; done
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
has "$first" "rc=0" && [ $cloned = 1 ] || fail "the remote command did not clone into the short-sha directory with its marker" "$first"
has "$again" "rc=0" && [ $replaced = 1 ] || fail "a re-run for the same sha did not replace the clone it made" "$again"
has "$foreign" "wp1-test: $aux/remote/$s exists and wp1-test.sh did not create it" && has "$foreign" "rc=3" && [ $kept = 1 ] \
  || fail "a directory wp1-test.sh did not make was not left alone" "$foreign"
has "$moved" "rc=4" && [ "$(tail -1 <<< "$mout")" = "exit 4" ] && grep -q "moved after $sha was resolved" <<< "$mout" || fail "a branch that moved after the sha lookup was tested as the sha" "$moved"
echo PASS
