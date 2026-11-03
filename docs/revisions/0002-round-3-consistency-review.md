# 0002 — Round 3 consistency review

Status: approved 2026-10-24 (user). Date: 2026-10-24. Applies to `docs/bayan-design-round-3.md` ("HANDOFF"), `docs/plans/2026-10-22-002-design-round-3.md` ("plan") and `SPEC.md`. Line numbers are those of the draft (at `27a5116`); `handoff:` means `docs/bayan-handoff.md`.

## Problem

The Fable review (draft 0002) found contradictions inside and between the HANDOFF and the plan, decisions with no build step, plan text that contradicts decided handoff Questions, and rules an implementer would have to guess.

## Decision

1. **Clause order includes `expects`:** `where` → `expects` → `becomes` → `fails` → `uses`. Update HANDOFF:83 (shape), HANDOFF:95 (order sentence), and the BYN1065 message at plan:18.
2. **`for` and `for try` are pattern-based**, like C#'s `foreach`: any type with `bool moveNext(ref this)` and `current` iterates, and the loop takes the effects and error set of the concrete `moveNext`. `IIterator<T>` stays as the bounded interface for generic code; `Rows<T>` does not claim to implement it. Reword HANDOFF:879 ("006 iterator protocol" → "the iteration pattern") and add the rule to SPEC §7.4.
3. **Guard 1 stays "exactly two".** Reword HANDOFF:774: the `remove`/`replace` particle forms are stdlib overloads (J65), and guard 1 governs the language's spellings, not library APIs.
4. **`std.db` shapes follow the particle rule:** `Command prepare(ref this, Sql sql, Millis timeout = 30s)` (a named optional); `bulkCopy` is unchanged, because `bool` parameters are named parameters (state this in J39); `IRow` statics become `checkShape(ref readonly Reader r, as Mapping mapping)` and `fromRow(ref Reader r, as Mapping mapping)`. Update HANDOFF:682–683, 857–858.
5. **The entry function is exempt from BYN6002** whatever its visibility, and examples write `void main()`. Update HANDOFF:360, HANDOFF:403 and J13.
6. **`log` is a non-allocating interpolation sink:** add it to the Q43 list in J14.
7. **v0.1 indexing:** C20 reads `T get(at i64 i)`; `Index` is the post-v0.1 spelling (§9(d), Q90).
8. **The BYN1043 local fix is a syntactic scan** (Q122): rewrite J55 and HANDOFF:77; `var` when the same block later assigns the name, otherwise `let`.
9. **`except .case`:** "parse only; BYN2022 until 5.3b" at plan:404 and plan:49 §7(d); 5.3b lifts it.
10. **Singular `state` is superseded:** strike it from the 5.1d row and C17, with a pointer to 5.1e and BYN1059.
11. **BYN1053 has one owner:** the first step that raises it (5.1a); 5.1b and 5.2a only consume it.
12. **5.3c codes:** "BYN7001–7005, 7007–7009 (7006 parses in 5.1e)".
13. **Recount 5.1e and the totals** from the step rows: 5.1e raises BYN1058–1061, 1063–1067 and parses BYN7006.
14. **Step rows:** BYN3216 in 5.3b (plan:410), BYN5013 in 5.3e (plan:413).
15. **Step rows:** BYN1066 and BYN1067 in 5.1e; BYN3217 in 5.3b if v0.1 has fibers, otherwise reserved.
16. **Primitive spellings:** the reservation of the C# names in `Keywords.cs` joins 5.1e; the project-style type printer joins 5.2b (with `TypeResolver.cs`).
17. **`bayan expand` is deferred** to §6 (after v0.1). Reword "shows them" at HANDOFF:358, 378, 923 to "will show them".
18. **Trap emission** (J89) joins 5.3b's `CEmitter.cs` work, beside the overflow checks.
19. **5.4 row** adds the `string.from` rename (BYN2230, Q115) and the BYN2614 message update (Q119); J82 names both.
20. **Pointers outside `extern`:** new BYN1067 `PointerOutsideExtern` "pointer types are allowed only in `extern` declarations" (label at the type; help "wrap the C call in an `extern` declaration"; fix no), raised in 5.1e. `unsafe because` blocks extend the rule when they first parse (not v0.1, Q132).
21. **`suspend` has no handler.** HANDOFF:386 says so; BYN6007's help names `suspend` as unhandleable.
22. **New BYN7009 `MissingBecome`** "`{0}` declares `becomes .{1}` but returns here without `become`" (label at the return; help "add `become .{1}(…)` before returning, or fail"; fix no), in 5.3c.
23. **Assignment precedence:** from outside the struct, BYN3216 only. Inside the struct's own methods, a field of a struct with `requires` is BYN3208 ("use `with`"); other fields assign freely. Never both. Record in J78.
24. **Byte spans and byte lists have no formatting `string(x)`:** `string(bytes)` always decodes UTF-8 (`try string(bytes)`, `string.tryFrom(bytes)`), including `List<u8>` through its span conversion (Q34). Debug output uses `$"{bytes:debug}"` or `Hex.encode`. This removes the overload overlap that HANDOFF §24 rule 1 forbids. Update HANDOFF §27 (decoding bullet) and SPEC §6.2.
25. **`fail` takes its operand on the same line:** add `fail` to HANDOFF:154 and the BYN1042 scope.
26. **BYN5014 fix** adds the word to the class the checker inferred; the dictionary gains `adjectives:` (HANDOFF:320 example).
27. **Mixed `it` plus other-field `requires` is rejected** (Q131): extend the BYN3207 wording; record in J43.
28. **stderr:** `print(rest ReadOnlySpan<Display> items, to Stream stream = .stdout) uses console`, so `print(p, to: .stderr)`. `Stderr.write` stays as the raw, unformatted writer. The `Panic.handler` example uses `print(p, to: .stderr)`.
29. **Deliverable 2** (HANDOFF:1037) lists the GRAMMAR/STYLE sections the plan edits: §1, §3, §4, §5, §7, §12–§15, §28.
30. **Changelog:** HANDOFF:7 reads "§24–§31"; add a 2026-10-24 line citing revision 0001 (§32, pointers, `#line`, BYN1030) and this revision.

## Sections

- HANDOFF: §2, §3, §11, §13, §14, §15, §24, §26, §27, §28, §30, changelog, deliverables.
- Plan: §1; §2 (C17, C20); §3 (J13, J14, J39, J43, J55, J78, J82, J89); §4 rows 5.1a–5.5; §6 (deferred `expand`); §7 totals.
- SPEC: §6.2 (byte decoding), §7.4 (iteration pattern), §18 (pointers only in `extern`), §21 (`bayan expand` deferred).

## Codes

- New: BYN1067 `PointerOutsideExtern` (syntax), BYN7009 `MissingBecome` (typestate).
- Re-homed: BYN1053 (5.1a), BYN1066 and BYN1067 (5.1e), BYN3216 and BYN3217 (5.3b), BYN5013 (5.3e), BYN7006 (5.1e parse, 5.3c semantics).
- Message changes: BYN1042 (adds `fail`), BYN1065 (adds `expects`), BYN3207 (mixed `requires`), BYN5014 (adjectives), BYN6007 (`suspend` unhandleable).
- Totals by the allocation list: 76 new (27 syntax, 17 types, 10 grammar, 7 effects, 9 typestate, 6 security), 12 reserved, 3 retired (1030, 1032, 1049). Item 13 recounts the step rows against this list; if they differ, the rows are fixed, not the list.

## Sūq files

None changed yet. The Sūq regeneration (C46) uses the shapes above: `prepare` with a named timeout, the `as Mapping` statics, `print(…, to: .stderr)`.
