# Design inputs review (2026-11-04)

Consistency review of the docs filed on 2026-11-04 (`bayan-views.md`, `bayan-concurrency.md`, `bayan-shorthands-proposal.md`, `bayan-style-proposal.md`, the round-4 draft) against the spec, Appendix A, the grammar, the style doc, revisions 0016-0018 and each other. One Opus reviewer; the dispatcher spot-checked its spec and code citations (spec :1243, :1310, :1549-1556, :2104, :2271, :2449, :2458; DiagnosticCodes.cs BYN1069, BYN4001, BYN7010). Rows await the user's decisions; nothing in the filed docs is changed yet.

Also: `bayan-handoff.md` "After v0.1" puts fibers and `Io.Evented` in v0.3; `bayan-concurrency.md` says v0.2 (with C3). The user's copies of `bayan-library-tiers.md`, `HANDOFF-014-tooling.md` and `addendum-A2-diagnostic-codes.md` are the originals of the filed versions (tooling spec renumbered 014 → 015); they were not re-filed.

## Status (2026-11-04)

User decisions:

- D-A (C1, A1, C2): `scope`/`spawn` replaces `async`/`await` and `Io.group()`. Round 4 removes them from spec §13 and Appendix A by revision. C3 follows: spec in round 4, runtime v0.2.
- D-B (C6): the block deadline is `scope(timeout:)` / `until:`; round 4 removes `provide timeout: d { … }`.
- D-C (C7): style and idiom lints take `x900–x999` of their area per A2 (revision 0017).
- D-C follow-up (2026-11-04): area by A2 rule 2, spelling-only mistakes in 19xx (`lambdas.it`, `guards.postfix`, adjacent types as an opt-in lint), otherwise the area misunderstood (redundant `.span` and hidden allocation 39xx, nested leading dot 29xx, `nesting.depth`/`conditions.named` 49xx, `functions.length` 09xx); a fix-it does not decide the area. Adjacent types stay off the formatter; S5's open questions (defaults, attributes, refinements, modes) are in the shorthands doc.
- C4, C16-C19 (2026-11-04): decided and written into bayan-concurrency.md §4-§7 and the new §12 (round-4 rewrite list for spec §13). C4: implicit cancellation, `Canceled` leaves the I/O error sets, `shield(timeout:)` is the one uncancelable region (defer bodies run as one), `Fiber.checkCanceled()` for CPU loops plus a candidate 49xx lint. C16: `context.Io` stays; `Io.Evented(workers: .perCore|n)`, `bayan run` uses 1; `Io.Blocking` panics on a sibling deadlock; `.compute` is its own pool (default one per core, inline under Blocking/Simulated); `Io.Threaded` dropped. C17: `@sendable because` (non-empty) replaces the `unsafe` marker. C18: `scope(workers: .compute)` only, `Io.offload` removed. C19: guards kept, `unlock(move this)` the one early release, `Mutex.with` dropped.
- I1, I2 (2026-11-04): I1 renamed to `Fiber.yieldNow()` (a suspend point, so it also unwinds on a pending cancel). I2 and A2 are moot: C19 removed `Mutex.with`. C4 addendum: a shield timeout cancels its own region (pending cancel continues, else `DeadlineError.exceeded`); nested shields are capped; slow `defer`s can delay a cancel by their timeouts added together; `scope(timeout:)` expiry is `DeadlineError.exceeded` (first failure wins; in the set only with `timeout:`/`until:`; a separate set so it never collides with `DbError.timeout`).
- C8, C9, C21, C23 (2026-11-04): C8 drop `braces`, take the style proposal's knobs (round-4 edit list in its §5). C9 wording in its §4.1. C21: one list rule (trailing comma, closer on its own line) and one brace rule (`{` ends the line, `) {`), no waqf revision; open: multi-clause header layout (§3.4). C23: `--check` lists files, `--check --diff` prints a diff.
- C5 follow-up (2026-11-04): the example uses a deferred `var thumbnail: Image` (definite assignment, no optional). Deferred `let` (BYN2101 today) is not a round-4 item. BYN2311 already skips deferred `var`s (NeverReassignedVarLint.cs:20).

Per row:

- applied: C1, C2, C3, C4, C5, C6, C7, C8, C9, C10, C11, C12, C14, C15, C16, C17, C18, C19, C21, C22, C23, C25, C26, I1, I2, I3, I4, I5, I6, I7, I9, I12, S1, S2, S4, S5, S6, S9, A1, A2
- open: C13, C20, C24, I8, I10, I11, I13-I16, S3, S7, S8, A3, A4
- notes: I6 is applied in the filed docs only; bayan-spec.md:633 (`cancelled`) stays until the round-4 revision. I1/I2 keep their names (no rename). C13: the views manifest example is unchanged.

## 1. Contradictions

| # | Severity | Where | vs | Issue | Suggested resolution |
|---|---|---|---|---|---|
| C1 | HIGH | bayan-concurrency.md:44-58, 141-143 (`spawn` … `.join()`) | bayan-spec.md:2449 (Appendix A "`async`/`await`"), :1549 ("`async f(args)` … `await` joins"), :1566 (`Io.group()`, `group.spawn`) | The doc adds `spawn`/`join()` as a second spelling of the settled `async`/`await` and `Io.group()`, and never says what happens to them. Appendix A :2453 bans synonyms. | Say explicitly whether `scope`/`spawn` replaces `async`/`await` and `Io.group()`. Replacing them reopens an Appendix A item, so it needs an explicit user decision and a revision. Otherwise, define `spawn` in terms of `async`. |
| C2 | HIGH | bayan-design-round-4-draft.md:353 ("Not adopted … async/await … Each contradicts a recorded decision") | bayan-spec.md:2449 | Appendix A records `async`/`await` as adopted, not rejected. | Remove async/await from the not-adopted row, or replace it with the actual rejected item (async *coloring*). |
| C3 | HIGH | bayan-design-round-4-draft.md:288 ("Structured concurrency with shared-nothing workers", v0.3, "on evidence only") | bayan-concurrency.md:3 ("Confirmed … specced in round 4 and implemented in v0.2"); bayan-library-tiers.md:104 ("Decided, v0.2"), :421 | The new docs disagree on timing. | Move the row to round 4 (spec) and v0.2 (runtime), or delete it and point to bayan-concurrency.md. |
| C4 | HIGH | bayan-concurrency.md:13 (R3 "Cancellation is an implicit unwind"), :105 | bayan-spec.md:1551 ("`Canceled` is in every I/O error set"); concurrency.md:307 ("test implicit cancellation against explicit `fails .canceled`") | The doc says R3 is "confirmed", but the spec makes cancellation an explicit error and the doc's own §10 leaves it open. | Mark R3 provisional until the Sūq test, or plan to remove `Canceled` from the I/O error sets in §13.2. |
| C5 | HIGH | bayan-concurrency.md:141-143 (`let thumbnail = try scope(workers: .compute) { return try spawn(…).join() }`) | bayan-concurrency.md:46 ("`scope` is a block, not a closure, so `return`, `try` and `break` keep their meaning") | Here `scope` is used as an expression whose `return` yields a value. By :46, that `return` leaves the enclosing function. | Rewrite as a statement-form scope that assigns a `var` declared outside, or define a value form for `scope`. |
| C6 | HIGH | bayan-concurrency.md:50, 62, 77 (`scope(timeout: 2s)`, `until: t`); bayan-views.md:255 | bayan-spec.md:1556-1560 (`provide timeout: d { … }`) | Two spellings for a block deadline. | Pick one. If it is `scope(timeout:)`, list `provide timeout:` for removal in round 4. (`provide timeout:` also conflicts with spec :1647/BYN6007, since `timeout` is not a handler name.) |
| C7 | HIGH | bayan-style-proposal.md:296, 300-310 (BYN4001-4005 as style lints; "BYN4xxx range looks unused"); bayan-shorthands-proposal.md:280, 291, 326, 351 ("Style lints are BYN4xxx") | revisions/0017:18 (rule 4 "`x900–x999` in every area are style and lints"), :28 (BYN4xxx = control flow and failure), :45 (4900s lints); DiagnosticCodes.cs:330 (`BYN4001` = `CCompilerRejected`, live) | BYN4001 is a live code, and A2 already assigns 4xxx to control flow and failure. The style doc's area list (:300, "3xxx types") also differs from A2 (2xxx types, 3xxx memory). | Put style and layout lints in `x9xx` of the matching area, or a 09xx tools category under A2. Remove the claim that 4xxx is unused. |
| C8 | HIGH | bayan-style-proposal.md:5, 173, 373 (width 120); :174/:190 (own-line `{`); :322-323 (`align`, `trailingCommas: .multiline`); no `braces` knob | bayan-style.md:11, 13-14, 72-74 (`braces: .sameLine`/`.nextLine`, `maxLine: 100`, `trailingCommas: true`); bayan-spec.md:2088-2094 | The proposal says "decided", but the current style doc and the spec's manifest example disagree on the width, the knob names and the knob types. | When it lands, update bayan-style.md and spec §19.2 together: rename `maxLine`→`width`, drop or keep `braces`, retype `trailingCommas`, add `align`/`lints`. |
| C9 | HIGH | bayan-style-proposal.md:296-298, 302-310 (BYN4xxx lints with severity `.warning`) | bayan-spec.md:2104 ("Style is never compiler-enforced"); bayan-style.md:5 ("never compiler-enforced") | The proposal emits idiom lints as compiler diagnostics with configurable severity. | Reword spec/STYLE: "layout is never enforced; idiom lints are diagnostics, `.suggestion` by default". |
| C10 | HIGH | bayan-style-proposal.md:315-331 (`Package {` with `name: "suq"` then newline `edition: 2026`, no commas) | bayan-spec.md:147 ("Entries in literals … are separated by commas"); spec :2079-2101 | The manifest example is not a valid struct literal. Its `name` also lacks the `scope/name` form. | Add commas and `"local/suq"`. |
| C11 | HIGH | bayan-style-proposal.md:281 ("`PascalCase`: … effects aliases") | bayan-style-proposal.md:239, 252 (`effect db = net, fs`); bayan-spec.md:1612 (`effect db = net, fs`) | The doc contradicts itself and the spec: effect aliases are lowercase. | Move effect aliases to the lowercase/camelCase row. |
| C12 | HIGH | bayan-views.md:264 (`ReviewForm.fromForm(data) catch error => { … }`), V10 :18 | bayan-spec.md:1243 ("There is no `catch (e)`"; forms are `catch value`, `catch return`, `catch switch`, `catch (Set.variant)`); round-3 plan J9 (no non-switch binding form) | `catch error => {…}` adds a non-switch catch that binds the error. V10 presents it as an existing rule ("same rule as J49"), but J49 covers `catch switch` arms only. | Write it as `catch switch { ValidationError error => { … } }`, or list a new catch-binding form as a round-4 syntax item. |
| C13 | HIGH | bayan-views.md:97 (`uses: [.net, .env]`), :99-100 (`dependencies:`, `Dependency { … uses: }`) | bayan-spec.md:1661, 2085-2086 (`capabilities:`, `deps:`); DiagnosticCodes.cs:353 (BYN6004 text says "`capabilities`"); bayan-style.md:68-69 | Manifest keys differ from the spec. bayan-library-tiers.md:194 already uses `uses:` grants, so the spec lags the tiers doc. | Pick `uses` or `capabilities` and `deps` or `dependencies`, then update spec §14/§19.2 by revision, or change the example. |
| C14 | HIGH | bayan-shorthands-proposal.md:172 ("`stackalloc u8[n] uninit` gives `var u8[]`") | bayan-spec.md:1310 ("`stackalloc T[n] uninit` yields `Uninit<T>`, which cannot be read") | Wrong producer type. | "gives `Uninit<u8>`; writing APIs return the initialized prefix as `var u8[]`". |
| C15 | HIGH | bayan-shorthands-proposal.md:173, round-4 draft:47 (`bayan format --display` shows the other style) | bayan-spec.md:2271-2273 ("no terse/explicit formatter modes … their views are inlay hints"), round 2 §15 | Reintroduces a formatter display mode that round 2 removed. | Make it an LSP display option or inlay hint, not a `bayan format` flag. |
| C16 | MEDIUM | bayan-concurrency.md:11, 42 ("one worker for `bayan run`") | bayan-spec.md:1541 (`Io.Threaded` is the default for scripts; the pool starts on the first `async`); :1540-1543 (Io implementations) | Thread-per-core workers replace the `Io.*` model, and the doc does not reconcile the two. | Map R1 onto `Io.Evented` and `Io.Threaded`, or list §13.1 for rewrite. |
| C17 | MEDIUM | bayan-concurrency.md:14, 150, 271 (`@sendable because` for C handles) | bayan-spec.md:1580 ("raw pointers cannot cross threads without an explicit `unsafe` marker") | Two mechanisms for the same thing. | Say `@sendable because` replaces the `unsafe` marker. |
| C18 | MEDIUM | bayan-concurrency.md:79, 175 (`workers: .compute`) | bayan-spec.md:1582 (`Io.offload(op)`) | Two spellings for CPU offload. | Pick one and list the other for removal. |
| C19 | MEDIUM | bayan-concurrency.md:15, 160-167 (`Mutex.with` closure) | bayan-spec.md:1571 ("a `Mutex` guard alive across a suspension point" is an error) | Different lock model (guards vs a closure). | State that guards are removed or kept; update §13.4. |
| C20 | MEDIUM | bayan-shorthands-proposal.md:257-281 (S6, target-typed `.zero`/`.red` as new, "gated on P7") | bayan-spec.md:554 ("`.member` has one meaning: a member of the expected type (`return .notFound`, `color: .red`)"), :865 | `.red` and `.member` against the expected type already exist. | Narrow S6 to what is actually new (statics and factories, if §3.16 means cases only), and say so. |
| C21 | MEDIUM | bayan-shorthands-proposal.md:329, 332-338 ("`)` closes on the last item's line", no trailing comma) | bayan-style-proposal.md:229 ("a trailing comma, and the closing bracket on its own line"); bayan-style.md:14 | The two new docs give opposite wrapped-list rules. | Give one rule for parameter lists vs argument lists, or align both. |
| C22 | MEDIUM | bayan-views.md:268 (`let _ = try addReview(…)`) | bayan-spec.md:142 ("Discard on purpose with `_ = f()`") | Different discard spelling. | `_ = try addReview(…)` |
| C23 | MEDIUM | bayan-style-proposal.md:362 (`--check` "prints a diff") | bayan-spec.md:2243 (`--check` "prints each file that would change and exits 1") | Different `--check` output. | Align, or record the change. |
| C24 | MEDIUM | bayan-design-round-4-draft.md:89 (C# "arrives through the one-time `bayan migrate cs` translator") | bayan-spec.md:2407 (§25 rule: "zero-allocation C# … compiles as Bayan"); :2250 (`bayan migrate` has no `cs`) | The doc agrees with bayan-library-tiers.md:287-292, but spec §21.2 and §25 still describe C# compiling directly as Bayān. | Bring the spec in line with tiers §10 in a docs revision. |
| C25 | MEDIUM | bayan-shorthands-proposal.md:175, 415 (`bayan migrate cs` mappings in round-4 tests) | bayan-library-tiers.md:428 ("`bayan migrate cs` post-v1") | A round-4 test plan for a tool scheduled after v1. | Drop it from the §8 tests, or label it post-v1. |
| C26 | MEDIUM | bayan-shorthands-proposal.md:117 (`let v: u8[] = buf` with `buf: u8[512]`, commented "var T[] → T[] is implicit") | bayan-spec.md:833, 835 (owned → view only as a call argument; otherwise `.span`) | A `let` is not a call argument, and `buf` is `T[N]`, not `var T[]`. | `let v: u8[] = buf.span`, or fix the comment and the example. |

## 2. Inconsistencies

| # | Severity | Where | vs | Issue | Suggested resolution |
|---|---|---|---|---|---|
| I1 | MEDIUM | bayan-concurrency.md:40, bayan-library-tiers.md:104 (`Fiber.yield()`) | bayan-spec.md:111 (`yield` is a control keyword), :109 ("every other keyword … is reserved and cannot be an identifier") | Member name clashes with a reserved keyword. | Rename it (e.g. `Fiber.pause()`), or make member names after `.` exempt by revision. |
| I2 | MEDIUM | bayan-concurrency.md:15, 160, 167 (`Mutex.with`, `buckets.with { … }`) | bayan-spec.md:117 (`with` is reserved), :2479 (`with` "is struct update") | Keyword clash, and `with` gets a second meaning. | Rename (e.g. `Mutex.lock { … }`). |
| I3 | MEDIUM | bayan-concurrency.md:167 (`{ ref map => … }` lambda) | bayan-spec.md:547-548 (lambda forms: `x => …` in parentheses; no `ref` parameter mode shown) | Braced trailing-lambda syntax with a `ref` parameter is not in §3.16. | Use `buckets.lock(ref map => …)` and define the `ref` lambda parameter. |
| I4 | MEDIUM | bayan-concurrency.md:29, 163 (`= [:]`) | bayan-spec.md:865 (map literal `["k": v]`; no `[:]`) | Undefined empty-map literal. | Use `[]` target-typed, or define `[:]`. |
| I5 | MEDIUM | bayan-concurrency.md:142 (`spawn(resize(…)).join()`) | bayan-concurrency.md:51, 64 (`spawn f(…)`) | Two call shapes for `spawn`. | Pick one. |
| I6 | MEDIUM | bayan-style-proposal.md:138, 148 (`.cancelled`); bayan-spec.md:633 (`cancelled(string reason)`) | bayan-concurrency.md:5 (US `canceled`); bayan-spec.md:1551 (`Canceled`); bayan-views.md:275 (`canceled`) | Mixed spellings, including within the spec. | US spelling everywhere; fix spec :633 and style-proposal :138/:148. |
| I7 | MEDIUM | bayan-design-round-4-draft.md:225, 249 (BYN7010 for `final`), :228 (BYN1069 for `!(x is .s)`) | DiagnosticCodes.cs:372 (BYN7010 `BecomesNeedsRef`), :105 (BYN1069 `StatementAttribute`), :382, 385 (reserved BYN1070, BYN7013) | Codes collide with live ones; the code file already flags this. | Use BYN7013 and BYN1070, or "TBD". |
| I8 | MEDIUM | bayan-design-round-4-draft.md:17 (flip BYN1065); all new docs use `where→expects→uses→becomes→fails` | bayan-spec.md:151, 1592 (`fails CarrierError uses net`); bayan-style.md:16; DiagnosticCodes.cs:101 (BYN1065 message hard-codes the old order); bayan-grammar.md (waqf §6) | The new order is still only proposed. bayan-views.md:3 and the style proposal :157 call it canonical. Function types such as `() -> T fails DbError` (spec :551) are not covered. | Keep "proposed" wording until the round-4 batch. List the BYN1065 text, style.md:16, spec :151/:147/:1592 and the function-type order as edits. |
| I9 | MEDIUM | bayan-views.md:3 ("round-4 clause order") vs bayan-concurrency.md:5 ("proposed round-4 clause order") | — | Status wording differs between the docs. | Use "proposed" in both. |
| I10 | MEDIUM | bayan-concurrency.md:292 ("area 6xxx"), :301 ("3xxx") | revisions/0017:30, 27 | Consistent with A2. Note only: "Replaces most of BYN3217" moves a 3xxx code into 6xxx. | State the move in the round-4 mapping. |
| I11 | MEDIUM | bayan-shorthands-proposal.md:367 (`RestInput (BYN1058)` with new text `rest {1}[] {2}`), :369 (BYN2310 text) | DiagnosticCodes.cs:95, 196; spec :282 | Changing live messages before round 4 would break goldens. | Mark both as round-4 text changes (already implied; say so). |
| I12 | LOW | bayan-style-proposal.md:128 (`@untrusted string note`) | bayan-spec.md taint model, bayan-library-tiers.md:196 (`Untrusted<T>`) | No `@untrusted` attribute exists. | Use a real attribute (e.g. `@json(name: …)`). |
| I13 | LOW | bayan-views.md:100 (`kit.db.pg`) | bayan-library-tiers.md:398 (Q1: `kit.db.pg` recommended, naming still Open) | Consistent with the recommendation, but Q1 is open. | Footnote Q1. |
| I14 | LOW | bayan-library-tiers.md:190 (`.native` grant) | bayan-spec.md:1661 (`.ffi`) | Grant name differs. Pre-existing; affects any manifest example. | Pick one. |
| I15 | LOW | bayan-concurrency.md:184 ("pool size is set in `package.byn`") | — | No field is named. | Name the field (e.g. `blockingPool:`). |
| I16 | LOW | bayan-shorthands-proposal.md:252-255 (S5 shared types) / :24 (single-letter type names forbidden) | bayan-spec.md:498 (`IParse<TSelf>`), spec examples using `T`, `E` | Fine, but S2's "user type names need two or more characters" needs a migration check over existing one-letter user types. | Add it to the §8 migration tests. |

## 3. Stale references

| # | Severity | Where | vs | Issue | Suggested resolution |
|---|---|---|---|---|---|
| S1 | MEDIUM | bayan-design-round-4-draft.md:15 ("SPEC.md and GRAMMAR.md are not in the project") | docs/bayan-spec.md, docs/bayan-grammar.md exist | Stale. | Delete the sentence and check the section references. |
| S2 | MEDIUM | bayan-shorthands-proposal.md:181 ("SPEC :1125") | bayan-spec.md:1437 (`T max<T>(ReadOnlySpan<T> xs) where T : IOrder<T>`); :1125 is `provide` text | Wrong line. | Cite §11, :1437. |
| S3 | MEDIUM | bayan-shorthands-proposal.md:9, 57, 122, 255, 383 ("§5": `?` and `[N]` bind to the type they follow) | bayan-spec.md:720 (§5 is Naming and morphology) | No such rule in spec §5. It may be GRAMMAR §5 (declarations) or nowhere. | Cite the actual location, or mark it as a new rule. |
| S4 | MEDIUM | bayan-concurrency.md:311 ("See the packages doc, section 6") | docs/ has no packages doc | Missing file. | Link the file, or inline the reference. |
| S5 | LOW | bayan-shorthands-proposal.md:319 ("the Nov 4 standard"), :425/:444 ("Round-4 §2") | bayan-style-proposal.md (undated) | Unclear what "Nov 4 standard" refers to. | Name bayan-style-proposal.md. |
| S6 | LOW | bayan-style-proposal.md:3, 84, 380 ("STYLE.md") | docs/bayan-style.md | Alias (also used in revisions/0016:36). | Use the file path. |
| S7 | LOW | bayan-style-proposal.md:364 ("the existing 5.2b test") | — | Unverified reference; I did not check it. | Cite the test name. |
| S8 | LOW | bayan-shorthands-proposal.md:464-471 (`Parser.cs:757-812`, `:520-562`, `:659-667`) | src/ (spec 011 in flight) | Line numbers will drift. I did not verify them. | Cite function names only. |
| S9 | LOW | bayan-design-round-4-draft.md:358 (open question: spelling of a failing function type) | bayan-spec.md:551 (`() -> T fails DbError` already specified) | Partly answered by the spec already. | Narrow the question to the order of `fails` and `uses` in function types. |

## 4. Re-proposals of Appendix A items

| # | Severity | Where | vs | Issue | Suggested resolution |
|---|---|---|---|---|---|
| A1 | HIGH | bayan-concurrency.md (all `spawn`/`join`) | Appendix A :2449 `async`/`await` | Effectively replaces a settled item (see C1). | Needs an explicit user decision and a revision; do not treat it as additive. |
| A2 | HIGH | bayan-concurrency.md:15, 160 (`Mutex.with`) | Appendix A :2479 ("`with` as the handler keyword" rejected; "it is struct update") | Gives `with` a second, scoped-resource meaning. | Rename (I2). |
| A3 | MEDIUM | bayan-design-round-4-draft.md:29, 39-47, 360 (reopen per-project `booleans`/`primitives`) | Appendix A :2458 ("both boolean spellings, chosen per project by `style.booleans`") | Re-proposes a settled item. The retrospective framing is fine, but :360 lists it as an open question. | Mark it as a deliberate reopen needing a user decision, or keep the setting and treat it as display only. |
| A4 | LOW | bayan-shorthands-proposal.md:173 / style-proposal (none) | Appendix A :2466 (views are `span`, `mutableSpan()`, `copy()`) | Not a re-proposal: S1 keeps these producers (:169-170). | None. |

## 5. Per-doc action list

**bayan-views.md**
- Now (docs-only):
  - Fix C12 (`catch error =>`), C13 (manifest keys), C22 (`_ =`), I13.
  - Label the clause order "proposed" (I9).
  - Add `markup` to the round-4 reserved-keyword list in the round-4 draft (V11). No spec edit yet.
- Wait for round 4 (after 012): reserve the `markup` keyword (spec §2.4 reserved list; selfhost lexer).
- Wait for v0.2: the feature itself, `kit.web`/`kit.ui`, `bayan import html`.

**bayan-concurrency.md**
- Now:
  - Fix C5, I1-I5, I15, S4.
  - Write a reconciliation section against spec §13: C1, C4, C6, C16-C19.
  - Get the user's decision on A1 (does `spawn` replace `async`/`await`?).
  - Settle spelling (I6).
- Round 4: spec §13/§14 rewrite as a revision; the `canceling` contextual word and `scope`/`spawn`/`shield` keywords or contextual words; 6xxx codes at renumbering.
- v0.2: runtime.

**bayan-shorthands-proposal.md**
- Now:
  - Fix C14, C20, C21, C25, C26, S2, S3, S5, S8.
  - Mark the BYN1058/BYN2310 text changes as round-4 (I11).
  - Replace BYN4xxx with A2 `x9xx` lint codes (C7).
  - Move `--display` to LSP (C15).
- Round 4 batch: everything else (parser, binder, migration). All of it touches src/selfhost.

**bayan-style-proposal.md**
- Now:
  - Fix C7, C10, C11, I6, I12.
  - Reword C9 in spec §19.2 and bayan-style.md.
  - Resolve C21 with the shorthands doc.
  - Update bayan-style.md defaults (C8) together with a spec §19.2 revision. Docs-only, and §19.2 says formatter behaviour is not in v0.1.
- Wait: item 3 (own-line `{`) needs the waqf revision and a parser change (round 4).
- After 011: formatter and lint implementation touch src/selfhost.

**bayan-design-round-4-draft.md**
- Now:
  - Fix C2 (the async/await row), C3, I7 (use BYN7013/BYN1070), S1, S9, C24 (spec §21.2/§25 vs tiers §10).
  - Add markup reservation and the concurrency spec as round-4 rows.
  - Flag A3 as a deliberate reopen.
- Round 4 (first commit after 012): flip BYN1065, then format; the rest of the batch.

Not reached: bayan-grammar.md body (waqf §6, §5), laws-and-decisions.md, specs/015-tooling/015-spec.md (014 vs 015 numbering and std module names unchecked), the handoff Questions beyond Q745/Q371, revision 0016 in full, and verification of the src line numbers and the "5.2b" test.
