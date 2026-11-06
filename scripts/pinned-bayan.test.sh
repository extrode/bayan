#!/usr/bin/env bash
# Tests scripts/pinned-bayan.sh on a scratch repo with a stub `dotnet` whose "build" writes a bayan that prints the
# commit's src/Bayan.Cli/stamp.
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
script="$root/scripts/pinned-bayan.sh"
tmp="$root/tmp/pinned-bayan-test"
rm -rf "$tmp"
mkdir -p "$tmp/aux"
trap 'rm -rf "$tmp"' EXIT
fail() { echo "FAIL: $1" >&2; exit 1; }

cat > "$tmp/aux/dotnet" <<'STUB'
#!/bin/bash
echo build >> "$AUX/builds.txt"
proj=$2; while [ $# -gt 0 ]; do [ "$1" = -o ] && out=$2; shift; done
[ -f "$proj/broken" ] && { echo "error CS0001: stub failure"; exit 1; }
git -C "$proj" rev-parse HEAD > "$AUX/commit.txt"
[ -n "${RACE_CACHE:-}" ] && { mkdir -p "$RACE_CACHE/bin"; printf '#!/bin/sh\necho winner\n' > "$RACE_CACHE/bin/bayan"; chmod +x "$RACE_CACHE/bin/bayan"; }
mkdir -p "$out"; printf '#!/bin/sh\necho %s\n' "$(cat "$proj/stamp")" > "$out/bayan"; chmod +x "$out/bayan"
STUB
chmod +x "$tmp/aux/dotnet"
export AUX="$tmp/aux" PATH="$tmp/aux:$PATH"
unset PINNED_BAYAN PINNED_BAYAN_REF

r="$tmp/repo"; mkdir -p "$r/src/Bayan.Cli"; cd "$r"
git init -q -b dev; git config core.hooksPath /dev/null  # a scratch repo: the global hooks refuse commits on dev
echo one > src/Bayan.Cli/stamp; git add -A; git commit -qm one
devsha=$(git rev-parse dev)
git checkout -q -b feat/x; echo two > src/Bayan.Cli/stamp; git commit -qam two
echo dirty > src/Bayan.Cli/stamp

# Built from dev's tip, not the branch or the dirty tree, and cached by that commit.
bin=$("$script") || fail "pinned build failed"
[ "$bin" = "$r/.cache/bayan/$devsha/bin/bayan" ] || fail "binary path was '$bin'"
[ "$("$bin")" = one ] || fail "the binary was not built from dev"
[ "$(cat "$AUX/commit.txt")" = "$devsha" ] || fail "git in the build sees $(cat "$AUX/commit.txt"), not $devsha (the version line would lie)"
[ "$(cat src/Bayan.Cli/stamp)" = dirty ] && [ "$(git rev-parse --abbrev-ref HEAD)" = feat/x ] || fail "the caller's tree changed"
[ -z "$(git status --porcelain -- . ':!src/Bayan.Cli/stamp' ':!.cache')" ] || fail "the build left files outside .cache/: $(git status --porcelain)"

# The second call reuses the cache.
[ "$("$script")" = "$bin" ] && [ "$(wc -l < "$AUX/builds.txt" | tr -d ' ')" = 1 ] || fail "the cached build was rebuilt"

# A worktree shares the main checkout's cache.
git worktree add -q --detach "$tmp/wt" feat/x
[ "$(cd "$tmp/wt" && "$script")" = "$bin" ] || fail "a worktree did not use the main checkout's cache"

# PINNED_BAYAN_REF picks another commit.
[ "$(PINNED_BAYAN_REF=feat/x "$script" | xargs sh)" = two ] || fail "PINNED_BAYAN_REF was not honoured"

# A concurrent build of the same commit that finishes first wins; ours leaves nothing behind.
git checkout -q -b race feat/x; echo race > src/Bayan.Cli/stamp; git commit -qam race; racesha=$(git rev-parse race)
rc="$r/.cache/bayan/$racesha"
RACE_CACHE="$rc" PINNED_BAYAN_REF=race "$script" > "$tmp/out" || fail "the lost race failed"
[ "$(cat "$tmp/out")" = "$rc/bin/bayan" ] && [ "$("$rc/bin/bayan")" = winner ] || fail "the race did not keep the winner's build"
[ -z "$(ls -d "$rc"/.build-* "$r/.cache/bayan"/.build-"$racesha"-* 2>/dev/null)" ] || fail "the losing build was left behind: $(ls -a "$rc")"

# A cache directory without a binary is refused, not rebuilt into.
git checkout -q -b hollow race; echo hollow > src/Bayan.Cli/stamp; git commit -qam hollow; mkdir -p "$r/.cache/bayan/$(git rev-parse hollow)"
if PINNED_BAYAN_REF=hollow "$script" > "$tmp/out" 2> "$tmp/err"; then fail "a hollow cache was accepted: $(cat "$tmp/out")"; fi
grep -q "exists but holds no bin/bayan" "$tmp/err" || fail "no hollow-cache message: $(cat "$tmp/err")"
git checkout -q feat/x

# A failed build exits 1 naming the commit and the log, and prints no binary.
git checkout -q -- src/Bayan.Cli/stamp; git checkout -q dev; touch src/Bayan.Cli/broken; git add src; git commit -qm broken; badsha=$(git rev-parse dev)
if out=$("$script" 2> "$tmp/err"); then fail "a failed build succeeded: $out"; fi
[ -z "$out" ] || fail "a failed build printed '$out'"
grep -q "building bayan at $badsha (dev) failed; log: .*build.log" "$tmp/err" || fail "no failure message: $(cat "$tmp/err")"
grep -q "stub failure" "$(sed -E 's/.*log: //' "$tmp/err")" || fail "the log does not hold the build output"

# An unknown ref is refused.
if PINNED_BAYAN_REF=nope "$script" 2> "$tmp/err"; then fail "an unknown ref was accepted"; fi
grep -q "no commit for 'nope'" "$tmp/err" || fail "no unknown-ref message: $(cat "$tmp/err")"

# PINNED_BAYAN (tests) must name an executable.
[ "$(PINNED_BAYAN="$bin" "$script")" = "$bin" ] || fail "PINNED_BAYAN was not printed"
if PINNED_BAYAN="$tmp/none" "$script" 2> "$tmp/err"; then fail "a missing PINNED_BAYAN was accepted"; fi
grep -q "PINNED_BAYAN=$tmp/none is not an executable" "$tmp/err" || fail "no PINNED_BAYAN message"

# After a build only the 3 most recently used commits stay, the new one among them; a recent build in progress is left alone.
for d in "$r/.cache/bayan"/*/; do touch -t 202001010000 "$d"; done  # everything built so far is long unused
for i in 1 2 3 4 5; do mkdir -p "$r/.cache/bayan/fake$i/bin"; touch -t 20200101000$i "$r/.cache/bayan/fake$i"; done
mkdir -p "$r/.cache/bayan/.build-other-1"
git checkout -q -b evict race; echo evict > src/Bayan.Cli/stamp; git commit -qam evict; evsha=$(git rev-parse evict)
PINNED_BAYAN_REF=evict "$script" > "$tmp/out" || fail "the eviction build failed"
[ -x "$r/.cache/bayan/$evsha/bin/bayan" ] || fail "the new build was evicted"
[ ! -e "$r/.cache/bayan/$evsha/src" ] || fail "the build's src checkout was kept"
[ "$(ls "$r/.cache/bayan" | grep -vx CACHEDIR.TAG | wc -l | tr -d ' ')" -le 3 ] || fail "more than 3 cached commits remain: $(ls "$r/.cache/bayan")"
head -1 "$r/.cache/bayan/CACHEDIR.TAG" | grep -qx "Signature: 8a477f597d28d172789f06886806bc55" || fail "the pinned cache carries no CACHEDIR.TAG"
[ -d "$r/.cache/bayan/.build-other-1" ] || fail "a recent build in progress was removed"
git checkout -q feat/x

# Resolving touches the pin, so eviction follows last use; a pin used within 2 hours survives even past the keep count.
touch -t 202001010000 "$r/.cache/bayan/$evsha"
PINNED_BAYAN_REF=evict "$script" > /dev/null || fail "resolving the cached pin failed"
[ -n "$(find "$r/.cache/bayan/$evsha" -maxdepth 0 -mmin -5)" ] || fail "resolving did not touch the pin"
for i in 1 2 3 4; do mkdir -p "$r/.cache/bayan/recent$i/bin"; done
git checkout -q -b evict2 evict; echo evict2 > src/Bayan.Cli/stamp; git commit -qam evict2
PINNED_BAYAN_REF=evict2 "$script" > /dev/null || fail "the second eviction build failed"
for i in 1 2 3 4; do [ -d "$r/.cache/bayan/recent$i" ] || fail "a pin used within 2 hours was evicted"; done
git checkout -q feat/x

# Wiring: the mutation step runs the pinned bayan; CI's mutation run pins the commit it checked out.
grep -q 'pinned-bayan.sh' "$root/scripts/selfhost/mutation-step.sh" || fail "mutation-step.sh does not use pinned-bayan.sh"
! grep -q 'bin/Debug/net10.0/bayan' "$root/scripts/selfhost/mutation-step.sh" || fail "mutation-step.sh still names the working-tree build"
grep -q 'PINNED_BAYAN_REF: HEAD' "$root/.github/workflows/mutation.yml" || fail "mutation.yml does not pin HEAD"
echo PASS
