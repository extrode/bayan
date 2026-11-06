# 012 plan (trimmed copy of the real file)

- **D2 Reuse, `public` only.** Where the driver needs a lexer or parser function that is module-private (`selfhost/lexer/main.byn:33` `dumpFile`, `selfhost/parser/main.byn:37` `parseOne`, `selfhost/emitter/main.byn:37` `t12Emit`), the task makes it `public` and changes nothing else, as 011 T1 did for semantics.

| File | Ports | Task | Lines |
|---|---|---|---|
| `selfhost/driver/main.byn` | `CliApp.cs` 37-106 (`Run`) | T1 | ~350 |
