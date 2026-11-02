#!/usr/bin/env bash
set -euo pipefail
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
t="$root/tmp/sdk-patch-check-test"; rm -rf "$t"; mkdir -p "$t"; trap 'rm -rf "$t"' EXIT
fail() { echo "FAIL: $1" >&2; exit 1; }
pinned="$(sed -nE 's/^version=([0-9.]+).*/\1/p' "$root/scripts/ci/install-dotnet.sh")"
printf '{\n  "channel-version": "10.0",\n  "latest-sdk": "%s",\n  "releases": []\n}\n' "$pinned" > "$t/same.json"
printf '{\n  "latest-sdk": "10.0.999",\n  "releases": [ { "sdk": { "version": "%s" } } ]\n}\n' "$pinned" > "$t/newer.json"
printf '{ "releases": [] }\n' > "$t/none.json"
SDK_RELEASES_JSON="$t/same.json" bash "$root/scripts/ci/sdk-patch-check.sh" > "$t/out" || fail "the pinned SDK being latest failed"
grep -q "SDK $pinned is the latest" "$t/out" || fail "no up-to-date line"
if SDK_RELEASES_JSON="$t/newer.json" bash "$root/scripts/ci/sdk-patch-check.sh" 2> "$t/err"; then fail "a newer SDK passed"; fi
grep -q "SDK 10.0.999 is out (pinned $pinned)" "$t/err" || fail "no bump message: $(cat "$t/err")"
SDK_RELEASES_JSON="$t/none.json" bash "$root/scripts/ci/sdk-patch-check.sh" 2> "$t/err" && fail "missing latest-sdk passed"
grep -q "no latest-sdk" "$t/err" || fail "no missing-field message"
echo PASS
