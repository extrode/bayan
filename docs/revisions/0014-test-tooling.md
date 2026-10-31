# 0014 — Test tooling: mutation operators, coverage, `@unobservable`, statement attributes

Status: applied 2026-11-01 by spec 013 T17 (accepted 2026-10-31, user decision on spec 013).

## Problem

SPEC §20 promises coverage and mutation testing in two bullets and marks `@unobservable` "not in v0.1". Spec 013 builds both. The spec does not list the mutation operators, the report format, what counts as a covered error path, or where `@unobservable` may appear. Attributes appear only on declarations, interface members and error variants, so a statement-level mark has no syntax. §19.2's `Tests` has no line or branch minimum and no way to turn off the default coverage summary.

## Decision

1. SPEC §20, the coverage bullet becomes: "Coverage: lines, branches and **error paths**. An error path is a `fails` variant of a function that a test makes the function return, whether the function raises it with `fail` or passes it on through `try`; the detailed report names where it came from. `bayan test` prints a one-line coverage summary in Debug builds (package opt-out `summary: false`); `bayan test --coverage` prints the full report, a per-line listing and `bayan-quality.json`. A test killed on timeout leaves its coverage unknown, never uncovered. `--sanitize` runs show coverage only with `--coverage`. `bayan build` reports public functions no test reaches."
2. SPEC §20, the mutation bullet becomes: "Mutation testing compiles every mutant into one build (mutant schemata) and picks one per run: `bayan test --mutate`. Each mutant is killed, survived, timed out (counted as killed), not reached, or marked. The report names each surviving mutant's file, line, column, original and mutated text, suggests the missing case, and gives build time, mean and maximum time per mutant, and per-mutant time as a share of the build. `bayan mutate --binary <exe> --sites <map> -- <cmd>` runs an outside command per mutant against a prebuilt mutant binary. A mutation that would not type-check is never emitted; the report counts these as not emitted."
3. SPEC §20 gains the operator table:

   | Operator | Changes |
   |---|---|
   | `boundary` | `<` ↔ `<=`, `>` ↔ `>=` |
   | `relational` | `==` ↔ `!=`, `<` ↔ `>=`, `>` ↔ `<=` |
   | `logical` | `&&` ↔ `||` |
   | `condition` | the condition of `if`, `unless`, `while`, do-while, a switch guard, `stmt if c` → its negation |
   | `bool` | `true` ↔ `false` |
   | `arithmetic` | `+` ↔ `-`, `*` ↔ `/`, `%` → `*` |
   | `literal` | an integer literal `n` → `n + 1`, `n - 1` |
   | `negation` | unary `-x` → `x` |
   | `statement` | a call with no result, an assignment or a compound assignment is removed |
   | `emptyReturn` | `return x` → the type's empty value, where the type has one |
   | `variant` | `fail E.a` → another variant of `E` with the same payload |

   Each change is one mutant; an operator that does not apply to the operand types is not emitted.
4. SPEC §20, `@unobservable` (the "not in v0.1" note is removed): "`@unobservable because \"…\"` on a statement marks that statement's mutants as ones no test can observe; an operator argument narrows it to one operator. Marked mutants still run: a marked mutant a test kills, or a mark with no mutant in its statement, is a warning in the report. The reason is required (BYN8001)."

   ```
   i32 clamp(i32 x, i32 lo, i32 hi)
       expects lo <= hi {
       @unobservable(boundary) because "x == hi returns hi either way"
       return hi if x >= hi
       return lo if x <= lo
       return x
   }
   ```
5. SPEC §4.4 (attributes), §7 (statements) and GRAMMAR: a statement may be preceded by attributes from an allow-list, which applies to the next statement only. The list is `@unobservable`. Any other attribute before a statement is BYN1069.
6. SPEC §19.2, `Tests` gains `lines`, `branches` (minimum percentages) and `summary` (bool, default `true`). `errorPaths` and `mutation` keep their meaning; `diffCoverage` is accepted and not enforced yet. A run below a minimum fails and names what is missing. The example becomes `test: Tests { lines: 100, branches: 100, errorPaths: 100, mutation: 75, nightly: [.fuzz] }`.
7. The runtime source (`bayan-rt.c`) carries a version line, bumped on every change, so a change to Debug binaries can be traced to a runtime version. The coverage flush hook does nothing unless coverage is on.

## Sections

- SPEC §4.4: the attribute paragraph (statement attributes, allow-list).
- SPEC §7: the statement rule (optional leading attributes).
- SPEC §19.2: the manifest example and the `Tests` field list.
- SPEC §20: the coverage bullet, the mutation bullet, the operator table, `@unobservable`.
- GRAMMAR: the statement rule gains an optional attribute list.

## Codes

- BYN1069: added, "attribute not allowed before a statement" (help names the allow-list).
- BYN8001: unchanged; it now also covers `@unobservable` without a reason.

## Sūq files

None changed. A Sūq use of `@unobservable` is added when spec 013's first mutation task finds a real unobservable mutant in Sūq code.

## Effect on work already done

None: no code parses statement attributes or reads `test:` fields today (`src/Bayan.Syntax/Packages/Manifest.cs:27` knows only the field name). The spec 010 guard's `// mutation-exempt: Qnnn` comments are a separate, temporary mechanism; spec 013 replaces them with `@unobservable` when the guard moves to `bayan mutate` (R14).
