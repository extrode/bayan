# Revisions

After round 3's documentation deliverables, `docs/bayan-spec.md`, `docs/bayan-grammar.md` and `docs/bayan-style.md` change only through a numbered revision here (HANDOFF round 3 §32).

- File: `NNNN-title.md`, four digits, next = highest + 1, kebab-case title.
- Sections: **Problem**, **Decision**, **Sections** (spec sections changed), **Codes** (diagnostics added, changed, retired), **Sūq files** (affected or added; a revision that adds a feature adds its Sūq use), and, when it amends a design document, **Effect on work already done** and the **Diff**.
- A revision is applied to the spec in one commit; the file stays as the record.
- The agent never changes the frozen documents on its own: a conflict it finds becomes a draft revision (`Status: draft`) for the user to accept.
- New features are built from the current design documents; a revision's diff drives the changes to features already built.
