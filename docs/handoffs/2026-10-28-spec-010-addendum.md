# Spec 010 addendum — 2026-10-28 (read after the 2026-10-26 dropoff)

This session reviewed the work landed after the dropoff, set up a stricter landing gate, and began redoing T11–T17. It stopped when the credit ran out. Everything is committed and pushed, and nothing was lost. The three writers that were running were stopped, and their worktrees were committed first.

## Where things stand

| Branch | State |
|---|---|
| `dev` | `feat/010-self-hosted-semantics` merged in with `--no-ff` (landing gate, ctx sections, T10 landed, T11–T17 unticked) |
| `feat/010-self-hosted-semantics` | **The base.** T1–T10 ticked, T11–T39 open. Branch from here. |
| `feat/010-t15-redo` | Redo written; guard passed at `de3ef1c`. Review 1 FAIL: 0 HIGH, 3 MEDIUM (`reviews/T15.md`). The last commit `994d479` is the writer's unfinished fix. **Land first.** |
| `feat/010-t13-redo` | Redo written; guard passed. Review 1 FAIL: 4 HIGH (`reviews/T13.md`). Base merged (Q714). The rulings below are not applied yet. |
| `feat/010-t11-redo` | Writer stopped mid-task (`135b978`): not guarded, not reviewed, and predates Q713/Q714. |
| `feat/010-t12-redo`, `t14`, `t16`, `t17` | Not started; each equals the base. |
| `claude/wonderful-cray-qaf9g3` | Same as the base (the session's push branch). |

Test status of the redo branches at the stop: see *Test status at handoff* at the end (T15 tip is broken WIP; last good de3ef1c).

## Do next, in order

1. **T15** (`feat/010-t15-redo`). Finish the review-1 rulings:
   - MEDIUM 1: capture the type map once, as the C# constructor sets `_types`, and keep it in the Binder `// ── T15 state` section, so lambdas keep the outer map.
   - MEDIUM 3: add an oracle test for a body-less function (`void f()` followed by another declaration) through `bindFunctionBody`, and correct Q594(c).
   - Cite Q713 in Q592. The writer was rewriting the Q590–Q594 line citations when it stopped; the guard's citation check catches stale ones.
   - Then run the guard, a fresh review and `land.sh T15 feat/010-t15-redo`.
2. **T13** (`feat/010-t13-redo`). Apply the rulings:
   - Drop `includeTests` and `skipOwnershipCheck` from the T13 section; they are shared now (Q714).
   - Port the `BindExitCode` loop over `c.exitCodes`, BindBody's `.main` case over `c.mainStatementsFn`/`mainStatements`, and BindTest with `c.testBodies[fn]`. Resolve each `SyntaxRef` against `c.files[f].unit.members`. Mark Q583 as resolved by Q714.
   - Once T15 has landed and the base is merged, `parameterDefault` uses T15's Binder `typeArgs`, set to `fn.typeMap`.
   - Strengthen the tests:
     - `inferErrorSets` fixed point with real bodies;
     - `bindFunction`'s second pass and rollback, where reachable;
     - the order and drain of `bindAll`, not just BYN2014.
   - Q582: write `*Resolved (dispatcher):* checkMutation blocked until T28 designs a binder-free MutationCheck record`, so it does not stay Open.
   - Use `addDiag`/`pushBag` instead of the copies, or say why not.
   - Keep the `isEntry` EntryModule derivation and record that T28 should unify it with T11's.
3. **T11** (`feat/010-t11-redo`).
   - Merge the base.
   - Fill the Q714 shared fields: `includeTests`, `skipOwnershipCheck`, `mainStatementsFn`/`mainStatements` (C# `_mainStatements`, Compilation.cs:541) and `testBodies` (`_testBodies`, :367). Document the `SyntaxRef` indexes used in a Question.
   - After T13 lands: add the `.constraint` arm of `runWork` (calls `t13RunConstraint(ref c, for: in work)`), and have `declare` set `interfaceMembersDeclared` and drain `pendingInterfaces`.
   - `AfterMembersDeclared` belongs to T11; it reports at the instantiation site through T13's fields.
   - Then guard, review, land.
4. **T12, T14, T16, T17.** Give each writer its brief (`wave-brief.py`) plus `reviews/2026-10-28-first-review-<pair>.md` for its findings, and use the redo addendum in *Writer prompt for a redo* below. T12 also owns `exitCodes` (fill it in `declareVariants`, Q714). At most 2 writers at a time.
5. Then T18–T25 and T34–T39 through the same loop, then T26–T33 per the dropoff.

## The loop (the only way to land)

```
python3 -I scripts/selfhost/wave-brief.py <worktree> T13      # writer brief -> <wt>/tmp/010-t13/prompt.md
# writer agent works in the worktree, never commits; ends on GUARD PASS
git -C <wt> add -A selfhost tests docs && git -C <wt> commit -m "feat(010): T13 …"
python3 -I scripts/selfhost/review-brief.py <worktree> T13    # reviewer brief -> <wt>/tmp/010-t13/review.md
# a DIFFERENT, fresh agent writes docs/specs/010-self-hosted-semantics/reviews/T13.md (Commit/Verdict/table)
# FAIL -> dispatcher rules on each finding, writer fixes, commit, review again. Commit every review file (history of why).
git switch feat/010-self-hosted-semantics && bash scripts/selfhost/land.sh T13 feat/010-t13-redo
```

- Worktrees: `git worktree add -b feat/010-tNN-redo tmp/wt/tNN feat/010-self-hosted-semantics`. `tmp/` is gitignored, and each worktree builds independently.
- Before landing, merge the latest base into the task branch. Two tasks merged one after the other conflict only in *Questions* and in ctx.byn sections: keep both sides.
- `land.sh` refuses unless the guard passes on the branch, `reviews/<T>.md` names the branch head (or its parent when the review file is the last commit), says `Verdict: PASS`, and has no open HIGH row. It merges with `--no-ff` and ticks the task.

## What the guard now checks (Q712)

`scripts/selfhost/guard.sh` runs the old checks and then `guard-checks.py`:
- **Oracle.** The test class must call the C# implementation (`Compilation.Create`, `Manifest.Read`, reflection on `Bayan.Semantics`, …). An `expected = new List<string> { "…" }` or an `AssertSection(…, ["…"])` literal is rejected; at most 2 per file are exempt, each tagged `// no-oracle: Qnnn`.
- **Stubs.** No `// stub: <T>` marker and no empty function body may remain, unless marked `(blocked: Qnnn)`.
- **ctx.byn.** Changes are allowed only inside the task's `// ── <T> state` section; no deletions.
- **Gates.** No Question saying "before <T>" may still be Open.
- **Docs.**
  - The task has a Progress log entry.
  - Every `file.byn:N` that a new Question cites must be a real line (not blank, not a brace), and a backticked snippet after it must appear within ±2 lines.
- **Mutations.**
  - Mutants that fail to compile are skipped instead of counted as killed.
  - One line per file may be exempted with `// mutation-exempt: Qnnn` on the line above it, only when the line's sole effect goes through a sibling's stub. Do not move code to dodge a mutant.
- **Coverage.** A line that is dead in the C# too (an internal-error throw, a branch the C# never takes) may be exempted from the coverage gate with `// coverage-exempt: Qnnn` (or `… through N`) on the line above it. The Question cites the `.byn` line and the C# line that shows the path is dead. A Question that cites no line of the marker's span is a guard failure. Do not exempt a line that only lacks a test program. A line that runs only once sibling task T<k> ports its stub `<fn>` takes `// coverage-blocked: Qnnn T<k> <fn>` (or `… through N`) instead, with the Question citing the `.byn` line and the stub (Q838). The guard accepts it while T<k> is open and is not the task under guard; T<k> covers those lines and removes the marker before it lands.

How long the guard takes: about 20–40 minutes per task (8 mutants, each a build plus the task's tests).

## How to test

- **Setup.** `builds.dotnet.microsoft.com` is blocked by the cloud proxy, so `dotnet-install.sh` fails. Use `apt-get install -y dotnet-sdk-10.0` (10.0.1xx satisfies `global.json` 10.0.100 with `latestFeature`). `cc`, `gcc` and `clang` are present. `dotnet run --project src/Bayan.Cli -- doctor` checks the toolchain.
- **Commands.**
  - `dotnet build -v q`, then `dotnet test -v q --filter 'FullyQualifiedName~SelfhostSemantics'`. On the old tip (`a00e69b`) that gave 234 passed, 0 skipped, in about 3.5 minutes.
  - One task: `--filter 'FullyQualifiedName~SelfhostSemanticsT15Tests'`.
  - Full suite: `dotnet test -v q` (2,704 tests on 2026-10-26). `LexerTests.OneMegabyteAllocatesOnlyTheTokenArray` is flaky under load.
- **Probing the language.** Write a snippet to `tmp/probe/<name>/main.byn`, then run `src/Bayan.Cli/bin/Debug/net10.0/bayan check <file>` (0.4 s) or `bayan run <file>`.
- **The model oracle test** is `tests/Bayan.Tests/SelfhostSemanticsT10Tests.cs` (`Expected`). Good redo examples are `SelfhostSemanticsT15Tests.cs` (a fresh C# `Binder` by reflection, Bayan rebuilding the same symbols, diagnostics as `code line:col message |label|help|fix`) and `SelfhostSemanticsT13Tests.cs` (private `Compilation` methods by reflection). When a sibling stub is in the way, choose inputs that avoid it, or build the sibling's output by hand on both sides; never assert a stub's default.

## What went wrong on 27–28 Oct, and the lessons

- **What went wrong.** T11–T17 were landed by an agent whose tests typed the expected lines by hand. Those tests pass trivially, and so did the old guard (tests green, mutants killed). Q711 lists the real defects found by four independent reviews. Examples:
  - T12 `checkOverloads` panics on any struct that has a method.
  - T14 tests the private bit as if it were the static bit.
  - T13 `checkMutation`/`checkRequires` were empty while the progress log said they were ported.
  - Sibling fallbacks: T15 `bindConstructor`, T16 `bindExitCode`/`bindAssert*`, T17 `bindName`/`bindConverted`, and T13's fake `body = 0`.
  - Q704 was skipped, and 8 Questions made false claims.
- **Root causes.**
  1. Nothing compared a port with the C# before it landed.
  2. ctx.byn was frozen without the C# state the ports need, so writers approximated it.
  3. Writers ticked their own tasks.
  4. Mutants that failed to compile counted as killed.

  All four are fixed (Q710, Q712, Q714).
- **Lessons from the redo.**
  - **The review works.** Independent reviewers found real HIGH defects even in guarded redos. Always run it, and give the reviewer the code, never the writer's summary.
  - **Cross-task state has to be decided by the dispatcher up front.** T13 needed state written by T11/T12 (`_exitCodes`, `_mainStatements`, `_testBodies`), so neither task's own section works; that is why the Q714 shared section exists. Expect more of these: when a reviewer flags missing state that another task writes, add a shared field (dispatcher only) rather than letting a writer approximate it.
  - **Land order matters** where a later task reads an earlier task's section (T13 reads T15's `typeArgs`).
  - **Writer turn budgets.** A redo took 100–140 tool calls and 30–75 minutes per task. Reviews took about 5 minutes and about 150k tokens.

## The `in` parameter modifier

The spec makes `in` optional, and a probe on 2026-10-28 confirms it.
- **What the spec says.** Spec §10's table: *(none)* means "read-only; the compiler passes by value or by reference based on size", and `in` means "accepted, redundant; removed by the formatter". The C# comparison table also lists `in` parameters as redundant.
- **Why it exists.** Only so that C# code ports without edits.
- **The probe** (`bayan check`/`run`):
  - A plain parameter of a struct that owns a `List` compiles and runs, at the declaration and at the call site (`countPlain(c.boxes[0])`).
  - Both `countIn(in b)` and `countIn(b)` compile.
  - The aliasing rule (BYN3005, `ref c` together with `c.boxes[0]` in one call) applies either way, so `in` adds nothing.
- **What to do.** New code should leave `in` out. The self-hosted sources carry about 900 uses in `selfhost/semantics` alone, and `010-api.md` signatures use it too. Remove them in one mechanical pass with the formatter (`bayan fmt`, if it implements the removal; otherwise a scripted edit that drops `in ` before a parameter type or argument), plus `010-api.md`, ideally before T18 starts. Keep in mind that `for x in xs` and the range test `x in a..b` use the same keyword.

## Design points worth acting on before T18

1. **Complete the C# state in one pass.** One agent should list every field of the C# `Compilation`, `Binder`, `Conformance` and `BinderExpressions` classes, then add each missing one to ctx.byn: shared fields where two tasks touch them, task sections otherwise. Writers then never invent state. This is the cheapest prevention left.
2. **Port in dependency order rather than all in parallel.** Coupling through stubs is why so many paths cannot be tested against the C# yet (T15's `bindChecks` waits on T38, `bindEnumArguments` on T20; T13's second pass waits on T15–T17 and T20). Doing the declaration tasks, then the binder core, then expressions and calls lets later tasks test against real siblings.
3. **Run the `in` cleanup now** (above).
4. Mutation-exempt lines, coverage-exempt lines and `(blocked: Qnnn)` stubs are debts for T28. Grep for `mutation-exempt:`, `coverage-exempt:` and `blocked: Q` at integration time; no `coverage-blocked:` marker may remain (Q838).

## Writer prompt for a redo (used this session; adapt T/paths)

```
You are the writer for task {T} of spec 010, working ONLY in the git worktree {WT}. Your full brief is {WT}/tmp/010-{t}/prompt.md; it is binding.
This is a REDO: start from the code in your owned files (the earlier attempt), keep what is right, rewrite what is not. Findings: {WT}/tmp/010-{t}/findings.md
(copy docs/specs/010-self-hosted-semantics/reviews/2026-10-28-first-review-<pair>.md there). Verify each finding against the C#, then fix it.
- Tests: every expected line computed from the C# in-process (model: SelfhostSemanticsT10Tests.Expected); diagnostics as `code line:col message`.
- No stand-ins/fallbacks for siblings; no invented branches; no fake handles.
- C# state with no home: your own `// ── {T} state` section of ctx.byn (Q710), or ask the dispatcher for a shared field (Q714).
- Rewrite your Questions to say what the code does; one Progress log entry; do not tick.
- Never commit, merge, checkout, reset or stash. Run builds/tests under `nice -n 19`. Finish only on GUARD PASS; report fixed / judged wrong (with C# line) / blocked (Q number).
```

## Scratch left in worktrees (not in git, not needed)

`tmp/wt/t11/tmp/t11bak` and `tmp/wt/t15/tmp/binder.orig` are older copies taken for break-tests; the commits hold the newest work. `tmp/wt/*/tmp/*-cases` hold generated test inputs, which the tests recreate. On a fresh machine, recreate the worktrees from the pushed `feat/010-tNN-redo` branches.

## Test status at handoff (2026-10-28, each branch's own test class)

- `feat/010-t11-redo` (`135b978`, WIP): builds, `SelfhostSemanticsT11Tests` 24/24 pass. Not guarded.
- `feat/010-t13-redo`: 14/14 pass. Merging the base had declared `includeTests`/`skipOwnershipCheck` twice (T13 section and the Q714 shared section), which broke every T13 test. The tip commit removes the T13 copies, which also applies the first ruling.
- `feat/010-t15-redo`: **the WIP tip `994d479` fails all 37 tests**. The generated main panics `index out of range` at binder.byn:435, because `t15ParameterOwner` returns -1 inside `bindParameterDefault` right after the new `t15CaptureTypes` call. The writer was stopped mid-fix. The last good commit is `de3ef1c` (35/35, GUARD PASS). Diff `de3ef1c..994d479` to finish the fix, or reset the task branch to `de3ef1c` plus the review file and redo the two MEDIUM fixes.
- Base `feat/010-self-hosted-semantics`: the T10 and T4 classes pass after the ctx.byn sections were added. The full SelfhostSemantics suite was 234/234 on the old tip; it was not re-run after Q714.
