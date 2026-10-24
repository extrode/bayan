# 0009 — `bayan format` is in v0.1; the CLI table is one table

Status: accepted 2026-10-24 (agent draft from handoff Question 242, amended in review: items 3–5 added).

## Problem

1. SPEC §21.2 says `bayan format` is "not in v0.1", but plan §29 ships it in v0.1 (`bayan format` and `--check`, J71), and step 5.2 built it: `src/Bayan.Cli/Format.cs` rewrites input forms through the diagnostics' fixes and applies `package.byn` `style` (booleans, primitives). Its prose flags (`--prose`, `--from-prose`, `--check-prose`) belong to §22 prose translation, a separate tool (`bayan-prose`) that v0.1 does not build.
2. The §21.2 command table is split in two by the `--color` / `--changed` paragraph: the rows `bayan pkg` through `bayan new test X` sit after it, so Markdown renders them as stray text, not table rows.
3. §21.2 does not say what `--check` reports or how it exits, which CI depends on.
4. HANDOFF §29 makes `bayan fmt` an unknown command whose message names `bayan format`; §21.2 does not mention it.
5. Revision 0002 item 17 deferred `bayan expand` past v0.1; the §21.2 row must say so if it is not already marked.

## Decision

1. **`bayan format` is in v0.1** with `--check` and the `style` rewrite. The prose flags stay in the row, marked as §22 and not in v0.1.
2. **The table is joined:** the `--color` / `--changed` paragraph moves below the last row. No row's text changes except as below.
3. **`--check` writes nothing,** prints the path of each file that would change, and exits 1 if any would, 0 otherwise (like `gofmt -l` and `prettier --check`). A file that does not parse is reported with its first diagnostic and also exits 1.
4. **One sentence after the table:** "`bayan fmt` is not a command; its message names `bayan format` (HANDOFF §29)."
5. **The `bayan expand` row ends with "not in v0.1 (revision 0002 item 17)"** unless it already says so.

## Sections

SPEC §21.2.

## Codes

None.

## Effect on work already done

- Decision 1: none; `Format.cs` already matches.
- Decision 3: check `Format.cs` against it. If `--check` already behaves this way, documentation only; otherwise a small fix in the step that owns `src/Bayan.Cli/` (5.5 has landed, so no active step owns it; list it in the gate summary).
- Decisions 4–5: documentation only.
- Handoff Question 242 closes when this is applied. The missing `bayan audit` section in `docs/bayan-terminal-style.md` §4 is not a frozen document and stays with that document's own step.

## Sūq files

None.

## Diff

SPEC §21.2, the `bayan format` row:

```diff
-| `bayan format` | Format to canonical form and rewrite input forms (§21.5) (`--check`, `--prose`, `--from-prose`, `--check-prose`); reads `style` from `package.byn` (§19.2); not in v0.1 |
+| `bayan format` | Format to canonical form and rewrite input forms (§21.5); reads `style` from `package.byn` (§19.2). `--check` writes nothing, prints each file that would change and exits 1 if any would. `--prose`, `--from-prose`, `--check-prose` are prose translation (§22), not in v0.1 |
```

SPEC §21.2, layout: the paragraph beginning "Every command takes `--color auto|always|never`" moves from after the `bayan lint, bayan doc, bayan repl, bayan lsp` row to after the `bayan new test X` row, unchanged, followed by the new sentence:

```diff
+`bayan fmt` is not a command; its message names `bayan format` (HANDOFF §29).
```

SPEC §21.2, the `bayan expand` row (only if it is not already marked):

```diff
-| `bayan expand` | … |
+| `bayan expand` | … ; not in v0.1 (revision 0002 item 17) |
```

Check after applying: render §21.2 and confirm every command, from the first row to `bayan new test X`, is a table row.
