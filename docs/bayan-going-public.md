# Bayan: going public on GitHub

Oct 20, 2026 · @Sy

## Recommendation

Keep the repository private for now, reserve the names publicly today, and go public at a clear milestone: the end of M9, when `bayan new` works.

At that point a visitor can clone the repository, create a project and run it, which makes a much stronger first impression than a spec document alone.

## Private versus public right now

Staying private wins on design freedom and first impressions; going public wins on CI cost.

|  | Public now | Private until a milestone |
| --- | --- | --- |
| Design stability | Every syntax change happens in public; decisions get debated before they're tested | Change the design freely while the compiler proves it |
| First impression | Early visitors see an unfinished compiler and may not return | The first thing people see is something that works |
| Feedback | Early, but mostly opinions about syntax | Later, focused on things that actually run |
| Credit and timestamps | The design is visibly first | Still provable later through the commit history |
| CI cost | Standard GitHub-hosted runners are free for public repositories | Monthly Actions allowance; macOS minutes billed at a much higher rate (check current GitHub pricing) |
| Security | Any secret ever committed is exposed | Time to clean the history before anyone sees it |

If private CI costs become a problem, run Linux-only CI while private and add macOS when going public.

## Reserve the names now

A web search found no programming language called Bayan, but the name is in use elsewhere: the Python package [bayan-lib](https://pypi.org/project/bayan-lib/) installs a command-line tool also named `bayan`, and a few small unrelated projects use the name.

- [ ] Create a GitHub organization (for example `bayan-lang`), with the repository private under it.
- [ ] Register a domain (for example `bayan-lang.org`).
- [ ] Claim the package name in the future Bayan registry when one exists.

The CLI clash only matters on machines with both tools installed; an alias such as `byn` could be added later if needed.

## When to go public

Go public when all four are true:

- [ ] v0.1 is done: hello world and tally-lite run on all three platforms.
- [ ] `bayan new` works, so a visitor can try Bayan in under a minute.
- [ ] The README explains the idea in one screen: what Bayan is, a short example, and how to install it.
- [ ] The settled-decisions list (spec Appendix A) is public, so early feedback doesn't reopen settled questions.

## Preparation before flipping to public

Making a repository public exposes every past commit, so the history needs as much care as the current files.

| Item | Why |
| --- | --- |
| Scan the whole history for secrets (gitleaks or trufflehog) | Every past commit becomes public, not just the current files |
| Keep the history or start fresh | Agent working logs and scratch files become public too; squash into one clean commit, or keep the record |
| License | For example Apache-2.0 or MIT for the compiler and standard library; Apache-2.0 adds a patent grant |
| `SECURITY.md` | A private way to report vulnerabilities, important for a language that makes safety claims |
| `CONTRIBUTING.md` and a code of conduct | Including the rule that design changes go through the spec process |
| Issue templates | Separate bugs from design proposals; proposals must check Appendix A first |
| Governance statement | One sentence: you make the final design decisions for now |
