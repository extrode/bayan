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
# Only a whole path is rewritten: the work directory followed by more name characters stays as it is.
printf '#!/bin/sh\necho "$PWD/a ${PWD}b"\n' > "$t/impl.sh"
bash "$root/scripts/parity/run.sh" mutation-exempt --impl "bash $t/impl.sh" spans > /dev/null || true
got=$(cat "$root/tmp/parity/mutation-exempt/spans/got/stdout")
[ "$got" = "\$WORK/a \$ROOT/tmp/parity/mutation-exempt/spans/workb" ] || fail "path rewrite went past a path boundary: $got"
# Traceback caret lines (Python 3.11+ only) are dropped from stderr; other stderr lines stay.
printf '#!/bin/sh\nprintf "  x = 1\\n      ^^^^^\\n   ~~~^^\\nE: ^ not alone\\n" >&2\n' > "$t/impl.sh"
bash "$root/scripts/parity/run.sh" mutation-exempt --impl "bash $t/impl.sh" spans > /dev/null || true
got=$(cat "$root/tmp/parity/mutation-exempt/spans/got/stderr")
[ "$got" = "$(printf '  x = 1\nE: ^ not alone')" ] || fail "caret lines not dropped from stderr: $got"
# A case or tool name that leaves its directory is refused before anything is removed.
out=$(bash "$root/scripts/parity/run.sh" mutation-exempt ..) && fail "case .. passed"
grep -q 'bad case name' <<< "$out" || fail "case .. was not refused: $out"
out=$(bash "$root/scripts/parity/run.sh" ../parity 2>&1) && fail "tool ../parity passed"
grep -q 'bad tool name' <<< "$out" || fail "tool ../parity was not refused: $out"
# --record reports a case it could not record.
if bash "$root/scripts/parity/run.sh" mutation-exempt --record no-such-case > /dev/null; then fail "--record of a missing case exited 0"; fi
echo PASS
