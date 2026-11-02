#!/usr/bin/env bash
# Every tool's recorded cases still hold against the original script (so the baselines stay true while it lives), and a
# command that behaves differently is caught.
set -euo pipefail
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
fail() { echo "FAIL: $1" >&2; exit 1; }
for t in "$root"/tests/parity/*/; do
  bash "$root/scripts/parity/run.sh" "$(basename "$t")" > /dev/null || fail "$(basename "$t") no longer matches its baseline: $(bash "$root/scripts/parity/run.sh" "$(basename "$t")" | head -3)"
done
if out=$(bash "$root/scripts/parity/run.sh" mutation-exempt --impl "printf skip"); then fail "a different implementation passed"; fi
grep -q 'FAIL mutation-exempt/spans: stdout differs' <<< "$out" || fail "no stdout mismatch reported: $out"
if bash "$root/scripts/parity/run.sh" no-such-tool 2> /dev/null; then fail "an unknown tool passed"; fi
echo PASS
