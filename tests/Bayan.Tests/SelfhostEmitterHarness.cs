using Bayan.Backend.C;
using Bayan.Cli;
using Bayan.Semantics;
using Bayan.Syntax.Diagnostics;
using Bayan.Syntax.Text;

namespace Bayan.Tests;

/// <summary>
/// The shared build-and-run harness for the 011 emitter tests: the top-level <c>selfhost/emitter/*.byn</c> built
/// once per run with <c>packageRoot: selfhost/</c> (the nested <c>emitter.cprint</c> import resolves through
/// <c>Compilation.cs:187</c>), in <c>fast</c> mode, then run as a process.
/// </summary>
public static class SelfhostEmitterHarness
{
    public static string EmitterDir => Path.Combine(SelfhostSemanticsHarness.SelfhostDir, "emitter");

    private static readonly Lazy<(string? Path, string Error)> Built = new(BuildNow);

    /// <summary>The built <c>bayan-emit</c>; fails the test with the compiler's complaint.</summary>
    public static string Build()
    {
        var (path, error) = Built.Value;
        if (path is null) Assert.Fail($"selfhost/emitter did not build:\n{error}");
        return path!;
    }

    private static (string? Path, string Error) BuildNow()
    {
        var sources = Directory.GetFiles(EmitterDir, "*.byn").Order(StringComparer.Ordinal).Select(SourceText.FromFile).ToList();
        Environment.SetEnvironmentVariable("BAYAN_CACHE", Path.Combine(Golden.RepoRoot, "tmp", "cache"));
        var c = Compilation.Create(sources, packageRoot: SemanticsDir);
        if (c.Diagnostics.HasErrors) return (null, DiagnosticRenderer.RenderAll(c.Diagnostics.Items, includePath: true));
        var build = SelfhostSemanticsHarness.BuildFast(c, new HashSet<string>(StringComparer.Ordinal));
        return build.Succeeded ? (build.ExecutablePath, "") : (null, build.Errors);
    }

    private static string SemanticsDir => SelfhostSemanticsHarness.SelfhostDir;

    /// <summary>Runs the built <c>bayan-emit</c> with <paramref name="args"/>.</summary>
    public static (int Exit, string Out, string Err) Run(params string[] args) => SelfhostParserTests.Run(Build(), args);

    /// <summary>Fails with the first line where <paramref name="actual"/> differs from <paramref name="expected"/>, CRLF read as LF.</summary>
    public static void AssertSame(string name, string expected, string actual)
    {
        // The C# CLI on Windows ends some lines with Environment.NewLine; the self-hosted runtime writes LF (Q816).
        var e = expected.Replace("\r\n", "\n").Split('\n');
        var a = actual.Replace("\r\n", "\n").Split('\n');
        for (var i = 0; i < Math.Max(e.Length, a.Length); i++)
        {
            var el = i < e.Length ? e[i] : "<end>";
            var al = i < a.Length ? a[i] : "<end>";
            if (el != al) Assert.Fail($"{name}: line {i + 1} differs\nexpected: {el}\nactual:   {al}");
        }
    }

    /// <summary>A golden case copied to <c>tmp/t12-tests/&lt;slug&gt;/</c> (a directory case whole), so the CLIs see it alone; returns its entry file.</summary>
    public static string Isolate(string name)
    {
        var source = Path.Combine(Golden.CasesRoot, name);
        var caseDir = Path.GetDirectoryName(source)!;
        var dir = Path.Combine(Golden.RepoRoot, "tmp", "t12-tests", name.Replace('/', '_'));
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        Directory.CreateDirectory(dir);
        if (Path.GetFileName(source) != "main.byn") File.Copy(source, Path.Combine(dir, Path.GetFileName(source)));
        else
        {
            foreach (var file in Directory.EnumerateFiles(caseDir, "*", SearchOption.AllDirectories))
            {
                var target = Path.Combine(dir, Path.GetRelativePath(caseDir, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target);
            }
        }
        return Path.Combine(dir, Path.GetFileName(source));
    }

    /// <summary>The mode the C# run harness builds <paramref name="path"/> in: its <c>// mode:</c> first line, else <c>safe</c> (RunTests.cs:75-78).</summary>
    public static string ModeOf(string path)
    {
        var first = File.ReadLines(path).FirstOrDefault() ?? "";
        if (first.StartsWith("// mode: ", StringComparison.Ordinal) && Toolchain.TryParseMode(first["// mode: ".Length..].Trim(), out var mode))
            return CliApp.ModeName(mode);
        return "safe";
    }

    /// <summary>
    /// The app of <c>CacheTests.ACallerIsRecheckedWhenACalleeStartsOrStopsStoringAnArgument</c> (CacheTests.cs:135-153): <c>bayan new</c>, then its
    /// <c>main.byn</c> and <c>keep.byn</c> with the callee that <c>copies</c> its argument or <c>stores</c> it (the latter is rejected, BYN3002).
    /// </summary>
    public static string CacheApp(string name, bool stores)
    {
        var root = Path.Combine(Golden.RepoRoot, "tmp", "t12-tests", name);
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        Directory.CreateDirectory(root);
        var app = Path.Combine(root, "tally");
        Assert.Equal(CliApp.ExitOk, CliApp.Run(["new", app, "--vcs", "none"], new StringWriter(), new StringWriter()));
        var callee = Path.Combine(app, "src", "keep", "keep.byn");
        Directory.CreateDirectory(Path.GetDirectoryName(callee)!);
        File.WriteAllText(callee, stores
            ? "public void pass(string s, to ref string g) {\n    g = s\n}\n"
            : "public void pass(string s, to ref string g) {\n    g = \"\"\n}\n");
        File.WriteAllText(Path.Combine(app, "src", "app", "main.byn"),
            "import keep\n\nvar kept = \"\"\n{\n    using let arena = Arena()\n    keep.pass(\"ann\".copy(to: arena), to: ref kept)\n}\nprint(kept)\n");
        return app;
    }

    /// <summary>
    /// The C that <c>bayan test -v &lt;dir&gt;</c> builds for the test file <paramref name="input"/> copied alone into <paramref name="work"/>
    /// (RunTests.cs:122-131): <c>CliApp.Compile</c> of the whole directory with tests, in the mode <c>ParseBuildOptions</c> gives without
    /// <c>--mode</c> (<c>Safe</c>, CliApp.cs:154), then <c>CliApp.Test</c>'s <c>Lowerer.Lower</c> and <c>CEmitter.Emit</c> (CliApp.cs:620-621,
    /// no probes outside <c>Debug</c>), which is <c>EmitC</c>. Null when the C# rejects the input.
    /// </summary>
    public static string? TestRunC(string input, string work)
    {
        if (Directory.Exists(work)) Directory.Delete(work, recursive: true);
        Directory.CreateDirectory(work);
        File.Copy(input, Path.Combine(work, Path.GetFileName(input)));
        if (CliApp.ParseBuildOptions(["-v", work], new StringWriter(), allowOutput: false, allowTestJson: true, allowVerbose: true) is not { } o) return null;
        if (CliApp.Compile(o, new StringWriter(), includeTests: true) is not { } compiled) return null;
        return CliApp.EmitC(compiled.Compilation, o.Mode);
    }

    /// <summary><c>CliApp.Run(cli)</c> against <c>bayan-emit self</c> (with <paramref name="namedBy"/> stripped from the C# text): stdout and stderr by <see cref="AssertSame"/>, then the exit code.</summary>
    public static void SameAsCli(string name, string[] cli, string[] self, string? namedBy = null)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var exit = CliApp.Run(cli, stdout, stderr);
        var (actualExit, actualOut, actualErr) = Run(self);
        // `namedBy`: a directory whose prefix the C# prints on a source's name, which `--package` gives as the file name alone (RunTests.cs:80).
        string Named(string text) => namedBy is null ? text : text.Replace(namedBy.TrimEnd('/', '\\') + Path.DirectorySeparatorChar, "");
        AssertSame($"{name} stderr", Named(stderr.ToString()), actualErr);
        AssertSame($"{name} stdout", Named(stdout.ToString()), actualOut);
        Assert.True(exit == actualExit, $"{name}: exit {actualExit}, expected {exit}");
    }
}
