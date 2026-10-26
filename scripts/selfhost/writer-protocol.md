## Writer protocol (binding; the dispatcher runs the same guard after you, so skipping a step only costs you a resume)

Turns are your scarce resource: this run has 120, and you will be resumed with the guard's report if you run out. Put several Read/Grep/Bash calls in ONE turn whenever they do not depend on each other.

1. **Read only what the brief lists** (turns 1–6). The brief gives you the exact C# line ranges, the signatures you may call, and the files you own. Do not grep the spec, tests/cases or other modules to learn the language: the cheat sheet below is compiler-checked, and when unsure, **probe**: write the snippet to `tmp/probe/<name>/main.byn` and run `src/Bayan.Cli/bin/Debug/net10.0/bayan check tmp/probe/<name>/main.byn` (0.3 s).
2. **Skeleton by turn 12, and it must compile.** Every function the brief assigns to you, with its exact signature and a stub body (`return <default>` / `.none`), plus the test file with one real test. Run the fast check (below). Fix until green before porting any body.
3. **Compile early, compile often.** Port one function or ~80 lines at a time, then run the fast check. Never write more than ~150 lines between two checks. Use Edit for slices, not a whole-file Write.
4. **Tests are part of the task.** Every ported function gets at least one test that compares with the C# result (or a golden), including a failure/diagnostic path. Then break-test: change one line the test covers, run it and see it FAIL, restore with `git checkout -- <file>`, run it and see it PASS. Record each break-test (file:line, change, failing test) in your Question or summary.
5. **Before you finish, run the guard** (command in the brief) and paste its last 15 lines into your summary. `GUARD PASS` is the only finished state. A surviving mutation means a line no test covers: add a test, do not weaken the guard.
6. **At turn ~110, stop adding.** Make it compile, commit nothing yourself, and end with: done / not done / the exact next step. The resume picks up there.

Never: edit files outside the brief's list (record the need as a Question instead); leave output files in the repo root; use tuple destructuring or `.0` fields; run commands in the background; run `git commit/merge/checkout <branch>/reset`.

Fast check: `dotnet build -v q 2>&1 | grep -E " error | warning " | head -20; dotnet test -v q --no-build --filter "FullyQualifiedName~SelfhostSemanticsT3Tests" 2>&1 | tail -3` (~15 s).


