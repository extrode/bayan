# 0010 — `becomes` needs a `ref this` receiver

Status: accepted 2026-10-24 (user direction, from the 5.3c gate findings on `this` narrowing).

## Problem

SPEC §3.17 lets any receiver form declare a transition: the BYN7003 page's own example is `void pay(move this is .draft, i64 amount) becomes .paid`. But a transition only means something to a caller that still holds the value afterwards. A `move this` receiver ends the caller's ownership, and a plain `this` is a copy the caller never sees again, so `becomes` on either changes a state nobody can observe. Allowing it also costs flow analysis: every place that forgets what `this` is known to be (loop entry, `become`, `becomes` calls) has to cover `move this` and `this` as well as `ref this`. The 5.3c gate found the `move this` loop case missing (a `while` body read a payload after `become .draft` with no BYN7002). Round 3's design note says the same: "`move this` ends ownership (`tx.commit()`, `rows.all()`); typestate (`becomes`) moves a live value through a lifecycle."

## Decision

1. A method that declares `becomes .s` must take `ref this` (with or without `is .s`). `becomes` on a `this` or `move this` receiver, or on a free function, is an error (BYN7010).
2. A `become` in a method whose `becomes` was rejected by BYN7010 reports nothing more (no BYN7003, no BYN7009 cascade).
3. `move this is .s` and `this is .s` stay valid receivers for methods without `becomes` (§3.17 item 3): they read or consume a value in a known state.
4. Facts about `this` are dropped at loop entry only for `ref this` receivers, which are now the only ones that can change state.

## Sections

- §3.17 item 11, after the first sentence: "Only a `ref this` method may declare `becomes`: a `move this` or `this` receiver leaves the caller with no value whose state could change (BYN7010)."
- §3.17 closing note, Codes: add BYN7010.

## Codes

- Added: BYN7010 (plan, revision 0010) `BecomesNeedsRef` "`{0}` declares `becomes .{1}` but takes `{2}`; a transition needs `ref this`" (label at the receiver; help "change the receiver of `{0}` to `ref this`"; fix yes: replace the receiver's `move`/plain mode with `ref`, keeping `is .s`).
- Changed: BYN7003 page example and fix text use `ref this`.

## Effect on built work

- 5.3c (second half, in progress): the binder reports BYN7010 at the signature; `ForgetChangingStates` goes back to `ref this` only. The `move this` loop case in `diag/this-narrowing` is replaced by a BYN7010 rejection case and a `ref this` loop case.
- Goldens whose `move this … becomes` is incidental (`diag/become-undeclared`, `diag/unknown-state`, `typed/state-types`) switch to `ref this`; parse goldens (`parse/receiver-states`, `parse/clauses-line-start`) are syntax only and stay.

## Sūq files

None (Sūq's typestate methods already use `ref this`).
