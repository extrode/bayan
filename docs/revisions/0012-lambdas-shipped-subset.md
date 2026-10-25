# 0012 — Lambdas: the shipped subset

Status: applied 2026-10-25 by spec 007 T13 (the spec's docs task). It records what spec 007 shipped and decides nothing beyond spec 007 and HANDOFF Questions 345–369.

## Problem

SPEC §3.16 described the full design (stored lambdas, borrowed and `move` captures, generic aliases, typed lambda parameters, built-in `where`/`select`) and ended "Not in v0.1 (spec 007)". Spec 007 ships a subset, and SPEC and GRAMMAR did not say which. BYN1055 sat in the round-3 plan's "Spec only (no v0.1 step)" row although 007 raises it.

## Decision

1. SPEC §3.16 gains a **Supported subset (spec 007)** block and a **Self-hosting gaps** block, in the form of §3.15's spec 006 blocks. The design bullets above them stay as the later model; the "Not in v0.1" line now points to the subset.
2. The subset: `->` function types with `fails` and `uses`; placement as parameter, alias and local types only (BYN2822 elsewhere, the stored `pricer` field included); named functions as values with particles dropped from the type; lambdas with name-only parameters typed from the expected type; `it`; captures by copy of `let` values and plain parameters; function values that stay in their frame and loop iteration; positional calls through values; effect-polymorphic function-typed parameters and a `uses` bound on function-typed locals; no generic aliases.
3. GRAMMAR gains the `A => B` input-form row (BYN1055), the v0.1 placement of function types, the lambda and `it` forms, and the `uses` bound on function-typed locals.
4. BYN1055 leaves the round-3 plan's spec-only row, which now lists 11 codes.
5. Laws added by spec 007: L013 "Direct and indirect calls agree" and L014 "Capture by copy never aliases the source".

## Sections

- SPEC §3.16: the "Not in v0.1" line; the two new blocks.
- GRAMMAR §3 input-form table: the BYN1055 row.
- GRAMMAR §5: the v0.1 placement bullet after the parenthesized function type bullet.
- GRAMMAR §9: the lambda bullet and the function-typed local bullet.
- Round-3 plan, cost table: the "Spec only (no v0.1 step)" row.

## Codes

- BYN1055: raised (was spec only).
- BYN1028: message, help and page use `->`; a fix is offered when the signature parses.
- BYN2029: raised (was reserved by round 2).
- BYN2820–BYN2824: added.

## Sūq files

None changed. `suq/fail/on-prefix.byn` (a stored `(Order -> void uses net) onPaid` field) and `suq/packing/packing.byn` (`sort(by: items[it].weight, …)`) use forms outside the subset (BYN2822; no built-in `sort(by:)`), and stay paper code until a later spec.

## Effect on work already done

None: the blocks describe the goldens spec 007 added (`tests/cases/*/lambda-*`, `function-type-*`, `run/self-hosting-lambdas`).
