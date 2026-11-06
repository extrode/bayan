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

    /// <summary>The coverage directory (spec 013 T26) when <c>BAYAN_COVERAGE</c> is set, else null; a relative path is
    /// taken from the repo root. The test host's own <c>BAYAN_COVERAGE</c> is never a counter prefix: each covered build
    /// runs through a wrapper that sets its own.</summary>
    public static string? CoverageDir =>
        Environment.GetEnvironmentVariable("BAYAN_COVERAGE") is { Length: > 0 } dir ? Path.GetFullPath(dir, Golden.RepoRoot) : null;

    /// <summary>
    /// <paramref name="c"/> built in fast mode. Under <c>BAYAN_SNAPSHOT</c> it is a schemata build without sites in
    /// <paramref name="generated"/> (harness-generated files), and with <c>BAYAN_SNAPSHOT_FILES</c> (comma-separated,
    /// relative to the repo root) only with sites in those files; its site map is written beside the binary and both are
    /// listed in <c>manifest.json</c>. Under <c>BAYAN_COVERAGE</c> it is a coverage build (see <see cref="BuildCovered"/>).
    /// </summary>
    internal static NativeBuild BuildFast(Compilation c, IReadOnlySet<string> generated) =>
        BuildFast(c, generated, SnapshotDir, CoverageDir, Environment.GetEnvironmentVariable("BAYAN_COVERAGE_FILES"));

    /// <summary><see cref="BuildFast(Compilation, IReadOnlySet{string})"/> with the snapshot and coverage directories and
    /// <c>BAYAN_COVERAGE_FILES</c> given; both directories at once throw (one mode per run).</summary>
    internal static NativeBuild BuildFast(Compilation c, IReadOnlySet<string> generated, string? snapshot, string? coverage, string? coverageFiles)
    {
        if (snapshot is not null && coverage is not null)
            throw new InvalidOperationException("BAYAN_SNAPSHOT and BAYAN_COVERAGE are both set: one mode per run; unset one");
        if (coverage is not null) return BuildCovered(c, generated, coverage, coverageFiles);
        if (snapshot is not { } dir) return Toolchain.Build(CliApp.EmitC(c, BuildMode.Fast), BuildMode.Fast);
        var compiled = c.Modules.SelectMany(m => m.AllFunctions).Select(f => f.File?.Source.Path).OfType<string>();
        var skip = SnapshotSkip(compiled, generated, Environment.GetEnvironmentVariable("BAYAN_SNAPSHOT_FILES"), root: c.DisplayRoot);
        var schema = Bayan.Testing.Schemata.Build(c, skip);
        var build = Toolchain.Build(CEmitter.Emit(Lowerer.Lower(c, schema.Switches), false), BuildMode.Fast);
        if (build.ExecutablePath is { } binary)
        {
            var sites = Bayan.Testing.Schemata.SitesPath(binary);
            File.WriteAllText(sites, schema.SitesJson());
            RecordBuild(dir, binary, sites);
        }
        return build;
    }

    /// <summary>The files a snapshot build carries no sites in: <paramref name="generated"/>, and with
    /// <paramref name="files"/> (<c>BAYAN_SNAPSHOT_FILES</c>) every <paramref name="compiled"/> file it does not list;
    /// an entry on disk that this compilation does not compile is ignored for this build (Q820), and one that does not
    /// exist throws, naming <paramref name="variable"/>. A relative compiled path (an
    /// imported module's file, relative to the compilation's <c>DisplayRoot</c>, Compilation.cs:209) resolves against
    /// <paramref name="root"/>, and a std module's file is its <see cref="StdSource"/>.</summary>
    internal static HashSet<string> SnapshotSkip(IEnumerable<string> compiled, IReadOnlySet<string> generated, string? files, string variable = "BAYAN_SNAPSHOT_FILES", string? root = null)
    {
        var skip = new HashSet<string>(generated, StringComparer.Ordinal);
        if (files is not { Length: > 0 }) return skip;
        var paths = compiled.ToList();
        var keep = files.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(f => Path.GetFullPath(f, Golden.RepoRoot)).ToHashSet(StringComparer.Ordinal);
        var full = (string p) => StdSource(p) ?? (root is null ? Path.GetFullPath(p) : Path.GetFullPath(p, root));
        var all = paths.Select(full).ToHashSet(StringComparer.Ordinal);
        if (keep.FirstOrDefault(k => !all.Contains(k) && !File.Exists(k)) is { } missing)
            throw new InvalidOperationException($"{variable}: {missing} matches no compiled source");
        skip.UnionWith(paths.Where(p => !keep.Contains(full(p))));
        return skip;
    }

    /// <summary>The <c>lib/std/</c> file a std module's <c>&lt;std&gt;/m/f.byn</c> (Compilation.cs:193) is embedded from, or
    /// null for any other path.</summary>
    internal static string? StdSource(string path) =>
        path.StartsWith("<std>/", StringComparison.Ordinal) ? Path.Combine(Golden.RepoRoot, "lib", "std", path["<std>/".Length..]) : null;

    /// <summary>Lists <paramref name="binary"/> and its site map in <paramref name="dir"/>'s <c>manifest.json</c>.</summary>
    internal static void RecordBuild(string dir, string binary, string sites)
    {
        lock (SnapshotLock)
        {
            ResetOnce(dir);
            var path = Path.Combine(dir, "manifest.json");
            var manifest = File.Exists(path) ? JsonNode.Parse(File.ReadAllText(path))!.AsObject() : new JsonObject { ["version"] = 1, ["builds"] = new JsonArray() };
            var builds = manifest["builds"]!.AsArray();
            if (!builds.Any(b => (string?)b!["binary"] == binary))
                builds.Add(new JsonObject { ["binary"] = binary, ["sites"] = sites });
            File.WriteAllText(path, manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
        }
    }

    private static readonly Lock SnapshotLock = new();

    private static readonly HashSet<string> SnapshotReset = new(StringComparer.Ordinal);

    /// <summary>Under <see cref="SnapshotLock"/>: creates <paramref name="dir"/> and, on this process's first write to it,
    /// deletes a previous recording's <c>manifest.json</c> and <c>runs.jsonl</c>.</summary>
    private static void ResetOnce(string dir)
    {
        Directory.CreateDirectory(dir);
        if (!SnapshotReset.Add(Path.GetFullPath(dir))) return;
        File.Delete(Path.Combine(dir, "manifest.json"));
        File.Delete(Path.Combine(dir, "runs.jsonl"));
    }

    private static readonly Lock CoverageLock = new();

    private static readonly List<(string Dir, Bayan.Testing.CoverageMap Map, string Prefix, List<(string File, int Start, int End)> Functions)> CoverageBuilds = [];

    private static readonly Dictionary<string, (Stopwatch Total, double BuildSeconds)> CoverageRuns = new(StringComparer.Ordinal);

    /// <summary>
    /// Under <c>BAYAN_COVERAGE</c>: <paramref name="c"/> built with the coverage probes of <c>bayan test --coverage</c>
    /// (spec 013 T9), without counters in <paramref name="generated"/> and, with <c>BAYAN_COVERAGE_FILES</c>, only with
    /// counters in the files it lists (as <see cref="SnapshotSkip"/> picks them). The build returned runs through
    /// <c>&lt;dir&gt;/counters/&lt;n&gt;/run</c>, a script setting the runtime's <c>BAYAN_COVERAGE</c> to this build's
    /// own counter prefix, so every run of it counts; <see cref="WriteCoverage"/> writes the report when the process exits.
    /// The line span of every function with a body in the counted files is kept for <c>functions.json</c>.
    /// </summary>
    private static NativeBuild BuildCovered(Compilation c, IReadOnlySet<string> generated, string dir, string? files)
    {
        var clock = Stopwatch.StartNew();
        var compiled = c.Modules.SelectMany(m => m.AllFunctions).Select(f => f.File?.Source.Path).OfType<string>();
        var skip = SnapshotSkip(compiled, generated, files, "BAYAN_COVERAGE_FILES", c.DisplayRoot);
        var map = Bayan.Testing.CoverageInstrumenter.Instrument(c, skip);
        // A function's lines run from its declaration (a lambda: its body) to the end of its body.
        var functions = c.Modules.SelectMany(m => m.AllFunctions)
            .Where(f => f.File is { } file && !skip.Contains(file.Source.Path) && (f.Decl?.Span ?? f.Body?.Syntax.Span) is not null)
            .Select(f =>
            {
                var (source, span) = (f.File!.Source, (f.Decl?.Span ?? f.Body!.Syntax.Span));
                return (source.Path, source.GetLinePosition(span.Start).Line, source.GetLinePosition(Math.Max(span.Start, span.Start + span.Length - 1)).Line);
            }).ToList();
        if (OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("BAYAN_COVERAGE: the run wrapper is a POSIX shell script");
        var build = Toolchain.Build(CEmitter.Emit(Lowerer.Lower(c, coverage: map.Probes), false), BuildMode.Fast);
        if (build.ExecutablePath is not { } binary) return build;
        lock (CoverageLock)
        {
            if (!CoverageRuns.TryGetValue(dir, out var run))
            {
                // This process's first build into dir: an earlier recording's report, listings and counters go.
                if (CoverageRuns.Count == 0) AppDomain.CurrentDomain.ProcessExit += (_, _) => { foreach (var d in CoverageRuns.Keys) WriteCoverage(d); };
                Directory.CreateDirectory(dir);
                File.Delete(Path.Combine(dir, "bayan-quality.json"));
                File.Delete(Path.Combine(dir, "functions.json"));
                foreach (var listing in Directory.EnumerateFiles(dir, "*.cov.txt", SearchOption.AllDirectories)) File.Delete(listing);
                if (Directory.Exists(Path.Combine(dir, "counters"))) Directory.Delete(Path.Combine(dir, "counters"), recursive: true);
                run = (Stopwatch.StartNew(), 0);
            }
            CoverageRuns[dir] = (run.Total, run.BuildSeconds + clock.Elapsed.TotalSeconds);
            var counters = Path.Combine(dir, "counters", CoverageBuilds.Count(b => b.Dir == dir).ToString(System.Globalization.CultureInfo.InvariantCulture));
            Directory.CreateDirectory(counters);
            var prefix = Path.Combine(counters, "BAYAN_COVERAGE");
            var wrapper = Path.Combine(counters, "run");
            static string Quote(string s) => "'" + s.Replace("'", "'\\''") + "'";
            File.WriteAllText(wrapper, $"#!/bin/sh\nBAYAN_COVERAGE={Quote(prefix)} exec {Quote(binary)} \"$@\"\n");
            File.SetUnixFileMode(wrapper, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            // An imported module's file is compiled under a path relative to DisplayRoot (Compilation.cs:209), a std
            // module's under `<std>/`; the report and functions.json name it by its full path, as CliApp.cs:1027
            // resolves it, so the guard finds it in git.
            string Full(string p) => StdSource(p) ?? (Path.IsPathRooted(p) ? p : Path.GetFullPath(Path.Combine(c.DisplayRoot, p)));
            CoverageBuilds.Add((dir, map with { Counters = map.Counters.Select(k => k with { File = Full(k.File) }).ToList() }, prefix,
                functions.Select(f => (Full(f.Path), f.Item2, f.Item3)).ToList()));
            return build with { ExecutablePath = wrapper };
        }
    }

    /// <summary>
    /// Writes <paramref name="dir"/>'s <c>bayan-quality.json</c> (kind coverage, <see cref="Bayan.Testing.QualityReport.Coverage"/>)
    /// and a <c>&lt;file&gt;.cov.txt</c> listing per file, its path relative to the repo root, as <c>bayan test --coverage</c>
    /// writes them. The counters of every build into <paramref name="dir"/> are merged by what they count (file, line,
    /// column, kind, outcome, error path), since counter ids are per build; an unreadable counter file makes what nothing
    /// hit unknown, as in <c>bayan test</c>. Beside them, <c>functions.json</c> (<c>{"version": 1, "files": {"&lt;path&gt;":
    /// [{"span": [first line, last line], "lines": [covered, total], "branches": [covered, total]}, …]}}</c>) gives each
    /// counted file's functions, a counter counting in the innermost one holding its line, so the guard can gate only the
    /// functions a task's diff touches.
    /// </summary>
    internal static void WriteCoverage(string dir)
    {
        lock (CoverageLock)
        {
            var builds = CoverageBuilds.Where(b => b.Dir == dir).ToList();
            if (builds.Count == 0) return;
            var ids = new Dictionary<Bayan.Testing.CoverageCounter, int>();
            var counters = new List<Bayan.Testing.CoverageCounter>();
            var hits = new Dictionary<int, ulong>();
            var unknown = false;
            foreach (var (_, map, prefix, _) in builds)
            {
                var counts = new Dictionary<int, ulong>();
                foreach (var file in Directory.EnumerateFiles(Path.GetDirectoryName(prefix)!, Path.GetFileName(prefix) + ".*"))
                    if (!CliApp.MergeCounters(file, counts)) unknown = true;
                foreach (var counter in map.Counters)
                {
                    if (!ids.TryGetValue(counter with { Id = 0 }, out var id))
                    {
                        ids[counter with { Id = 0 }] = id = counters.Count;
                        counters.Add(counter with { Id = id });
                    }
                    if (counts.TryGetValue(counter.Id, out var n)) hits[id] = hits.GetValueOrDefault(id) + n;
                }
            }
            var (total, buildSeconds) = CoverageRuns[dir];
            // The report reads only the counters; the probes are the first build's, unused.
            var report = Bayan.Testing.QualityReport.Coverage("dotnet test (BAYAN_COVERAGE)", new Bayan.Testing.CoverageMap(counters, builds[0].Map.Probes),
                hits, unknown, buildSeconds, total.Elapsed.TotalSeconds);
            File.WriteAllText(Path.Combine(dir, "bayan-quality.json"), report.Json());
            var spans = new JsonObject();
            foreach (var g in builds.SelectMany(b => b.Functions).Distinct().GroupBy(f => f.File).OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                var fns = g.OrderBy(f => f.Start).ThenBy(f => f.End).ToList();
                var tally = fns.ToDictionary(f => f, _ => new int[4]);  // covered lines, lines, covered branches, branches
                (string, int, int)? Innermost(int line) => fns.Where(f => f.Start <= line && line <= f.End).Select(f => ((string, int, int)?)f).MinBy(f => f!.Value.Item3 - f.Value.Item2);
                foreach (var byLine in counters.Where(k => k.File == g.Key && k.Kind == Bayan.Testing.CoverageInstrumenter.Line).GroupBy(k => k.Line))
                    if (Innermost(byLine.Key) is { } fn) { tally[fn][1]++; if (byLine.All(k => hits.ContainsKey(k.Id))) tally[fn][0]++; }
                foreach (var k in counters.Where(k => k.File == g.Key && k.Kind == Bayan.Testing.CoverageInstrumenter.Branch))
                    if (Innermost(k.Line) is { } fn) { tally[fn][3]++; if (hits.ContainsKey(k.Id)) tally[fn][2]++; }
                spans[g.Key] = new JsonArray(fns.Select(f => (JsonNode)new JsonObject
                {
                    ["span"] = new JsonArray(f.Start, f.End),
                    ["lines"] = new JsonArray(tally[f][0], tally[f][1]),
                    ["branches"] = new JsonArray(tally[f][2], tally[f][3]),
                }).ToArray());
            }
            File.WriteAllText(Path.Combine(dir, "functions.json"), new JsonObject { ["version"] = 1, ["files"] = spans }.ToJsonString() + "\n");
            foreach (var f in report.Files)
            {
                if (!File.Exists(f.Path)) continue;
                var listing = Path.Combine(dir, CliApp.ListingName(Path.GetRelativePath(Golden.RepoRoot, f.Path)) + ".cov.txt");
                Directory.CreateDirectory(Path.GetDirectoryName(listing)!);
                File.WriteAllText(listing, Bayan.Testing.QualityReport.Listing(f.Coverage!, File.ReadAllLines(f.Path)));
            }
        }
    }

    /// <summary>
    /// Under <c>BAYAN_SNAPSHOT</c>: runs <paramref name="program"/> twice and appends the run to <c>runs.jsonl</c>; the
    /// replay compares its exit code, stdout and stderr. The C# oracle checks run once, at record time, in the caller
    /// after this returns: the run is appended before they assert, so a recording is valid only if the recording
    /// <c>dotnet test</c> exited 0 (013-plan.md:21, "compute the C# oracle once").
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
            ResetOnce(dir);
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
