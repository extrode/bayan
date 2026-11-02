# Move Bayān to the Ikhbat Foundation

Date: 2026-11-02. Tier: Standard. Status: rev 3 (reviews A, B and C folded in, the user's choices
applied), waiting for the user's approval.

## Goal

| | From | To |
|---|---|---|
| GitHub | `github.com/extrode/bayan` | `github.com/ikhbat-org/bayan` (org `ikhbat-org`, "Ikhbat Foundation") |
| mb1 checkout | `/Users/sy/Developer/extrode.com/bayan` | `/Users/sy/Developer/ikhbat.org/bayan` |
| wp1, wlt1 checkout | `C:\home\code\extrode.com\bayan` | `C:\home\code\ikhbat.org\bayan` |
| Claude project key (mb1) | `-Users-sy-Developer-extrode-com-bayan` | `-Users-sy-Developer-ikhbat-org-bayan` |
| Home page | none | `https://bayan.run` (registered 2026-11-02, Cloudflare) |

Nothing is lost: every session (resumable at the new path), memory, lesson, prompt history, worker
key, release and the review watermark is reachable at the new location, and the old GitHub URL
keeps redirecting.

## User decisions (2026-11-02)

- Finish worktree business before the move, so no linked worktree moves. Done: `docs/011-plan`
  merged (52f5c35e), `.worktrees/worker-plan-011` removed by the user's cleanup run; `git worktree
  list` shows the main checkout only.
- Transcripts: rewrite the recorded `cwd` so `claude --resume` at the new path accepts old sessions.
- `~/.claude/history.jsonl`: rewrite `project`, with a backup.
- Trusted-directory entry in `~/.claude.json`: moved to the new path (step 2.7).

## Facts this plan relies on (measured 2026-11-02)

- `gh api orgs/ikhbat-org`: exists, 12 private repos, none named `bayan`. The org's Actions
  policy is unread (the token lacks `admin:org`); the user checks it in the web UI before step 6.
- `extrode/bayan`: public; no Actions secrets, rulesets, webhooks or deploy keys. Workflows use
  GitHub-hosted runners only and `$GITHUB_REPOSITORY`, so nothing in `.github/` names the owner.
  Release `v0.1.0` has 7 assets (6 archives + SHA256SUMS). GitHub branches: `dev`, `main`.
- GitHub transfer moves issues, releases, tags, Actions history and settings, and redirects the old
  web and git URLs for as long as no new `extrode/bayan` is created.
- `origin/dev` = 52f5c35e (pushed by the user); local `dev` = 64ed9dba (011 rev 4 merged since).
  Local branches with commits not on any remote: `dev`, `docs/011-rev4` (in dev), `fix/q745` (in
  flight; its worktree lands or is removed before step 7), `docs/ikhbat-move-plan` (merges in step 1). Local refs move with
  the directory, so nothing depends on pushing them, but the transfer check compares `dev`.
- Review watermark: local config `review.reviewedthrough` = e27f0daf (no `refs/review/reviewed`
  locally or on origin). It lives in `.git/config` and moves with the checkout.
- Tracked files naming the old path: `docs/misc/b-trees.md` (three `file:///…` links),
  `scripts/cleanup/016-spec-013-and-011-draft.sh` (`/home/code/extrode.com/bayan`).
  `docs/laws/README.md` names `Extrode.Jaunty`, unrelated, unchanged.
- mb1 Claude state naming the path or its key. Counts drift while sessions run (transcripts 911
  then 912, history 336 then 338), so the script computes every count at run time; these are
  orientation only:

  | Where | Count | Handled by |
  |---|---|---|
  | `projects/-Users-sy-Developer-extrode-com-bayan*` dirs | 368 (main 271 MB, 29 memory files) | rename (2.5) |
  | transcript files with a `"cwd":"<old>…"` line | 911 | rewrite `cwd` (2.6) |
  | `lessons/-Users-sy-Developer-extrode-com-bayan*` dirs | 163 | rename (2.5) |
  | `lessons/INDEX.md` lines naming the key | 274 | rewrite (2.6) |
  | `history.jsonl` rows with `"project":"<old>"` | 336 | rewrite (2.8) |
  | `history.jsonl` rows with the old path in `display` (message text) | 10 | not rewritten, on purpose |
  | `lessons/queue.jsonl` rows (`transcript`, `cwd`, `project`) | 5 | backup + rewrite (2.8), line by line |
  | `sessions/*.json` `cwd` of live sessions; `daemon.lock` `spawnedBy.cwd` | 3 + 1 | not rewritten: preflight refuses while any is live; files left after their process ends are stale |
  | `~/.claude.json` `projects["<old>"]`, `githubRepoPaths["extrode/bayan"]` | 1 + 1 | rewrite (2.7) |
  | `workers/registry.json` string fields: `cwd` 331, `repo` 114, `worktree` 114, `criteria` 74 | 633 | rewrite (2.8) |
  | `jobs/c6b5c540/state.json` (`cwd`, `linkScanPath`); `daemon/roster.json` (this job's worker) | 2 | this job ends before step 7; state.json rewritten (2.8) |
  | `scripts/cleanup/mac-background-services{,.test}.sh` | 2 files | step 3 |

  The key prefix matches only bayan's own dirs (344 `--worktrees-*`, 18 `-tmp-wt-*`, 5
  `-tmp-bench-*`, 1 main); no `extrode.com/bayan-x` sibling exists. Accepted as history, not
  rewritten: `workers/dispatch-log.jsonl`, `workers/logs/`, `file-history/`, `~/.claude/tmp/`,
  `backups/.claude.json.backup.*`, `jobs/*/timeline.jsonl`, `~/.cache/zig`, the repo's `.cache/`
  and `obj/` (rebuilt in step 10), message text inside transcripts. Also accepted: `tmp/bench/repo`,
  a nested scratch repo whose 7 linked worktrees hold absolute `gitdir` paths under `OLD_REPO`; they
  break after the `mv`, and the 5 `-tmp-bench-*` session dirs point into them. Scratch, not repaired.
- Running Claude processes take several forms: `~/.local/bin/claude` (incl. `claude daemon run`),
  `~/.local/share/claude/versions/<v>` (basename is the version), `ClaudeCode.app/…/claude
  --bg-pty-host`, and a bare `claude` argv (PID 89218, interactive). Live sessions with the old cwd
  (`sessions/*.json`, 2026-11-02): this one (bg job c6b5c540), 89218 (interactive), 10319 (a
  `--print` worker); the daemon was spawned from an old-path worktree (`daemon.lock`). Job files
  carry `.state` (`done`/`working`), not `status`.
- Vault (`~/dotfiles/claude-vault`, keyed by git remote): no bayan entry. The slug for the new
  remote is `github.com--ikhbat-org--bayan`.
- wp1 and wlt1: checkout at `C:\home\code\extrode.com\bayan` on `feat/013-test-tooling` at
  fbecfff1 (in dev; not a GitHub branch), `origin` = a local bundle, nothing unpushed, no stash,
  one untracked file `tmp-t19-run.ps1`. No bayan Claude state. `C:\home\code\ikhbat.org` exists
  on wp1 only.
- `~/.claude` worktrees `.worktrees/feat/agy-shell-class` and `.worktrees/fix/agy-session-settings`
  carry their own copies of `mac-background-services{,.test}.sh` with the old path.
- Disk: 553 GiB free; APFS, so `cp -c` backups are clones.

## Decisions

| # | Question | Choice | Rejected |
|---|---|---|---|
| D1 | URL | `https://bayan.run`: registered, and it is the CLI verb | bayan.sh, bayan.build |
| D2 | Old mb1 path | No symlink: a session started there would re-create the old key | symlink for a transition |
| D3 | Transcripts | Rewrite only the `"cwd"` field, prefix-matched with a `/` or `"` boundary; clone backup first | leave as is (resume filter rejects); rewrite every mention |
| D4 | wp1/wlt1 | Move the dir, set `origin` to GitHub, switch to `dev` | fresh clone |
| D5 | GitHub transfer | The agent runs it after the user's "yes" in that turn | — |
| D6 | bayan.run DNS | Out of scope; a Cloudflare redirect rule is the user's | — |

## Order (each step checks the previous one)

1. **Repo edits** on `chore/ikhbat-move` off dev (this branch merged in), merge `--no-ff`:
   - `docs/misc/b-trees.md`: the three `file:///…` links become relative.
   - `README.md`: one line naming `https://bayan.run` and `github.com/ikhbat-org/bayan`.
   - `CHANGELOG.md` `## [Unreleased]`: "Changed: the repository moved to
     `github.com/ikhbat-org/bayan` (Ikhbat Foundation); home page `https://bayan.run`."
   - `scripts/cleanup/016-spec-013-and-011-draft.sh`: Windows path → `/home/code/ikhbat.org/bayan`.
   - The move script and its test (step 2), the handoff progress-log entry.
2. **Move script** `scripts/move/2026-11-02-ikhbat-org.sh` + `.test.sh`, written by
   `codegen-lite`, gated against this plan. Dry run by default, `--execute` acts. Every path from
   variables (`OLD_REPO`, `NEW_REPO`, `CLAUDE_HOME`, `CLAUDE_JSON`, `NEW_REMOTE`, `PGREP`)
   defaulting to the real ones. Every rewrite matches `OLD_REPO` or the key prefix (fixed strings,
   never regex built from the path) only when the next character is not a name character
   `[A-Za-z0-9._]`, or at end of string: so `/`, `-`, `"`, `)` and space all end a match, and
   `bayan-x`/`bayan.x`/`bayan_x` never do (INDEX.md has 16 `(key)` tags; queue.jsonl has the key
   before `"` and space). Each step prints `would:`/`run:`/`skip:`/`refuse:` and is idempotent: the
   manifest records finished steps, a rerun after an interruption skips them, and a step whose
   target already holds the new form is a `skip:`.
   1. Preflight, all refusals before any change. With no manifest every check below applies. With
      a manifest (a resumed run) the checks a finished step made obsolete are replaced by the
      manifest's recorded state: after step 3, `NEW_REPO` must exist and `OLD_REPO` be absent;
      after step 5, the recorded targets must exist and their sources be absent. Process checks
      always apply.
      - no Claude process: `pgrep -fl` for `/claude/versions/`, `ClaudeCode.app`, `bin/claude`,
        and `pgrep -x claude` for a bare argv; the script prints each PID and command line and
        stops nothing itself;
      - no `sessions/*.json` whose `pid` is alive names an old path; `daemon.lock` names no live
        PID;
      - `daemon/roster.json` and every `jobs/*/state.json` with `.state != "done"` name no old
        path;
      - `NEW_REPO` and every target project/lesson dir absent;
      - `OLD_REPO` clean, `git worktree list` shows the main checkout only;
      - `git ls-remote "$NEW_REMOTE" refs/heads/dev` equals local `dev`;
      - `jq` and `perl` present.
   2. Backup to `~/.claude/backups/bayan-move-2026-11-02/`: `~/.claude.json`,
      `workers/registry.json`, `lessons/INDEX.md`, `lessons/queue.jsonl`, `history.jsonl`, `jobs/*/state.json` that name
      the path, and `cp -c` clones of every transcript the rewrite touches. Manifest: each dir
      rename (old, new) and each file rewritten.
   3. `mkdir -p` the parent; `mv OLD_REPO NEW_REPO`.
   4. In `NEW_REPO`: `git remote set-url origin "$NEW_REMOTE"`, `git -C "$NEW_REPO" fetch -q origin`.
   5. Rename every `projects/` and `lessons/` dir starting with the old key prefix to the new
      prefix (suffix kept); refuse the step on any existing target.
   6. Transcripts in the renamed project dirs (`*.jsonl`, subagent files included): `perl -pi`
      replacing `"cwd":"<OLD_REPO>` at a boundary with `"cwd":"<NEW_REPO>`. `lessons/INDEX.md`:
      the key prefix. Line counts and byte sizes compared before/after (size changes by exactly
      the path-length delta times the replacements).
   7. `~/.claude.json` via `jq` to a temp file then `mv`: every `projects` key at `OLD_REPO` or
      below moves to the same key under `NEW_REPO`; `githubRepoPaths`: drop `extrode/bayan`, set
      `ikhbat-org/bayan` to `[NEW_REPO]`. Refuses if the output is invalid or the top-level key
      count changed.
   8. Same `jq` discipline, every string value at a boundary: `workers/registry.json` (`cwd`,
      `repo`, `worktree`, `criteria`), `history.jsonl` (`project` only, line by line, line count
      unchanged; `display` is message text and stays), `lessons/queue.jsonl` (`transcript`, `cwd`,
      `project`, line by line, line count unchanged), `jobs/*/state.json` (`cwd`, `linkScanPath`,
      the project key in session paths).
   9. `undo.sh` in the backup dir reverses 3–8 from the manifest and backups; idempotent.
   10. Verify: zero `projects/`/`lessons/` dirs with the old prefix; renamed counts equal the
       manifest's; `grep -c` of old-path `cwd` lines in the new dirs is 0; the two `.claude.json`
       keys read back; `git -C NEW_REPO status -sb` and `remote -v`; `OLD_REPO` absent.

   The test runs on a fixture `$HOME` under the repo's `tmp/` and covers: dry run changes nothing
   (tree hash before/after); each preflight refusal (each process form via a stub `PGREP`, the
   bare-argv form included; a live `sessions/*.json`; a job with `"state":"working"` and one with
   `"state":"done"` that must not refuse; dirty repo, linked worktree, ls-remote mismatch, target
   exists); boundary cases (`/…/bayan-x`, `-…-bayan-x`, `bayan.x` untouched; `"cwd":"<old>/sub"`,
   `(key)`, key before `"` and space rewritten); a full execute (repo moved, remote set, dirs
   renamed with suffixes, transcripts' `cwd` rewritten and other bytes equal, INDEX,
   `.claude.json` keys moved and other keys byte-equal, registry, history with a `display` row
   left unchanged, queue.jsonl, job state); runs interrupted after steps 3, 5 and 6, each resumed
   to the same end tree as an uninterrupted run; `undo.sh` restores the starting tree hash, twice.
3. **~/.claude edits** (branch off its dev, merge `--no-ff`): `mac-background-services.sh`
   (the variable and the comment at line 12) and `.test.sh` (`bayan=` at line 14) name the new
   path. The two open agy worktree branches keep the old copy; noted in `~/.claude`'s
   `work/todo.md` to take dev's version when they merge.
4. **Review** of steps 1–3 (`review`, both lenses), fix, merge both repos into dev.
5. **User pushes dev** (`git push origin dev`). The transfer check in step 6 and the preflight need
   GitHub's `dev` to equal local `dev`. The user also checks the org's Actions policy
   (ikhbat-org → Settings → Actions → General: actions allowed, public-repo runners usable).
6. **GitHub transfer** (confirm-first): `gh api -X POST repos/extrode/bayan/transfer -f
   new_owner=ikhbat-org`; poll `gh api repos/ikhbat-org/bayan`; `gh repo edit ikhbat-org/bayan
   --homepage https://bayan.run`. Checks: `gh release view v0.1.0 --repo ikhbat-org/bayan` lists 7
   assets; `git ls-remote git@github.com:ikhbat-org/bayan.git refs/heads/dev refs/heads/main`
   equals local `dev` and `origin/main`; tags `v0.1.0` present. Never create `extrode/bayan` again.
7. **mb1 local move**: the user ends every Claude session (this one and its bg job, the
   interactive one, any `--print` worker; the dry run lists what is still live), quits the desktop
   app and the daemon, then in a plain terminal:
   ```
   bash ~/Developer/extrode.com/bayan/scripts/move/2026-11-02-ikhbat-org.sh            # dry run, read it
   bash ~/Developer/extrode.com/bayan/scripts/move/2026-11-02-ikhbat-org.sh --execute
   ```
   Then `cd ~/Developer/ikhbat.org/bayan && claude --resume` lists this session; resuming it works.
8. **Vault**: in the new session, `memory-sync.js scan`, `push --dry-run`, `push`: adds
   `github.com--ikhbat-org--bayan` with 29 memory files. Committing `~/dotfiles` stays the user's.
9. **wp1, wlt1** (over ssh, PowerShell, dry run first): refuse if the target exists or the source
   has changes other than `tmp-t19-run.ps1`; `dotnet build-server shutdown`; `New-Item -ItemType
   Directory -Force C:\home\code\ikhbat.org`; `Move-Item` the checkout; `git -C <new> remote set-url
   origin https://github.com/ikhbat-org/bayan.git`; `git -C <new> fetch -q origin`; `git -C <new>
   switch dev` (or `switch --track origin/dev` when no local dev). Verify `git -C <new> rev-parse
   HEAD origin/dev` equal and `tmp-t19-run.ps1` present.
10. **Post-move checks** (new session, new path): `dotnet build -v q`, `dotnet test -v q`,
    `laws-check.js --run`, `structure-audit.js`, `bash scripts/release/package.test.sh`, `bash
    scripts/ci/install-dotnet.test.sh`, `review-nudge.js --watermark` prints e27f0daf or later; a
    CI run on `ikhbat-org/bayan` after the user's next push is green.
11. **Records**: handoff progress log; memory note that the project moved (old key retired per
    `docs/memory-supersession.md`).

## Rollback

- Before 6: nothing outside branches changed.
- After 6: transfer back (`gh api -X POST repos/ikhbat-org/bayan/transfer -f new_owner=extrode`).
- After 7: `bash ~/.claude/backups/bayan-move-2026-11-02/undo.sh` (Claude closed).
- After 9: `Move-Item` back; `origin` → `https://github.com/extrode/bayan.git` (redirects).

## Risks

- **A Claude process writes during the move** → split state. Preflight refuses on any process form
  (bare argv included), any live session file and any job not `done`.
- **`.claude.json` or registry corruption** → `jq` to temp, validate, key count, backup, `undo.sh`.
- **Transcript rewrite damage** → `cwd` field only, boundary-matched, size delta checked, clone
  backups restored by `undo.sh`.
- **Worker auto keys** embed a hash of the cwd: they start fresh after the move. Explicit `--key`
  workers resume. Accepted.
- **bayanlang.org** is someone else's (registered 2026-10-22); name confusion possible. Noted.
