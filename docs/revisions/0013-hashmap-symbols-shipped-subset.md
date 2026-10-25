# 0013 — `Symbol` and `Map` keys: the shipped subset

Status: applied 2026-10-25 by spec 008 T12 (the spec's docs task). It records what spec 008 shipped and decides nothing beyond spec 008 and HANDOFF Questions 371–381.

## Problem

SPEC §3.3 described `Symbol` in two sentences, without the conversion rules, `Symbol.intern`, `.text` or the key rules. SPEC §3.5 listed `HashMap<K, V>` as an unordered, faster standard collection; spec 008 does not add it (Q371) and reports it as BYN2830. SPEC §3.5 and §16 did not say which types a `Map` key may have, and spec 008 extends that set to `Symbol`, tuples and structs. GRAMMAR's input-form table did not mention `HashMap`.

## Decision

1. SPEC §3.3 gains a **Supported subset (spec 008)** block in the form of §3.15's spec 006 and §3.16's spec 007 blocks, and a **Self-hosting gaps** block. The two design sentences stay above them.
2. The subset: `Symbol` as a copyable 4-byte built-in type, defaultable to the symbol of `""`; `==`/`!=` by ID, ordering by text; a non-interpolated string literal converts where `Symbol` is expected, any other `string` is BYN2832 with a fix to `Symbol.intern(…)`; `Symbol.intern` is `uses alloc` and its text lives until the program exits; `sym.text` and interpolation give the text; no ID member and no cast; one table per run, single-threaded.
3. SPEC §3.5: `HashMap<K, V>` leaves the list of collections and becomes BYN2830 with a fix to `Map<K, V>` (Q371 stays open: an unordered map can be added later). The `Map` key types are listed, with BYN2760 and BYN2831, and composite keys hash field by field. `HashMap` leaves the `unique` collection list.
4. SPEC §16's map bullet gains the key types and points to §3.5.
5. GRAMMAR §3: a note after the input-form table that `HashMap<K, V>` is BYN2830, a checker diagnostic with a fix that `bayan format` does not apply. No grammar rule changes.
6. Laws added by spec 008: L015 "Interning is identity on text" and L016 "Equal keys are one entry".

## Sections

- SPEC §3.3: the two new blocks.
- SPEC §3.5: the collections bullet, the `Map` ordering bullet, the `unique` collections bullet, a new key-types bullet.
- SPEC §16: the "Maps:" bullet.
- GRAMMAR §3: the note after the input-form table.

## Codes

- BYN2830–BYN2832: added (2833–2839 spare).
- BYN2760: help rewritten to list the new key kinds.

## Sūq files

None changed. `suq/packing/packing.byn` initializes `Symbol` fields from literals (accepted by spec 008) inside a module-level `const` array of structs, which stays BYN2229 (Q379), so the file stays paper code.

## Effect on work already done

None: the blocks describe the goldens spec 008 added (`tests/cases/*/symbol-*`, `map-key-*`, `hashmap-*`, `invalid-map-key`, `panic-handler-symbol-intern`, `run/selfhost-symbol-tables`).
