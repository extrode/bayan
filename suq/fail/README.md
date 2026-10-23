# Sūq compile-fail corpus

One broken snippet per diagnostic (HANDOFF §21, method 2). Each file starts with `// expect:` (what the compiler must reject) and, where there is one, `// fix-it:`. The snippets are paper: no test runs them yet. They come from `docs/suq-reference-app.zip` (its `fail/` directory, renamed from `*.byn.cs` to `*.byn`), where `tools/check_round3.py --self-test` checks that each is caught by its own rule only.

Codes are the ones SPEC §21.5, the round-3 plan (`docs/plans/2026-10-22-002-design-round-3.md`) or addendum 1 allocate; "(plan)" marks a code the spec does not allocate yet. "None allocated" means no document gives the case a code yet; the snippet waits for one.

| Snippet | Rejects | Code |
|---|---|---|
| `arrow-return.byn` | name-first return `f() -> T` | BYN1043 |
| `attr-case.byn` | PascalCase attribute name `@Derive` | none allocated |
| `bare-this.byn` | bare `this` receiver | BYN1061 |
| `bare-unit.byn` | bare literal for a unit type | BYN3203 |
| `block-comment.byn` | `/* … */` comment | BYN1047 |
| `bracket-attr.byn` | `[Attr]` attribute | BYN1045 |
| `byte-count.byn` | byte count by dividing by `1B` | none allocated |
| `case-except.byn` | `case _ except .x` | BYN1051 |
| `char.byn` | `char` (use `rune`) | BYN1048 |
| `clause-order.byn` | signature clauses out of order | BYN1065 |
| `context-block.byn` | `context (…)` block | BYN6006 |
| `csharp-alias.byn` | C# alias type name `int` | BYN1030 |
| `dangling-blank.byn` | statement continues into a blank line | none allocated |
| `drop-ref-this.byn` | `drop(ref this)` cleanup | none allocated |
| `else-newline.byn` | `else` at the start of a line | BYN1041 |
| `extern-string.byn` | `extern "lib"` block | none allocated |
| `fn-type-arrow.byn` | `=>` in a function type | BYN1055 |
| `foreach.byn` | `foreach` | BYN1019 |
| `get-property.byn` | interface property `{ get }` | BYN1060 |
| `in-convert.byn` | removed unit conversion `.in(Unit)` | none allocated |
| `keyword-name.byn` | keyword used as a name | BYN0012 |
| `leading-operator.byn` | line starts with a binary operator | none allocated |
| `log-at.byn` | log level passed with `at:` | none allocated |
| `log-level-member.byn` | `Log.debug(…)` | BYN5007 |
| `missing-because.byn` | bypass without `because` | BYN8001 |
| `mode-particle.byn` | particle after the passing mode (`ref from`) | none allocated |
| `modifier-first.byn` | `unique struct` | BYN1044 |
| `name-first-field.byn` | name-first field `sku: Sku` | BYN1043 |
| `name-first-param.byn` | name-first parameter `line: Line` | BYN1043 |
| `name-first-payload.byn` | name-first payload `timeout(after: Millis)` | BYN1043 |
| `noalloc.byn` | removed effect keyword `noalloc` | BYN6001 |
| `not-a-particle.byn` | `into` is not a particle | BYN5008 (plan) |
| `null-compare.byn` | null check with `!=` | BYN2027 |
| `on-field.byn` | `requires … on .field` | none allocated |
| `on-prefix.byn` | `onX` field name | BYN5010 |
| `owned-copied.byn` | `owned()` / `copied()` | none allocated |
| `paren-condition.byn` | parenthesized `if` condition | BYN1017 |
| `paren-for.byn` | `for (let x in xs)` | BYN1019 |
| `repeated-particle.byn` | the same particle twice | BYN5013 (plan) |
| `requires-value.byn` | `requires value …` | BYN1050 |
| `semicolon.byn` | trailing `;` | BYN1040 |
| `state-line.byn` | per-line `state x` | BYN1059 |
| `string-plus.byn` | string `+` | BYN2209 |
| `symbol-bool.byn` | symbol boolean in a `.words` project | none (`bayan format` only; SPEC §21.5) |
| `to-as-call.byn` | call to a builtin `asX` member (`asSpan()`) | BYN2230 |
| `to-member.byn` | declared `toX` member | BYN5009 |
| `tostring.byn` | `toString()` | BYN2031 |
| `try-catch.byn` | `try` and `catch` on the same call | none allocated |
| `try-literal.byn` | `try` on a literal construction | none allocated |
| `two-objects.byn` | two unlabeled parameters | BYN5004 |
| `type-first.byn` | type-first local binding `let Grams w` | BYN1043 |
| `unit-round.byn` | removed unit rounding `roundUp()` | none allocated |
| `using-let.byn` | `using let` | none allocated |
