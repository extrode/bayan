using System.Diagnostics;
using Bayan.Semantics;
using Bayan.Syntax.Diagnostics;
using Bayan.Syntax.Text;

namespace Bayan.Tests;

/// <summary>
/// The shared harness for the 012 driver tests: <c>selfhost/driver/*.byn</c> built once per run as stage 1 with
/// <c>packageRoot: selfhost/</c> (the shape of <see cref="SelfhostEmitterHarness"/>), in <c>fast</c> mode, then run as a process.
/// </summary>
public static class SelfhostDriverHarness
{
    public static string DriverDir => Path.Combine(SelfhostSemanticsHarness.SelfhostDir, "driver");

    private static readonly Lazy<(string? Path, string Error)> Built = new(BuildNow);

    /// <summary>The built driver; fails the test with the compiler's complaint.</summary>
    public static string Build()
    {
        var (path, error) = Built.Value;
        if (path is null) Assert.Fail($"selfhost/driver did not build:\n{error}");
        return path!;
    }

    private static (string? Path, string Error) BuildNow()
    {
        var sources = Directory.GetFiles(DriverDir, "*.byn").Order(StringComparer.Ordinal).Select(SourceText.FromFile).ToList();
        Environment.SetEnvironmentVariable("BAYAN_CACHE", Path.Combine(Golden.RepoRoot, "tmp", "cache"));
        var c = Compilation.Create(sources, packageRoot: SelfhostSemanticsHarness.SelfhostDir);
        if (c.Diagnostics.HasErrors) return (null, DiagnosticRenderer.RenderAll(c.Diagnostics.Items, includePath: true));
        var build = SelfhostSemanticsHarness.BuildFast(c, new HashSet<string>(StringComparer.Ordinal));
        return build.Succeeded ? (build.ExecutablePath, "") : (null, build.Errors);
    }

    /// <summary>Runs the built driver with <paramref name="args"/>.</summary>
    public static (int Exit, string Out, string Err) Run(params string[] args) => SelfhostParserTests.Run(Build(), args);

    private static readonly Lazy<(string? Path, string Error)> ProbeBuilt = new(BuildProbeNow);

    /// <summary>
    /// The driver with its own <c>main</c> renamed <c>driverMain</c> and a probe <c>main</c> appended to <c>main.byn</c>
    /// (the real lines keep their numbers): <c>style &lt;args&gt;</c> runs <c>run</c> and prints the decided style,
    /// <c>library &lt;path&gt; &lt;command&gt;</c> prints <c>refuseLibrary</c>'s result, <c>isscript &lt;path&gt;</c> <c>t1IsScript</c>'s.
    /// </summary>
    private const string ProbeMain = """

        type T26Options = semantics.T26Options

        i32 main(ReadOnlySpan<string> args) {
            if args[0] == "style" {
                var d = newDCtx()
                let code = run(args[1..args.length], through: ref d)
                Io.stdout.write($"\nout {d.styleOut.color} {d.styleOut.ascii} {d.styleOut.width} err {d.styleErr.color} {d.styleErr.ascii} {d.styleErr.width} tty {d.stderrTty}\n")
                return code
            }
            if args[0] == "isscript" {
                Io.stdout.write($"{t1IsScript(args[1])}\n")
                return 0
            }
            if args[0] == "decide" {
                let ascii = args[2] == "true"
                let tty = args[3] == "true"
                let width: i64 = args[4] == "80" ? 80 : 0
                let s = decideStyle(args[1], by: ascii, through: tty, for: width)
                Io.stdout.write($"{s.color} {s.ascii} {s.width}\n")
                return 0
            }
            let o = T26Options { path: args[1] }
            let ascii = false
            let symbols = semantics.t26Symbols(ascii)
            var reported = List<semantics.T26Reported>()
            var ok = false
            let c = semantics.t26Compile(o, by: symbols, to: ref reported, through: ref ok)
            Io.stdout.write($"{refuseLibrary(c, by: args[2])}\n")
            return 0
        }
        """;

    private static (string? Path, string Error) BuildProbeNow()
    {
        var sources = Directory.GetFiles(DriverDir, "*.byn").Order(StringComparer.Ordinal)
            .Select(p => Path.GetFileName(p) == "main.byn"
                ? SourceText.FromString(p, File.ReadAllText(p).Replace("i32 main(ReadOnlySpan<string> args) {", "i32 driverMain(ReadOnlySpan<string> args) {") + ProbeMain + "\n")
                : SourceText.FromFile(p)).ToList();
        Environment.SetEnvironmentVariable("BAYAN_CACHE", Path.Combine(Golden.RepoRoot, "tmp", "cache"));
        var c = Compilation.Create(sources, packageRoot: SelfhostSemanticsHarness.SelfhostDir);
        if (c.Diagnostics.HasErrors) return (null, DiagnosticRenderer.RenderAll(c.Diagnostics.Items, includePath: true));
        var build = SelfhostSemanticsHarness.BuildFast(c, new HashSet<string>(StringComparer.Ordinal));
        return build.Succeeded ? (build.ExecutablePath, "") : (null, build.Errors);
    }

    /// <summary>Runs the probe build with <paramref name="env"/> added to the environment.</summary>
    public static (int Exit, string Out, string Err) RunProbe(IReadOnlyDictionary<string, string> env, params string[] args)
    {
        var (path, error) = ProbeBuilt.Value;
        if (path is null) Assert.Fail($"selfhost/driver with the probe main did not build:\n{error}");
        var psi = new ProcessStartInfo(path!) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var (k, v) in env) psi.Environment[k] = v;
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        p.WaitForExit();
        return (p.ExitCode, stdout.Result.ReplaceLineEndings("\n"), stderr.Result.ReplaceLineEndings("\n"));
    }
}
