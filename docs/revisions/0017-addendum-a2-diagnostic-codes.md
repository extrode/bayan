# Bayān — Addendum A2: diagnostic code scheme

Nov 1, 2026

The scheme is decided now. Renumbering happens once in round 4, after 012. Until then, no existing code changes; this addendum only adds rules for codes allocated mid-flight.

## Why not renumber now

Codes live in five places: the C# compiler, the Bayān compiler (010 is porting diagnostics now), golden `*.diag` files, `docs/diagnostics/` pages and fix-it tests. Renumbering during 010 would collide with T26 (golden harness) and T28 (parity). Doing it once after 012 is one script and one golden regeneration.

## The scheme

Rules:

1. Thousands digit = area. Hundreds digit = category within the area.
2. Classify by the mistake the user made, not by the compiler phase that caught it.
3. `x00` is the generic code of its category (as HTTP 400 and 500).
4. `x900–x999` in every area are style and lints, including input forms with fix-its.
5. Severity is not encoded in the number; projects can change it.
6. Runtime traps (overflow, bounds, failed `expects`) share 9xxx, so `bayan explain` covers them.

| Range | Area |
| --- | --- |
| BYN0xxx | Project, files, `package.byn`, CLI, tools (format, audit, test runner) |
| BYN1xxx | Syntax |
| BYN2xxx | Names and types: resolution, generics, overloads, conversions, units, distinct types |
| BYN3xxx | Values and memory: ownership, moves, `unique`, regions, borrows, unused values |
| BYN4xxx | Control flow and failure: `fails`, `try`, `fail`, `catch`, `switch` exhaustiveness, `defer` |
| BYN5xxx | Grammar and naming: particles, word patterns, naming rules |
| BYN6xxx | Effects and concurrency: `uses`, `provide`, `suspend`, `scope`, thread sending, locks |
| BYN7xxx | Typestate and contracts: `states`, `become`, `final`, `requires`, `expects`, laws |
| BYN8xxx | Trust and security: `Untrusted`, `Secret`, `because`, `@allow`, `sql` |
| BYN9xxx | Build, C backend, linking, toolchain, runtime traps |

Example categories:

| Syntax | | Control flow and failure | |
| --- | --- | --- | --- |
| 1000s | Tokens and literals | 4000s | Reachability |
| 1100s | Declarations | 4100s | `switch` and exhaustiveness |
| 1200s | Statements | 4200s | `fails` lists |
| 1300s | Expressions | 4300s | `try` and propagation |
| 1400s | Patterns | 4400s | `catch` |
| 1500s | Attributes | 4500s | `defer` family |
| 1900s | Input forms (fix-its) | 4900s | Lints |

The full category table for every area is written in round 4, with the mapping.

## Rules while 010–013 run

- [ ] No existing code is renumbered or moved.
- [ ] A new code takes the next free number in the current allocation, as today.
- [ ] Every new or reserved code carries a target comment, for example `// target: 41xx switch`, so the round-4 script can map it.
- [ ] Round-4 reservations sit together in one `// Reserved: round 4` block in `DiagnosticCodes.cs`.
- [ ] A test fails if a live code reuses a reserved number (add it if it does not exist; about ten lines).
- [ ] Docs pages keep current numbers; no pages are created under new numbers yet.

## Answers to the agent

- **SwitchExpressionNeedsElse:** keep `BYN1071` in the parser range now. Tag it `// target: 41xx switch`. It moves at renumbering, because the scheme classifies by the mistake (an incomplete `switch`), not the phase.
- **Round-4 codes as comments:** acceptable until round 4, in one reserved block, with the collision test above.

## Round 4 renumbering steps

1. Write the full category table per area.
2. Generate the old-to-new mapping by script from the target comments and a manual pass.
3. Apply it to both compilers, goldens, fix-it tests and docs in one commit.
4. Keep old codes as aliases for one edition: `bayan explain BYN1040` resolves, and old docs URLs redirect.
5. Freeze codes at 1.0.

## Applied (2026-11-01)

Filed from `addendum-A2-diagnostic-codes.md`; the text above is unchanged. On `docs/a2-diagnostic-codes`:

- The four codes revision 0016 reserved as comments now sit in one `// Reserved: round 4` block at the end of `DiagnosticCodes.cs`, ended by `// End reserved: round 4.`, one line each with a target:

| Code | Name | Target |
|---|---|---|
| BYN1070 | `NegatedIsInput` | `19xx input form` |
| BYN1071 | `SwitchExpressionNeedsElse` | `41xx switch` (stays in the parser range until round 4) |
| BYN2825 | `BorrowedClosureStored` | `3xxx borrows` |
| BYN7013 | `FinalStateNotReached` | `7xxx typestate` |

- `DiagnosticCodesTests.NoLiveCodeReusesAReservedNumber` reads the block. It fails if the block is missing or empty, if a line lacks `// target:`, if a number repeats, or if a code in `DiagnosticCodes.All` equals a reserved number. It was proven by breaking it: a reserved BYN7012 (live `AssignmentChangesState`) and a line without its target each fail it.
- No code was renumbered, no docs page was added, and no descriptor changed. The older reserved comments (BYN3213, BYN3215, BYN3217) are v0.1 reservations, not round 4's, and stay where they are.
- In-flight writers are told to add `// target:` to any new code.
