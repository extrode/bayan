using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Bayan.Backend.C;
using Bayan.Cli;
using Bayan.Lowering;
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
        var build = BuildFast(c, new HashSet<string>(StringComparer.Ordinal) { sources[^1].Path });
        return build.Succeeded ? (build.ExecutablePath, "") : (null, build.Errors);
    }

    /// <summary>The snapshot directory (spec 013 R14) when <c>BAYAN_SNAPSHOT</c> is set, else null; a relative path is
    /// taken from the repo root, since the test host runs in its output directory.</summary>
    public static string? SnapshotDir =>
        Environment.GetEnvironmentVariable("BAYAN_SNAPSHOT") is { Length: > 0 } dir ? Path.GetFullPath(dir, Golden.RepoRoot) : null;

    /// <summary>
    /// <paramref name="c"/> built in fast mode. Under <c>BAYAN_SNAPSHOT</c> it is a schemata build without sites in
    /// <paramref name="generated"/> (harness-generated files), and with <c>BAYAN_SNAPSHOT_FILES</c> (comma-separated,
    /// relative to the repo root) only with sites in those files; its site map is written beside the binary and both are
    /// listed in <c>manifest.json</c>.
    /// </summary>
    internal static NativeBuild BuildFast(Compilation c, IReadOnlySet<string> generated)
    {
        if (SnapshotDir is not { } dir) return Toolchain.Build(CliApp.EmitC(c, BuildMode.Fast), BuildMode.Fast);
        var skip = new HashSet<string>(generated, StringComparer.Ordinal);
        if (Environment.GetEnvironmentVariable("BAYAN_SNAPSHOT_FILES") is { Length: > 0 } files)
        {
            var keep = files.Split(',').Select(f => Path.GetFullPath(f, Golden.RepoRoot)).ToHashSet(StringComparer.Ordinal);
            skip.UnionWith(c.Modules.SelectMany(m => m.AllFunctions).Select(f => f.File?.Source.Path).OfType<string>()
                .Where(p => !keep.Contains(Path.GetFullPath(p))));
        }
        var schema = Bayan.Testing.Schemata.Build(c, skip);
        var build = Toolchain.Build(CEmitter.Emit(Lowerer.Lower(c, schema.Switches), false), BuildMode.Fast);
        if (build.ExecutablePath is { } binary)
        {
            var sites = Bayan.Testing.Schemata.SitesPath(binary);
            File.WriteAllText(sites, schema.SitesJson());
            lock (SnapshotLock)
            {
                Directory.CreateDirectory(dir);
                var path = Path.Combine(dir, "manifest.json");
                var manifest = File.Exists(path) ? JsonNode.Parse(File.ReadAllText(path))!.AsObject() : new JsonObject { ["version"] = 1, ["builds"] = new JsonArray() };
                var builds = manifest["builds"]!.AsArray();
                if (!builds.Any(b => (string?)b!["binary"] == binary))
                    builds.Add(new JsonObject { ["binary"] = binary, ["sites"] = sites });
                File.WriteAllText(path, manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
            }
        }
        return build;
    }

    private static readonly Lock SnapshotLock = new();

    /// <summary>
    /// Under <c>BAYAN_SNAPSHOT</c>: runs <paramref name="program"/> twice and appends the run to <c>runs.jsonl</c>; the
    /// replay compares its exit code, stdout and stderr. The C# oracle checks run once, here at record time, and a
    /// recording only completes if every recorded test passed them (013-plan.md:21, "compute the C# oracle once").
    /// Two runs that differ in any of the three throw: such a test is tagged <c>[Trait("Snapshot", "skip")]</c>.
    /// </summary>
    internal static (int Exit, byte[] Out, byte[] Err) RunRecorded(string dir, string program, byte[]? input, string[] args)
    {
        var first = Exec(program, input, args);
        var second = Exec(program, input, args);
        var differ = new List<string>();
        if (first.Exit != second.Exit) differ.Add("exit code");
        if (!first.Out.AsSpan().SequenceEqual(second.Out)) differ.Add("stdout");
        if (!first.Err.AsSpan().SequenceEqual(second.Err)) differ.Add("stderr");
        if (differ.Count > 0)
            throw new InvalidOperationException($"BAYAN_SNAPSHOT: two runs of `{string.Join(' ', args.Prepend(program))}` differ in "
                + $"{string.Join(", ", differ)}; tag the test [Trait(\"Snapshot\", \"skip\")] if its output is not deterministic");
        var record = new JsonObject
        {
            ["program"] = program,
            ["args"] = new JsonArray(args.Select(a => (JsonNode?)a).ToArray()),
            ["cwd"] = Environment.CurrentDirectory,
            ["stdin"] = input is null ? null : Convert.ToBase64String(input),
            ["stdout"] = Convert.ToBase64String(first.Out),
            ["stderr"] = Convert.ToBase64String(first.Err),
            ["exit"] = first.Exit,
        };
        lock (SnapshotLock)
        {
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "runs.jsonl"), record.ToJsonString() + "\n");
        }
        return first;
    }

    private static (int Exit, byte[] Out, byte[] Err) Exec(string program, byte[]? input, string[] args)
    {
        var psi = new ProcessStartInfo(program) { RedirectStandardInput = input is not null, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var stdout = new MemoryStream();
        var stderr = new MemoryStream();
        var copyOut = p.StandardOutput.BaseStream.CopyToAsync(stdout);
        var copyErr = p.StandardError.BaseStream.CopyToAsync(stderr);
        if (input is not null)
        {
            p.StandardInput.BaseStream.Write(input);
            p.StandardInput.Close();
        }
        p.WaitForExit();
        copyOut.Wait();
        copyErr.Wait();
        return (p.ExitCode, stdout.ToArray(), stderr.ToArray());
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
