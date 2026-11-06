# HANDOFF addendum 01

Merge this into `HANDOFF.md` (answers go under *Questions*, milestones after M8) and apply the spec changes in §3. Same ground rules as the original handoff.

---

## 1. Answers to open questions

### Q34 — read-only `string.bytes`

**Decision: add `ReadOnlySpan<T>`, with C#'s semantics.**

- `Span<T>` is a mutable view; `ReadOnlySpan<T>` is a read-only view. `Span<T>` → `ReadOnlySpan<T>` is implicit. `List<T>` → `ReadOnlySpan<T>` is implicit as a function argument, like `List<T>` → `Span<T>`.
- `string.bytes` returns `ReadOnlySpan<u8>`. A `string` itself converts implicitly to `ReadOnlySpan<u8>` as an argument.
- Standard-library functions that only read take `ReadOnlySpan<T>`.
- New lint **BYN2310**: a `Span<T>` parameter that is never written to → "use `ReadOnlySpan<T>`" with a fix-it.

Why not make span parameters read-only by default: it would give `Span<T>` a different meaning from C#, breaking the rule that zero-allocation C# compiles unchanged (that code uses `ReadOnlySpan<T>` heavily). The cautious `.copy(to:)` requirement goes away for read-only callers.

### Float formatting boundary

**Decision: match .NET's thresholds, with Bayan's exponent spelling.**

- Digits: always the **shortest string that round-trips** to the same `f64`/`f32` (Ryu-style).
- Fixed notation when `1e-5 ≤ |x| < 1e15`; exponential outside that range (the same boundaries as .NET Core 3.0+'s default `ToString()`).
- Exponent spelling: lowercase, no `+`, no leading zeros: `1e15`, `1.5e-7`, `-2.25e20`. The output is always a valid Bayan literal and valid JSON.
- Special values: `nan`, `inf`, `-inf` in `$"{x}"`; `Json.encode` rejects them (`JsonError.nonFinite`).
- `-0.0` prints `-0`. Integral values print without a decimal point (`42`, not `42.0`), matching .NET.
- Fixed-format control (`{x:F2}`) remains deferred to the format-specifier design.

Add golden cases at the boundaries: `999999999999999.9`, `1e15`, `0.00001`, `0.0000099`, `0.1 + 0.2`, `-0.0`, `nan`.

---

## 2. New milestones

### M9 — `bayan new` and `bayan init`

```
bayan new <name>             # app (default)
bayan new <name> --lib       # library
bayan new <name> --script    # single-file script
bayan init [--lib|--script]  # in the current directory (refuses if package.byn exists)
  --vcs git|none             # default: git, when git is installed and not already inside a repository
  --ci github|none           # default: none
```

**Generated app layout:**

```
tally/
  package.byn                 manifest (data only)
  README.md                   name, one-line description, how to run and test
  .gitignore
  .gitattributes
  .editorconfig
  src/
    app/
      main.byn                print("Hello, world!");
  tests/
    app.test.byn              one passing test calling into the app module
  testdata/                   (empty, with .gitkeep)
  docs/                       (empty, with .gitkeep)
  out/                        not created until the first build; ignored
  dist/                       not created until `bayan dist`; ignored
  gen/                        not created until a generator runs; ignored
```

**Library layout** replaces `src/app/main.byn` with `src/<name>/<name>.byn` (one public function and a doc comment) and `tests/<name>.test.byn`. **Script layout** is a single `<name>.byn` with a shebang, `#:io threaded`, and nothing else (no manifest, no git).

**Generated `package.byn` (app):**

```csharp
Package {
    name    = "local/tally",      // `local/` until the user publishes under a verified namespace
    version = "0.1.0",
    edition = 2027,
    kind    = .app,
    capabilities = [],
    deps    = [],
    dist = Dist {
        targets = [.host],        // expands to the machine's own target; see M11
        mode    = .safe,
    },
}
```

**`.gitignore`:**
```
/out/
/dist/
/gen/
```
Lockfile policy (name superseded 2026-11-06: the lockfile is `package.lock.byn`, `docs/bayan-design-round-4-inventory.md` batch 1): `bayan.lock` is committed for apps and libraries alike (reproducible builds); do not ignore it.

**`.gitattributes`:**
```
* text=auto eol=lf
*.byn linguist-language=C#
```
(The `linguist-language` line gives `.byn` files C# highlighting on GitHub until Bayan has its own grammar there.)

**`.editorconfig`:** UTF-8, LF, 4-space indentation, final newline, trim trailing whitespace.

**`--ci github`** adds `.github/workflows/bayan.yml` running `bayan build`, `bayan test`, and (once they exist) `bayan fmt --check` and `bayan fmt --check-prose`.

**Git behavior:** run `git init` only if `--vcs git` (the default), git is on `PATH`, and the target isn't already inside a work tree (`git rev-parse --is-inside-work-tree`). **Never commit, branch or configure anything.** If git is missing, print a note and continue.

**Acceptance:**
- Golden cases under `tests/cases/new/`: the full file tree plus every file's contents for app, lib and script, snapshot-tested.
- A freshly generated app passes `bayan run` and `bayan test` with no edits.
- `bayan init` in a non-empty directory adds missing files only and lists what it skipped.
- Names are validated (camelCase-or-kebab directory names allowed, package names lowercase with `-`), with teaching diagnostics.

### M10 — Build outputs: `out/` and `dist/`

Two different directories with different jobs:

| Directory | Contents | Lifetime |
|---|---|---|
| `out/` | Everything `bayan build`, `run` and `test` produce: emitted C, objects, binaries, test reports | Disposable; per worktree; ignored |
| `dist/` | Versioned release artifacts from `bayan dist` | Kept; immutable per version; ignored by git by default |

**`out/` layout:**
```
out/
  <mode>/<target>/            e.g. out/debug/linux-x64/
    c/                        emitted C (kept for inspection)
    obj/
    tally                     the binary
  test/
    results.json              machine-readable test results
```

**`dist/` layout:**
```
dist/
  0.1.0/
    linux-x64/tally
    linux-arm64/tally
    macos-arm64/tally
    windows-x64/tally.exe
    SHA256SUMS
    BUILDINFO.json
  0.2.0/
    …
  latest/                     a copy of the newest version directory
    VERSION                   contains "0.2.0"
    linux-x64/tally
    …
```

Rules:
- **Target names:** `linux-x64`, `linux-arm64`, `macos-x64`, `macos-arm64`, `windows-x64`, `windows-arm64`, `wasm32`. `.host` in `package.byn` resolves to the current machine. `bayan dist --target all` builds every target `zig cc` supports from the current host.
- The version comes from `package.byn`. **A version directory is immutable:** `bayan dist` refuses to overwrite an existing `dist/<version>/` unless `--force`, and says to bump the version instead.
- `latest/` is a **copy, not a symlink** (symlinks need privileges on Windows and break in archives). It is rebuilt atomically: write to `dist/.latest.tmp/`, then rename. "Newest" means the highest semantic version present, not the most recent build.
- `SHA256SUMS` lists every artifact in the version directory.
- `BUILDINFO.json`: package name and version, compiler version, mode, targets, source commit hash and whether the tree was dirty (if in a git repository), and a hash of the inputs. This is the seed of the isnād provenance chain.
- `bayan dist` refuses to run from a **dirty git tree** unless `--allow-dirty` (recorded in `BUILDINFO.json`).
- Libraries: `dist/<version>/<target>/` contains the static and shared libraries plus the generated C header, once FFI export exists; until then, `bayan dist` on a library reports that library distribution arrives later.
- Scripts have no `dist`.
- `bayan clean` removes `out/`; `--dist` also removes `dist/`; `--cache` clears the global cache (M11). Nothing is removed without one of these commands.

**Acceptance:** golden tests for both layouts (tree listings, `VERSION`, `SHA256SUMS` format, `BUILDINFO.json` schema); immutability and `latest` selection tested with multiple versions; cross-compiling at least `linux-x64`, `macos-arm64` and `windows-x64` from one host in CI.

### M11 — Global cache and worktrees

- **Location:** `$BAYAN_CACHE` if set; otherwise `$XDG_CACHE_HOME/bayan` or `~/.cache/bayan` (Linux), `~/Library/Caches/bayan` (macOS), `%LOCALAPPDATA%\bayan\cache` (Windows).
- **Content-addressed:** keys are hashes of (compiler version, mode, target, flags, input content). In v0.1 the unit is the emitted C file and object per module; with DIR it becomes per function.
- **Safe under concurrency:** several worktrees and several agents may build at once. Write to a temporary file and rename atomically; never lock the whole cache; a corrupted entry is detected by its hash and rebuilt.
- **No absolute paths in any output.** `#line` directives and debug info use paths relative to the package root, so cache entries are valid in every worktree and every checkout location.
- `out/` is always local to the worktree; only the cache is shared.
- **Script caching:** `bayan run script.byn` caches the compiled binary under the cache key (source, directives, compiler version); a cache hit starts the binary directly.
- `bayan cache stats` and `bayan clean --cache`.

**Acceptance:** two worktrees of the same repository building the same commit — the second build compiles nothing (asserted from cache statistics); parallel builds of the same package in two directories never corrupt the cache (stress test); moving a project directory does not invalidate its cache hits.

### M12 — CLI completeness and machine-readable output

Things the original handoff implied but didn't list:

- **`--diagnostics json`** on `build`, `run`, `test`: one JSON object per diagnostic (code, severity, file, span, message, help, fixes). Coding agents and editors consume this instead of parsing text.
- **`bayan test --json`** writes results to stdout in the same format as `out/test/results.json`.
- **Exit codes:** `0` success · `1` compile errors · `2` usage errors · `3` test failures · `4` toolchain problems (missing `zig`, unwritable cache). Document them in `bayan --help`.
- **Color:** on for terminals; off when piped, when `NO_COLOR` is set, or with `--color never`.
- **`bayan doctor`:** reports compiler version, `zig` path and version, git availability, cache location and writability, and the host target. Every failure has a fix suggestion.
- **`bayan test --changed`** (simple version): uses `git diff --name-only` against `HEAD` (or `--base <ref>`) and runs tests in modules whose files changed. Read-only git use only.
- **`bayan --version`** prints the compiler version and the commit it was built from.

**Acceptance:** golden tests for JSON diagnostics and test output; exit codes asserted in `run/`-style CLI tests; `bayan doctor` tested with a missing `zig` (via `BAYAN_CC` pointing nowhere).

---

## 3. Spec changes to apply

Edit `SPEC.md`:

- **§3.5** — add `ReadOnlySpan<T>`; `Span<T>` → `ReadOnlySpan<T>` implicit; `string.bytes : ReadOnlySpan<u8>`.
- **§6.2** — add the `ReadOnlySpan<T>` conversions (implicit from `Span<T>`, `string` and, as an argument, `List<T>`).
- **§6.6** — add float formatting (shortest round trip; fixed for `1e-5 ≤ |x| < 1e15`; lowercase exponent without `+`; `nan`, `inf`, `-0`).
- **§16** — read-only queries and views take `ReadOnlySpan<T>`.
- **New §21.4 "Projects, outputs and VCS"** — summarize M9–M12: `new`/`init` defaults, layouts, `out/` versus `dist/`, immutable versions, `latest/` as a copy, `BUILDINFO.json`, the global content-addressed cache, worktree rules, and read-only git use (Bayan never writes git history).
- **§21.2** — add `new`, `init`, `dist`, `clean`, `cache`, `doctor` to the command table, plus `--diagnostics json`, `--json`, and exit codes.

## 4. Order of work

M12's JSON diagnostics and exit codes first (small, and they make every later milestone easier to test with agents), then M9 → M10 → M11, then the rest of M12.
