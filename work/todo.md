# Todo — bayan

The unscheduled backlog: everything known to be worth doing that nobody has committed to a date.
Items enter here from anywhere (a review comment, an out-of-scope note, a bug found in passing)
and leave by being promoted into `status/next.md` or dropped with a reason.

Keep it honest rather than tidy. A backlog that only contains things someone intends to do is a
wish list; the value is in recording the ones nobody has got to yet.

## Now-ish

Small, clear, and could be picked up without a design decision.

- CI: install zig (or set `BAYAN_CC`) on all three runners so `run/` goldens execute there.

## Later

Real, but needs a decision, a dependency, or more information first. Say which.

- `(T)-x` parses as subtraction (handoff Q24); needs a language decision on cast-vs-binary for keyword types.
- `i64.max` / `i64.min` style integer limits (Q25); needs a spec decision on syntax.

## Dropped

Not doing, with the reason. Kept so the same idea does not get re-raised every quarter.

- (nothing yet)
- Implement the `borrows a` narrowing clause (spec §9.4 rule 4); until then array-literal args make call results borrow the literal (handoff Q33).
- `src/runtime/bayan-rt.c:277-278,337-342` list/arena growth (`cap * 2`, `grown * elem_size`, `n + 15`) is not overflow-checked (review 246772b..5f5ec5c).
- `src/Bayan.Backend.C/Toolchain.cs:79-84` concurrent builds of one program rewrite the same cache dir; cache key omits the C compiler version (review 246772b..5f5ec5c).
- `Toolchain.Lookup` (src/Bayan.Backend.C/Toolchain.cs): an `IOException` while hashing (e.g. a Windows sharing violation) discards a good shared entry; `UnauthorizedAccessException` escapes. Review 1557d6fd639d..29c1d013a33e.
- `Toolchain.Stats` throws `DirectoryNotFoundException` if an entry is discarded mid-enumeration (`bayan cache stats`, `doctor`). Review 1557d6fd639d..29c1d013a33e.
- `Scaffold.Git` trims stdout, so `test --changed` with `-z` loses leading whitespace of the first file name.
- Test timeout detail prints "timed out after 0 s" for sub-second `BAYAN_TEST_TIMEOUT_MS`.
- `test --changed` on macOS: git reports NFC paths, so a module directory with a non-ASCII name stored in NFD never matches and its tests are skipped. Review 1557d6fd639d..29c1d013a33e.
- `catch { … }` with a block body does not parse; it is read as a struct literal (found in M15, spec 001).
- `bayan run` on a single file compiles every `.byn` in that file's directory (found in M15, spec 001).
- `Map.get` on a map whose value type is already optional returns `V??` without a diagnostic (M20, spec 001).
- `using` on a `Map` is BYN2226 while `List` allows it (M20, spec 001).
- A struct with a `List` field passes the formattable check but makes `CEmitter.Format` throw (M20, spec 001).
- Generated union format helper indents `break;` one level too deep (cosmetic, M18, spec 001).
- BYN3002 help suggests `.copy(to: alloc)` for an `Owned` value, which has no such method; `RecursiveStruct`/`RecursiveUnion` descriptors now unreferenced in `src/` (M21)
- Generics (M19): `mod.f<T>(…)` type args on a member expression not wired; shape checks (borrowed fields, recursion) skip instances; polymorphic recursion (`f<T>` → `f<List<T>>`) unbounded; no target-type inference (`Option2.Nothing`, `Pair` without args); generic struct formats as `Pair { … }` without type args; `print(x)` with `x: T` rejected (use `$"{x}"`)
- Misplaced doc comment above `BoundVariantGroup` in `BinderExpressions.cs` (belongs to `BoundBuiltinGroup`)
- `LexerTests.OneMegabyteAllocatesOnlyTheTokenArray` flaky under parallel runs (seen once by the M19 writer)
- `List<T>` copied by value (`var b = a; b.add(…)` ×N) shares and reallocs storage: heap use-after-free under the default allocator (ASan, review of spec 001); pre-existing since v0.1, needs `unique`/`move` (spec §9) or a BYN2762-style copy ban
- `Io.readText` abandons the whole file buffer from `context.allocator` when the bytes are not UTF-8 (`bayan-rt.c` read path)
- `Io.run` (POSIX): only the pipe fds are `FD_CLOEXEC`; `byn_io_open` uses `fopen` without `"e"`, so open files are inherited by children
- Flow analysis: `x ??= return 3;` followed by code gives a false BYN2405 "unreachable code" warning (`x = x ?? return 3;` does not); found in the spec 002 loop-3 review
- L005 proof never runs a real `cmd.exe`: the read-back model in `tests/Bayan.Tests/L005RunBatchTests.cs` encodes cmd rules by hand, so a quoted character cmd.exe or code page 65001 reads differently would pass; needs a Windows run of the generated cases through `cmd.exe` and a `%~1`/`shift` batch file (review of feat/laws-005-007)
- `map[k].clear()` and `map[k].add(x)` report BYN2106: a `map[k]` receiver is a write target only for `take()` (spec 003 T1); other mutating builtins on a map element need the same `MapWriteTarget` treatment
- Unverified (spec 003 review, finding F): `StrBuf.take()` refills the place using the current `byn_context` allocator, and RegionAnalysis does not see the `StrBufGrow` the lowering introduces; check whether a `take()` on a `ref StrBuf` from an outer region allocates the refill in the wrong region
- `_ = move a;` reports BYN2001, while BYN1012's help (for a bare `move a;`) suggests `_ = f();`; either accept a discarded move or change the help text
- Spec 003 review loop 3: `CEmitter` skips the after-call claim of a root moved anywhere in the statement, even on a branch that does not run (`let n = flag ? consume(move b) : b.grow();`): storage grown on the other branch stays unclaimed; no failing program found
- xunit `Assert.Contains`/`DoesNotContain` on strings is culture-sensitive, so ICU ignores `\u001b` and `"\u001b["` matches any `[`: the older escape checks in `CliEnvironmentTests` (e.g. `ColorFollowsTheTerminalNoColorAndTheFlag`) can pass or fail for the wrong reason; pass `StringComparison.Ordinal` (found in spec 005 T3)
- Spec 004 R4: a discarded first pass now drops the generic function instances it created, but not generic struct/union instances (`InstantiateType`), which can stay as unused `i32` instantiations
- Spec 004 R4: a value derived from a pending `var` through a call (`n = id(n);`, generic `T` inferred from `n`) counts as a typed `i32` use and fixes `n` at `i32`
- Spec 004 R4: an optional integer target (`i64?`) does not count as a constraining use, so a later `u8` use can still retype the local
- `SelfhostLexerTests` (`StdinRuns`, sequential `bayan-lex` processes) did not finish within 10 minutes while several worktrees ran `dotnet test` at once (spec 004 T9); the rest of the suite took about 75 s, and running `bayan-lex --json` by hand on `tests/cases/lex/*.byn` exits right away
- An untyped array literal used directly as a `for` source (`for (let w in ["a", "b"])`) gives BYN2218 "needs a known type here"; infer the element type (found in 004 T15).
- Spec §21.2 CLI table lists neither `bayan check`, `lex` and `parse` nor the new `types` (spec 004 T16)
- R4 literal inference (spec 004 review): retyping a `var` at its first constraining use can break a later use that only compiled by widening (`var n = 0; i64 big = n; takeI32(n);` → BYN2202) and silently changes overflow behaviour (`var n = 2147483647; i64 big = n; n += 1;` no longer panics). Consider a lint or requiring all constraining uses to agree.
- R6 receiver borrows (spec 004 review): reassigning the whole receiver (`b = Buf{…}`) while a span from `b.view()` is alive is not BYN3004 (memory-safe, same as the direct `b.data.span` form).
- `List.removeRange` on a list of unique elements does not free the removed elements (same as `clear`; spec 004 review).
- R4 pending-var groups resolve by statement order (`var a = 0; var b = 0; i64 x = a; u8 y = b; a = b;` is BYN2202, swapped declarations compile); a widest-fitting choice would remove the order dependence (review loop 2, 004).
- `LexerTests.OneMegabyteAllocatesOnlyTheTokenArray` fails when other test suites run concurrently on the machine (per-thread allocation count); passes alone. Seen 4 times during the 004/005 runs.
- R4 pending-var groups with mixed uses: `var a = 0; var b = 0; a = b; f64 y = a; u8 x = b;` is BYN2202 (group falls back to i32). Needs a rule that picks a type converting to every member's use (u8 here); neither "first integer constraint" (breaks i64+f64) nor "fall back to i32" (breaks u8) works. Dev rejects it too (review loop 3, 004).
- Round 2 §1: spec §3.12-3.14 still show PascalCase union cases and no case values; `flags` is still lexed (spec-only part of the plan, not done in the unions compiler task).
- Map literals do not diagnose duplicate constant keys (`["a": 1, "a": 2]` keeps the last); spread element types must match the literal's exactly (no `i32` → `i64` widening per element) (round 2 §11 literals).
- HANDOFF §10/§18: SPEC §19.2 manifest example and `docs/diagnostics/BYN310x.md` examples still use `field = value`; `examples/*/` have no `package.byn` to migrate (r2-colon step was code only).
- wasm32-wasi build of `src/runtime/bayan-rt.c` fails on `sys/wait.h`, `signal.h` and `pipe` (pre-existing, seen during the round-2 defer work); `defer panicking` handlers never run there (`BYN_PANIC_CATCH` is 0 without setjmp).
- Panic handlers (BYN2612) are not checked for allocation inside user functions they call; only direct allocations in the handler body or the `Panic.handler` function are reported.
- BYN2613 accepts a `let` moved after the handler registers (`let xs = …; defer panicking print($"{xs.length}"); take(move xs);`); the handler would read a moved value.
- HANDOFF §5 (r2-catch): `T!S` type syntax does not parse today, so `error set` is usable only in `fails`; `/// A type X = T alias` doc in Compilation.cs sits on `MutationCheck` instead of `AliasPlaceholder`.
- `Messages` (005 T4) formats with the current culture to stay byte-identical; `DiagnosticDescriptor.Format` uses the invariant culture, so `{1:0.0} MB` differs by locale (decide when translations land).
- R15 (005 T15): `HelpLanguageTests` scans help literals right after `help:`/`help =`; ternary branches and help variables built from lists (e.g. `OwnershipAnalysis` options) are only covered through the goldens, and CLI `stderr.WriteLine("  help: …")` lines in `Dist.cs`/`Scaffold.cs` are not scanned.
- `bayan test` (005 T12): the "top three stack frames" trim is untested end to end because the runtime prints one `at file:line` frame per panic.
- The error cap line (`… and N more errors`) follows the last diagnostic with no blank line between them (`tests/cases/style/diag-cap.*`); the guide does not say which (005 T14).
- `bayan test -v` pass lines have no per-test timing (spec 005 R8 asked for one)
- `bayan test -v` pending line is `  ◆ <module>: <name> (pending)`, not the planned `  pending <module>: <name>: <message>` (spec 005)
- Diagnostics for loose files show absolute paths in `-->` (spec 005 R4: relative to the package root)
- On a tty, .NET console setup writes the keypad escape `\e[?1h\e=` (also after each child process), so `NO_COLOR` output is not escape-free; pre-existing on dev (005 review A1)
- The ASCII / `TERM=dumb` progress line leaves `... <phase>` in scrollback; the guide's §6 "removed when the operation finishes" and "prints `... <phase>` once" disagree (005 review A5)
- `src/Bayan.Cli/Progress.cs:60-67` `Columns` counts emoji outside U+1F300–1F64F/U+1F900–1F9FF (🚀 U+1F680–1F6FF, U+1FA70–1FAFF, BMP emoji such as ✅ U+2705) as 1 column, so a long progress line can still wrap (005 review loop 3).
- `src/Bayan.Cli/Messages.cs:156`: the unselectable-rerun notice says "control characters" for bidi-only names and is always singular (005 review loop 3).
- Mixed selectable/unselectable failures in one `bayan test` run have no test (005 review loop 2).

- (worktree audit 2026-10-22) clean worktrees whose work is already in a branch: chore-laws-timeout and feat-005-terminal-style (merged into `dev`); worker-r2-{cases,catch,defer,flow,naming,null,nullmove,refthis,requires,unions}, docs-design-round-2, worker-r3-plan, worker-007-docs (contained in their feature branches). The worker keys for r2-* and 007-docs are still live in `~/.claude`; worker-r3-plan has no key left.
- BYN2211 help text still suggests the replaced `if (p is { } value)` idiom (round 2 §null narrowing).
- Null narrowing not applied in `switch` `when` guards, `?:`, or `do`/`while` conditions (round 2 §null).
- `error set` alias of several sets is rejected as a `catch switch` arm pattern (`BinderCalls.cs:754,982` accept only `ErrorSetSymbol`) (round 2 §5).
- Spec §7.4 lists BYN2232 for a fallible sequence without `for try`; r2-flow reuses BYN2221 with a `try` fix-it. Decide whether BYN2232 is still wanted (round 2 §4).
- `if unlikely` emits no branch hint to C yet (round 2 §4).
- BYN1008's help still suggests the paren form (round 2 §4).
- CHANGELOG still says `requires` is reserved (round 2 §7).
- A `requires Upper` clause right before an empty `{}` body could parse as a struct literal (round 2 §7).
- `distinct type` is not parsed, so BYN2322 has only its descriptor and page for that case (round 2 §7).
- Redundant checks on an already-narrowed value (`p ?? 0`, `p is null`) now raise BYN2210 or a pattern-type error instead of passing; consider a dedicated "already non-null" lint (round 2 §4 null).
- With a narrowed local, `b is null` reports BYN2411 "`null` cannot match `T`" instead of an always-false note (round 2 §4 null).
- BYN2117 labels a duplicate `ref this` "after other parameters" even when nothing is in between (round 2 §9).
- In-place element access is top level only: `map[k].inner.f()` with `ref this` still gives BYN2106 (round 2 §9).
- `getOrAdd` does not exist; `Map.slot` is the closest builtin (round 2 §9/§11 naming).
- Interfaces declaring `ref this`, with implementations required to match, wait for spec 006 (round 2 §9).
- `switch (move d)` on a narrowed optional types the subject as `T?` (so `case null:` is accepted as dead) while `switch (d)` types it `T` (BYN2411); re-wrap the narrowing in BindSwitchSubject as BindMove does (round 2 §4 null).
- `move` parameters are never narrowed by `if (b is not null)` (`BinderExpressions.cs` IsNonNull); `b.length` still reports BYN2211 (round 2 §4 null).
- 006 T7: an enumerator held in a Map slot (`m[k]`, a `BoundBuiltinCall`) is spilled and copied instead of checked for writability (Binder.cs `UserElement`).
- 006 T9: `dyn-ref-dispatch` default `describe` prints instead of returning a string, since a returned interpolated string allocates through `byn_context`.
- 006 reviewfix: BYN2807 is cached per instantiation, so a second call site with the same failing type argument is not reported again.
- 006 T6: a generic interface method called through a constraint or a `dyn ref` reports Unsupported.
- 006 T9: `CEmitter` `SlotName` can collide a getter `x` (`get_x`) with a method named `get_x`; vtable pointer member uses `PrimitiveType.I64` as a placeholder dependency type (`members.Add((PrimitiveType.I64, "void* data"))`).
- 006 T8: a struct literal cannot be the bare iterable (`for x in S { … } {` is BYN1005); goldens wrap it in parentheses.
- 006 T7: `Binder.cs:749` treats any `BoundField` enumerator as a place regardless of receiver, so `for n in make().stream` takes the MutationCheck path instead of spilling.
- `[Cases(1)]` on a test with no parameters gives BYN2773 with the awkward help "in order: `()`" (`Compilation.cs:289-291`) (round 2 §16).
- Named tuple elements in a case `(a: 1, b: 2)` are accepted and the names ignored (`Compilation.cs:284`) (round 2 §16).
- Test parameter modifiers (`ref`/`move`) and defaults parse but are ignored; reject them (round 2 §16).
- `[Cases]` with no arguments on a test with parameters yields zero tests silently (round 2 §16).
- A test whose single parameter is a tuple cannot take `[Cases]` (round 2 §16).
- BYN2773 says "1 values"/"1 parameters" for counts of one (plan wording) (round 2 §16).
- BYN5xxx: free functions and generic struct instances are not checked; BYN5102's fix-it renames only the declaration (round 2 §17).
- BYN5003 formats `{1}` from the failing member's return type, so `void x() fails` yields "must return `void?` or `bool`" (round 2 §17).
- `StrBuf.finish()` is a borrowed view like `view`; it does not consume the buffer (round 2 §12).
- The `format()` fix-it, the `{x:debug}` help and `chars.take`/`graphemes` in BYN2201's help name APIs not implemented yet (round 2 §12–§13).
- No `chars` iterator exists, so `chars.count` was not added (round 2 §12).
- String patterns (`"/api/" + var rest`) are not implemented, so "stay legal" under BYN2209 is untested (round 2 §12).
- BYN2209 on `a + b + c` rewrites one pair per fix pass (round 2 §12).
- diag/renamed-member also shows a BYN2311 warning (round 2 §11).
- 007 Q121/Q124 give BYN1050 to `=>` in a function type; round 3 uses BYN1050 for `RequiresValue` and BYN1055 for that form (C26). Fix on feat/007-lambdas before merge (round 3, Sūq brief).
- 007 Q115 makes a stored function type such as `onPaid: Order -> void uses net` BYN2822; Sūq and SPEC `:1287` use one (round 3, Sūq brief).
- HANDOFF round 3 §11 (~`:204-210`) still says `on .field` and §13 (~`:259`) has `Order => void`; superseded by their resolved paragraphs, not rewritten.
- No `docs/diagnostics/BYN1056.md`, `BYN1057.md`, `BYN7005.md` pages yet; GRAMMAR §3 links the first two (round 3, Sūq brief).
- SPEC has no `T.zero` (friction F-31); the F-30 user-cleanup member name is open, conservative `dispose(ref this)` (round 3, Sūq brief).
- SPEC `:389` `return .forbidden unless …` snippet has no signature; borderline under A2 (`fail` vs response value).
- J54 type-first revert: 007 spec on feat/007-lambdas is name-first throughout (R1/R4/R15, examples :4-5, :36, :44-78); update to type-first decls and parenthesized function-type params before 007 T1. 006 C4/C20 shrink (006 already type-first).
- J54: Sūq checker `type-first binding` rule only catches capitalised types (`i32 n = 3` unflagged); parameter lists spanning lines are only partly handled.
- `if p == P { x: 1 } { … }` (unwrapped struct literal in a bare condition) gives BYN1012/BYN1005 with no hint to add parentheses (round 2 §4).
- `ref this` on a constructor falls through to the `ref` modifier with type `this` instead of a targeted error (round 2 §9).
- Postfix `break if (x);` parentheses are not reported as an input form; BYN1030 omits `sbyte`, `ushort`, `nint`, `nuint`; `RecoverIfNeeded` can resync into follow-on errors after an input-form error; the dangling-`else` case of BYN1017 is unchecked (round 2 §15).
- The binder keeps `c.Binding` / `IsCatch` paths for `catch (e)` that only erroneous input reaches now (round 2 §15).
- No golden covers the BYN1237 `NoTargetType` help (round 2 §15).
- `tests/cases/run/condition-cast.byn` line 10 prints "parenthesized if" but is now `if flag { … }`; only the cast lines cover paren-vs-cast (round 2 §15).
- Scaffold templates and `tests/cases/new/*.tree` still use `assert(...)`; move to `assert x;` (round 2 §16).
- Power-assert: string values print unquoted; column math ignores tabs and wide characters; guarded pieces emit one `if` each, so C size grows with the square of the lazy-column count (round 2 §16).
- Spec §20 examples and "assertion failed" wording in older docs and plans are not migrated to power-assert (round 2 §16).
- `tryFirst` has a BYN2230 row in the grammar table but no `ReportRenamed` mapping; the BYN2230 help for `asString()` says "rename it `finish`" without `()` (round 2 §11).
- `InterpolationSource` (`BinderExpressions.cs:865`) may cut `"""` raw strings wrong, giving a broken BYN2209 fix; BYN2201 help rows were misaligned (r2 stdlib gate; recheck).
- r2-finalfix: `unless (!x) print("f"); else print("g");` leaves neither BYN1007 nor BYN1018 with a fix (BYN1006 already forces a rewrite).
- r2-finalfix: `if ((!x)) { … }` gets no BYN1032; `ParseIf` (`Parser.cs:1302`) tests the condition without `Unwrap`.
- `Parser.cs` `ParseDefer` (~:1585) still passes `clean = ErrorCount == errors` to ReportBraceless while ParseBody uses "no inner diagnostic has a fix": `defer let x = <expr with a fixless error>;` loses its braces fix (review of fix/review-2026-10-22-1)
- `unless (a == null) s;` on a `bool` `a` reports BYN1017/1018/1006 but no BYN1023 (review of fix/review-2026-10-22-1, probe p12)
- A field witnessing a getter declared with `fails` may get a vtable thunk that ignores the `fails` return convention (`CEmitter.cs` Vtable). Unprobed; flagged by the 006-fixa writer.
- Region analysis runs on generic instances only, so a never-instantiated generic struct's methods get no BYN3004 (audit 2026-10-23 probe p7); instantiated ones match the non-generic case.
- BYN2202 on a generic method's type parameter reads "expected `U`, found `U`" (two distinct `U`s print alike).
- Reassigning an owner (`xs = List<i64>()`, `this.items = …` inside `for x in items`) while a span or loop borrows it records no mutation; memory-safe only because lists live in arenas and reassignment frees nothing (probes p4b/p8/p9 of the 006 final review, 2026-10-23). Becomes a hole if reassignment ever frees.
- `RegionAnalysis.DirectChanges` (used by `ChangedFields`) does not count a `dyn ref` local that borrows a `this` field and is passed on to a writable `dyn ref` parameter (006-fixj note).
- A `dyn ref` assigned from anything but a ToDynRef conversion, a tracked local or a `for` variable is assumed to hold borrowable storage, so passing it to a writable `dyn ref` parameter can give a false BYN3004 (006-fixj note).
- `docs/bayan-handoff.md` Questions: numbers repeat (two 51–56 runs, lines ~463 and ~533); renumber or switch to ids.
- `bayan format <dir>` recurses into everything (`tmp/`, `out/`, hidden dirs, and `tests/cases` fixtures that use input forms on purpose); decide on exclusions (review of 6c25f34).
- SPEC §18 writes pointers after the type (`Sqlite3* db`); whether they go first is open (docs/bayan-handoff.md Question 120).
- r3-51a: the BYN1053 message says "one line" even when the comma-separated cases span several lines.
- r3-51a: a line starting with `?`, `:` or `|` is not waṣl, so a ternary or pattern split before its operator ends the statement.
- r3-51a: a labeled statement as the first line of a `catch { ... }` brace body is ambiguous with a struct-literal field.
- r3-51a: the C-style `for (init; cond; step)` header keeps its `;`; decide whether it is an input form too.
- r3-51a: `if cond` with `{` on the next line reads the brace as a stranded postfix instead of reporting the brace placement.
- r3-51a: two declarations on one line after `}` (`int f() { return 0 } void g() {`) give no BYN1005.
- r3-51a: `return` then a call at the same indent on the next line is a bare return plus an unreachable statement (BYN2405), not BYN1042; by design, check the docs agree.
- r3-51a: tests/cases/diag and parse `input-forms-one-line` no longer fit on one line after expand-blocks; rename or reshape.
- Waṣl clause lines `uses`, `expects`, `except`, `becomes` (J61): no parser support yet; add a continuation test with each clause's parser step (5.1b–5.3). From r3-51a gate.
- r3-suq-paper: 16 `suq/fail/` snippets have no allocated code (listed "none allocated" in `suq/fail/README.md`: attr-case, byte-count, dangling-blank, drop-ref-this, extern-string, in-convert, keyword-name, leading-operator, log-at, mode-particle, on-field, owned-copied, try-catch, try-literal, unit-round, using-let); allocate or map them.
- BYN2226 (`using` not disposable) and its doc stay until the step that drops `using` per C46; then retire the code and rewrite `docs/diagnostics/BYN2226.md`.
- r3-51b1: the BYN1043 local fix picks `let`/`var` by a syntactic scan, so a local changed only through a mutating method gets `let` and then BYN2104; move the choice to the binder (handoff Q122).
- r3-51b1: C# test strings still holding type-first locals are found only when a test fails; a sweep of `tests/Bayan.Tests/*.cs` program strings for `T x = e` would catch any that expect errors.
- r3-51b1: `IsWrittenLater` scans to the enclosing `}`, so a top-level script local is scanned to end of file and a same-named local in a later function counts as a write.
- r3-51b1 gate: plan §1 (c) BYN1043 helps ("declarations put the type first", "local bindings put the name first") do not start with a verb, so the code prefixes one to pass HelpLanguageTests; reconcile plan wording or the verb rule.
- r3-51b2a: messages and docs still say `error enum`/`unique struct`: BYN2608, BYN2609, the BYN2804 "an error enum" kind, the `[ExitCode]` help in `Compilation.cs`, and `TypeFacts.WhyUnique` ("declared `unique struct`"); the pages under `docs/diagnostics/` that show `error enum` examples.
- r3-51b2a: `ErrorEnumDecl`/`ErrorSetDecl` were kept (AST dump `ErrorEnum`/`ErrorSet`) instead of the plan's single `ErrorUnionDecl`; merge them if the binder needs one node.
- r3-51b2a: `except` narrowing of a `union error` is rejected with BYN2022 until function error sets carry excluded cases (handoff Q124).
- r3-51b2a gate: BYN3202 in a nested composition is reported once per composed alias that flattens it (CheckComposition runs per alias); report once at the innermost head.
- r3-51b2a gate: `union error Name<T>` type parameters do not parse; needed for HANDOFF §11 `ValidationError<T>`.
- r3-51b2b: test parameters already parsed type-first with `[Cases]` binding (round 2); 5.1b added no code for them beyond the BYN1043 input form from part 1.
- r3-51b2b: J46 "no `uses`" rule for computed properties (expression or block body) waits for `uses` (5.1d).
- r3-51b2b: `const x: T = pattern …` (a typed pattern const) parses; no pattern type exists to check `T` against.
- r3-51b2b gate: `suq/orders/order.byn:32` and `suq/storefront/catalog.byn:29` write `public const x = pattern …`, which is BYN1001 (`public` then `const`); fix the paper app or the grammar.
- r3-51b2b gate: `ref this is .s` after other parameters (already BYN2117) leaves `is .s` unconsumed and adds a second parse error.
- r3-51c: BYN0007/0008/0009 messages, `TokenKind.CharLiteral` and `PrimitiveKind.Char` still say char; rename in the C# and selfhost lexers together (handoff Q133).
- r3-51c: `unsafe because` (J6) waits for an `unsafe` block statement; none parses today.
- r3-51c: SPEC §7.9 "Not in v0.1: `requires` on fields and distinct types (BYN2322)" disagrees with the plan's v0.1 yes; reconcile in 5.3b, which also deletes `Compilation.CheckRequires`.
- r3-51c: BYN2322's help still suggests a function's `requires`, which becomes `expects` (BYN1063, 5.1e).
- r3-51c: C17 lists `rune` as a keyword; 5.1c keeps it a type name like `bool`.
- r3-51c: `suq/fail/{bracket-attr,block-comment,char,requires-value}.byn` keep their input forms on purpose (compile-fail corpus).
- `tests/cases/diag/input-negated-if.byn` and `input-paren-negation.byn` keep `input-` names though BYN1032 is retired (5.1c); rename or move to a non-diag case.
