You are doing task T1 of spec 012 (the Bayan compiler driver ported from C# `src/Bayan.Cli/` (`CliApp`) into Bayan under `selfhost/driver/`, compiled with package root `selfhost/` (stage 1 of the bootstrap)). Other writers port the sibling files in parallel; every cross-file function already exists as a compiling stub with its final signature, so you call real names and never write stand-ins. A separate reviewer compares your port with the C# line by line before it lands; invented logic, sibling fallbacks and hand-typed test expectations fail that review.

## Writer protocol (binding; the dispatcher runs the same guard after you, so skipping a step only costs you a resume)

Turns are your scarce resource: this run has 120, and you will be resumed with the guard's report if you run out. Put several Read/Grep/Bash calls in ONE turn whenever they do not depend on each other.

1. **Read only what the brief lists** (turns 1–6). The brief gives you the exact C# line ranges, the signatures you may call, and the files you own. Do not grep the spec, tests/cases or other modules to learn the language: the cheat sheet below is compiler-checked, and when unsure, **probe**: write the snippet to `tmp/probe/<name>/main.byn` and run `src/Bayan.Cli/bin/Debug/net10.0/bayan check tmp/probe/<name>/main.byn` (0.3 s).
2. **Skeleton by turn 15, and it must compile.** Every function the brief assigns to you, with its exact signature and a stub body (`return <default>` / `.none`), plus the test file with one real test. Run the fast check (below). Fix until green before porting any body.
3. **Compile early, compile often.** Port one function or ~80 lines at a time, then run the fast check. Never write more than ~150 lines between two checks. Use Edit for slices, not a whole-file Write.
4. **Tests are part of the task, and the C# is the oracle.** Every ported function gets a test whose expected lines are *computed* by running the C# implementation in-process on the same input (`Compilation.Create`, `new Binder(…)`, or the C# class directly; model: `SelfhostDriverHarness`), including a failure/diagnostic path; diagnostics compare as `code line:col message`. Typing the expected lines by hand is not a test: it only proves the code does what the code does, and the guard rejects it (at most 2 exemptions per file, each `// no-oracle: Qnnn` with the Question saying why no C# path exists). When a sibling stub stands in the way, pick an input that does not reach it, or build the sibling's output by hand on both sides; never assert a stub's default. Then break-test: change one line the test covers, run it and see it FAIL, restore by copying back a copy you took first (`cp f tmp/f.orig` … `cp tmp/f.orig f`; never `git checkout`, which reverts to the stub and loses your work), run it and see it PASS. Record each break-test (file:line `code as it stands`, change, failing test) in your Question; cite lines as they are after your last edit.
4a. **Port, do not invent.** Method for method, in C# order, every branch. Logic that is not in the C# is a bug even if it makes a test pass. In particular, never write a stand-in for a sibling: call the sibling's entry point even while it is a stub, and accept that its default flows through. No fallback for a sibling's `-1`/`false`/empty result, no private re-implementation of a sibling's function, no fake handles, no skipping a sibling call.
4b. **State has a home.** When the C# keeps a field (`_instantiating`, `_caught`, `MutationChecks`, …) that `Ctx` or `Binder` lacks, add it to your own `// ── <task> state` section of `selfhost/driver/dctx.byn`, with a default value; touch nothing else in those files (the guard checks). Never approximate a field (a scope depth for a scope, a path prefix for a list, an index for an identity).
4c. **Blocked is a state, not a workaround.** If a Question in the handoff says it must be decided before your task and is still Open, stop and report it. If an entry point truly cannot be ported yet, leave its stub, mark it `// stub: <task> … (blocked: Qnnn)`, and write the Question. Delete the `// stub:` marker of every function you port.
4d. **Docs say what the code does.** Each Question describes the code as it stands, not as planned; add one Progress log entry for your task (what landed, what is blocked, test count). Do not tick the task: `scripts/selfhost/land.sh` ticks it after an independent review passes.
5. **Before you finish, run the guard** (command in the brief) and paste its last 15 lines into your summary. `GUARD PASS` is the only finished state. The per-task guard runs no mutants (user decision 2026-11-01): mutation runs in batches and survivors become T28 cases, so do not add cases only to kill mutants; do cover every ported branch with a C#-oracle case. For the batch runs, only when a statement's sole observable effect goes through an unported sibling's stub may you mark it `@unobservable because "…"` (spec 013 R7; narrow it to one operator with `@unobservable(boundary) because "…"`); the reason names the sibling and the test to add when it lands. Its mutants are reported `marked`, not survived.
6. **At turn ~110, stop adding.** Make it compile, commit nothing yourself, and end with: done / not done / the exact next step. The resume picks up there.

Standing rule: a C# private helper needed outside its owner task is ported early in the owner's file by a chore with its own test, and listed as pre-ported in the owner's brief; tasks call it and add no copies. Parser span helpers are called as `parser.exprSpan` etc.; write no new `tNNExprSpan` copies. Existing copies (t11/t15/t16/t17ExprSpan, t17Host, t18Host) stay until T28 removes them.

Never: edit files outside the brief's list (record the need as a Question instead); leave output files in the repo root; use tuple destructuring or `.0` fields; run commands in the background; run `git commit/merge/checkout <branch>/reset`.

Fast check: `dotnet build -v q 2>&1 | grep -E " error | warning " | head -20; dotnet test -v q --no-build --filter "FullyQualifiedName~SelfhostDriverT1Tests" 2>&1 | tail -3` (~15 s).




Guard (run before you finish):
```
bash scripts/selfhost/guard.sh --base test-base --allow 'selfhost/driver/main.byn,selfhost/driver/dctx.byn,tests/Bayan.Tests/SelfhostDriverHarness.cs,tests/Bayan.Tests/SelfhostDriverTests.cs,tests/Bayan.Tests/SelfhostDriverApiTests.cs,docs/specs/012-self-hosted-bootstrap/012-api.md,docs/bayan-handoff.md,docs/specs/012-self-hosted-bootstrap/012-tasks.md' --filter 'FullyQualifiedName~SelfhostDriverT1Tests' --task T1 --spec 012
```
Do not commit, merge, checkout, reset or stash; the dispatcher commits for you.

**Order of work:** turns 1–6 read; by turn 15 your file(s) compile with every function stubbed and the test file has one passing test; then port in C# order, ~80 lines per compile; tests for each function as you go; guard; docs. If you reach turn 100 with tests unwritten, stop porting and write tests: the dispatcher resumes you for the rest.

## Task (verbatim, docs/specs/012-self-hosted-bootstrap/012-tasks.md)
- [ ] T1 Skeleton, dispatch, refusals — files: `selfhost/driver/main.byn`, `selfhost/driver/dctx.byn` (its own `// ── T1 state` section), `tests/Bayan.Tests/SelfhostDriverHarness.cs` (build stage 1 once with `Compilation.Create(sources, packageRoot: selfhost/)` as `SelfhostEmitterHarness.cs:28-36`, run as a process), `tests/Bayan.Tests/SelfhostDriverTests.cs` (usage corpus from `CliTests.cs` argument lists; `SourcesAreFormatted` runs `bayan format --check` over `selfhost/driver/`), `tests/Bayan.Tests/SelfhostDriverApiTests.cs`, `docs/specs/012-self-hosted-bootstrap/012-api.md` — C#: `CliApp.cs` 16-35, 37-106, 315-376 — covers: R1, R2, R3, R13, R14 — after: P1 — difficulty: medium → `codegen-lite` — done when: `version` (Q3 masked), `help`, no arguments, an unknown command, `fmt`, every refused command and option (Q4 text) give `CliApp.Run`'s exit code and output; the other commands are stubs returning the R3 refusal until their task lands.

## What you port (plan file table)
| `selfhost/driver/main.byn` | `CliApp.cs` 37-106 (`Run`) | T1 | ~350 |
Port method for method in C# order, keeping C# names lowerCamel. Read the C# ranges named above whole, once. Line ranges marked `~` are approximate: start and stop at the method boundary nearest to them, and say in your Question where you cut.

## Your entry points (exact; already stubbed in your file with `// stub:` comments; replace each body, keep each signature byte for byte)
- T1 `func main() -> Int` (`selfhost/driver/main.byn`)

## Calling siblings
`docs/specs/012-self-hosted-bootstrap/012-api.md` lists every cross-file function with its exact signature and owner. Call those names as written. A sibling still a stub returns a default (`-1`, `errorType()`, empty list), so **test your functions on inputs that do not go through an unported sibling**, or build the sibling's output by hand (arenas and structs are module-visible). Never edit a sibling's file: if you need a function that is not in the index, write it as a private helper in your own file with a `T1`-prefixed name (e.g. `t1FindX`) and record it in a Question so integration (T28) can merge duplicates.
Landed and callable: see `docs/specs/012-self-hosted-bootstrap/012-api.md` and the task's `after:` list; the lexer, parser, semantics and emitter modules under `selfhost/` are public to you as `012-plan.md` D2 says.

## Tests: use the shared harness, do not write another build helper
Not landed yet: `tests/Bayan.Tests/SelfhostDriverHarness.cs` is created by T1, as `012-tasks.md` T1 and `012-plan.md` define it. It builds stage 1 once with `Compilation.Create(sources, packageRoot: selfhost/)` like `SelfhostEmitterHarness.cs:28-36` and runs it as a process. Expected values come from `CliApp.Run`/`CliApp.EmitC` or the C# golden tests. Every ported function gets a compared case including a failure path.

## Files you own
`selfhost/driver/main.byn`, `selfhost/driver/dctx.byn`, `tests/Bayan.Tests/SelfhostDriverHarness.cs`, `tests/Bayan.Tests/SelfhostDriverTests.cs`, `tests/Bayan.Tests/SelfhostDriverApiTests.cs`, `docs/specs/012-self-hosted-bootstrap/012-api.md`, `docs/bayan-handoff.md` (Questions in your range only, inserted in number order), your own `// ── T1 state` sections of `selfhost/driver/dctx.byn` (new fields with defaults only). Do not tick the task: after your guard passes, an independent reviewer checks the port against the C# (`scripts/selfhost/review-brief.py`) and `scripts/selfhost/land.sh` merges and ticks it only on `Verdict: PASS`.

## Bayan cheat sheet (compiler-checked)
# Bayan cheat sheet for porters (compiler-checked 2026-10-26)

**Probe before you write.** Unsure whether something compiles? Put it in `tmp/probe/<name>/main.byn` (one dir per probe; a dir is a module) and run
`<repo>/src/Bayan.Cli/bin/Debug/net10.0/bayan check tmp/probe/<name>/main.byn` (0.3 s). Do this instead of grepping the spec.

## Checked by the compiler (trust these over anything below)
| Form | Result |
|---|---|
| `let (a, b) = t` | BYN1001 — no let-destructuring |
| `t.0` | BYN1001 — no positional tuple fields |
| named tuple `(i64 line, i64 col) f() => (line: 1, col: 2)` then `t.line` | compiles — use this instead of `.0` |
| `switch t { case (let a, let b): … }` | compiles |
| `for (k, v) in map` | compiles |
| `a?.x` | BYN2022 — not supported |
| `a ?? 0` | compiles |
| `a == null` | BYN2027 — write `a is null` / `a is not null` |
| `print("a", "b")` | BYN2202 — print takes one string; use `print($"{a} {b}")` |
| `xs.remove(at: 0)` | compiles |
| function-typed struct field `i64 -> i64 f` | BYN2822 — not supported |
| `i32(a)`, `u8.tryFrom(a) ?? 0` | compile |
| `Map<string,i64> m = …` style local | BYN1043 — write `let m: Map<string, i64> = …` or `var m: … = …` |

## Has (idioms that compile; copy these)

**Struct declarations — type first, newline-separated fields, defaults allowed.**
```byn
struct Parser {
    LexResult lexed
    i64 count
    List<bool> inGroup
    bool inExtern
}
```
`selfhost/parser/parser.byn:27-36`

**Struct literal — `{ field: value }` with trailing comma; nested literals target-typed.**
```byn
return Parser {
    lexed: move lexed,
    count: count,
    pos: 0,
    inExtern: false,
}
```
`selfhost/parser/parser.byn:62-69`

**Union declaration — one camelCase case per line, payloads named and type-first.**
```byn
union Decl {
    importDecl(Owned<ImportDecl> node)
    structDecl(Owned<StructDecl> node)
    none
}
```
`selfhost/parser/nodes.byn:190-210`

**Enum (payload-free union form) — one case per line, no commas.**
```byn
enum ParamModifier {
    none
    refKeyword
    moveKeyword
}
```
`selfhost/parser/nodes.byn:369-374`

**`switch` on a union is exhaustive; payload binds with `case .x(let n)`, `_` binds whole.**
```byn
switch lexed.tokens[i].kind {
    case .openParen or .openBracket or .interpolationStart:
        groups.append(true)
    case .openBrace:
        groups.append(false)
    default:
        continue
}
```
`selfhost/parser/parser.byn:47-57`

**Optional: `T?`, `null` literal, `is null` / `is not null`; bind a field path with `is { }` to narrow.**
```byn
if (d.value.failsKeyword is { } fails) { … fails.errors … }
if failsSpan is null { failsSpan = at }
```
`docs/bayan-handoff.md:750` (Q480), `selfhost/parser/decls.byn:825`

**Optional early exit: `?? return` / `?? fail`.**
```byn
let bytes = Io.readFile(path) catch return unreadable(path)
let order = db.find(id) ?? return .notFound
```
`selfhost/parser/main.byn:48`, `docs/bayan-spec.md:1010`

**List creation and append; `length`; index; `for` binds a borrow.**
```byn
var inGroup = List<bool>()
for i in 0..count {
    inGroup.append(groups.length > 0 && groups[groups.length - 1])
}
```
`selfhost/parser/parser.byn:40-57`

**Map: literal `["k": v]`, empty `[]` target-typed, `contains`, indexing, `tryGet(at:) ??`.**
```byn
let keywords: Map<Symbol, i32> = ["let": 1, "var": 2]
var members: Map<MemberKey, i32> = []
if keywords.contains(name) { members[MemberKey { owner: 1, name: name }] = keywords[name] }
```
`docs/bayan-spec.md:227-232`; read without insert `map.tryGet(at: k) ?? fallback` (`docs/bayan-spec.md:1779`)

**Iterate a map with a tuple pattern (loop pattern, not `let (a,b)`).**
```byn
for (key, value) in members { print($"{key.name} {value}") }
```
`docs/bayan-spec.md:233-235`

**Strings: immutable UTF-8, byte `length`, byte-offset slices, no indexer, no `+`; build with `StrBuf`.**
```byn
ReadOnlySpan<u8> text(ref this, Token token) => lexed.source.bytes.span[token.start..token.end]
var sb = StrBuf()
sb.append($"== {name}\n")
Io.stdout.write(sb.finish())
```
`selfhost/parser/parser.byn:99`, `selfhost/parser/main.byn:65-69`

**Interpolation is the concat form; `$"…"` allocates unless passed to `print`/`StrBuf.append`; return a copy of `finish()` with `$"{…}"`.**
```byn
return $"{sb.finish()}"
```
`docs/bayan-handoff.md:732` (Q455), `docs/bayan-spec.md:195`

**String comparisons are ordinal and by bytes: `==`, `startsWith`, `endsWith`, `contains`, `position` (byte offset).**
```byn
unless name.endsWith(".byn") { continue }
```
`selfhost/parser/main.byn:61`; API `docs/bayan-spec.md:1922`

**Integer types: `i8 i16 i32 i64 u8 … usize f32 f64`; sizes/counts are `i64`; conversions are calls `T(x)` (narrowing traps), `T.tryFrom(x)` returns `T?`.**
```byn
const exitOk: i32 = 0
let size = result.source.bytes.length      // i64
u8(x)                                       // never (u8)x
```
`selfhost/lexer/main.byn:24`, `docs/bayan-spec.md:828`

**`ref this` declares a mutating member; default is read-only; call through a `var`.**
```byn
Token advance(ref this) { … pos += 1 … return token }
i32 errorCount(ref this) => …
```
`selfhost/parser/parser.byn:86-91`

**`ref`/`in` at the call site for a mutable/read-only borrow; `Owned<T>` payload is read-only, so readers take `in`.**
```byn
string exprText(in Expr e)        // called exprText(in n.value.left)
var p = Parser.create(lexer.lex(…))
let unit = parseCompilationUnit(ref p)
```
`docs/bayan-handoff.md:717` (Q436), `selfhost/parser/main.byn:50-51`

**`move` transfers ownership; `List`/`Map`/`StrBuf`/`Owned` and any struct holding them are `unique`. Broken implicit copy is BYN2780.**
```byn
return Parser { lexed: move lexed, inGroup: move inGroup, … }
let b = move a                     // var b = a is BYN2780
```
`selfhost/parser/parser.byn:62-69`, `docs/diagnostics/BYN2780.md`

**`Owned` and `in` — mutating through `Owned` or a temporary is BYN2106.**
```byn
// BYN2106: modifying a temporary or read-only value
// "an `Owned` value is read-only" — set every field before Owned(…)
```
`docs/diagnostics/BYN2106.md`, `docs/bayan-handoff.md:717` (Q436b)

**Argument particles: at most one unlabeled object; every other required parameter takes `from:`/`to:`/`until:`/`through:`/`at:`/`by:`/`for:`/`as:`, written at both declaration and call.**
```byn
public void remove(ref this, from Index start, until Index end)
xs.remove(from: 2, until: 5)
bool inside(TextSpan span, until i64 size) => span.end <= size
```
`docs/bayan-spec.md:1356-1364`, `selfhost/lexer/main.byn:55`

**`bool` arguments must be passed by name (BYN2305).**
```byn
p.setNoLambda(value: true)
```
`docs/bayan-handoff.md:749` (Q472)

**Errors: `T f() fails E`, `fail .x`, `try f()`, `catch` / `catch switch`; `return` always returns a value.**
```byn
Response handle(Request r) fails DbError { … }
let resp = signUp(form) catch switch {
    EmailError      => .badRequest("Invalid email"),
    DbError.timeout => .unavailable,
    DbError e       => .serverError(e),
}
let n = parse(s) catch { return -1 }
```
`docs/bayan-spec.md:1224-1242`

**Allocators: `Arena()`, `provide allocator: arena { … }`, `using let arena = Arena()`; `context.allocator`.**
```byn
using let arena = Arena()
provide allocator: arena {
    let path = $"{dir}/{name}"
    …
}
```
`selfhost/parser/main.byn:62-63`

**Imports: a directory is a module; `import lexer`; qualify nothing — import brings names in; alias with `as`.**
```byn
import lexer

const exitOk: i32 = 0
```
`selfhost/parser/main.byn:16-18`, `docs/bayan-spec.md:2071`

**Re-export/alias a module type with `type X = lexer.X`; a struct literal cannot name its type through a module (Q431).**
```byn
type TextSpan = lexer.TextSpan
type Token = lexer.Token
```
`selfhost/parser/parser.byn:19-23`

**Tests: `test "name" { assert expr }`, sibling `x.test.byn`; `assert f() fails E.case`; `@cases(...)`; plain expressions, no fluent API.**
```byn
test "parse rejects missing @" { assert Email.parse("x.y") is .invalid }
@cases((0, false), (1, true), (100, true), (101, false))
test "quantity bounds" (i32 input, bool valid) { … }
```
`docs/bayan-spec.md:2132-2138`; goldens in `tests/cases/run/assert-fails.test.byn`, `tests/cases/run/test-cases.test.byn`

**`print` takes ONE string (2nd positional is a `Stream`); interpolate. `Io.stdout.write` for raw text; `panic` aborts.**
```byn
print($"total {total} for {order.id}")
```
`docs/bayan-spec.md:1396-1398`, `docs/bayan-spec.md:1090`

**No semicolons; newlines end statements; braces mandatory; no parens around conditions.**
```byn
if token.kind != .endOfFile {
    pos += 1
    prevEnd = token.end
}
```
`selfhost/parser/parser.byn:87-92`

**Casing: types PascalCase, everything else camelCase; union cases/error variants camelCase; keywords can't be identifiers.**
```byn
enum ParamModifier { none, refKeyword, moveKeyword }   // written one per line
```
`docs/bayan-spec.md:723-726`, `selfhost/parser/nodes.byn:369-374`

## Does NOT have (C# habit → BYN code → replacement)

| C# habit | Evidence / code | Bayan replacement |
|---|---|---|
| `let (a, b) = pair`, `pair.0` | BYN1001 (compiler-checked) | named tuple fields `t.line`, or `switch t { case (let a, let b): }`, or a small struct |
| classes, inheritance, `object` | §1:41 "Structs only … No classes, no inheritance, no `object`, no GC"; §3.10:382 | structs + `interface`; `embed T` promotes fields |
| generics beyond constrained monomorphized forms: generic interface methods, variance, higher-kinded types | §11:1454-1463; selfhost gaps §3.15:535-541 | concrete types; `dyn ref IVisitor`; `switch` |
| storing a lambda in a field or returning one | BYN2822, `docs/bayan-spec.md:560`, `docs/bayan-handoff.md:696` (Q346) | function-typed *parameter*, `type Predicate<T> = T -> bool`; switch on an enum key (`Map<Symbol,i32>`) |
| lambda capturing a `var` / `this` / `Map` | BYN2821 (§3.16:600), BYN2780 (`docs/bayan-spec.md:252`) | capture only `let`/`const`/params by copy; pass the map as a parameter |
| stored lambda keyed in a `Map<Symbol, () -> void>` | BYN2822, **spec §3.3:251** | `Map<Symbol, i32>` + switch |
| LINQ (`Where`/`Select`/`Sum` chains) | §16:1774 "Not in v0.1: everything that takes a lambda"; only plain loops/builtins | `for`, `if`, explicit `List` building |
| `a?.x` | BYN2022 "`?.` is not supported in v0.1" (compiler-checked) | `if (a is { } v) { … v.x … }`; `??` does work |
| exceptions / `throw` / `try … finally` / `using` for disposal | §1:44 errors are `fails`/`Result`; §7.6:1072 "no `using` for disposal"; `using` is only arena scoping and is BYN2226 on a `Map` (`work/todo.md:50`) | `fails`, `try`, `catch`, `defer` |
| `Int128`, `BigInteger`, `decimal` (C# `decimal`/`m` suffix) | §3.1:169 "`decimal` … not accepted"; types are `i8…u64/isize/usize/f32/f64/dec`; no Int128/BigInteger anywhere in §3.1 or §17 | `i64`/`u64`, `dec` for decimal; overflow traps |
| `var` as a parameter or field type (implicitly typed declarations) | §7.1:964 `var` is only a local binding; fields/params are type-first (§3.10:368); `var` parameters would be BYN1043 if written name-first | write the type; `var` only for mutable locals |
| `foreach` / `for (…;…;…)` / `while (x)` | BYN1019, BYN1017 (`docs/bayan-spec.md:2275-2276`) | `for x in xs`, `while x` |
| string `+`, `s[i]` indexer, `Substring`, `ToString()`, `Length` capital, `string.Format` | `docs/bayan-spec.md:190` (no indexer), `:195` (no `+`), `:1749` (slice for Substring), `:915` (no `toString`), `:188` (`length`) | `$"{a}{b}"`, `StrBuf`, `s[a..b]`, `string(x)`, `.length` |
| `s.Contains` case-insensitive ambient culture | §16.4:1970-1971 no ambient culture, ordinal only | `equals(t, as: .caseless)`; culture via `kit.intl` |
| current working directory / `Environment.CurrentDirectory` builtin | **Unverified — I found no cwd builtin** in §16 or the selfhost; the CLI takes explicit paths (`selfhost/parser/main.byn:5-9`) | pass paths explicitly; `Io.listDir`/`Io.readFile` take a path |
| ASCII-only casing assumptions in the ported compiler | Q442: `char.IsUpper` is Unicode in C#, `startsUpper` is `A`–`Z` because Bayan has no case table | check `A`–`Z` explicitly; don't port Unicode casing |
| tuple-typed struct field `(i8, i64) f1` | `work/todo.md:24` BYN1001 (parsed as function type) | use a named struct |
| `List<T>` element removal by index in the port's first cut | Q440 "`List` has no `removeAt`" (a tombstone workaround); spec has `remove(at:)` §16:1743 | set `code = ""` and skip, or `remove(at:)` where built |
| `HashSet<T>` | Q449(c): replaced by `List` searched in order; `Set` is not in v0.1 (§16:1774) | `List` + linear search, or `Map<K,bool>` |
| `params object[]`, `T... xs` | BYN1058 (`docs/bayan-spec.md:2314`); no boxing | `rest ReadOnlySpan<T> items`; spread `..xs` |
| struct literal named through a module `lexer.TextSpan { … }` | BYN1001, Q431 (`work/todo.md:305`) | `type TextSpan = lexer.TextSpan`, then `TextSpan { … }` |
| method overload resolution / `out` in new code | `out` accepted for C# compat, `in` redundant (§10:1336-1337); overloads checked at declaration (§10:1380) | return `T?` or a tuple; `ref`/`move` |
| modifying a temporary or an `Owned` child | BYN2106 | build fully, then `Owned(…)`; take `in` |

## Gotchas seen in the selfhost port

- Q430: no package root for `selfhost/parser`, so `import lexer` works only via the test loader / API, not `bayan build selfhost/parser`.
- Q431: only public lexer members are reachable; the parser made `TokenKind`, `Keywords.build`, `TextSpan`, `SourceText` and token fields public.
- Q433: 70 parser codes (69 before BYN1069, spec 013 T2), and an empty file dumps `CompilationUnit\n` without the extra blank line.
- Q435: union children are `Owned<…>` variants plus `none`; `List<Stmt>`/`List<Decl>` of the unions; defaults `List<T>()`, `.none`, `null`, `false` are all writable in a struct.
- Q436: a union with a `List` payload is `unique`, so a child cannot be passed by value (BYN2780); build into locals and `move`; `Owned.value` is read-only (BYN2106), so set fields before wrapping; readers take `in`.
- Q437: union variants need `Keyword` suffixes (`importDecl`) and keyword-named fields become `defaultKeyword` etc.; `import`/`struct`/`if` cannot be variant names.
- Q440: no `removeAt` in the first cut — `unreport` tombstoned the diagnostic (`code = ""`) and `errorCount` skips it.
- Q441: sibling-owned helpers were stand-ins in `struct T3 { static … }` because a module cannot define a function twice; deleted at T15.
- Q442: `char.IsUpper` became ASCII `A`–`Z` (`startsUpper`), so non-ASCII upper-case names parse as constants.
- Q445/Q450/Q460/Q467/Q470/Q476: every cross-file helper got a `struct T4/T5/T7/T8/T9/T10` static stand-in with the C# name and parameter order (particles guessed) until T15.
- Q446: C# O3 (`ParseStruct` moving field rules into `StructDecl.rules` with `with`) cannot run after `Owned`, so it moved to `parseFieldOrProperty` before wrapping (Q314 in `work/todo.md`).
- Q447/Q448/Q452/Q456: C# `Name` strings with no source span became `TextSpan`; a missing name is a zero-length span and the dump rebuilds `<missing>`, C# type renames and `char`→`rune`.
- Q449: `HashSet<string>` became a `List` searched in order; `out`-style extra results became `to ref`/`through ref` parameters (`parseParamsWith(to ref refThis, through ref moveThis, for ref receiverStates, …)`).
- Q451: `decl with { Doc = …, Attributes = … }` after construction is impossible on an `Owned`, so per-kind parse functions return their struct and the caller wraps once.
- Q455: `StrBuf.finish()` is a borrow (BYN3003 on return), so decoders return `$"{sb.finish()}"`; a `List` returned from a span-taking helper counts as borrowing it.
- Q457: `ReportPointerOutsideExtern` appends a `Diagnostic` directly to `p.lexed.diagnostics` to avoid moving `lastErrorAt`.
- Q461: parser-only fields (`_bracesReport`/`_bracesFix`) became a passed `BracesReport`; a `Diagnostic?` is an index into `p.lexed.diagnostics` (-1 for null) and fixes are set in place.
- Q463: `ExpectName("¬<missing>")` text is lost; a tuple loop variable is the group span, not `"_"`.
- Q466: unions have no `.Span`, so `exprSpan(in Expr)`/`patternSpan(in Pattern)` were written with one arm per variant.
- Q471: a child cannot be moved out of an `Owned` (BYN2782), so `reparseConstant` rewinds and re-parses instead.
- Q472: bool literals must be named (`value: true`), `== null` is BYN2027, `unless` rejects `&&`/`||` (BYN1022).
- Q473: C# evaluates `SpanFrom(start)` before its child, so a `move`/`fail`/`except`/`?.` span ends early; the port matches C# and the child falls outside the parent span (L018).
- Q475/Q477: a `TextSpan` cannot hold a renamed name (compact specs, `stackalloc int` → `i32`), and storing `decodeInterpolatedText(p.text(token))` is BYN3004 (p modified while borrowed) — copy with `$"{…}"`.
- Q480: field paths do not narrow, so bind the field: `if (d.value.failsKeyword is { } fails)`; `for` over `List<Param>` binds a borrow because `let param = parameters[i]` is BYN2780; a parameter named like its function shadows it (BYN2220), so rename to `node`.
- Q485/Q487: `BlockExpr` needs a statement dump entry point; `Sexp` with `params Expr?[]` was inlined because there is no `params` and move-only unions cannot go in a list.
- Q486: `is { }` must be parenthesized in an `if` head; a payload variant needs a pattern (`x is .mapEntryExpr(_)`), and `none` needs none.
- Q488/Q493/Q504: break-tests were run by editing one arm/string, running a named test, then restoring from `tmp/*.bak`.
- Q490/Q500/Q502: `--many` exits 0/2 like `bayan-lex`; the span-check lines are Bayan-only and compared up to the first `span out of range:`.
- `work/todo.md`: `if a == b` emits `((x == y))` and fails clang `-Werror`; `map[k].clear()/add` are BYN2106; `List<T>` reassignment while borrowed is unchecked; unique generic instances are not region-checked; `string` and `Symbol` have duplicate-switch-arm holes.

Not reached: I did not read the actual `tests/cases/run/*.test.byn` bodies (only their names/sizes), the `selfhost/lexer/lexer.byn` internals, or the C# front end; tuple `let`-destructuring and `pair.0` absence are marked unverified rather than proven.
exit=0

## End your final message with
Files changed; test count; the guard's last 3 lines; break-tests (file:line → failing test); Questions added; done / not done / exact next step.
