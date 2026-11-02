#!/usr/bin/env bash
# Tests scripts/ci/install-dotnet.sh against a fake download, and the shell checkout/SDK steps in .github/workflows/.
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
script="$root/scripts/ci/install-dotnet.sh"
tmp="$root/tmp/install-dotnet-test"
rm -rf "$tmp"
mkdir -p "$tmp"
trap 'rm -rf "$tmp"' EXIT
fail() { echo "FAIL: $1" >&2; exit 1; }
sha() { if command -v sha512sum >/dev/null; then sha512sum < "$1" | cut -d' ' -f1; else shasum -a 512 < "$1" | cut -d' ' -f1; fi; }

case "$(uname -s)-$(uname -m)" in
  Linux-x86_64) rid=linux-x64 ;;
  Darwin-arm64) rid=osx-arm64 ;;
  *) echo "SKIP: no fake archive for $(uname -s)-$(uname -m)"; exit 0 ;;
esac
name="dotnet-sdk-10.0.403-$rid"

# A fake SDK: a tarball whose dotnet prints its version, unpacked at the archive root like the real one.
mkdir -p "$tmp/src" "$tmp/site/10.0.403"
printf '#!/bin/sh\necho 10.0.403\n' > "$tmp/src/dotnet"; chmod +x "$tmp/src/dotnet"
tar -czf "$tmp/site/10.0.403/$name.tar.gz" -C "$tmp/src" dotnet
good="$(sha "$tmp/site/10.0.403/$name.tar.gz")"

# The pinned sum does not match the fake archive: refused, archive removed, nothing unpacked.
if out="$(DOTNET_BASE_URL="file://$tmp/site" "$script" "$tmp/bad" 2>&1)"; then fail "a mismatched archive was installed"; fi
echo "$out" | grep -q "sha512 mismatch for $name.tar.gz" || fail "no mismatch message: $out"
[ ! -e "$tmp/bad/$name.tar.gz" ] && [ ! -e "$tmp/bad/dotnet" ] || fail "a mismatched archive was left or unpacked"

# The matching sum: unpacked, its dotnet runs, stdout is the dotnet root only.
dir="$(DOTNET_BASE_URL="file://$tmp/site" DOTNET_SHA512="$good" "$script" "$tmp/good" 2>"$tmp/err")" || fail "matching archive refused: $(cat "$tmp/err")"
[ "$dir" = "$tmp/good" ] || fail "stdout was '$dir'"
[ "$("$dir/dotnet" --version)" = 10.0.403 ] || fail "installed dotnet does not run"
grep -qx 10.0.403 "$tmp/err" || fail "dotnet version not reported on stderr"
[ ! -e "$tmp/good/$name.tar.gz" ] || fail "archive not removed after unpacking"

# A destination path holding a backslash, as Windows runners' RUNNER_TEMP does.
DOTNET_BASE_URL="file://$tmp/site" DOTNET_SHA512="$good" "$script" "$tmp/back\\slash" >/dev/null 2>"$tmp/err" || fail "backslash path refused: $(cat "$tmp/err")"

# A host with no pinned SDK is refused by name.
mkdir -p "$tmp/shim"; printf '#!/bin/sh\ncase "$1" in -s) echo Plan9;; -m) echo mips;; esac\n' > "$tmp/shim/uname"; chmod +x "$tmp/shim/uname"
if out="$(PATH="$tmp/shim:$PATH" "$script" "$tmp/none" 2>&1)"; then fail "an unknown host was accepted"; fi
echo "$out" | grep -q "no pinned SDK 10.0.403 for Plan9-mips" || fail "no unknown-host message: $out"

# DOTNET_RID picks another target's archive (release.yml on windows-11-arm); an unknown one is refused by name.
cp "$tmp/site/10.0.403/$name.tar.gz" "$tmp/site/10.0.403/dotnet-sdk-10.0.403-linux-arm64.tar.gz"
DOTNET_RID=linux-arm64 DOTNET_BASE_URL="file://$tmp/site" DOTNET_SHA512="$good" "$script" "$tmp/rid" >/dev/null 2>"$tmp/err" || fail "DOTNET_RID archive refused: $(cat "$tmp/err")"
[ -x "$tmp/rid/dotnet" ] || fail "DOTNET_RID archive not unpacked"
rm -f "$tmp/site/10.0.403/dotnet-sdk-10.0.403-linux-arm64.tar.gz"
if out="$(DOTNET_RID=linux-arm64 DOTNET_BASE_URL="file://$tmp/site" DOTNET_SHA512="$good" "$script" "$tmp/rid2" 2>&1)"; then fail "DOTNET_RID ignored: the host archive was installed"; fi
if out="$(DOTNET_RID=plan9-x64 "$script" "$tmp/rid3" 2>&1)"; then fail "an unknown DOTNET_RID was accepted"; fi
echo "$out" | grep -q "no pinned SDK 10.0.403 for plan9-x64" || fail "no unknown-rid message: $out"

# A global.json for another release line is refused before anything is downloaded.
mkdir -p "$tmp/repo/scripts/ci"; cp "$script" "$tmp/repo/scripts/ci/"
printf '{\n  "sdk": {\n    "version": "11.0.100",\n    "rollForward": "latestFeature"\n  }\n}\n' > "$tmp/repo/global.json"
if out="$(DOTNET_BASE_URL="file://$tmp/site" "$tmp/repo/scripts/ci/install-dotnet.sh" "$tmp/other" 2>&1)"; then fail "a global.json without sums was installed"; fi
echo "$out" | grep -q "global.json asks for 11.0.x but the sums here are for 10.0.403" || fail "no version-mismatch message: $out"
[ ! -e "$tmp/other" ] || fail "something was downloaded for an unpinned release line"

# Wiring: checkout and the SDK are shell steps; only the artifact actions remain.
# Every release target has a pinned 128-hex sum.
for r in linux-x64 linux-arm64 osx-arm64 osx-x64 win-x64 win-arm64; do
  grep -qE "^  $r\) +ext=[a-z.]+; +sum=[0-9a-f]{128} ;;" "$script" || fail "no pinned sum for $r"
done

for wf in "$root/.github/workflows/ci.yml" "$root/.github/workflows/mutation.yml" "$root/.github/workflows/release.yml"; do
  ! grep -qE "uses: actions/(checkout|setup-dotnet)@" "$wf" || fail "$wf still uses actions/checkout or actions/setup-dotnet"
  grep -q 'git fetch .*"\$GITHUB_SHA"' "$wf" || fail "$wf does not fetch GITHUB_SHA"
  ! grep -E "uses: " "$wf" | grep -vqE "uses: actions/(upload|download)-artifact@" || fail "$wf uses an action other than the artifact ones"
done
grep -q "scripts/ci/install-dotnet.sh" "$root/.github/workflows/ci.yml" || fail "ci.yml does not run install-dotnet.sh"
grep -q "scripts/ci/install-dotnet.sh" "$root/.github/workflows/mutation.yml" || fail "mutation.yml does not run install-dotnet.sh"
echo PASS
