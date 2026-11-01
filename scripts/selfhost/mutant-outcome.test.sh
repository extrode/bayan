#!/bin/bash
# mutant_outcome classifies a test run of one mutant: passing tests = survived, a Bayan build failure = uncompiled, exit 124 = timeout, else killed.
set -e
cd "$(dirname "$0")/../.."
. scripts/selfhost/mutant-outcome.sh
d=tmp/mutant-outcome-test; mkdir -p $d
printf 'Passed!  - Failed: 0\n' > $d/pass.txt
printf '  Failed Bayan.Tests.X [1 s]\n  Error Message:\n   selfhost/semantics with the generated main did not build:\nerror BYN1005\n' > $d/nobuild.txt
printf '  Failed Bayan.Tests.X [1 s]\n  Error Message:\n   body f: line 2\n expected: a\n   actual: b\n' > $d/fail.txt
[ "$(mutant_outcome 0 $d/pass.txt)" = survived ] || { echo "FAIL: passing tests are not survived"; exit 1; }
[ "$(mutant_outcome 1 $d/nobuild.txt)" = uncompiled ] || { echo "FAIL: build failure counted as killed"; exit 1; }
[ "$(mutant_outcome 1 $d/fail.txt)" = killed ] || { echo "FAIL: a failing assertion is not killed"; exit 1; }
[ "$(mutant_outcome 124 $d/pass.txt)" = timeout ] || { echo "FAIL: exit 124 is not timeout"; exit 1; }
rm -rf $d
echo PASS
