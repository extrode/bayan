# Spec 010 addendum — 2026-10-29 (read after 2026-10-28 addendum)

This document tracks progress made since the 2026-10-28 addendum: landing of T15, T13, and T11 redos, the current state of `dev` and `feat/010-self-hosted-semantics`, and the in-progress T18 task.

## Current State of Branches and Worktrees

| Branch / Worktree | Commit | State |
|---|---|---|
| `dev` | `0c472cd` | Merged `feat/010-self-hosted-semantics` with T15, T13, and T11 redos landed. Working tree clean. |
| `feat/010-self-hosted-semantics` | `08d9b24` | **The base.** T1–T11, T13, T15 ticked. Tasks T12, T14, T16–T39 open. |
| `feat/010-t18` (`tmp/wt/t18`) | `c56a7eb` | **Active task.** Method-for-method port of `BinderExpressions.cs:600–1155` into `selfhost/semantics/express2.byn`. 27/27 oracle tests pass. Guard passed all checks except 5 surviving mutations. Working tree clean. |
| `feat/010-t12-redo`, `t14-redo`, `t16-redo`, `t17-redo` | remote heads | Not started; pending redos per the 2026-10-28 review findings. |

---

## What Landed Since 2026-10-28

1. **T15 redo** (`feat/010-t15-redo`):
   - Review 1 MEDIUM findings resolved (captured type map once via `t15CaptureTypes` so lambda bodies keep outer map; oracle test for body-less functions; citations updated).
   - Guard passed with all mutations killed.
   - Review passed (`docs/specs/010-self-hosted-semantics/reviews/T15.md`).
   - Landed via `land.sh T15 feat/010-t15-redo` (`618094b`), ticked (`5c07606`).

2. **T13 redo** (`feat/010-t13-redo`):
   - Applied review rulings: used Q714 shared state (`c.exitCodes`, `c.mainStatements`, `c.testBodies`).
   - Drained exit codes, main statements, and test bodies in `bindAll`.
   - Guard passed with 15/15 tests passing against C# oracle.
   - Review passed (`docs/specs/010-self-hosted-semantics/reviews/T13.md`).
   - Landed via `land.sh T13 feat/010-t13-redo` (`df97559`), ticked (`c746100`).

3. **T11 redo** (`feat/010-t11-redo`):
   - Wired Q714 shared fields: `includeTests`, `skipOwnershipCheck`, `mainStatementsFn`/`mainStatements`, `testBodies`.
   - Handled `afterMembersDeclared` and `runWork` constraint dispatch (`t13RunConstraint`).
   - Guard passed with 24/24 oracle tests passing.
   - Review passed (`docs/specs/010-self-hosted-semantics/reviews/T11.md`).
   - Landed via `land.sh T11 feat/010-t11-redo` (`e31d788`), ticked (`08d9b24`).

4. **Merge to `dev`** (`0c472cd`):
   - Merged `feat/010-self-hosted-semantics` into `dev` with `--no-ff`.

---

## Immediate Next Task: Finish T18 (`feat/010-t18`)

The task is in worktree `tmp/wt/t18` on branch `feat/010-t18`.

### What is done in T18:
- Ported all assigned entry points in `selfhost/semantics/express2.byn`:
  - `constantRef`
  - `lookupMember`
  - `bindImplicitThisMember`
  - `bindMember`
  - `bindValueMember`
  - `bindIndex`
  - `bindSlice`
  - `interpolationSource`
- Static member binding for `Io` (`stdout`, `stderr`, `stdin`), `Hex`, `Symbol`, `Utf8`, `string`, `Enum`, `Union`, and `Struct`.
- Value member dispatch, synthesized getters, and error/union field narrowings (`b.errorNarrowed`, `b.narrowed`).
- Index and slice expressions, from-end indexing (`^k`), bounds checking, and string indexer diagnostics (BYN2201).
- 27 unit tests in `tests/Bayan.Tests/SelfhostSemanticsT18Tests.cs` comparing directly against C# oracle in-process. All 27 pass.
- Handoff Questions 605–609 documented and resolved; Progress log entry written.
- Working tree committed and clean at `c56a7eb`.

### What remains for T18:
Running `bash scripts/selfhost/guard.sh --base feat/010-self-hosted-semantics --allow 'selfhost/semantics/express2.byn,tests/Bayan.Tests/SelfhostSemanticsT18Tests.cs' --filter 'FullyQualifiedName~SelfhostSemanticsT18Tests' --mutate selfhost/semantics/express2.byn` currently reports 5 surviving mutations:
1. `selfhost/semantics/express2.byn:311`:
   `if c.errorVariants[v].name != name {`
   -> Needs a test where an error set has multiple variants, resolving a member that matches one but not others.
2. `selfhost/semantics/express2.byn:482`:
   `if foundProp == -1 {`
   -> Needs a test where a property lookup fails / falls through to methods or errors.
3. `selfhost/semantics/express2.byn:635`:
   `if d.readOnly || c.functions[foundMember].isMutating {`
   -> Needs a test checking mutating method access on a read-only struct receiver.
4. `selfhost/semantics/express2.byn:756`:
   `if isCalleeMatch && (b.calleeOf is { } uCall) && uCall.args.length != 0 {`
   -> Needs a test verifying callee match with non-empty call arguments.
5. `selfhost/semantics/express2.byn:861`:
   `if c.errorVariants[v].name != name {`
   -> Needs a test for variant lookup in error union cases.

### Steps to Land T18:
1. In `tmp/wt/t18`: add oracle test cases in `SelfhostSemanticsT18Tests.cs` covering the 5 mutant sites.
2. Re-run `guard.sh` until output is `GUARD PASS`.
3. Commit all changes: `git -C tmp/wt/t18 commit -am "feat(010): T18 expression binding B"`.
4. Run review brief: `python3 -I scripts/selfhost/review-brief.py tmp/wt/t18 T18`.
5. Have an independent review verify against C# and write `docs/specs/010-self-hosted-semantics/reviews/T18.md` with `Verdict: PASS`.
6. Land via:
   ```bash
   git switch feat/010-self-hosted-semantics
   bash scripts/selfhost/land.sh T18 feat/010-t18
   ```

---

## Order of Subsequent Work

After landing T18:

1. **Redo T12** (`feat/010-t12-redo`):
   - Member declarations B (`declare.byn`, `Compilation.cs:655–1037`).
   - Review findings in `docs/specs/010-self-hosted-semantics/reviews/2026-10-28-first-review-t11-t12.md`.
   - T12 owns `exitCodes` (fill in `declareVariants`, Q714).
   - Write oracle tests, guard, review, `land.sh T12`.

2. **Redo T14** (`feat/010-t14-redo`):
   - Conformance checking (`conformance.byn`, `Compilation.cs:1953–2299`).
   - Review findings in `reviews/2026-10-28-first-review-t13-t14.md`.
   - Guard, review, `land.sh T14`.

3. **Redo T16** (`feat/010-t16-redo`):
   - Binder statements B (`binder2.byn`, `Binder.cs:500–979`).
   - Review findings in `reviews/2026-10-28-first-review-t15-t16.md`.
   - Guard, review, `land.sh T16`.

4. **Redo T17** (`feat/010-t17-redo`):
   - Expression binding A (`express.byn`, `BinderExpressions.cs:1–599`).
   - Review findings in `reviews/2026-10-28-first-review-t17-t18.md`.
   - Guard, review, `land.sh T17`.

5. **Proceed to T19–T25 and T34–T39**:
   - Follow the same loop: wave brief -> writer in isolated worktree -> guard pass -> review brief -> independent review PASS -> `land.sh`.
   - Maximum 2 writers concurrently.

---

## Important Rules & Reminders

- **Landing Gate**: Spec 010 tasks ONLY land via `scripts/selfhost/land.sh`. Never merge directly or tick manually.
- **Oracle Tests**: All tests in `SelfhostSemanticsT*Tests.cs` must invoke the C# compiler/binder in-process via reflection and assert matching output. No hand-typed expected lines.
- **Redundant `in` Modifier**: Per spec §10 and the 2026-10-28 probe, parameter `in` modifier is redundant; leave it out of new Bayan code.
- **Cross-Task State**: Add shared fields to the Q714 shared section of `ctx.byn` when multiple tasks need them; do not invent local approximations.
