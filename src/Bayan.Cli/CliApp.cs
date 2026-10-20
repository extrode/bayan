using Bayan.Backend.C;
using Bayan.Lowering;
using Bayan.Semantics;
using Bayan.Syntax.Ast;
using Bayan.Syntax.Diagnostics;
using Bayan.Syntax.Lexing;
using Bayan.Syntax.Packages;
using Bayan.Syntax.Parsing;
using Bayan.Syntax.Text;

namespace Bayan.Cli;

public static class CliApp
{
    public const string Version = "0.1.0-dev";

    public const int ExitOk = 0;
    public const int ExitFailure = 1;
    public const int ExitUsage = 2;
    public const int ExitTestFailure = 3;
    public const int ExitToolchain = 4;

    private const string Usage = """
        usage: bayan <command> [args]

        commands:
          new <name>           create an app in a new directory [--lib|--script] [--vcs git|none] [--ci github|none]
          init [<dir>]         add a project's missing files to a directory (same options as new)
          lex <file>           print the token stream
          parse [--ast] <file> check syntax; with --ast print the syntax tree
          check <path>         report diagnostics for a program (a file or directory)
          build <path>         compile to an executable [-o <file>] [--mode debug|safe|fast] [--emit-c]
          run <path>           compile and run [--mode debug|safe|fast] [-- <program args>]
          test <path>          compile and run the tests, each in its own process [--mode debug|safe|fast]
          explain <code>       explain a diagnostic, e.g. `bayan explain BYN0003`
          <script>.byn         run a script, so `#!/usr/bin/env bayan` works
          --version            print the compiler version
          --help               print this message

        check, build, run and test take a project directory (one with `package.byn`; the default
        inside a project), a script (a file starting with `#!`), or a file or directory of `.byn` files.

        options for check, build, run and test:
          --diagnostics json   print each diagnostic to stderr as one JSON object per line
                               (code, severity, file, span, message, label, help, fixes)

        exit codes:
          0  success
          1  compile errors
          2  usage errors (unknown command or option, missing path)
          3  test failures
          4  toolchain problems (no C compiler, unwritable cache, C compiler failure)

        `bayan run` exits 1 when compilation fails and otherwise passes the program's own exit code
        through unchanged, so it cannot tell a build failure from a program that exits 1. When that
        matters, run `bayan build` first or read `--diagnostics json`.
        """;

    public static int Run(string[] args, TextWriter stdout, TextWriter stderr)
    {
        if (args.Length == 0)
        {
            stderr.WriteLine(Usage);
            return ExitUsage;
        }

        var rest = args[1..];
        switch (args[0])
        {
            case "--version":
            case "version":
                stdout.WriteLine($"bayan {Version}");
                return ExitOk;
            case "--help":
            case "-h":
            case "help":
                stdout.WriteLine(Usage);
                return ExitOk;
            case "lex":
                return Lex(rest, stdout, stderr);
            case "parse":
                return Parse(rest, stdout, stderr);
            case "explain":
                return Explain(rest, stdout, stderr);
            case "check":
                return Check(rest, stdout, stderr);
            case "build":
                return Build(rest, stdout, stderr);
            case "run":
                return RunProgram(rest, stderr);
            case "test":
                return Test(rest, stdout, stderr);
            case "new":
                return Scaffold.New(rest, stdout, stderr);
            case "init":
                return Scaffold.Init(rest, stdout, stderr);
            case var file when file.EndsWith(".byn", StringComparison.Ordinal) && File.Exists(file):
                // `#!/usr/bin/env bayan` runs `bayan script.byn [args]`.
                return RunProgram([file, "--", .. rest], stderr);
            default:
                stderr.WriteLine($"bayan: unknown command `{args[0]}`");
                stderr.WriteLine(Usage);
                return ExitUsage;
        }
    }

    private static int Lex(string[] args, TextWriter stdout, TextWriter stderr)
    {
        if (!TryReadSingleFile(args, stderr, out var source)) return ExitUsage;
        var result = Lexer.Lex(source);
        stdout.Write(TokenDump.Dump(result));
        return result.Diagnostics.Any(d => d.IsError) ? ExitFailure : ExitOk;
    }

    private static int Parse(string[] args, TextWriter stdout, TextWriter stderr)
    {
        var printAst = args.Contains("--ast");
        if (!TryReadSingleFile(args.Where(a => a != "--ast").ToArray(), stderr, out var source)) return ExitUsage;
        var tree = SyntaxTree.Parse(source);
        if (printAst)
        {
            stdout.Write(AstDump.Dump(tree));
        }
        else if (tree.Diagnostics.Count > 0)
        {
            stderr.Write(DiagnosticRenderer.RenderAll(tree.Diagnostics, includePath: true));
        }
        return tree.Diagnostics.Any(d => d.IsError) ? ExitFailure : ExitOk;
    }

    private sealed record BuildOptions(string Path, BuildMode Mode, string? Output, bool EmitC, bool Json, string[] ProgramArgs);

    private static BuildOptions? ParseBuildOptions(string[] args, TextWriter stderr, bool allowOutput)
    {
        string? path = null, output = null;
        var mode = BuildMode.Safe;
        bool emitC = false, json = false;
        var programArgs = Array.Empty<string>();
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--":
                    programArgs = args[(i + 1)..];
                    i = args.Length;
                    break;
                case "--mode" when i + 1 < args.Length:
                    if (!Toolchain.TryParseMode(args[++i], out mode))
                    {
                        stderr.WriteLine($"bayan: unknown mode `{args[i]}` (expected debug, safe or fast)");
                        return null;
                    }
                    break;
                case "--diagnostics" when i + 1 < args.Length:
                    switch (args[++i])
                    {
                        case "json": json = true; break;
                        case "text": json = false; break;
                        default:
                            stderr.WriteLine($"bayan: unknown diagnostics format `{args[i]}` (expected json or text)");
                            return null;
                    }
                    break;
                case "-o" when allowOutput && i + 1 < args.Length:
                    output = args[++i];
                    break;
                case "--emit-c" when allowOutput:
                    emitC = true;
                    break;
                default:
                    if (path is not null || args[i].StartsWith('-'))
                    {
                        stderr.WriteLine($"bayan: unexpected argument `{args[i]}`");
                        return null;
                    }
                    path = args[i];
                    break;
            }
        }
        if (path is null && File.Exists("package.byn")) path = ".";
        if (path is null)
        {
            stderr.WriteLine("bayan: expected a file or directory (or run inside a project with a `package.byn`)");
            return null;
        }
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            stderr.WriteLine($"bayan: not found: {path}");
            return null;
        }
        return new BuildOptions(path, mode, output, emitC, json, programArgs);
    }

    /// <summary>A compiled program, with its manifest and root when it is a project.</summary>
    private sealed record Compiled(Compilation Compilation, Manifest? Manifest, string? ProjectRoot);

    /// <summary>
    /// Compiles a project directory (one with <c>package.byn</c>), a script (a file starting with
    /// <c>#!</c>, compiled alone) or a file or directory (the whole directory is the entry module).
    /// Prints diagnostics; null on errors.
    /// </summary>
    private static Compiled? Compile(BuildOptions o, TextWriter stderr, bool includeTests = false)
    {
        Compilation c;
        Manifest? manifest = null;
        string? root = null;
        if (Directory.Exists(o.Path) && File.Exists(Path.Combine(o.Path, "package.byn")))
        {
            root = Path.GetFullPath(o.Path);
            manifest = Manifest.Read(Path.Combine(root, "package.byn"));
            Report(o, manifest.Diagnostics, stderr);
            if (manifest.HasErrors) return null;
            var entry = EntryModule(manifest);
            if (!Directory.Exists(Path.Combine(root, "src", entry)))
            {
                stderr.WriteLine($"bayan: `{manifest.Name}` is {(manifest.Kind == PackageKind.Lib ? "a library" : "an app")}, so its code goes in `src/{entry}/`, which doesn't exist");
                return null;
            }
            c = Compilation.FromProject(root, entry, new CompilationOptions(includeTests, Library: manifest.Kind == PackageKind.Lib));
        }
        else
        {
            c = Compilation.FromPath(o.Path, new CompilationOptions(includeTests), wholeDirectory: !IsScript(o.Path));
        }
        Report(o, c.Diagnostics.Items, stderr);
        return c.Diagnostics.HasErrors ? null : new Compiled(c, manifest, root);
    }

    private static void Report(BuildOptions o, IReadOnlyList<Diagnostic> diagnostics, TextWriter stderr)
    {
        if (diagnostics.Count == 0) return;
        stderr.Write(o.Json ? DiagnosticJson.RenderAll(diagnostics) : DiagnosticRenderer.RenderAll(diagnostics, includePath: true));
    }

    /// <summary>An app's code is <c>src/app/</c>; a library's is <c>src/&lt;name&gt;/</c> with the name in camelCase.</summary>
    public static string EntryModule(Manifest manifest) =>
        manifest.Kind == PackageKind.Lib ? Manifest.CamelOf(manifest.ShortName) : "app";

    private static bool IsScript(string path)
    {
        if (!File.Exists(path)) return false;
        Span<byte> head = stackalloc byte[2];
        using var f = File.OpenRead(path);
        return f.Read(head) == 2 && head[0] == '#' && head[1] == '!';
    }

    /// <summary>A library has nothing to build or run until FFI export exists.</summary>
    private static bool RefuseLibrary(Compiled compiled, string command, TextWriter stderr)
    {
        if (compiled.Manifest is not { Kind: PackageKind.Lib } m) return false;
        stderr.WriteLine($"bayan: `{m.Name}` is a library, so there is no program to {command}; `bayan test` runs its tests");
        return true;
    }

    /// <summary>Builds, or reports why the toolchain could not.</summary>
    private static string? BuildNative(string source, BuildMode mode, TextWriter stderr, string? output = null)
    {
        var result = Toolchain.Build(source, mode, output);
        if (result.Succeeded) return result.ExecutablePath;
        stderr.WriteLine("bayan: " + result.Errors);
        return null;
    }

    /// <summary>The C translation unit for an error-free compilation.</summary>
    public static string EmitC(Compilation c) => CEmitter.Emit(Lowerer.Lower(c));

    private static int Check(string[] args, TextWriter stdout, TextWriter stderr)
    {
        if (ParseBuildOptions(args, stderr, allowOutput: false) is not { } o) return ExitUsage;
        return Compile(o, stderr) is null ? ExitFailure : ExitOk;
    }

    private static int Build(string[] args, TextWriter stdout, TextWriter stderr)
    {
        if (ParseBuildOptions(args, stderr, allowOutput: true) is not { } o) return ExitUsage;
        if (Compile(o, stderr) is not { } compiled) return ExitFailure;
        if (RefuseLibrary(compiled, "build", stderr)) return ExitUsage;
        var source = EmitC(compiled.Compilation);
        if (o.EmitC)
        {
            stdout.Write(source);
            return ExitOk;
        }
        var output = o.Output ?? DefaultOutput(o.Path, compiled.Manifest);
        return BuildNative(source, o.Mode, stderr, output) is null ? ExitToolchain : ExitOk;
    }

    private static string DefaultOutput(string path, Manifest? manifest)
    {
        if (manifest is not null) return manifest.ShortName + (OperatingSystem.IsWindows() ? ".exe" : "");
        var full = Path.GetFullPath(path);
        var name = Directory.Exists(full) ? Path.GetFileName(full.TrimEnd(Path.DirectorySeparatorChar)) : Path.GetFileNameWithoutExtension(full);
        if (name == "main" && !Directory.Exists(full)) name = Path.GetFileName(Path.GetDirectoryName(full)!);
        return name + (OperatingSystem.IsWindows() ? ".exe" : "");
    }

    private static int RunProgram(string[] args, TextWriter stderr)
    {
        if (ParseBuildOptions(args, stderr, allowOutput: false) is not { } o) return ExitUsage;
        if (Compile(o, stderr) is not { } compiled) return ExitFailure;
        if (RefuseLibrary(compiled, "run", stderr)) return ExitUsage;
        if (BuildNative(EmitC(compiled.Compilation), o.Mode, stderr) is not { } executable) return ExitToolchain;
        var psi = new System.Diagnostics.ProcessStartInfo(executable) { UseShellExecute = false };
        foreach (var a in o.ProgramArgs) psi.ArgumentList.Add(a);
        using var process = System.Diagnostics.Process.Start(psi)!;
        process.WaitForExit();
        return process.ExitCode;
    }

    /// <summary>A test that runs longer than this is stopped and reported as timed out.</summary>
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(60);

    private static int Test(string[] args, TextWriter stdout, TextWriter stderr)
    {
        if (ParseBuildOptions(args, stderr, allowOutput: false) is not { } o) return ExitUsage;
        if (Compile(o, stderr, includeTests: true) is not { } compiled) return ExitFailure;
        var program = Lowerer.Lower(compiled.Compilation);
        if (BuildNative(CEmitter.Emit(program), o.Mode, stderr) is not { } executable) return ExitToolchain;
        var count = program.Tests.Count;
        stdout.WriteLine($"running {count} test{(count == 1 ? "" : "s")}");
        int passed = 0, failed = 0, panicked = 0;
        var total = System.Diagnostics.Stopwatch.StartNew();
        for (var i = 0; i < count; i++)
        {
            var outcome = RunTest(executable, i);
            var (status, tally) = outcome.ExitCode switch
            {
                0 => ("ok   ", 0),
                1 => ("FAIL ", 1),
                _ => ("PANIC", 2),
            };
            if (outcome.TimedOut) (status, tally) = ("TIME ", 2);
            if (tally == 0) passed++;
            else if (tally == 1) failed++;
            else panicked++;
            stdout.WriteLine($"  {status}  {program.Tests[i].Test.Name}  ({FormatMs(outcome.Elapsed)})");
            if (tally == 0) continue;
            var details = outcome.TimedOut ? $"timed out after {TestTimeout.TotalSeconds:0} s" : (outcome.Stdout + outcome.Stderr).TrimEnd();
            foreach (var line in details.Split('\n'))
                stdout.WriteLine("         " + line.TrimEnd('\r'));
        }
        total.Stop();
        var summary = $"{passed} passed";
        if (failed > 0) summary += $", {failed} failed";
        if (panicked > 0) summary += $", {panicked} panicked";
        stdout.WriteLine();
        stdout.WriteLine($"{count} test{(count == 1 ? "" : "s")}: {summary}  ({FormatMs(total.Elapsed)})");
        return failed + panicked == 0 ? ExitOk : ExitTestFailure;
    }

    private sealed record TestOutcome(int ExitCode, bool TimedOut, string Stdout, string Stderr, TimeSpan Elapsed);

    private static TestOutcome RunTest(string executable, int index)
    {
        var psi = new System.Diagnostics.ProcessStartInfo(executable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add("--test");
        psi.ArgumentList.Add(index.ToString(System.Globalization.CultureInfo.InvariantCulture));
        var clock = System.Diagnostics.Stopwatch.StartNew();
        using var p = System.Diagnostics.Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        if (!p.WaitForExit(TestTimeout))
        {
            p.Kill(entireProcessTree: true);
            p.WaitForExit();
            return new TestOutcome(-1, true, stdout.Result, stderr.Result, clock.Elapsed);
        }
        return new TestOutcome(p.ExitCode, false, stdout.Result, stderr.Result, clock.Elapsed);
    }

    private static string FormatMs(TimeSpan t) =>
        t.TotalMilliseconds.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " ms";

    private static int Explain(string[] args, TextWriter stdout, TextWriter stderr)
    {
        if (args.Length != 1)
        {
            stderr.WriteLine("bayan: expected one diagnostic code, e.g. `bayan explain BYN0003`");
            return ExitUsage;
        }
        var text = ExplainText(args[0]);
        if (text is null)
        {
            stderr.WriteLine($"bayan: no explanation for `{args[0]}`");
            return ExitUsage;
        }
        stdout.Write(text);
        return ExitOk;
    }

    /// <summary>The embedded <c>docs/diagnostics/BYN####.md</c> for a code, matched case-insensitively.</summary>
    public static string? ExplainText(string code)
    {
        var name = code.Trim().ToUpperInvariant();
        if (name.Length != 7 || !name.StartsWith("BYN", StringComparison.Ordinal) || name.AsSpan(3).ContainsAnyExceptInRange('0', '9'))
            return null;
        using var stream = typeof(CliApp).Assembly.GetManifestResourceStream($"diagnostics/{name}.md");
        if (stream is null) return null;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static bool TryReadSingleFile(string[] args, TextWriter stderr, out SourceText source)
    {
        source = null!;
        if (args.Length != 1)
        {
            stderr.WriteLine("bayan: expected exactly one file");
            return false;
        }
        if (!File.Exists(args[0]))
        {
            stderr.WriteLine($"bayan: file not found: {args[0]}");
            return false;
        }
        source = SourceText.FromFile(args[0]);
        return true;
    }
}
