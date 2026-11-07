# In-band errors and C-boundary safety (sshole lessons)

Date: 2026-11-07. Plan only: no code, spec or test edits. Spec gaps below are proposals for handoff Questions.

Revision 1 (2026-11-07): new W0 (fail-open exit code in `Io.run`) moved out of W1; W1 gains the libc version pin, interior NUL and errno rows and a testable acceptance; W2 reduced to a Question (W2a), a gated warning (W2b) and the W2c metric moved into W7; W4 and W6 now live in the v0.3 and v0.4 plans, W5 in `docs/security/crypto-api-checklist.md`; D1 and D3 removed, D8 updated; priority order replaces "easiest first".

## Source incident

sunshowers.io, "*sshole" (2026-11-06): musl's `crypt()` returns the string `"*"` when a password is too long, instead of POSIX's NULL plus `errno`. `"*"` is also the hash of a locked account. sshd hashes attempts against locked accounts to hide timing, so on musl without PAM an unauthenticated user gets a shell. Not a memory-safety bug: one in-band value with two meanings, plus a library that changed its contract to cover for callers that did not check NULL.

Three lessons: (1) errors need a channel the caller cannot confuse with data; (2) a library that widens or narrows that channel later must break its callers loudly; (3) code that agents and people copy from sets the norm, whatever the language allows.

## Why this reaches Bayan

- Every Linux binary links musl statically: `src/Bayan.Backend.C/Targets.cs:11-12` (`x86_64-linux-musl`, `aarch64-linux-musl`). macOS uses its libc, Windows the UCRT (`x86_64-windows-gnu`, `Targets.cs:15`).
- The runtime (`src/runtime/bayan-rt.c`, 3,569 lines; `bayan-rt.h`, 572) calls 96 libc and OS functions, listed in `src/runtime/libc-uses.txt` (spec 012 T8, `docs/specs/012-self-hosted-bootstrap/012-tasks.md:16`). A libc divergence reaches every program through it.
- The runtime already carries the norm in places: `byn_io_read` returns `0` or an `IoError` code (`bayan-rt.h:445-446`); `Env.get` returns `string?` (`src/Bayan.Semantics/Builtins.cs:128`); `position` returns `i64?` (`Binding/BuiltinMembers.cs:64`).
- The norm-setter does not follow the norm: `selfhost/**/*.byn` (90 files) has 140 `return -1`, 620 `== -1`/`!= -1` and 27 lines with `) fails ` (grep, 2026-11-07). Spec 010/011 rule the port matches the C# method for method (handoff Q711, `docs/bayan-handoff.md:1142`), so this is deliberate and must not change mid-port.

## 1. Lessons mapped to Bayan mechanisms

| # | Lesson | Mechanism | Status | Evidence |
|---|---|---|---|---|
| L1 | Errors travel on their own channel | `fails`, `union error`, `try`, exhaustive `catch switch`, `catch (E.v)` | exists | `docs/bayan-spec.md:1224-1247`; BYN2602-2606 `src/Bayan.Syntax/Diagnostics/DiagnosticCodes.cs:216-220` |
| L2 | Absence is a type, not a value | `T?`, `??`, `?? return/fail` | exists | spec `:310-313`; `position` is `i64?` `BuiltinMembers.cs:64-66`; `tryGet` `:102,:107` |
| L3 | A dropped result is a bug | BYN3201, "an unused non-void value is a compile error"; `let _ = f()` discards | exists, opt-out takes no reason | spec `:142` (writes `_ = f()`, see W2a); `Binding/BinderExpressions.cs:65-70`; golden `tests/cases/diag/unused-value.byn` |
| L4 | A failure must not become a valid-looking value | `f() catch value` is allowed with any `value` | partial: legal and documented, nothing warns | spec `:1244`; cheatsheet shows `catch { return -1 }` `scripts/selfhost/bayan-cheatsheet.md:198`; `Io.readText(path) catch ""` `selfhost/driver/build.byn` |
| L5 | A library cannot change its error contract silently | exhaustive `catch switch` breaks on a new variant; no tool reports it as a breaking change | partial: compile error exists, no API diff | spec `:1244`; `bayan audit` lists bypasses only `src/Bayan.Cli/Audit.cs:8-10`; §19.3 names advisories as spec-only `:2124` |
| L6 | C's in-band errors stay in C | `extern` needs `unsafe`; wrappers are "once in safe APIs" by convention | partial: no check that a wrapper translates NULL/-1 | spec `:2056-2057`; `comptime CImport` is spec-only (no match in `src/`) |
| L7 | Security states are variants, not magic strings | `Secret<T>`, `equalsConstantTime`, `CryptoRandom`, `Untrusted<T>` | spec only for secrets; `Untrusted` partial | spec `:1718-1726`; no `Secret` in `Builtins.cs`; `Untrusted` 5 hits in `Builtins.cs` |
| L8 | libc returns are checked | out-of-memory panics (`bayan-rt.c:186,1407,1439`), `getcwd` (`:2098-2099`), `getrandom` (`:2347-2351`) | partial: `clock_gettime` `:3566`, `QueryPerformanceCounter` `:3559`, `WaitForSingleObject`/`GetExitCodeProcess` `:3419-3421`, `snprintf` `:438,:458`, `atoi` `:449`, Unix `waitpid` `:3511` unchecked | audit in W0 (the wait calls) and W1 (the rest) |
| L9 | No locale-dependent parsing | per-call "C" locale, `strtod_l`/`_strtod_l`; the runtime never calls `setlocale` (0 matches in `bayan-rt.c`), so plain `strtod`/`atoi` also run in the C locale | exists | `bayan-rt.c:1544-1559,1591-1593` vs `:439,449` |
| L10 | The norm-setter follows the norm | selfhost code | missing by design until parity | counts above; Q711 |

## 2. Workstreams

Priority order (replaces "easiest first"): W0, then W1 (libc pin, NUL, errno, remaining rows), then the W3 table, the W2b dry run, the W5 checklist, and W7 last (after parity and L027; it includes W2c). Laws: enact L031 with W0; L029 and L030 after W1's tables exist (D9). W4 and W6 are requirements in their home plans, not work here.

### W0. Fail-open exit code in `Io.run` (do first, one commit)

**Problem.** `Io.run` can report exit code 0 when it never learned the child's status.

- Unix (`bayan-rt.c:3510-3512`): `status` is initialised to 0 (`:3510`) and the `waitpid` loop only retries on `EINTR` (`:3511`). Any other `waitpid` failure leaves `status` 0 and the code is 0 (success).
- Windows (`:3419-3421`): the `WaitForSingleObject` result is ignored and `status` starts at 0 (`:3420`); a failed `GetExitCodeProcess` (`:3421`) leaves it 0. Same outcome.

**Finding on `WIFEXITED`.** The Unix path does not test `WIFEXITED`. Line `:3512` is `WIFSIGNALED(status) ? 128 + WTERMSIG(status) : WEXITSTATUS(status)`. A signal-killed child therefore does not report 0: it reports `128 + signal`, the shell convention, which is indistinguishable from a child that itself exits with that number. The `else` branch applies `WEXITSTATUS` to anything not signaled, which is correct only because `waitpid` is called without `WUNTRACED`/`WCONTINUED` (so no stopped state), and which also reads the zeroed `status` after a failed wait. Checked by reading `:3510-3512`; not run.

**Change.**
- A failed `waitpid` (non-`EINTR`), a failed `WaitForSingleObject` or a failed `GetExitCodeProcess` is `fail IoError`, never exit code 0.
- A signal-killed child must not report 0 and must be told apart from a normal exit: map it to a distinct `IoError` variant, or keep a documented signal exit code. Question for the handoff; this plan recommends the distinct variant.
- Move the status-to-result mapping into one function that takes the wait outcome and raw status and returns the code or an `IoError`. Both platform paths call it.

**Test (this is L031).** Unit-test the mapping function with injected failures: wait failed, exit query failed, signaled, normal exit. Run on linux-x64 (musl), macOS and wp1.

**Files.** `src/runtime/bayan-rt.c`, `bayan-rt.h` (the `byn_io_run` surface), `tests/native/*.c`, `tests/Bayan.Tests/RuntimeNativeTests.cs`.

### W1. Runtime audit: every libc return checked, no in-band error returns

**Scope.** All 96 entries of `src/runtime/libc-uses.txt`. For each call site: is the return checked; what does each libc do on failure; does the runtime's own API return an in-band value to the compiler.

**Files.** `src/runtime/bayan-rt.c`, `bayan-rt.h`, `src/runtime/libc-uses.txt`, new `docs/security/runtime-libc-audit.md` (the table), `tests/native/*.c`, `tests/Bayan.Tests/RuntimeNativeTests.cs`.

**Known in-band returns inside the runtime API** (internal to compiler-emitted C, translated by the emitter, so acceptable if documented and tested):

| Function | In-band value | Surface it becomes |
|---|---|---|
| `byn_str_position`, `byn_str_last_position` (`bayan-rt.h:207-209`) | `-1` | `i64?` (`BuiltinMembers.cs:64-66`) |
| `byn_int_parse_at` (`:337`) | `-1` ok, `-2` overflow, else offset | `T.parse fails ParseError` |
| `byn_hex_decode` (`:344`) | `-1` ok, else offset | `Hex.decode fails ParseError` |
| `byn_map_find` (`:395`) | `-1` | `tryGet` returns `V?` |
| `byn_env_get`, `byn_path_directory_name` (`:558,:351`) | `bool` + out param | `string?` |

Acceptance for these: a comment at each declaration naming the Bayan surface, and a `SelfhostDriverInventoryTests`-style check over this listed set (`byn_str_position`, `byn_str_last_position`, `byn_int_parse_at`, `byn_hex_decode`, `byn_map_find`) that each declaration carries that comment (unverified whether that test file is the right home; it owns `libc-uses.txt` today). Today `byn_map_find`'s comment (`bayan-rt.h:394`, "The entry holding `key`, or -1.") is the only one that does not name its surface. A new sentinel-returning function is added to the set in the same change.

**Unchecked or divergent libc calls to fix or justify** (each row is one commit or one documented exception):

| Call | Line | musl | glibc | macOS | UCRT | Action |
|---|---|---|---|---|---|---|
| `strtod(sci, NULL)` in float formatting | `:439` | C locale | same | same | same: this call has no `#if`, so it runs on Windows too | the runtime never calls `setlocale` (0 matches in `bayan-rt.c`), so the C locale holds; record that, and add a check that `bayan-rt.c` still has no `setlocale` call |
| `atoi(exponent)` | `:449` | returns 0 on garbage, no errno | same | same | same | input is `snprintf`'s own output; add an assert or parse by hand |
| `snprintf` into fixed buffer | `:438` (`sci`), `:458`; `:224,:246` are already checked (`:225,:247`) | returns needed length, may truncate | same | same | pre-2015 UCRT returned -1 on truncation | check the unchecked `:438` and `:458` against buffer size |
| `clock_gettime(CLOCK_MONOTONIC)` | `:3566` | cannot fail on Linux | same | available since 10.12 | n/a | check return or comment why not |
| `QueryPerformanceCounter`/`Frequency` | `:3559-3560` | n/a | n/a | n/a | cannot fail on XP+ | comment |
| `getrandom` | `:2347` | present since musl 1.1.20 | 2.25+ | n/a (`arc4random_buf`) | `BCryptGenRandom` via `GetProcAddress` | checked; keep |
| `newlocale`/`strtod_l` | `:1555,:1593` | musl has `newlocale`; `strtod_l` is declared under `_GNU_SOURCE` (`:3`) (unverified: confirm against the zig-bundled musl version) | ok | ok | `_strtod_l` | keep; no locale test, since the runtime never sets a locale |
| `getenv` vs `GetEnvironmentVariableW` | `:2965,:2940` | returns NULL; a set-but-empty variable is `""` | same | same | `ERROR_ENVVAR_NOT_FOUND` vs empty | both map to `string?`; add a run case for set-but-empty on all three |
| `readdir` errno protocol | `:3072-3075` | `errno = 0` before, check after | same | same | `FindNextFileW` + `GetLastError` | already done; keep as the pattern |
| `fread`/`fclose` | `:2746-2752` | checked | | | | keep |

The two wait rows (`WaitForSingleObject`/`GetExitCodeProcess` `:3419-3421`, `waitpid` `:3510-3512`) moved to W0.

**Added rows.**

- **libc version pin.** `libc-uses.txt` (or a sibling file) records the musl version bundled by the pinned zig, and the mingw-w64 CRT (confirm UCRT vs msvcrt: this decides the `snprintf` row; `Targets.cs:15` only says `x86_64-windows-gnu`, so the CRT is unverified). A test fails when the zig/musl version changes without the audit file's recorded version being updated. That forces re-review of every divergence and "cannot fail" row on each toolchain bump.
- **Interior NUL.** Every runtime function that passes a Bayan string to libc or the OS rejects an embedded `\0` with `IoError` (`fileExists` keeps `false`; see W3). The table lists each such call site. One `run/` golden per API family: open/read, `listDir`, `run` argv, env.
- **errno.** errno is read only after a return that signals failure. `errno = 0` precedes calls whose protocol needs it (`strtod*` with `ERANGE`, `readdir`). Record what `f64.tryParse` returns on overflow (`"1e999"`) and on underflow, and add goldens.

Hashing: the map seed comes from the OS CSPRNG or is fixed (`:2360-2365`). No password or message hashing exists in the runtime, so the `crypt` class of bug has no current site. Record this in the audit table so a future `std.crypto` (W5) does not add one to the runtime.

**Acceptance.** Every row above is either fixed or has a one-line reason in `runtime-libc-audit.md`. `libc-uses.txt` gains a column or a sibling file: per function, "checked at line N" or "cannot fail: reason".
 Each fixed row's error mapping is unit-tested through an injectable seam (`tests/native/`, built by `Toolchain.Build` as `RuntimeNativeTests.cs:193` does); OS-level reproduction is optional. Run on linux-x64 (musl; `RuntimeNativeTests.IsMusl` at `:101` is a private check the existing heap test uses at `:95`, reuse it for musl-only tests) and on wp1 (`tests/windows-known-failures.txt` lists one entry today, `SelfhostEmitterSpeedTests.OneMegabyteLowerAndEmitWithinTenTimes`, and must gain no runtime test).

**Touches.** Spec §21.1 (none), handoff Question: "runtime libc returns: policy is check or document, no exceptions".

### W2. Language rules against sentinels

The discarded-result rule already exists (L3), so the gap is narrower than the brief assumed. W2 is one Question and one gated warning.

**W2a. Spec and code disagree on the discard spelling (Question only).** `let _ = f()` silences BYN3201 (`tests/cases/diag/unused-value.byn:5`; the help text at `Binding/BinderExpressions.cs:70` says the same form), but spec `:142` writes `_ = f()`. Proposed handoff Question: align the spec with the code. No lint.

**W2b. `catch <literal>` warning, gated.** Fire when the handler value is a literal whose type equals the call's non-optional result type (`catch -1` on an `i64` call, `catch ""` on a `string` call). `null` is not a value of a non-optional type (spec `:311`), so it is never a candidate. Do not fire when the handler is `return`/`fail`/`break`/`continue`/a `never` call, when the `catch` is a `catch switch`, or when the enclosing function's name starts with `try` (the `tryX` convention, `docs/bayan-library-tiers.md:200`). A `return <literal>` handler does not fire (the lint only sees a value substituted in place), so the cheatsheet's `catch { return -1 }` (`scripts/selfhost/bayan-cheatsheet.md:198`) stays silent. Fix-it: "handle with `?? return`, `catch return`, or add the error to this function's `fails`".

Gate: before merging, run it over `lib/` and `selfhost/` and report the hit count with a sample of 10 hits classified true or false positive. Ship only if the false-positive rate is low; otherwise drop it and record why.

**W2c** moved into W7 as the migration's progress metric.

**Files.** `src/Bayan.Syntax/Diagnostics/DiagnosticCodes.cs`, a new lint in `src/Bayan.Semantics/Binding/`, `docs/diagnostics/BYNxxxx.md`, `tests/cases/diag/catch-literal-sentinel.byn` and `.diag`, plus the matching `selfhost/semantics` port in the same change as the C#. L022 (`docs/laws/022-binder-equivalence.md`, enacted) requires `bayan-check` and `bayan check` to report the same diagnostics on every clean-parsing diag case, so a C# lint with a golden and no port fails it (conservative choice over an L022 ruling). The port adds lint code only; no existing selfhost return shape changes.

**Acceptance.** One golden that fires and one that stays silent (each exemption above is a line in the silent golden). The dry-run report above is attached to the change. Zero new warnings on `lib/std/json/*.byn`.

**Numbers.** 3xxx is at BYN3219 (`DiagnosticCodes.cs:322`); 4xxx and the other blocks follow `docs/bayan-spec.md`. Lints are planned as "TBD (x9xx of their area, A2)": `docs/bayan-shorthands-proposal.md:289,300,335,360` and `docs/bayan-style-proposal.md:192,317-321`. W2b (control flow and failure, BYN4xxx) is BYN49xx per `docs/revisions/0017-addendum-a2-diagnostic-codes.md:26-28`. The failure diagnostics in code today are BYN26xx (`DiagnosticCodes.cs:216-220`), so confirm in A2 which block W2b belongs to. See Needs decision D4.

**Touches.** Spec §2.5 (`docs/bayan-spec.md:142`, BYN3201 and `_ = f()`), §8 (`:1244`, `catch value`), §21.1 diagnostics (`:2232`); `docs/diagnostics/` one file. Handoff Questions: the `let _ =` spelling (W2a) and W2b's severity (D2); none of the existing Questions covers sentinels (`sentinel` has 0 matches in `docs/bayan-handoff.md`, grep 2026-11-07).

### W3. Std API sweep

**Scope.** Every public function in `Builtins.cs`, `BuiltinMembers.cs` and `lib/std/**/*.byn` that returns a scalar or string where one value means "failed" or "not found".

**Method.** One table in `docs/security/std-sentinel-sweep.md`: function, return type, in-band value if any, verdict (optional / `fails` / keep with reason). Known results from this reading:

| Function | Today | Verdict |
|---|---|---|
| `string.position`, `lastPosition` | `i64?` (`BuiltinMembers.cs:64-66`) | keep |
| `List.tryGet`, `Map.tryGet` | `V?` (`:102,:107`) | keep |
| `Env.get` | `string?` (`Builtins.cs:128`) | keep |
| `T.parse` / `T.tryParse`, `f64.tryParse` | `fails ParseError` / `T?` (`Builtins.cs:333-337`; `bayan-rt.h:334-340`) | keep |
| `Hex.decode` | `fails ParseError` (`Builtins.cs:116,:190`) | keep |
| `Io.readFile`, `readText`, `listDir`, `run`, `remove`, `createDirectory` | `fails IoError` (`bayan-rt.h:446-474,564-570`) | keep |
| `Fs.fileExists` | `bool` (`bayan-rt.h:561`): false for missing, for a directory, for a NUL in the path, and for permission denied | record: "exists" conflates "cannot tell" with "no"; propose `Fs.stat() fails IoError` for callers that need the difference, keep `fileExists` as the convenience |
| `string.count(x)` | `0` for an empty `x` (`bayan-rt.h:211`) | keep: 0 is a count, not an error |
| `Io.run` exit code | `i32` from `GetExitCodeProcess` or `waitpid` even when the wait failed (W0) | fix in W0 |
| `std.json` | `fails JsonError` throughout (`lib/std/json/value.byn:23,:36`) | keep |

**Acceptance.** The table is complete for every public builtin (count them from `BuiltinMembers.cs:140`'s name list and `Builtins.cs`'s `Make` calls); each "fix" row has a `run/` golden; the `docs/bayan-library-tiers.md` §8 rules gain one line: "no in-band failure value; absence is `T?`, failure is `fails`". That line is a handoff Question, not an edit here.

### W4, W5, W6: moved

- **W4** (error-set diff in `bayan pkg diff`, classification table) is requirement 8 in `docs/plans/2026-11-05-003-v0.3-packages.md`.
- **W5** (security-sensitive std design rules) is `docs/security/crypto-api-checklist.md`. The first `std.crypto` spec must cite it. No other W5 work.
- **W6** (unchecked extern return lint, audit row, `@cReturns` with CImport) is requirement 9 in `docs/plans/2026-11-05-004-v0.4-interop.md`.

### W7. Norms: selfhost migration and teaching material

**Timing.** Not during the ports. Spec 010/011/012 ports match C# method for method (Q711); changing return shapes there would break the oracle tests. The migration is its own spec after the self-hosted compiler reaches parity on every golden kind and the bootstrap fixpoint law (L027, `docs/laws/027-bootstrap-fixpoint.md`) is enacted. Working name: spec "selfhost sentinel migration", number assigned then (017 is taken by selfhost-default; the next free number then).

**Progress metric (W2c, moved from W2).** A sentinel-comparison lint: for a non-public function `f` returning an integer type, if every call site compares the result with the same literal (`== -1`, `!= -1`, `< 0`) and `f` has at least one `return <that literal>`, report once at `f`'s declaration: "callers treat `-1` as absence; return `i64?` or declare `fails`". It needs every call site of `f`, so it runs after binding over all functions of the module; `NeverReassignedVarLint` (`Binding/NeverReassignedVarLint.cs:13`) is per-function, so this needs a new module-level pass. It ships with the migration spec, the C# lint and the selfhost port together (L022). It is a warning; a test counts it over `selfhost/` and that count is the baseline. No `lints:` manifest key and no module-level `@allow` are needed. Files: a new `src/Bayan.Semantics/Binding/SentinelLint.cs`, `tests/cases/diag/sentinel-return.byn` and `.diag` (one fires, one silent), `docs/diagnostics/BYNxxxx.md`; code BYN49xx (D4).

**Oracle.** The migration spec's oracle is the migrated compiler against itself: every `run/`, `diag`, `typed`, `parse`, `lex` golden unchanged, L027 fixpoint holds, and the W2c lint count over `selfhost/` goes from the W7 baseline to zero, file by file. Each task takes one file (`selfhost/semantics/states.byn` and `manifest.byn` lead with 13 `return -1` each, `binder.byn` 12).

**Teaching material.** Everything below is now except the porters' cheatsheet, which waits for parity. These are what agents read first:

- `scripts/selfhost/bayan-cheatsheet.md:190-200` is the porters' reference and its `catch { return -1 }` example matches what they port, so it is not touched during the ports. After parity, replace it with `?? return .notFound` and `catch return` and add one line: "a failure is never a value; return `T?` or `fails`". Until then the same text goes in a separate guide for non-porters (the style doc below).
- `docs/bayan-style.md`: a short "absence and failure" rule with the three shapes (`T?`, `fails`, `never`) and the one forbidden one (a literal that is also a valid result).
- `lib/std/json` already models it (`fails JsonError`); link it as the example.
- `bayan new` templates (`src/Bayan.Cli/Scaffold.cs:270,286`): the generated `greeting()`/`twice()` get a sibling that returns `T?` so the first program a user sees has one optional.

**Acceptance.** After parity, the cheatsheet is compiler-checked as its header requires (`bayan-cheatsheet.md:2`); a grep for `return -1` in `docs/**/*.md` code blocks outside the handoff and specs finds only the migration plan.

### W8. Law candidates (user enacts; proposed only)

| Candidate | Kind | Statement | Proof sketch |
|---|---|---|---|
| L029 runtime-no-in-band-error | property | For every exported runtime function in `bayan-rt.h` whose Bayan surface can fail or be absent, the C return is a status code or a `bool` with an out parameter, or the declaration comment names the Bayan surface that wraps its sentinel | parse `bayan-rt.h` declarations; assert each `int64_t`-returning function with a negative sentinel comment has a matching `OptionalType` or `fails` builtin in `BuiltinMembers.cs`/`Builtins.cs` |
| L030 libc-returns-checked | property | Every libc call in `libc-uses.txt` is either checked at its call site or listed as "cannot fail" with a reason | extend `SelfhostDriverInventoryTests` (unverified owner) to read W1's table |
| L031 no-exit-code-zero-on-failed-wait | property | `Io.run` never reports exit code 0 when the wait or the exit-code query failed | W0's injected-failure unit test of the status mapping function |

The sshd bug would have been L029's counterexample: a hash function whose error is a valid hash.

## 3. Needs decision

| # | Choice | Options | Recommendation |
|---|---|---|---|
| D2 | W2b `catch <literal>` severity | warning / error | Warning. It is heuristic (the literal might be a true default); the fix-it does the teaching |
| D4 | Diagnostic numbers | continue 32xx (after BYN3219) / x9xx of each lint's own area as the A2 convention says (`docs/bayan-shorthands-proposal.md:335`; `0017-addendum-a2-diagnostic-codes.md:26-28`) | W2b and W2c BYN49xx; descriptor names only until the A2 numbering, per `docs/plans/2026-11-05-r4-section-10-lessons.md` conventions |
| D5 | Selfhost migration timing | now / per file during ports / after parity and L027 | After parity and L027, its own spec with the goldens plus the fixpoint as oracle |
| D6 | W4 home | `bayan audit` / new `bayan pkg diff` | `bayan pkg diff` (spec `:2250` already groups `audit` and `export --json` there) |
| D7 | W6 marker spelling | `@cReturns(.nullOnError)` / infer from type only | Infer from type in the lint now; add the attribute with `CImport`. One Question |
| D8 | Spec numbers | W1 as a fix batch under 012 reviews / its own spec | Own spec: "runtime libc audit", small, before any `std.crypto` work; it owns W0, W1, the W2a Question, W2b, W3 and the W5 checklist. W4 is a v0.3 packages requirement, W6 a v0.4 interop requirement; W7 is its own spec (D5) |
| D9 | Laws | enact L029-L031 as proposed / fold into L005's style | Enact L031 first (smallest, one counterexample); L029 and L030 after W1 gives them their tables |

## 4. Out of scope

Memory safety, the `unsafe` rules, taint (`Untrusted<T>`) beyond the one "secret sink" row in W5, PAM-style pluggable auth, any change to `docs/bayan-spec.md` or `docs/bayan-handoff.md` from this plan, and any edit to `selfhost/` before the W7 spec.

## Noticed, not addressed

`Fs.fileExists` returning `false` for permission denied (`bayan-rt.h:561`) is the same conflation the article describes, one level down; W3 records it, nothing fixes it here.
