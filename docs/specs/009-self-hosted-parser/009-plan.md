# 009 — Plan
Spec: ./009-spec.md

## Approach

Port `src/Bayan.Syntax/Parsing/Parser.cs` (4,248 lines), `Ast/Nodes.cs` (523) and `Ast/AstDump.cs` (836) method by method into one Bayan module, `selfhost/parser/`, the way spec 001 ported the lexer. The module is many files; Bayan modules are directories, so every file in `selfhost/parser/` sees every other, and the parallel writers each own whole files. The C# output is the reference: every function keeps its C# name in camelCase, its C# order of checks and its C# recovery points, and a comment at the top of each file names the C# range it ports.

The shape of the port, decided here so the parallel wave does not have to:

- **Tokens come from `selfhost/lexer/` by `import lexer`.** Imports resolve under the compilation's package root (`Compilation.ResolveImport` joins `PackageRoot` with the import path and loads every `*.byn` in that directory). From `selfhost/parser/`, `import lexer` resolves only when the package root is `selfhost/`. `Compilation.FromPath(selfhost/parser/main.byn)` uses the file's own directory as the root, so it cannot see the lexer; the test harness builds the parser with `Compilation.Create(sources, packageRoot: <repo>/selfhost)` (the same `SourceText.FromFile` loading `FromPath` does for a whole directory). No compiler change is needed to build and test. The CLI has no way to set a package root outside a `package.byn` project (`CliApp` uses `FromProject(root, entry)` with `src/<entry>/` or `FromPath`), so `bayan build selfhost/parser/main.byn` does not work today; this is recorded as a Question, not fixed in 009 (see Risks).
- **Lexer declarations the parser uses become `public`.** Module visibility is the default (`Compilation.cs` maps no modifier to `Accessibility.Module`), and a `Module` member used from another module is BYN2005 (`BinderExpressions.cs`, "not `public`"; types in `TypeResolver.cs` likewise). Nothing in `selfhost/lexer/` is `public` today. The skeleton task adds `public` to the types, fields, methods and functions the parser needs (`TokenKind` and its helpers, `Token`, `Keywords`, `lex`, `LexResult`, `SourceText`, `TextSpan`, `Diagnostic`, `renderAll`, `sourceOrder`, `escape`). A `public` function without `uses` is checked against an empty effect set (`EffectAnalysis.cs`, `fn.Access == Accessibility.Public`), so `lex`, `Keywords.build`, `SourceText.create`, `renderAll` and `escape` get `uses alloc` (the clause form `uses alloc` is in `run/lambda-uses-provide.byn`). Token and diagnostic output does not change; the lexer goldens and L001/L002 are rerun. Importing a module loads all its files, so `selfhost/lexer/main.byn` is compiled too; its `main` is an ordinary function because only the entry module's `main` is the entry (`Compilation.cs`, `file.Module == EntryModule`).
- **The tree is five unions with one struct per node kind.** `Decl`, `TypeSyntax`, `Stmt`, `Expr` and `Pattern` are unions whose variants carry `Owned<XxxNode>` payloads (`run/owned-expr.byn` proves a recursive union through `Owned<T>` under `provide allocator: arena`). Because every variant is an `Owned`, the union itself is two words, so a child is stored as a plain `Expr` field, a child list as `List<Expr>`, and a nullable C# child as a `none` variant of the union rather than `Owned<T>?`. Support records that are not polymorphic in C# (`Param`, `Argument`, `SwitchArm`, `FieldInit`, …) are plain structs. `Modifiers` is a bitmask of `u16` consts (the lexer did the same for token flags; `flags` is a keyword). The other enums (`BindingKind`, `AssignOp`, `UnaryOp`, `BinaryOp`, …) are Bayan enums with camelCase members and a text function per enum matching `AstDump`'s spelling.
- **Names and texts are spans, not strings, until the dump.** A node holds the `TextSpan` of an identifier and the dump slices the source, so no node borrows from the source (the lexer port hit BYN3003 on returned borrows). Decoded literal text (escapes, raw strings, interpolation text, doc comments) is the exception: it is built with `StrBuf` into the arena and stored as `string`.
- **`struct Parser` holds state and the cursor; parsing is free functions over `ref Parser p`.** A struct body cannot span files, so the cursor methods (`current`, `kind`, `peek`, `advance`, `accept`, `text`, `spanFrom`, `atEnd`, `atShiftRight`) live in `parser.byn`, and each C# `ParseX` becomes `parseX(ref Parser p, …)` in the file owning its C# line range. The three flags with `try/finally` in C# (`_noLambda`, `_exprTypeArgs`, `_inExtern`) are saved and restored around a call to a helper function with a single exit; Bayan has no exceptions, so nothing is lost. `WithoutLambdas(Func<T>)` (4 sites) and `ParseBody(string, Func<Decl?>)` (3 sites) are inlined as save/restore plus a direct call; no callback is stored (spec 007's subset).
- **Diagnostics reuse the lexer's `Diagnostic`, `renderAll` and `sourceOrder`.** The parser never uses `Related` (zero occurrences in `Parser.cs`) and every BYN1 code is an error, so the lexer's struct (code, span, message, label, help, fix) is enough. The parser appends to `LexResult.diagnostics`, so lexer and parser diagnostics render in one list as in C#. The 65 BYN1 descriptors become `enum ParseCode` plus `codeText` and `messageFormat` in `codes.byn`; the lexer's `format` handles `{0}` and `{1}` only, while one BYN1 template uses `{2}` and two use `{{ }}`, so `codes.byn` has its own three-argument `format` that also unescapes braces. `Report` returns the diagnostic's index (or -1 when `_lastErrorAt` suppresses it) and `Unreport` tombstones that index (code set to `""`); `List` has no `removeAt`, and the unreported diagnostic is not always the last one. The dump compacts tombstones away before `renderAll`.
- **Output format and exit code mirror `bayan parse --ast`.** `CliApp.Parse` writes `AstDump.Dump(tree, Style.Plain)` and exits `ExitFailure` when any diagnostic is an error, else `ExitOk`. `dump.byn` writes `CompilationUnit`, one two-space-indented line per declaration and statement, inline S-expressions for types, patterns and expressions, then `"\n"` and the rendered diagnostics with no path line (the lexer's `render` already omits the path, like `TokenDump`).
- **The harness mirrors `SelfhostLexerTests.cs`.** One lazily built binary; a golden theory over `Golden.Cases("parse")` against the `.ast` files; a theory over the `diag/` cases whose `.diag` contains `BYN1`, compared with the in-process C# dump written under `tmp/` so `Golden.Compare` names the first differing line; a `--many <dir>` mode for corpora; `--time <path>` for R8; `--codes` to cross-check the message table against `DiagnosticCodes`; L017 and L018 theories with `[Trait("Law", …)]`; `BAYAN_SKIP_TIMING=1`.

Order of work: T1 (skeleton, import mechanism, cursor and report API, stubs for every other file) lands first, then T2 (node types) lands second because every parsing file names the node structs. The wave T3–T14 then runs with disjoint files. T15 integrates (the only task that may touch any parser file after the wave), T16–T19 prove and enact the laws, T20 writes the docs. Each wave writer rebases over merged siblings before finishing and lists which goldens already pass; full parity is T15's done-when.

## Tech / dependencies

- Bootstrap compiler on `dev` with specs 006–008 merged (interfaces, lambdas subset, `Symbol`, composite `Map` keys). No new language feature is planned; any one that turns out to be strictly needed gets its own Question and a compiler task per the spec's non-goals.
- `selfhost/lexer/*.byn` as the token source (`lex`, `Keywords.build`, `SourceText.create`, `TextSpan`, `Token`, `TokenKind`, `Diagnostic`, `renderAll`, `escape`), made `public` in T1.
- Language features relied on: unions with `Owned<T>` payloads and positional patterns, arenas (`using let arena = Arena(); provide allocator: arena`), `List<T>`, `StrBuf` and `$"…"`, `ref` parameters and struct methods on `ref this`, enums with `.ordinal`/`fromOrdinal`, `switch` on strings, function-typed parameters only where a named function is passed (none planned).
- Build and test: `Compilation.Create(IReadOnlyList<SourceText>, packageRoot, options)`, `CliApp.EmitC(c, BuildMode.Fast)`, `Toolchain.Build(…, BuildMode.Fast)` under `BAYAN_CACHE=tmp/cache`; xunit with `Golden.Cases`, `Golden.Compare`, `Golden.RepoRoot`; the process helpers copied from `SelfhostLexerTests.cs`. No new packages.
- Reference data: `tests/cases/parse/` (59 `.byn` + `.ast`), the 84 `tests/cases/diag/*.diag` files containing `BYN1` and their inputs (some live in subdirectories; each `.byn` is parsed on its own), `DiagnosticCodes.cs` BYN1000–1999 (65 descriptors, all errors).

## Changes by area

**selfhost/lexer/** (T1 only, behaviour-neutral): `public` on the declarations listed in Approach and `uses alloc` on the public functions that allocate. No other edit; `bayan-lex` output is unchanged and `Law=L001|Law=L002` rerun.

**selfhost/parser/** (new; one file per task, stubs created by T1 and replaced by their owners):

| File | C# source | Owner | Est. Bayan lines |
|---|---|---|---|
| `main.byn` | `CliApp.Parse`, lexer `main.byn` shape | T13 (stub T1) | 150 |
| `parser.byn` | `Parser.cs` fields, ctor (`_inGroup`), cursor, `Report`, `Describe`, `Expect`, flag save/restore helpers | T1 | 450 |
| `codes.byn` | `DiagnosticCodes.cs` BYN1xxx, `InputForms`, 3-arg `format` | T1 | 250 |
| `nodes.byn` | `Nodes.cs` | T2 | 700 |
| `recover.byn` | `Synchronize`, `RecoverIfNeeded`, `ExpectEnd`, `ReportSemicolon`, `IndentAt`, `BreaksLine`, `InGroup`, `HasBlankLineBefore`, `IsWasl`, `LineContinues`, `EndsBefore`, `ExpectName`, `NameOf`, `IsName`, `IsStatementKeyword`, `Unreport`, `IsContextualKeyword`, `IsCSharpPrimitive`, `ItName`, `Particles` | T3 | 350 |
| `patterns.byn` | `Parser.cs` 4058–4248 | T3 | 250 |
| `decls.byn` | `Parser.cs` 347–~1060 (compilation unit, imports, attributes, modifiers, functions, params, struct, `ParseBody(string, …)`) | T4 | 850 |
| `decls2.byn` | `Parser.cs` ~1066–1740 (interface, extension, fields, properties, enums, error enums and sets, unions, aliases, effects, tests) | T5 | 800 |
| `types.byn` | `Parser.cs` 1743–2113 (`ScanType`, `ScanExprTypeArgs`, `CSharpTypeNames` as a `switch`) | T6 | 450 |
| `literals.byn` | `LiteralDecoder.cs` (118) | T6 | 150 |
| `stmts.byn` | `Parser.cs` 2116–~2665 (blocks, locals, `ParseBody(Token, ref …)`, if/unless/guarded, loops) | T7 | 650 |
| `stmts2.byn` | `Parser.cs` 2667–3160 (switch, defer, assert, context, labels, assignment and remaining statements) | T8 | 600 |
| `exprs.byn` | `Parser.cs` 3158–~3605 (precedence climbing, unary, postfix, members, calls, index, ranges, conditional) | T9 | 550 |
| `exprs2.byn` | `Parser.cs` 3607–4056 (primaries, literals and interpolation with `ParseFormatSpecs`, collections, struct literals, lambdas, blocks, switch expressions) | T10 | 550 |
| `dump.byn` | `AstDump.cs` writer, `Decl`, `Stmt`, `Mods`, `TypeParams`, `Params`, `Doc`, `Bases`, `Where`, `Attribute`, `Rules`, tombstone compaction and `renderAll` call | T11 | 500 |
| `dump2.byn` | `AstDump.cs` `Type`, `Pattern`, `Expr`, `Sexp`, `FieldInits`, `Quote`, `AssignText`, `BinaryText`, `UnaryText` | T12 | 450 |
| `spans.byn` | new: walker asserting every span within the input and every child within its parent (L018), printed like the lexer's `checkSpans` | T12 | 300 |

**tests/Bayan.Tests/**: `SelfhostParserTests.cs` (T1 creates the build `Lazy` and a smoke test; T13 adds the theories; `SelfhostParserGenerator.cs` and `SelfhostParserGeneratorTests.cs` (T14: valid and corrupted program generator, `OneMegabyte()`, `GeneratedSeedsAgree`), `SelfhostParserEquivalenceLawTests.cs` (T16) and `SelfhostParserNoPanicLawTests.cs` (T18), so parallel tasks never share a test file.

**docs/**: `docs/laws/017-parser-equivalence.md`, `docs/laws/018-no-parser-panic.md` (T16–T19), `docs/bayan-handoff.md` (Questions 430 upward, progress entry, Road row 009, readiness section), `CHANGELOG.md`, `009-spec.md` status (T20). `work/todo.md` lines for C# parser bugs found and matched (any task, listed in T20).

## Data / interfaces

**Parser state** (`parser.byn`, T1):

```
struct Parser {
    LexResult lexed            // tokens, diagnostics (appended to), source
    i64 pos; i64 prevEnd; i64 lastErrorAt; i64 syntaxErrors
    bool inExtern; bool noLambda; bool exprTypeArgs
    List<bool> inGroup         // precomputed in create(), as the C# ctor does
}
Parser Parser.create(move LexResult lexed)
Token current(ref this); TokenKind kind(ref this); Token peek(ref this, i64 n); TokenKind kindAt(ref this, i64 n)
bool atEnd(ref this); Token advance(ref this); bool accept(ref this, TokenKind kind)
ReadOnlySpan<u8> text(ref this, Token t); TextSpan spanFrom(ref this, i64 start); bool atShiftRight(ref this)
i64 report(ref Parser p, ParseCode code, TextSpan span, by (string a0, string a1, string a2) args, string label, string help)   // index or -1
i64 reportFix(ref Parser p, …, TextSpan fixSpan, string fixReplacement)
Token expect(ref Parser p, TokenKind kind, string label)
string describe(ref Parser p, Token t)
```

Cross-file entry points fixed by T1 as stubs, one per owning file: `CompilationUnit parseCompilationUnit(ref Parser p)` (decls), `Decl parseDeclaration(ref Parser p, bool topLevel, TextSpan structName)` (decls), `TypeSyntax parseType(ref Parser p)` (types), `Stmt parseStatement(ref Parser p)` and `Stmt parseBlock(ref Parser p)` (stmts), `Expr parseExpression(ref Parser p)` (exprs), `Expr parsePrimary(ref Parser p)` (exprs2), `Pattern parsePattern(ref Parser p)` (patterns), `void synchronize(ref Parser p)` and `void recoverIfNeeded(ref Parser p, i64 syntaxErrorsBefore)` (recover), `string decodeString(…)` and siblings (literals), `void dump(ref CompilationUnit unit, ref Parser p, to: ref StrBuf sb)` (dump), `void dumpExpr(ref Expr e, …)`, `dumpType`, `dumpPattern` (dump2), `void checkSpans(ref CompilationUnit unit, i64 length, to: ref StrBuf sb)` (spans). Every other function is private to its file's area and named after its C# method.

**Tree** (`nodes.byn`, T2): `union Decl { import(Owned<ImportDecl>) globalStatement(Owned<GlobalStatement>) function(Owned<FunctionDecl>) … none }`, likewise `TypeSyntax`, `Stmt`, `Expr`, `Pattern`, each with a `none` variant standing for a C# `null` child. Each node struct starts with `TextSpan span` and carries the C# properties in order: names as `TextSpan`, decoded texts as `string`, children as the union type, lists as `List<…>`, booleans and enums as is. `Modifiers` is `u16` with one const per flag. Node spans and child order are those of the C# records so T11/T12 can dump them without lookups.

**Diagnostics**: `enum ParseCode` in `DiagnosticCodes.cs` order; `string codeText(ParseCode)`; `string messageFormat(ParseCode)`; `bool isInputForm(ParseCode)`; `string format(string template, by (string a0, string a1, string a2) args)`. Reused from the lexer: `Diagnostic`, `renderAll(ReadOnlySpan<Diagnostic>, to:, from:)`, `sourceOrder`, `escape`.

**CLI** (`main.byn`): `bayan-parse [<path>|-]` prints the dump and exits 0, or 1 when any diagnostic is present (all lexer and parser diagnostics are errors); `--many <dir>` prints `== <name>\n` + dump + span-check lines per `.byn` in a fresh arena; `--time <path>` prints the nanoseconds for lex + parse + dump of one file; `--codes` prints `code\tmessage` for every `ParseCode`. Usage and missing-file exits follow the lexer's constants.

**Harness** (`SelfhostParserTests.cs`): `Built` = `Lazy<string>` building `Compilation.Create(selfhost/parser/*.byn, packageRoot: selfhost/)` in fast mode; `ParseGolden(name)` over `Golden.Cases("parse")` vs `.ast`; `DiagGolden(name)` over diag cases whose `.diag` contains `BYN1`, reference `AstDump.Dump(SyntaxTree.Parse(SourceText.FromFile(path)))` written to `tmp/parser-ref/`; `CodesMatchDiagnosticCodes`; `GeneratedSeedsAgree` (valid and corrupted corpora under `tmp/parser-corpus/`, run with `--many`, compared file by file with the in-process reference); `[Trait("Law","L017")]` equality including exit codes; `[Trait("Law","L018")]` exit 0 or 1 and no span-check line over corrupted and random-byte corpora; `OneMegabyteWithinTenTimes` median of 5.

## Risks & mitigations

- **Cross-module visibility forces lexer edits (T1, Q431).** `public` is required (BYN2005 otherwise), and `public` turns on effect checking, so allocating functions need `uses alloc`. If the cascade reaches something that cannot be expressed (a `public` method on a `ref this` struct that the checker rejects, or an effect the lexer should not declare), T1 stops and records the exact diagnostic; a compiler fix with a test is in scope per R9. If §4.1 of the spec intends package-wide visibility for module members, that is a compiler gap and goes in the same Question for the semantics port to settle.
- **The CLI cannot build the parser (T1, Q430).** `bayan build` has no package-root option. Conservative choice: the harness builds through `Compilation.Create`; `dotnet test --filter SelfhostParser` is the documented way to build `bayan-parse`. A `--root` option or a `package.byn` for `selfhost/` is proposed in the Question for a later spec.
- **Recursive unions through the allocator (T2, Q435–Q437).** `Owned<T>` in a union variant is proven by `run/owned-expr.byn`; `Owned<T>?` is not, hence the `none` variants. `List<Expr>` holding a union of `Owned` payloads (move-only elements) is checked by T2's first build; `run/owned-list.byn` covers `List<Owned<T>>`. Fallback if either shape fails: node arenas as `List<XxxNode>` per kind in `Parser` with `i64` children, still allocated through the provided allocator; the dump and spans walker only change how they reach a child. T2 records which shape shipped.
- **Union size and copies.** Every variant is an `Owned`, so a union is small and `List<Expr>` growth moves two words per element. Node structs are reached through `.value` and never copied.
- **Borrowed strings from the source (all parsing tasks).** Nodes hold `TextSpan`s; `text(token)` returns a `ReadOnlySpan<u8>` used only for comparisons (`sameBytes` from the lexer) and for building decoded strings with `StrBuf` into the arena. If a decoded `string` from `finish()` is still rejected as a borrow when stored in a node, T6 records it (Q455) and stores the span of the literal plus a flag, decoding at dump time.
- **`Unreport` without `removeAt` (T3, Q440).** Tombstone by setting `code` to `""` and decrementing `syntaxErrors` as C# does; `dump.byn` compacts before `renderAll`. `_lastErrorAt` is not reset by `Unreport` in C# and is not here.
- **Templates with `{2}` and `{{ }}` (T1).** `codes.byn` formats up to three arguments and unescapes doubled braces; the `--codes` cross-check in T13 catches a mistyped message.
- **Callbacks and `try/finally` (T4, T7, T9, T10).** `WithoutLambdas` and `ParseBody(string, Func)` are inlined as save/restore around a direct call to a helper with one exit. No function-typed field, no stored lambda, no `ref` capture. If a site cannot be inlined without duplicating a large body, a named function is passed as a function-typed parameter (`by (ref Parser -> Expr) f` is unverified; the owning task records whether it compiled).
- **`List<bool>` (T1).** No run golden uses it; if it misbehaves, `inGroup` becomes `List<u8>`.
- **Tables as `switch`.** `InputForms`, `ItName`, `Particles`, `CSharpTypeNames` and the statement-keyword set become `switch` functions on `ParseCode`, `TokenKind` or `string`; no `Map` on the hot path.
- **Performance (T13, T15, Q500).** Costs mirror C#: one arena allocation per node, `StrBuf` for the dump, `InGroup` scans as in C#, `describe` and `format` allocate only on error. `--time` reports lex + parse + dump so a miss can be attributed. If the ratio exceeds 10, T15 records the breakdown and the cause (expected suspects: `List` growth of unions, `renderAll` sorting, per-token `text` slicing) and fixes it only inside `selfhost/parser/`.
- **Unbounded recursion in both parsers (T18, Q508).** `Parser.cs` has no nesting limit, so deep input overflows the stack in C# and in the port alike. The generator caps nesting depth; L018's proof notes state the exclusion and the Question proposes a shared limit for a later spec.
- **L018 may be false of the C# reference (T18, Q509).** A zero-length `Expect` token at `Current.Start` or a recovery that ends a node before its last child could put a child outside its parent on some corrupted input. If the walker finds such a case, the Bayan parser keeps matching C# (L017 and the non-goal win), the input goes to `work/todo.md` and the proof notes, and the law's statement is left as proposed until the C# fix lands; enacting is then T19's call to skip and say so.
- **Lexer `main.byn` is compiled into the parser (T1, Q432).** Its `main` is an ordinary function in a non-entry module. If the binder or effect pass objects to it, the conservative choice is to keep `main.byn` as is and record the diagnostic; renaming or moving the lexer entry is a lexer change outside this spec.
- **Diag cases in subdirectories (T13).** Each `.byn` under a multi-file case is parsed separately; the reference is the in-process C# dump of that one file, never the `.diag` golden (which also holds semantic diagnostics).
- **Writers cannot see full parity until the wave merges.** Each wave task's done-when is a complete method-for-method port that builds; the writer rebases over merged siblings and names the goldens that already pass; T15 owns the last mile and may edit any parser file.

## Coverage check

| Requirement / law | Tasks |
|---|---|
| R1 program, same text and exit code | T13 (`main.byn`, harness), T15 (parity) |
| R2 lexer by import, L001/L002 hold | T1 (import mechanism, `public`, lexer suite rerun) |
| R3 tree, every node kind, allocator | T2 |
| R4 dump byte-identical | T11, T12, T15 |
| R5 diagnostics and recovery | T1 (`report`, `expect`), T3 (recovery), T4–T10 (per area), T15 |
| R6 golden coverage, first differing line | T13 |
| R7 generator, fixed seeds, mutation sensitivity | T14 |
| R8 speed bound | T13 (`--time`, test), T14 (`OneMegabyte`), T15 (measured) |
| R9 port friction as Questions / todo lines, blocking bugs fixed with tests | every task's Question range; T20 lists them |
| R10 docs | T20 |
| L017 | T16 prove, T17 enact |
| L018 | T12 (`spans.byn`), T18 prove, T19 enact |

