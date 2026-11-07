# Move Bayān to the Ikhbat Foundation

Date: 2026-11-02. Tier: Standard. Status: rev 3e (reviews A, B and C folded in, the user's choices
applied; rev 3b: move-script fix round 1, F1-F16; rev 3c: fix round 2, G1-G6; rev 3d: session-end
review fixes M1-M6; rev 3e: review loop 2, N1-N4), waiting for the user's approval.

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
  merged (735a6688), `.worktrees/worker-plan-011` removed by the user's cleanup run; `git worktree
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
- `origin/dev` = 735a6688 (pushed by the user); local `dev` = 9a698381 (011 rev 4 merged since).
  Local branches with commits not on any remote: `dev`, `docs/011-rev4` (in dev), `fix/q745` (in
  flight; its worktree lands or is removed before step 7), `docs/ikhbat-move-plan` (merges in step 1). Local refs move with
  the directory, so nothing depends on pushing them, but the transfer check compares `dev`.
- Review watermark: local config `review.reviewedthrough` = 174401e0 (no `refs/review/reviewed`
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
  | `lessons/queue.jsonl` rows (`transcript`, `cwd`, `project`, `repoTop`) | 5 | backup + rewrite (2.8), every string value |
  | `sessions/*.json` `cwd` of live sessions; `daemon.lock` `spawnedBy.cwd` | 3 + 1 | not rewritten: preflight refuses while any is live; files left after their process ends are stale |
  | `~/.claude.json` `projects["<old>"]`, `githubRepoPaths["extrode/bayan"]` | 1 + 1 | rewrite (2.7) |
  | `workers/registry.json` string fields: `cwd` 331, `repo` 114, `worktree` 114, `criteria` 74 | 633 | rewrite (2.8) |
  | `jobs/c6b5c540/state.json` (`cwd`, `linkScanPath`); `daemon/roster.json` (this job's worker) | 2 | this job ends before step 7; state.json and ended roster workers rewritten (2.8) |
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
  be54c0a0 (in dev; not a GitHub branch), `origin` = a local bundle, nothing unpushed, no stash,
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
| D3 | Transcripts | Rewrite only the `"cwd"` field, matched at a boundary (step 2: the path ends at a char outside `[A-Za-z0-9._-]`, the key outside `[A-Za-z0-9._]`; both start at the string's start or after a char outside `[A-Za-z0-9._-]`, for the path not `/` either); clone backup first | leave as is (resume filter rejects); rewrite every mention |
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
   defaulting to the real ones. Matching uses fixed strings, never a regex built from the path, and
   two boundary rules (rev 3a, found by the script's writer: one class cannot serve both forms):
   - `OLD_REPO` (a path) matches only when the next character is not `[A-Za-z0-9._-]`, or at end
     of string: `/`, `"`, `)` and space end it; `bayan-x`, `bayan.x`, `bayan_x` never match.
   - The key prefix matches only when the next character is not `[A-Za-z0-9._]`, or at end: `-`
     must end it, since the key turns `/` and `.` into `-` (`…-bayan-tmp-wt-x`,
     `…-bayan--worktrees-x`). A sibling repo `extrode.com/bayan<anything>` would get a key that
     this cannot tell apart, so preflight refuses when any entry of `OLD_REPO`'s parent is named
     `bayan` plus at least one more character (none exists today). INDEX.md has 16 `(key)` tags;
     queue.jsonl has the key before `"` and space.
   - Left boundary (rev 3d), both forms: a match counts only at the start of the string or after
     a char outside `[A-Za-z0-9._-]`; for the path, not after `/` either, so
     `/Volumes/bk/Users/sy/…` and `x-Users-sy-…` stay. A run of `/` counts as one char (rev 3e): the
     path matches after `file://` and `/` (`file:///Users/…`, `//Users/…`), not after `/a`. Inside JSON strings an escape such as `\n`
     before the match counts as the char it stands for, a boundary.

     Every `git -C <dir>` (script and `undo.sh`) first
     checks that `git rev-parse --show-toplevel` is `<dir>` itself and refuses otherwise, and both
     export `GIT_CEILING_DIRECTORIES` = the checkouts' parents, so git never reaches an enclosing
     repo. Each step prints `would:`/`run:`/`skip:`/`refuse:` and is idempotent: the
   manifest records finished steps, a rerun after an interruption skips them, and a step whose
   target already holds the new form is a `skip:`.
   1. Preflight, all refusals before any change. With no manifest every check below applies. With
      a manifest (a resumed run) the checks a finished step made obsolete are replaced by the
      manifest's recorded state: after step 3, `NEW_REPO` must exist and `OLD_REPO` be absent;
      for each recorded rename, its source and target are not both present and not both absent
      (for renames step 2 recorded, once step 5 is done the target must exist and the source be
      absent; after step 5 a present source with an absent target is a late dir's rename, 2.2,
      recorded as a `rename-late` manifest line). Process
      checks always apply.
      - no Claude process: `pgrep -fl` for `/claude/versions/`, `ClaudeCode.app`, `bin/claude`,
        and `pgrep -x claude` for a bare argv; the script prints each PID and command line and
        stops nothing itself; `pgrep` exit 1 means none, any other non-zero exit refuses. The
        process and live-session checks run again before steps 6, 8 and 10; a hit stops the run
        with the resume command;
      - no `$CLAUDE_HOME/sessions/*.json` whose `pid` is alive (`kill -0`) has a `cwd` at an old
        path (files without `pid` are skipped); `$CLAUDE_HOME/daemon.lock`'s `pid` is not alive;
      - no entry of `OLD_REPO`'s parent named `bayan` plus more characters (key collision);
      - every `jobs/*/state.json` with `.state != "done"` names no old path; a
        `daemon/roster.json` worker (`.workers`, keyed by id) naming the old path or key refuses
        only when its `pid` or `replPid` is alive (`kill -0`); ended workers are backed up and
        rewritten (2.8);
      - every file the run reads or rewrites is readable and parses: `sessions/*.json`,
        `daemon.lock`, roster, job state, `workers/registry.json` and `~/.claude.json` (`jq
        empty`), `history.jsonl` and `lessons/queue.jsonl` line by line; transcripts are checked
        line by line in step 2, before any change; every transcript dir at the old key can be
        scanned (every `grep -r`/`grep -l` scan, here and in steps 2, 6 and 10, treats exit 2 as
        an error that refuses, naming the unreadable path; exit 1 = no match);
      - `~/.claude.json` (as backed up, on a resumed run) has no `projects` key equal to
        `NEW_REPO` or starting with `NEW_REPO/`;
      - `NEW_REPO` and every target project/lesson dir absent; on a resumed run, a dir at the old
        key that step 2 did not list (2.2) refuses when its target exists, naming both;
      - `OLD_REPO` clean, `git worktree list` shows the main checkout only; `git status` or `git
        worktree list` failing refuses (`cannot check <repo>: <git error>`), never passes as clean
        or as no worktrees;
      - `git ls-remote "$NEW_REMOTE" refs/heads/dev` equals local `dev` (after step 3 a mismatch
        is a `warn:`: the checkout has moved, and the run goes on);
      - `jq` and `perl` present.
   2. Backup to `~/.claude/backups/bayan-move-2026-11-02/`: `~/.claude.json`,
      `workers/registry.json`, `lessons/INDEX.md`, `lessons/queue.jsonl`, `history.jsonl`, `jobs/*/state.json` that name
      the path, `daemon/roster.json` when it names the path, and `cp -c` clones of every
      transcript with an old-path `cwd`. Manifest: each dir rename (old, new) and each backed-up
      file (path before and after the renames). That list is the rewrite set: steps 6-8 rewrite
      only listed files, and a file with old-path hits that is not listed refuses, except a
      transcript with an old `cwd` written after step 2 (found on a resumed run): it is backed up
      at that moment, appended to the list with a `note:` line naming it, and rewritten like the
      others. Likewise a `projects/` or `lessons/` dir at the old key created after step 2 (rev
      3d): right after step 2 on a resumed run it is appended to the manifest's renames with a
      `note:` line (manifest tag `rename-late`), so `undo.sh` reverses it; when step 5 is already done it is renamed then, and
      when step 6 is already done its transcripts with an old `cwd` are adopted and rewritten then
      (otherwise steps 5 and 6 do it); a late `lessons/` dir after step 6 also gets `lessons/INDEX.md`
      rewritten (key only, as step 6 does; decided from the manifest's `rename-late` lines, so a resume
      after an interruption still does it; undo needs `--discard-changes-since-move` there, since the
      file changed after its step 6 write). `undo.sh` is written here already, so it exists whether or
      not step 9 runs.
   3. `mkdir -p` the parent; `mv OLD_REPO NEW_REPO`.
   4. In `NEW_REPO`: `git remote set-url origin "$NEW_REMOTE"`, `git -C "$NEW_REPO" fetch -q origin`;
      a failed fetch is a `warn:`, not an abort. From here on any failure prints the resume
      command, `bash <NEW_REPO>/scripts/move/2026-11-02-ikhbat-org.sh --execute`.
   5. Rename every `projects/` and `lessons/` dir starting with the old key prefix to the new
      prefix (suffix kept, late dirs of 2.2 included); refuse the step on any existing target.
   6. Transcripts in the renamed project dirs (`*.jsonl`, subagent files included): `perl` writes
      the file with `"cwd":"<OLD_REPO>` at a boundary replaced by `"cwd":"<NEW_REPO>` to a temp
      file next to it, `touch -r` copies the mtime, an atomic `mv` replaces the file (never `perl
      -pi`). `lessons/INDEX.md`: the key prefix. Line counts and byte sizes compared before/after
      (size changes by exactly the path-length delta times the replacements); every rewrite keeps
      the file's mode and mtime. Every rewrite in steps 6-8 (`.claude.json` included) appends
      `<sha256> <path>` to the backup dir's `written.sha256`: the temp file's hash keyed by the
      destination path, recorded before the atomic `mv` (rev 3d), so an interrupt leaves the file
      equal to either its backup or its recorded hash; a file whose content differed from its
      backup just before the rewrite gets no line (a `note:` says so), so `undo.sh` lists it as
      changed. A transcript not in step 2's list is adopted as in 2.2.
      The dry run counts files with an old `cwd` and, apart, files naming the old path
      elsewhere only.
   7. `~/.claude.json` via `jq` to a temp file then `mv`: every `projects` key at `OLD_REPO` or
      below moves to the same key under `NEW_REPO`; `githubRepoPaths`: drop `extrode/bayan`; its
      elements, each under the path rule and in order, are merged after any existing
      `ikhbat-org/bayan` elements, `NEW_REPO` appended, duplicates dropped. Refuses if the output is invalid or the top-level key
      count changed.
   8. Same `jq` discipline, every string value at a boundary: `workers/registry.json` (`cwd`,
      `repo`, `worktree`, `criteria`), `history.jsonl` (`project` only, line by line, line count
      unchanged; `display` is message text and stays), `lessons/queue.jsonl` (every string value,
      path and key rules both, as job state; today `transcript`, `cwd`, `project`, `repoTop`; line
      count unchanged), `daemon/roster.json` (ended workers; every string value, both rules),
      `jobs/*/state.json` (every string value, path and key rules both; today `cwd`,
      `linkScanPath` and `fan[].label`).
   9. `undo.sh` in the backup dir reverses 3–8 from the manifest and backups; idempotent. It reads
      the manifest when it runs, so lines appended after step 2 (late transcripts and dirs) are
      reversed too; step 9 writes the same text again, a no-op unless the file was removed or
      edited. `undo.sh` checks, before changing anything: no Claude
      process and no live session (the preflight's `pgrep` rules; a live session's `cwd` at the
      old or new path), no rename source and target both present, no repo source and target
      both present, the checkout is a git top-level, the moved checkout has no linked worktree
      (`git worktree list --porcelain` beyond the main one, named), and no file it would restore
      changed since the move: a file is unchanged when its hash equals its latest
      `written.sha256` line (restored) or its backup's (skipped, nothing to restore); anything
      else is a change. Any failure refuses, lists what it found and changes nothing;
      `--discard-changes-since-move` overrides the last two checks, and with a linked worktree
      runs `git worktree repair` after moving the checkout back and reports the result.
   10. Verify: zero `projects/`/`lessons/` dirs with the old prefix; renamed counts equal the
       manifest's; `grep -c` of old-path `cwd` lines in the new dirs is 0; the two `.claude.json`
       keys read back; `git -C NEW_REPO status -sb` and `remote -v`; `OLD_REPO` absent.

   The test runs on a fixture `$HOME` under the repo's `tmp/` and covers: dry run changes nothing
   (tree hash before/after); each preflight refusal (each process form via a stub `PGREP`, the
   bare-argv form included; a live `sessions/*.json`; a job with `"state":"working"` and one with
   `"state":"done"` that must not refuse; an unreadable working job; `pgrep` exiting 2; a live
   roster worker; a bad `history.jsonl` line; an existing `projects[NEW_REPO]` and a
   `NEW_REPO/sub` key; an unreadable transcript under the old key; dirty repo, linked
   worktree, ls-remote mismatch, target exists; a sibling `bayan-x` dir next to `OLD_REPO`; a
   corrupt `.git/index`); boundary cases (paths `/…/bayan-x`,
   `/…/bayan.x`, `/…/bayan_x` untouched; key `-…-bayanx` untouched; `"cwd":"<old>/sub"`, `(key)`,
   key before `-`, `"` and space rewritten; queue values `/Volumes/bk<old>/x`, `/mnt/<old>/x`,
   `x<key>`, `x-<key>` untouched, `<old>/x`, `<key>` and `l1\n<old>/x` rewritten); a full execute (repo moved, remote set, dirs
   renamed with suffixes, transcripts' `cwd` rewritten and other bytes equal, INDEX,
   `.claude.json` keys moved and other keys byte-equal, registry, history with a `display` row
   left unchanged, queue.jsonl incl. `repoTop`, an ended roster worker, job state, transcript
   mtimes kept); `githubRepoPaths` merge; runs interrupted after steps 3, 4, 5, 6, 7 and 8, and by
   a Claude process appearing before step 6, each resumed to the same end tree as an
   uninterrupted run; a failed fetch after step 3 warns and finishes; a transcript gaining an old
   `cwd` after step 2 is backed up, rewritten and restored by `undo.sh`; a project and a lesson dir
   at the old key created after step 2 (and after step 6) are renamed, their transcript's `cwd`
   rewritten and both restored by `undo.sh`, and one whose target exists refuses at preflight; a
   run stopped right after the `mv` of a transcript and of `.claude.json` (`STOP_AFTER_MV`)
   resumes and `undo.sh` restores the start without `--discard-changes-since-move`; `016
   --delete-remote` (stub `ssh`) refuses the run script on a host with no new checkout and
   checks it on one with; `undo.sh` restores the
   starting tree hash, twice, and refuses on an empty non-git `OLD_REPO`, on a file changed after
   the move (proceeding with the flag), on a history row appended after a stop at step 6 (no step
   9), on a row appended between a mid-run stop and the resume, on a rename source recreated
   after the move, on a Claude process, and on a linked worktree added after the move. Every git
   command runs under
   `GIT_CEILING_DIRECTORIES`, and the enclosing repo's `.git/config` hash is the same at the end.
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
   If the run stops after step 3 (the checkout moved), it prints the resume command, which names
   the script under the new path:
   `bash ~/Developer/ikhbat.org/bayan/scripts/move/2026-11-02-ikhbat-org.sh --execute`.
8. **Vault**: in the new session, `memory-sync.js scan`, `push --dry-run`, `push`: adds
   `github.com--ikhbat-org--bayan` with 29 memory files. Committing `~/dotfiles` stays the user's.
9. **wp1, wlt1** (over ssh, PowerShell, dry run first): refuse if the target exists or the source
   has changes other than `tmp-t19-run.ps1`; `dotnet build-server shutdown`; `New-Item -ItemType
   Directory -Force C:\home\code\ikhbat.org`; `Move-Item` the checkout; `git -C <new> remote set-url
   origin https://github.com/ikhbat-org/bayan.git`; `git -C <new> fetch -q origin`; `git -C <new>
   switch dev` (or `switch --track origin/dev` when no local dev). Verify `git -C <new> rev-parse
   HEAD origin/dev` equal and `tmp-t19-run.ps1` present. Only after this step, run
   `scripts/cleanup/016-spec-013-and-011-draft.sh --delete-remote`: its wp1/wlt1 section names
   `/home/code/ikhbat.org/bayan`, and on a host where `/home/code/ikhbat.org/bayan/.git` does not
   exist yet it refuses the run script (`refuse: <h>: … is not a checkout yet (move step 9
   first)`) and leaves it alone; the other section-5 paths are unaffected.
10. **Post-move checks** (new session, new path): `dotnet build -v q`, `dotnet test -v q`,
    `laws-check.js --run`, `structure-audit.js`, `bash scripts/release/package.test.sh`, `bash
    scripts/ci/install-dotnet.test.sh`, `review-nudge.js --watermark` prints 6ece74af or later (the history rewrite replaced 174401e0); a
    CI run on `ikhbat-org/bayan` after the user's next push is green.
11. **Records**: handoff progress log; memory note that the project moved (old key retired per
    `docs/memory-supersession.md`).

## Rollback

- Before 6: nothing outside branches changed.
- After 6: transfer back (`gh api -X POST repos/ikhbat-org/bayan/transfer -f new_owner=extrode`).
- After 7: `bash ~/.claude/backups/bayan-move-2026-11-02/undo.sh` (Claude closed). It refuses,
  restoring nothing, when a file it would restore changed after the move (it lists them): a
  restore would discard those changes, e.g. history rows or sessions written at the new path.
  `undo.sh --discard-changes-since-move` restores anyway.
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
