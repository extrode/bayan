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
- Suite flake: the first `dotnet test` after landing the review fixes failed 10 tests in 1 m 20 s; four later runs (one with a cold `BAYAN_CACHE`) passed in 35–55 s; failure output not captured (spec 001)
