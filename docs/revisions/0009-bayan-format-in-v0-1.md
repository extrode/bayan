# 0009 — `bayan format` is in v0.1; the CLI table is one table

Status: draft 2026-10-24 (agent, from handoff Question 242). Awaiting the user.

## Problem

1. SPEC §21.2 says `bayan format` is "not in v0.1", but plan §29 ships it in v0.1 (`bayan format` and `--check`, J71), and step 5.2 built it: `src/Bayan.Cli/Format.cs` rewrites input forms through the diagnostics' fixes and applies `package.byn` `style` (booleans, primitives). Its prose flags (`--prose`, `--from-prose`, `--check-prose`) belong to §22 prose translation, a separate tool (`bayan-prose`) that v0.1 does not build.
2. The §21.2 command table is split in two by the `--color` / `--changed` paragraph: the rows `bayan pkg` through `bayan new test X` sit after it, so Markdown renders them as stray text, not table rows.

## Decision

1. **`bayan format` is in v0.1** with `--check` and the `style` rewrite. The prose flags stay in the row, marked as §22 and not in v0.1.
2. **The table is joined:** the `--color` / `--changed` paragraph moves below the last row. No row's text changes.

## Sections

SPEC §21.2.

## Codes

None.

## Effect on work already done

None: `Format.cs` already matches decision 1. The handoff Question 242 closes when this is accepted. The missing `bayan audit` section in `docs/bayan-terminal-style.md` §4 is not a frozen document and stays with that document's own step.

## Sūq files

None.

## Diff

SPEC §21.2, the `bayan format` row:

```diff
-| `bayan format` | Format to canonical form and rewrite input forms (§21.5) (`--check`, `--prose`, `--from-prose`, `--check-prose`); reads `style` from `package.byn` (§19.2); not in v0.1 |
+| `bayan format` | Format to canonical form and rewrite input forms (§21.5) (`--check`); reads `style` from `package.byn` (§19.2). `--prose`, `--from-prose`, `--check-prose` are prose translation (§22), not in v0.1 |
```

SPEC §21.2, layout: the paragraph beginning "Every command takes `--color auto|always|never`" moves from after the `bayan lint, bayan doc, bayan repl, bayan lsp` row to after the `bayan new test X` row, unchanged.
