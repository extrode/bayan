# 0002 — Round 3 consistency review (Fable pass)

Status: draft. Date: 2026-10-24. Source: a `think` (Fable) read-only review of the HANDOFF `docs/bayan-design-round-3.md`, the plan `docs/plans/2026-10-22-002-design-round-3.md`, revision 0001 and handoff Questions 100+. Line numbers refer to those files at `99e1edc` ("handoff" = `docs/bayan-handoff.md`). Each item is a proposed resolution for the user to accept, change or reject.

## Problem
Contradictions inside and between the HANDOFF and the plan, decisions with no build step, plan text that contradicts decided handoff Questions, and rules an implementer would have to guess.

## Decision (proposed)
1. HANDOFF:83, HANDOFF:95, plan:18 vs HANDOFF:292, plan:365 (J68). The signature shape and the BYN1065 clause-order message omit `expects`, while §11 and J68 put `expects` in the signature; 5.1e implements the order from the message. Add `expects` to the shape at HANDOFF:83, the order sentence at HANDOFF:95, and the BYN1065 message at plan:18, in the position J68 chose.
2. HANDOFF:369, HANDOFF:677 vs HANDOFF:879. `IIterator<T>.moveNext(ref this)` declares no `uses` (pure upper bound) yet `Rows<T>.moveNext … fails DbError uses db, suspend` claims to satisfy the "006 iterator protocol", which the effects rule (interface methods declare an upper bound) forbids. Give `IIterator<T>` (or a second `IFallibleIterator<T>`) a `fails E uses …` bound, or state that `for try` iterates a protocol distinct from `IIterator<T>`.
3. HANDOFF:23 vs HANDOFF:774. Guard 1 says "Exactly two" synonym exceptions exist (§5, §28) while §27 calls `remove`/`replace` particle forms a third "deliberate stdlib exception to guard 1". Either reword HANDOFF:774 as particle-set overloading (J65), which is not a synonym, or change HANDOFF:23 to list three.
4. HANDOFF:486 vs HANDOFF:857, HANDOFF:858, HANDOFF:682–683. The rule allows at most one unlabeled parameter but `prepare(ref this, Sql sql, Millis timeout)`, `bulkCopy<T>(ref this, ReadOnlySpan<T> rows, bool ignoreConstraints)` and the `IRow` statics have two, so BYN5004 in 5.3e would reject the std.db shape. Add particles (`prepare(Sql sql, within Millis timeout)`, `bulkCopy(rows, ignoringConstraints: …)`) or carve a named exemption in J39.
5. HANDOFF:360 vs HANDOFF:403, plan:291 (J13). `main` "infers" effects but the `Panic.handler` example writes `public void main()` and J13 says every public function must declare (BYN6002). State that `main` is exempt from BYN6002 regardless of `public`, or drop `public` from the example.
6. HANDOFF:380 vs handoff:461 (Q43), plan:292 (J14). `log` "never allocates" but Q43 says `$"…"` allocates everywhere except `print`, `panic`, `StrBuf.append`, and J14 does not add `log` to that list. Add `log` to the Q43 non-allocating interpolation sinks in J14.
7. plan:248 (C20), plan:232 (C4) vs plan:61 §9(d), handoff:501 (Q90). C20 adopts `T get(at Index i)` while §9(d) says no `Index` type in v0.1 and Q90 decided `get(i64 index)`. Make C20 read `get(at i64 i)` for v0.1 and note `Index` as the post-v0.1 spelling.
8. plan:352 (J55), HANDOFF:77 vs handoff:546 (Q122). J55 says the BYN1043 fix runs the binder; Q122 decided a syntactic scan. Rewrite J55 and HANDOFF:77 to the syntactic scan.
9. plan:404 (5.1b row), plan:49 §7(d) vs handoff:548 (Q124). The plan lists `except .case` as built and v0.1 "yes" while Q124 decided BYN2022 rejects it after parsing. Mark `except .case` "parse only; BYN2022 until 5.3b" in both places, and name the step that lifts it.
10. plan:406 (5.1d row), plan:245 (C17) vs plan:417 (5.1e row), HANDOFF:411. 5.1d and C17 still describe singular contextual `state` while the `states { }` block and BYN1059 moved to 5.1e. Strike `state` from 5.1d and C17 or mark it "superseded by 5.1e".
11. plan:403, plan:404, plan:407. BYN1053 is allocated to 5.1a and 5.1b, and the O1 one-line union rewrite to 5.2a, so two steps claim the same code and golden. Keep BYN1053 in one row and say the others only consume it.
12. plan:411 vs plan:417. BYN7006 appears inside 5.3c's "BYN7001–7008" and in 5.1e's parse-level list. Write 5.3c as "BYN7001–7005, 7007–7008 (7006 parse in 5.1e)".
13. plan:417 vs plan:455. The 5.1e row names eight codes (1065, 1061, 7006, 1059, 1058, 1063, 1060, 1064) but the size table says seven, so the §7 total is off by one. Recount 5.1e and the totals at plan:458.
14. plan:193 vs plan:410, plan:413. BYN3216 (O11) and BYN5013 (O12) are assigned "5.3b" and "5.3e" in the follow-ups but neither step row lists them. Add BYN3216 to row 410 and BYN5013 to row 413.
15. plan:207–211, plan:213–217, plan:391 vs plan:400–419. §31 (implicit `unique`, BYN3217) and Pointers (postfix `T*`, BYN1066) have §1 items but no §4 row, so no step builds or reserves them. Add BYN1066 parse to 5.1e and BYN3217 to 5.3b (or a "reserved" note) in §4.
16. HANDOFF:922–923, plan:367 (J70), revision 0001:30 vs plan:408. Reserving C# primitive names in the lexer and the project-style type printer are v0.1 decisions, but 5.2b's files exclude `Keywords.cs`/`TypeResolver.cs` and no other row owns them. Add a row (or extend 5.4) naming the lexer reservation and the type-printer setting.
17. HANDOFF:358, HANDOFF:378, HANDOFF:923 vs plan:416. `bayan expand` is referenced by three decisions but has no §1 item and the 5.5 row lists only audit, features and format. Either add `expand` to 5.5 (files, scope) or move it to §6 Deferred and remove "shows them" claims from v0.1 text.
18. plan:388 (J89), plan:201–205 vs §4. §30's cold trap-function emission is a v0.1 codegen decision with no step row. Assign it to 5.3b or 5.3c's `CEmitter.cs` work.
19. handoff:539 (Q115), handoff:543 (Q119) vs plan:381 (J82), plan:415. The decided `string.from` rename under BYN2230 and the BYN2614 message update are placed "in 5.4" by the Questions but the 5.4 row and J82 do not name them. Add both to row 415.
20. HANDOFF:99, plan:213–217, handoff:556 (Q132). Pointers are legal only inside `extern` and `unsafe because`, but no code rejects `T*` elsewhere and Q132 says no `unsafe` block statement is parsed, so 5.1e cannot enforce the rule. Allocate a code (or reuse an existing FFI code, say which) and decide whether `unsafe because` blocks parse in 5.1e.
21. HANDOFF:386 vs plan:84 (BYN6007). `provide` handles "the effects plus `log`" but the BYN6007 handler list omits `suspend`. State explicitly that `suspend` has no handler and BYN6007 names it as unhandleable.
22. HANDOFF:454 (rule 11) vs plan:90 (BYN7003). A `becomes .s` method that returns on a path without `become` has no diagnostic; BYN7003 covers only `become` without `becomes`. Add the missing-transition case to BYN7003 or allocate BYN7009 in 5.3c.
23. plan:72 (BYN3208) vs plan:192 (BYN3216). Both can fire on an assignment to a `requires` field inside the struct's own `ref this` method, and the plan gives no precedence. State that own-method assignments re-check (BYN3208) and foreign assignments reject (BYN3216), never both.
24. HANDOFF:814, HANDOFF:822, HANDOFF:318 with Q34 (`List<T>` → `ReadOnlySpan<T>`). `string(xs)` on a `List<u8>` is ambiguous between formatting the list and decoding bytes via `try string(bytes)`. Say decoding requires the `try` form and the span type exactly, and that `string(List<u8>)` formats.
25. HANDOFF:154 vs plan:24 (BYN1042). The operand same-line rule names only `return`, `break`, `continue`, so whether `fail\n.x` is a bare `fail` is a guess. Add `fail` to HANDOFF:154 and the BYN1042 scope.
26. HANDOFF:320 vs plan:198. The dictionary holds verbs, nouns and adjectives, but the BYN5014 fix adds only verbs and nouns, so an adjective rejection has no fix-it. Say the fix adds the word in whichever class the checker inferred, or state adjectives get no fix.
27. handoff:555 (Q131) vs plan:410. The mixed `it` plus field `requires` rule is left to "5.3b should give it the field above as `at`, or reject it" with no decision. Decide reject (BYN3207 wording extended) and record it in J43.
28. HANDOFF:355. `console` covers "stderr written by `print`" but no stderr form of `print` is specified anywhere in §27 or 5.4. Name the form (`print(x, to: .stderr)` or `eprint`) or delete stderr from the definition.
29. HANDOFF:1037 vs plan:23, plan:29, plan:47, plan:89. Deliverable 2 limits GRAMMAR/STYLE edits to §1, §5, §12, §13, §15, §28, while the plan adds GRAMMAR edits for §3, §4, §7 and §14. Update deliverable 2 to the plan's list.
30. HANDOFF:1066, HANDOFF:7 vs revision 0001:1. The changelog folds §32, pointers, `#line` and BYN1030 into the 2026-10-23 entry and attributes §24–§32 to the signature review, while revision 0001 dates them 2026-10-24. Add a 2026-10-24 changelog line citing revision 0001 and amend HANDOFF:7 to "§24–§31".
Also noticed, not in scope: `docs/bayan-status-for-suq.md:108` lists `expand` as "Not started", which matches finding 17.

## Sections
HANDOFF §2, §5, §11, §13–§15, §17, §20, §27, §28, §30, changelog, deliverables; plan §1, §2 (C17, C20), §3 (J14, J43, J55, J70, J82, J89), §4 rows 5.1a–5.5, §7 totals.

## Codes
Possibly new: BYN7009 (item 22), a pointer-placement code (item 20). Re-homed: BYN1053, BYN1066, BYN3216, BYN3217, BYN5013, BYN7006.

## Sūq files
`std.db` shapes in the HANDOFF (item 4); none in `suq/` checked yet.
