# 0004 — Conflicts left after applying 0002 and 0003

Status: accepted. Date: 2026-10-24. Found while applying revisions 0002 and 0003 (`chore/worker-r3-rev23`). Amends 0002 item 27 and 0003 §2 and §4.

## Problem

Text in SPEC, GRAMMAR and the plan that revisions 0002 and 0003 did not cover, or that they contradicted.

## Decision

1. **New code for the mixed `requires` rule.** BYN3207 stays `LiteralNeedsTry`. Allocate BYN3218 `RequiresMixesFields` "this `requires` reads `it` and other fields" (label at the rule; help "split it: an `it` rule under the field, and a struct-level rule with `at .field`"; fix no), raised in 5.3b. Amends 0002 item 27; record in J43.
2. **`-ed` names a changed copy or a view; a plain verb acts on `ref this`.** Rewrite GRAMMAR §1 tree item 2 and SPEC §16 to 0003 §2. The `-ed` form does not require a mutating verb to exist: an immutable type (`string`) has `trimmed()` and `lowercased()` with no `trim` or `lowercase`. A mutable type with both forms keeps both (`xs.sort()` / `xs.sorted()`).
3. **The raw multi-line literal stays as SPEC §2.3 defines it.** It predates 0003; 0003 §4's "not decided" was wrong. Amend 0003 §4's literal list to include it.
4. **Remove SPEC §7.4's `T? next() fails E` bullet.** The `moveNext`/`current` pattern (0002 item 2) is the only iteration protocol.
5. **Drop `string.invalidUtf8At`.** `try Utf8.decode(bytes)` reports the position through `Utf8Error`. Decoding validates without copying: when the input bytes are borrowed, the resulting `string` borrows them too, so decode-to-validate allocates nothing.
6. **Recount the plan §7 per-step code counts against the allocation list** (0002 item 13): the rows are fixed, not the list. The list after this revision is 78 new (28 syntax, 18 types, 10 grammar, 7 effects, 9 typestate, 6 security), 12 reserved, 3 retired (1030, 1032, 1049).
7. **SPEC typestate gains one sentence:** a method that declares `becomes .s` must `become` on every path that returns normally; a path that fails leaves the state unchanged (BYN7009).

## Sections

SPEC §2.3 (no change; 0003 amended instead), §3.2, §7.4, §16, typestate section; GRAMMAR §1; plan §3 (J43), §7; revision 0003 §4.

## Codes

- New: BYN3218 `RequiresMixesFields` (5.3b).
- Unchanged: BYN3207 `LiteralNeedsTry`.

## Sūq files

None.
