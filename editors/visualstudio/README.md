# Bayan in Visual Studio 2022

Two layers, same assets as VS Code.

## 1. Colorization now (no build)

Run `install-textmate.ps1`. It copies the TextMate grammar and language configuration to
`%USERPROFILE%\.vs\Extensions\bayan`, which Visual Studio loads for `.byn` files on restart.

## 2. Language server (completion, hover, go to definition, diagnostics)

`Bayan.VisualStudio/` is a VisualStudio.Extensibility (out-of-process) extension that starts
`bayan-lsp` over stdio. Build it on Windows with VS 2022 17.10+ and the "Visual Studio extension
development" workload; F5 launches the experimental instance. Set `BAYAN_LSP` to the server path if
`bayan-lsp` is not on `PATH`.

Status: skeleton, not built here. Check the package versions against NuGet and the
LanguageServerProvider sample before the first build.

## 3. Debugging

The C backend writes `#line N "file.byn"`, so the native debugger maps the program back to `.byn`.
Build with `bayan build --debug` (MSVC or clang-cl, PDB output), then use Open Folder with a
`launch.vs.json` entry of `"type": "native"` pointing at the built executable. Breakpoints in
`.byn` files depend on VS accepting breakpoints in a TextMate-colored file: verify first.
