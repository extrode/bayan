#!/usr/bin/env bash
# Compiler release packaging, run by .github/workflows/release.yml (decision 2026-11-02: GitHub Release per v* tag).
#   package.sh check-tag <tag>                  exit 0 when <tag> is v + Toolchain.CompilerVersion
#   package.sh pack <rid> <publish-dir> <out>   <out>/bayan-<version>-<target>.tar.gz (.zip for Windows), the binary
#                                               inside a bayan-<version>-<target>/ folder with README.md and LICENSE
#   package.sh sums <dir>                       <dir>/SHA256SUMS over every archive in <dir>
#   package.sh notes <version>                  the CHANGELOG.md section `## [<version>]`, heading excluded
# Targets use bayan's own names (bayan dist): osx-arm64 -> macos-arm64, win-x64 -> windows-x64.
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
die() { echo "package: $*" >&2; exit 1; }
version() { sed -nE 's/.*const string CompilerVersion = "([^"]+)";.*/\1/p' "$root/src/Bayan.Backend.C/Toolchain.cs"; }

case "${1:-}" in
  check-tag)
    tag="${2:?usage: package.sh check-tag <tag>}"; v="$(version)"
    [ -n "$v" ] || die "no CompilerVersion in src/Bayan.Backend.C/Toolchain.cs"
    [ "$tag" = "v$v" ] || die "tag $tag does not match CompilerVersion $v (expected v$v)"
    case "$v" in *-*) die "CompilerVersion $v is a prerelease; set the release version before tagging" ;; esac
    echo "$v" ;;
  pack)
    rid="${2:?usage: package.sh pack <rid> <publish-dir> <out>}"; pub="${3:?}"; out="${4:?}"
    case "$rid" in
      linux-x64|linux-arm64) target="$rid"; exe=bayan; ext=tar.gz ;;
      osx-x64|osx-arm64) target="macos-${rid#osx-}"; exe=bayan; ext=tar.gz ;;
      win-x64|win-arm64) target="windows-${rid#win-}"; exe=bayan.exe; ext=zip ;;
      *) die "unknown rid $rid" ;;
    esac
    [ -f "$pub/$exe" ] || die "$pub/$exe not found"
    v="$(version)"; name="bayan-$v-$target"
    mkdir -p "$out"; out="$(cd "$out" && pwd)"
    stage="$out/.stage-$target"; rm -rf "$stage"; mkdir -p "$stage/$name"
    cp "$pub/$exe" "$stage/$name/"
    for f in README.md LICENSE; do [ -f "$root/$f" ] && cp "$root/$f" "$stage/$name/"; done
    if [ "$ext" = zip ]; then
      # bsdtar writes zip: System32's on Windows runners, the system tar on macOS; else Info-ZIP.
      if [ -n "${SYSTEMROOT:-}" ] && [ -x "$SYSTEMROOT/System32/tar.exe" ]; then (cd "$stage" && "$SYSTEMROOT/System32/tar.exe" -a -cf "$out/$name.zip" "$name")
      elif tar --version 2>/dev/null | grep -q bsdtar; then (cd "$stage" && tar -a -cf "$out/$name.zip" "$name")
      else (cd "$stage" && zip -qr "$out/$name.zip" "$name"); fi
    else
      tar -czf "$out/$name.tar.gz" -C "$stage" "$name"
    fi
    rm -rf "$stage"
    echo "$out/$name.$ext" ;;
  sums)
    dir="${2:?usage: package.sh sums <dir>}"
    ( cd "$dir"
      set -- bayan-*.tar.gz bayan-*.zip
      files=(); for f in "$@"; do [ -f "$f" ] && files+=("$f"); done
      [ ${#files[@]} -gt 0 ] || die "no archives in $dir"
      if command -v sha256sum >/dev/null; then sha256sum "${files[@]}"; else shasum -a 256 "${files[@]}"; fi > SHA256SUMS )
    echo "$dir/SHA256SUMS" ;;
  notes)
    v="${2:?usage: package.sh notes <version>}"
    notes="$(awk -v h="## [$v]" 'index($0, h) == 1 { on = 1; next } on && /^## \[/ { exit } on' "$root/CHANGELOG.md")"
    [ -n "$(printf '%s' "$notes" | tr -d '[:space:]')" ] || die "CHANGELOG.md has no ## [$v] section"
    printf '%s\n' "$notes" ;;
  *) die "usage: package.sh check-tag|pack|sums|notes …" ;;
esac
