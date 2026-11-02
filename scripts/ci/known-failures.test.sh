#!/usr/bin/env bash
# Tests scripts/ci/known-failures.py and its wiring in .github/workflows/ci.yml.
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
script="$root/scripts/ci/known-failures.py"
tmp="$root/tmp/known-failures-test"
rm -rf "$tmp"
mkdir -p "$tmp"
trap 'rm -rf "$tmp"' EXIT

NS="http://microsoft.com/schemas/VisualStudio/TeamTest/2010"
DX='Bayan.Tests.P.Check(name: "diag/x/main.byn")'

# trx FILE NAME=OUTCOME...
trx() {
  local f="$1"; shift
  {
    echo "<?xml version=\"1.0\" encoding=\"utf-8\"?>"
    echo "<TestRun xmlns=\"$NS\"><Results>"
    for kv in "$@"; do
      local n="${kv%=*}" o="${kv##*=}"
      n="$(printf '%s' "$n" | sed -e 's/&/\&amp;/g' -e 's/"/\&quot;/g' -e 's/</\&lt;/g')"
      echo "<UnitTestResult testName=\"$n\" outcome=\"$o\"/>"
    done
    echo "</Results></TestRun>"
  } > "$f"
}

list="$tmp/list.txt"
printf '# comment\nBayan.Tests.A.One\n%s\n' "$DX" > "$list"

# run NAME EXPECTED_CODE PATTERN ARGS...
run() {
  local name="$1" want="$2" pat="$3"; shift 3
  local out code=0
  out="$(python3 -I "$script" "$@" 2>&1)" || code=$?
  if [ "$code" != "$want" ]; then
    echo "FAIL $name: exit $code, want $want"; echo "$out"; exit 1
  fi
  if [ -n "$pat" ] && ! grep -qF -- "$pat" <<<"$out"; then
    echo "FAIL $name: output lacks '$pat'"; echo "$out"; exit 1
  fi
}

trx "$tmp/ok.trx" "Bayan.Tests.A.One=Failed" "$DX=Failed" "Bayan.Tests.A.Two=Passed"
run listed-only 0 "0 new" "$list" "$tmp/ok.trx"

trx "$tmp/new.trx" "Bayan.Tests.A.One=Failed" "$DX=Failed" "Bayan.Tests.A.Three=Failed"
run new-failure 1 "Bayan.Tests.A.Three" "$list" "$tmp/new.trx"

trx "$tmp/pass.trx" "Bayan.Tests.A.One=Passed" "$DX=Failed"
run now-passes 1 "Bayan.Tests.A.One" "$list" "$tmp/pass.trx"

trx "$tmp/absent.trx" "$DX=Failed" "Bayan.Tests.A.Two=Passed"
run not-run 0 "not run: Bayan.Tests.A.One" "$list" "$tmp/absent.trx"

for o in Error Timeout Aborted; do
  trx "$tmp/$o.trx" "Bayan.Tests.A.One=Failed" "$DX=Failed" "Bayan.Tests.A.Four=$o"
  run "outcome-$o" 1 "new failure: Bayan.Tests.A.Four" "$list" "$tmp/$o.trx"
  trx "$tmp/$o-known.trx" "Bayan.Tests.A.One=$o" "$DX=Failed"
  run "outcome-$o-known" 0 "2 failed, 2 known" "$list" "$tmp/$o-known.trx"
done

# exact match of a display name with a (name: "...") suffix
trx "$tmp/exact.trx" "Bayan.Tests.A.One=Failed" 'Bayan.Tests.P.Check(name: "diag/x/other.byn")=Failed'
run exact-name 1 "new failure: Bayan.Tests.P.Check(name: \"diag/x/other.byn\")" "$list" "$tmp/exact.trx"
run exact-name-listed 1 "not run: $DX" "$list" "$tmp/exact.trx"

run no-trx 2 "" "$list"
echo "<TestRun" > "$tmp/bad.trx"
run malformed 2 "" "$list" "$tmp/bad.trx"
run missing-file 2 "" "$list" "$tmp/nope.trx"
trx "$tmp/empty.trx"
run zero-results 2 "" "$list" "$tmp/empty.trx"

# ci.yml wiring
ci="$root/.github/workflows/ci.yml"
grep -q "runner.os == 'Windows'" "$ci" || { echo "FAIL ci.yml: no Windows-only step"; exit 1; }
grep -A3 "known-failures.py" "$ci" | grep -q "tests/windows-known-failures.txt" \
  || { echo "FAIL ci.yml: script step lacks tests/windows-known-failures.txt"; exit 1; }
grep -B3 -A3 "runner.os != 'Windows'" "$ci" | grep -q "dotnet test Bayan.slnx -c Release$" \
  || { echo "FAIL ci.yml: non-Windows Test step changed"; exit 1; }

echo PASS
