#!/usr/bin/env bash
# scripts/dist.sh with a fake dotnet: layout per target and version, latest replaced on each build, --from import, refusals.
set -uo pipefail
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
t="$root/tmp/dist-test-$$"
mkdir -p "$t/bin"
trap 'rm -rf "$t"' EXIT
fail=0
check() { if [ "$2" = "$3" ]; then echo "ok: $1"; else echo "FAIL: $1: got '$2', want '$3'"; fail=1; fi; }
d="$t/dist"
# fake dotnet: `dotnet publish <proj> -c Release -r <rid> -o <dir>` writes a binary naming its rid and FAKE_BUILD
cat > "$t/bin/dotnet" <<'FAKE'
#!/usr/bin/env bash
while [ $# -gt 0 ]; do case "$1" in -r) rid=$2; shift 2 ;; -o) out=$2; shift 2 ;; *) shift ;; esac; done
[ "${FAKE_FAIL:-}" = 1 ] && exit 1
mkdir -p "$out"; exe=bayan; case "$rid" in win-*) exe=bayan.exe ;; esac
echo "$rid ${FAKE_BUILD:-1}" > "$out/$exe"
FAKE
chmod +x "$t/bin/dotnet"
run() { PATH="$t/bin:$PATH" bash "$root/scripts/dist.sh" --dist "$d" "$@"; }

out=$(FAKE_BUILD=1 run --rid osx-arm64 --label 1.0.0 2>/dev/null); check "first build exits 0" "$?" 0
check "prints the binary path" "$out" "$d/macos-arm64/1.0.0/bayan"
check "version dir binary" "$(cat "$d/macos-arm64/1.0.0/bayan")" "osx-arm64 1"
check "latest binary" "$(cat "$d/macos-arm64/latest/bayan")" "osx-arm64 1"
check "binary executable" "$([ -x "$d/macos-arm64/1.0.0/bayan" ] && echo y)" y
check "BUILD-INFO version" "$(grep '^version ' "$d/macos-arm64/latest/BUILD-INFO")" "version 1.0.0"
check "BUILD-INFO rid" "$(grep '^rid ' "$d/macos-arm64/1.0.0/BUILD-INFO")" "rid osx-arm64"

FAKE_BUILD=2 run --rid osx-arm64 --label 1.1.0 >/dev/null 2>&1
check "older version kept" "$(cat "$d/macos-arm64/1.0.0/bayan")" "osx-arm64 1"
check "new version dir" "$(cat "$d/macos-arm64/1.1.0/bayan")" "osx-arm64 2"
check "latest replaced by the newest build" "$(cat "$d/macos-arm64/latest/bayan")" "osx-arm64 2"
check "latest BUILD-INFO replaced" "$(grep '^version ' "$d/macos-arm64/latest/BUILD-INFO")" "version 1.1.0"
check "no staging dir left" "$(ls -A "$d/macos-arm64" | tr '\n' ' ')" "1.0.0 1.1.0 latest "

FAKE_BUILD=3 run --rid win-x64 --rid linux-arm64 --label 1.1.0 >/dev/null 2>&1
check "windows target name and exe" "$(cat "$d/windows-x64/latest/bayan.exe")" "win-x64 3"
check "linux target" "$(cat "$d/linux-arm64/1.1.0/bayan")" "linux-arm64 3"
check "other targets untouched" "$(cat "$d/macos-arm64/latest/bayan")" "osx-arm64 2"

mkdir -p "$t/pub"; echo imported > "$t/pub/bayan.exe"
run --from "$t/pub" --rid win-x64 --label 1.2.0 >/dev/null 2>&1; check "--from exits 0" "$?" 0
check "--from imported" "$(cat "$d/windows-x64/latest/bayan.exe")" imported
check "--from source recorded" "$(grep '^source ' "$d/windows-x64/1.2.0/BUILD-INFO")" "source $t/pub"

FAKE_FAIL=1 run --rid osx-arm64 --label 2.0.0 >/dev/null 2>&1; check "failed publish exits 1" "$?" 1
check "failed publish leaves latest" "$(cat "$d/macos-arm64/latest/bayan")" "osx-arm64 2"
check "failed publish makes no version dir" "$([ -e "$d/macos-arm64/2.0.0" ] && echo y || echo n)" n

err=$(run --rid sparc-x64 --label 1.0.0 2>&1); check "unknown rid exits 1" "$?" 1
check "unknown rid message" "$err" "dist: unknown rid sparc-x64"
run --rid osx-arm64 --label latest >/dev/null 2>&1; check "label 'latest' refused" "$?" 1
run --rid osx-arm64 --label ../x >/dev/null 2>&1; check "label with / refused" "$?" 1
run --from "$t/pub" >/dev/null 2>&1; check "--from without --rid refused" "$?" 1
run --bogus >/dev/null 2>&1; check "unknown argument refused" "$?" 1

lbl=$(FAKE_BUILD=4 run 2>/dev/null); check "host build exits 0" "$?" 0
check "default label is CompilerVersion(+commit)(.dirty)" "$(echo "$lbl" | grep -cE '/[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.]+\+[0-9a-f]{12})?(\.dirty)?/bayan(\.exe)?$')" 1
exit $fail
