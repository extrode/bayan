# Bayan.UnicodeGen

Generates `selfhost/lexer/unicode-tables.byn`: identifier-class ranges and the NFC data
(CCC, full canonical decompositions, composition pairs) that the self-hosted lexer uses for
identifiers and the BYN0005 NFC check. `Nfc.cs` is the reference algorithm the Bayan port mirrors
method by method.

## Regenerate

From the repository root:

```
dotnet run --project tools/Bayan.UnicodeGen -- --unicode 16.0.0 --out selfhost/lexer/unicode-tables.byn
```

The UCD files are downloaded once from `https://www.unicode.org/Public/<version>/ucd/` into
`tmp/ucd/<version>/`. Output is deterministic; commit the regenerated file.

Before writing, the generator checks that (a) the identifier-start and identifier-continue sets from
`UnicodeData.txt` equal those from `Rune.GetUnicodeCategory` over every scalar value, and (b) `Nfc.cs`
driven by the tables agrees with `string.Normalize(FormC)` and `IsNormalized(FormC)` for every
identifier code point, every composition pair and 5000 random identifier strings. It exits non-zero
naming the first disagreement.

## Why the version is pinned

The C# lexer takes categories from the .NET runtime and its NFC answer from system ICU (handoff Q6),
so the self-hosted lexer can only agree with it for one Unicode version at a time. Lexer equivalence
holds per pinned version (Q49). The tables change only when someone reruns this deliberately; if a
new runtime or ICU moves to another Unicode version, the assertions fail instead of the two lexers
silently disagreeing. Pick the version that passes: with .NET 10.0.12 on macOS that is 16.0.0
(15.1.0 and 17.0.0 fail assertion (a)).
