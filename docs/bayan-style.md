# Bayan style guide

Style is guidance: never compiler-enforced, configurable per project, applied by `bayan fmt`. Grammar rules ([GRAMMAR](bayan-grammar.md)) are not style and cannot be configured away.

## Defaults

| Choice | Default |
|---|---|
| Braces | Same line |
| Indent | 4 spaces |
| Line length | 100 |
| Trailing commas | Always in multi-line literals and argument lists |
| Member order | Fields, typestate `state` declarations, properties, constructors, methods |
| Statements | One per line (postfix guards count as one) |
| Booleans | One spelling per project, set by `booleans`: `.symbols` (`!` `&&` `\|\|`) or `.words` (`not` `and` `or`) |

## Configuration

Configuration lives in `package.byn` as a Bayan struct literal:

```csharp
Package {
    name: "local/shop",
    version: "0.1.0",
    edition: 2027,
    kind: .app,
    capabilities: [.net],
    deps: [],
    style: Style {
        indent: 4,
        braces: .sameLine,        // or .nextLine
        maxLine: 100,
        trailingCommas: true,
        booleans: .symbols,       // or .words
    },
}
```
