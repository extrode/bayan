# 0008 — `@sealed` misuse helps; `@positional` gets a step; `move x with { … }`

Status: accepted 2026-10-24 (user, reviewed copy). Erratum (2026-10-24): GRAMMAR has no precedence table, so the precedence change is in SPEC §6.1 only; Questions 224 and 263 live on the 5.3b and 5.3e branches and close when those steps land. Erratum (2026-10-24): "every prefix operator binds tighter than `with`" excludes `try`, which is a scope keyword covering the whole expression to its right (SPEC §18); `try x with { … }` is `try (x with { … })`. Date: 2026-10-24. Found while building 5.3b (`chore/worker-r3-53b`, handoff Question 224) and 5.3e (`chore/worker-r3-53e`, Question 263). Amends revision 0007 §2.

## Problem

1. 5.3b reports a misplaced or argument-carrying `@sealed` as BYN1016 `ModifierNotAllowed` ("`{0}` is not allowed here") with two help strings the plan does not have. The plan is the single source of diagnostic text (revision 0006 item 3), so they must be in it before 5.3b lands.
2. J39 and C35 exempt constructors of `@positional` structs from BYN5004–5006 (HANDOFF §15: `Point(3, 4)`, not `Date`), but no §4 step builds `@positional`: `CheckAttributes` rejects it as unknown. The design also never says which constructor `Point(3, 4)` calls, since Bayan structs are otherwise built with literals (`Point { x: 3, y: 4 }`).
3. Revision 0007 says `let copy = tx with { … }` on a `unique` value "needs `move tx`", but `move tx with { … }` parses as `move (tx with { … })` and reports BYN2782. BYN2780's help on a `with` copy does not mention `move`.

## Decision

1. **The plan gains a BYN1016 entry** (the code predates round 3), with two `@sealed` help forms worded as 5.3b built them. Message, label and fix are unchanged ("`{0}` is not allowed here"; label at the attribute; fix no).
   - On anything but a struct: help "put `@sealed` on a struct".
   - With arguments or `because`: help "write `@sealed` with no arguments and no `because`".
2. **`@positional` is built in step 5.3g**, after 5.3e has landed (5.3g then edits `Particles.cs` under the Files rule of 0007 §3, listed in its gate summary).
   - **What it generates:** a struct marked `@positional` gets a constructor taking every field in declaration order, `Point(3, 4)` ≡ `Point { x: 3, y: 4 }`. If the struct has `requires`, the constructor is fallible like the literal (`try Point(3, 4)`).
   - **The exemption** from BYN5004–5006 covers only this generated constructor. Static factories and methods on the type follow the particle rules.
   - **Misuse is BYN1016** with three help forms: "put `@positional` on a struct"; "write `@positional` with no arguments and no `because`"; and on a struct with a `states` block, "remove `@positional`: a struct with states is built with state literals".
   - **Tests:** one `diag/` case for the exemption, one per misuse form. Q263 is closed by it.
3. **`with` binds looser than prefix operators**, as in C# (where `with` and `switch` sit below the unary operators). So `move tx with { … }` is `(move tx) with { … }`; every prefix operator binds tighter than `with`. This is a precedence change in the expression table, not a new "place" rule: whether `move`'s operand is something that can be moved stays a semantic check, and `move (tx with { … })` and `move xs[0]` stay BYN2782.
   - BYN2780's help on a `with` that copies a `unique` value becomes "write `move {0} with {{ … }}` to give up `{0}`". The literal braces are escaped as `{{ … }}` because the string is a format template.
   - The 0007 §2 example is correct as written once this lands: `let copy = move tx with { label: "copy" }`.

## Sections

- SPEC §3 operator precedence table (`with` below the unary operators), §9.3 (the `move … with` example), §4 attributes (`@positional` generates the positional constructor).
- GRAMMAR: the precedence table; `@positional` beside `@sealed`.
- HANDOFF §15: one sentence on the generated constructor.
- Plan §2: new BYN1016 entry (five help forms in total) and the BYN2780 entry; §4 rows 5.3b (`@sealed` helps, the precedence change, the BYN2780 help) and 5.3g (`@positional`); §7 5.3g size note. Handoff Questions 224 and 263 closed.

## Codes

- Changed: BYN1016 (five help forms: two for `@sealed`, three for `@positional`), BYN2780 (help on a `with` copy).
- No new, reserved or retired codes. Totals unchanged.

## Effect on built work

- 5.3b (at gate): the `@sealed` helps already match item 1. Item 3 is a precedence change in `Parser.cs` (no active step owns it since 5.1e landed; list it in the gate summary) plus the BYN2780 help in `OwnershipAnalysis.cs`.
- 5.3e (at gate): unchanged. Its dormant `extern` exemption in `Particles.cs` stays untested until `extern` declarations parse (J50, spec-only in v0.1).

## Sūq files

None.
