#!/usr/bin/env bash
# Installs the pinned zig into DEST and prints its bin directory; checks the archive's sha256 first.
# Replaces mlugg/setup-zig, whose latest release (v2.2.1) still runs on Node 20.
# Usage: install-zig.sh DEST   (tests only: ZIG_BASE_URL replaces https://ziglang.org/download, ZIG_SHA256 the pinned sum)
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
sums_for=0.17.0  # the release the sums below belong to
version="$(tr -d '[:space:]' < "$root/.zig-version")"
if [ "$version" != "$sums_for" ]; then
  echo "install-zig: .zig-version is $version but the sums here are for $sums_for; copy $version's from https://ziglang.org/download/index.json" >&2
  exit 1
fi
dest="${1:?usage: install-zig.sh DEST}"
base="${ZIG_BASE_URL:-https://ziglang.org/download}"

# From https://ziglang.org/download/index.json, release 0.17.0.
case "$(uname -s)-$(uname -m)" in
  Linux-x86_64)                   target=x86_64-linux;   ext=tar.xz; sum=1cbe9df9f27e6b78d14ccbca43b6703a404ef79ef1c463de901d7f088d4e2026 ;;
  Darwin-arm64)                   target=aarch64-macos;  ext=tar.xz; sum=b607e9b9234790a008116ae5bdb71c6243b84b9fb42a53a9e70fde41c06c536a ;;
  MINGW64*-x86_64|MSYS*-x86_64)   target=x86_64-windows; ext=zip;    sum=b5663f69581dcf391293fbf16c06cb80d81d806545ce618b4d0bab7f0eb8c428 ;;
  *) echo "install-zig: no pinned zig $version for $(uname -s)-$(uname -m)" >&2; exit 1 ;;
esac
name="zig-$target-$version"
sum="${ZIG_SHA256:-$sum}"

mkdir -p "$dest"
archive="$dest/$name.$ext"
curl -fsSL --retry 3 -o "$archive" "$base/$version/$name.$ext"
if command -v sha256sum >/dev/null; then got="$(sha256sum < "$archive" | cut -d' ' -f1)"; else got="$(shasum -a 256 < "$archive" | cut -d' ' -f1)"; fi # stdin: GNU sha256sum prefixes the sum with \ when the path holds one (Windows runners)
if [ "$got" != "$sum" ]; then
  echo "install-zig: sha256 mismatch for $name.$ext: got $got, want $sum" >&2
  rm -f "$archive"; exit 1
fi
case "$ext" in
  zip) unzip -q -o "$archive" -d "$dest" ;;
  *)   tar -xJf "$archive" -C "$dest" ;;
esac
rm -f "$archive"
"$dest/$name/zig" version >&2
echo "$dest/$name"
