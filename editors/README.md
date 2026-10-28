# HANDOFF: Bayan editor tooling (v0.0.1)

Adds editor support for `.byn` in VS Code and Visual Studio 2022. Phase 1 is done and runs without
the compiler; phases 2–4 connect it to the compiler. Spec authority: round 3 + revisions 0002–0007.

## 1. What exists

| Path | What | Status |
| --- | --- | --- |
| `editors/vscode/syntaxes/bayan.tmLanguage.json` | TextMate grammar, shared by VS Code and VS | Verified with vscode-textmate on `samples/shop.byn` |
| `editors/vscode/language-configuration.json` | `//` toggle, brackets, auto-close, `///` continuation | Done |
| `editors/vscode/snippets/bayan.json` | Snippets in canonical forms (type-first, no parens, `union error`) | Done |
| `editors/vscode/extension.js`, `package.json` | LSP client (stdio), `bayan` debug type, `.byn` breakpoints | Packaged to `.vsix` |
| `src/Bayan.LanguageServer` | `bayan-lsp`, NativeAOT, hand-rolled JSON-RPC, no LSP library | Builds, AOT publish clean (2.2 MB), smoke-tested |
| `editors/visualstudio` | TextMate drop-in script; VisualStudio.Extensibility LSP skeleton | Skeleton not built (needs Windows) |
| `tests/` | `grammar-dump.js` (scopes per token), `lsp-smoke.js` (protocol driver), `lsp-regress.js` (asserting edge cases) | `npm install`, then `npm run grammar` / `npm run lsp` / `npm run regress` |

Server features today: incremental sync, lexical diagnostics (BYN1040 `;`, BYN1047 `/* */`,
unclosed literals), completion (locals in scope, members after `.`, target-typed `.case`/`.state`,
`@` attributes, keywords, primitives, builtins), hover (header + `///` docs, keyword docs), go to
definition (locals, params, fields, methods, cases, states, types, across files), document outline,
workspace symbols, signature help (receiver skipped, multi-line calls), lazy completion docs
(`completionItem/resolve`), context-narrowed `.x` completion (`fail .` → error cases, `is .`/`becomes .` → states).
Workspace lookups go through a lazily rebuilt symbol index (by name, by container, types). Type flow is shallow: declared types, `let x =
Type {…}`, and member chains `a.b().c`.

## 2. Gate rules

- The LSP server never forks the compiler's rules. Once phase 2 lands, `Analysis/Lexer.cs` and
  `Analysis/Indexer.cs` are deleted, not kept in sync.
- Messages use the compiler's plain-English text and codes; the server adds none of its own after
  phase 2.
- Zero-allocation where it is cheap: no LINQ in request paths, `Utf8JsonWriter` for output,
  `"…"u8` property names. No reflection (AOT).

## 3. Phase 2: compiler front end in the server

1. Move `src/Bayan.LanguageServer` into the compiler solution. Reference `Bayan.Syntax` and
   `Bayan.Semantics`.
2. Replace `Document.Analyze` with a `Compilation` over the workspace (one per `package.byn`).
   Re-bind only changed files where the binder allows; otherwise debounce 150 ms after the last edit.
3. Diagnostics: map `DiagnosticBag` entries (code, message, label span, help) to LSP; `help` goes
   into `relatedInformation` or the message's second line; set `codeDescription.href` to the code's
   docs page.
4. Code actions: every diagnostic with "fix: yes" (BYN1043, 1044, 1053, 1061, 1065, 3209, …)
   becomes a `quickfix`. The fix edits come from the compiler; the server only transports them.
5. Completion, hover, definition, signature help from bound symbols: exact types, overloads by type
   and particle, effects (`uses …` shown in hover), states, `fails` sets. Keep the current
   response shapes so the clients do not change.
6. Keyword table: delete `Analysis/Keywords.cs` and read the compiler's `Keywords.cs`. Add a test
   that every compiler keyword appears in the TextMate grammar's keyword regexes.
7. Add `references`, `rename` (respecting the grammar checker's naming rules), `formatting` (calls
   `bayan format` in-process, honouring `package.byn` `style`), and `semanticTokens` (fields vs
   locals vs states vs effects; the grammar stays as fallback colouring).

## 4. Phase 3: `bayan lsp`

Host the same server as `bayan lsp` (stdio). The VS Code setting becomes
`"bayan.server.path": "bayan", "bayan.server.args": ["lsp"]`. Keep `bayan-lsp` as an alias.

## 5. Phase 4: debugging

The extension already registers a `bayan` debug type and `.byn` breakpoints; it delegates to a
native debugger. The compiler must provide:

1. `bayan build --debug --print-output`: C compiled with `-g -O0` (MSVC: `/Zi /Od`), `#line` kept
   (J93), absolute executable path as the last stdout line.
2. Debug-friendly C names: user locals and fields keep their Bayan names in C where legal
   (`order`, not `_b_order_3`); document the mangling for the rest so watch expressions work.
3. Pretty printers, later: LLDB Python formatters and a `.natvis` for `string` (UTF-8 ptr+len),
   spans, unions (tag + payload) and typestate tags, emitted next to the binary.
4. A Bayan DAP adapter is not needed until expression evaluation in Bayan syntax is wanted.

## 6. Visual Studio

`install-textmate.ps1` gives colouring now. `Bayan.VisualStudio` starts `bayan-lsp` through
`LanguageServerProvider`; build it on Windows, fix package versions against NuGet, then test
completion, hover, go to definition and diagnostics against `samples/shop.byn`.

## 7. Known gaps (phase 1)

- Name-first input forms (`f() -> T`, `x: T` params) are not indexed; they are diagnostics later.
- Overloads collapse to the first declaration in hover and definition.
- Builtin member tables are hand-written from revision 0003 and SPEC §16; they go away in phase 2.
- `import` and module visibility are ignored: every workspace symbol is visible.
