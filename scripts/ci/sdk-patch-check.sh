#!/usr/bin/env bash
# Monthly reminder (spec 015 D2): exits 1 when a newer patch of the pinned .NET SDK band is out, naming both versions,
# so the scheduled workflow fails and GitHub mails the owner. The pin itself stays exact (install-dotnet.sh).
# Usage: sdk-patch-check.sh   (tests only: SDK_RELEASES_JSON reads that file instead of the release metadata URL)
set -euo pipefail
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
pinned="$(sed -nE 's/^version=([0-9.]+).*/\1/p' "$root/scripts/ci/install-dotnet.sh")"
[ -n "$pinned" ] || { echo "sdk-patch-check: no version= line in scripts/ci/install-dotnet.sh" >&2; exit 2; }
channel="${pinned%.*}"
url="https://builds.dotnet.microsoft.com/dotnet/release-metadata/$channel/releases.json"
if [ -n "${SDK_RELEASES_JSON:-}" ]; then meta="$(cat "$SDK_RELEASES_JSON")"; else meta="$(curl -fsSL --retry 3 "$url")"; fi
latest="$(sed -nE 's/^ *"latest-sdk": *"([0-9.]+)".*/\1/p' <<< "$meta" | head -1)"
[ -n "$latest" ] || { echo "sdk-patch-check: no latest-sdk in $url" >&2; exit 2; }
if [ "$latest" = "$pinned" ]; then echo "sdk-patch-check: SDK $pinned is the latest $channel"; exit 0; fi
echo "sdk-patch-check: SDK $latest is out (pinned $pinned). Bump version= and the three sums in scripts/ci/install-dotnet.sh from $url" >&2
exit 1
