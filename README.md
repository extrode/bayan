# Bayān

*Bayān (ASCII: Bayan). The CLI, package names, URLs and identifiers use `bayan`.*

Bayan (بيان) is a value-oriented, natively compiled language derived from C#: no runtime, no GC, no exceptions. This repo holds the v0.1 bootstrap compiler (C# / .NET 10, NativeAOT) that compiles Bayan to C and then to a native executable via `zig cc`.

- Language spec: [`docs/bayan-spec.md`](docs/bayan-spec.md)
- Naming and style guides: [`docs/bayan-grammar.md`](docs/bayan-grammar.md), [`docs/bayan-style.md`](docs/bayan-style.md)
- v0.1 plan, milestones, progress log: [`docs/bayan-handoff.md`](docs/bayan-handoff.md)
- Lessons from other languages: [`docs/design/lessons-from-other-languages.md`](docs/design/lessons-from-other-languages.md); where each lands: [`docs/plans/2026-11-05-000-lessons-index.md`](docs/plans/2026-11-05-000-lessons-index.md)
- Home: <https://bayan.run>. Repository: <https://github.com/ikhbat-org/bayan> (Ikhbat Foundation).

## Build and test

Requires the .NET 10 SDK (and `zig` on `PATH` from milestone M4 on).

```sh
dotnet test                                    # unit + golden tests
BAYAN_UPDATE=1 dotnet test                     # rewrite golden expected files
dotnet publish src/Bayan.Cli -c Release -r osx-arm64 -o tmp/publish
./tmp/publish/bayan --version
```

Local builds are kept in `dist/` (gitignored), one directory per target and version, plus a `latest/` per
target that every build replaces:

```sh
scripts/dist.sh                                # this host: dist/macos-arm64/0.2.0-dev+<commit>/bayan
scripts/dist.sh --rid osx-x64                  # another arch of the same OS
scripts/dist.sh --from <publish-dir> --rid win-x64 --commit <sha>   # import a binary published on another OS
dist/macos-arm64/latest/bayan --version
```

NativeAOT does not cross-compile between operating systems, so Linux and Windows binaries are published on
those systems (or in a container) and imported with `--from`. Each version directory holds a `BUILD-INFO`
(version, target, commit, build time, host, source).

## Layout

| Path | Contents |
|---|---|
| `src/Bayan.Cli` | `bayan` executable |
| `src/Bayan.Syntax` | source text, lexer, parser, AST, diagnostics |
| `src/Bayan.Semantics` | binder, symbols, types, checks |
| `src/Bayan.Lowering` | typed AST → lowered tree |
| `src/Bayan.Backend.C` | C emitter, toolchain invocation |
| `tests/Bayan.Tests` | unit tests and the golden-file harness |
| `tests/cases` | golden cases (see its README) |
