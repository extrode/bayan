# Golden test cases

One input per case, expected output beside it. `dotnet test` compares; `BAYAN_UPDATE=1 dotnet test` rewrites the expected files (review the diff like code).

| Dir | Input | Expected |
|---|---|---|
| `lex/` | `*.byn` | `*.tokens` |
| `parse/` | `*.byn` | `*.ast` |
| `diag/` | `*.byn` | `*.diag` (exact rendered text) |
| `diag-json/` | `*.byn` | `*.jsonl` (`--diagnostics json` lines) |
| `typed/` | `*.byn` | `*.types` ([`bayan types`](../../docs/typed-dump.md) JSON) |
| `run/` | `*.byn`, optional `*.args`, `*.stdin` | `*.stdout`, optional `*.stderr`, `*.exitcode` |
| `manifest/` | `*.byn` (a `package.byn`) | `*.diag` |
| `new/` | `bayan new` options in `ScaffoldTests` | `*.tree` (every file and its contents) |
| `out/` | projects made in `OutputTests` | `out.tree`, `dist.tree` (paths, with JSON and checksum files masked for timings and hashes), `results-fail.json` |

Every new diagnostic needs a `diag/` case; every runtime feature needs a `run/` case. The harness is `tests/Bayan.Tests/Golden.cs`.
