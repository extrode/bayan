---
id: L003
title: Map iterates live keys in first-insertion order
status: proposed
kind: invariant
scope: src/runtime/bayan-rt.c
---

## Statement
After any sequence of `set`, `remove` and `clear` on a `Map<K, V>`, iteration visits exactly the live keys, each once, in the order each was first inserted since it was last removed (an update keeps a key's position; a key re-inserted after removal goes to the end), and `get(k)` returns the last value `set` for every live key and null for every other key; `count` equals the number of live keys.

## Counterexample looks like
`set(a,1); set(b,2); remove(a); set(a,3)` iterating as `a, b` instead of `b, a`, or `count` returning 3 after it.
