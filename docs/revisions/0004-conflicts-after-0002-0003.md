# 0004 — Conflicts left after applying 0002 and 0003

Status: draft. Date: 2026-10-24. Found while applying revisions 0002 and 0003 to SPEC, GRAMMAR, STYLE, HANDOFF and plan (`chore/worker-r3-rev23`). Each item is text the revisions did not cover; the proposed resolution is for the user to accept, change or reject.

## Problem and proposed decision

1. **BYN3207 is `LiteralNeedsTry`**, but 0002 item 27 puts the mixed-`it` `requires` rejection under BYN3207. *Proposed:* allocate a new code for the mixed rule (next free 32xx) and keep BYN3207 as is.
2. **GRAMMAR §1 tree item 2** ("plain verb `join`, `trim`, never `joined`") and **SPEC §16** ("`-ed` exists only as the copy form of a mutating verb") contradict 0003's `trimmed()` and `joined`. *Proposed:* rewrite both to 0003 §2: `-ed` names a changed copy or a view; plain verbs act on `ref this`.
3. **SPEC §2.3 lists a `"""raw multi-line"""` literal**, but 0003 §4 says a raw multi-line literal is not decided. *Proposed:* mark the row "not decided (revision 0003)".
4. **SPEC §7.4 keeps the `T? next() fails E` bullet** beside the new `moveNext` pattern rule (0002 item 2). *Proposed:* remove the bullet.
5. **SPEC §3.2 still has `string.invalidUtf8At`**, which 0003 does not rename. *Proposed:* drop it; `Utf8Error` carries the position of the bad byte (0003 §1).
6. **The plan §7 per-step code counts do not add up** to the total line, before or after the edits. *Proposed:* recount the rows against the allocation list (0002 item 13: fix the rows, not the list).
7. **SPEC's typestate section omits BYN7009** (`MissingBecome`, 0002 item 22). *Proposed:* add one sentence: a `becomes .s` method must `become` on every returning path.

## Sections
SPEC §2.3, §3.2, §7.4, §16, typestate; GRAMMAR §1; plan §7.

## Codes
Possibly new: one 32xx code for item 1.

## Sūq files
None.
