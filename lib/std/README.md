# lib/std

The std library: Bayān modules a program gets with `import std.<name>` (spec 016).

- One directory per module: `lib/std/<a>/<b>/*.byn` is `std.a.b`. A module is the `.byn` files directly in its
  directory; `*.test.byn` files beside them are its tests and are not part of the module.
- No `extern`: std source calls builtins and other std modules only.
- The source is embedded in the compiler at build time; nothing is read from here at run time, and a program cannot
  replace a std module (a package's own `std/` directory is not consulted).
