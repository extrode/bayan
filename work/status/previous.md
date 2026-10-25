# Previous

Spec 008, HashMap and Symbol (`docs/specs/008-hashmap-symbols/`): `Symbol`, an interned 4-byte identifier (`==` by ID, ordering by text, `Symbol.intern`, `.text`); `Map` keys over tuples, structs, enums and `Symbol` with field-wise hashing; `HashMap<K, V>` as BYN2830; BYN2831–2832; laws L015 and L016. Review fixes: typed `const` of `Symbol`, `switch` on `Symbol`, `.debug` of a `Symbol` field, use-site BYN2831 for deferred struct keys.
