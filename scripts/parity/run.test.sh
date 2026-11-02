#!/usr/bin/env bash
# Every tool's recorded cases still hold against the original script (so the baselines stay true while it lives), and a
# command that behaves differently is caught.
set -euo pipefail
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
fail() { echo "FAIL: $1" >&2; exit 1; }
# A tool whose interpreter is not installed (CI runners have no deno) is skipped, not failed.
needs() { read -r cmd _ < "$1/impl"; command -v "$cmd" > /dev/null; }
for t in "$root"/tests/parity/*/; do
  needs "$t" || { echo "SKIP $(basename "$t"): $(read -r c _ < "$t/impl"; echo "$c") not installed"; continue; }
  bash "$root/scripts/parity/run.sh" "$(basename "$t")" > /dev/null || fail "$(basename "$t") no longer matches its baseline: $(bash "$root/scripts/parity/run.sh" "$(basename "$t")" | head -3)"
done
if PATH=/nonexistent needs "$root/tests/parity/plan-strings-check"; then fail "a missing interpreter was found"; fi
if out=$(bash "$root/scripts/parity/run.sh" mutation-exempt --impl "printf skip"); then fail "a different implementation passed"; fi
grep -q 'FAIL mutation-exempt/spans: stdout differs' <<< "$out" || fail "no stdout mismatch reported: $out"
# A left-over file and a changed file both count; an empty directory does not.
t="$root/tmp/parity-test"; mkdir -p "$t"
printf '#!/bin/sh\nmkdir -p empty-dir\necho %s > "$1"\n' "$(printf 'x')" > "$t/impl.sh"
if out=$(bash "$root/scripts/parity/run.sh" mutation-exempt --impl "bash $t/impl.sh" spans); then fail "a changed input file passed"; fi
grep -q 'FAIL mutation-exempt/spans: files differ' <<< "$out" || fail "no file mismatch reported: $out"
printf '#!/bin/sh\ntouch extra\n' > "$t/impl.sh"
if out=$(bash "$root/scripts/parity/run.sh" mutation-exempt --impl "bash $t/impl.sh" spans); then fail "an extra file passed"; fi
grep -q 'FAIL mutation-exempt/spans: files differ' <<< "$out" || fail "no extra-file mismatch reported: $out"
printf '#!/bin/sh\nmkdir -p empty-dir\n' > "$t/impl.sh"
out=$(bash "$root/scripts/parity/run.sh" mutation-exempt --impl "bash $t/impl.sh" spans || true)
! grep -q 'files differ' <<< "$out" || fail "an empty directory counted as a file change: $out"
if bash "$root/scripts/parity/run.sh" no-such-tool 2> /dev/null; then fail "an unknown tool passed"; fi
echo PASS
