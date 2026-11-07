#!/usr/bin/env bash
# Written 2026-11-06 by the memory/disk leak audit (docs/memory-leaks.md). Removes the scratch and parked output the
# audit measured: tmp/ignored-* (18.4 GB), tmp/stale-worker-files (2.4 GB), pre-v1 tmp/cache/<16hex> entries, tmp/land-*
# and tmp/guard-land-* leftovers, the audit's own scratch, and old bayan-* temp dirs and dead dotnet-diagnostic sockets
# in $TMPDIR. Worktrees are not handled here: run scripts/cleanup/merged-worktree-backlog.sh, which now retires trees
# whose only ignored files sit under a CACHEDIR.TAG cache.
# Dry run by default. --execute / -e plus --delete-scratch deletes (untracked files, irreversible).
set -euo pipefail

execute=0; scratch=0
for a in "$@"; do
  case "$a" in
    --execute|-e) execute=1 ;;
    --delete-scratch) scratch=1 ;;
    *) printf 'unknown flag: %s\n' "$a"; exit 2 ;;
  esac
done
repo="$(git rev-parse --show-toplevel)"
cd "$repo"
main="$(git rev-parse --path-format=absolute --git-common-dir)"; main="${main%/.git}"
if [ "$repo" != "$main" ]; then printf 'refuse: run from the main checkout (%s), not a worktree.\n' "$main"; exit 1; fi
failed=0; total=0

# drop PATH: removes one untracked path (relative to the repo, or absolute outside it), printing its size.
drop() {
  local p="$1" rel kb
  if [ ! -e "$p" ] && [ ! -L "$p" ]; then printf 'skip:  %s is gone\n' "$p"; return; fi
  case "$p" in /*) rel="" ;; *) rel="$p" ;; esac
  if [ -n "$rel" ] && [ -n "$(git ls-files -- "$rel" | head -1)" ]; then
    printf 'refuse: %s holds files tracked by git. Propose it as a commit instead. Left alone.\n' "$rel"; failed=1; return
  fi
  kb=$(du -sk "$p" 2>/dev/null | cut -f1); kb=${kb:-0}; total=$((total + kb))
  if [ "$execute" = 1 ] && [ "$scratch" = 1 ]; then
    rm -rf -- "$p" && printf 'gone:  %s (%s KB)\n' "$p" "$kb" || { printf 'fail:  %s\n' "$p"; failed=1; }
  else
    printf 'would: %s (%s KB)\n' "$p" "$kb"
  fi
}

printf '== 1. parked ignored output in tmp/\n'
for p in tmp/ignored-* tmp/stale-worker-files; do [ -e "$p" ] && drop "$p"; done

printf '== 2. pre-v1 cache entries (tmp/cache/<16 hex>)\n'
for p in tmp/cache/*; do
  case "$(basename "$p")" in [0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f]) drop "$p" ;; esac
done

printf '== 3. land.sh and guard leftovers\n'
for p in tmp/land-* tmp/guard-land-*; do [ -e "$p" ] && drop "$p"; done

printf '== 4. audit and writer scratch\n'
for p in tmp/leak-audit tmp/cmb tmp/cmb-backup tmp/bayan-lsp.old-2026-10-27; do drop "$p"; done

printf '== 5. $TMPDIR: bayan-* dirs older than 24 h, dotnet-diagnostic sockets of dead processes\n'
t="${TMPDIR:-/tmp}"
while IFS= read -r p; do drop "$p"; done < <(find "$t" -maxdepth 1 -name 'bayan-*' -mmin +1440 2>/dev/null)
while IFS= read -r s; do
  pid="$(basename "$s" | sed -n 's/^dotnet-diagnostic-\([0-9]*\)-.*/\1/p')"
  [ -n "$pid" ] && kill -0 "$pid" 2>/dev/null && continue
  drop "$s"
done < <(find "$t" -maxdepth 1 -name 'dotnet-diagnostic-*' 2>/dev/null)

printf '== 6. root CACHEDIR.TAG written by dev 71d681cd (the tag now lives in v1/)\n'
for p in tmp/cache/CACHEDIR.TAG "${BAYAN_CACHE:-}/CACHEDIR.TAG" "${XDG_CACHE_HOME:-$HOME/.cache}/bayan/CACHEDIR.TAG" "$HOME/Library/Caches/bayan/CACHEDIR.TAG"; do
  [ -f "$p" ] && [ -d "$(dirname "$p")/v1" ] && grep -q "created by bayan" "$p" && drop "$p"
done

printf '== 7. scratch left by the 2026-11-06 review and 012 landing session\n'
for p in tmp/union-resolve.js tmp/pb.keep tmp/rv3-test-insert.sh tmp/wp1-t7.log tmp/preflight-land-t7.txt tmp/memory-gate-test tmp/memory-gate-test.* tmp/pinned-bayan-test.*; do [ -e "$p" ] && drop "$p"; done
for p in /tmp/rl /tmp/gen /tmp/gen2 /tmp/gen.py /tmp/gen-em.txt /tmp/gen-sem.txt; do [ -e "$p" ] && [ -O "$p" ] && drop "$p"; done

printf '== done: %s KB %s\n' "$total" "$([ "$execute" = 1 ] && [ "$scratch" = 1 ] && echo removed || echo 'would be removed (dry run; --execute --delete-scratch acts)')"
if [ "$execute" = 1 ] && [ "$scratch" = 0 ]; then printf 'note: --execute alone deletes nothing here; add --delete-scratch.\n'; fi
if [ "$execute" = 1 ] && [ "$failed" = 1 ]; then exit 1; fi
exit 0
