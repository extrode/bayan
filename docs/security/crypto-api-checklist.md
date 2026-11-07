# Crypto and auth API checklist

Date: 2026-11-07. Source: plan `docs/plans/2026-11-07-006-in-band-errors-and-c-boundary.md` (the sshole lessons: an error returned as a valid-looking value).

The first `std.crypto` spec must cite each item below by number, with a golden for each. No code exists yet (`Secret` is absent from `Builtins.cs`; spec §15.2-15.3 are spec-only), so these rules are written before the first line.

1. **No magic-string states.** A password-hash API returns `union error HashError { tooLong(i64 max), unsupportedAlgorithm, … }` and a distinct `PasswordHash` type. "Locked" is a field or variant of the account, never a hash value.
2. **Over-limit input fails.** Input above a limit is `fail .tooLong`: never truncated, never hashed as a sentinel. The limit is a named constant in the signature's doc.
3. **Fail closed by construction.** Verification returns `bool` only through `Secret<T>.equalsConstantTime` (spec `:1719`). The function that decides access takes a `Verified` typestate (spec §3.17), so a caller cannot pass a raw `bool` it computed itself.
4. **Secrets are typed.** Constant-time paths take `Secret<T>` parameters. A `string` password argument is BYN8xxx (the `Untrusted` sink rule, spec `:1691`, extended to a "secret sink").
5. **Randomness only through `CryptoRandom`** (spec `:1726`).
6. **Libc and OS primitives are audited first.** Any primitive used underneath (`crypt`, `getentropy`, `BCrypt*`) goes through the runtime libc audit table (`docs/security/runtime-libc-audit.md`, plan W1) before use.

Also: add a section with these rules to `docs/bayan-library-tiers.md` (handoff Question, not an edit here).
