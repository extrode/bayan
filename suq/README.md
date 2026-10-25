# Sūq (سوق) — paper sources

The paper version of three Sūq parts (HANDOFF §21, `docs/bayan-design-round-3.md`): the storefront, the order lifecycle and warehouse packing. The code is paper: it is not compiled and no test runs it. The v0.1 compiler does not implement round 3 yet.

The full reference app, with the other parts, `package.byn`, the C# baseline (`suq-csharp/`) and the round-3 checker, is `docs/suq-reference-app.zip`. These files are copied from it unchanged, renamed from `*.byn.cs` to `*.byn`.

```
storefront/    HTTP handlers: validation, trust layers, typestate narrowing   (zip: src/storefront/)
orders/        the Order type and its states                                  (zip: src/model/order.byn.cs)
packing/       zero-allocation packing, typed indices, labels                 (zip: src/warehouse/packing.byn.cs)
fail/          compile-fail corpus, one snippet per diagnostic (README.md)    (zip: fail/)
```

Coverage: `docs/bayan-reference-app-coverage.md`. Friction log: `docs/bayan-reference-app-friction.md`.
