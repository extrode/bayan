#!/bin/bash
# land.sh --check-scope for each row of specs.tsv (010 by its old two-argument form, 011 by name) on a throwaway repo under tmp/,
# with the real wave-brief.py, guard.sh and specs.tsv: a branch that touches the row's own .byn file and its shared-state files is
# accepted, one that touches the other row's shared-state file is refused with the guard's scope message; an unknown spec is refused;
# the brief lands in the row's tmp dir. It fails if specs.tsv loses the 011 row.
set -e
root="$(cd "$(dirname "$0")/../.." && pwd)"
r="$root/tmp/land-rows-test"
rm -rf "$r" && mkdir -p "$r/scripts/selfhost" && cd "$r"
awk -F'\t' '$1 == "010"' "$root/scripts/selfhost/specs.tsv" | grep -q . || { echo "FAIL: specs.tsv has no 010 row"; exit 1; }
awk -F'\t' '$1 == "011"' "$root/scripts/selfhost/specs.tsv" | grep -q . || { echo "FAIL: specs.tsv has no 011 row"; exit 1; }
for f in specs.tsv guard.sh wave-brief.py writer-protocol.md bayan-cheatsheet.md; do cp "$root/scripts/selfhost/$f" scripts/selfhost/; done
git init -q -b base && git config user.email t@t && git config user.name t
printf 'tmp/\n' > .gitignore
for d in 010-self-hosted-semantics 011-self-hosted-emitter; do
  mkdir -p docs/specs/$d; n=${d%%-*}
  printf -- '- [ ] T99 fixture — files: `a.byn` — covers: R1\n' > docs/specs/$d/$n-tasks.md; : > docs/specs/$d/$n-plan.md
done
printf '# Handoff\n' > docs/bayan-handoff.md
git add -A && git commit -qm "test: base"
branch() { git switch -q -c "$1" base; for f in "${@:2}"; do mkdir -p "$(dirname "$f")"; echo "$1" > "$f"; done; git add -A; git commit -qm "test: $1"; git switch -q base; }
branch in010 selfhost/semantics/a.byn selfhost/semantics/ctx.byn
branch out010 selfhost/semantics/a.byn selfhost/emitter/lctx.byn
branch in011 selfhost/emitter/a.byn selfhost/emitter/lctx.byn selfhost/emitter/cprint/cctx.byn
branch out011 selfhost/emitter/a.byn selfhost/semantics/ctx.byn
branch own011 selfhost/emitter/a.byn selfhost/emitter/other.byn
scope() { /bin/bash "$root/scripts/selfhost/land.sh" --check-scope "$@" 2>&1 || true; }
in010=$(scope T99 in010); out010=$(scope T99 out010)
in011=$(scope 011 T99 in011); out011=$(scope 011 T99 out011); own011=$(scope 011 T99 own011)
nospec=$(scope 012 T99 in011)
prompt011=$(grep -c -- "--spec 011" tmp/011-t99/prompt.md || true); prompt010=$(grep -c -- "--spec" tmp/010-t99/prompt.md || true)
cd "$root"; rm -rf "$r"
fail() { echo "FAIL: $1"; echo "$2"; exit 1; }
echo "$in010" | grep -q '^SCOPE OK T99$' || fail "010: an in-area branch was refused" "$in010"
echo "$out010" | grep -q 'scope: selfhost/emitter/lctx.byn is outside --allow' && echo "$out010" | grep -q '^LAND REFUSED' || fail "010: a branch touching the 011 shared file was not refused" "$out010"
echo "$in011" | grep -q '^SCOPE OK T99$' || fail "011: an in-area branch (both shared-state files) was refused" "$in011"
echo "$out011" | grep -q 'scope: selfhost/semantics/ctx.byn is outside --allow' && echo "$out011" | grep -q '^LAND REFUSED' || fail "011: a branch touching the 010 shared file was not refused" "$out011"
echo "$own011" | grep -q 'scope: selfhost/emitter/other.byn is outside --allow' || fail "011: a .byn outside the task's --allow was accepted" "$own011"
echo "$nospec" | grep -q '^LAND REFUSED: no row for spec 012' || fail "an unknown spec was not refused" "$nospec"
[ "$prompt011" = 1 ] || fail "the 011 brief has no '--spec 011' guard line in tmp/011-t99" "$prompt011"
[ "$prompt010" = 0 ] || fail "the 010 brief carries --spec (its prompt must stay as it was)" "$prompt010"
echo PASS
