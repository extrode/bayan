#!/usr/bin/env bash
# Installs the pinned .NET SDK into DEST and prints DEST (the dotnet root); checks the archive's sha512 first.
# Replaces actions/setup-dotnet (spec 015 handoff, T4), which resolved the newest 10.0 SDK on each run.
# Usage: install-dotnet.sh DEST   (tests only: DOTNET_BASE_URL replaces https://builds.dotnet.microsoft.com/dotnet/Sdk, DOTNET_SHA512 the pinned sum)
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
version=10.0.403  # the SDK the sums below belong to
wanted="$(sed -nE 's/.*"version": *"([0-9]+\.[0-9]+)\..*/\1/p' "$root/global.json")"
if [ "${version%.*}" != "$wanted" ]; then
  echo "install-dotnet: global.json asks for $wanted.x but the sums here are for $version; copy a $wanted SDK's from https://builds.dotnet.microsoft.com/dotnet/release-metadata/$wanted/releases.json" >&2
  exit 1
fi
dest="${1:?usage: install-dotnet.sh DEST}"
base="${DOTNET_BASE_URL:-https://builds.dotnet.microsoft.com/dotnet/Sdk}"

# From https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json, SDK 10.0.403.
case "$(uname -s)-$(uname -m)" in
  Linux-x86_64)                  rid=linux-x64; ext=tar.gz; sum=b7b3d8461cf98b07d2552ce9199702fa3ad010ac2671a5f8cd5136f7534e50f6855ba7bbb02b11494563f1d04156276a31cda1103b900e5eecd0d9473dc7b9c5 ;;
  Darwin-arm64)                  rid=osx-arm64; ext=tar.gz; sum=5893c66b48ea3674c7da18501059d606e96f3672fabe4d5e41762e692c7c83df3ee0b2b9dc5a0786191da2dc413d7335158101c14c935ab217f31ac2529770f3 ;;
  Linux-aarch64)                 rid=linux-arm64; ext=tar.gz; sum=0966aa2b70f4677b8319be9df447a78a0dcd6164836425b55a68fd714f4023feb87b4cac21b9a996b79472ad54487cd0adcd2a9dfd570e2b2a1d26423a2baa9b ;;
  Darwin-x86_64)                 rid=osx-x64;   ext=tar.gz; sum=ce95d225b86d2401f111c2aa6df0f5e6855c375b7b34ab4a73df90c56f2b0fc2a44398cfe455cb689670bc8590012e943b64c7a590122af98cdc067721b12223 ;;
  MINGW64*-x86_64|MSYS*-x86_64)  rid=win-x64;   ext=zip;    sum=7a114c318fa7f1d9822991b534b2814d0e78386ceda816b371f5141acefe741d87ccb606ede06cadac8f9ac5070e32b708a67bbd1619ff5e5a7858a27874ef48 ;;
  MINGW*-aarch64|MSYS*-aarch64)  rid=win-arm64; ext=zip;    sum=f78ecd8b48e24fd72ee604687b75f1bbb81505e8343f7d52e4baf9ea32372557eb30e88facaaad195bf2646fcce117fd941e49da0d5bdee1d60d63d836968429 ;;
  *) echo "install-dotnet: no pinned SDK $version for $(uname -s)-$(uname -m)" >&2; exit 1 ;;
esac
name="dotnet-sdk-$version-$rid"
sum="${DOTNET_SHA512:-$sum}"

mkdir -p "$dest"
archive="$dest/$name.$ext"
curl -fsSL --retry 3 -o "$archive" "$base/$version/$name.$ext"
if command -v sha512sum >/dev/null; then got="$(sha512sum < "$archive" | cut -d' ' -f1)"; else got="$(shasum -a 512 < "$archive" | cut -d' ' -f1)"; fi # stdin: see install-zig.sh
if [ "$got" != "$sum" ]; then
  echo "install-dotnet: sha512 mismatch for $name.$ext: got $got, want $sum" >&2
  rm -f "$archive"; exit 1
fi
case "$ext" in
  zip) unzip -q -o "$archive" -d "$dest" ;;
  *)   tar -xzf "$archive" -C "$dest" ;;
esac
rm -f "$archive"
"$dest/dotnet" --version >&2
echo "$dest"
