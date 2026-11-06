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
                return Doctor.Run(rest, stdout, s_styleOut ?? Style.Plain);
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
            case "format":
                return Format.Run(rest, stdout, stderr);
            case "audit":
                return Audit.Run(rest, stdout, stderr, s_styleOut ?? Style.Plain);
            case "features":
                return Features.Run(rest, stdout, stderr);
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
            case "mutate":
                return MutateBinary(rest, stdout, stderr);
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
                if (args[0] == "fmt") stderr.WriteLine(Messages.FormatterHint.Text());
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
        stdout.Write(json ? TokenDump.ToJson(result) : TokenDump.Dump(result, s_styleOut ?? Style.Plain));
        return result.Diagnostics.Any(d => d.IsError) ? ExitFailure : ExitOk;
    }

    private static int Parse(string[] args, TextWriter stdout, TextWriter stderr)
    {
        var printAst = args.Contains("--ast");
        if (!TryReadSingleFile(args.Where(a => a != "--ast").ToArray(), stderr, out var source)) return ExitUsage;
        var tree = SyntaxTree.Parse(source);
        if (printAst)
        {
            stdout.Write(AstDump.Dump(tree, s_styleOut ?? Style.Plain));
        }
        else if (tree.Diagnostics.Count > 0)
        {
            stderr.Write(DiagnosticRenderer.RenderAll(tree.Diagnostics, includePath: true, s_styleErr ?? Style.Plain));
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

    internal sealed record BuildOptions(string Path, BuildMode Mode, string? Output, bool EmitC, bool Json, string[] ProgramArgs, string? Target = null, bool TestJson = false, bool Changed = false, string? Base = null, bool Verbose = false, bool All = false, string[]? Only = null, bool Mutate = false, bool Coverage = false, string? Run = null);

    internal static BuildOptions? ParseBuildOptions(string[] args, TextWriter stderr, bool allowOutput, bool allowTestJson = false, bool allowVerbose = false)
    {
        string? path = null, output = null, target = null;
        var mode = BuildMode.Safe;
        bool emitC = false, json = false, testJson = false, changed = false, all = false, verbose = false, mutate = false, coverage = false;
        string? baseRef = null, run = null;
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
                case "--all":
                    all = true;
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
                case "--mutate" when allowTestJson:
                    mutate = true;
                    break;
                case "--run" when allowTestJson && i + 1 < args.Length:
                    run = args[++i];
                    break;
                case "--coverage" when allowTestJson:
                    coverage = true;
                    break;
                case "--only" when allowTestJson && i + 1 < args.Length:
                    only.Add(args[++i]);
                    break;
                case "-v" or "--verbose" when allowVerbose:
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
        return new BuildOptions(path, mode, output, emitC, json, programArgs, target, testJson, changed, baseRef, verbose, all, only.Count > 0 ? [.. only] : null, mutate, coverage, run);
    }

    /// <summary>A compiled program, with its manifest and root when it is a project.</summary>
    internal sealed record Compiled(Compilation Compilation, Manifest? Manifest, string? ProjectRoot);

    /// <summary>
    /// Compiles a project directory (one with <c>package.byn</c>), a script (a file starting with
    /// <c>#!</c>, compiled alone) or a file or directory (the whole directory is the entry module).
    /// Prints diagnostics; null on errors.
    /// </summary>
    internal static Compiled? Compile(BuildOptions o, TextWriter stderr, bool includeTests = false, List<Diagnostic>? reported = null)
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
            reported?.AddRange(manifest.Diagnostics);
            if (manifest.HasErrors) return null;
            var entry = EntryModule(manifest);
            if (!Directory.Exists(Path.Combine(root, "src", entry)))
            {
                progress.Done();
                stderr.WriteLine((manifest.Kind == PackageKind.Lib ? Messages.NoEntryLib : Messages.NoEntryApp).Text(manifest.Name, $"src/{entry}/"));
                return null;
            }
            c = Compilation.FromProject(root, entry, new CompilationOptions(includeTests, Library: manifest.Kind == PackageKind.Lib,
                Capabilities: manifest.Capabilities, PackageName: manifest.Name));
        }
        else
        {
            c = Compilation.FromPath(o.Path, new CompilationOptions(includeTests), wholeDirectory: !IsScript(o.Path));
        }
        progress.Done();
        Report(o, c.Diagnostics.Items, stderr);
        reported?.AddRange(c.Diagnostics.Items);
        return c.Diagnostics.HasErrors ? null : new Compiled(c, manifest, root);
    }

    private static Progress StartProgress(bool json, TextWriter stderr, string phase) =>
        Progress.Start(stderr, s_styleErr ?? Style.Plain, s_stderrTty, json, Environment.GetEnvironmentVariable, phase);

    private const int ErrorLimit = 20;

    private static void Report(BuildOptions o, IReadOnlyList<Diagnostic> diagnostics, TextWriter stderr)
    {
        if (diagnostics.Count == 0) return;
        if (o.Json)
        {
            stderr.Write(DiagnosticJson.RenderAll(diagnostics));
            return;
        }
        var style = s_styleErr ?? Style.Plain;
        stderr.Write(DiagnosticRenderer.RenderAll(diagnostics, includePath: true, style, o.All ? int.MaxValue : ErrorLimit, out var omitted));
        if (omitted > 0) stderr.WriteLine(Messages.DiagCapped.Text(style.Symbols.Ellipsis, omitted));
    }

    /// <summary>
    /// Removes <c>--color auto|always|never</c> (or <c>--color=…</c>) and <c>--ascii</c> from before any <c>--</c>,
    /// and before a script run without a command; null after reporting a bad value.
    /// </summary>
    private static (string Color, bool Ascii)? TakeGlobalOptions(ref string[] args, TextWriter stderr)
    {
        var color = "auto";
        var ascii = false;
        var command = false;
        var kept = new List<string>(args.Length);
        for (var i = 0; i < args.Length; i++)
        {
            string? value = null;
            // A script's arguments are its own (`bayan --ascii tool.byn --color never`).
            if (args[i] == "--" || !command && !args[i].StartsWith('-') && args[i].EndsWith(".byn", StringComparison.Ordinal) && File.Exists(args[i]))
            {
                kept.AddRange(args[i..]);
                break;
            }
            if (!args[i].StartsWith('-')) command = true;
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
        ReportToolchain(result, stderr, json);
        return null;
    }

    /// <summary>Why the toolchain could not build; generated C the C compiler rejected is an internal compiler error (BYN4001) with its text attached.</summary>
    internal static void ReportToolchain(NativeBuild result, TextWriter stderr, bool json, Style? style = null)
    {
        if (!result.Rejected)
        {
            stderr.WriteLine(Messages.ToolchainError.Text(result.Errors));
            return;
        }
        var ice = new Diagnostic(Severity.Error, DiagnosticCodes.CCompilerRejected.Code, null, default, DiagnosticCodes.CCompilerRejected.Format(), Help: result.Errors);
        if (json)
        {
            stderr.Write(DiagnosticJson.Render(ice) + "\n");
            return;
        }
        // Without a location the renderer prints the head only; the C text follows verbatim.
        stderr.Write(DiagnosticRenderer.Render(ice, includePath: true, style ?? s_styleErr ?? Style.Plain));
        stderr.Write(result.Errors.EndsWith('\n') ? result.Errors : result.Errors + "\n");
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
        if (!File.Exists(path) || ReadShared(path) != text) Toolchain.ReplaceFile(path, temp => File.WriteAllText(temp, text));
    }

    /// <summary>Reads without blocking a concurrent build's rename over the file, which Windows refuses while it is open without FileShare.Delete.</summary>
    private static string ReadShared(string path)
    {
        using var reader = new StreamReader(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete));
        return reader.ReadToEnd();
    }

    internal static string HostTarget => Targets.Host ?? "host";

    private static int Check(string[] args, TextWriter stdout, TextWriter stderr)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        if (ParseBuildOptions(args, stderr, allowOutput: false) is not { } o) return ExitUsage;
        var reported = new List<Diagnostic>();
        if (Compile(o, stderr, reported: reported) is not { } compiled) return Failed(o, reported, stderr);
        if (!o.Json) stdout.WriteLine(Success(Messages.CheckSummary.Text(Dot, ProgramName(compiled, o.Path), Seconds(clock.Elapsed))));
        return ExitOk;
    }

    private static int Build(string[] args, TextWriter stdout, TextWriter stderr)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        if (ParseBuildOptions(args, stderr, allowOutput: true, allowVerbose: true) is not { } o) return ExitUsage;
        var reported = new List<Diagnostic>();
        if (Compile(o, stderr, reported: reported) is not { } compiled) return Failed(o, reported, stderr);
        if (RefuseLibrary(compiled, "build", stderr)) return ExitUsage;
        var emit = System.Diagnostics.Stopwatch.StartNew();
        var source = EmitC(compiled.Compilation, o.Mode);
        emit.Stop();
        if (o.EmitC)
        {
            stdout.Write(source);
            return ExitOk;
        }
        var target = o.Target ?? HostTarget;
        string output;
        if (o.Output is not null) output = o.Output;
        else if (PrepareOut(compiled, o.Mode, target, source) is { } dir) output = Path.Combine(dir, Targets.ExecutableName(compiled.Manifest!.ShortName, target));
        else output = Targets.ExecutableName(ProgramName(compiled, o.Path), target);
        var native = System.Diagnostics.Stopwatch.StartNew();
        using var progress = StartProgress(o.Json, stderr, Messages.BuildProgress.Text());
        var result = Toolchain.Build(source, o.Mode, output, o.Target);
        progress.Done();
        native.Stop();
        if (!result.Succeeded)
        {
            ReportToolchain(result, stderr, o.Json);
            return ExitToolchain;
        }
        if (o.Json) return ExitOk;
        if (o.Verbose)
        {
            var c = compiled.Compilation;
            stdout.WriteLine(Phase(Messages.PhaseParse, Seconds(c.ParseTime)));
            stdout.WriteLine(Phase(Messages.PhaseCheck, Seconds(c.CheckTime)));
            stdout.WriteLine(Phase(Messages.PhaseEmit, Seconds(emit.Elapsed)));
            stdout.WriteLine(Phase(Messages.PhaseCompile, result.CacheHit ? Messages.PhaseFromCache.Text() : Seconds(native.Elapsed)));
            var shown = compiled.ProjectRoot is { } root && o.Output is null ? Path.GetRelativePath(root, output).Replace('\\', '/') : output;
            stdout.WriteLine(Messages.PhaseOutput.Text(Note(shown)));
        }
        var summary = Messages.BuildSummary.Text(Dot, ProgramName(compiled, o.Path), ModeName(o.Mode), target, Seconds(clock.Elapsed));
        if (result.CacheHit) summary += Messages.BuildFromCache.Text((s_styleOut ?? Style.Plain).Symbols.Separator);
        stdout.WriteLine(Success(summary));
        return ExitOk;
    }

    private static string Dot => (s_styleOut ?? Style.Plain).Symbols.Dot;

    private static string Success(string line) => (s_styleOut ?? Style.Plain).Paint(Role.Success, line);

    private static string Note(string text) => (s_styleOut ?? Style.Plain).Paint(Role.Note, text);

    private static string Phase(Message name, string value) => Messages.PhaseLine.Text(name.Text(), Note(value));

    private static string Seconds(TimeSpan t) =>
        Messages.Seconds.Text(t.TotalSeconds.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));

    /// <summary>
    /// The footer after a failed compile: error and warning counts, and how to explain the first
    /// error. Nothing in JSON mode, or when no diagnostic caused the failure.
    /// </summary>
    private static int Failed(BuildOptions o, List<Diagnostic> reported, TextWriter stderr)
    {
        if (o.Json || DiagnosticRenderer.Sort(reported).FirstOrDefault(d => d.IsError) is not { } first) return ExitFailure;
        var style = s_styleErr ?? Style.Plain;
        int errors = reported.Count(d => d.IsError), warnings = reported.Count(d => d.Severity == Severity.Warning);
        stderr.WriteLine();
        stderr.WriteLine(style.Paint(Role.Error, Messages.BuildFailed.Text(
            style.Symbols.Cross,
            (errors == 1 ? Messages.ErrorsOne : Messages.ErrorsMany).Text(errors),
            (warnings == 1 ? Messages.WarningsOne : Messages.WarningsMany).Text(warnings))));
        stderr.WriteLine(Messages.BuildExplain.Text(first.Code));
        return ExitFailure;
    }

    /// <summary>A project's short name, else the file's (a <c>main.byn</c> is named after its directory) or the directory's.</summary>
    private static string ProgramName(Compiled compiled, string path)
    {
        if (compiled.Manifest is { } m) return m.ShortName;
        var full = Path.GetFullPath(path);
        var name = Directory.Exists(full) ? Path.GetFileName(full.TrimEnd(Path.DirectorySeparatorChar)) : Path.GetFileNameWithoutExtension(full);
        if (name == "main" && !Directory.Exists(full)) name = Path.GetFileName(Path.GetDirectoryName(full)!);
        return name;
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
        var reported = new List<Diagnostic>();
        if (Compile(o, stderr, reported: reported) is not { } compiled) return Failed(o, reported, stderr);
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
            ReportToolchain(result, stderr, o.Json);
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

    /// <summary>Removes a scratch directory; one another process already swept or that cannot be removed is not an error.</summary>
    internal static void DeleteWorkDir(string dir)
    {
        try { Directory.Delete(dir, recursive: true); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>A test that runs longer than this (60 s, or <c>$BAYAN_TEST_TIMEOUT_MS</c>) is stopped and reported as timed out.</summary>
    private static TimeSpan TestTimeout =>
        int.TryParse(Environment.GetEnvironmentVariable("BAYAN_TEST_TIMEOUT_MS"), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var ms) && ms > 0
            ? TimeSpan.FromMilliseconds(ms)
            : TimeSpan.FromSeconds(60);

    private static int Test(string[] args, TextWriter stdout, TextWriter stderr)
    {
        if (ParseBuildOptions(args, stderr, allowOutput: false, allowTestJson: true, allowVerbose: true) is not { } o) return ExitUsage;
        if (Compile(o, stderr, includeTests: true) is not { } compiled) return ExitFailure;
        if (o.Run is not null && !o.Mutate)
        {
            stderr.WriteLine(Messages.UnexpectedArgument.Text("--run"));
            return ExitUsage;
        }
        if (o.Mutate && (o.Coverage || o.TestJson))
        {
            stderr.WriteLine(Messages.UnexpectedArgument.Text(o.Coverage ? "--coverage" : "--json"));
            return ExitUsage;
        }
        if (o.Mutate) return MutateTests(o, compiled, stdout, stderr);
        // Debug runs carry probes for the one-line summary unless `summary: false` (spec 013 R17); a future sanitize run must skip probes unless `--coverage` is given.
        var probes = o.Coverage || o.Mode == BuildMode.Debug && (compiled.Manifest?.Summary ?? true);
        var map = probes ? Testing.CoverageInstrumenter.Instrument(compiled.Compilation) : null;
        var program = Lowerer.Lower(compiled.Compilation, coverage: map?.Probes);
        var source = CEmitter.Emit(program, o.Mode == BuildMode.Debug);
        var output = PrepareOut(compiled, o.Mode, HostTarget, source) is { } dir
            ? Path.Combine(dir, Targets.ExecutableName(compiled.Manifest!.ShortName + "-test", HostTarget))
            : null;
        var selected = Enumerable.Range(0, program.Tests.Count).ToList();
        if (o.Only is { } wanted && !program.Tests.Any(t => wanted.Any(s => t.Test.Name.Contains(s, StringComparison.Ordinal))))
        {
            stderr.WriteLine(Messages.TestOnlyNoMatch.Text(string.Join(", ", wanted.Select(s => "`" + s + "`"))));
            return ExitUsage;
        }
        if (o.Changed)
        {
            if (ChangedModules(compiled, o, stderr) is not { } changed) return ExitUsage;
            selected = selected.Where(i => changed.All || changed.Directories.Contains(ModuleOf(compiled, program.Tests[i].Test))).ToList();
        }
        var unchanged = program.Tests.Count - selected.Count;
        if (o.Only is { } only)
            selected = selected.Where(i => only.Any(s => program.Tests[i].Test.Name.Contains(s, StringComparison.Ordinal))).ToList();
        var built = System.Diagnostics.Stopwatch.StartNew();
        if (BuildNative(source, o.Mode, stderr, output, json: o.Json || o.TestJson) is not { } executable) return ExitToolchain;
        var buildSeconds = built.Elapsed.TotalSeconds;
        string? coverageDir = null, coveragePrefix = null;
        var hits = new Dictionary<int, ulong>();
        // A strict subset (`--only`, `--changed`) leaves counters no selected test could reach: unknown, not missed, and no gate applies.
        var partial = selected.Count < program.Tests.Count;
        var unknown = partial;
        if (map is not null)
        {
            coverageDir = Directory.CreateTempSubdirectory("bayan-coverage-").FullName;
            coveragePrefix = Path.Combine(coverageDir, "BAYAN_COVERAGE");
            Testing.CoverageInstrumenter.DeleteStale(coveragePrefix);
        }
        try
        {
            var count = selected.Count;
            var human = o.TestJson ? TextWriter.Null : stdout;
            var symbols = (s_styleOut ?? Style.Plain).Symbols;
            var modules = selected.Select(i => ModuleOf(compiled, program.Tests[i].Test)).Distinct().Count();
            human.WriteLine(Messages.TestRunning.Text(
                (count == 1 ? Messages.TestCountOne : Messages.TestCountMany).Text(count),
                (modules == 1 ? Messages.TestModuleCountOne : Messages.TestModuleCountMany).Text(modules),
                o.Changed ? Messages.TestSkipped.Text(unchanged) : ""));
            var results = new List<TestResult>();
            var failures = new List<(string Name, bool Selectable)>();
            var total = System.Diagnostics.Stopwatch.StartNew();
            foreach (var i in selected)
            {
                var test = program.Tests[i].Test;
                var progress = StartProgress(o.Json || o.TestJson, stderr, Messages.TestProgress.Text(test.Name));
                var outcome = RunTest(executable, i, coveragePrefix);
                progress.Done();
                if (coveragePrefix is not null)
                {
                    // A killed test's counters are lost, as are those of an unreadable counter file: what no other test hit is unknown, not missed.
                    var counterFile = coveragePrefix + "." + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    if (outcome.TimedOut || !MergeCounters(counterFile, hits)) unknown = true;
                }
                var status = outcome.TimedOut ? TestStatus.Timeout : outcome.ExitCode switch
                {
                    0 => TestStatus.Pass,
                    1 => TestStatus.Fail,
                    4 => TestStatus.Pending,
                    _ => TestStatus.Panic,
                };
                var captured = (outcome.Stdout + outcome.Stderr).TrimEnd();
                var details = outcome.TimedOut ? Messages.TestTimedOut.Text(TestTimeout.TotalSeconds) + (captured.Length > 0 ? "\n" + captured : "") : captured;
                var (file, line) = test.Location is { } at ? (at.Source.Path, at.Source.GetLinePosition(at.Span.Start).Line) : ("", 0);
                results.Add(new TestResult(test.Name, file, line, status, outcome.Elapsed, status == TestStatus.Pass ? "" : details));
                var module = Path.GetFileName(ModuleOf(compiled, test));
                var name = TerminalStyle.Visible(test.Name);
                if (status == TestStatus.Pass)
                {
                    if (!o.Verbose) continue;
                    human.WriteLine(Messages.TestPassLine.Text(Paint(Role.Success, symbols.Dot), module, name));
                    // Each test runs in its own process, so its captured `console` output is its own (J67).
                    if (details.Length > 0)
                        foreach (var l in details.Split('\n'))
                            human.WriteLine(Messages.TestDetail.Text(TerminalStyle.Visible(l.TrimEnd('\r'))));
                    continue;
                }
                if (status == TestStatus.Pending)
                {
                    if (o.Verbose) human.WriteLine(Messages.TestPendingLine.Text(Paint(Role.Warning, symbols.Dot), module, name));
                    continue;
                }
                failures.Add((name, name == test.Name));
                human.WriteLine();
                human.WriteLine(Messages.TestFailureLine.Text(Paint(Role.Error, symbols.Cross, bold: true), module, name));
                foreach (var l in FailureDetail(details, o.Verbose, symbols, (s_styleOut ?? Style.Plain).Width - Messages.TestDetail.Text("").Length))
                    human.WriteLine(Messages.TestDetail.Text(TerminalStyle.Visible(l)));
            }
            total.Stop();
            var report = new TestReport(compiled.Manifest?.Name, ModeName(o.Mode), HostTarget, results, total.Elapsed, unchanged);
            int passed = report.Count(TestStatus.Pass), pending = report.Count(TestStatus.Pending);
            var failed = count - passed - pending;
            var seconds = Messages.TestSeconds.Text(total.Elapsed.TotalSeconds.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));
            var dot = Paint(Role.Success, symbols.Dot);
            human.WriteLine();
            human.WriteLine(failed > 0 ? Messages.TestSummaryFailed.Text(dot, passed, symbols.Separator, Paint(Role.Error, symbols.Cross), failed, pending, seconds)
                : pending > 0 ? Messages.TestSummaryPending.Text(dot, passed, symbols.Separator, pending, seconds)
                : Messages.TestSummaryPassed.Text(dot, passed, symbols.Separator, seconds));
            if (failures.Count is > 0 and <= 3)
            {
                var selectable = failures.Where(f => f.Selectable).ToList();
                if (selectable.Count > 0)
                {
                    var command = "bayan test" + string.Concat(selectable.Select(f => Messages.TestRerunOnly.Text(ShellQuote(f.Name))));
                    human.WriteLine((selectable.Count == 1 ? Messages.TestRerunOne : Messages.TestRerunMany).Text(command));
                }
                if (selectable.Count < failures.Count) human.WriteLine(Messages.TestRerunUnselectable.Text());
            }
            var json = report.ToJson();
            if (o.TestJson) stdout.Write(json);
            if (compiled.ProjectRoot is { } root)
            {
                var testDir = Path.Combine(root, "out", "test");
                Directory.CreateDirectory(testDir);
                Toolchain.ReplaceFile(Path.Combine(testDir, "results.json"), temp => File.WriteAllText(temp, json));
            }
            var gated = map is not null && WriteCoverage(o, compiled, map, hits, unknown, partial, buildSeconds, total.Elapsed.TotalSeconds, human, stderr);
            return failed > 0 || gated ? ExitTestFailure : ExitOk;
        }
        finally
        {
            if (coverageDir is not null) DeleteWorkDir(coverageDir);
        }
    }

    /// <summary>
    /// Adds a test's counter file to <paramref name="hits"/>; false when the file is unreadable (empty or truncated), so its
    /// counters are unknown. A missing file adds nothing.
    /// </summary>
    internal static bool MergeCounters(string counterFile, Dictionary<int, ulong> hits)
    {
        if (!File.Exists(counterFile)) return true;
        Dictionary<int, ulong> counts;
        try
        {
            counts = Testing.CoverageInstrumenter.Read(counterFile);
        }
        catch (Exception e) when (e is InvalidDataException or IndexOutOfRangeException or FormatException or OverflowException)
        {
            return false;
        }
        foreach (var (id, n) in counts)
            hits[id] = hits.GetValueOrDefault(id) + n;
        return true;
    }

    /// <summary>
    /// <c>bayan test --coverage</c> (spec 013 R9, R12): prints the summary and writes <c>out/test/bayan-quality.json</c> and a
    /// <c>&lt;file&gt;.cov.txt</c> per file beside it, under the project root (or the directory of the file given). A
    /// <paramref name="partial"/> run prints no percentages and applies no gate.
    /// </summary>
    private static bool WriteCoverage(BuildOptions o, Compiled compiled, Testing.CoverageMap map, Dictionary<int, ulong> hits, bool unknown,
        bool partial, double buildSeconds, double totalSeconds, TextWriter human, TextWriter stderr)
    {
        var report = Testing.QualityReport.Coverage("bayan test --coverage", map, hits, unknown, buildSeconds, totalSeconds);
        if (!o.Coverage)
        {
            if (partial) return false;
            // The default Debug summary: one line, no files.
            var t = report.CoverageTotals!;
            static string Part(Testing.Tally x) => $"{x.Covered}/{x.Total} ({(x.Total == 0 ? "100" : Testing.Gates.Fmt(100.0 * x.Covered / x.Total))}%)";
            human.WriteLine(Messages.TestCoverageSummary.Text(Part(t.Lines), Part(t.Branches), Part(t.ErrorPaths)));
            return false;
        }
        if (!partial) human.Write(report.CoverageSummary());
        var root = compiled.ProjectRoot ?? Path.GetDirectoryName(Path.GetFullPath(o.Path))!;
        var dir = Path.Combine(root, "out", "test");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "bayan-quality.json"), report.Json());
        foreach (var f in report.Files)
        {
            var path = Path.GetFullPath(f.Path, root);
            if (!File.Exists(path)) continue;
            var listing = Path.Combine(dir, ListingName(Path.GetRelativePath(root, path)) + ".cov.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(listing)!);
            File.WriteAllText(listing, Testing.QualityReport.Listing(f.Coverage!, File.ReadAllLines(path)));
        }
        var m = compiled.Manifest;
        return !partial && m is not null && ReportGates(Testing.Gates.Coverage(report, m.MinLines, m.MinBranches, m.MinErrorPaths), stderr);
    }

    /// <summary>
    /// The path under <c>out/test</c> of a source's listing, from its path relative to the root: each <c>..</c> segment becomes
    /// <c>_</c>, and a rooted path (another drive) keeps only its file name, so no listing lands outside <c>out/test</c>.
    /// </summary>
    internal static string ListingName(string relative) =>
        Path.IsPathRooted(relative)
            ? Path.GetFileName(relative)
            : string.Join(Path.DirectorySeparatorChar, relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Select(s => s == ".." ? "_" : s));

    /// <summary>
    /// <c>bayan test --mutate</c> (spec 013 R1): one schemata build, then every mutant run against the tests that reach it.
    /// Prints a line per mutant and the totals; the site map is written beside the binary.
    /// </summary>
    private static int MutateTests(BuildOptions o, Compiled compiled, TextWriter stdout, TextWriter stderr)
    {
        var schema = Testing.Schemata.Build(compiled.Compilation);
        var program = Lowerer.Lower(compiled.Compilation, schema.Switches);
        var source = CEmitter.Emit(program, o.Mode == BuildMode.Debug);
        var output = PrepareOut(compiled, o.Mode, HostTarget, source) is { } dir
            ? Path.Combine(dir, Targets.ExecutableName(compiled.Manifest!.ShortName + "-mutate", HostTarget))
            : null;
        var build = System.Diagnostics.Stopwatch.StartNew();
        if (BuildNative(source, o.Mode, stderr, output, json: o.Json) is not { } executable) return ExitToolchain;
        File.WriteAllText(Testing.Schemata.SitesPath(executable), schema.SitesJson());
        if (o.Run is not null)
            return RunExternal(Testing.ExternalSite.ReadAll(schema.SitesJson())!, ShellCommand(o.Run), executable, "bayan test --mutate --run",
                Path.Combine(compiled.ProjectRoot ?? (Directory.Exists(o.Path) ? Path.GetFullPath(o.Path) : Path.GetDirectoryName(Path.GetFullPath(o.Path))!), "out", "test"), build.Elapsed.TotalSeconds, stdout, stderr, compiled.Manifest?.MinMutation);
        var work = Directory.CreateTempSubdirectory("bayan-mutate-");
        try
        {
            var result = Testing.MutationRunner.Run(executable, schema, Enumerable.Range(0, program.Tests.Count).ToList(), work.FullName,
                TestTimeout, m => stdout.WriteLine(MutantLine(m)));
            foreach (var t in result.Excluded)
                stderr.WriteLine(Messages.MutateExcluded.Text(program.Tests[t].Test.Name));
            var counts = Enum.GetValues<Testing.MutantStatus>().Select(s => (object)result.Mutants.Count(m => m.Status == s)).ToArray();
            stdout.WriteLine(Messages.MutateTotals.Text(counts));
            if (compiled.Manifest?.MinMutation is not { } min) return ExitOk;
            return ReportGates(Testing.Gates.Mutation(Testing.QualityReport.Mutation("bayan test --mutate", result, schema, 0, [], 0), min), stderr) ? ExitTestFailure : ExitOk;
        }
        finally
        {
            DeleteWorkDir(work.FullName);
        }
    }

    /// <summary><c>bayan mutate --binary &lt;exe&gt; --sites &lt;map.json&gt; -- &lt;cmd&gt;</c> (spec 013 R3): the command runs once per site of a prebuilt schemata binary.</summary>
    private static int MutateBinary(string[] args, TextWriter stdout, TextWriter stderr)
    {
        string? binary = null, sites = null;
        string[] command = [];
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--binary" when i + 1 < args.Length: binary = args[++i]; break;
                case "--sites" when i + 1 < args.Length: sites = args[++i]; break;
                case "--":
                    command = args[(i + 1)..];
                    i = args.Length;
                    break;
                default:
                    stderr.WriteLine(Messages.UnexpectedArgument.Text(args[i]));
                    return ExitUsage;
            }
        }
        if (binary is null || sites is null || command.Length == 0)
        {
            stderr.WriteLine(Messages.MutateBinaryUsage.Text());
            return ExitUsage;
        }
        foreach (var path in new[] { binary, sites })
            if (!File.Exists(path))
            {
                stderr.WriteLine(Messages.NotFound.Text(path));
                return ExitUsage;
            }
        if (Testing.ExternalSite.ReadAll(File.ReadAllText(sites)) is not { } map)
        {
            stderr.WriteLine(Messages.MutateBadSites.Text(sites));
            return ExitUsage;
        }
        return RunExternal(map, command, Path.GetFullPath(binary), "bayan mutate",
            Path.GetDirectoryName(Path.GetFullPath(binary))!, 0, stdout, stderr,
            File.Exists("package.byn") ? Manifest.Read("package.byn").MinMutation : null);
    }

    private static string[] ShellCommand(string command) =>
        OperatingSystem.IsWindows() ? ["cmd", "/c", command] : ["/bin/sh", "-c", command];

    /// <summary>Runs the external command over <paramref name="sites"/>, prints a line per site and the report summary, and writes <c>bayan-quality.json</c> to <paramref name="reportDir"/>.</summary>
    private static int RunExternal(IReadOnlyList<Testing.ExternalSite> sites, IReadOnlyList<string> command, string binary, string tool,
        string reportDir, double buildSeconds, TextWriter stdout, TextWriter stderr, int? minMutation = null)
    {
        var total = System.Diagnostics.Stopwatch.StartNew();
        var environment = new Dictionary<string, string> { ["BAYAN_BINARY"] = binary };
        var results = Testing.MutationRunner.RunExternal(sites, command, environment, TestTimeout, r => stdout.WriteLine(ExternalLine(r)));
        if (results is null)
        {
            stderr.WriteLine(Messages.MutateBaselineFailed.Text());
            return ExitFailure;
        }
        var report = Testing.QualityReport.Mutation(tool, results, buildSeconds, total.Elapsed.TotalSeconds);
        stdout.Write(report.Summary([]));
        Directory.CreateDirectory(reportDir);
        File.WriteAllText(Path.Combine(reportDir, "bayan-quality.json"), report.Json());
        return ReportGates(Testing.Gates.Mutation(report, minMutation), stderr) ? ExitTestFailure : ExitOk;
    }

    /// <summary>Prints a line per failed gate, each followed by what is missing; whether any gate failed.</summary>
    private static bool ReportGates(IReadOnlyList<Testing.GateFailure> failures, TextWriter stderr)
    {
        foreach (var f in failures)
        {
            stderr.WriteLine(Messages.GateBelow.Text(f.Name, Testing.Gates.Fmt(f.Percent), f.Min));
            foreach (var line in f.Missing) stderr.WriteLine(line);
        }
        return failures.Count > 0;
    }

    private static string ExternalLine(Testing.ExternalResult r) =>
        Messages.MutateLine.Text(r.Site.File, r.Site.Line, r.Site.Column, r.Site.Operator, r.Site.Original, r.Site.Mutated, StatusName(r.Status))
            + (r.Status == Testing.MutantStatus.Timeout ? Messages.MutateTimeoutNote.Text() : "");

    private static string StatusName(Testing.MutantStatus s) => s switch
    {
        Testing.MutantStatus.Killed => Messages.MutateKilled.Text(),
        Testing.MutantStatus.Survived => Messages.MutateSurvived.Text(),
        Testing.MutantStatus.Timeout => Messages.MutateTimeout.Text(),
        Testing.MutantStatus.NotReached => Messages.MutateNotReached.Text(),
        Testing.MutantStatus.Marked => Messages.MutateMarked.Text(),
        _ => Messages.MutateNotEmitted.Text(),
    };

    private static string MutantLine(Testing.MutantResult m)
    {
        var s = m.Site.Site;
        return Messages.MutateLine.Text(s.File, s.Line, s.Column, s.Operator, s.Original, s.Mutated, StatusName(m.Status))
            + (m.Status == Testing.MutantStatus.Timeout ? Messages.MutateTimeoutNote.Text() : "");
    }

    /// <summary>
    /// <paramref name="text"/> as one literal word for a POSIX shell: in double quotes when nothing in it
    /// expands there, else in single quotes with each <c>'</c> written <c>'\''</c>.
    /// </summary>
    private static string ShellQuote(string text) =>
        text.AsSpan().IndexOfAny("$`\\\"!") < 0 ? "\"" + text + "\"" : "'" + text.Replace("'", "'\\''") + "'";

    /// <summary>
    /// A failed test's output for display: <c>panic: </c> dropped, the diagram under a failed <c>assert</c> moved
    /// left with it, drawn with <paramref name="symbols"/> and cut to <paramref name="width"/> columns, and
    /// <c>at file:line</c> frames as <c>file:line</c>, the top three unless verbose.
    /// </summary>
    internal static List<string> FailureDetail(string details, bool verbose, Symbols symbols, int width)
    {
        var lines = new List<string>();
        var frames = 0;
        var diagram = false;
        foreach (var raw in details.Split('\n'))
        {
            var l = raw.TrimEnd('\r');
            if (l.StartsWith("  at ", StringComparison.Ordinal))
            {
                if (verbose || frames++ < 3) lines.Add(l["  at ".Length..]);
                diagram = false;
                continue;
            }
            if (l.StartsWith("panic: ", StringComparison.Ordinal))
            {
                l = l["panic: ".Length..];
                diagram = l.StartsWith("assert ", StringComparison.Ordinal) || l.StartsWith("assert(", StringComparison.Ordinal);
            }
            else if (diagram && l.StartsWith(PowerAssert.Indent, StringComparison.Ordinal))
                l = Progress.Fit(l[PowerAssert.Indent.Length..].Replace(PowerAssert.Bar.ToString(), symbols.Bar), width, symbols.Ellipsis);
            lines.Add(l);
        }
        return lines;
    }

    internal sealed record TestOutcome(int ExitCode, bool TimedOut, string Stdout, string Stderr, TimeSpan Elapsed);

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

    internal static TestOutcome RunTest(string executable, int index, string? coveragePrefix = null)
    {
        var psi = new System.Diagnostics.ProcessStartInfo(executable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add("--test");
        psi.ArgumentList.Add(index.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (coveragePrefix is not null) psi.Environment["BAYAN_COVERAGE"] = coveragePrefix;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        using var p = System.Diagnostics.Process.Start(psi)!;
        var stdout = BoundedCapture.Start(p.StandardOutput.BaseStream);
        var stderr = BoundedCapture.Start(p.StandardError.BaseStream);
        if (!p.WaitForExit(TestTimeout))
        {
            p.Kill(entireProcessTree: true);
            p.WaitForExit();
            // A grandchild may still hold the pipes open; do not wait for them forever.
            // What was read so far is kept either way.
            try { Task.WaitAll([stdout.Completion, stderr.Completion], TimeSpan.FromSeconds(5)); }
            catch (AggregateException) { }
            // Closing our ends ends the readers still blocked on a pipe a grandchild holds open.
            try { p.StandardOutput.Dispose(); p.StandardError.Dispose(); }
            catch (Exception e) when (e is IOException or ObjectDisposedException) { }
            return new TestOutcome(-1, true, stdout.Snapshot(), stderr.Snapshot(), clock.Elapsed);
        }
        return new TestOutcome(p.ExitCode, false, stdout.Completion.Result, stderr.Completion.Result, clock.Elapsed);
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
