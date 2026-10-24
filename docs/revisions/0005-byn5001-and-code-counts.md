# 0005 — BYN5001 re-scoped; spec-only codes in the plan §7 count

Status: approved 2026-10-24 (user, in session). Date: 2026-10-24. Found while applying revision 0004 (`chore/worker-r3-rev4`). Amends 0004 items 2 and 6.

## Problem

1. 0004 item 2 lets an `-ed` member exist without a mutating verb (`string.trimmed()` with no `trim`). BYN5001, as built, rejects exactly that: "an `-ed` member needs a `ref this` verb of the same stem" (GRAMMAR tree 1 and rule table, SPEC §5.2 table, `docs/diagnostics/BYN5001.md`).
2. 0004 item 6 fixes the plan §7 rows and keeps the list's 78 new codes, but the rows add up to 65. 13 codes are SPEC allocations that no v0.1 compiler step raises (for example BYN3203, BYN3210, BYN8002).

## Decision

1. **BYN5001 is re-scoped, not retired.** It fires on an `-ed` member that takes `ref this`: "`{0}` changes `this`; name it `{1}`" (label at the name; help "name the member that changes `this` with the plain verb, and keep `-ed` for the copy or view"; fix no). The `-ed` form names a changed copy or a view (0004 item 2) and never requires a mutating verb to exist. The participle detection (`-ed`, `-d`, doubled consonant, `-ied`, and the not-a-participle list) is unchanged. The checker change lands in 5.4, with the existing "BYN5001 state and math exemptions" item.
2. **Plan §7 gains a row "Spec only (no v0.1 step)"** listing the codes SPEC allocates that no step raises. With 5.3b at 7 (BYN3218 added), the rows sum to the 78 new. The reserved count must agree with that row: either the reserved count becomes 13, or the total row names the one code that is in the spec-only row but deliberately not reserved, with the reason. A mismatch left unexplained is a defect.

## Sections

GRAMMAR tree 1 and the rule table; SPEC §5.2 table; `docs/diagnostics/BYN5001.md`; plan §4 row 5.4 and §7.

## Codes

- Changed: BYN5001 (meaning re-scoped; same number).
- Unchanged: totals of 0004 item 6 (78 new, 3 retired); reserved count per decision 2.

## Effect on built work

- The BYN5001 checker (`src/Bayan.Semantics`) still rejects an `-ed` member with no `ref this` verb, and does not flag an `-ed` member that takes `ref this`. Fixed in 5.4; `work/todo.md` line.

## Sūq files

None.
