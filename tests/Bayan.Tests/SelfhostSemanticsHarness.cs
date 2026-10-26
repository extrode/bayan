using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Bayan.Backend.C;
using Bayan.Cli;
using Bayan.Semantics;
using Bayan.Syntax.Diagnostics;
using Bayan.Syntax.Text;

namespace Bayan.Tests;

/// <summary>
/// The shared build-and-compare harness for the 010 checker tests: <c>selfhost/semantics</c> with a
/// generated <c>main.byn</c>, built once per distinct source, its stdout split into <c>== name</c>
/// sections, and each section compared line by line with a C# oracle's lines.
/// </summary>
public static class SelfhostSemanticsHarness
{
    public static string SelfhostDir => Path.Combine(Golden.RepoRoot, "selfhost");

    public static string SemanticsDir => Path.Combine(SelfhostDir, "semantics");

    private static readonly ConcurrentDictionary<string, Lazy<(string? Path, string Error)>> Builds = new();

    /// <summary>
    /// Every <c>selfhost/semantics/*.byn</c> but <c>main.byn</c>, plus <paramref name="mainSource"/> as
    /// <c>main.byn</c>, built once per distinct source; fails the test with the compiler's complaint.
    /// </summary>
    public static string Build(string mainSource)
    {
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(mainSource)));
        var (path, error) = Builds.GetOrAdd(key, _ => new(() => BuildNow(mainSource))).Value;
        if (path is null) Assert.Fail($"selfhost/semantics with the generated main did not build:\n{error}");
        return path!;
    }

    private static (string? Path, string Error) BuildNow(string mainSource)
    {
        var sources = Directory.GetFiles(SemanticsDir, "*.byn")
            .Where(p => Path.GetFileName(p) != "main.byn")
            .Order(StringComparer.Ordinal)
            .Select(SourceText.FromFile)
            .ToList();
        sources.Add(SourceText.FromString(Path.Combine(SemanticsDir, "main.byn"), mainSource));
        Environment.SetEnvironmentVariable("BAYAN_CACHE", Path.Combine(Golden.RepoRoot, "tmp", "cache"));
        var c = Compilation.Create(sources, packageRoot: SelfhostDir);
        if (c.Diagnostics.HasErrors) return (null, DiagnosticRenderer.RenderAll(c.Diagnostics.Items, includePath: true));
        var build = Toolchain.Build(CliApp.EmitC(c, BuildMode.Fast), BuildMode.Fast);
        return build.Succeeded ? (build.ExecutablePath, "") : (null, build.Errors);
    }

    /// <summary>Builds and runs <paramref name="mainSource"/>; it must exit 0 with empty stderr.</summary>
    public static Dictionary<string, List<string>> RunSections(string mainSource, params string[] args)
    {
        var (exit, stdout, stderr) = SelfhostParserTests.Run(Build(mainSource), args);
        Assert.True(exit == 0 && stderr.Length == 0, $"generated main exited {exit}:\n{stderr}");
        return Sections(stdout);
    }

    /// <summary>
    /// Splits output at lines <c>== name</c>; lines before the first header go to section <c>""</c>.
    /// A repeated header is an error, so two tests cannot silently share one section.
    /// </summary>
    public static Dictionary<string, List<string>> Sections(string stdout)
    {
        var sections = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var current = new List<string>();
        sections[""] = current;
        foreach (var line in stdout.Split('\n'))
        {
            if (line.StartsWith("== ", StringComparison.Ordinal))
            {
                var name = line[3..];
                Assert.False(sections.ContainsKey(name), $"section '{name}' printed twice");
                sections[name] = current = [];
            }
            else if (line.Length > 0) current.Add(line);
        }
        return sections;
    }

    /// <summary>
    /// <paramref name="actual"/>'s section <paramref name="section"/> against <paramref name="expected"/>,
    /// naming the first differing line; a missing section or a count mismatch fails too.
    /// </summary>
    public static void AssertSection(Dictionary<string, List<string>> actual, string section, IReadOnlyList<string> expected)
    {
        Assert.True(actual.TryGetValue(section, out var lines), $"section '{section}' was not printed");
        var n = Math.Min(lines!.Count, expected.Count);
        for (var i = 0; i < n; i++)
            if (lines[i] != expected[i])
                Assert.Fail($"{section}: line {i + 1}\n expected: {expected[i]}\n   actual: {lines[i]}");
        Assert.True(lines.Count == expected.Count, $"{section}: printed {lines.Count} lines, expected {expected.Count}");
        Assert.True(expected.Count > 0, $"{section}: the oracle produced no lines, so nothing was compared");
    }
}
