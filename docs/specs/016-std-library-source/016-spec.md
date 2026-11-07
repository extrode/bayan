# 016 — Standard-library source: `import std.x`
Status: done
Created: 2026-11-06

## Problem
Spec 015's later ports need `std.json` (S7), `std.regex` (S3) and glob (S5), approved as Bayān library modules. The
compiler has no way to load one: `import a.b` resolves only to `<package root>/a/b/` (`src/Bayan.Semantics/Compilation.cs`
`ResolveImport`), and every library function today is a builtin written three times (C#, the C runtime, the selfhost
port; spec 014's `Path`). Writing JSON or regex that way would triple the work and the review for code that needs no
OS. Until a program can `import std.json` and get Bayān source shipped with the toolchain, ports 5–10 stay blocked
(user, 2026-11-06: spec the mechanism).

## Goals
- A program anywhere (script, file, project) writes `import std.<name>` and the toolchain supplies the module, with no
  file of the user's and no package dependency.
- The C# compiler and the selfhost compiler resolve it the same way and give the same diagnostics (L027/L028 hold).
- A std module is ordinary Bayān: written, tested, formatted and covered like `selfhost/`.

## Non-goals
- `kit` and versioned packages (library tiers §2: versioned modules come from `kit`, never `std`).
- Moving today's builtins (`Io`, `Path`, `Env`, `Fs`, `Utf8`, …) into std modules.
- The modules themselves (`std.json`, `std.regex`, glob): each is its own task under 015, after this lands.
- A package registry, `deps:` resolution, or script-mode auto-import (proposed, not decided, tiers §2).

## User stories
- As a tool author, I want `import std.json` in `tools/quality-baseline.byn`, so that the port needs no JSON parser of its own.
- As a std maintainer, I want std modules in the repo as `.byn` files with tests beside them, so that a change is reviewed and tested like any Bayān code.

## Requirements
- R1 `import std.<name>` (and `import std.<a>.<b>`) resolves to the std module of that name from any program kind: a
  `#!` script, a single file, a directory, a project.
- R2 The std source a build uses is the one shipped with that compiler build; a program cannot replace or shadow it, and
  a package's own `std/` directory is not consulted for `std.` imports.
- R3 An unknown std module is a diagnostic naming the module and listing the std modules that exist.
- R4 A std module may import other std modules; cycles are diagnosed as for package modules.
- R5 A std module's public declarations follow the same visibility, effect and capability rules as package modules; a
  std function's effects count against the importing package's `capabilities` (§14).
- R6 Diagnostics inside std source point at a stable display path (e.g. `<std>/json/reader.byn:12:5`), not at a
  machine-specific install path.
- R7 The selfhost compiler implements R1–R6 with the C# behavior as its oracle; the driver equivalence (L028) covers a
  program importing a std module.
- R8 Std source is checked by the repo's gates like `selfhost/`: `bayan format --check`, tests, coverage.

## Acceptance criteria
- Given `tmp/a.byn` starting with `#!` and `import std.probe` (a test-only std module), when `bayan run tmp/a.byn`, then
  it runs and prints what `std.probe` returns, with the C# and the selfhost compiler alike.
- Given a project with its own `std/probe/` directory, when it imports `std.probe`, then the shipped module is used.
- Given `import std.nope`, then the R3 diagnostic, with a `diag/` case.

## Law candidates
- For every program in the run/ and diag/ corpus that imports a std module, the C# and selfhost compilers produce the
  same output (an extension of L028's corpus, not a new law).

## Decisions (user, 2026-11-06)
- Q1 (a) Embedded: std source is compiled into the `bayan` binary at build time, as `selfhost/driver/runtime.byn` embeds
  the C runtime; nothing is looked up at run time, so the pinned `.cache/bayan/<sha>/bin/bayan` stays one file.
- Q2 The source lives in `lib/std/<module>/*.byn` (a new root code directory; `lib/kit/` later).
- Q3 No `extern`: std source calls builtins and other std modules only; revisited when a module needs a new OS call.
- Q4 The first module after the mechanism is the `std.json` reader (S7), for ports 6 and 8.

## Open questions
- None.

## Decision for T4 (user, 2026-11-06; members revised 2026-11-07, 016-tasks.md T4): `std.json` members, tree and encode first
The first sketch (struct `JsonValue` with `asBool`/`get`/`at` and `Json.object` builders) failed BYN1064/BYN2009/BYN2608/BYN2022; the public members as implemented:
```
public union JsonValue { nothing, flag(bool value), number(f64 value, string text), text(string value), list(List<JsonValue> items), object(List<JsonMember> members) }
public struct JsonMember { string key; JsonValue value }        // an object keeps its members in text order, duplicates included
public union error JsonError { syntax(string message, i64 offset, i32 line, i32 column), tooDeep(i64 offset, i32 line, i32 column), nonFinite, nestedTooDeep }
Untrusted<JsonValue> Json.parse(string text) fails JsonError   // static members of `public struct Json`
string Json.encode(JsonValue v, i32 indent = 0) fails JsonError // indent 2 = Python json.dumps(indent=2); NaN/±inf: nonFinite; past 128 levels: nestedTooDeep
JsonReader JsonReader.over(string text)                         // the public pull reader
JsonToken? JsonReader.next() fails JsonError
public union JsonToken { startObject, endObject, startList, endList, key(string name), text(string value), number(f64 value, string text), flag(bool value), nothing }
```
No key lookup member: deferred until borrows or union methods exist (a lookup on a unique tree would deep-copy on every call); its future name follows tryX (`tryGet`); lookup-heavy code uses `JsonReader` or matches `.object(let members)`.
