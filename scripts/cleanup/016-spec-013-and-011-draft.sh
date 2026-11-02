#!/usr/bin/env bash
# Cleanup after spec 013 merged into dev (8eccaac, partial) and the 011 draft (95a0276), 2026-11-02.
# 1. worktrees of 013's worker writers, the feat/013, docs/011 and Windows-fix checkouts, and their branches, when dev holds them;
# 2. the two ~/.claude worktrees of the agy shell class work, when ~/.claude's dev holds them;
# 3. (--delete-scratch) gitignored scratch in tmp/ from 013's review, refute, T19 and agy-shell work;
# 4. (--remove-docker-image) the bayan-t19 image (2.16 GB) built for the T19 Linux run;
# 5. (--delete-remote) the T19 scratch clone, bundles and run script on wp1 and wlt1 (ssh), and wp1's old zig 0.16.0-dev and the 0.17.0 zip.
#    Run --delete-remote only after step 9 of docs/plans/2026-11-02-003-move-to-ikhbat-org.md has moved the wp1/wlt1
#    checkout to C:\home\code\ikhbat.org\bayan: section 5 names that path, and refuses on a host where it has no .git yet.
# Section 1 also takes the 2026-11-02 review-fix, Q742, docs and tmp/win-combined (test/win-combined-1102), tmp/combined-1102b (test/combined-1102b) worktrees once dev holds them.
# Dry run by default. --execute / -e acts on worktrees and branches; each later section also needs its own flag.
# Merged-ness, tracked-ness and existence are asked at run time, never taken from this header.
set -euo pipefail

execute=0; scratch=0; docker_img=0; remote=0
for a in "$@"; do
  case "$a" in
    --execute|-e) execute=1 ;;
    --delete-scratch) scratch=1 ;;
    --remove-docker-image) docker_img=1 ;;
    --delete-remote) remote=1 ;;
    *) printf 'unknown flag: %s\n' "$a"; exit 2 ;;
  esac
done
repo="$(git rev-parse --show-toplevel)"
cd "$repo"
failed=0

if [ "$(git branch --show-current)" != "dev" ]; then
  printf 'refuse: check out dev first (on %s).\n' "$(git branch --show-current)"; exit 1
fi

# remove_worktrees REPO PATTERN...: worktrees of REPO whose path (relative) matches a PATTERN and whose branch tip dev holds.
remove_worktrees() {
  local r="$1"; shift
  local listing wt br rel p hit
  listing="$(git -C "$r" worktree list --porcelain)"
  while IFS=$'\t' read -r wt br; do
    rel="${wt#"$r"/}"; hit=0
    for p in "$@"; do case "$rel" in $p) hit=1 ;; esac; done
    [ $hit -eq 1 ] || continue
    if [ -z "$br" ] || ! git -C "$r" merge-base --is-ancestor "refs/heads/$br" refs/heads/dev 2>/dev/null; then
      printf 'keep:  %s (%s not in dev)\n' "$wt" "${br:-detached}"; continue
    fi
    printf '%s\n' "$br" >> "$branches_file"
    if [ $execute -eq 1 ]; then
      if git -C "$r" worktree remove "$wt"; then printf 'removed: %s\n' "$wt"; else printf '  refused: %s (git said why above). Left alone.\n' "$wt"; failed=1; fi
    else
      printf 'would: git -C %s worktree remove %s\n' "$r" "$wt"
    fi
  done < <(printf '%s\n' "$listing" | awk '/^worktree /{w=substr($0,10)} /^branch /{print w "\t" substr($0,19)} /^detached/{print w "\t"}')
  if [ $execute -eq 1 ]; then git -C "$r" worktree prune; else printf 'would: git -C %s worktree prune\n' "$r"; fi
}
delete_branches() {
  local r="$1" b
  while read -r b; do
    [ -n "$b" ] || continue
    if ! git -C "$r" rev-parse --verify --quiet "refs/heads/$b" >/dev/null; then printf 'skip:  branch %s no longer exists\n' "$b"
    elif [ $execute -eq 1 ]; then git -C "$r" branch -d "$b" || { printf '  refused: %s. Left alone.\n' "$b"; failed=1; }
    else printf 'would: git -C %s branch -d %s\n' "$r" "$b"; fi
  done < "$branches_file"
  : > "$branches_file"
}
branches_file="$(mktemp)"; trap 'rm -f "$branches_file"' EXIT

# 1. This repo.
printf '== 1. bayan worktrees and branches\n'
remove_worktrees "$repo" '.worktrees/worker-w-013-*' '.worktrees/worker-w-winport' '.worktrees/feat-013-test-tooling' \
  '.worktrees/docs-011-spec' '.worktrees/fix-windows-portability' '.worktrees/worker-w-rev1102*' '.worktrees/worker-w-q742*' \
  '.worktrees/fix-q742-lf-output' '.worktrees/fix-review-2026-11-02-2' '.worktrees/chore-remove-mutant-outcome' \
  '.worktrees/docs-handoff-1102' '.worktrees/docs-handoff-1102-close' 'tmp/win-combined' '.worktrees/chore-q743-known-failures' '.worktrees/worker-w-q743kf' '.worktrees/fix-windows-raw-stdout' '.worktrees/fix-review-2026-11-02-3' 'tmp/combined-1102b'
delete_branches "$repo"

# 2. ~/.claude (its own dev).
printf '== 2. ~/.claude worktrees and branches\n'
cr="$HOME/.claude"
if [ -d "$cr/.git" ] || git -C "$cr" rev-parse --git-dir >/dev/null 2>&1; then
  remove_worktrees "$cr" '.worktrees/feat/agy-shell-class' '.worktrees/fix/agy-session-settings'
  delete_branches "$cr"
else
  printf 'skip:  %s is not a git repo\n' "$cr"
fi

# 3. Scratch under tmp/ (gitignored; refused if git tracks it).
printf '== 3. tmp/ scratch\n'
for rel in tmp/agy-sh tmp/agy-sh-repo tmp/audit-013.txt tmp/refute2 tmp/review-013 tmp/review-dev-1102 tmp/win-port \
           tmp/c4 tmp/t19 tmp/bk tmp/mutate-batch tmp/guard-run tmp/013-t27 tmp/law-tier-proposal.md tmp/q743-known tmp/mb-break.out tmp/review-1102b tmp/win-port/raw.trx tmp/win-port/raw2.trx tmp/win-port/raw2.bundle tmp/win-port/wp1-raw.out tmp/win-port/wp1-raw2.out; do
  if [ ! -e "$rel" ]; then printf 'skip:  %s already gone\n' "$rel"; continue; fi
  if [ -n "$(git ls-files -- "$rel" | head -1)" ]; then
    printf 'refuse: %s is tracked by git. Propose it as a commit instead. Left alone.\n' "$rel"; failed=1; continue
  fi
  if [ $execute -eq 1 ] && [ $scratch -eq 1 ]; then rm -rf -- "$rel"; printf 'removed: %s\n' "$rel"
  else printf 'would: rm -rf %s (needs --execute --delete-scratch)\n' "$rel"; fi
done

# 3b. The T27 gate reviewer's copies in /tmp (outside the repo; the hook blocked its own rm).
for abs in /tmp/t27rev /tmp/t27base; do
  if [ ! -e "$abs" ]; then printf 'skip:  %s already gone\n' "$abs"; continue; fi
  if [ $execute -eq 1 ] && [ $scratch -eq 1 ]; then rm -rf -- "$abs"; printf 'removed: %s\n' "$abs"
  else printf 'would: rm -rf %s (needs --execute --delete-scratch)\n' "$abs"; fi
done

# 4. Docker image.
printf '== 4. docker image bayan-t19\n'
if ! command -v docker >/dev/null 2>&1; then printf 'skip:  docker not installed\n'
elif ! docker image inspect bayan-t19 >/dev/null 2>&1; then printf 'skip:  image bayan-t19 already gone\n'
elif [ $execute -eq 1 ] && [ $docker_img -eq 1 ]; then docker image rm bayan-t19 || { printf '  refused: docker image rm bayan-t19. Left alone.\n'; failed=1; }
else printf 'would: docker image rm bayan-t19 (needs --execute --remove-docker-image)\n'; fi

# 5. Remote T19 scratch (PowerShell over ssh). /home/code/ikhbat.org/bayan is the hosts' real checkout: only the run script there.
printf '== 5. wp1 / wlt1 T19 scratch\n'
for h in wp1 wlt1; do
  for p in C:/Users/Sy/bayan-bcl C:/Users/Sy/bcl.bundle C:/Users/Sy/AppData/Local/Temp/bayan-013.bundle /home/code/ikhbat.org/bayan/tmp-t19-run.ps1 \
           C:/Users/Sy/winport.bundle C:/home/lang/zig-0.16.0-dev.2682 C:/home/lang/zig-x86_64-windows-0.17.0.zip C:/Users/Sy/kf-trx; do
    if [ "${p##*/}" = tmp-t19-run.ps1 ]; then
      if ! co="$(ssh -o ConnectTimeout=5 "$h" "if (Test-Path '/home/code/ikhbat.org/bayan/.git') { 'yes' } else { 'no' }" 2>/dev/null | tr -d '\r')"; then
        printf 'skip:  %s unreachable\n' "$h"; continue 2
      fi
      if [ "$co" != yes ]; then
        printf 'refuse: %s: /home/code/ikhbat.org/bayan is not a checkout yet (move step 9 first). Left alone.\n' "$h"; failed=1; continue
      fi
    fi
    if ! there="$(ssh -o ConnectTimeout=5 "$h" "if (Test-Path '$p') { 'yes' } else { 'no' }" 2>/dev/null | tr -d '\r')"; then
      printf 'skip:  %s unreachable\n' "$h"; continue 2
    fi
    if [ "$there" != yes ]; then printf 'skip:  %s:%s already gone\n' "$h" "$p"; continue; fi
    if [ "${p##*/}" = tmp-t19-run.ps1 ] && \
       [ "$(ssh "$h" "git -C /home/code/ikhbat.org/bayan ls-files -- tmp-t19-run.ps1" 2>/dev/null | tr -d '\r')" != "" ]; then
      printf 'refuse: %s:%s is tracked by git there. Left alone.\n' "$h" "$p"; failed=1; continue
    fi
    if [ $execute -eq 1 ] && [ $remote -eq 1 ]; then
      ssh "$h" "Remove-Item -Recurse -Force '$p'" && printf 'removed: %s:%s\n' "$h" "$p" || { printf '  refused: %s:%s. Left alone.\n' "$h" "$p"; failed=1; }
    else printf 'would: ssh %s Remove-Item -Recurse -Force %s (needs --execute --delete-remote)\n' "$h" "$p"; fi
  done
done

printf '\nremaining worktrees:\n'
git worktree list
if [ $execute -eq 1 ] && [ $failed -eq 1 ]; then exit 1; fi
exit 0
