# 0006 — Error-case payloads step; BYN2322 retired; plan help strings lead with a verb

Status: approved 2026-10-24 (user, in session). Date: 2026-10-24. Found while building 5.3b (`chore/worker-r3-53b`, handoff Question 220) and while adding the plan-strings gate (`scripts/plan-strings-check.js`).

## Problem

1. 5.3b needs `ValidationError<T>` with case `invalid(List<Violation<T>> violations)` (plan §11 (a), (d); J23), but an error case cannot carry a payload: `timeout(i64 after)` inside `union error` fails to parse (BYN1001). No §4 step builds error-case payloads, yet SPEC §3.14 uses them (`timeout(Millis after)`, `outOfStock(Sku sku)`), and so do `DecodeError<T>` and the decoders.
2. Once 5.3b checks `requires` on fields and distinct types, nothing raises BYN2322 (`RequiresNotHere`). SPEC §11 still names it as the code for "not in v0.1", and the plan §7 total counts 3 retired codes.
3. Two BYN1043 help strings in the plan ("declarations put the type first", "local bindings put the name first") do not start with a verb, so they cannot be copied verbatim without breaking R15 (`HelpLanguageTests`). Four other plan helps start with verbs the test's list lacks: `split`, `validate`, `annotate`, `group`.

## Decision

1. **A new step 5.3h builds error-case payloads**: named payloads on `union error` cases (type-first, J54; named, J74), construction `fail .timeout(after)` and `.timeout(x)`, payload binding in `catch switch` arms and `except` (positional `.case(let a)` and whole-value `.case e`, J29), and the `Result` layout of a tag plus the largest payload (SPEC §3.12). It then attaches the violation list: `ValidationError<T>`'s case becomes `invalid(List<Violation<T>> violations)`. 5.3h runs after 5.3b and 5.3f, and before 5.4. Until 5.3h lands, 5.3b's `requires` checks fail with `ValidationError<T>.invalid` carrying no payload; the violation list is not faked.
2. **BYN2322 is retired.** It keeps its `RequiresNotHere` descriptor, renamed for retirement as BYN1049 was (J34), and a tombstone page in `docs/diagnostics/BYN2322.md` ("No longer reported … never reuse", as BYN1032). The SPEC §11 "not in v0.1" note becomes a pointer to that page. Retired count: 4 (1030, 1032, 1049, 2322). The change lands in 5.3b, the step that stops raising the code.
3. **Every help string in the plan starts with a verb from `HelpLanguageTests`' list.** The two BYN1043 helps are rewritten (below), and the four missing verbs are added to the list. `plan-strings-check.js` checks the rule for each checked code and, with `--all-help`, for the whole plan, so the plan stays the single source of the text.
   - Field, parameter or payload: "put the type before the name; declarations are type-first" (the text the 5.1b code already uses).
   - Local binding: "write `{2} {1}: {0}`; local bindings put the name first", where `{2}` is `let`, or `var` when the local is reassigned (J55).
4. **BYN3216 golden changes are expected.** O11 makes code outside a struct, `main` included, unable to assign its fields. Goldens that did so are rewritten to struct literals or `with`, and the 5.3b gate summary lists every golden changed this way.

## Sections

Plan §4 (new row 5.3h; 5.4 order), §7 (5.3h size row; retired count), §11 (a) and (d), §2 (c) BYN1043; SPEC §11 (BYN2322 note); `tests/Bayan.Tests/HelpLanguageTests.cs` verb list; `docs/diagnostics/BYN2322.md` (in 5.3b).

## Codes

- Retired: BYN2322.
- Changed: BYN1043 (help text only).
- Totals: 78 new, 12 reserved, 4 retired.

## Effect on built work

- 5.1b's BYN1043 local-binding help (`Parser.cs`, "write `{word} {name}: {type}`; local bindings put the name first") already matches; the field help already matches.
- 5.3b (in progress) builds `ValidationError<T>` with a payload-less `invalid` case, retires BYN2322, and rewrites the goldens that assign fields from outside the struct.

## Sūq files

None.
