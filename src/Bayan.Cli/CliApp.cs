using Bayan.Backend.C;
using Bayan.Lowering;
using Bayan.Semantics;
using Bayan.Semantics.Binding;
using Bayan.Syntax.Ast;
using Bayan.Syntax.Diagnostics;
using Bayan.Syntax.Lexing;
using Bayan.Syntax.Packages;
using Bayan.Syntax.Parsing;
using Bayan.Syntax.Text;

namespace Bayan.Cli;

public static class CliApp
{
    public const string Version = Toolchain.CompilerVersion;

    public const int ExitOk = 0;
    public const int ExitFailure = 1;
    public const int ExitUsage = 2;
    public const int ExitTestFailure = 3;
    public const int ExitToolchain = 4;

    private static string Usage => Messages.Usage.Text();

    /// <summary>Whether stdout and stderr are terminals, and the terminal's width (0 when unknown).</summary>
    public readonly record struct Terminal(bool Stdout, bool Stderr, int Width = 0);

    [ThreadStatic] private static Style? s_styleOut;
    [ThreadStatic] private static Style? s_styleErr;
    [ThreadStatic] private static bool s_stderrTty;

    public static string VersionLine => Messages.VersionLine.Text("bayan", Version, BuildInfo.Commit);

    public static int Run(string[] args, TextWriter stdout, TextWriter stderr) => Run(args, stdout, stderr, default);

    public static int Run(string[] args, TextWriter stdout, TextWriter stderr, Terminal terminal)
    {
        if (TakeGlobalOptions(ref args, stderr) is not var (color, ascii)) return ExitUsage;
        s_styleOut = TerminalStyle.Decide(color, ascii, terminal.Stdout, Environment.GetEnvironmentVariable, terminal.Width);
        s_styleErr = TerminalStyle.Decide(color, ascii, terminal.Stderr, Environment.GetEnvironmentVariable, terminal.Width);
        s_stderrTty = terminal.Stderr;
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
                stdout.WriteLine(VersionLine);
                return ExitOk;
            case "doctor":
                return Doctor.Run(rest, stdout);
            case "--help":
            case "-h":
            case "help":
                stdout.WriteLine(Usage);
                return ExitOk;
            case "lex":
                return Lex(rest, stdout, stderr);
            case "parse":
                return Parse(rest, stdout, stderr);
            case "types":
                return Types(rest, stdout, stderr);
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
            case "dist":
                return Dist.Run(rest, stdout, stderr);
            case "cache":
                return Cache(rest, stdout, stderr);
            case "clean":
                return Dist.Clean(rest, stdout, stderr);
            case "init":
                return Scaffold.Init(rest, stdout, stderr);
            case var file when file.EndsWith(".byn", StringComparison.Ordinal) && File.Exists(file):
                // `#!/usr/bin/env bayan` runs `bayan script.byn [args]`.
                return RunProgram([file, "--", .. rest], stderr);
            default:
                stderr.WriteLine(Messages.UnknownCommand.Text(args[0]));
                stderr.WriteLine(Usage);
                return ExitUsage;
        }
    }

    private static int Lex(string[] args, TextWriter stdout, TextWriter stderr)
    {
        var json = args.Contains("--json");
        args = args.Where(a => a != "--json").ToArray();
        SourceText source;
        if (args is ["-"])
        {
            using var input = new MemoryStream();
            Console.OpenStandardInput().CopyTo(input);
            source = new SourceText("<stdin>", input.ToArray());
        }
        else if (!TryReadSingleFile(args, stderr, out source)) return ExitUsage;
        var result = Lexer.Lex(source);
        stdout.Write(json ? TokenDump.ToJson(result) : TokenDump.Dump(result));
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

    private static int Types(string[] args, TextWriter stdout, TextWriter stderr)
    {
        if (ParseBuildOptions(args, stderr, allowOutput: false) is not { } o) return ExitUsage;
        if (Compile(o, stderr) is not { } compiled) return ExitFailure;
        stdout.Write(TypedDump.ToJson(compiled.Compilation));
        return ExitOk;
    }

    internal sealed record BuildOptions(string Path, BuildMode Mode, string? Output, bool EmitC, bool Json, string[] ProgramArgs, string? Target = null, bool TestJson = false, bool Changed = false, string? Base = null, bool Verbose = false, string[]? Only = null);

    private static BuildOptions? ParseBuildOptions(string[] args, TextWriter stderr, bool allowOutput, bool allowTestJson = false)
    {
        string? path = null, output = null, target = null;
        var mode = BuildMode.Safe;
        bool emitC = false, json = false, testJson = false, changed = false, verbose = false;
        string? baseRef = null;
        var only = new List<string>();
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
                        stderr.WriteLine(Messages.UnknownMode.Text(args[i]));
                        return null;
                    }
                    break;
                case "--diagnostics" when i + 1 < args.Length:
                    switch (args[++i])
                    {
                        case "json": json = true; break;
                        case "text": json = false; break;
                        default:
                            stderr.WriteLine(Messages.UnknownDiagnosticsFormat.Text(args[i]));
                            return null;
                    }
                    break;
                case "-o" when allowOutput && i + 1 < args.Length:
                    output = args[++i];
                    break;
                case "--emit-c" when allowOutput:
                    emitC = true;
                    break;
                case "--target" when allowOutput && i + 1 < args.Length:
                    target = args[++i];
                    if (target == "host") target = null;
                    else if (!Targets.IsKnown(target))
                    {
                        stderr.WriteLine(Messages.UnknownTarget.Text(target, string.Join(", ", Targets.All)));
                        return null;
                    }
                    break;
                case "--changed" when allowTestJson:
                    changed = true;
                    break;
                case "--base" when allowTestJson && i + 1 < args.Length:
                    baseRef = args[++i];
                    changed = true;
                    break;
                case "--json" when allowTestJson:
                    testJson = true;
                    break;
                case "--only" when allowTestJson && i + 1 < args.Length:
                    only.Add(args[++i]);
                    break;
                case "-v" or "--verbose":
                    verbose = true;
                    break;
                default:
                    if (path is not null || args[i].StartsWith('-'))
                    {
                        stderr.WriteLine(Messages.UnexpectedArgument.Text(args[i]));
                        return null;
                    }
                    path = args[i];
                    break;
            }
        }
        if (path is null && File.Exists("package.byn")) path = ".";
        if (path is null)
        {
            stderr.WriteLine(Messages.ExpectedPath.Text("package.byn"));
            return null;
        }
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            stderr.WriteLine(Messages.NotFound.Text(path));
            return null;
        }
        return new BuildOptions(path, mode, output, emitC, json, programArgs, target, testJson, changed, baseRef, verbose, only.Count > 0 ? [.. only] : null);
    }

    /// <summary>A compiled program, with its manifest and root when it is a project.</summary>
    internal sealed record Compiled(Compilation Compilation, Manifest? Manifest, string? ProjectRoot);

    /// <summary>
    /// Compiles a project directory (one with <c>package.byn</c>), a script (a file starting with
    /// <c>#!</c>, compiled alone) or a file or directory (the whole directory is the entry module).
    /// Prints diagnostics; null on errors.
    /// </summary>
    internal static Compiled? Compile(BuildOptions o, TextWriter stderr, bool includeTests = false)
    {
        using var progress = StartProgress(o.Json, stderr, Messages.CompileProgress.Text());
        Compilation c;
        Manifest? manifest = null;
        string? root = null;
        if (Directory.Exists(o.Path) && File.Exists(Path.Combine(o.Path, "package.byn")))
        {
            root = Path.GetFullPath(o.Path);
            manifest = Manifest.Read(Path.Combine(root, "package.byn"));
            if (manifest.Diagnostics.Count > 0) progress.Done();
            Report(o, manifest.Diagnostics, stderr);
            if (manifest.HasErrors) return null;
            var entry = EntryModule(manifest);
            if (!Directory.Exists(Path.Combine(root, "src", entry)))
            {
                progress.Done();
                stderr.WriteLine((manifest.Kind == PackageKind.Lib ? Messages.NoEntryLib : Messages.NoEntryApp).Text(manifest.Name, $"src/{entry}/"));
                return null;
            }
            c = Compilation.FromProject(root, entry, new CompilationOptions(includeTests, Library: manifest.Kind == PackageKind.Lib));
        }
        else
        {
            c = Compilation.FromPath(o.Path, new CompilationOptions(includeTests), wholeDirectory: !IsScript(o.Path));
        }
        progress.Done();
        Report(o, c.Diagnostics.Items, stderr);
        return c.Diagnostics.HasErrors ? null : new Compiled(c, manifest, root);
    }

    private static Progress StartProgress(bool json, TextWriter stderr, string phase) =>
        Progress.Start(stderr, s_styleErr ?? Style.Plain, s_stderrTty, json, Environment.GetEnvironmentVariable, phase);

    private static void Report(BuildOptions o, IReadOnlyList<Diagnostic> diagnostics, TextWriter stderr)
    {
        if (diagnostics.Count == 0) return;
        stderr.Write(o.Json ? DiagnosticJson.RenderAll(diagnostics) : DiagnosticRenderer.RenderAll(diagnostics, includePath: true, s_styleErr ?? Style.Plain));
    }

    /// <summary>
    /// Removes <c>--color auto|always|never</c> (or <c>--color=…</c>) and <c>--ascii</c> from before any <c>--</c>;
    /// null after reporting a bad value.
    /// </summary>
    private static (string Color, bool Ascii)? TakeGlobalOptions(ref string[] args, TextWriter stderr)
    {
        var color = "auto";
        var ascii = false;
        // A script's arguments are its own (`bayan tool.byn --color never`).
        if (args is [var first, ..] && first.EndsWith(".byn", StringComparison.Ordinal) && File.Exists(first)) return (color, ascii);
        var kept = new List<string>(args.Length);
        for (var i = 0; i < args.Length; i++)
        {
            string? value = null;
            if (args[i] == "--")
            {
                kept.AddRange(args[i..]);
                break;
            }
            if (args[i] == "--ascii")
            {
                ascii = true;
                continue;
            }
            if (args[i] == "--color" && i + 1 < args.Length) value = args[++i];
            else if (args[i].StartsWith("--color=", StringComparison.Ordinal)) value = args[i]["--color=".Length..];
            else if (args[i] == "--color") value = "";
            if (value is null)
            {
                kept.Add(args[i]);
                continue;
            }
            if (value is not ("auto" or "always" or "never"))
            {
                stderr.WriteLine(Messages.UnknownColor.Text(value));
                return null;
            }
            color = value;
        }
        args = [.. kept];
        return (color, ascii);
    }

    private static string Paint(Role role, string text, bool bold = false) =>
        s_styleOut?.Colors is { } colors ? (bold ? Palette.Bold : "") + colors.Prefix(role) + text + Palette.Reset : text;

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
    internal static bool RefuseLibrary(Compiled compiled, string command, TextWriter stderr)
    {
        if (compiled.Manifest is not { Kind: PackageKind.Lib } m) return false;
        stderr.WriteLine(Messages.LibraryNoProgram.Text(m.Name, command, "bayan test"));
        return true;
    }

    /// <summary>Builds, or reports why the toolchain could not.</summary>
    internal static string? BuildNative(string source, BuildMode mode, TextWriter stderr, string? output = null, string? target = null, bool json = false)
    {
        using var progress = StartProgress(json, stderr, Messages.BuildProgress.Text());
        var result = Toolchain.Build(source, mode, output, target);
        progress.Done();
        if (result.Succeeded) return result.ExecutablePath;
        stderr.WriteLine(Messages.ToolchainError.Text(result.Errors));
        return null;
    }

    /// <summary>The C translation unit for an error-free compilation.</summary>
    public static string EmitC(Compilation c, BuildMode mode) => CEmitter.Emit(Lowerer.Lower(c), mode == BuildMode.Debug);

    public static string ModeName(BuildMode mode) => mode switch
    {
        BuildMode.Debug => "debug",
        BuildMode.Safe => "safe",
        _ => "fast",
    };

    /// <summary>
    /// A project's <c>out/&lt;mode&gt;/&lt;target&gt;/</c>, with the emitted C written to its
    /// <c>c/</c> for inspection; null outside a project.
    /// </summary>
    private static string? PrepareOut(Compiled compiled, BuildMode mode, string target, string source)
    {
        if (compiled.ProjectRoot is not { } root) return null;
        var dir = Path.Combine(root, "out", ModeName(mode), target);
        var c = Path.Combine(dir, "c");
        Directory.CreateDirectory(c);
        WriteIfChanged(Path.Combine(c, "main.c"), source);
        WriteIfChanged(Path.Combine(c, "bayan-rt.h"), Toolchain.RuntimeHeader);
        WriteIfChanged(Path.Combine(c, "bayan-rt.c"), Toolchain.RuntimeSource);
        return dir;
    }

    private static void WriteIfChanged(string path, string text)
    {
        if (!File.Exists(path) || File.ReadAllText(path) != text) Toolchain.ReplaceFile(path, temp => File.WriteAllText(temp, text));
    }

    internal static string HostTarget => Targets.Host ?? "host";

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
        var source = EmitC(compiled.Compilation, o.Mode);
        if (o.EmitC)
        {
            stdout.Write(source);
            return ExitOk;
        }
        var target = o.Target ?? HostTarget;
        string output;
        if (o.Output is not null) output = o.Output;
        else if (PrepareOut(compiled, o.Mode, target, source) is { } dir) output = Path.Combine(dir, Targets.ExecutableName(compiled.Manifest!.ShortName, target));
        else output = DefaultOutput(o.Path, target);
        return BuildNative(source, o.Mode, stderr, output, o.Target, o.Json) is null ? ExitToolchain : ExitOk;
    }

    private static string DefaultOutput(string path, string target)
    {
        var full = Path.GetFullPath(path);
        var name = Directory.Exists(full) ? Path.GetFileName(full.TrimEnd(Path.DirectorySeparatorChar)) : Path.GetFileNameWithoutExtension(full);
        if (name == "main" && !Directory.Exists(full)) name = Path.GetFileName(Path.GetDirectoryName(full)!);
        return Targets.ExecutableName(name, target);
    }

    private static int RunProgram(string[] args, TextWriter stderr)
    {
        if (ParseBuildOptions(args, stderr, allowOutput: false) is not { } o) return ExitUsage;
        string? scriptKey = null;
        if (IsScript(o.Path))
        {
            scriptKey = Toolchain.ScriptKey(File.ReadAllBytes(o.Path), o.Mode);
            if (Toolchain.LookupScript(scriptKey, Targets.ExecutableName("program", HostTarget)) is { } remembered)
                return Start(remembered, o.ProgramArgs);
        }
        if (Compile(o, stderr) is not { } compiled) return ExitFailure;
        if (RefuseLibrary(compiled, "run", stderr)) return ExitUsage;
        var source = EmitC(compiled.Compilation, o.Mode);
        var output = PrepareOut(compiled, o.Mode, HostTarget, source) is { } dir
            ? Path.Combine(dir, Targets.ExecutableName(compiled.Manifest!.ShortName, HostTarget))
            : null;
        using var progress = StartProgress(o.Json, stderr, Messages.BuildProgress.Text());
        var result = Toolchain.Build(source, o.Mode, output);
        progress.Done();
        if (!result.Succeeded)
        {
            stderr.WriteLine(Messages.ToolchainError.Text(result.Errors));
            return ExitToolchain;
        }
        if (scriptKey is not null && result.Key is { } key) Toolchain.RememberScript(scriptKey, key);
        return Start(result.ExecutablePath!, o.ProgramArgs);
    }

    private static int Start(string executable, string[] programArgs)
    {
        var psi = new System.Diagnostics.ProcessStartInfo(executable) { UseShellExecute = false };
        foreach (var a in programArgs) psi.ArgumentList.Add(a);
        using var process = System.Diagnostics.Process.Start(psi)!;
        process.WaitForExit();
        return process.ExitCode;
    }

    private static int Cache(string[] args, TextWriter stdout, TextWriter stderr)
    {
        if (args is not ["stats"])
        {
            stderr.WriteLine(Messages.CacheUsage.Text("bayan cache stats", "bayan clean --cache"));
            return ExitUsage;
        }
        var s = Toolchain.Stats();
        stdout.WriteLine(Messages.CacheDirectory.Text(s.Directory));
        stdout.WriteLine(Messages.CachePrograms.Text(s.Programs, s.Bytes / 1024.0 / 1024.0));
        stdout.WriteLine(Messages.CacheScripts.Text(s.Scripts));
        return ExitOk;
    }

    /// <summary>A test that runs longer than this (60 s, or <c>$BAYAN_TEST_TIMEOUT_MS</c>) is stopped and reported as timed out.</summary>
    private static TimeSpan TestTimeout =>
        int.TryParse(Environment.GetEnvironmentVariable("BAYAN_TEST_TIMEOUT_MS"), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var ms) && ms > 0
            ? TimeSpan.FromMilliseconds(ms)
            : TimeSpan.FromSeconds(60);

    private static int Test(string[] args, TextWriter stdout, TextWriter stderr)
    {
        if (ParseBuildOptions(args, stderr, allowOutput: false, allowTestJson: true) is not { } o) return ExitUsage;
        if (Compile(o, stderr, includeTests: true) is not { } compiled) return ExitFailure;
        var program = Lowerer.Lower(compiled.Compilation);
        var source = CEmitter.Emit(program, o.Mode == BuildMode.Debug);
        var output = PrepareOut(compiled, o.Mode, HostTarget, source) is { } dir
            ? Path.Combine(dir, Targets.ExecutableName(compiled.Manifest!.ShortName + "-test", HostTarget))
            : null;
        var selected = Enumerable.Range(0, program.Tests.Count).ToList();
        if (o.Changed)
        {
            if (ChangedModules(compiled, o, stderr) is not { } changed) return ExitUsage;
            selected = selected.Where(i => changed.All || changed.Directories.Contains(ModuleOf(compiled, program.Tests[i].Test))).ToList();
        }
        var unchanged = program.Tests.Count - selected.Count;
        if (o.Only is { } only)
        {
            selected = selected.Where(i => only.Any(s => program.Tests[i].Test.Name.Contains(s, StringComparison.Ordinal))).ToList();
            if (selected.Count == 0 && !o.Changed)
            {
                stderr.WriteLine(Messages.TestOnlyNoMatch.Text(string.Join(", ", only.Select(s => "`" + s + "`"))));
                return ExitUsage;
            }
        }
        if (BuildNative(source, o.Mode, stderr, output, json: o.Json || o.TestJson) is not { } executable) return ExitToolchain;
        var count = selected.Count;
        var skipped = program.Tests.Count - count;
        var human = o.TestJson ? TextWriter.Null : stdout;
        var symbols = (s_styleOut ?? Style.Plain).Symbols;
        var modules = selected.Select(i => ModuleOf(compiled, program.Tests[i].Test)).Distinct().Count();
        human.WriteLine(Messages.TestRunning.Text(
            (count == 1 ? Messages.TestCountOne : Messages.TestCountMany).Text(count),
            (modules == 1 ? Messages.TestModuleCountOne : Messages.TestModuleCountMany).Text(modules),
            o.Changed ? Messages.TestSkipped.Text(unchanged) : ""));
        var results = new List<TestResult>();
        var failures = new List<string>();
        var total = System.Diagnostics.Stopwatch.StartNew();
        foreach (var i in selected)
        {
            var test = program.Tests[i].Test;
            var progress = StartProgress(o.Json || o.TestJson, stderr, Messages.TestProgress.Text(test.Name));
            var outcome = RunTest(executable, i);
            progress.Done();
            var status = outcome.TimedOut ? TestStatus.Timeout : outcome.ExitCode switch
            {
                0 => TestStatus.Pass,
                1 => TestStatus.Fail,
                4 => TestStatus.Pending,
                _ => TestStatus.Panic,
            };
            var details = outcome.TimedOut ? Messages.TestTimedOut.Text(TestTimeout.TotalSeconds) : (outcome.Stdout + outcome.Stderr).TrimEnd();
            var (file, line) = test.Location is { } at ? (at.Source.Path, at.Source.GetLinePosition(at.Span.Start).Line) : ("", 0);
            results.Add(new TestResult(test.Name, file, line, status, outcome.Elapsed, status == TestStatus.Pass ? "" : details));
            var module = Path.GetFileName(ModuleOf(compiled, test));
            if (status == TestStatus.Pass)
            {
                if (o.Verbose) human.WriteLine(Messages.TestPassLine.Text(Paint(Role.Success, symbols.Dot), module, test.Name));
                continue;
            }
            if (status == TestStatus.Pending)
            {
                if (o.Verbose) human.WriteLine(Messages.TestPendingLine.Text(Paint(Role.Warning, symbols.Dot), module, test.Name));
                continue;
            }
            failures.Add(test.Name);
            human.WriteLine();
            human.WriteLine(Messages.TestFailureLine.Text(Paint(Role.Error, symbols.Cross, bold: true), module, test.Name));
            foreach (var l in FailureDetail(details, o.Verbose))
                human.WriteLine(Messages.TestDetail.Text(l));
        }
        total.Stop();
        var report = new TestReport(compiled.Manifest?.Name, ModeName(o.Mode), HostTarget, results, total.Elapsed, skipped);
        int passed = report.Count(TestStatus.Pass), pending = report.Count(TestStatus.Pending);
        var failed = count - passed - pending;
        var seconds = Messages.TestSeconds.Text(total.Elapsed.TotalSeconds.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));
        var dot = Paint(Role.Success, symbols.Dot);
        human.WriteLine();
        human.WriteLine(failed > 0 ? Messages.TestSummaryFailed.Text(dot, passed, Paint(Role.Error, symbols.Cross), failed, pending, seconds)
            : pending > 0 ? Messages.TestSummaryPending.Text(dot, passed, pending, seconds)
            : Messages.TestSummaryPassed.Text(dot, passed, seconds));
        if (failures.Count is > 0 and <= 3)
        {
            var command = "bayan test" + string.Concat(failures.Select(n => Messages.TestRerunOnly.Text(n)));
            human.WriteLine((failures.Count == 1 ? Messages.TestRerunOne : Messages.TestRerunMany).Text(command));
        }
        var json = report.ToJson();
        if (o.TestJson) stdout.Write(json);
        if (compiled.ProjectRoot is { } root)
        {
            var testDir = Path.Combine(root, "out", "test");
            Directory.CreateDirectory(testDir);
            Toolchain.ReplaceFile(Path.Combine(testDir, "results.json"), temp => File.WriteAllText(temp, json));
        }
        return failed == 0 ? ExitOk : ExitTestFailure;
    }

    /// <summary>
    /// A failed test's output for display: <c>panic: </c> dropped, <c>assertion failed: e</c> as <c>assert(e)</c>,
    /// value lines kept under it, and <c>at file:line</c> frames as <c>file:line</c>, the top three unless verbose.
    /// </summary>
    private static List<string> FailureDetail(string details, bool verbose)
    {
        var lines = new List<string>();
        var frames = 0;
        foreach (var raw in details.Split('\n'))
        {
            var l = raw.TrimEnd('\r');
            if (l.StartsWith("  at ", StringComparison.Ordinal))
            {
                if (verbose || frames++ < 3) lines.Add(l["  at ".Length..]);
                continue;
            }
            if (l.StartsWith("panic: ", StringComparison.Ordinal)) l = l["panic: ".Length..];
            if (l.StartsWith("assertion failed: ", StringComparison.Ordinal)) l = "assert(" + l["assertion failed: ".Length..] + ")";
            lines.Add(l);
        }
        return lines;
    }

    private sealed record TestOutcome(int ExitCode, bool TimedOut, string Stdout, string Stderr, TimeSpan Elapsed);

    private sealed record ChangedSet(bool All, HashSet<string> Directories);

    /// <summary>
    /// The module directories with files that differ from <c>HEAD</c> (or <c>--base</c>) in the
    /// working tree, untracked files included; a changed <c>package.byn</c> selects everything.
    /// Read-only git. Null after reporting why git couldn't answer.
    /// </summary>
    private static ChangedSet? ChangedModules(Compiled compiled, BuildOptions o, TextWriter stderr)
    {
        var dir = compiled.ProjectRoot ?? (Directory.Exists(o.Path) ? Path.GetFullPath(o.Path) : Path.GetDirectoryName(Path.GetFullPath(o.Path))!);
        if (Toolchain.FindOnPath("git") is not { } git)
        {
            stderr.WriteLine(Messages.ChangedNoGit.Text("--changed"));
            return null;
        }
        if (Scaffold.Git(git, dir, "rev-parse", "--show-toplevel") is not (0, _))
        {
            stderr.WriteLine(Messages.ChangedNoRepository.Text("--changed", o.Path));
            return null;
        }
        // Paths relative to `dir`, not the toplevel, which git reports with symlinks resolved;
        // `-z` keeps non-ASCII names unquoted.
        var baseRef = o.Base ?? "HEAD";
        if (Scaffold.Git(git, dir, "diff", "--name-only", "-z", "--relative", baseRef, "--", ".") is not (0, var diff))
        {
            stderr.WriteLine(Messages.ChangedNoCompare.Text(baseRef));
            return null;
        }
        var untracked = Scaffold.Git(git, dir, "ls-files", "-z", "--others", "--exclude-standard", "--", ".") is (0, var u) ? u : "";
        var set = new ChangedSet(false, new HashSet<string>(StringComparer.Ordinal));
        foreach (var relative in (diff + "\0" + untracked).Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            var full = Path.GetFullPath(Path.Combine(dir, relative));
            if (compiled.ProjectRoot is { } root && full == Path.Combine(root, "package.byn")) return set with { All = true };
            if (full.EndsWith(".byn", StringComparison.Ordinal)) set.Directories.Add(ModuleDirectory(compiled, full));
        }
        return set;
    }

    private static string ModuleOf(Compiled compiled, Semantics.Symbols.TestSymbol test)
    {
        if (test.Location is not { } at) return "";
        var path = at.Source.Path;
        return ModuleDirectory(compiled, Path.IsPathRooted(path) ? path : Path.GetFullPath(Path.Combine(compiled.Compilation.DisplayRoot, path)));
    }

    /// <summary>A file's module directory; a project's <c>tests/</c> files belong to the entry module.</summary>
    private static string ModuleDirectory(Compiled compiled, string file)
    {
        var dir = Path.GetDirectoryName(file)!;
        if (compiled.ProjectRoot is { } root && dir == Path.Combine(root, "tests"))
            return Path.Combine(root, "src", EntryModule(compiled.Manifest!));
        return dir;
    }

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

    private static int Explain(string[] args, TextWriter stdout, TextWriter stderr)
    {
        if (args.Length != 1)
        {
            stderr.WriteLine(Messages.ExplainExpected.Text("bayan explain BYN0003"));
            return ExitUsage;
        }
        var text = ExplainText(args[0]);
        if (text is null)
        {
            stderr.WriteLine(Messages.ExplainUnknown.Text(args[0]));
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
            stderr.WriteLine(Messages.ExpectedOneFile.Text());
            return false;
        }
        if (!File.Exists(args[0]))
        {
            stderr.WriteLine(Messages.FileNotFound.Text(args[0]));
            return false;
        }
        source = SourceText.FromFile(args[0]);
        return true;
    }
}
