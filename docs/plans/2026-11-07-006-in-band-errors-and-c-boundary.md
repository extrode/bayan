# In-band errors and C-boundary safety (sshole lessons)

Date: 2026-11-07. Plan only: no code, spec or test edits. Spec gaps below are proposals for handoff Questions.

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
| L1 | Errors travel on their own channel | `fails`, `union error`, `try`, exhaustive `catch switch`, `catch (E.v)` | exists | `docs/bayan-spec.md:1224-1247`; BYN2602-2606 `src/Bayan.Syntax/Diagnostics/DiagnosticCodes.cs:215-220` |
| L2 | Absence is a type, not a value | `T?`, `??`, `?? return/fail` | exists | spec `:310-313`; `position` is `i64?` `BuiltinMembers.cs:64-66`; `tryGet` `:102,:107` |
| L3 | A dropped result is a bug | BYN3201, "an unused non-void value is a compile error"; `_ = f()` discards | exists, opt-out takes no reason | spec `:141`; `Binding/BinderExpressions.cs:65-70`; golden `tests/cases/diag/unused-value.byn` |
| L4 | A failure must not become a valid-looking value | `f() catch value` is allowed with any `value` | partial: legal and documented, nothing warns | spec `:1244`; cheatsheet shows `catch { return -1 }` `scripts/selfhost/bayan-cheatsheet.md:198`; `Io.readText(path) catch ""` `selfhost/driver/build.byn` |
| L5 | A library cannot change its error contract silently | exhaustive `catch switch` breaks on a new variant; no tool reports it as a breaking change | partial: compile error exists, no API diff | spec `:1244`; `bayan audit` lists bypasses only `src/Bayan.Cli/Audit.cs:8-10`; §19.3 names advisories as spec-only `:2124` |
| L6 | C's in-band errors stay in C | `extern` needs `unsafe`; wrappers are "once in safe APIs" by convention | partial: no check that a wrapper translates NULL/-1 | spec `:2056-2057`; `comptime CImport` is spec-only (no match in `src/`) |
| L7 | Security states are variants, not magic strings | `Secret<T>`, `equalsConstantTime`, `CryptoRandom`, `Untrusted<T>` | spec only for secrets; `Untrusted` partial | spec `:1718-1726`; no `Secret` in `Builtins.cs`; `Untrusted` 5 hits in `Builtins.cs` |
| L8 | libc returns are checked | out-of-memory panics (`bayan-rt.c:186,1407,1439`), `getcwd` (`:2098-2099`), `getrandom` (`:2347-2351`) | partial: `clock_gettime` `:3566`, `QueryPerformanceCounter` `:3559`, `WaitForSingleObject`/`GetExitCodeProcess` `:3419-3421`, `snprintf` `:458`, `atoi` `:449` unchecked | audit in W1 |
| L9 | No locale-dependent parsing | per-call "C" locale, `strtod_l`/`_strtod_l` | exists for parsing; formatting still calls plain `strtod` and `atoi` | `bayan-rt.c:1544-1559,1591-1593` vs `:439,449` |
| L10 | The norm-setter follows the norm | selfhost code | missing by design until parity | counts above; Q711 |

## 2. Workstreams, easiest first

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

Acceptance for these: a comment at each declaration naming the Bayan surface, and a `SelfhostDriverInventoryTests`-style check that no new `int64_t`-returning runtime function uses a negative sentinel without that comment (unverified whether that test file is the right home; it owns `libc-uses.txt` today).

**Unchecked or divergent libc calls to fix or justify** (each row is one commit or one documented exception):

| Call | Line | musl | glibc | macOS | UCRT | Action |
|---|---|---|---|---|---|---|
| `strtod(sci, NULL)` in float formatting | `:439` | locale-sensitive decimal point | same | same | same: this call has no `#if`, so it runs on Windows too, unlike the parser's `_strtod_l` at `:1591` | use `byn_c_locale()` as `:1591-1593` do, or document that the runtime never calls `setlocale` (0 calls) and a linked C library might |
| `atoi(exponent)` | `:449` | returns 0 on garbage, no errno | same | same | same | input is `snprintf`'s own output; add an assert or parse by hand |
| `snprintf` into fixed buffer | `:458,:224,:246` | returns needed length, may truncate | same | same | pre-2015 UCRT returned -1 on truncation | check against buffer size |
| `clock_gettime(CLOCK_MONOTONIC)` | `:3566` | cannot fail on Linux | same | available since 10.12 | n/a | check return or comment why not |
| `QueryPerformanceCounter`/`Frequency` | `:3559-3560` | n/a | n/a | n/a | cannot fail on XP+ | comment |
| `WaitForSingleObject`, `GetExitCodeProcess` | `:3419-3421` | n/a | n/a | n/a | can fail; exit code then stays 0 (success) | a failed wait must be an `IoError`, never exit code 0 |
| `getrandom` | `:2347` | present since musl 1.1.20 | 2.25+ | n/a (`arc4random_buf`) | `BCryptGenRandom` via `GetProcAddress` | checked; keep |
| `newlocale`/`strtod_l` | `:1555,:1593` | musl has `newlocale`; `strtod_l` is a glibc/BSD extension, declared under `_GNU_SOURCE` (`:3`) and present in musl since 1.1.x as a wrapper that ignores the locale (unverified: confirm against the zig-bundled musl version) | ok | ok | `_strtod_l` | add a musl run test that parses `1.5` with `LC_ALL=de_DE.UTF-8` set |
| `getenv` vs `GetEnvironmentVariableW` | `:2965,:2940` | returns NULL; a set-but-empty variable is `""` | same | same | `ERROR_ENVVAR_NOT_FOUND` vs empty | both map to `string?`; add a run case for set-but-empty on all three |
| `readdir` errno protocol | `:3072-3075` | `errno = 0` before, check after | same | same | `FindNextFileW` + `GetLastError` | already done; keep as the pattern |
| `fread`/`fclose` | `:2746-2752` | checked | | | | keep |

Hashing: the map seed comes from the OS CSPRNG or is fixed (`:2360-2365`). No password or message hashing exists in the runtime, so the `crypt` class of bug has no current site. Record this in the audit table so a future `std.crypto` (W5) does not add one to the runtime.

**Acceptance.** Every row above is either fixed or has a one-line reason in `runtime-libc-audit.md`. `libc-uses.txt` gains a column or a sibling file: per function, "checked at line N" or "cannot fail: reason". A native test (`tests/native/`, built by `Toolchain.Build` as `RuntimeNativeTests.cs:193` does) drives `byn_io_run` with a process the OS refuses to wait on where that is reproducible; otherwise a unit test on the error-mapping function. Run on linux-x64 (musl, `RuntimeNativeTests.IsMusl` at `:101` already gates musl-only tests) and on wp1 (`tests/windows-known-failures.txt` lists one entry today, `SelfhostEmitterSpeedTests.OneMegabyteLowerAndEmitWithinTenTimes`, and must gain no runtime test).

**Touches.** Spec §21.1 (none), handoff Question: "runtime libc returns: policy is check or document, no exceptions".

### W2. Language rules against sentinels

Three items. The discarded-result rule already exists (L3), so the gap is narrower than the brief assumed.

**W2a. `_ = f()` takes a reason.** Today `_ = f()` silences BYN3201 with no text (`tests/cases/diag/unused-value.byn:5`). Proposal: a lint, not a rule, that fires on `_ = f()` when `f` returns `T?`, `Result`, or a type named in a `fails` clause's payload, and is silenced by `@allow(BYNxxxx) because "…"` (`spec :710,:781`). Plain `_ = one()` on an `i64` stays silent. Rationale: a dropped optional or result is exactly the unchecked-NULL shape; a dropped count is not.

**W2b. `catch <literal>` lint.** Fire when the handler value is a literal that is also a valid result of the call: `-1`, `0`, `""`, `false`, `null` only when the call's type is non-optional and the literal lies inside its range. Do not fire when the literal is `null` on a `T?`-typed binding, when the handler is `return`/`fail`/`break`/`continue`/a `never` call, when the `catch` is a `catch switch`, or when the enclosing function's name starts with `try` (the `tryX` convention, `docs/bayan-library-tiers.md:200`). Fix-it: "handle with `?? return`, `catch return`, or add the error to this function's `fails`". Scope note: the cheatsheet's `catch { return -1 }` (`bayan-cheatsheet.md:198`) is a `return`, so it does not fire; the lint should.

**W2c. Sentinel-comparison lint (callers compare with a literal).** For a non-public function `f` returning an integer type, if every call site compares the result with the same literal (`== -1`, `!= -1`, `< 0`) and `f` has at least one `return <that literal>`, report once at `f`'s declaration: "callers treat `-1` as absence; return `i64?` or declare `fails`". Needs whole-module call graph, so it runs in the analysis loop (after binding, like `NeverReassignedVarLint`, `Binding/NeverReassignedVarLint.cs`). Off in `selfhost/` until W7 (a package-level `lints:` entry in `package.byn`, or `@allow` at module level; the former is the simpler story).

**Files.** `src/Bayan.Syntax/Diagnostics/DiagnosticCodes.cs`, a new `src/Bayan.Semantics/Binding/SentinelLint.cs`, `docs/diagnostics/BYNxxxx.md` (three files), `tests/cases/diag/{discard-optional,catch-literal-sentinel,sentinel-return}.byn` and `.diag`, plus the matching `selfhost/semantics` port only after W7 (the port rule: C# first, port second, oracle test).

**Acceptance.** Each lint has a golden that fires and one that stays silent (the exemptions above are each a line in the silent golden). Running the three lints over `selfhost/` prints a count that W7 uses as its baseline. Zero new warnings on `lib/std/json/*.byn`.

**Numbers.** 2xxx is at BYN2832, 3xxx at BYN3218, 5xxx at BYN5103 (last codes in `docs/bayan-spec.md`). Lints are planned as "TBD (x9xx of their area, A2)": `docs/bayan-shorthands-proposal.md:289,300,335,360` and `docs/bayan-style-proposal.md:192,317-321`. All three W2 lints are semantic, so they would be BYN29xx, assigned in A2. See Needs decision D4.

**Touches.** Spec §2.5 (`docs/bayan-spec.md:141`, BYN3201 and `_ = f()`), §8 (`:1244`, `catch value`), §4.4 (`:710`, `@allow(X) because`), §21.1 diagnostics (`:2232`); `docs/diagnostics/` one file per code. Handoff Questions: new ones for D1-D4; none of the existing Questions covers sentinels (`sentinel` has 0 matches in `docs/bayan-handoff.md`, grep 2026-11-07).

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
| `Io.run` exit code | `i32` from `GetExitCodeProcess` even when the wait failed (W1 row) | fix in W1 |
| `std.json` | `fails JsonError` throughout (`lib/std/json/value.byn:23,:36`) | keep |

**Acceptance.** The table is complete for every public builtin (count them from `BuiltinMembers.cs:140`'s name list and `Builtins.cs`'s `Make` calls); each "fix" row has a `run/` golden; the `docs/bayan-library-tiers.md` §8 rules gain one line: "no in-band failure value; absence is `T?`, failure is `fails`". That line is a handoff Question, not an edit here.

### W4. Error-set evolution and API diff

**Problem.** Adding a variant to a public `union error` compiles for the library and breaks every exhaustive `catch switch` downstream (spec `:1244`). That is the right default (the article's point), but it must surface as a major change, not as a surprise.

**Scope.** `bayan pkg export --json` (spec `:2117`) gains the public error sets with their variants and payload types. A new `bayan api diff <old.json> <new.json>` (or a mode of `bayan audit`) classifies: variant added to a public error set = major; variant removed = major; payload type changed = major; `fails` clause widened on a public function = major; narrowed = minor. Advisories (spec `:2124`, spec-only) carry the same classification so `bayan audit` can say "this update adds `ParseError.tooLong`; 3 `catch switch` sites in your code do not handle it".

**Files.** `src/Bayan.Cli/` (new `ApiDiff.cs`), the JSON export, `docs/bayan-spec.md` §19.3-19.4 text (Question), `tests/cases/` new kind or `tests/Bayan.Tests/ApiDiffTests.cs`.

**Acceptance.** Golden pairs of exports with expected classification; a run test where a dependency adds a variant and the consumer's `catch switch` fails with BYN-exhaustiveness and the diff names the same variant. Depends on packages (`docs/plans/2026-11-05-003-v0.3-packages.md`), so it is scheduled with v0.3.

### W5. Security-sensitive std design rules (future `std.crypto`, auth)

No code exists yet (`Secret` absent from `Builtins.cs`; §15.2-15.3 are spec-only). This workstream writes the rules down before the first line, as a section in `docs/bayan-library-tiers.md` (Question) and a checklist in `docs/security/`:

1. No magic-string states. A password-hash API returns `union error HashError { tooLong(i64 max), unsupportedAlgorithm, … }` and a distinct `PasswordHash` type; "locked" is a field or variant of the account, never a hash value.
2. Inputs above a limit are `fail .tooLong`, never truncated and never hashed as a sentinel. The limit is a named constant in the signature's doc.
3. Fail closed by construction: verification returns `bool` only through `Secret<T>.equalsConstantTime` (spec `:1719`), and the function that decides access takes a `Verified` typestate (§3.17), so a caller cannot pass a raw `bool` it computed itself.
4. Constant-time paths take `Secret<T>` parameters; a `string` password argument is BYN8xxx (the `Untrusted` sink rule, spec `:1691`, extended to a "secret sink").
5. Randomness only through `CryptoRandom` (spec `:1726`).
6. Any libc or OS primitive used underneath goes through W1's table first (`crypt`, `getentropy`, `BCrypt*`).

**Acceptance.** The checklist exists and the first `std.crypto` spec cites each item with a golden. A law candidate is in §W8.

### W6. FFI boundary: wrappers translate in-band C errors

**Scope.** For an `extern c` function whose return is a pointer or a signed integer, the safe wrapper that calls it inside `unsafe because` must branch on the return before any other use. `bayan audit` already lists every `extern` and `unsafe` (`Audit.cs:8-10`, tokens only). Extend it with one check that needs binding, so it moves from tokens to the bound tree or becomes a lint instead:

- Lint "unchecked extern return": the result of a pointer- or `c_int`-returning `extern` call is used, stored or returned without a comparison, `?? `, or `is null` on the same path. Opt-out `@allow(…) because "…"`.
- `bayan audit` row: for each `extern`, the wrapper function name and whether the lint passed, so a reviewer sees "sqlite3_open: checked" per binding.
- `comptime CImport` (spec `:2055`, not implemented): when it lands, imported functions carry a `@cReturns(.nullOnError)` / `.negativeOnError` / `.errno` marker inferred from the header where possible (`int` → `.negativeOnError` by default), and the lint uses the marker instead of the type. Until then the marker is written by hand on the `extern` line.

**Files.** `src/Bayan.Cli/Audit.cs`, a new lint in `src/Bayan.Semantics/Binding/`, `docs/bayan-spec.md` §18 (Question: the marker attribute), goldens `tests/cases/diag/extern-unchecked-return.byn`, and a `run/` case with a real C source via `build.addCSource` once that exists (spec `:2058`, status unverified in `src/`).

**Acceptance.** The v0.4 interop plan's SQLite item (`docs/plans/2026-11-05-004-v0.4-interop.md` item 1) passes the lint with zero `@allow`; the JauntyQ shim's "status → CatalogError" rule (same file, "Shim rules") is the lint's positive example.

### W7. Norms: selfhost migration and teaching material

**Timing.** Not during the ports. Spec 010/011/012 ports match C# method for method (Q711); changing return shapes there would break the oracle tests. The migration is its own spec after the self-hosted compiler reaches parity on every golden kind and the bootstrap fixpoint law (L027, `docs/laws/027-bootstrap-fixpoint.md`) is enacted. Working name: spec "selfhost sentinel migration", number assigned then (017 does not exist yet; unverified which number is free by then).

**Oracle.** The migration spec's oracle is the migrated compiler against itself: every `run/`, `diag`, `typed`, `parse`, `lex` golden unchanged, L027 fixpoint holds, and the W2c lint count over `selfhost/` goes from the W2 baseline to zero, file by file. Each task takes one file (`selfhost/semantics/states.byn` and `manifest.byn` lead with 13 `return -1` each, `binder.byn` 12).

**Teaching material, now, not after parity.** These are what agents read first:

- `scripts/selfhost/bayan-cheatsheet.md:190-200`: replace the `catch { return -1 }` example with `?? return .notFound` and `catch return`, and add one line: "a failure is never a value; return `T?` or `fails`".
- `docs/bayan-style.md`: a short "absence and failure" rule with the three shapes (`T?`, `fails`, `never`) and the one forbidden one (a literal that is also a valid result).
- `lib/std/json` already models it (`fails JsonError`); link it as the example.
- `bayan new` templates (handoff Q36, `docs/bayan-handoff.md:523`): the generated `greeting()`/`twice()` get a sibling that returns `T?` so the first program a user sees has one optional.

**Acceptance.** The cheatsheet is compiler-checked as its header requires (`bayan-cheatsheet.md:2`); a grep for `return -1` in `docs/**/*.md` code blocks outside the handoff and specs finds only the migration plan.

### W8. Law candidates (user enacts; proposed only)

| Candidate | Kind | Statement | Proof sketch |
|---|---|---|---|
| L029 runtime-no-in-band-error | property | For every exported runtime function in `bayan-rt.h` whose Bayan surface can fail or be absent, the C return is a status code or a `bool` with an out parameter, or the declaration comment names the Bayan surface that wraps its sentinel | parse `bayan-rt.h` declarations; assert each `int64_t`-returning function with a negative sentinel comment has a matching `OptionalType` or `fails` builtin in `BuiltinMembers.cs`/`Builtins.cs` |
| L030 libc-returns-checked | property | Every libc call in `libc-uses.txt` is either checked at its call site or listed as "cannot fail" with a reason | extend `SelfhostDriverInventoryTests` (unverified owner) to read W1's table |
| L031 no-exit-code-zero-on-failed-wait | property | `Io.run` never reports exit code 0 when the wait or the exit-code query failed | native driver with a handle closed early (Windows); `waitpid` with a bad pid (Unix) |

The sshd bug would have been L029's counterexample: a hash function whose error is a valid hash.

## 3. Needs decision

| # | Choice | Options | Recommendation |
|---|---|---|---|
| D1 | W2a discarded optional/result: rule or lint | error like BYN3201; warning; off | Warning by default, error under `bayan check --strict` if that exists (unverified); `@allow … because` opt-out. A hard error would hit `_ = map.tryGet(…)` idioms in tests |
| D2 | W2b `catch <literal>` severity | warning / error | Warning. It is heuristic (the literal might be a true default); the fix-it does the teaching |
| D3 | W2c sentinel-comparison lint scope | all code / public only / off in selfhost | All code, with `lints:` disabled for `selfhost/` in its `package.byn` until W7, so the baseline is measurable |
| D4 | Diagnostic numbers | continue 32xx (after BYN3218) / x9xx of the semantic area (BYN29xx) as the A2 lint convention says (`docs/bayan-shorthands-proposal.md:335`) | BYN29xx, as the other planned lints do; descriptor names only until the A2 numbering, per `docs/plans/2026-11-05-r4-section-10-lessons.md` conventions |
| D5 | Selfhost migration timing | now / per file during ports / after parity and L027 | After parity and L027, its own spec with the goldens plus the fixpoint as oracle |
| D6 | W4 home | `bayan audit` / new `bayan api diff` / `bayan pkg` | `bayan api diff` as a verb of `bayan pkg` (spec `:2250` already groups `audit` and `export --json` there) |
| D7 | W6 marker spelling | `@cReturns(.nullOnError)` / infer from type only | Infer from type in the lint now; add the attribute with `CImport`. One Question |
| D8 | Spec numbers | W1 as a fix batch under 012 reviews / its own spec | Own spec: "runtime libc audit", small, before any `std.crypto` work; W2 and W3 share a spec; W4 joins v0.3 packages; W5 and W6 join v0.4 interop |
| D9 | Laws | enact L029-L031 as proposed / fold into L005's style | Enact L031 first (smallest, one counterexample); L029 and L030 after W1 gives them their tables |

## 4. Out of scope

Memory safety, the `unsafe` rules, taint (`Untrusted<T>`) beyond the one "secret sink" row in W5, PAM-style pluggable auth, any change to `docs/bayan-spec.md` or `docs/bayan-handoff.md` from this plan, and any edit to `selfhost/` before the W7 spec.

## Noticed, not addressed

`Fs.fileExists` returning `false` for permission denied (`bayan-rt.h:561`) is the same conflation the article describes, one level down; W3 records it, nothing fixes it here.
