# 002 — Tasks
Plan: ./002-plan.md

- [x] T1 Map API: index read/write, getOr, slot, length, contains; drop get/set/count/containsKey; migrate goldens and MapModelTests; BYN2785 — files: BuiltinMembers.cs, binder, bayan-rt.{h,c}, Lowering, CEmitter, tests/cases/run/map-*, MapModelTests.cs — covers: R5 — done when: suite green, new run/map-index-panic, run/map-slot, diag/map-slot-no-default exist
- [x] T2 prove L003 — files: tests/Bayan.Tests/MapModelTests.cs, docs/laws/003-map-insertion-order.md — covers: R12 — done when: `[Trait("Law","L003")]` test drives Bayan programs through the new API against a model; laws-check shows L003 holds
- [x] T3 L004 coverage rules + guard table, prove L004 — files: binder switch exhaustiveness, tests/cases/diag, a law test, docs/laws/004-union-switch-exhaustive.md — covers: R9, R12 — done when: guard table test tagged L004 passes, L004 holds
- [x] T4 Shared Unicode tables: UnicodeGen `--cs-out`, Nfc.cs into Bayan.Syntax, Lexer off ICU, NoSystemUnicode test — files: tools/Bayan.UnicodeGen/*, src/Bayan.Syntax/Lexing/{Lexer.cs,Nfc.cs,UnicodeTables.g.cs}, LexerTests.cs — covers: R6 — done when: suite green, no ICU refs in lexer
- [x] T5 `--json` dump in both lexers, stdin input for bayan-lex (and `bayan lex -`) — files: TokenDump.cs, CliApp.cs, selfhost/lexer/{dump,main}.byn, runtime if stdin read missing — covers: R7, R8 — done when: both print identical JSON for every tests/cases/lex golden; stdin mode works
- [ ] T6 prove L001 and L002 — files: tests/Bayan.Tests/LexerLawTests.cs, docs/laws/00{1,2}-*.md — covers: R12 — done when: corpus test tagged L001/L002 passes; laws-check shows both hold
- [x] T7 explicit `copy(to:)` for List/Map/StrBuf, BYN2784 — files: BuiltinMembers.cs, runtime, emitter, run/collection-copy, diag/collection-copy-unique-element — covers: R3 — done when: suite green
- [ ] T8 Ownership: IsUnique, `move` expr/params, OwnershipAnalysis (BYN2780–2783), retire BYN2762, migrate selfhost/lex-diff/tally-lite/goldens — files: TypeFacts, Parser, Binder, OwnershipAnalysis.cs, RegionAnalysis.cs, docs/diagnostics, tests/cases — covers: R1, R2, R4 — done when: suite green, all spec R1/R2 acceptance cases exist as diag/run goldens
- [x] T9 `Untrusted<T>`, `trust`, BYN2787 — files: TypeResolver, binder, emitter, run/untrusted-trust, diag/trust-reason — covers: R10 — done when: suite green
- [x] T10 `Io.runBatch` + `byn_batch_cmdline` + BYN2786 + native escape table test — files: bayan-rt.{h,c}, binder, emitter, tests/native/batch-cmdline.c, RuntimeNativeTests.cs, diag/run-batch-untrusted — covers: R11 — done when: suite green on macOS, escape table test passes
- [x] T11 revise L005 statement (`%~N`), draft L006 unique-never-aliases as proposed — files: docs/laws/005-*, docs/laws/006-* — covers: law candidates — done when: files written, status proposed
- [ ] T12 enact L005, L006 — user — done when: user sets status enacted (awaiting)
- [ ] T13 prove L005, L006 — after T12 — done when: laws-check holds
- [ ] T14 handoff Q55/Q56 answered + new Questions, progress log, CHANGELOG, status — files: docs/bayan-handoff.md, CHANGELOG.md, work/status/* — covers: R13 — done when: written
