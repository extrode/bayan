# bootstrap

`bayan.c` is the self-hosted compiler (`selfhost/driver`) as C: the C that stage 1 emits for
`build --emit-c --mode safe --package selfhost selfhost/driver` (spec 012 R11). `bayan-rt.h` and
`bayan-rt.c` are byte-for-byte copies of `src/runtime/bayan-rt.h` and `src/runtime/bayan-rt.c`.
`BootstrapTests` fails when any of the three differs from what it copies.

Build it with a C11 compiler and no .NET or Zig, from the repository root (the flags are the
`safe` ones of `src/Bayan.Backend.C/Toolchain.cs`):

```
cc -std=c11 -O2 -DBYN_NDEBUG -fwrapv -ffp-contract=off bootstrap/bayan.c bootstrap/bayan-rt.c -lm
```

The result is `a.out`; `./a.out version` prints `bayan <version> (selfhost)`.

Regenerate the three files with `BAYAN_UPDATE=1 dotnet test --filter FullyQualifiedName~BootstrapTests`.
`scripts/selfhost/land.sh` does this when it lands a change under `selfhost/` or `src/runtime/`.
