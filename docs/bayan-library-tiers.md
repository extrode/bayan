# Bayān Library Tiers: core, std and kit

Nov 2, 2026

## 1. Purpose and status

This doc is the single source of truth for where every Bayān library API lives: `core`, `std`, `kit`, an official package, or the ecosystem. It has two jobs:

1. **Now:** guide spec 015, which replaces the repo's Node, Python, Deno and other non-Bayān tooling with Bayān or shell, adding library code only in its proper home.
2. **Later:** be the reference for building the libraries themselves.

It consolidates the stdlib chat (Oct 22), round 3 and revisions 0002, 0003, 0007 and 0015, the packages, concurrency, codegen and markup designs (Nov 1), and the tooling decisions of Nov 2.

**Rules for using it**

- A module placed here is placed for good. It may be incomplete; it may not be moved without a numbered revision.
- Nothing is added to a module unless a real consumer needs it (the per-port rule, §12). The catalog lists the full intended scope so additions land in the right place, not so they are built ahead of need.
- Where sources disagree, §13 lists the conflict and the proposed resolution. Until resolved, the agent asks rather than picks.
- Status words: **Decided** (user confirmed), **Proposed** (recommended, not yet confirmed), **Open** (needs a decision), **Superseded** (replaced by a later decision).

## 2. Tier model (Decided)

Three tiers, named `core`, `std` and `kit`. Arabic names (`nawa`/`asas`/`zad`) were considered and rejected to reduce confusion; the Arabic influence lives in keyword word order, ishtiqāq forms and the grammar checker instead.

| Tier | Depends on | Stability | Versioning | Ships | Purpose |
| --- | --- | --- | --- | --- | --- |
| `core` | nothing: no OS, no allocator, no libc | frozen per edition | with the language | always; no import needed | language support; usable on bare metal and in comptime |
| `std` | `core`, OS, allocator | frozen per edition | with the language | always | the portable foundation every program assumes |
| `kit` | `std` | semver, deprecation window | separately from editions | with the toolchain, installed by default | batteries: protocols, drivers, formats, data that changes |
| Official packages | `std`, `kit` | semver | separately | on demand | maintained beside `kit`, not bundled (e.g. the ndarray library) |
| Ecosystem | anything | author's choice | author's choice | through the registry | everything else |

Why this split: Python froze protocols in its stdlib and they rotted ("dead batteries": urllib, asyncore, smtpd). Go is batteries-included but froze `net/smtp`. Keeping `std` small and permanent, and `kit` bundled but versioned, gives Go's out-of-the-box experience without Python's rot.

Related decisions:

- Versioned modules come from `kit`, never `std` (packages design, Nov 1).
- `kit` gets a lifecycle policy: semver, a documented deprecation window, and `bayan audit` covering `kit` advisories.
- `core` must not need libc. `std` and `kit` pull libc in only when used, through the runtime (`rt`), never directly (toolchain item K4). This keeps cross-builds and the future own-sysroot work simple.
- Dropped 2026-11-02 (Q10): script mode imports explicitly; it does not auto-import `kit`.

## 3. Placement rules

Apply these in order; the first that matches decides the home. They are drawn from the decisions and lessons in §11.

1. **Can the language express it?** Then it is a language feature, not a library: errors (`fails`, `union error`), validation (`requires`, distinct types, taint), dependency injection (effects and `provide`), mocking (effects with fake handlers), boilerplate (derivations and generators).
2. **Does it need no OS, no allocator and no libc?** `core`. This includes anything comptime must run.
3. **Is it a frozen format or algorithm that almost every program needs?** `std`. Frozen means the spec will not change: JSON, UTF-16, Base64, SHA-2, UUID.
4. **Is it an interface that libraries would otherwise each invent?** `std`, even if every implementation lives in `kit` (log, codec, database driver). Competing facades (SLF4J vs commons-logging, Rust's `log` vs `tracing`) come from shipping the interface too late.
5. **Is it a protocol, a driver, or a format that keeps evolving?** `kit`: HTTP, TLS, SMTP, SQL drivers, YAML.
6. **Is it data that changes on its own schedule?** `kit`: the timezone database, the TLS root store, CLDR locale data.
7. **Is it security-sensitive and churns with CVEs?** `kit`, so fixes ship without an edition: TLS, XML.
8. **Is it large and needed by few programs?** An official package outside the bundle (ndarray).
9. **Is it a framework, a vendor SDK, or a style choice?** Ecosystem.

Three cross-checks before adding anything:

- **No micro-packages.** A small need that recurs across ecosystems (uuid, glob, temp files, user dirs, retry) belongs in `std` or `kit`, so nobody publishes a left-pad.
- **No two APIs for one job.** If a redesign is needed later, it ships as a `v2` module gated by edition (as Go did with `encoding/json/v2`), never as a parallel library.
- **One I/O module per effect.** Every module that touches the outside world maps one-to-one to an effect and a package grant (§8).

## 4. `core` catalog

`core` needs no import (round 3, §27). Builtins already in the v0.1 compiler live in `BuiltinMembers.cs` and the C runtime; they are `core` (or `std`, where marked) by home even though they are implemented in the compiler today.

| Area | Contents | Status | Source |
| --- | --- | --- | --- |
| Integers and floats | sized integers with C# aliases as a style choice; overflow traps in every build; `+%` / `+\|` / `checked(expr)`; bit operations | v0.1 | spec, round 2 |
| Scalar math | scalar nouns and `-ed` copies; `floor`, `ceiling`, `rounded`, `truncated`, `clamped`; free `min`/`max` | v0.1 (existing members) | round 3 §27, J70 |
| Parsing and formatting numbers | `T.parse`/`T.tryParse(text, as: Radix)`; `string(n, as: .hex \| .binary \| .octal \| .decimal)`; `f64.tryParse` locale-pinned (C locale, .NET Core 3.0+ overflow behavior) | v0.1; `f64.tryParse` in revision 0015 | J81, rev 0015 |
| Strings | immutable validated UTF-8 `string`; byte `length`; no integer indexer; `rune`; `Symbol`; sub-range results are borrowed views | v0.1 | rev 0003 |
| String members (v0.1 set) | `isEmpty`, `isAscii`, `runeCount`, `bytes`, `runes`, `utf16`, `lines`, `parts`, slices, `position`, `lastPosition`, `count(x)`, `equals(as: .caseless)` (ASCII), `trimmed`, ASCII `lowercased`/`uppercased`, `replaced` (literal), `joined`, `repeated`, `copy()` | v0.1 | rev 0003, J31 |
| Building and formatting text | `StrBuf` (`append`, `insert`, `clear`, `finish`); `string(x)` as the only `Display` conversion; `Display.formatInto(ref StrBuf out, as FormatSpec spec)`; specs `as`, `width`, `fill`, `align`, `digits`, `debug` as named arguments in interpolation holes | v0.1 | rev 0003 |
| UTF-8 codec | `Utf8.decode`, `Utf8.tryDecode`, `Utf8.decode(bytes, invalid: .replace)` | v0.1 | J82 |
| Unicode classification | rune categories needed by the lexer and `std.text` | Proposed home; see §13 on case tables | stdlib chat |
| Memory | `Span`/`ReadOnlySpan`, slicing, views (`span` property, `mutableSpan()`), the `Allocator` interface only (no implementations) | v0.1 in part | stdlib chat, J80 |
| Errors and absence | `Result<T, E>`, `T?`, `never`, `Panic`, `Panic.handler` (static `PanicHandler`, bound `uses console`, first statement of `main` only) | v0.1 | J77 |
| Iteration and queries | `for` over the `moveNext`/`current` cursor pattern; `IIterator<T>` as a bounded generic; lazy queries (`where`, `except`, `all()`); collection aggregates `sum`, `product`, `average`, `min`, `max`, `min(by:)`, `percentile(at:)` and their `try` forms | in progress (006/007) | rev 0002, round 3 §27 |
| Safety types | `Untrusted<T>` with `trustUnchecked() because "…"`; `Secret<T>`; distinct types with `requires`; units of measure; typed indices | v0.1 in part; units, typed indices spec-only | round 3 §8, §9, §17 |
| Concurrency marker interfaces | `ISendable`, `IShareable` (auto-derived), `@sendable` | Decided, v0.2 | concurrency design R4 |
| Hashing | non-cryptographic only (wyhash or rapidhash class) for maps | Proposed | stdlib chat |
| Comptime | reflection (`typeof(T).fields`), derivation hooks, `comptime for`/`if`, opt-in `law` declarations | spec; interpreter in v0.2 | codegen design |
| SIMD | portable `Vec<T, N>` with runtime CPU dispatch (AVX2, AVX-512, NEON) | Proposed, needs const generics | ndarray discussion |
| Builtins | `print` and stdin (`uses console`), `print(…, to: .stderr)`, ambient `log(…, as: .level)` (non-allocating, `level: message` to stderr in v0.1), `panic`, `assert` | v0.1 | J14, J67 |

The v0.1 set is deliberately small. Everything else in revision 0003 (words, graphemes, full case mapping, normalization) goes to `std.text`, not `core`.

## 5. `std` catalog

The **015** column marks modules the tooling ports are expected to need first; they grow only as far as a port requires (§12).

| Module | Contents (full intended scope) | Effect | Status | 015 |
| --- | --- | --- | --- | --- |
| `std.alloc` | `General` (default for `main`), `Arena`, `Pool`, `FixedBuffer`, leak-checking test allocator | `alloc` | spec | |
| `std.collections` | `List`, `Map`, `Set`, `Deque`, `PriorityQueue`, `BitSet`, inline/small-buffer list (`InlineList<T, N>` needs const generics); members `append`/`prepend`, `add` for sets, `get(at:)` behind `[]` (traps), `tryGet(at:)`, `remove(at:)`, `remove(from:until:)`/`(from:through:)`, `replace(at:by:)`, stable `sort`; map literals | `alloc` | v0.1 in part | yes |
| `std.io` | `Reader`/`Writer` interfaces, buffered I/O, stdin/stdout streams | `console` for std streams | spec | yes |
| `std.fs` | files, directories, metadata, exists/mkdir/remove/rename, glob, walk, temp files and dirs, watching, user dirs, file locks; capability `Dir` handles (paths resolve relative to a handle; `Untrusted` paths cannot escape it) | `fs` | spec | yes |
| `std.path` | path operations without allocation; `PathSegment` sink type | none | spec | yes |
| `std.process` | spawn, pipes, exit codes, `run`; `Arg` sink type | `process` | `run` in v0.1 | yes |
| `std.env` | environment variables, args | `env` | spec | yes |
| `std.cli` | argument parsing derived at comptime from a struct | `env` | spec | yes |
| `std.config` | binds env, files and args to a struct at comptime; dependencies come from effects, not a container | `env`, `fs` | Proposed | |
| `std.time` | `Instant`, `Duration` as units, `DateTime`, UTC and offsets; all immutable values | `clock` | spec | yes |
| `std.fiber` (and sync types) | fibers, `scope`/`spawn` runtime (`limit:`, `timeout:`/`until:`, `policy: .collectAll`, `workers: .compute`), `Channel`, `select`, timers, `Fiber.yieldNow()`, `Fiber.checkCanceled()`, `shield(timeout:)`, `Mutex` guards (`lock()`, `unlock(move this)`), `AsyncMutex`, `Arc`, `Rc`, blocking pool for `@blocking` externs, compute pool, debug watchdog, data-parallel `par` query | `suspend` | Decided, v0.2 | |
| `std.net` | TCP, UDP, DNS, Unix sockets, named pipes; timeouts or deadlines required in signatures | `net` | spec | |
| `std.codec` | the shared codec interface so JSON, TOML, CSV and binary formats use one comptime derivation | none | Proposed | |
| `std.json` | comptime-generated, zero-allocation reader and writer; decoded output `Untrusted` by default; `@derive(Json)` (unions, validation on decode); no reflection-based polymorphic decoding | none | Decided (std); derivation v0.2 | yes |
| Byte codecs | `Hex` (v0.1), `Base64` (spec-only), varint; static `encode`/`decode`, `decode` fails a codec error | none | J83; module Open (Q3) | yes (`Hex`) |
| `std.text` | `Utf16.decode`, `words`, `graphemes`, full Unicode case mapping and folding, `normalized`, `truncated`, `wrapped`, identifier-case transforms, `slugged`, `escaped`, regex `replaced`, `grouping`, date specs; ordinal and invariant only | none | after v0.1 (rev 0003) | maybe |
| `std.regex` | compiled at comptime when the pattern is a literal | none | spec | yes |
| `std.crypto` | SHA-2/3, BLAKE3, HMAC, CSPRNG, ChaCha20-Poly1305, AES-GCM, Ed25519, X25519, Argon2; integrated with `Secret`; algorithms always named explicitly | `random` for CSPRNG | spec; scope Open (Q2) | yes (SHA-256) |
| `std.random` | seeded PRNG, separate from the CSPRNG | `random` | spec | |
| `std.log` | the ambient `log` effect, structured fields, spans; never evaluates message content; tainted fields stay marked | `log` (ambient) | `log` builtin in v0.1 | |
| `std.uri` | URL parsing, `Url` sink type, `Url.tryParse` (rejects `javascript:` in markup `href`) | none | spec | maybe |
| `std.uuid` | 16-byte `Uuid` value; only `Uuid.v7()` (monotonic within a millisecond) and `Uuid.v4()`, no `new()`; parsing takes `Untrusted<string>`, canonical hyphenated form only, typed failure; JSON as string; native SQL column types; v5 later on demand | `random`, `clock` | Decided | |
| `std.math` | vectors and statistics (scalar math is `core`) | none | spec-only | |
| `std.db` | database interfaces: `IQuerySource`, `unique Rows<T>` (`moveNext`/`current`, `first`, `single`, `all`, `readInto`), `IRow`/`IEntity`, `Sql` sink type, `sql"…"` with `$n`, `prepare(…, Millis timeout = 30s)`, `checkShape(… as Mapping)`, cancellation from the fiber scope, `effect db = net, fs`; named placeholders and batches deferred; proposed Oct 24, not decided: interpolation holes instead of `$n`, composable `Sql` fragments, compile-time schema checks, dialect-neutral lowering | `db` alias | spec-only; naming Open (Q1) | |
| `std.c` | FFI and C ABI interop, comptime header import, `extern c`, postfix pointers (`Sqlite3* db`), `cstr` and wide strings, C ABI exports so other languages can call Bayān | `.native` grant | v0.1 in part | |
| `std.test` | power asserts, parameterized tests via attributes, benchmarks, doctests, mutation-testing hooks (`@unobservable`), `console` capture per test, effect fakes via `provide` | inferred | v0.1 in part | yes |
| Events | `Event<T>`, `unique Subscription`; events named for what happened (no `on` prefix) | bound in the type | spec-only; module Open (Q7) | |

**Not in `std`, on purpose:** protocols (HTTP, SMTP, TLS), drivers, interop formats (CSV, TOML, YAML, XML), timezone data, culture (`kit.intl`), and anything else that changes faster than an edition.

## 6. `kit` catalog

`kit` ships with the toolchain and is versioned separately. Every module here can revise its API without an edition bump.

| Module | Contents | Depends on | Status | 015 |
| --- | --- | --- | --- | --- |
| `kit.http` | HTTP/1.1 and HTTP/2 client and server, routing primitives, timeouts required, resilience (timeout, retry, circuit breaker); `HeaderValue` sink type; per-worker connection ownership | `kit.tls`, `kit.mime` | spec | yes (see §12.5) |
| `kit.tls` | TLS client and server; the CA root store ships here, not frozen | `std.crypto` | spec; backend Open (Q8) | indirectly |
| `kit.jwt` | token signing and verification | `std.crypto` | spec | |
| `kit.retry` | retry and backoff outside HTTP (may fold into `kit.http`) | `std.fiber` | Proposed | |
| `kit.mime` | message builder and parser, multipart, attachments, encoded-word UTF-8 headers, `EmailAddress` distinct type; also used for HTTP multipart uploads | `std` | Decided | |
| `kit.mail` | SMTP submission: implicit TLS (465), STARTTLS (587), AUTH PLAIN/LOGIN/XOAUTH2, SMTPUTF8, pipelining, DKIM signing; `MailWriter` markup target (email-safe HTML plus plain text). No IMAP, SMTP server or provider APIs | `kit.tls`, `kit.mime` | Decided | |
| Database drivers | SQLite and PostgreSQL drivers implementing `std.db`; `@derive(Row)`; binary COPY; per-worker pools with a total `connections` budget in `package.byn`; transactions are `unique` values | `std.db`, `std.net` | spec; naming Open (Q1) | |
| `kit.compress` | deflate/gzip, zstd, brotli; extraction stays inside a `Dir` handle (no zip slip) | `std.fs` | spec | |
| `kit.csv` | CSV reader and writer; `@derive(Csv)` with columns matched by header name | `std.codec` | spec | maybe |
| `kit.toml`, `kit.yaml` | interop formats only (`package.byn` uses Bayān struct literals); safe loading only, no arbitrary types | `std.codec` | spec | maybe |
| `kit.xml` | pull reader and writer; no DTDs, only the five predefined entities, external entities off; decoded output `Untrusted` | `std` | **Proposed (new, Nov 2)** | yes (.trx, Cobertura) |
| `kit.tz` | the IANA timezone database | `std.time` | spec | |
| `kit.intl` | culture-sensitive formatting and parsing through an explicit `for:` argument; CLDR data; no ambient culture anywhere | `std.text` | Decided (rev 0003) | |
| Legacy encodings | `Latin1.decode` and other non-UTF encodings | `std` | spec-only; module Open (Q5) | |
| `kit.term` | colors, prompts, progress bars, basic TUI; a markup target (terminal buffer) | `std.io` | spec | maybe |
| `kit.otel` | OpenTelemetry export for `std.log` spans and metrics | `kit.http` | Proposed | |
| `kit.web` | `HtmlWriter` markup target with streamed HTML and HTMX attributes (`hxPost:`, `hxTarget:`, `hxSwap:`); `Html` sink type and `Html.trusted(…) because "…"`; `@derive(Form)` (one rule set drives the server parser and HTML5 attributes); `Routes` derivation with an OpenAPI document; client generation from OpenAPI | `kit.http`, `std.uri` | Decided provisionally (Nov 1), v0.2 | |
| `kit.ui` | native UI from the same markup: node table, a Bayān profile of CSS (logical properties only, non-collapsing margins), lookless controls with named states, `@element` controls (no `control` keyword); XML only through one-way `bayan import` | platform layer via `std.c` | Decided provisionally | |
| `kit.svg`, `kit.pdf` | markup targets for charts and reports | `std.io` | Proposed | |
| `kit.markdown` | Markdown rendering and sanitizing | `std.text` | Proposed (named in the markup design) | |
| `kit.proto` | Protocol Buffers; `Protobuf.generator` for `build.byn` | `std.codec` | later | |

The markup feature is a generic language construct (`markup target { … }`, `@element` structs) with no built-in vocabulary. Each target library supplies its vocabulary. The keyword `markup` is reserved in round 4; the feature lands in v0.2 with `kit.web` and `kit.ui`.

## 7. Not a library: language features, official packages, ecosystem

**Covered by the language, so no library will be written:**

| Need | Replaces | Bayān mechanism |
| --- | --- | --- |
| Error handling | anyhow, thiserror, pkg/errors | `fails`, `union error`, `catch switch` |
| Validation and schemas | FluentValidation, zod, validator, pydantic | distinct types with `requires`, `expects`, taint |
| Dependency injection | MEL DI, Spring | effects and `provide` |
| Mocking | Moq, Mockito, testify mocks | `provide` fake handlers for `clock`, `random`, `fs`, `net`, `env` |
| Boilerplate | source generators, Lombok, syn/quote | derivations and `build.byn` generators into `gen/` (staged, option B) |
| Templates for HTML, mail, terminal, UI | Razor, templ, JSX | `markup` blocks with library vocabularies |
| Async split | tokio vs async-std, Python sync/async twins | colorless fibers; one API |

**Official packages outside the bundle:** the ndarray library (strided views, broadcasting, SIMD kernels, BLAS/LAPACK through `std.c`, pocketfft port, `.npy` and Arrow C Data Interface interop, differential tests against NumPy). It needs const generics, operator forms, multi-dimensional slicing and SIMD first.

**Ecosystem only:** cloud SDKs (AWS, Azure), ORMs, gRPC, IMAP, SMTP servers, mail provider APIs (they are `kit.http` calls), image and audio, and model definition or training code that is itself written in Python.

**Superseded:** the stdlib chat (Oct 22) listed GUI and the web framework as ecosystem territory. The markup decision (Nov 1) puts HTML and UI vocabularies in `kit.web` and `kit.ui`. The language stays free of them; the ecosystem can still add other vocabularies the same way. `kit.template` from the stdlib chat is replaced by markup plus generators (Q6).

## 8. Rules every library follows

**Effects and package grants.** The effect list is `alloc`, `suspend`, `net`, `fs`, `clock`, `random`, `env`, `process`, `console`, plus ambient `log`. Public functions declare their effects; private ones infer. Each I/O module maps to exactly one effect and one grant:

| Module | Effect | Grantable to a dependency |
| --- | --- | --- |
| `std.fs` | `fs` | yes |
| `std.net`, `kit.http` | `net` | yes |
| `std.process` | `process` | yes |
| `std.env`, `std.cli` | `env` | yes |
| `std.time` | `clock` | yes |
| `std.random`, CSPRNG | `random` | yes |
| `print`, stdin, `std.io` std streams | `console` | yes |
| `std.alloc` | `alloc` | no (ambient) |
| `std.fiber` | `suspend` | no (ambient) |
| `std.log` | `log` | no (ambient) |
| `std.c`, any `extern` | `.native` | yes, separately |

In `package.byn`, a dependency gets `uses:` grants; a consumer's grant must cover the dependency's `uses:`, and an update that adds an effect stops the build. Closures passed into a library count against the caller, not the library.

**Taint.** Anything read from outside (files, network, env, decoded data) is `Untrusted<T>`. Filtering keeps taint; constructing a `requires` type removes it. Sinks take dedicated types: `Sql`, `Html`, `PathSegment`, `Url`, `HeaderValue`, `Arg`. `expects` never reads `Untrusted`.

**Culture.** No ambient culture. `std` is ordinal or invariant; culture is an explicit `for:` argument in `kit.intl` (avoids the dotted/dotless-i casing and culture-sensitive `Parse` bugs).

**Naming grammar.** Every library is the grammar checker's first test suite:

- instance nouns and adjectives name results; verbs act on `ref this`; `-ed` names a changed copy; static and free functions are verbs;
- fallible pairs are `T.parse`/`T.tryParse`; `tryGet(at:)` style;
- conversions are `T(x)` and `string(x)`, views are `span`/`mutableSpan()`, copies are `copy()`; no instance `toX`/`asX`;
- particles (`at`, `from`, `to`, `until`, `through`, `by`, `for`, `as`) at most once per signature, never as member names;
- events are past participles or adjectives, never `on…`; `log` is the verb, the level is `as: .x`;
- assertion verbs (`contains`, `startsWith`, …) are a closed group of queries;
- codecs are static `encode`/`decode` (`Utf8`, `Hex`, `Base64`).

**Allocation contract.** Each function either takes an allocator (directly or through `uses alloc` and the provided allocator) or allocates nothing, and docs and the formatter surface which. Zero-allocation paths are the default for hot APIs (`std.json`, markup writers, `log`).

**Safe defaults.** Network calls require a timeout or deadline. Algorithms are named explicitly (no `Uuid.new()`, no default hash); a default may change only per edition. All time types are immutable. Decoders never construct arbitrary types and never fetch external entities.

**Concurrency.** Resources (connections, pools, files) are owned per worker and per child; connections are not `ISendable`; no OS lock is held across a suspend point; blocking C calls are `@blocking`.

**Generated code.** Derivations write readable code into `gen/` (mirroring `src/`), formatted by `bayan format`; the compiler always regenerates and compares; published packages never contain `gen/`.

**Runtime boundary.** Libraries call libc only through `rt`, which keeps the list of libc functions in use.

## 9. Priority order

Ordered by what unblocks what: language and `core` first, then `std` for scripts and CLIs (the fastest dogfooding path, and what spec 015 needs), then `std` services, then `kit`. Within a tier, real consumers pull work forward; nothing is built speculatively.

**P0: language and `core`**

| # | Category | Home | Replaces |
| --- | --- | --- | --- |
| 1 | Error handling | language | anyhow, thiserror, pkg/errors |
| 2 | Codegen and boilerplate | language (derivations, generators; v0.2) | source generators, Lombok, syn/quote |
| 3 | Collections and queries | `core` iteration, `std.collections` (builds on 006/007) | Guava, lodash, itertools, indexmap, smallvec |
| 4 | Text | `core` strings and `format`, then `std.text`, `std.regex` | Commons Lang, regex, memchr |
| 5 | Platform and FFI | `std.c` | libc, windows-sys, System.Memory shims |

**P1: `std` for scripts and CLIs (spec 015 lives here)**

| # | Category | Home | Replaces |
| --- | --- | --- | --- |
| 6 | Filesystem | `std.fs`, `std.path` | Commons IO, fs-extra, tempfile, walkdir, glob |
| 7 | Processes and environment | `std.process`, `std.env` | child_process, subprocess, os/exec |
| 8 | CLI arguments | `std.cli` | clap, commander, cobra, click |
| 9 | Logging | `std.log` | Serilog, SLF4J, tracing, debug, zap |
| 10 | Serialization | `std.codec`, `std.json` | System.Text.Json, Jackson, serde, pydantic |
| 11 | Hashing and byte codecs | `std.crypto` hashes, `Hex`, `Base64` | hashlib, crypto, sha2 |
| 12 | Regex | `std.regex` | re, regex, RegExp |
| 13 | Config | `std.config` | MEL Options/Config, dotenv, viper |
| 14 | Validation | language (first consumers: `std.json`, `std.config`) | FluentValidation, zod |
| 15 | Testing | toolchain plus `std.test` and effects | xUnit, JUnit, Moq, pytest |

**P2: `std` services**

| # | Category | Home | Replaces |
| --- | --- | --- | --- |
| 16 | Concurrency | `std.fiber` (v0.2 design confirmed) | tokio, crossbeam, rayon, rxjs |
| 17 | Sockets and DNS | `std.net` | low-level tokio, Netty |
| 18 | Date and time | `std.time` | NodaTime, chrono, jiff, moment, dateutil |
| 19 | Crypto (rest) | `std.crypto` with `Secret` | BouncyCastle, ring, cryptography |
| 20 | UUID | `std.uuid` | google/uuid, uuid crate |
| 21 | Database interfaces | `std.db` | ADO.NET interfaces, database/sql |

**P3: `kit` batteries**

| # | Category | Home | Replaces |
| --- | --- | --- | --- |
| 22 | HTTP client and server | `kit.http` | HttpClient, reqwest, axios, requests |
| 23 | TLS and tokens | `kit.tls`, `kit.jwt` | rustls, IdentityModel, pyjwt |
| 24 | Mail | `kit.mime`, `kit.mail` (after `kit.tls`) | MailKit, nodemailer |
| 25 | Database drivers | SQLite, PostgreSQL (naming Q1) | Dapper, Npgsql, pgx, SQLAlchemy |
| 26 | Resilience | `kit.http`, `kit.retry` | Polly, resilience4j, tenacity |
| 27 | Terminal | `kit.term` | Spectre, chalk, rich, tqdm |
| 28 | Timezones | `kit.tz` | tzdata, pytz |
| 29 | Culture | `kit.intl` | ICU, Globalization |
| 30 | Web and UI vocabularies | `kit.web`, `kit.ui` (v0.2 with markup) | Razor, templ, HTMX helpers |
| 31 | Observability export | `kit.otel` | OpenTelemetry SDKs |

**P4: later or ecosystem**

| # | Category | Home | Replaces |
| --- | --- | --- | --- |
| 32 | Formats | `kit.compress`, `kit.csv`, `kit.toml`, `kit.yaml`, `kit.xml` | flate2, pyyaml, toml, lxml |
| 33 | Documents | `kit.markdown`, `kit.svg`, `kit.pdf` | markdig, svgwrite |
| 34 | RPC | `kit.proto` | Google.Protobuf, gRPC, prost |
| 35 | Arrays | official ndarray package | NumPy |
| 36 | Cloud SDKs | ecosystem | AWSSDK, boto3 |

Spec 015 pulls a few P3 and P4 items forward (`kit.xml` for test reports, possibly `kit.http`). They keep their `kit` home; only their timing changes.

## 10. Interop strategy (Decided)

C is the only first-class, live interop, through `std.c`. Everything else goes through it or through a one-time translation.

| Source | Route | When | Why |
| --- | --- | --- | --- |
| C libraries | `std.c`: comptime header import, `extern c`, `@blocking`, `native:` block in `package.byn`, `.native` grant | v1 | C is the only universal ABI |
| C# / .NET code | `bayan migrate cs`, a one-time translator that maps BCL and popular NuGet packages to `std`/`kit` and stops with teaching errors on classes, inheritance, reflection and `dynamic` | post-v1, once `std`/`kit` exist to map onto | Bayān has no classes, inheritance or GC, so live import would be wrong |
| .NET libraries used live | export from NativeAOT with `[UnmanagedCallersOnly(EntryPoint = …)]` and call through `std.c` | any time | fills early library gaps at the edges, accepting the .NET runtime cost there |
| Python | no embedding and no lowering | — | numpy, pandas and similar are written against the CPython API |
| What Python libraries wrap | bind the native engine directly: BLAS/LAPACK (numpy linalg), Apache Arrow and DuckDB (pandas, polars), ONNX Runtime and llama.cpp (inference), SQLite, zlib, OpenSSL; PyTorch via ONNX export | as needed | no Python dependency |
| Your own Python, Deno, Node scripts | rewrite in Bayān or shell (spec 015) | after 012 passes (015 S1) | better result than any automatic conversion |

The C# translator converts the clean subset (structs, records, static methods, `Span`, `stackalloc`, pattern matching, most LINQ, `async` with coloring dropped) automatically. Exceptions translate only partly, since `fails` needs to know which errors flow where. Zero-allocation, span-based C# translates best; Jaunty and the user's other libraries are the first test corpus.

`kit.python` was mentioned once as an optional embedding and then rejected; it is not planned.

## 11. Lessons from other ecosystems

The download rankings (NuGet, crates.io, PyPI, Maven, npm, Go modules; researched Oct 22) show the same six groups everywhere: std-gap fillers, serialization, logging and config, HTTP, testing and mocking, and code generation. .NET's top downloads are DI, options and logging abstractions; Rust's are std-gap crates plus serde and tokio; Python's are the HTTP stack, cloud SDKs and validation; Java's mostly extend the JDK; Go's are routers, uuid, testify and database helpers. Bayān answers each group with `std`, `kit` or a language feature (§7, §9).

| Pitfall | Where it happened | Bayān rule |
| --- | --- | --- |
| Freezing protocols in std | Go `net/smtp`, .NET `SmtpClient`, Python asyncore/smtpd | protocols live in `kit` |
| Leaving obvious gaps too long | Go uuid (a decade, until Go 1.27); Rust rand, regex, JSON | small universal needs go in `std` early |
| Two competing APIs for one job | Newtonsoft vs System.Text.Json; `java.util.Date` vs java.time; chrono, time, jiff | design once; redesigns ship as an edition-gated `v2` module |
| Defaults that age | uuid v4 "for now", MD5/SHA-1, bcrypt vs Argon2, old TLS | name algorithms explicitly; defaults change only per edition |
| Async splitting the ecosystem | tokio vs async-std; Python sync/async twins | colorless fibers, one API |
| Micro-package supply chain | left-pad, `is-*`, event-stream | cover small needs in `std`/`kit`; `uses:` grants; `bayan audit` |
| Payload hidden outside the repository | event-stream (2018), xz (2024) | no install scripts; sandboxed `build.byn`; `gen/` always regenerated; packages never ship `gen/` |
| Competing facades | SLF4J vs commons-logging, `log` vs `tracing` | interfaces (log, codec, database) in `std` before libraries pick their own |
| Logs interpreting their input | Log4Shell | `std.log` never evaluates content; taint stays marked |
| Unsafe deserialization | Java serialization, PyYAML `load`, XML XXE, NumPy `allow_pickle` | no reflection-based polymorphic decoding; external entities off; decoded output `Untrusted` |
| No timeouts by default | Go `http.DefaultClient`, Python requests | network calls require a timeout or deadline |
| Locale-dependent behavior | .NET culture-sensitive `Parse`/`ToString`, dotted/dotless-i casing | invariant by default; culture via `for:` |
| Mutable time types | moment.js, `java.util.Date` | all time types are immutable values |
| Path traversal, zip slip | many archive libraries | capability `Dir` handles; extraction stays inside the handle |
| Frozen data in std | tzdata, CA bundles (certifi) | changing data ships in `kit` (`kit.tz`, TLS root store) |
| Per-process, not per-dependency, permissions | Deno permissions; Java `SecurityManager` (removed) | compile-time `uses:` grants per dependency |
| Ambient transaction lost across await | .NET `TransactionScope` without `AsyncFlowOption` | transactions are values passed explicitly |
| Generated code hidden and unreadable | C# source generators | one visible `gen/` tree, readable, ejectable |

Further design points from the same research, all adopted or proposed above: effects replace mocking libraries; effects plus `provide` replace DI containers; one derivation serves many formats through `std.codec`; every `std` signature shows its allocation contract; capability-based filesystem; `std` is the grammar reference; no micro-packages; a `kit` lifecycle policy.

## 12. Spec 015: replacing non-Bayān tooling

Goal: every script and tool in the repo is Bayān or shell, except where a host platform requires another language. Library additions land in their proper home from the start, even if that home stays incomplete.

### 12.1 Decisions (Nov 2)

| # | Question | Decision | Reason |
| --- | --- | --- | --- |
| T1 | Std scope | **Per port, homes fixed now.** Add only what the script being ported needs, in the module this doc names. Each new public API goes to the user as a spec question. | Smallest surface, nothing built ahead of need, nothing thrown away (no hand-parsed XML) |
| T2 | Which `bayan` runs the tooling | **Pinned build.** Build `bayan` from the last `dev` merge into a cache keyed by that commit; gates (guard, land) and tooling use it, never the working tree. | A broken compiler change cannot disable the gate meant to catch it |
| T3 | Timing | **Write spec 015 now; port after 012 passes** (015 S1, 2026-11-02; first said after 010). 015 does not interrupt 013 T19. | Std additions are then written once, in Bayān, instead of in C# and ported again in 010 |
| T4 | CI JS actions | **Replace now:** `actions/checkout` → `git clone`/`fetch` in a shell step; `actions/setup-dotnet` → pinned `dotnet-install.sh`. Keep `upload-artifact`/`download-artifact`. | Removes 2 of 4 Node actions; GitHub artifacts need the official actions |

Conditions on T4: authenticate `git` with `GITHUB_TOKEN` when the repo is private and set the fetch depth explicitly; verify `dotnet-install.sh` by SHA-256 (or vendor it) instead of piping a live download into the shell.

### 12.2 What stays non-Bayān

| Item | Stays as | Why |
| --- | --- | --- |
| Bootstrap (stage 1 and `bootstrap/bayan.c`) | shell plus C | it must run before any `bayan` binary exists |
| Pinned-build fetch and cache step | shell | it produces the `bayan` the other tools use |
| VS Code extension | TypeScript/JavaScript | VS Code loads extensions only as JS |
| Visual Studio provider | C# | Visual Studio SDK |
| `upload-artifact`, `download-artifact` | GitHub JS actions | no supported shell equivalent |
| C# compiler and its tests until 012 | C# | self-hosting replaces them; out of 015's scope |

Everything else is a candidate. Tests and generators around the editor tooling (for example `npm run lsp`, `npm run grammar`, `tests/lsp-regress.js`) are candidates, since they only need `std.process`, `std.json` and `std.fs`.

### 12.3 Shell or Bayān?

- **Shell** for glue: one-liners, running other tools in sequence, environment setup, and anything that must work without `bayan`. POSIX `sh` or `bash` with `set -euo pipefail`, checked by ShellCheck.
- **Bayān** for anything with logic: parsing (JSON, XML, text formats), data transformation, hashing, decisions over structured data, or more than about 50 lines of shell.
- A Bayān tool lives as a script (`bayan run tools/<name>.byn`) unless it is reused across tools, in which case it becomes a small package under `tools/`.

### 12.4 Where each known need lands

| Need | Home | Notes |
| --- | --- | --- |
| Environment variables | `std.env` | |
| Exists, mkdir, remove, rename, glob, walk, temp | `std.fs`, `std.path` | |
| Spawning tools, exit codes, pipes | `std.process` | every bash port needs it |
| Arguments | `std.cli` | start with hand-parsed `args` if `std.cli` derivation is not ready; keep the call site in `std.cli` shape |
| JSON | `std.json` | reading needs decoders (SPEC §11), spec-only in v0.1; that is the real dependency |
| Regex | `std.regex` | |
| SHA-256 | `std.crypto` | hashes belong in `std` whatever the crypto scope decision |
| Hex | `Hex` codec | already v0.1 |
| XML (.trx, Cobertura) | `kit.xml` | new; pull reader only to start, secure defaults |
| HTTP(S) | `kit.http` over `kit.tls` | see 12.5 |
| Unicode data tables (revision 0015 generator) | `std.fs`, `std.text` | |

### 12.5 The HTTP gap

A native `kit.tls` is a large job and must not block 015. Until it exists, a ported script that fetches over HTTPS calls `curl` through `std.process`, and the port records that as a tracked todo. The alternative, backing `kit.tls` with a C library through `std.c` first, is a separate decision (Q8).

### 12.6 Pinned build details

- Cache path keyed by the merge commit, e.g. `.cache/bayan/<sha>/bayan`; built once, reused.
- The pin follows the last merge into `dev` automatically; it is never edited by hand.
- If the pinned build fails to build, the gate fails loudly with the commit and the build log, and never falls back to the working tree.
- The working-tree `bayan` is still what the compiler's own tests exercise; only tooling and gates use the pin.

## 13. Open questions and decision log

### 13.1 Conflicts and open questions

The agent asks before touching any of these.

| # | Question | What the sources say | Proposed resolution |
| --- | --- | --- | --- |
| Q1 | Database module names | Round 3: `std.db`. Stdlib chat: `kit.sql` + `kit.sqlite`, `kit.postgres`. Packages doc: `kit.db.pg`. | Interfaces in `std.db` (rule 4: facades in `std`); drivers as `kit.db.sqlite`, `kit.db.pg`. Retire `kit.sql`. |
| Q2 | Crypto scope in `std` | Stdlib chat put all of it in `std` but left it open. | Hashes, HMAC and CSPRNG in `std` (frozen algorithms). Decide AEAD, signatures and Argon2 before P2 #19. |
| Q3 | Byte-codec module name (settled Nov 2: `std.encoding`) | Stdlib chat: `std.encoding`. Round 3 (J83): "`Hex` and `Base64` are `std` codecs", no module named. | One module, `std.encoding`, holding `Hex`, `Base64`, varint; `Utf16` stays in `std.text`, `Utf8` in `core`. |
| Q4 | Unicode case tables | Rev 0003: full case mapping in `std.text` after v0.1. Rev 0015: `lowercased()` with tables from `UnicodeData.txt`, added to the compiler now. | Tables generated into `core` (classification and simple case mapping); folding, special casing and normalization in `std.text`. Confirm rev 0015's scope. |
| Q5 | Legacy encodings | Rev 0003: "`kit` (`kit.intl`, `Latin1` and other encodings)". | Separate `kit.encoding`; culture and encodings are different concerns. |
| Q6 | `kit.template` | Stdlib chat proposed it; Nov 1 markup design covers HTML through markup and content files through generators. | Drop `kit.template`. |
| Q7 | `Event<T>` home | Spec §16, spec-only, no module. | `core` if it needs no allocator; otherwise `std.event`. Decide when implemented. |
| Q8 | `kit.tls` backend | Not discussed. | Native Bayān long-term. Short-term, decide between a C library via `std.c` (fast, adds a `.native` dependency) and `curl` shell-outs (§12.5). |
| Q9 | `kit.xml` | New on Nov 2. Spec 015 S9 (Nov 2): build it only if the known-failures script outlives the C# test suite; confirm first. | Confirm `kit` placement and the pull-reader-first scope. |
| Q10 | Script mode auto-imports `kit` (dropped Nov 2: scripts import explicitly) | Proposed in the stdlib chat, never confirmed. | Confirm or drop. |
| Q11 | `std.config` | Proposed; overlaps with `std.cli` and `std.env`. | Keep as a thin binder over both; confirm before P1 #13. |
| Q12 | Operator forms, const generics, SIMD | Needed by ndarray, `InlineList<T, N>`, crypto blocks. | Round 4 or later; spec text not settled. |

### 13.2 Decision log

Newest first.

| Date | Decision | Source |
| --- | --- | --- |
| Nov 2 | Spec 015 answers S1–S12, D1–D4: ports wait for 012; `std.env`, `std.regex` (RE2-style), `std.fs` path-string verbs (`createDirectory`, `canonical`), `run(directory:, input:, env:)`, layered `std.json`; Q3 settled (`std.encoding`), Q10 dropped, Q9 conditional | `docs/specs/015-tooling/015-spec.md` *Decisions* |
| Nov 2 | Spec 015 decisions T1–T4 (§12.1); `kit.xml` proposed | this doc |
| Nov 1 | `markup` blocks and `@element` are a generic language feature; HTML and UI vocabularies live in `kit.web`, `kit.ui`; `markup` reserved in round 4 | markup design |
| Nov 1 | Packages: `uses:` grants per dependency, `.native`, versioned modules from `kit`, update cooldown, lockfile hashes | packages design |
| Nov 1 | Concurrency: thread-per-core, `scope`/`spawn`, implicit cancellation, `ISendable`/`IShareable`, no lock across suspend, `@blocking` and compute offload; v0.2 | concurrency design |
| Nov 1 | Code generation option B (staged): `Row`, `Json`, `Csv`, generators, `bayan expand` in v0.2; readable `gen/` tree | codegen design |
| Oct 31 | Rev 0015: `f64.tryParse` (locale-pinned) and `lowercased()` with generated Unicode tables | agent review |
| Oct 24 | Rev 0003: decoding is a codec; `std` ordinal/invariant; culture only in `kit.intl` with `for:`; identifier-case transforms in `std` | strings revision |
| Oct 23 | Round 3: `std.db` shape, `std.math` vectors spec-only, `Hex` v0.1 and `Base64` spec-only as `std` codecs, effect list and aliases, `Panic.handler` | round 3 |
| Oct 22 | Tiers named `core`/`std`/`kit` (Arabic names rejected) | stdlib chat |
| Oct 22 | Mail in `kit.mime` + `kit.mail`; `std.uuid` from day one with explicit `v7`/`v4` | stdlib chat |
| Oct 22 | Interop: `std.c` only; `bayan migrate cs` post-v1; no Python embedding; bind native engines; ndarray as an official package | stdlib chat |
| by Oct 20 | JSON is a first-class standard-library feature, not built into the language | early design |

### 13.3 Sources

- [Standard library for bayan (Oct 22)](https://claude.ai/chat/1b1aa21d-4cb6-4781-b59d-75fcdcf0b28d)
- [Concurrency, packages, codegen and markup designs; addendum A1; round 4 draft (Nov 1)](https://claude.ai/chat/046e3bfd-b68c-478a-9d7c-554705f99fb9)
- [Rev 0015 and agent review decisions (Oct 31)](https://claude.ai/chat/ab4980ec-bcc3-4642-a347-e56a034721d5)
- [Hermetic builds and `kit.ui` (Oct 25)](https://claude.ai/chat/61f64adf-f126-4b3b-88f1-40d9ee3e8b0a)
- [SQL tagged literals (Oct 24)](https://claude.ai/chat/d514e735-621f-4159-9f7d-e0f034f9983e)
- Project files: `2026-10-22-002-design-round-3.md`, `Bayan_Round_3_Consolidated_Summary.md`
