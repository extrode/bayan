# Tasklist — bayan

Every task for the body of work currently underway. This file **stays put** while work moves
through it: the `status/` rotation is a three-deep sliding window over this list, not a copy of
it.

The unit is one body of work, not one task and not the whole project. "Migrate the storage layer"
is a tasklist. When that work is done the file is emptied and refilled for the next body of work,
and what shipped is summarised in `CHANGELOG.md`.

## How it feeds the rotation

```
tasklist.md        T1 [x]  T2 [x]  T3 [x]  T4 [ ]  T5 [ ]  T6 [ ]  T7 [ ]
                            |       |       |
                            v       v       v
status/          previous.md  current.md  next.md      <- one task each
```

On each task completion, in this order:
1. `current.md` -> `previous.md`
2. `next.md` -> `current.md`
3. the next unchecked task here -> `next.md`, and tick it off here when it lands

`/status roll` does exactly this. `tasklist.md` is not touched by the rotation except for the
tick.

## Relationship to the neighbours

- `todo.md` is the *unscheduled* backlog: things worth doing that nobody has committed to. A task
  arrives here from there, or from a spec, or from the user directly.
- For Durable-tier work this list is `docs/specs/<NNN>-*/<NNN>-tasks.md`. Point at it rather than
  duplicating it — two copies of a task list is one stale copy.
- `milestones.md` is where the project is going. A tasklist is how one milestone gets done.

## Current work: Bayan v0.1 bootstrap compiler (detail: `docs/bayan-handoff.md`)

- [x] **T1 — M0 skeleton** — done when: CI green on linux-x64/osx-arm64/win-x64; bayan --version runs from the AOT binary
- [x] **T2 — M1 lexer** — done when: lex/ golden cases cover every token kind and lexer diagnostic; 1 MB file allocates only the token array
- [x] **T3 — M2 parser** — done when: parse/ cases cover every v0.1 construct; diag/ cases cover common syntax errors
- [x] **T4 — M3 semantics I** — done when: every names/types/mutability rule has passing and failing diag/ cases
- [x] **T5 — M4 C backend hello world** — done when: examples/hello runs on all platforms; run/ cases per handoff M4
- [x] **T6 — M5 errors** — done when: run/ cases for every error form incl. cleanup order; diag/ for non-exhaustive catch and try in defer
- [x] **T7 — M6 memory basics** — done when: run/ cases for slicing and arenas; diag/ for borrow escape, mutation while borrowed
- [x] **T8 — M7 tests** — done when: bayan test reports passing, failing and panicking tests correctly
- [x] **T9 — M8 tally-lite** — done when: bayan run prints the sample transfers; bayan test examples/tally-lite passes
- [x] **T10 — Q34 ReadOnlySpan and float formatting** — done when: ReadOnlySpan<T> conversions and BYN2310; float output per addendum 001 §1
- [x] **T11 — M12a JSON diagnostics and exit codes** — done when: `--diagnostics json` goldens; exit codes 0-4 asserted in CLI tests
- [x] **T12 — M9 bayan new and init** — done when: tests/cases/new goldens; fresh app runs and tests; init adds missing files only
- [x] **T13 — M10 out/ and dist/** — done when: layout goldens; dist immutability and latest selection
- [x] **T14 — M11 global cache and worktrees** — done when: second worktree compiles nothing; parallel builds never corrupt the cache; moving a project keeps cache hits
- [x] **T15 — M12b CLI completeness** — done when: test --json, color, doctor, test --changed, --version with commit
