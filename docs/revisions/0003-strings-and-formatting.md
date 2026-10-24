# 0003 — Strings, culture and formatting

Status: approved 2026-10-24 (user). Date: 2026-10-24. Applies to `SPEC.md`, `GRAMMAR.md`, `STYLE.md`, the HANDOFF `docs/bayan-design-round-3.md` and the plan `docs/plans/2026-10-22-002-design-round-3.md`. Amends revision 0002 item 24 and HANDOFF O13.

## Problem

1. Revision 0002 item 24 made `string(bytes)` decode UTF-8 and removed formatting for byte types. In generic code only the `Display` bound is known, so `string(item)` would format there and decode in a concrete call: one expression, two results.
2. The string API, culture rules and formatting design (round 2's pending `format()` item) were scattered or missing.

## Decision

### 1. Decoding is a codec; `string(x)` always formats

Amends 0002 item 24 and O13. Every binary↔text conversion is a codec, like `Hex` and `Base64` (O14); `string(x)` is only the `Display` conversion, for every type, including byte spans (`[104, 105]`).

```csharp
let s = try Utf8.decode(bytes)                   // fails Utf8Error with the position of the bad byte
let s = Utf8.tryDecode(bytes)                    // string?
let s = Utf8.decode(bytes, invalid: .replace)    // lossy: U+FFFD, never fails
let s = try Latin1.decode(bytes)                 // kit
let s = try Utf16.decode(units)                  // std.text
let hex = Hex.encode(digest)
let raw = try Base64.decode(text)
```

`string.fromUtf8`, `try string(bytes)`, `string.tryFrom(bytes)` and `string(bytes, as: .latin1)` are renamed under BYN2230 to the codec forms.

### 2. Grammar: what names stand for

- **Instance members:** a noun or an adjective names a result (`s.length`, `s.position("x")`, `s.isEmpty`); a verb names an act on the receiver and takes `ref this` (`buf.append(x)`); `-ed` names a changed copy (`s.trimmed()`).
- **Assertion verbs** are queries: third-person verbs that state a fact about the receiver and return `bool` (`contains`, `startsWith`, `endsWith`, `matches`, `equals`). GRAMMAR lists them as a closed group; adding one is a GRAMMAR change.
- **Static and free functions** have no receiver, so they name acts and are verbs: `print(x)`, `copy(from:to:)`, `Utf8.decode(b)`, `Hex.encode(d)`.
- **Fallible pairs:** a base function `fails` with detail (`T.parse(text)`, `Utf8.decode(b)`), its derived `tryX` returns `T?` (`T.tryParse`, `Utf8.tryDecode`), and the keyword `try` marks every call to the base form. Both forms always exist.

### 3. The string model

- `string` is immutable UTF-8. It either borrows its bytes (a literal, a request body, a slice) or owns them; `s.copy()` and `s.copy(to: arena)` make owned copies (A5).
- `s.length` is the byte length, O(1). There is no integer indexer; `rune` is the 32-bit Unicode scalar.
- A string has no mutating members; text is built with `StrBuf`, interpolation or `joined`. String `+` stays deferred.
- An `-ed` result that is a sub-range of the original (`trimmed()`, `parts`, slices) is a borrowed view and never allocates. Only results with new text allocate.
- `$"…"` allocates, except passed directly to a formatting sink: `print`, `log`, `panic`, `StrBuf.append`.

### 4. The API

**Queries and views**

| Member | Returns | Alloc | Notes |
|---|---|---|---|
| `length` | `i64` | no | Bytes |
| `isEmpty`, `isAscii` | `bool` | no | |
| `runeCount` | `i64` | no | O(n) |
| `bytes`, `runes`, `utf16` | views | no | `bytes` is a `ReadOnlySpan<u8>` |
| `lines` | view of `string` | no | `\n` and `\r\n` |
| `parts(by: sep)`, `parts(by: sep, limit: n)` | view of `string` | no | Splitting |
| `s[a..b]` | `string` (borrowed) | no | Byte range; traps when not on a rune boundary |
| `position(x)`, `position(x, from: i)`, `lastPosition(x)` | `i64?` | no | Byte position of the first or last occurrence |
| `count(x)` | `i64` | no | Non-overlapping occurrences |
| `words`, `graphemes` | views | no | `std.text` (Unicode segmentation) |

**Assertions:** `contains(x)`, `startsWith(x)`, `endsWith(x)`, `matches(re)`, `equals(t, as: .caseless)`. Plain equality is `==`.

**Copies**

| Member | Alloc | Notes |
|---|---|---|
| `trimmed()`, `trimmed(from: .start / .end)`, `trimmed(by: [' ', '\t'])` | no | View |
| `lowercased()`, `uppercased()`, `caseFolded()` | yes | Invariant |
| `camelCased()`, `pascalCased()`, `snakeCased()`, `kebabCased()`, `slugged()` | yes | Identifier and URL forms; derivations use them |
| `replaced(x, by: y)`, `replaced(re, by: y)` | yes | |
| `truncated(to: n)`, `truncated(to: n, by: "…")` | yes | Cuts on a grapheme boundary |
| `padded(to: n, by: ' ')` | yes | `width:` in a spec is usually better |
| `wrapped(at: 80)` | yes | |
| `repeated(n)` | yes | |
| `escaped(for: .json / .csv / .shell)` | yes | Display escaping; sinks stay typed (`Html`, `Sql`, `Arg`) |
| `normalized(as: .nfc / .nfd)` | yes | `std.text` |
| `xs.joined(by: sep)` | yes | On a collection of strings |

Prefix and suffix views (`prefix(through:)`, `dropped(first:)`) are not named yet: the Sūq friction log decides, and byte slices cover them meanwhile.

**Building: `StrBuf`**

```csharp
var buf = StrBuf(capacity: 256)
buf.append($"order {order.id}: ")       // formats straight into the buffer
buf.append(total)                        // any Display value
buf.insert("!", at: 0)
buf.clear()
let text = buf.finish()                  // move this: the buffer becomes the string, no copy
```

**Conversions:** `string(x)` (`Display`), `string(n, as: .hex / .binary / .octal / .decimal)`, `T.parse(text)` / `T.tryParse(text)` with the same `as:`, and the codecs of §1.

**Literals:** `"…"`, `$"…"`, `b"…"` (bytes), `sql"…"` with `$n`. `html"…"` and interpolating `sql"…{x}…"` stay deferred. The raw multi-line literal `"""…"""` stays as SPEC §2.3 defines it (revision 0004 item 3).

### 5. Culture

- **No ambient culture.** No operation reads a global or per-thread locale; a call's result never depends on the machine.
- **`std` is ordinal or invariant:** `==`, `<`, `compare`, `contains`, `startsWith`, `endsWith`, `position` and `count` compare bytes (UTF-8 byte order is code-point order). `lowercased()` and `uppercased()` use Unicode's invariant mappings (`"I".lowercased()` is always `"i"`). `equals(t, as: .caseless)` uses Unicode case folding without the Turkic special cases.
- **Culture is explicit, through `kit.intl`,** with the `for` particle:
  ```csharp
  let tr = Culture("tr-TR")
  "FILE".lowercased(for: tr)                  // "fıle"
  names.sorted(by: Collator(for: tr))          // dictionary order
  let shown = string(price, for: Culture("ms-MY"))
  ```
  `titlecased()` exists only in `kit.intl`, because title case is culture-dependent.
- STYLE: `std` operations for protocols, keys and identifiers; `kit.intl` for text shown to people.

### 6. Formatting

`Display` has one required member, in the `xInto` pattern:

```csharp
public interface Display {
    void formatInto(ref StrBuf out, as FormatSpec spec)
}
```

`string(x)`, interpolation and `StrBuf.append(x)` all call it; `@derive(Display)` generates it for plain structs.

**Specs are named arguments inside the interpolation hole** (canonical):

| Spec | Applies to | Example |
|---|---|---|
| `as: .hex / .binary / .octal / .decimal` | Integers | `{n, as: .hex}` → `ff` |
| `digits: n` | Floats, `Money` | `{x, digits: 2}` → `3.14` |
| `width: n`, `fill: c`, `align: .start / .end / .center` | All | `{id, width: 6, fill: '0'}` → `000042` |
| `grouping: true` | Numbers | `1,234,567` (invariant) |
| `case: .upper` | Hex, booleans | `FF` |
| `as: .iso / .date / .time` | Dates and times | `2026-10-24T09:30:00Z` |
| `as: .debug` | All | Developer view: quoted strings, field names, `[104, 105]` |
| `for: culture` | Numbers, dates, `Money` | `kit.intl` only |

**Compact input forms**, expanded by `bayan format` (BYN1068): `{n:hex}`, `{n:binary}`, `{n:octal}`, `{x:.2}`, `{d:iso}`, `{x:debug}`.

## Sections

- SPEC: new §16.x "Text" (§3–§6 above); §6.2 conversions (codecs, `string(x)` formats only); §3.x literals; §21.5 row for BYN1068.
- GRAMMAR §1: instance vs static naming, assertion verbs as a closed group, fallible pairs.
- STYLE: the `std` vs `kit.intl` rule; `width:` over `padded`.
- HANDOFF §27 "Text views and number formatting": replace the decoding bullet with §1; add a pointer to the SPEC text chapter. HANDOFF O13 reads "superseded by revision 0003".
- Plan: §1 entry "Text (revision 0003)"; J82 rewritten to the codec renames; 5.1e parses named spec arguments and the compact forms; 5.4 implements the v0.1 members.

## Codes

- New: BYN1068 `CompactFormatSpec` "`{0}` is an input form; write `{1}`" (label at the spec; help "write the spec as named arguments: `{1}`"; fix yes). 5.1e.
- Reused: BYN2230 for `fromUtf8`, `try string(bytes)`, `string.tryFrom(bytes)`, `offset` → `position` and the other renames.
- Totals: 78 new (28 syntax, 18 types, 10 grammar, 7 effects, 9 typestate, 6 security), 12 reserved, 3 retired.

## v0.1 scope

- **v0.1:** the model; `length`, `isEmpty`, `isAscii`, `runeCount`, `bytes`, `runes`, `utf16`, `lines`, `parts`, slices, `position`, `lastPosition`, `count`; the assertions (`equals(as: .caseless)` ASCII-only until `std.text`); `trimmed`, ASCII `lowercased`/`uppercased`, `replaced` (literal), `joined`, `repeated`; `StrBuf`; `string(x)`, `string(n, as:)`, `parse`/`tryParse`; `Utf8` and `Hex`; `Display.formatInto` and the specs `as`, `width`, `fill`, `align`, `digits`, `debug`; BYN1068.
- **After v0.1 (`std.text`):** `words`, `graphemes`, full Unicode case mapping and folding, `normalized`, `truncated` (needs graphemes), `wrapped`, the identifier-case members, `slugged`, `escaped`, `Utf16.decode`, `Base64`, regex `replaced`, `grouping`, date specs.
- **`kit`:** `kit.intl` (`Culture`, `Collator`, `for:` on case, formatting and sorting, `titlecased`), `Latin1` and other encodings.

## Sūq files

The regeneration (C46) uses these forms: `Utf8.decode` for webhook and request bodies, `Hex.encode` for HMAC output, named specs in interpolation, `escaped(for: .csv)` for exports.
