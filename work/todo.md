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
- Terminal style (`docs/bayan-terminal-style.md`) is not implemented: `-->` location line, column-0 `help:` with `+++` fix preview, `◆`/`✗` summaries with `--ascii`, 20-error cap, `FORCE_COLOR`/`TERM=dumb`/`BAYAN_THEME`, progress line, power-assert values in `bayan test`; renderer changes must move `selfhost/lexer/diagnostics.byn` in the same commit (L001)
- Flow analysis: `x ??= return 3;` followed by code gives a false BYN2405 "unreachable code" warning (`x = x ?? return 3;` does not); found in the spec 002 loop-3 review
- L005 proof never runs a real `cmd.exe`: the read-back model in `tests/Bayan.Tests/L005RunBatchTests.cs` encodes cmd rules by hand, so a quoted character cmd.exe or code page 65001 reads differently would pass; needs a Windows run of the generated cases through `cmd.exe` and a `%~1`/`shift` batch file (review of feat/laws-005-007)
- `map[k].clear()` and `map[k].add(x)` report BYN2106: a `map[k]` receiver is a write target only for `take()` (spec 003 T1); other mutating builtins on a map element need the same `MapWriteTarget` treatment
- Unverified (spec 003 review, finding F): `StrBuf.take()` refills the place using the current `byn_context` allocator, and RegionAnalysis does not see the `StrBufGrow` the lowering introduces; check whether a `take()` on a `ref StrBuf` from an outer region allocates the refill in the wrong region
- `_ = move a;` reports BYN2001, while BYN1012's help (for a bare `move a;`) suggests `_ = f();`; either accept a discarded move or change the help text
- Spec 003 review loop 3: `CEmitter` skips the after-call claim of a root moved anywhere in the statement, even on a branch that does not run (`let n = flag ? consume(move b) : b.grow();`): storage grown on the other branch stays unclaimed; no failing program found
- Spec 004 R4: a discarded first pass now drops the generic function instances it created, but not generic struct/union instances (`InstantiateType`), which can stay as unused `i32` instantiations
- Spec 004 R4: a value derived from a pending `var` through a call (`n = id(n);`, generic `T` inferred from `n`) counts as a typed `i32` use and fixes `n` at `i32`
- Spec 004 R4: an optional integer target (`i64?`) does not count as a constraining use, so a later `u8` use can still retype the local
- `SelfhostLexerTests` (`StdinRuns`, sequential `bayan-lex` processes) did not finish within 10 minutes while several worktrees ran `dotnet test` at once (spec 004 T9); the rest of the suite took about 75 s, and running `bayan-lex --json` by hand on `tests/cases/lex/*.byn` exits right away
- An untyped array literal used directly as a `for` source (`for (let w in ["a", "b"])`) gives BYN2218 "needs a known type here"; infer the element type (found in 004 T15).
- Spec §21.2 CLI table lists neither `bayan check`, `lex` and `parse` nor the new `types` (spec 004 T16)
- R4 literal inference (spec 004 review): retyping a `var` at its first constraining use can break a later use that only compiled by widening (`var n = 0; i64 big = n; takeI32(n);` → BYN2202) and silently changes overflow behaviour (`var n = 2147483647; i64 big = n; n += 1;` no longer panics). Consider a lint or requiring all constraining uses to agree.
- R6 receiver borrows (spec 004 review): reassigning the whole receiver (`b = Buf{…}`) while a span from `b.view()` is alive is not BYN3004 (memory-safe, same as the direct `b.data.asSpan()` form).
- `List.removeRange` on a list of unique elements does not free the removed elements (same as `clear`; spec 004 review).
- R4 pending-var groups resolve by statement order (`var a = 0; var b = 0; i64 x = a; u8 y = b; a = b;` is BYN2202, swapped declarations compile); a widest-fitting choice would remove the order dependence (review loop 2, 004).
- `LexerTests.OneMegabyteAllocatesOnlyTheTokenArray` fails when other test suites run concurrently on the machine (per-thread allocation count); passes alone. Seen 4 times during the 004/005 runs.
- R4 pending-var groups with mixed uses: `var a = 0; var b = 0; a = b; f64 y = a; u8 x = b;` is BYN2202 (group falls back to i32). Needs a rule that picks a type converting to every member's use (u8 here); neither "first integer constraint" (breaks i64+f64) nor "fall back to i32" (breaks u8) works. Dev rejects it too (review loop 3, 004).
