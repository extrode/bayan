# Bayan style guide

Style is guidance: never compiler-enforced, configurable per project, applied by `bayan format`. Grammar rules ([GRAMMAR](bayan-grammar.md)) are not style and cannot be configured away.

## Defaults

| Choice | Default |
|---|---|
| Braces | Same line |
| Indent | 4 spaces |
| Line length | 100 |
| Trailing commas | Always in multi-line literals and argument lists |
| Member order | Fields, the `states` block, events, properties, static factories, methods |
| Signature clauses | `where`, `becomes`, `fails`, `uses` (`bayan format` reorders; BYN1065) |
| Statements | One per line (postfix guards count as one) |
| Booleans | One spelling per project, set by `booleans`: `.symbols` (`!` `&&` `\|\|`) or `.words` (`not` `and` `or`) |
| Primitives | One spelling per project, set by `primitives`: `.sized` (`i32 i64 u8 f64 …`, the default) or `.csharp` (`int long byte double …`); `char`, `decimal` and `object` are not primitives under either |

## Declarations and bindings

Not configurable: declarations are type-first and local bindings name-first ([GRAMMAR §5](bayan-grammar.md)). `bayan format` rewrites the other order (BYN1043):

| Before | After |
|---|---|
| `public shippingWeight(order: Order) -> Grams { … }` | `public Grams shippingWeight(Order order) { … }` |
| `public ship(ref this, line: Line) { … }` | `public void ship(ref this, Line line) { … }` |
| `sku: Sku` (field) | `Sku sku` |
| `timeout(after: Millis)` (payload) | `timeout(Millis after)` |
| `Grams w = 0g` (in a function body) | `let w: Grams = 0g` |

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
        primitives: .sized,       // or .csharp
    },
}
```
