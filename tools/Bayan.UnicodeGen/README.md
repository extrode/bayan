# Bayan.UnicodeGen

Generates `selfhost/lexer/unicode-tables.byn` and `src/Bayan.Syntax/Lexing/UnicodeTables.g.cs`
from the same UCD data: identifier-class ranges and the NFC data (CCC, full canonical decompositions,
composition pairs) that both lexers use for identifiers and the BYN0005 NFC check.
`src/Bayan.Syntax/Lexing/Nfc.cs` is the algorithm the C# lexer runs and the Bayan port mirrors method
by method; the generator compiles that same file.

## Regenerate

From the repository root:

```
dotnet run --project tools/Bayan.UnicodeGen -- --unicode 16.0.0 --out selfhost/lexer/unicode-tables.byn --cs-out src/Bayan.Syntax/Lexing/UnicodeTables.g.cs
```

The UCD files are downloaded once from `https://www.unicode.org/Public/<version>/ucd/` into
`tmp/ucd/<version>/`. Output is deterministic; commit both regenerated files.

Before writing, the generator checks that (a) the identifier-start and identifier-continue sets from
`UnicodeData.txt` equal those from `Rune.GetUnicodeCategory` over every scalar value, and (b) `Nfc.cs`
driven by the tables agrees with `string.Normalize(FormC)` and `IsNormalized(FormC)` for every
identifier code point, every composition pair and 5000 random identifier strings. It exits non-zero
naming the first disagreement.

## Why the version is pinned

Both lexers read the same generated tables, so they agree by construction; the C# lexer no longer asks
the .NET runtime or ICU. The assertions pin the tables to the Unicode version this runtime uses, so
identifier classes and NFC match what `Rune.GetUnicodeCategory` and `string.Normalize` give. Lexer
equivalence holds per pinned version (Q49). The tables change only when someone reruns this deliberately; if a
new runtime or ICU moves to another Unicode version, the assertions fail when someone regenerates.
Pick the version that passes: with .NET 10.0.12 on macOS that is 16.0.0
(15.1.0 and 17.0.0 fail assertion (a)).
