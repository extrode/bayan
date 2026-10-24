# 0011 — Regex after v0.1; `ValidationError` needs its type argument

Status: accepted 2026-10-25 (user decisions on HANDOFF Questions 326 and 339).

## Problem

1. **Question 326.** SPEC §16.4 lists `matches(re)` among the v0.1 string assertions and `replaced(re, by: y)` among the copies, and §7.9's `Rule` has `pattern(Regex re)`, but no `Regex` type exists and no step builds one. A regex engine is a project of its own. Patterns run against untrusted input, so a backtracking engine (.NET's, PCRE) is a ReDoS risk, and Bayan's has to be linear-time (the RE2 / Rust `regex` approach). Nothing in v0.1 needs one: not the self-hosted compiler, not the first Sūq parts.
2. **Question 339.** Each struct with `requires` fails with its own `ValidationError<T>`. A bare `ValidationError` still resolves, to a shared payload-less set, which covers none of them, and BYN2603's help prints `fails ValidationError, ValidationError`. A function that validates two types needs both instantiations, `fails ValidationError<SignupForm>, ValidationError<Address>`, and both have a case `invalid`, which §3.14 makes a compile error (BYN3202) when the two are composed into a named set.

## Decision

1. **Regex arrives after v0.1, in `std.regex`.** `s.matches(re)`, `s.replaced(re, by: y)` and `Rule.pattern(Regex re)` move out of v0.1. The constraint is fixed now: the engine runs in linear time with no backtracking, and a pattern built from `Untrusted` input still needs `Regex.escape` (§15.2). Until then a call to `matches` is an unknown member, like any other; a dedicated message ("`matches` arrives with `std.regex` after v0.1") is optional.
2. **`ValidationError` takes one type argument.** `ValidationError<T>` carries `List<Violation<T>>`, and `Violation<T>` holds a `T.Field`; without `T` there is no field type, so the bare name means nothing. A bare `ValidationError` is BYN2023 (`` `ValidationError` takes 1 type argument(s), not 0 ``). Letting the bare name cover every instantiation was rejected: it would make an open error set, and error sets are closed so that a `catch switch` over them is exhaustive.
3. **Different instantiations of one generic error set compose.** They are distinct components, matched by type:

   ```csharp
   public void register(Untrusted<string> body) fails ValidationError<SignupForm>, ValidationError<Address> { … }

   register(body) catch switch {
       ValidationError<SignupForm> e => …,
       ValidationError<Address> e => …,
   }
   ```

   BYN3202 still rejects two *different* sets sharing a case name, and the same instantiation listed twice is one component. A bare `.invalid` arm is accepted when only one instantiation is in the set and is ambiguous (BYN2227) when there are several.
4. **Diagnostics print type arguments.** Every message, label and help that names an error set names it with its type arguments: BYN2603's help becomes `` fails ValidationError<SignupForm>, ValidationError<Address> ``, and BYN2227 and BYN3202 name `ValidationError<SignupForm>`, never the bare name twice.

## Sections

- SPEC §3.14: the composition bullet for instantiations.
- SPEC §7.9: the bare name rejected; matching by type; the `Rule` listing without `pattern`; the `expects` table names `ValidationError<T>`.
- SPEC §15.4: regexes are linear-time and after v0.1.
- SPEC §16.4: `matches(re)` and `replaced(re, by: y)` moved to an after-v0.1 line.
- GRAMMAR, the `union error` bullet: instantiations of one generic set may be composed.
- HANDOFF `docs/bayan-design-round-3.md` §2 (errors) and §11 (`Rule`): the same changes.

(Question 339's answer names "SPEC §3.2 strings" and "§11 validation"; the string members are in §16.4 and validation is §7.9.)

## Codes

- BYN2023: now also raised for a bare `ValidationError` (no new code).
- BYN3202: no longer raised for two instantiations of one generic set.
- BYN2603, BYN2227, BYN3202: error-set names print with their type arguments.

## Sūq files

None affected: no Sūq source uses `matches`, `Regex` or a bare `ValidationError`.

## Effect on work already done

- Step 5.4b built `Rule` without `pattern` and left `matches` unbuilt (Questions 326, 337): nothing to remove.
- The shared payload-less `ValidationError` built-in goes away; `diag/literal-needs-try` writes `ValidationError<T>`.
- `union error X = ValidationError<A>, ValidationError<B>` (BYN3202 today) becomes valid; a `fails` clause listing both already worked.

## Diff

SPEC §3.14, after "Two components sharing a case name is a compile error (BYN3202).":

```
+- Different instantiations of one generic error set compose: `ValidationError<SignupForm>` and `ValidationError<Address>` are distinct components, matched by type (`ValidationError<SignupForm> e => …`). A bare case arm (`.invalid e`) is accepted when only one instantiation is in the set, and is ambiguous (BYN2227) otherwise.
```

SPEC §7.9, after the `Violation<T>` and `DecodeError<T>` listing:

```
+- `ValidationError` always takes its type argument: a bare `ValidationError` is BYN2023, since without `T` there is no `T.Field`. A function that validates two types lists both: `fails ValidationError<SignupForm>, ValidationError<Address>` (§3.14), and diagnostics print the type arguments.
```

SPEC §7.9 `Rule`, the `pattern` line moves to a note:

```
-      pattern(Regex re)             // it.matches(re)
+- Not in v0.1: `pattern(Regex re)` (`it.matches(re)`), with `std.regex`.
```

SPEC §15.4:

```
-Regexes are compile-time finite automata (no ReDoS).
+Regexes (`std.regex`, after v0.1) run in linear time with no backtracking (no ReDoS); a pattern built from `Untrusted` input still needs `Regex.escape` (§15.2).
```

SPEC §16.4:

```
-**Assertions:** `contains(x)`, `startsWith(x)`, `endsWith(x)`, `matches(re)`, `equals(t, as: .caseless)`. Plain equality is `==`.
+**Assertions:** `contains(x)`, `startsWith(x)`, `endsWith(x)`, `equals(t, as: .caseless)`. Plain equality is `==`. After v0.1, `matches(re)` and `replaced(re, by: y)` arrive with `std.regex` (§15.4).
-| `replaced(x, by: y)`, `replaced(re, by: y)` | yes | |
+| `replaced(x, by: y)` | yes | |
```
