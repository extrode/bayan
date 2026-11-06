#!/usr/bin/env bash
# Tests scripts/release/memory-gate.sh: it runs every command of docs/memory-leaks.md's *Release gate* block, reports
# each failure and exits 1 on any, and release.yml cannot draft a release without it passing.
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
gate="$root/scripts/release/memory-gate.sh"
doc="$root/docs/memory-leaks.md"
wf="$root/.github/workflows/release.yml"
tmp="$root/tmp/memory-gate-test"
rm -rf "$tmp"; mkdir -p "$tmp/bin"
trap 'rm -rf "$tmp"' EXIT
fail() { echo "FAIL: $1" >&2; exit 1; }

# The doc's gate block, comments dropped, is the list the script must run.
awk '/^## Release gate/{s=1} s&&/^```sh/{b=1;next} b&&/^```/{exit} b' "$doc" | sed -E 's/[[:space:]]+#.*$//; s/[[:space:]]+$//' \
  | grep -v '^$' > "$tmp/doc-cmds"
[ -s "$tmp/doc-cmds" ] || fail "no command block under '## Release gate' in docs/memory-leaks.md"
while IFS= read -r c; do
  grep -qF "run $c" "$gate" || fail "memory-gate.sh does not run the documented gate command: $c"
done < "$tmp/doc-cmds"
[ "$(grep -c '^run ' "$gate")" = "$(wc -l < "$tmp/doc-cmds" | tr -d ' ')" ] || fail "memory-gate.sh runs commands the doc's gate block does not list"
grep -q 'scripts/release/memory-gate.sh' "$doc" || fail "docs/memory-leaks.md does not name scripts/release/memory-gate.sh"

# Stub dotnet and a scratch copy of the script tree: all pass, then one fails.
cat > "$tmp/bin/dotnet" <<'STUB'
#!/bin/bash
echo "dotnet $*" >> "$LOG"
case "$*" in *"$FAIL_ON"*) [ -n "$FAIL_ON" ] && exit 1 ;; esac
exit 0
STUB
chmod +x "$tmp/bin/dotnet"
w="$tmp/w"; mkdir -p "$w/scripts/release"; cp "$gate" "$w/scripts/release/"
printf '#!/bin/bash\necho pinned >> "$LOG"\n[ "$FAIL_ON" = pinned ] && exit 1\nexit 0\n' > "$w/scripts/pinned-bayan.test.sh"
export LOG="$tmp/log" PATH="$tmp/bin:$PATH"

: > "$LOG"
FAIL_ON= bash "$w/scripts/release/memory-gate.sh" > "$tmp/out" 2> "$tmp/err" || fail "an all-pass gate exited non-zero: $(cat "$tmp/err")"
grep -qx "memory-gate: PASS" "$tmp/out" || fail "an all-pass gate printed no PASS"
[ "$(wc -l < "$LOG" | tr -d ' ')" = 3 ] || fail "the gate did not run all 3 commands: $(cat "$LOG")"

: > "$LOG"
if FAIL_ON=CliMemoryBounds bash "$w/scripts/release/memory-gate.sh" > "$tmp/out" 2> "$tmp/err"; then fail "a failing CliMemoryBounds run passed the gate"; fi
grep -q "FAILED: dotnet test -v q --filter FullyQualifiedName~CliMemoryBounds" "$tmp/err" || fail "the failing command was not named: $(cat "$tmp/err")"
[ "$(wc -l < "$LOG" | tr -d ' ')" = 3 ] || fail "a failure stopped the remaining gate commands: $(cat "$LOG")"
! grep -q PASS "$tmp/out" || fail "a failing gate printed PASS"

: > "$LOG"
if FAIL_ON=pinned bash "$w/scripts/release/memory-gate.sh" > /dev/null 2> "$tmp/err"; then fail "a failing pinned-bayan test passed the gate"; fi
grep -q "FAILED: bash scripts/pinned-bayan.test.sh" "$tmp/err" || fail "the pinned-bayan failure was not named"

# Wiring: release.yml runs the gate in its own job and the draft release needs it.
grep -q 'bash scripts/release/memory-gate.sh' "$wf" || fail "release.yml does not run memory-gate.sh"
awk '/^  release:/{r=1} r&&/needs:/{print; exit}' "$wf" | grep -q 'memory-gate' || fail "release.yml's release job does not need memory-gate"
grep -q '^  memory-gate:' "$wf" || fail "release.yml has no memory-gate job"
echo PASS
