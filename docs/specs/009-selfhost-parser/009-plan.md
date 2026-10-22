# 009 — Plan
Spec: ./009-spec.md
Written 2026-10-22 against dev at `7cff9b9`. Line numbers below are at that commit; round-3 steps 5.1a–5.3a move them, and T1 re-reads them.

## Approach

Port the C# parser the way spec 001 ported the lexer: one Bayan method per C# method, same names in camel case, same order of checks, same diagnostics, and a test harness that builds the Bayan program once and diffs its output against the C# output on every input the repository has plus a generated corpus. The port starts only after every round-3 step that changes the parser has landed (Depends-on line), so it ports one grammar, not a moving one.

The one C# change comes first (T1): a nesting limit, BYN1070, in `Parser.cs`. Without it the C# test process dies on deep input with a stack overflow that no test can catch, and the two parsers would fail at different depths, so L017 could not be stated for every input.

The tree is data the binder port can key on: five pools of `{ span, kind }` structs, single-field id structs, flat child lists, and `Symbol` names (Q144, Q145). The dump walks the pools and prints exactly `AstDump`'s text, then the diagnostics through the lexer port's `renderAll`. Equivalence is checked on that text only (Q146).

The module is the lexer's library files plus the parser files, assembled by the harness with `Compilation.Create` (Q143), so the lexer stays as round 3 leaves it.

## Tech

- Language: Bayan in round-3 syntax for `selfhost/parser/`; C# for T1 and the harness.
- Uses from 006: `IIndexable` (`length: i64`, `get(at i: i64) -> Item` after round-3 C20) on `Pool<T>` so `for node in pool` works; a generic `checkSpans<T>(pool: Pool<T>) where T : ISpanned`; `ISpanned` is a 009 interface with one property `span: TextSpan`.
- Uses from 007: function-type parameters `TokenKind -> bool` and lambdas over `it` for recovery skips. No lambda captures `this` or a `var`; no function value is stored in a field.
- Uses from 008: `Symbol`, `Symbol.from(text)`, `sym.text`, symbol literals for contextual keywords; `Map<Symbol, TokenKind>` if the lexer's `Keywords` table needs a counterpart in the parser (contextual keywords only).
- Build: `Compilation.Create(entryFiles, packageRoot: selfhost/parser, …)` (`src/Bayan.Semantics/Compilation.cs:94`), then `CliApp.EmitC(c, BuildMode.Fast)` and `Toolchain.Build`, as `tests/Bayan.Tests/SelfhostLexerTests.cs` does; `BAYAN_CACHE=tmp/cache`.
- No new NuGet packages, no runtime (`src/runtime/`) change, no C emitter change.

## Changes by area

### C# parser (T1)

- `src/Bayan.Syntax/Parsing/Parser.cs`: add `private int _depth` and `private bool _tooDeep`. A helper `bool Enter()` increments `_depth` and, at 257, reports BYN1070 at `Current.Span`, sets `_tooDeep`, and moves `_pos` to the end-of-file token; callers return an `ErrorExpr` / `ErrorStmt` / error type / error pattern on `false` and decrement in a `finally`. Call sites: `ParseExpression` (`:1510`), `ParseStatement` (`:845`), `ParseType` (`:757`), `ParsePattern` (`:2121`), and the prefix-operator branch of `ParseUnary` (`:1652`) and `ParseNotPattern` (`:2147`). `Report` (`:85-91`) returns early when `_tooDeep`. `ParseCompilationUnit` (`:202`) and `ParseBlock` (`:825`) stop their loops at end of file, as they already do.
- `src/Bayan.Syntax/Diagnostics/DiagnosticCodes.cs`: `NestingTooDeep = "BYN1070"` with its template (message, label, help) in the same table as BYN1001–1016.
- `tests/Bayan.Tests/ParserTests.cs`: a fact that 100,000 nested `(` parse to exactly one BYN1070 without crashing; a fact that 256 levels report nothing; a fact that records the deepest nesting in `tests/cases`, `examples`, `selfhost` and `tools` and asserts it is under 64.

### Bayan parser (T2–T7)

New directory `selfhost/parser/`:

- `main.byn`: `usage`, `parseOne`, `parseMany`, `parseTimed`, `main`, mirroring `selfhost/lexer/main.byn`'s `lexOne` / `lexMany` / `lexTimed` with the same exit codes.
- `ast.byn`: `TextSpan` is reused from the lexer; `Name`, the id structs, `IdRange`, `Pool<T>`, the five node structs and their `kind` unions, `ISpanned`, and `Tree { decls, stmts, exprs, types, patterns, lists, diagnostics }`.
- `parser.byn`: `struct Parser` with the token list, position, `lastErrorAt`, `conditionStart`, `depth`, `tooDeep`; token access, `report`, `describe`, `expect`, `skip(to:)`, the brace and semicolon scans (`Parser.cs:1000-1071`). If it passes about 1,500 lines it splits into `declarations.byn` (`Parser.cs:202-756`), `statements.byn` (`:825-1509`) and `expressions.byn` (`:1510-2287`); the split is by C# region so a reviewer can read them side by side.
- `literals.byn`: port of `src/Bayan.Syntax/Parsing/LiteralDecoder.cs` (118 lines), `decodeString` and `decodeDoc`, used where `Parser.cs:224`, `:674` and `:1862-1868` use it.
- `ast-dump.byn`: port of `src/Bayan.Syntax/Ast/AstDump.cs` (696 lines): `dump(tree, source) -> string`, `mods`, `params`, `attribute`, one function per node category; escapes via the lexer's `escape` (`selfhost/lexer/dump.byn`).
- `messages.byn`: the parser message table (code, message template, label, help, fix text) for every `BYN1xxx` code `Parser.cs` reports, including BYN1070.

`selfhost/lexer/`: no change, unless Q151's condition holds at T6 (then `diagnostics.byn:74` gains `severity` and `related`, `render` and `renderAll` print them as `DiagnosticRenderer` does, and L001 is re-run).

### Harness (T2, T6–T9)

- `tests/Bayan.Tests/SelfhostParserTests.cs` (new), modelled on `SelfhostLexerTests.cs`:
  - a `Lazy` build of `bayan-parse` from `selfhost/lexer/*.byn` minus `main.byn` plus `selfhost/parser/*.byn`;
  - `[Theory]` over `Golden.Cases("parse")` comparing stdout with the `.ast` file;
  - `[Theory]` over every `.byn` under `tests/cases`, `examples`, `selfhost`, `tools` comparing with `AstDump.Dump(SyntaxTree.Parse(source), Style.Plain)`;
  - a `[Fact]` for the self-reach set (R13);
  - a `[Fact]` that every `BYN1xxx` constant in `DiagnosticCodes.cs` appears in `selfhost/parser/messages.byn`;
  - `[Fact, Trait("Law","L017")]` over the corpus (Q149) through `--many`, plus stdin runs for a sample of 200;
  - the timing `[Fact]` honouring `BAYAN_SKIP_TIMING`.
  - On a mismatch the message names the input and prints the first differing line of each side.

### Docs (T1, T10–T12)

- `docs/diagnostics/BYN1070.md`, `tests/cases/diag/nesting-too-deep.{byn,diag}`, `tests/cases/parse/nesting-too-deep.{byn,ast}`.
- `docs/laws/017-parser-equivalence.md` (T11).
- `docs/bayan-handoff.md`: Q143–Q151 under Questions (`:411`), row 009 marked done (`:376-402`).
- `docs/bayan-self-hosting.md`: phase 3 notes the parser as ported.
- `CHANGELOG.md`: one entry.

## Data and interfaces

### New diagnostic code

| Code | Name | Message | Label | Help | Fix |
|---|---|---|---|---|---|
| BYN1070 | `NestingTooDeep` | nesting is deeper than 256 levels | too deep | move the inner part into a named local or function | none |

BYN1070 is the first code of a parser block nothing allocates: round 2 reserved BYN1017–1033, round 3 allocates BYN1040–1062, and a search of dev, 006, 007 and 008 at this commit finds no BYN107x. BYN1071–1079 stay spare for 009. 009 allocates nothing in BYN28xx (006: 2800–2819, 007: 2820–2829, 008: 2830–2839).

### Tree (round-3 syntax)

```bayan
struct Name {
    span: TextSpan
    text: Symbol
}

struct ExprId {
    index: i32
}

struct IdRange {
    start: i32
    length: i32
}

interface ISpanned {
    span: TextSpan { get }
}

struct ExprNode : ISpanned {
    span: TextSpan
    kind: ExprKind
}

union ExprKind {
    name(name: Name)
    literal(token: Token, text: string)
    binary(op: TokenKind, left: ExprId, right: ExprId)
    call(callee: ExprId, args: IdRange)
    error
}

struct Pool<T> : IIndexable {
    items: List<T>
    length: i64 => items.length

    get(at i: i64) -> T => items[i]

    add(ref this, item: T) -> i32 {
        items.add(item)
        return (i32)(items.length - 1)
    }
}
```

`ExprKind` shows four of the 31 cases; each C# record in `Nodes.cs:227-345` becomes one case with the same fields, children as ids, lists as `IdRange` into `Tree.lists` (one flat `List<i32>` per category). Lists are collected on a scratch stack and copied into `lists` when the enclosing node is finished, so a list's ids are contiguous.

### Recovery

```bayan
skip(ref this, to stop: TokenKind -> bool) {
    while not this.atEnd and not stop(this.current.kind) {
        this.advance()
    }
}

this.skip(to: it == .closeBrace or it == .newlineStatementStart)
```

The token kind names are the lexer's (`selfhost/lexer/tokens.byn`); the ones above are placeholders until T1 reads them after round 3.

### CLI

`bayan-parse [<path>|-] | --many <dir> | --time <path>`; output is always `Style.Plain`; exit 0 / 1 / 2 / 4 as spec R1.

## Risks

- Round-3 drift. 5.1a–5.3a rewrite large parts of `Parser.cs` and `AstDump.cs` and regenerate the parse goldens. Mitigation: 009 starts after them (T1 depends on 5.3a), and any later parser change is a C# change first and fails L017 until ported (Q148).
- 006/007/008 slip. Mitigation: `Pool<T> : IIndexable` can be a plain struct with an index loop, recovery predicates can be one `skip` per stop set, and `Name.text` can be `string`; each fallback is local to `ast.byn` or `parser.byn` and is recorded in T2 if taken. The equivalence target does not change.
- Size. The port is about 3,000 lines of Bayan, the largest Bayan program yet; compiler bugs will show up. Mitigation: each one is fixed in the C# compiler with its own golden before the port continues, and the task that found it names it.
- Speed. Generic pools and union matches may cost more than the lexer's flat loops. Mitigation: R11's 10× bound is the same as the lexer's; if it fails, profile with `--time` before changing the tree shape.
- Deep recursion in Bayan. Bayan's stack at 256 levels of the deepest call chain (about 12 frames per level in the expression parser) must fit in the default C stack. Mitigation: T1's 300-level test runs against the Bayan parser in T5, and if it overflows the limit drops for both parsers.
- Spans not compared. Q146 compares text, so a wrong node span that the dump does not print passes. Mitigation: the `--many` range check, the diagnostic carets, and 010's goldens; a span dump is a later addition.

## Coverage check

| Req | Tasks |
|---|---|
| R1 | T2, T8, T9 |
| R2 | T2–T7 |
| R3 | T3, T4, T5, T6 |
| R4 | T2, T3, T4, T5 |
| R5 | T6 |
| R6 | T2, T3, T4, T5 |
| R7 | T6 |
| R8 | T1, T5 |
| R9 | T6, T7 |
| R10 | T8, T10, T11 |
| R11 | T9 |
| R12 | T1 |
| R13 | T7 |
| L017 | T10, T11 |
| Q143–Q151 | T12 |
