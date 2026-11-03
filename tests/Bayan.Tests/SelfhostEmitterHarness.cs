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

    /// <summary>Fails with the first line where <paramref name="actual"/> differs from <paramref name="expected"/>.</summary>
    public static void AssertSame(string name, string expected, string actual)
    {
        if (expected == actual) return;
        var e = expected.Split('\n');
        var a = actual.Split('\n');
        for (var i = 0; i < Math.Max(e.Length, a.Length); i++)
        {
            var el = i < e.Length ? e[i] : "<end>";
            var al = i < a.Length ? a[i] : "<end>";
            if (el != al) Assert.Fail($"{name}: line {i + 1} differs\nexpected: {el}\nactual:   {al}");
        }
    }
}
