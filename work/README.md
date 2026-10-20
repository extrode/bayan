# work/

The state of the work underway. Not documentation — documentation is `../docs/`.

| File | Holds |
|---|---|
| `tasklist.md` | Every task for the body of work currently underway. Stays put; only the ticks change. |
| `status/current.md` | The one task in flight. |
| `status/next.md` | The one task queued, pulled from the tasklist. |
| `status/previous.md` | The task that just finished. |
| `todo.md` | Unscheduled backlog. Out-of-scope findings land here as one line. |
| `milestones.md` | Where the project is going. Coarse, rarely edited. |
| `archive/` | Completed tasklists, `YYYY-MM-DD-<slug>.md`, one per shipped body of work. |

The `status/` trio is a three-deep sliding window over `tasklist.md`, one task each. What shipped
goes to `../CHANGELOG.md`, at the repo root.

**Drive it with `task.js`, from anywhere — it takes `--repo` and nothing is installed per repo:**

```
deno run -A ~/.claude/tools/task.js list   --repo .              # the window, plus the counts
deno run -A ~/.claude/tools/task.js init   --repo .              # stamp work/ when it is missing
deno run -A ~/.claude/tools/task.js new    --repo . "<title>"    # append to tasklist.md
deno run -A ~/.claude/tools/task.js start  --repo .              # pull the next task into the window
deno run -A ~/.claude/tools/task.js done   --repo . -e           # roll, tick, changelog, commit
```

`done` is the one that matters. It archives `previous.md` to `done/<NNN>-<slug>.md` by **renaming**,
so `git log --follow` reaches back through the window; then it moves `current` onto `previous` and
`next` onto `current`, ticks the tasklist entry, writes the `## [Unreleased]` bullet and commits
with an explicit pathspec so unrelated staged work is never swept in. Bare invocation is the dry
run; `-e` acts. Exit 0 ok, 2 refused with nothing done, 3 rolled but bookkeeping left over.

Doing the rotation by hand is how the finished task's notes get overwritten. `/task` documents the
flags; `status-roll.js` is the rotation alone, when that is all you want.

Updated in the same branch as the work, never batched to the end of a session.
