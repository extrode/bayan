# Typed dump: `bayan types`

`bayan types <path>` compiles a program the way `bayan check` does (a project directory, a script, or a file or directory of `.byn` files) and prints, for every function, the resolved type of each parameter and local. Diagnostics go to stderr. A program with errors prints nothing to stdout and exits 1.

The output is indented JSON written by `TypedDump.ToJson` (`src/Bayan.Semantics/Binding/TypedDump.cs`). Golden cases live in `tests/cases/typed/` (`*.byn` → `*.types`).

## Form

```json
{
  "version": 1,
  "functions": [
    {
      "path": "main.byn",
      "name": "count",
      "span": { "start": 4, "end": 9 },
      "bindings": [
        { "name": "xs", "kind": "param", "span": { "start": 28, "end": 30 }, "type": "ReadOnlySpan<i64>" },
        { "name": "i", "kind": "var", "span": { "start": 42, "end": 43 }, "type": "i64" }
      ]
    }
  ]
}
```

(The real output puts every member on its own line.)

| Field | Meaning |
|---|---|
| `version` | Format version, `1`. A change to the fields or ordering bumps it. |
| `functions[].path` | The file declaring the function, relative to the compilation root (the project root in a project, otherwise the entry directory), with forward slashes. |
| `functions[].name` | The function's name; a member is `Owner.name`. Top-level statements are `main`, test blocks `test`. |
| `functions[].span` | Byte offsets (`start` inclusive, `end` exclusive) of the function's declaring name, or of the first statement or test for `main` and `test`. |
| `bindings[].name` | The name as written (`_` included). |
| `bindings[].kind` | `param`; `var`, `let` or `const` for a declaration (a typed declaration without a keyword is `var`, as is a `var` pattern binding); `for` for a `for` variable; `pattern` for a `let` pattern or type-pattern binding; `catch` for the error bound by `defer failing (e)`. |
| `bindings[].span` | Byte offsets of the binding's name. |
| `bindings[].type` | The resolved type as diagnostics display it (`i64`, `List<string>`, `T`). An unconstrained integer literal is `i32`; one constrained to another integer type by a comparison, an argument or an assignment has that type. |

Compiler temporaries are left out, as are generic instances: a generic function appears once, as its template, with type parameters by name.

## Ordering and determinism

- Modules in compilation order (dependencies before dependents), then files in module order (sorted by name), then functions by source position.
- Bindings in bind order: parameters first, in declaration order, then locals as the body binds them.
- No addresses, hash-order iteration, timings or run-specific IDs; symbols are identified by path and span. Two runs on the same input are byte-identical.
