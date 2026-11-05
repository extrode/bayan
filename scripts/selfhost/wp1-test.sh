#!/bin/bash
# wp1-test.sh — run a branch's tests on the Windows host wp1 and record what ran (preflight.sh reads the record).
#   bash scripts/selfhost/wp1-test.sh chore/worker-012-t17 'FullyQualifiedName~StdFsTests|FullyQualifiedName~CInventoryTests'
# Bundles <branch> into tmp/wp1/<short sha>.bundle, copies it to wp1:/C:/home/code/extrode.com/bayan/tmp/ (scp), and over ssh
# clones it there into tmp/<short sha> and runs `dotnet test tests/Bayan.Tests -c Release -p:NuGetAudit=false -v q --filter <filter>`.
# Writes tmp/wp1/<sha>.out: branch, sha, filter and the remote command, then the run's output, then `exit <code>`.
# Exits with the run's code, or 1 when the run matched no tests.
set -u
[ $# = 2 ] || { echo "usage: wp1-test.sh <branch> <filter>"; exit 2; }
BR=$1; FILT=$2
[[ $FILT != *\'* ]] || { echo "wp1-test: the filter holds a single quote"; exit 2; }
SHA=$(git rev-parse --verify -q "$BR^{commit}") || { echo "wp1-test: no branch $BR"; exit 2; }
S=${SHA:0:8}; R=C:/home/code/extrode.com/bayan/tmp; OUT=tmp/wp1/$SHA.out
mkdir -p tmp/wp1
git bundle create "tmp/wp1/$S.bundle" "$BR" > /dev/null 2>&1 || { echo "wp1-test: git bundle create failed for $BR"; exit 1; }
CMD="cd $R && git clone -q -b $BR $S.bundle $S && cd $S && dotnet test tests/Bayan.Tests -c Release -p:NuGetAudit=false -v q --filter '$FILT'"
printf 'branch: %s\nsha: %s\nfilter: %s\ncommand: ssh wp1 "%s"\n' "$BR" "$SHA" "$FILT" "$CMD" > "$OUT"
scp -q "tmp/wp1/$S.bundle" "wp1:/$R/" >> "$OUT" 2>&1; rc=$?
[ $rc = 0 ] && { ssh wp1 "$CMD" >> "$OUT" 2>&1; rc=$?; }
echo "exit $rc" >> "$OUT"
tail -4 "$OUT"
[ $rc = 0 ] || exit $rc
grep -qE 'Total: *[1-9]' "$OUT" || { echo "wp1-test: the run matched no tests ($OUT)"; exit 1; }
