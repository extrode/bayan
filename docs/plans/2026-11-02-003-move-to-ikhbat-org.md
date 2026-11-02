# Move Bayān to the Ikhbat Foundation

Date: 2026-11-02. Tier: Standard. Status: draft, waiting for review and the user's approval.

## Goal

| | From | To |
|---|---|---|
| GitHub | `github.com/extrode/bayan` | `github.com/ikhbat-org/bayan` (org `ikhbat-org`, "Ikhbat Foundation") |
| mb1 checkout | `/Users/sy/Developer/extrode.com/bayan` | `/Users/sy/Developer/ikhbat.org/bayan` |
| wp1, wlt1 checkout | `C:\home\code\extrode.com\bayan` | `C:\home\code\ikhbat.org\bayan` |
| Claude project key (mb1) | `-Users-sy-Developer-extrode-com-bayan` | `-Users-sy-Developer-ikhbat-org-bayan` |
| Home page | none | `https://bayan.run` (registered 2026-11-02, Cloudflare) |

Nothing is lost: every session, memory, lesson, worker key, release and the review watermark is
reachable at the new location, and the old GitHub URL keeps redirecting.

## Facts this plan relies on (measured 2026-11-02)

- `gh api orgs/ikhbat-org`: exists, 0 repos, the user is admin of both orgs.
- `extrode/bayan`: public; no Actions secrets, no rulesets, no webhooks, no deploy keys. Workflows use
  GitHub-hosted runners only (`ubuntu-24.04[-arm]`, `macos-15[-intel]`, `windows-2025`,
  `windows-11-arm`, `ubuntu-latest`), and every checkout uses `$GITHUB_REPOSITORY`, so nothing in
  `.github/` names the owner. Release `v0.1.0` (6 archives + SHA256SUMS) is published.
- GitHub transfer moves issues, releases, tags, Actions history and settings, and redirects the
  old web and git URLs for as long as no new `extrode/bayan` is created.
- Tracked files naming `extrode`: `docs/misc/b-trees.md` (three `file:///Users/sy/Developer/extrode.com/bayan/…`
  links), `docs/laws/README.md` (an `Extrode.Jaunty` example, unrelated, unchanged),
  `scripts/cleanup/016-spec-013-and-011-draft.sh` (`/home/code/extrode.com/bayan` on wp1/wlt1).
- mb1 Claude state keyed by the path:
  - `~/.claude/projects/-Users-sy-Developer-extrode-com-bayan*`: 368 dirs (the main one 266 MB,
    81 sessions, `memory/` with 26 files; the rest are worktree sessions).
  - `~/.claude/lessons/-Users-sy-Developer-extrode-com-bayan*`: 171 dirs; `lessons/INDEX.md`: 274
    lines naming them. `lessons/` is gitignored in `~/.claude`.
  - `~/.claude.json`: `projects["/Users/sy/Developer/extrode.com/bayan"]` (trust, allowed tools,
    MCP settings) and `githubRepoPaths["extrode/bayan"]`.
  - `~/.claude/workers/registry.json`: 329 entries whose `cwd` is under the old path.
  - `~/.claude/scripts/cleanup/mac-background-services.sh` (+ `.test.sh`): `BAYAN="$HOME/Developer/extrode.com/bayan"`.
  - The memory files themselves never mention `extrode`.
- Vault (`~/dotfiles/claude-vault`, keyed by git remote): has no bayan entry. Bayan's memory
  exists only on mb1.
- wp1 and wlt1 (over `ssh wp1`, `ssh wlt1`): each has the checkout at `C:\home\code\extrode.com\bayan`
  on `feat/013-test-tooling` at fbecfff1, `origin` = `C:\Users\Sy\AppData\Local\Temp\bayan-013.bundle`,
  nothing unpushed, no stash, one untracked file `tmp-t19-run.ps1`. Neither has a bayan Claude
  project dir, lesson dir or `.claude.json` reference. `C:\home\code\ikhbat.org` exists on wp1,
  not on wlt1; `C:\home\code\ikhbat.org\bayan` exists on neither.
- `~/Developer/ikhbat.org/bayan` does not exist on mb1. No other repo under `~/Developer`, and no
  launchd agent, crontab or shell rc, references the bayan path.
- Claude Code rewrites `~/.claude.json` and appends to the session's project dir while it runs.
  This session's cwd is the path being moved.

## Decisions (recommended; the user confirms)

| # | Question | Recommendation | Rejected |
|---|---|---|---|
| D1 | URL | `https://bayan.run`: already registered, and it is the CLI verb (`bayan run`) | bayan.sh, bayan.build, getbayan.org, bayan.foundation (free, unregistered) |
| D2 | Old mb1 path | No symlink. A symlink lets a session start at the old path and re-create the old project key, splitting memory again | Symlink for a transition period |
| D3 | Session files | Rename the dirs; do not edit the `.jsonl` contents. Resume lists sessions by directory; the old `cwd` inside a transcript is history | `sed` over 266 MB+ of transcripts |
| D4 | wp1/wlt1 | Move the dir (keeps `tmp-t19-run.ps1` and build caches), set `origin` to the GitHub URL, fetch | Fresh clone (loses the scratch file); leave as is |
| D5 | Who runs the GitHub transfer | The agent, after the user says yes in that turn (confirm-first); or the user runs the one command | — |
| D6 | bayan.run DNS | Out of scope here: the domain has Cloudflare NS and no A record. A redirect to the GitHub repo until a site exists is a Cloudflare rule the user sets | — |

## Order (each step checks the previous one)

0. **User pushes dev** (`git push origin dev`): origin/dev must equal local dev (`e27f0daf` or later).
   Preflight in step 3 refuses otherwise.
1. **Repo edits** (branch `chore/ikhbat-move` off dev, merge `--no-ff`, unpushed):
   - `docs/misc/b-trees.md`: the three `file:///…` links become relative (`../bayan-spec.md#L284`,
     `../../src/runtime/bayan-rt.h#L27`, `…#L324`).
   - `README.md`: one line naming `https://bayan.run` and `github.com/ikhbat-org/bayan`.
   - `CHANGELOG.md` `## [Unreleased]`: "Changed: the repository moved to `github.com/ikhbat-org/bayan`
     (Ikhbat Foundation); home page `https://bayan.run`."
   - `scripts/cleanup/016-spec-013-and-011-draft.sh`: the Windows checkout path becomes
     `/home/code/ikhbat.org/bayan` (the script may still be unrun).
   - The move script and its test (step 2), and the handoff progress-log entry.
   - Not changed: `docs/laws/README.md`, historical handoffs and specs that mention old paths as
     history.
2. **Move script** `scripts/move/2026-11-02-ikhbat-org.sh` + `scripts/move/2026-11-02-ikhbat-org.test.sh`,
   written by `codegen-lite`, gated against this plan. Interface per `~/.claude/scripts/cleanup/README.md`:
   dry run by default, `--execute` acts, no irreversible step (every rename has a backup or is a
   rename back). Every path comes from variables (`OLD_REPO`, `NEW_REPO`, `CLAUDE_HOME`,
   `CLAUDE_JSON`, `NEW_REMOTE`) defaulting to the real ones, so the test runs it on a fixture
   `$HOME` under the repo's `tmp/`. Steps, each printing `would:`/`run:`/`skip:`/`refuse:`:
   1. Preflight, all refusals before any change:
      - no running Claude Code process (`pgrep -f` on the claude binary; the script names the
        PIDs it found);
      - `NEW_REPO` and every target project/lesson dir absent (no collision);
      - `OLD_REPO` clean (`git status --porcelain` empty), on any branch, and `dev` equal to
        `origin/dev`;
      - `git ls-remote "$NEW_REMOTE" refs/heads/dev` answers with the same sha (the transfer
        happened and nothing is lost);
      - `jq` present.
   2. Backup to `~/.claude/backups/bayan-move-2026-11-02/`: `~/.claude.json`,
      `workers/registry.json`, `lessons/INDEX.md`, and a manifest (old name, new name) of every
      dir to rename. Project/lesson dirs are renamed, not copied, so the manifest is the undo.
   3. `mv OLD_REPO NEW_REPO` (`mkdir -p` the parent).
   4. In `NEW_REPO`: `git worktree repair` (fixes `.worktrees/worker-plan-011`), `git remote set-url
      origin "$NEW_REMOTE"`, `git fetch -q origin`.
   5. Rename every `projects/` and `lessons/` dir whose name starts with the old key prefix to the
      new prefix (suffix kept), refusing the whole step on any existing target.
   6. `lessons/INDEX.md`: replace the old key prefix with the new one (fixed string, not regex).
   7. `~/.claude.json` via `jq` to a temp file then `mv`: move every `projects` key starting with
      `OLD_REPO` to the same key under `NEW_REPO`; `githubRepoPaths`: drop `extrode/bayan`, set
      `ikhbat-org/bayan` to `[NEW_REPO]`. Refuses if the output is not valid JSON or the number of
      top-level keys changed.
   8. `workers/registry.json`: every `cwd` under `OLD_REPO` rewritten to `NEW_REPO` (same `jq`
      discipline). Session ids are unchanged, so keyed workers resume from the renamed dirs.
   9. Undo file: `undo.sh` in the backup dir reverses 3–8 from the manifest and the backups.
   10. Verify and print: `git -C NEW_REPO status -sb`, `git worktree list`, count of renamed
       dirs (expect 368 + 171 on mb1), `jq` read of the two moved `.claude.json` keys, and that
       `NEW_REPO/../../extrode.com/bayan` no longer exists.
   The test covers: dry run changes nothing (tree hash of the fixture before/after); each preflight
   refusal (Claude running via a stub `pgrep`, dirty repo, dev ahead, ls-remote mismatch, target
   exists); a full execute on the fixture (repo moved, worktree repaired, remote set, dirs renamed
   with suffixes, INDEX rewritten, `.claude.json` keys moved and other keys byte-equal, registry
   cwds rewritten); `undo.sh` restores the fixture to its starting tree hash.
3. **~/.claude edits** (branch off `~/.claude`'s dev, merge `--no-ff`, unpushed):
   `scripts/cleanup/mac-background-services.sh` and its test: `BAYAN="$HOME/Developer/ikhbat.org/bayan"`.
4. **Review of steps 1–3** (`review` worker, both lenses), fix, merge both repos into dev.
5. **GitHub transfer** (confirm-first, D5):
   `gh api -X POST repos/extrode/bayan/transfer -f new_owner=ikhbat-org`, then poll
   `gh api repos/ikhbat-org/bayan` until it answers; then
   `gh repo edit ikhbat-org/bayan --homepage https://bayan.run`. Check: `gh release view v0.1.0
   --repo ikhbat-org/bayan` lists the 7 assets; `git ls-remote git@github.com:ikhbat-org/bayan.git
   refs/heads/dev` equals local dev. Never create a repo named `extrode/bayan` afterwards.
6. **mb1 local move** — the user, in a plain terminal, with every Claude Code session closed:
   ```
   bash ~/Developer/extrode.com/bayan/scripts/move/2026-11-02-ikhbat-org.sh            # dry run, read it
   bash ~/Developer/extrode.com/bayan/scripts/move/2026-11-02-ikhbat-org.sh --execute
   ```
   Then `cd ~/Developer/ikhbat.org/bayan && claude --continue` (or `claude --resume`) must list
   this session.
7. **Vault**: in the new session, `deno run -A ~/.claude/tools/memory-sync.js scan`, then
   `push --dry-run`, then `push`: adds `github.com--ikhbat-org--bayan` with the 26 memory files.
   Committing/pushing `~/dotfiles` stays the user's.
8. **wp1, wlt1** (from mb1 over ssh, dry run first, then execute; PowerShell):
   `New-Item -ItemType Directory -Force C:\home\code\ikhbat.org`;
   `Move-Item C:\home\code\extrode.com\bayan C:\home\code\ikhbat.org\bayan` (refuse if the target
   exists or the source is not clean apart from `tmp-t19-run.ps1`);
   `git -C C:\home\code\ikhbat.org\bayan remote set-url origin https://github.com/ikhbat-org/bayan.git`;
   `git fetch -q origin`. Verify with `git status -sb` there. No Claude state to move (measured).
9. **Post-move checks** (new session, new path): `dotnet build -v q` (regenerates `obj/` with new
   paths), `dotnet test -v q`, `laws-check.js --run`, `structure-audit.js`, `bash
   scripts/release/package.test.sh`, `bash scripts/ci/install-dotnet.test.sh`; a CI run on
   `ikhbat-org/bayan` after the user's next push is green.
10. **Records**: handoff progress log; memory note that the project moved (new key, old key retired
    per `docs/memory-supersession.md`); `AGENTS.md` unchanged (it names no path).

## Rollback

- Before step 5: nothing outside the branches changed; drop them.
- After 5: GitHub can transfer back (`gh api -X POST repos/ikhbat-org/bayan/transfer -f new_owner=extrode`).
- After 6: `bash ~/.claude/backups/bayan-move-2026-11-02/undo.sh`.
- After 8: `Move-Item` back and `remote set-url` to the bundle path.

## Risks

- **A Claude process writes during the move** → split state. Preflight refuses while any runs.
- **`.claude.json` corruption** → Claude loses every project's settings. `jq` to temp, validate,
  key count check, backup, `undo.sh`.
- **Worker auto keys** embed a hash of the cwd (`bayan-f1322a`): after the move they compute a new
  key and start fresh. Explicit `--key` workers resume. Accepted.
- **bayanlang.org** was registered on 2026-10-22 by someone else; name confusion is possible. Noted only.
- **Old absolute paths in transcripts and historical docs** stay as written (D3).
