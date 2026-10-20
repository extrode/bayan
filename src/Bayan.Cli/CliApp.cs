using Bayan.Backend.C;
using Bayan.Lowering;
using Bayan.Semantics;
using Bayan.Syntax.Ast;
using Bayan.Syntax.Diagnostics;
using Bayan.Syntax.Lexing;
using Bayan.Syntax.Parsing;
using Bayan.Syntax.Text;

namespace Bayan.Cli;

public static class CliApp
{
    public const string Version = "0.1.0-dev";

    public const int ExitOk = 0;
    public const int ExitFailure = 1;
    public const int ExitUsage = 2;

    private const string Usage = """
        usage: bayan <command> [args]

        commands:
          lex <file>           print the token stream
          parse [--ast] <file> check syntax; with --ast print the syntax tree
          check <path>         report diagnostics for a program (a file or directory)
          build <path>         compile to an executable [-o <file>] [--mode debug|safe|fast] [--emit-c]
          run <path>           compile and run [--mode debug|safe|fast] [-- <program args>]
          test <path>          compile and run the tests, each in its own process [--mode debug|safe|fast]
          explain <code>       explain a diagnostic, e.g. `bayan explain BYN0003`
          --version            print the compiler version
          --help               print this message
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

    private sealed record BuildOptions(string Path, BuildMode Mode, string? Output, bool EmitC, string[] ProgramArgs);

    private static BuildOptions? ParseBuildOptions(string[] args, TextWriter stderr, bool allowOutput)
    {
        string? path = null, output = null;
        var mode = BuildMode.Safe;
        var emitC = false;
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
        if (path is null)
        {
            stderr.WriteLine("bayan: expected a file or directory");
            return null;
        }
        return new BuildOptions(path, mode, output, emitC, programArgs);
    }

    /// <summary>Compiles a program; prints diagnostics; null on errors.</summary>
    private static Compilation? Compile(string path, TextWriter stderr, bool includeTests = false)
    {
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            stderr.WriteLine($"bayan: not found: {path}");
            return null;
        }
        var c = Compilation.FromPath(path, new CompilationOptions(includeTests));
        if (c.Diagnostics.Count > 0) stderr.Write(DiagnosticRenderer.RenderAll(c.Diagnostics.Items, includePath: true));
        return c.Diagnostics.HasErrors ? null : c;
    }

    /// <summary>The C translation unit for an error-free compilation.</summary>
    public static string EmitC(Compilation c) => CEmitter.Emit(Lowerer.Lower(c));

    private static int Check(string[] args, TextWriter stdout, TextWriter stderr)
    {
        if (ParseBuildOptions(args, stderr, allowOutput: false) is not { } o) return ExitUsage;
        return Compile(o.Path, stderr) is null ? ExitFailure : ExitOk;
    }

    private static int Build(string[] args, TextWriter stdout, TextWriter stderr)
    {
        if (ParseBuildOptions(args, stderr, allowOutput: true) is not { } o) return ExitUsage;
        if (Compile(o.Path, stderr) is not { } c) return ExitFailure;
        var source = EmitC(c);
        if (o.EmitC)
        {
            stdout.Write(source);
            return ExitOk;
        }
        var output = o.Output ?? DefaultOutput(o.Path);
        var result = Toolchain.Build(source, o.Mode, output);
        if (!result.Succeeded)
        {
            stderr.WriteLine("bayan: " + result.Errors);
            return ExitFailure;
        }
        return ExitOk;
    }

    private static string DefaultOutput(string path)
    {
        var full = Path.GetFullPath(path);
        var name = Directory.Exists(full) ? Path.GetFileName(full.TrimEnd(Path.DirectorySeparatorChar)) : Path.GetFileNameWithoutExtension(full);
        if (name == "main" && !Directory.Exists(full)) name = Path.GetFileName(Path.GetDirectoryName(full)!);
        return name + (OperatingSystem.IsWindows() ? ".exe" : "");
    }

    private static int RunProgram(string[] args, TextWriter stderr)
    {
        if (ParseBuildOptions(args, stderr, allowOutput: false) is not { } o) return ExitUsage;
        if (Compile(o.Path, stderr) is not { } c) return ExitFailure;
        var result = Toolchain.Build(EmitC(c), o.Mode);
        if (!result.Succeeded)
        {
            stderr.WriteLine("bayan: " + result.Errors);
            return ExitFailure;
        }
        var psi = new System.Diagnostics.ProcessStartInfo(result.ExecutablePath!) { UseShellExecute = false };
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
        if (Compile(o.Path, stderr, includeTests: true) is not { } c) return ExitFailure;
        var program = Lowerer.Lower(c);
        var result = Toolchain.Build(CEmitter.Emit(program), o.Mode);
        if (!result.Succeeded)
        {
            stderr.WriteLine("bayan: " + result.Errors);
            return ExitFailure;
        }
        var count = program.Tests.Count;
        stdout.WriteLine($"running {count} test{(count == 1 ? "" : "s")}");
        int passed = 0, failed = 0, panicked = 0;
        var total = System.Diagnostics.Stopwatch.StartNew();
        for (var i = 0; i < count; i++)
        {
            var outcome = RunTest(result.ExecutablePath!, i);
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
        return failed + panicked == 0 ? ExitOk : ExitFailure;
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
            return ExitFailure;
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
