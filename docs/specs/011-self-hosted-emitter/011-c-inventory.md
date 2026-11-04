# 011 K2 inventory: compiler extensions in the emitted C

R4. Every hit of the token regex in `tests/Bayan.Tests/CInventoryTests.cs` is one row of the table below, and every row has a hit; the test fails on either gap. Scope: `src/runtime/bayan-rt.{h,c}` as C, `src/Bayan.Backend.C/CEmitter.cs` and `selfhost/emitter/cprint/*.byn` over their string literals only (interpolation holes removed; Q828). An `__attribute__((a, b))` list is one row per item. `alloca` and the other regex tokens without a row have no use. Line numbers are as of T15; a moved line is a test failure to fix here.

| Site | Token | Why it is needed |
|---|---|---|
| src/runtime/bayan-rt.h:18 | `__attribute__((noreturn))` | Marks `BYN_NORETURN` panics so the compiler drops dead code after them. C11 replacement: `_Noreturn`. |
| src/runtime/bayan-rt.h:19 | `__attribute__((cold))` | Marks `BYN_COLD` failure paths unlikely. C11 replacement: none needed, drop it (hint only). |
| src/runtime/bayan-rt.h:20 | `__builtin_expect` | `BYN_UNLIKELY` branch hint. C11 replacement: none needed, `#define BYN_UNLIKELY(x) (x)`. |
| src/runtime/bayan-rt.h:482 | `__builtin_add_overflow` | Checked `+` traps on overflow. C11 replacement: range test before the add in unsigned arithmetic. |
| src/runtime/bayan-rt.h:484 | `__builtin_sub_overflow` | Checked `-` traps on overflow. C11 replacement: range test before the subtract. |
| src/runtime/bayan-rt.h:486 | `__builtin_mul_overflow` | Checked `*` traps on overflow. C11 replacement: division-based range test or a wider type. |
| src/runtime/bayan-rt.h:488 | `__builtin_sub_overflow` | Checked negation as `0 - a`. C11 replacement: `a == MIN` test. |
| src/runtime/bayan-rt.h:510 | `__builtin_add_overflow` | Saturating `+` needs the overflow flag. C11 replacement: as 482. |
| src/runtime/bayan-rt.h:512 | `__builtin_sub_overflow` | Saturating `-` needs the overflow flag. C11 replacement: as 484. |
| src/runtime/bayan-rt.h:514 | `__builtin_mul_overflow` | Saturating `*` needs the overflow flag. C11 replacement: as 486. |
| src/Bayan.Backend.C/CEmitter.cs:47 | `asm` | In the reserved-identifier list; reserved word, not a use; no replacement needed. |
| src/Bayan.Backend.C/CEmitter.cs:47 | `typeof` | In the reserved-identifier list; reserved word, not a use; no replacement needed. |
| selfhost/emitter/cprint/cctx.byn:66 | `asm` | Port of `CEmitter.cs:47`'s reserved-identifier list; reserved word, not a use; no replacement needed. |
| selfhost/emitter/cprint/cctx.byn:66 | `typeof` | Port of `CEmitter.cs:47`'s reserved-identifier list; reserved word, not a use; no replacement needed. |
| src/Bayan.Backend.C/CEmitter.cs:82 | `__attribute__((unused))` | Emitted on `byn_w_error` so an unused static does not warn. C11 replacement: `(void)byn_w_error;` or drop it. |
| selfhost/emitter/cprint/program.byn:28 | `__attribute__((unused))` | Port of `CEmitter.cs:82` on `byn_w_error`; same C11 replacement. |

## Platform guards (not extensions, not checked by the regex)

Predefined macros the runtime and emitter test, listed for 012: `_WIN32` (15 in `bayan-rt.c`), `__wasi__`, `__APPLE__`, `__linux__`, `__GNUC__`, `__clang__` (`bayan-rt.h:17-25` selects the attribute and builtin spellings above).

## Direct libc calls in emitted C (012 T8, Q7)

The C that `CliApp.EmitC` gives for every `tests/cases/run` input (all three modes; a `*.test.byn` as `bayan test` builds it) calls these library functions directly, not through `byn_*`. They are rows for 012, not a fix: no emitted C changes (Q7). `SelfhostDriverT8Tests` (in SelfhostDriverInventoryTests.cs, named for the guard filter) fails on a call missing here or listed without a call.

- `atoi`
- `memcpy`
- `memmove`
- `strcmp`
