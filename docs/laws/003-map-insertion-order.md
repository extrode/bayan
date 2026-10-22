---
id: L003
title: Map iterates live keys in first-insertion order
status: enacted
kind: invariant
scope: src/Bayan.Semantics/Binding/BuiltinMembers.cs
proof: tests/Bayan.Tests/MapModelTests.cs
check: dotnet test -v q --filter "Law=L003"
enacted: 2026-10-20
---

## Statement
After any sequence of `map[k] = v`, `remove`, `clear` and `slot(k)` on a `Map<K, V>`, iteration visits exactly the live keys, each once, in the order each was first inserted since it was last removed (an update keeps its position; a key re-inserted after removal goes to the end); `getOr(k, d)` returns the last value set for every live key and `d` otherwise; `map[k]` panics exactly for keys that aren't live; `length` equals the number of live keys.

## Counterexample looks like
`m[a] = 1; m[b] = 2; m.remove(a); m[a] = 3` iterating as `a, b` instead of `b, a`, `m.count` being 3 after it, or `m[c]` returning a default instead of panicking.
