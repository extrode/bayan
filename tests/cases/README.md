# Golden test cases

One input per case, expected output beside it. `dotnet test` compares; `BAYAN_UPDATE=1 dotnet test` rewrites the expected files (review the diff like code).

| Dir | Input | Expected |
|---|---|---|
| `lex/` | `*.byn` | `*.tokens` |
| `parse/` | `*.byn` | `*.ast` |
| `diag/` | `*.byn` | `*.diag` (exact rendered text) |
| `run/` | `*.byn` | `*.stdout`, optional `*.exitcode` |

Every new diagnostic needs a `diag/` case; every runtime feature needs a `run/` case. The harness is `tests/Bayan.Tests/Golden.cs`.
