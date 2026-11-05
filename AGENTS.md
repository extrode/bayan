# Bayan — agent instructions

- Source of truth: `docs/bayan-spec.md`. Work plan, milestones, open questions and progress log: `docs/bayan-handoff.md`. Read both before working.
- Appendix A of the spec lists settled decisions; do not re-propose them. Spec gaps go to *Questions* in the handoff, with the most conservative option implemented meanwhile.
- Update the handoff's *Progress log* at the end of every working session.
- Stack: C# / .NET 10, NativeAOT for `src/` (`IsAotCompatible`, warnings are errors). No reflection, no `Reflection.Emit`, no parser generators.
- Lexer works over `ReadOnlySpan<byte>` UTF-8; tokens are structs; positions are byte offsets (`TextSpan`).
- Tests: `dotnet test -v q`. Golden cases live in `tests/cases/<kind>/`; `BAYAN_UPDATE=1 dotnet test` rewrites expected files. Every new diagnostic needs a `diag/` case, every runtime feature a `run/` case.
- Diagnostic codes `BYN####`: 0001–0999 lexer, 1000–1999 parser, 2000–3999 semantics, 4000–4999 backend. Rendered format is fixed by `DiagnosticRendererTests`.
- Scratch goes in `tmp/` (gitignored).
- Work in flight: `docs/handoffs/` (dated files); spec 010 continues per `docs/handoffs/2026-10-26-spec-010-dropoff.md`, then `docs/handoffs/2026-10-28-spec-010-addendum.md` (current state, read it second). Tooling ports (spec 015, replacing Python/Deno/Node scripts with Bayān or shell): `docs/handoffs/2026-11-02-spec-015-tooling.md`; library placement: `docs/bayan-library-tiers.md`.
- Spec 010 work lands only through `scripts/selfhost/land.sh` (guard + an independent review against the C#, Q712). Tests take their expected lines from the C# implementation, never typed by hand. Port, do not invent: no fallbacks for unported siblings. The `in` parameter modifier is redundant (spec §10): leave it out of new code.
- Memory and disk bounds: read `docs/memory-leaks.md` before writing a walk, watcher, cache, child-process capture or input recursion; its *Release gate* must pass before any release.
