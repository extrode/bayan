# 009 — Parser in Bayan
Status: draft
Created: 2026-10-22
Depends on: design round 3 plan (`docs/plans/2026-10-22-001-design-round-3.md` §4): every step that changes what the C# parser accepts or prints lands before 009 T1, and round 3 wins on any conflict. Those are 5.1a–5.1d (no semicolons, name-first bindings and fields, `->` returns, `@` attributes, `//` only, word booleans, `rune`, `requires` rules, `except`, `uses`, `provide`, typestate syntax, particles), 5.2a (the input-form sweep, which sets the fix spans in `Parser.cs`) and 5.3a (`try` covers the whole expression, `Parser.cs:1671-1673`). The same steps move `selfhost/lexer/` to round-3 syntax and keep L001. Spec 006 (interfaces and iterators: `IIndexable`, `where` constraints), spec 007 (function types and non-capturing lambdas with `it`) and spec 008 (`Symbol`, single-field struct keys) are on dev before 009 T2; 009 uses each as listed in R4 and R5.

## Problem

The handoff's "Road to self-hosting" row 009 ports the parser: a Bayan program in `selfhost/parser/`, checked against `bayan parse --ast` on every parse golden. Spec 001 did this for the lexer (`selfhost/lexer/`, L001). The parser is four times its size: `Parser.cs` (2,287 lines, 114 methods, recovery at statement and declaration boundaries, the brace and semicolon lookahead scans at `:1000-1071`), `Nodes.cs` (102 node records: 12 declarations, 22 statements, 31 expressions, 16 types and patterns), `LiteralDecoder.cs` and `AstDump.cs` (696 lines, the text form the 30 `parse/` goldens keep). Nothing in Bayan holds a syntax tree today. Two gaps sit in the C# side too. The C# parser has no nesting limit: `ParseUnary` (`:1652`), `ParsePrimary` (`:1842`), `ParseType` (`:757`), `ParseNotPattern` (`:2147`) and `ParseBlock` (`:825`) recurse once per level, so a file of 100,000 `(` overflows the .NET stack and kills the process, and a Bayan parser would crash at a different depth, so no equivalence law can hold "on every input". And the dump the goldens use is the only text form; it prints no node spans.

## Goals

- `bayan-parse`, written in Bayan in round-3 syntax and built by the C# compiler, prints exactly what `bayan parse --ast` prints, on every parse golden and on every other input: the tree and the rendered diagnostics.
- The tree it builds is the one the binder port (spec 010) will read: names are `Symbol`s, nodes are addressed by small ids, and no node holds a pointer or a borrow.
- Both parsers bound their nesting the same way, so neither crashes on any input.
- A change to either parser that makes them disagree fails the normal test run and names the input and the first differing line.

## Non-goals

- Replacing the C# parser inside the compiler. The Bayan parser is a second implementation, checked against the first, until spec 012.
- Better recovery or new syntax. The port is 1:1 by method; a change to recovery or grammar lands in `Parser.cs` first and is ported after (Q148).
- A JSON or span-carrying form of `bayan parse` (Q146).
- The binder, flow analysis, lowering and the C emitter (specs 010–011), and a `bayan` driver built from Bayan (spec 012).
- `import` between `selfhost/` directories or a package layout for the self-hosted compiler (Q143).
- `Owned<T>` node trees, `type distinct` ids and round-3 §9 typed indices (Q144).
- Changing `selfhost/lexer/` beyond what round 3 and Q151 require.

## User stories

- As the author of the binder port, I want the Bayan parser's tree to equal the C# tree on every input, so that a binder disagreement in spec 010 is never a parser bug in disguise.
- As the port author, I want `Name { span: TextSpan, text: Symbol }` in every node, so that scopes in 010 are `Map<Symbol, …>` with no interning pass.
- As the port author, I want nodes addressed by `ExprId { index: i32 }`, so that 010's side tables are `Map<ExprId, TypeId>` (008 R5) and no node borrows its source.
- As a compiler developer, I want a failing test that names the input and the first differing dump line whenever the parsers disagree, so that the two cannot drift.
- As anyone fuzzing `bayan parse`, I want deep nesting to be a diagnostic, not a stack overflow.

## Requirements

- R1 Program. `selfhost/parser/main.byn` is `bayan-parse`: `bayan-parse [<path>|-]` prints the dump of `<path>` (stdin given `-` or no path, named `<stdin>`) and exits 0 when no diagnostic is an error, else 1, as `CliApp.Parse` with `--ast` does (`CliApp.cs:116-130`). `bayan-parse --many <dir>` prints `== <name>` and the dump of every `*.byn` in `<dir>` in name order, preceded by a `span out of range` line for every node, token or diagnostic span outside the input. `bayan-parse --time <path>` prints only the nanoseconds spent lexing and parsing. Exit 2 on a usage error, 4 when the input cannot be read, as `bayan-lex`.
- R2 Source. The parser is written in round-3 syntax (spec and GRAMMAR on this branch): no semicolons, name-first bindings and fields, `->` returns, `@` attributes, particles, `length` for sizes. `bayan fmt --check selfhost/parser` (round-3 5.2b) changes nothing. It is one module with the lexer's library files (every `selfhost/lexer/*.byn` except `main.byn`), unchanged, assembled by the test harness (Q143).
- R3 Coverage. The parser accepts and reports exactly what `Parser.cs` does after the round-3 steps in the Depends-on line: every declaration, statement, expression, type and pattern form, every input-form diagnostic with its fix (BYN1040–1062 as allocated by round 3, BYN1017–1033 where raised), contextual keywords, the newline (waqf) rules, recovery and the `Report` rule that drops a second diagnostic at the same start (`Parser.cs:85-91`).
- R4 Tree. Each node category (declarations, statements, expressions, types, patterns) is a pool `Pool<T>` of `struct XNode { span: TextSpan, kind: XKind }` where `XKind` is a union with one case per C# record; children are single-field id structs (`ExprId { index: i32 }`, `StmtId`, `DeclId`, `TypeId`, `PatternId`) and child lists are `IdRange { start: i32, length: i32 }` into a flat id list per category. Names are `Name { span: TextSpan, text: Symbol }` interned with `Symbol.from` (008 R3); decoded string, character and doc texts are `string`s in the per-file arena. `Pool<T>` conforms to `IIndexable` (006 R9) so the dump and the span check iterate it with `for node in pool`, and every node struct conforms to `ISpanned` (`span: TextSpan`) so one `checkSpans<T>(pool: Pool<T>) where T : ISpanned` serves all five pools (006 R4). No node holds a pointer, an `Owned<T>` or a borrow (Q144, Q145).
- R5 Recovery predicates. Token skipping during recovery takes a `TokenKind -> bool` (007), written at the call as a lambda over `it` (`this.skip(to: it == .closeBrace or it == .newlineStatementStart)`); no lambda captures `this` (007 non-goal).
- R6 Dump. The output is byte-identical to `AstDump.Dump(tree, Style.Plain)`: the same lines, indentation, S-expression forms, modifier order, quoting and escapes (`TokenDump.Escape`, already in `selfhost/lexer/dump.byn`), and after a blank line the diagnostics rendered by the lexer port's `renderAll`, sorted by span start (stable).
- R7 Diagnostics. Every diagnostic the C# parser reports has the same code, span, message, label, help and fix in the Bayan parser, in the same order, with lexer diagnostics first, as `Parser` adds them (`Parser.cs:36`). The parser's message templates live in one table in `selfhost/parser/`, and a test checks that every `BYN1xxx` code in `DiagnosticCodes.cs` has an entry.
- R8 Nesting limit. Both parsers count nesting on entry to the expression, statement, type and pattern parsers and on each prefix operator (unary, `not`, `try`, `move`, `ref`). At more than 256 levels they report BYN1070 at the token that would open level 257, report nothing else for the file, and stop parsing it; the tree holds everything parsed before that token (Q147). The C# parser gains this in T1, before the port.
- R9 Checks in the suite. The normal test run builds `bayan-parse` once per test process (cached as `bayan-lex` is) and compares its output with the expected dump for: every `tests/cases/parse/*.byn` against its `.ast` golden; every `.byn` under `tests/cases`, `examples`, `selfhost` and `tools` against `AstDump.Dump` in process. A disagreement fails the run and names the input and the first differing line.
- R10 Corpus. On a generated corpus of at least 10,000 inputs (Q149), `bayan-parse --many` prints exactly the C# dump for each, exits 0, never panics, prints no `span out of range` line, and finishes each input within the test's timeout.
- R11 Speed. Lexing and parsing a 1 MB file of concatenated parse goldens with `bayan-parse` built in `fast` mode takes no more than 10 times as long as `SyntaxTree.Parse` on the same machine (median of 5; `BAYAN_SKIP_TIMING=1` skips) (Q150).
- R12 Diagnostic docs. BYN1070 has a `docs/diagnostics/BYN1070.md` page, a `diag/` golden, a `parse/` golden (so the Bayan parser's theory covers it) and a verb-first help (spec 005 R15).
- R13 Self-hosting reach. `bayan-parse` on every file of `selfhost/parser/` and `selfhost/lexer/` prints the C# dump, so the parser parses itself; this is part of R9's `selfhost` set and is named in its own test so a failure there is visible.

## Acceptance criteria

- Given `tests/cases/parse/hello.byn`, when `bayan-parse` runs on it, then it prints `CompilationUnit` and `  Expr (call print "Hello, world!")` (or round 3's regenerated `hello.ast`) and exits 0.
- Given every `tests/cases/parse/*.byn`, then `bayan-parse`'s output equals the `.ast` golden byte for byte.
- Given a file with a `;` after a statement (BYN1040) and an `else` on a new line (BYN1041), then the Bayan dump renders both diagnostics with the same carets, help and fix lines as `bayan parse --ast`, and exits 1.
- Given a file where two parse errors start at the same offset, then both parsers report only the first.
- Given 300 nested `(` before `1`, then both parsers report exactly one diagnostic, BYN1070, at the 257th `(`, and the C# test process does not crash; given 100,000 nested `(`, then the same holds.
- Given 256 nested `(` around `1` followed by 256 `)`, then neither parser reports BYN1070.
- Given `let x = 1` on stdin, when `bayan-parse -` runs, then the output equals `bayan parse --ast` on a file with that text.
- Given a directory with three `.byn` files, when `bayan-parse --many <dir>` runs, then it prints three `== <name>` headers in name order, each followed by that file's dump, and no `span out of range` line.
- Given every `.byn` under `tests/cases`, `examples`, `selfhost` and `tools`, then the Bayan dump equals `AstDump.Dump`; in particular for every file of `selfhost/parser/`.
- Given the 10,000-input corpus, then every dump agrees, the run exits 0, stderr has no `panic:`, and no span is outside its input.
- Given a 1 MB file of concatenated parse goldens, then the Bayan median over 5 runs is at most 10 times the C# median.
- Given a `BYN1xxx` code in `DiagnosticCodes.cs` with no entry in the Bayan message table, then the table test fails naming the code.

## Law candidates

- L017 (property, proven like L001): for every input in the parser corpus (every parse golden, every `.byn` in the repository, and the generated corpus of R10), `bayan-parse` on stdin and `AstDump.Dump(SyntaxTree.Parse(source), Style.Plain)` are byte-identical, and `bayan-parse` exits 0 or 1 within the timeout. A mutation in the Bayan parser that swaps two binary precedence levels, or that drops the same-offset rule of `Report`, makes the proof fail.

## Open questions

Each records the conservative choice 009 takes; the handoff gets them in T12.

- Q143 Build layout: `bayan-parse` is one module made of `selfhost/lexer/*.byn` without `main.byn` plus `selfhost/parser/*.byn`, assembled by the test harness with `Compilation.Create`; no `import` (imports resolve under the entry directory, `Compilation.cs:140-144`), no `public` added to lexer declarations, and no copy of lexer code. Conservative: no compiler or lexer change; spec 012 decides the self-hosted compiler's package layout.
- Q144 Tree representation: per-category pools of `{ span, kind }` structs, addressed by single-field id structs, child lists as ranges into flat id lists, built through a scratch stack so nested lists never interleave. Not `Owned<T>`, not `type distinct` ids (round-3 5.3b), not `List<T, RowId>` (round-3 §9, spec-only in v0.1). Conservative: no pointers or borrowed fields (handoff "Regions" decision), and ids are valid `Map` keys under 008 R5 for spec 010.
- Q145 Names: `Name.text` is a `Symbol`; contextual keywords (`state`, `pattern`, `failing`, the particles) are compared as symbols; the dump prints `name.text.text`. Literal, doc and interpolation texts stay `string`. Conservative: 010 keys scopes by `Symbol` (008's user story); text that is never looked up is not interned.
- Q146 What equivalence compares: the `bayan parse --ast` text dump with its rendered diagnostics, byte for byte. `bayan parse` gains no `--json` or span form. Node spans are checked through the `--many` range check, diagnostic carets, and spec 010's `diag/` goldens. Conservative: the C# CLI and the 30 parse goldens are unchanged; a span dump can be added when 010 needs it.
- Q147 Nesting limit: 256 levels, counted as R8 says, BYN1070 once, then the file stops parsing with no further diagnostic. Conservative: both parsers are bounded identically, the C# process cannot overflow, and 256 is far above any golden (T1 measures the deepest file in the repository and records it).
- Q148 Recovery parity: ported 1:1 by method, including `_lastErrorAt`, `_conditionStart` and the lookahead scans; recovery is not improved in 009. Conservative: any recovery change is a C# change first, ported after, so L017 never blocks a fix.
- Q149 Corpus: 10,000 inputs from a fixed xorshift seed, generated in the test (as Q50 for the lexer), from parse goldens and repository `.byn` files: whole lines mixed, token-level deletions, duplications and swaps, byte mutations, unbalanced brackets and braces, stray `;` and `else`, and nesting depths 200–300 to straddle the limit. Conservative: deterministic, and every input is kept in `tmp/parser-corpus/` on failure.
- Q150 Speed: at most 10 times the C# parser on 1 MB, lexing included, median of 5, skippable. Conservative: the same bound spec 001 R10 set for the lexer.
- Q151 Diagnostic shape: the lexer port's `Diagnostic` (`selfhost/lexer/diagnostics.byn:74`) has no severity and no related spans. The C# parser reports neither a warning nor a `RelatedSpan` today (grep at `7cff9b9`); if a round-3 step adds one, T6 adds the field to the lexer's struct and renderer and L001 is re-run. Conservative: the struct grows only when an input needs it.
