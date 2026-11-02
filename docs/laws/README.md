# Laws

Rules this project must never break, each backed by a test that proves it. One law per
file, `NNN-slug.md`, numbered in the order they were written and revised in place.

The pattern is Bend's `LAWS.bend` / `PROOF.bend` split, without the language: the human
states the claim, the agent writes the proof, and the gate refuses a merge while any enacted
law is unproven or failing.

```
deno run -A ~/.claude/tools/laws-check.js          # static: every enacted law has a proof
deno run -A ~/.claude/tools/laws-check.js --run    # and every proof passes
```

Success prints `All laws hold. (N enacted, M proposed)`. Anything else is a stop.

## Who edits what

| Field or section | Owner | Notes |
|---|---|---|
| `status`, `title`, `## Statement`, `## Counterexample looks like` | Human | An agent may create a file as `status: proposed` and may not edit these on an enacted law |
| `status: proposed` -> `enacted` | Human | Or an agent, only when the user says "enact" in that turn |
| `proof`, `check`, `## Proof notes` | Agent | Written by `/prove`, the only fields an agent touches on an enacted law |
| `status: enacted` -> `retired` | Human | A retired law keeps its file; the number is never reused |

A law is a claim about behaviour, not a preference. "Sort returns ascending output for every
input" is a law. "Prefer records over classes" is a convention and belongs in
`docs/conventions.md`. "No React" is a constitution rule; if it can be checked by a test, it
is also a law, and the constitution points here.

## File format

```markdown
---
id: L001
title: Strict mapping rejects an unmapped property
status: enacted
kind: property
scope: src/Extrode.Jaunty/Internals/Read/DrDispatcher.cs
proof: tests/Extrode.Jaunty.UnitTests/Laws/L001StrictMappingTests.cs
check: dotnet test --filter Law=L001
enacted: 2026-09-18
---

## Statement
For every entity type T with at least one public writable property, and every result set
missing a column for one of those properties, `Query<T>` in Strict mode throws
InvalidOperationException naming the property.

## Counterexample looks like
A result set missing `Name`, and `Query<Product>` returning a Product with Name == null.

## Proof notes
Mutation: commented out the throw in DrDispatcher.cs:64; L001 failed with 3 counterexamples.
```

- `id` is `L` plus the file's three-digit number. `laws-check.js` refuses a mismatch.
- `status` is `proposed`, `enacted` or `retired`. Only enacted laws gate.
- `kind` picks the proof shape: `property` (for all generated inputs, an output relation
  holds), `invariant` (after any sequence of operations, a state predicate holds), `guard`
  (a table of inputs and the verdict each must get), `regex` (the function accepts exactly
  the strings matching a spec-cited regular language; Z3 derives the boundary strings of that
  language and a harness checks the function's verdict against every one of them).
- `scope` is the file or symbol the law is about, so `/prove` targets real code.
- `proof` is the test file, relative to the repo root. It must contain the id literally.
- `check` is the command that exits 0 when the law holds. Optional: when absent it is derived
  from the runner (below). Set it when the derived form is wrong for this repo. The gate runs
  it verbatim through the shell and prints it first, so read a `check` line at review the way
  you would read a script: it is agent-written and it executes on the reviewer's machine. A
  check whose output says it ran zero tests (`0 passed`, `pass=0`, `No test matches`) fails.

## Tagging a proof

The id appears in the test so the runner can select it and a reader can find it:

| Runner | Tag | Derived check |
|---|---|---|
| xunit (C#) | `[Trait("Law", "L001")]` on the class | `dotnet test --filter Law=L001` (VSTest mode only; under a `global.json` test runner of Microsoft.Testing.Platform the filter is silently ignored and "Zero tests ran" exits 5, so set `check` to `dotnet build <proj> && dotnet <built>.dll -trait "Law=L001"`) |
| `deno test` | test name starts with `L001:` | `deno test -A --filter "L001:"` |
| node with a `test` script | test name starts with `L001:` | `npm test -- -t "L001:"` |
| pytest | test name starts with `test_l001_` | `pytest -k l001` |
| run-directly Deno suite | `// L001:` comment above the case(s) | none; set `check` to the suite command with its argv, e.g. `deno run -A hooks/x.test.js hooks/x.exe` |

## Writing a law

1. `/laws new <statement>` drafts the file as `proposed` with the next number.
2. Read it. Tighten the statement until a counterexample is describable. Set `status: enacted`.
3. `/prove L###` writes the test, breaks the code to confirm the test fails, restores it, and
   fills `proof` and `check`.
4. `laws-check.js --run` prints `All laws hold.` and `/implement` will not merge without it.
