# Bayan: self-hosting plan

**Short answer:** small tools can be written in Bayan right after v0.1; the compiler itself can move to Bayan once v0.2 lands, because v0.2 adds the features a compiler can't do without.

## What a compiler needs from its own language

| Needed for | Feature | Status |
|---|---|---|
| Reading source files | File I/O, UTF-8 strings, slicing | v0.1 |
| Error reporting | `fails`, error unions, `defer` | v0.1 |
| Fast allocation of syntax trees | Arenas, `context` allocator | v0.1 |
| Tokens and diagnostics | Structs, fixed arrays, `List<T>` | v0.1 |
| Syntax-tree nodes | Unions + exhaustive `switch` | v0.2 |
| Symbol tables | `Map` / `HashMap`, `Symbol` interning | v0.2 |
| Reusable passes | Generics, interfaces, iterators | v0.2 |
| Nodes referring to source text | Borrowed struct fields (full regions) | v0.2 (byte offsets instead of slices work in v0.1) |
| Running `zig cc` | Spawning processes | v0.2 (`Io.Blocking`) |
| Tests | `test` blocks, `property`, `fuzz` | v0.1 / v0.2 |

## Phases

| Phase | When | What happens |
|---|---|---|
| 1. Dogfood with tools | After v0.1 | Write small real programs in Bayan: release scripts, the corpus checker, small CLI utilities. Each exposes design problems cheaply. |
| 2. Port the lexer | After v0.2 | Write `bayan-lex` in Bayan, compiled by the C# compiler. Run it differentially against the C# lexer on the whole test corpus: identical tokens or the test fails. |
| 3. Port component by component | Next | Parser, then semantics, then the C emitter, each verified the same way against the C# implementation's output. |
| 4. Self-compile | Once all components are ported | The C# compiler (stage 0) builds the Bayan compiler (stage 1). Stage 1 builds itself (stage 2), and stage 2 builds itself (stage 3). Stages 2 and 3 must be byte-identical. Reached by spec 012 (2026-11-06, in branch): stages 2 and 3 emit byte-identical C in `safe` and `debug` on the same host (`BootstrapTests`, L027); cross-host identity is not claimed (L027 says so). |
| 5. Retire the C# compiler | When stages 2 and 3 match on every platform | The C# compiler is frozen and kept only for verification. |

## Bootstrapping from source afterwards

Commit the compiler's own generated C (`bootstrap/bayan.c`) and update it at each release. Building Bayan from source then needs only a C compiler. Zig does something similar with a checked-in WebAssembly build of its compiler.

## Keeping the frozen C# compiler

A self-hosting compiler can in principle hide malicious code that reinserts itself every time it compiles itself (Ken Thompson's "Trusting Trust" attack). The defense is **diverse double-compiling**: build the Bayan compiler with two independent compilers and check that the results match. The frozen C# compiler is that second compiler, and it completes the isnād provenance chain for the compiler itself.

## Trade-offs

| Benefit | Cost |
|---|---|
| Daily use exposes design flaws early | Two compilers to keep in step during phases 2–4 |
| The compiler becomes the largest real-world Bayan program and test | Compiler bugs can hide inside the compiler that compiles them, which is why the stage 2/3 comparison matters |
| Contributors only need to know Bayan | The C# compiler's tooling (debuggers, profilers) is more mature at first |

Other languages took different amounts of time: Go self-hosted in version 1.5, Zig several years after it started, and Rust early on by bootstrapping from OCaml. The real limit for Bayan is how quickly v0.2 becomes stable, since porting a compiler while its language is still changing doubles the work.

## Next step

Add a "self-hosting readiness" section to the v0.2 handoff listing the features in the first table, so v0.2 is explicitly scoped to make phase 2 possible.
