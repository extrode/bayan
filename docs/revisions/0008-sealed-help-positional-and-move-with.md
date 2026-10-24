# 0008 — `@sealed` misuse helps; `@positional` gets a step; `move x with { … }`

Status: draft. Date: 2026-10-24. Found while building 5.3b (`chore/worker-r3-53b`, handoff Question 224) and 5.3e (`chore/worker-r3-53e`, Question 263).

## Problem

1. 5.3b reports a misplaced or argument-carrying `@sealed` as BYN1016 `ModifierNotAllowed` ("`{0}` is not allowed here") with two help strings the plan does not have: "put `@sealed` on a struct" and "write `@sealed` with no arguments and no `because`". The plan is the single source of diagnostic text (revision 0006 item 3), so these must be in it before 5.3b lands.
2. J39 and C35 exempt constructors of `@positional` structs from BYN5004–5006 (SPEC §5 particles, GRAMMAR, HANDOFF §15: `Point(3, 4)`, not `Date`). No §4 step builds the `@positional` attribute: `CheckAttributes` rejects it as unknown, so the exemption cannot be written or tested. 5.3e removed its dead exemption line (Q263). The design text is right; the plan has a gap.
3. Revision 0007 says `let copy = tx with { … }` on a `unique` value "needs `move tx`", but `move tx with { … }` parses as `move (tx with { … })` and reports BYN2782 ("cannot move out of …"). Only `(move tx) with { … }` compiles. BYN2780's help on a `with` copy does not mention `move`.

## Decision

1. **The plan gains a BYN1016 entry (it has none; the code predates round 3) with the two `@sealed` help forms**, worded as 5.3b built them (both start with listed verbs, R15):
   - On anything but a struct: help "put `@sealed` on a struct".
   - With arguments or `because`: help "write `@sealed` with no arguments and no `because`".
   Message, label and fix are unchanged ("`{0}` is not allowed here"; label at the attribute; fix no).
2. **`@positional` is built in step 5.3g** (R semantics, which already owns the particle-adjacent call rules J62–J65 and runs after 5.3e): the attribute is accepted bare on a struct (misuse is BYN1016, with helps "put `@positional` on a struct" and "write `@positional` with no arguments and no `because`"), and its constructors are exempt from BYN5004–5006 per J39. One `diag/` case for the exemption and one for misuse; Q263 is closed by it.
   - *Alternative:* mark `@positional` spec-only for v0.1 (no code; C35's `Point(3, 4)` needs particles until it lands). Cheaper by one small item; the self-hosted compiler does not need it.
3. **`move` takes a place, so `move x with { … }` is `(move x) with { … }`.** The operand of `move` is a place (a name, a field path or an index), never a larger expression, so `with` applies to the moved value. This is how SPEC §9.3 already uses `move x`; the grammar just says so. `move (tx with { … })` stays BYN2782. BYN2780's help on a `with` that copies a `unique` value becomes "write `move {0} with { … }` to give up `{0}`".
   - *Alternative:* keep the precedence and teach the parentheses: BYN2780's help says "write `(move {0}) with { … }`". No grammar change; an unusual spelling for a common case.

## Sections

SPEC §9.3 (the `move` operand is a place; the `with` example); GRAMMAR (`move` operand); plan §2 a new BYN1016 entry and the BYN2780 entry, §4 rows 5.3b (helps) and 5.3g (`@positional`), §7 5.3g size note; handoff Questions 224 and 263.

## Codes

- Changed: BYN1016 (four help forms added), BYN2780 (help on a `with` copy).
- No new, reserved or retired codes. Totals unchanged.

## Effect on built work

- 5.3b (at gate): the BYN1016 `@sealed` helps already match item 1. Item 3 is a parser change in `Parser.cs` (owned by no active step once 5.1e landed) plus the BYN2780 help in `OwnershipAnalysis.cs`; it lands in 5.3b, which owns the `with` move.
- 5.3e (at gate): unchanged. Its dormant `extern` exemption in `Particles.cs` stays untested until `extern` declarations parse (J50, spec-only in v0.1); no revision needed.

## Sūq files

None.
