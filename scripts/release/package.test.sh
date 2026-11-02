#!/usr/bin/env bash
# package.sh on a fixture repo: tag check, archive names and layout per rid, SHA256SUMS, CHANGELOG notes, refusals.
set -euo pipefail
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
t="$root/tmp/release-package-test"
rm -rf "$t" && mkdir -p "$t/repo/scripts/release" "$t/repo/src/Bayan.Backend.C"
trap 'rm -rf "$t"' EXIT
cp "$root/scripts/release/package.sh" "$t/repo/scripts/release/"
s="$t/repo/scripts/release/package.sh"
fail() { echo "FAIL: $1" >&2; exit 1; }
setv() { printf '    public const string CompilerVersion = "%s";\n' "$1" > "$t/repo/src/Bayan.Backend.C/Toolchain.cs"; }
setv 1.2.3
echo readme > "$t/repo/README.md"
printf '# Changelog\n\n## [Unreleased]\n\n## [1.2.3] - 2026-11-02\n\n### Added\n- one\n\n## [1.2.2]\n- old\n' > "$t/repo/CHANGELOG.md"

[ "$(bash "$s" check-tag v1.2.3)" = 1.2.3 ] || fail "matching tag refused"
out="$(bash "$s" check-tag v1.2.4 2>&1)" && fail "wrong tag accepted"
echo "$out" | grep -q "does not match CompilerVersion 1.2.3" || fail "no mismatch message: $out"
setv 1.3.0-dev
out="$(bash "$s" check-tag v1.3.0-dev 2>&1)" && fail "prerelease version accepted"
echo "$out" | grep -q "is a prerelease" || fail "no prerelease message: $out"
setv 1.2.3

mkdir -p "$t/pub/unix" "$t/pub/win"
printf 'bin' > "$t/pub/unix/bayan"; printf 'exe' > "$t/pub/win/bayan.exe"
bash "$s" pack osx-arm64 "$t/pub/unix" "$t/out" >/dev/null
bash "$s" pack linux-x64 "$t/pub/unix" "$t/out" >/dev/null
bash "$s" pack win-arm64 "$t/pub/win" "$t/out" >/dev/null
[ -f "$t/out/bayan-1.2.3-macos-arm64.tar.gz" ] && [ -f "$t/out/bayan-1.2.3-linux-x64.tar.gz" ] || fail "tar.gz names wrong: $(ls "$t/out")"
[ -f "$t/out/bayan-1.2.3-windows-arm64.zip" ] || fail "zip name wrong: $(ls "$t/out")"
[ "$(tar -tzf "$t/out/bayan-1.2.3-macos-arm64.tar.gz" | LC_ALL=C sort | tr '\n' ' ')" = "bayan-1.2.3-macos-arm64/ bayan-1.2.3-macos-arm64/README.md bayan-1.2.3-macos-arm64/bayan " ] \
  || fail "tar layout: $(tar -tzf "$t/out/bayan-1.2.3-macos-arm64.tar.gz")"
unzip -l "$t/out/bayan-1.2.3-windows-arm64.zip" | grep -q "bayan-1.2.3-windows-arm64/bayan.exe" || fail "zip lacks the exe"
[ -z "$(ls -A "$t/out" | grep '^\.stage' || true)" ] || fail "staging left behind"
out="$(bash "$s" pack win-x64 "$t/pub/unix" "$t/out" 2>&1)" && fail "missing exe accepted"
echo "$out" | grep -q "bayan.exe not found" || fail "no missing-exe message: $out"
out="$(bash "$s" pack freebsd-x64 "$t/pub/unix" "$t/out" 2>&1)" && fail "unknown rid accepted"

bash "$s" sums "$t/out" >/dev/null
[ "$(wc -l < "$t/out/SHA256SUMS" | tr -d ' ')" = 3 ] || fail "SHA256SUMS lines: $(cat "$t/out/SHA256SUMS")"
(cd "$t/out" && if command -v sha256sum >/dev/null; then sha256sum -c --quiet SHA256SUMS; else shasum -a 256 -c --quiet SHA256SUMS; fi) || fail "sums do not verify"
mkdir -p "$t/empty"; bash "$s" sums "$t/empty" 2>/dev/null && fail "sums over no archives accepted"

[ "$(bash "$s" notes 1.2.3)" = "$(printf '\n### Added\n- one\n')" ] || fail "notes: $(bash "$s" notes 1.2.3)"
bash "$s" notes 9.9.9 2>/dev/null && fail "missing CHANGELOG section accepted"
echo PASS
