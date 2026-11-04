using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Bayan.Backend.C;
using Bayan.Cli;
using Bayan.Semantics;
using Bayan.Syntax.Text;

namespace Bayan.Tests;

/// <summary>
/// Spec 014's slice (011 P2), 014 T1, T2 and T3: <c>Path.isRooted</c>, <c>Path.fileName</c>, <c>Path.directoryName</c>, <c>Path.combine</c> (both forms), <c>Path.fullPath</c> and <c>Path.relative</c> give what .NET's
/// <c>Path.IsPathRooted</c>, <c>Path.GetFileName</c>, <c>Path.GetDirectoryName</c>, <c>Path.Combine</c>, <c>Path.GetFullPath</c> and <c>Path.GetRelativePath</c> give on the same OS, over a seeded corpus
/// with Windows-shaped inputs. On Windows the .NET calls use Windows rules, so the same corpus checks both separators, drive
/// letters, UNC and case-insensitive <c>relative</c> there. Both sides run in the test process's working directory, so relative
/// inputs resolve alike.
/// </summary>
public sealed class StdPathTests
{
    private static readonly string[] Atoms =
        ["a", "b", "B", "abc", "ABC", "x y", "é", ".", "..", "...", "", "C:", "c:", "C:x", "file.txt", ".hidden", "~", "UNC", "?", "server", "share"];

    private static readonly string[] Shapes =
    [
        "", " ", "/", "//", "\\", "\\\\", "a", "/a", "a/", "/a/", "a/b", "a//b", "./a", "../a", ".", "..", "a/.", "a/..", "/a/b/",
        "C:", "C:\\", "C:/", "C:x", "C:\\x", "C:/x\\y", "c:\\X\\", "1:\\x", "\\x", "/x\\y", "\\\\server\\share", "\\\\server\\share\\",
        "\\\\server\\share\\x", "//server/share/x", "\\\\server", "\\\\?\\C:\\x", "\\\\.\\C:\\x", "\\\\?\\UNC\\server\\share\\x", "\\??\\C:\\x",
        "a\\b", "a b/c d", "/é/ü", "file.txt", "/dir.d/", "a/b.c.d",
    ];

    private static List<string> Corpus(int seed, int count)
    {
        var random = new Random(seed);
        var seps = new[] { "/", "\\", "//", "\\\\", "/\\" };
        var list = new List<string>(Shapes);
        while (list.Count < count)
        {
            var sb = new StringBuilder();
            if (random.Next(3) == 0) sb.Append(seps[random.Next(seps.Length)]);
            var parts = random.Next(1, 5);
            for (var i = 0; i < parts; i++)
            {
                if (i > 0) sb.Append(seps[random.Next(seps.Length)]);
                sb.Append(Atoms[random.Next(Atoms.Length)]);
            }
            if (random.Next(4) == 0) sb.Append(seps[random.Next(seps.Length)]);
            list.Add(sb.ToString());
        }
        return list;
    }

    private static List<(string From, string To)> Pairs(int seed, int count)
    {
        var random = new Random(seed);
        var paths = Corpus(seed, count);
        var pairs = new List<(string, string)>
        {
            ("/a/b", "/a/b/c"), ("/a/b", "/a/B/c"), ("/A/b", "/a/b"), ("/a/b", "/a/c"), ("/a/b", "/x"), ("/a/b/", "/a/b"), ("/a/b", "/a/b/"),
            ("/a", "/a/./b/../c"), ("a", "b"), ("a", "../x"), ("/", "/a"), ("/a", "/"), ("/a/b", "/a/bc"), ("/a/bc", "/a/b"), ("/a/b", "/a/b/c/"),
            ("C:\\x", "C:\\y"), ("C:\\x", "c:\\X\\y"), ("C:\\x", "D:\\x"), ("C:x", "C:y"), ("\\\\server\\share\\a", "\\\\server\\share\\b"),
            ("\\\\server\\share\\a", "\\\\SERVER\\Share\\a\\b"), ("\\\\s1\\share\\a", "\\\\s2\\share\\a"), ("C:/a/b", "C:\\a\\c"), ("/..", "/a"),
            // Letters that differ only by case beyond ASCII: `char.ToUpperInvariant` per UTF-16 unit for the common part, and
            // `OrdinalIgnoreCase` for a Windows root, where `ſ` and a surrogate pair fold differently. Bytes that are not UTF-8 are
            // in InvalidUtf8MatchesDotNet, since no Bayan string holds them.
            ("/é/a", "/É/b"), ("/É/a", "/é/b"), ("/ß/a", "/ẞ/b"), ("/ı/a", "/I/b"), ("/i/a", "/İ/b"), ("/ς/a", "/Σ/b"), ("/σ/a", "/Σ/b"),
            ("/ſ/a", "/S/b"), ("/ǅ/a", "/ǆ/b"), ("/ɐ/a", "/Ɐ/b"), ("/\U00010428/a", "/\U00010400/b"), ("/\U00010400/a", "/\U00010400/b"),
            ("/a\U00010428", "/a\U00010400"), ("/\uFFFD/a", "/\uFFFD/b"), ("/é", "/É"), ("/éa/b", "/Éa"), ("/a/é/", "/A/É"),
            ("\\\\é\\share\\a", "\\\\É\\share\\b"), ("\\\\ſ\\share\\a", "\\\\S\\share\\b"), ("\\\\ς\\share\\a", "\\\\Σ\\share\\b"),
            ("\\\\\U00010428\\s\\a", "\\\\\U00010400\\s\\b"), ("C:\\é\\a", "c:\\É\\b"),
            // 8.3 short names: .NET expands a `~` segment that exists, and keeps a tail that does not (on Windows).
            ("C:\\PROGRA~1\\a", "C:\\Program Files\\b"), ("C:\\Program Files\\a", "C:\\PROGRA~1"), ("C:\\PROGRA~1\\NOPE~1\\x", "C:\\Program Files\\NOPE~1"),
            ("C:\\NOPE~1\\a", "C:\\NOPE~1\\b"), ("\\\\.\\C:\\PROGRA~1\\a", "\\\\.\\C:\\Program Files"), ("~1/a", "~1/b"),
        };
        for (var i = 0; i < count; i++)
        {
            var from = paths[random.Next(paths.Count)];
            // Half the targets share the source's start, so the common-prefix arms get exercised.
            var to = random.Next(2) == 0 ? from + "/" + paths[random.Next(paths.Count)] : paths[random.Next(paths.Count)];
            pairs.Add((from, to));
        }
        return pairs;
    }

    private static string Literal(string s)
    {
        var sb = new StringBuilder("\"");
        foreach (var ch in s)
            sb.Append(ch switch { '\\' => "\\\\", '"' => "\\\"", _ => ch.ToString() });
        return sb.Append('"').ToString();
    }

    private static string? Relative(string from, string to)
    {
        try { return Path.GetRelativePath(from, to); }
        catch (ArgumentException) { return null; }
    }

    private static string? FullPath(string path)
    {
        try { return Path.GetFullPath(path); }
        catch (ArgumentException) { return null; }
    }

    [Fact]
    public async Task EachFunctionMatchesDotNet()
    {
        Environment.SetEnvironmentVariable("BAYAN_CACHE", Path.Combine(Golden.RepoRoot, "tmp", "cache"));
        var dir = Path.Combine(Golden.RepoRoot, "tmp", "std-path");
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        Directory.CreateDirectory(dir);

        var source = new StringBuilder();
        var expected = new List<string>();
        foreach (var p in Corpus(14, 300))
        {
            source.Append($"print(Path.isRooted({Literal(p)}) ? \"true\" : \"false\")\n");
            expected.Add(Path.IsPathRooted(p) ? "true" : "false");
            source.Append($"print(Path.fileName({Literal(p)}))\n");
            expected.Add(Path.GetFileName(p));
            // .NET's null (a root, an empty path) prints as a marker no path in the corpus holds.
            source.Append($"print(Path.directoryName({Literal(p)}) ?? \"<null>\")\n");
            expected.Add(Path.GetDirectoryName(p) ?? "<null>");
            // Relative inputs resolve against the working directory on both sides. .NET throws on an empty path (and on Windows a
            // blank one); that is FullPathPanicsWhereDotNetThrows.
            if (FullPath(p) is not { } full) continue;
            source.Append($"print(Path.fullPath({Literal(p)}))\n");
            expected.Add(full);
        }
        foreach (var (from, to) in Pairs(1014, 400))
        {
            // .NET throws on an empty path (and on Windows a blank one); the runtime panics there instead, so neither is in the program.
            if (Relative(from, to) is not { } r) continue;
            source.Append($"print(Path.relative(from: {Literal(from)}, to: {Literal(to)}))\n");
            expected.Add(r);
        }
        // `combine` brackets its result: an empty one is a line too.
        foreach (var (a, b) in Pairs(2014, 200))
        {
            source.Append($"print($\"[{{Path.combine({Literal(a)}, {Literal(b)})}}]\")\n");
            expected.Add($"[{Path.Combine(a, b)}]");
        }
        // Spans of 0-4 parts from the atoms and the shapes, which hold rooted ones, so a later rooted part restarts the result.
        var spanRandom = new Random(3014);
        var spanParts = Atoms.Concat(Shapes).ToArray();
        for (var i = 0; i < 300; i++)
        {
            var parts = Enumerable.Range(0, i % 5).Select(_ => spanParts[spanRandom.Next(spanParts.Length)]).ToArray();
            source.Append($"print($\"[{{Path.combine([{string.Join(", ", parts.Select(Literal))}])}}]\")\n");
            expected.Add($"[{Path.Combine(parts)}]");
        }
        Assert.True(expected.Count > 900, $"only {expected.Count} lines");
        Assert.True(source.ToString().Split('\n').Count(l => l.StartsWith("print(Path.fullPath(\"") && !l.StartsWith("print(Path.fullPath(\"/")) > 100, "too few relative fullPath inputs");

        var c = Compilation.Create([new SourceText("paths.byn", Encoding.UTF8.GetBytes(source.ToString()))], dir);
        Assert.False(c.Diagnostics.HasErrors);
        var build = Toolchain.Build(CliApp.EmitC(c, BuildMode.Safe), BuildMode.Safe);
        Assert.True(build.Succeeded, build.Errors);
        var psi = new ProcessStartInfo(build.ExecutablePath!)
        {
            WorkingDirectory = Directory.GetCurrentDirectory(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardOutputEncoding = Encoding.UTF8,
        };
        using var proc = Process.Start(psi)!;
        var stderr = proc.StandardError.ReadToEndAsync();
        var stdout = await proc.StandardOutput.ReadToEndAsync();
        Assert.True(proc.WaitForExit(120_000), "program timed out after 120 s");
        Assert.True(proc.ExitCode == 0, await stderr);
        var actual = stdout.Split('\n');
        Assert.Equal(expected.Count + 1, actual.Length);
        var source0 = source.ToString().Split('\n');
        for (var i = 0; i < expected.Count; i++)
            Assert.True(expected[i] == actual[i], $"line {i + 1}: {source0[i]}\n.NET: [{expected[i]}]\nBayan: [{actual[i]}]");
    }

    /// <summary>
    /// <c>Path.fullPath</c> panics, naming itself, on each input where <c>Path.GetFullPath</c> throws <c>ArgumentException</c> (empty,
    /// a NUL byte, and on Windows blank), and prints what it returns otherwise.
    /// </summary>
    [Fact]
    public async Task FullPathPanicsWhereDotNetThrows()
    {
        Environment.SetEnvironmentVariable("BAYAN_CACHE", Path.Combine(Golden.RepoRoot, "tmp", "cache"));
        var dir = Path.Combine(Golden.RepoRoot, "tmp", "std-path-full");
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        Directory.CreateDirectory(dir);
        (string Path, string Panic)[] cases =
            [("", "the path is empty"), (" ", "the path is empty"), ("\0", "the path has a NUL byte"), ("a\0b", "the path has a NUL byte"), ("/a\0", "the path has a NUL byte")];
        foreach (var (p, panic) in cases)
        {
            var full = FullPath(p);
            // Only a blank path differs by OS: .NET throws on it on Windows alone.
            Assert.True(full is null || p == " ", $"GetFullPath([{p}]) does not throw ArgumentException");
            var source = $"print(Path.fullPath({Literal(p).Replace("\0", "\\0")}))\n";
            var c = Compilation.Create([new SourceText("full.byn", Encoding.UTF8.GetBytes(source))], dir);
            Assert.False(c.Diagnostics.HasErrors);
            var build = Toolchain.Build(CliApp.EmitC(c, BuildMode.Safe), BuildMode.Safe);
            Assert.True(build.Succeeded, build.Errors);
            var psi = new ProcessStartInfo(build.ExecutablePath!)
            {
                WorkingDirectory = Directory.GetCurrentDirectory(),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                StandardOutputEncoding = Encoding.UTF8,
            };
            using var proc = Process.Start(psi)!;
            var stderr = proc.StandardError.ReadToEndAsync();
            var stdout = await proc.StandardOutput.ReadToEndAsync();
            Assert.True(proc.WaitForExit(120_000), "program timed out after 120 s");
            var err = (await stderr).ReplaceLineEndings("\n");
            if (full is null)
            {
                Assert.True(proc.ExitCode != 0, $"[{p}]: .NET throws, Bayan exits 0 with [{stdout}]");
                Assert.Equal($"panic: Path.fullPath: {panic}\n  at full.byn:1\n", err);
            }
            else
            {
                Assert.True(proc.ExitCode == 0, err);
                Assert.Equal(full + "\n", stdout);
            }
        }
    }

    /// <summary>
    /// <c>byn_path_relative</c> on bytes that are not UTF-8 gives what <c>Path.GetRelativePath</c> gives on their
    /// <c>Encoding.UTF8</c> decoding (each maximal invalid subpart one U+FFFD). Bayan text is always valid UTF-8 (literals,
    /// <c>Utf8.decode</c>, arguments and <c>Io</c> all check), so a native driver (<c>tests/native/path-relative-cases.c</c>)
    /// calls the runtime directly.
    /// </summary>
    [Fact]
    public async Task InvalidUtf8MatchesDotNet()
    {
        byte[][] bad =
        [
            [0xFF], [0xC0, 0xAF], [0xE2, 0x82], [0xED, 0xA0, 0x80], [0xE0, 0x80], [0xF4, 0x90], [0xF0, 0x90, 0x90], [0x80, 0x80],
            [0xC3], [0xFE, 0xC3, 0xA9],
        ];
        byte[][] good = [[], "a"u8.ToArray(), "é"u8.ToArray(), "É"u8.ToArray(), "\U00010428"u8.ToArray(), "\U00010400"u8.ToArray()];
        static byte[] Cat(params byte[][] parts) => parts.SelectMany(p => p).ToArray();
        var cwd = Encoding.UTF8.GetBytes(Directory.GetCurrentDirectory());
        var pairs = new List<(byte[] From, byte[] To)>();
        foreach (var b in bad)
            foreach (var g in good)
            {
                var seg = Cat(b, g);
                pairs.Add((Cat("/"u8.ToArray(), seg, "/a"u8.ToArray()), Cat("/"u8.ToArray(), seg, "/b"u8.ToArray())));
                pairs.Add((Cat("/"u8.ToArray(), g, b, "/a"u8.ToArray()), Cat("/"u8.ToArray(), g, "/b"u8.ToArray())));
                pairs.Add((Cat("/x/"u8.ToArray(), seg), Cat("/x/"u8.ToArray(), g, b, "/y"u8.ToArray())));
                pairs.Add((Cat("/x"u8.ToArray(), b), Cat("/x"u8.ToArray(), b, "/"u8.ToArray(), g)));
                pairs.Add((Cat(seg, "/a"u8.ToArray()), Cat(seg, "/b"u8.ToArray())));
                pairs.Add((Cat(cwd, "/"u8.ToArray(), b), Cat(b, "/"u8.ToArray(), g, "c"u8.ToArray())));
                pairs.Add((Cat("C:\\"u8.ToArray(), seg, "\\a"u8.ToArray()), Cat("c:\\"u8.ToArray(), seg, "\\b"u8.ToArray())));
                pairs.Add((Cat("\\\\"u8.ToArray(), seg, "\\s\\a"u8.ToArray()), Cat("\\\\"u8.ToArray(), seg, "\\s\\b"u8.ToArray())));
            }
        // Different bytes that decode to the same U+FFFD count are the same segment to .NET.
        foreach (var b1 in bad)
            foreach (var b2 in bad)
                pairs.Add((Cat("/"u8.ToArray(), b1, "/a"u8.ToArray()), Cat("/"u8.ToArray(), b2, "/b"u8.ToArray())));

        var input = new StringBuilder();
        var expected = new List<string>();
        var shown = new List<string>();
        foreach (var (from, to) in pairs)
        {
            // .NET throws on an empty path (and on Windows a blank one); the runtime panics there, so neither is a case.
            if (Relative(Encoding.UTF8.GetString(from), Encoding.UTF8.GetString(to)) is not { } r) continue;
            input.Append(Convert.ToHexStringLower(from)).Append(' ').Append(Convert.ToHexStringLower(to)).Append('\n');
            expected.Add(r);
            shown.Add($"{Convert.ToHexStringLower(from)} -> {Convert.ToHexStringLower(to)}");
        }
        Assert.True(expected.Count > 400, $"only {expected.Count} cases");

        var actual = await RunDriver(input.ToString());
        Assert.Equal(expected.Count + 1, actual.Length);
        for (var i = 0; i < expected.Count; i++)
        {
            var got = Encoding.UTF8.GetString(Convert.FromHexString(actual[i]));
            Assert.True(expected[i] == got, $"case {i + 1}: {shown[i]}\n.NET: [{expected[i]}]\nruntime: [{got}] ({actual[i]})");
        }
    }

    /// <summary>
    /// <c>byn_path_full</c> on 8.3 short names gives <c>Path.GetFullPath</c>, step by step: an existing short name, one under a
    /// non-existent tail (the trimmed retry of <c>PathHelper.TryExpandShortFileName</c>), a <c>\\.\</c> device path and one past
    /// 260 units. <c>relative</c> from a root no target shares returns the target's full path, on both sides, so the driver shows it.
    /// Short names are Windows-only, and elsewhere every absolute path shares the root <c>/</c>.
    /// </summary>
    [Fact]
    public async Task ShortNamesExpandAsDotNet()
    {
        if (!OperatingSystem.IsWindows()) return;
        const string elsewhere = "\\\\?\\Z:\\";
        string[] paths =
        [
            "C:\\PROGRA~1\\a", "C:\\PROGRA~1", "C:\\PROGRA~1\\NOPE~1\\x", "C:\\NOPE~1\\a", "\\\\.\\C:\\PROGRA~1\\a",
            "C:\\PROGRA~1\\" + string.Join("\\", Enumerable.Repeat("abcdefghij", 30)),
        ];
        static string Hex(string s) => Convert.ToHexStringLower(Encoding.UTF8.GetBytes(s));
        var actual = await RunDriver(string.Concat(paths.Select(p => Hex(elsewhere) + " " + Hex(p) + "\n")));
        Assert.Equal(paths.Length + 1, actual.Length);
        var steps = paths.Select((p, i) => (Path: p, Full: Path.GetFullPath(p), Relative: Relative(elsewhere, p),
            Runtime: Encoding.UTF8.GetString(Convert.FromHexString(actual[i])))).ToList();
        var report = string.Join("\n", steps.Select(s => $"{s.Path}\n  GetFullPath: [{s.Full}]\n  runtime:     [{s.Runtime}]"));
        Assert.True(steps.All(s => s.Relative == s.Full), "GetRelativePath from another root is not GetFullPath:\n" + report);
        Assert.True(steps.All(s => s.Runtime == s.Full), report);
    }

    /// <summary>Runs <c>tests/native/path-relative-cases.c</c> on <paramref name="input"/> and returns its output lines.</summary>
    private static async Task<string[]> RunDriver(string input)
    {
        var driver = File.ReadAllText(Path.Combine(Golden.RepoRoot, "tests", "native", "path-relative-cases.c"));
        var build = Toolchain.Build(driver, BuildMode.Debug);
        Assert.True(build.Succeeded, build.Errors);
        var psi = new ProcessStartInfo(build.ExecutablePath!)
        {
            WorkingDirectory = Directory.GetCurrentDirectory(),
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        using var proc = Process.Start(psi)!;
        var stderr = proc.StandardError.ReadToEndAsync();
        var stdout = proc.StandardOutput.ReadToEndLfAsync();
        proc.StandardInput.Write(input);
        proc.StandardInput.Close();
        var output = await stdout;
        Assert.True(proc.WaitForExit(120_000), "driver timed out after 120 s");
        Assert.True(proc.ExitCode == 0, await stderr);
        return output.Split('\n');
    }

    // The casing .NET's path comparisons use. `OrdinalCasing.ToUpper` (a unit outside a surrogate pair) and `CharUnicodeInfo.ToUpper`
    // (a pair's scalar) are what `OrdinalIgnoreCase` calls and are internal, so they are read by reflection; the test also checks
    // each against the public `OrdinalIgnoreCase` comparison.
    private static readonly Func<char, char> OrdinalUpper = typeof(object).Assembly.GetType("System.Globalization.OrdinalCasing")!
        .GetMethod("ToUpper", BindingFlags.Static | BindingFlags.NonPublic, [typeof(char)])!.CreateDelegate<Func<char, char>>();

    private static readonly Func<uint, uint> ScalarUpper = typeof(CharUnicodeInfo)
        .GetMethod("ToUpper", BindingFlags.Static | BindingFlags.NonPublic, [typeof(uint)])!.CreateDelegate<Func<uint, uint>>();

    // One block per OS whose .NET folds case in paths, since their casing differs; Linux compares ordinally and has none.
    private static readonly string TablesOs = OperatingSystem.IsWindows() ? "Windows" : "macOS";
    private static readonly string TablesBegin = $"/* BEGIN path case tables ({TablesOs})";
    private static readonly string TablesEnd = $"/* END path case tables ({TablesOs}) */";

    /// <summary><c>{first, last, step, delta}</c> runs (step 1 or 2) of the values in <paramref name="from"/>..<paramref name="to"/> that <paramref name="map"/> moves.</summary>
    private static List<int[]> Runs(int from, int to, Func<int, int> map)
    {
        var runs = new List<int[]>();
        for (var c = from; c <= to; c++)
        {
            var delta = map(c) - c;
            if (delta == 0) continue;
            if (runs.Count > 0 && runs[^1] is var r && r[3] == delta
                && (r[0] == r[1] ? c - r[1] is 1 or 2 : c - r[1] == r[2]))
            {
                if (r[0] == r[1]) r[2] = c - r[1];
                r[1] = c;
            }
            else
            {
                runs.Add([c, c, 1, delta]);
            }
        }
        return runs;
    }

    private static string Table(string decl, IEnumerable<int[]> rows, string empty)
    {
        var items = rows.Select(r => "{" + string.Join(", ", r.Select((v, i) => i < 2 && v >= 0 ? $"0x{v:X4}" : v.ToString())) + "}").ToList();
        if (items.Count == 0) items.Add(empty);
        var sb = new StringBuilder(decl).Append(" = {\n");
        for (var i = 0; i < items.Count; i += 4)
            sb.Append("    ").Append(string.Join(", ", items.Skip(i).Take(4))).Append(i + 4 < items.Count ? ",\n" : "\n");
        return sb.Append("};\n").ToString();
    }

    private static string GenerateTables()
    {
        var invariant = Runs(0, 0xFFFF, c => char.ToUpperInvariant((char)c));
        var ordinal = Enumerable.Range(0, 0x10000).Where(c => OrdinalUpper((char)c) != char.ToUpperInvariant((char)c))
            .Select(c => new[] { c, (int)OrdinalUpper((char)c) });
        var scalar = Runs(0x10000, 0x10FFFF, c => (int)ScalarUpper((uint)c));
        return TablesBegin + ": generated by StdPathTests.CaseTablesMatchDotNet from .NET's own casing; `BAYAN_UPDATE=1` rewrites them. */\n"
            + "/* `char.ToUpperInvariant` on a UTF-16 unit: {first, last, step, delta} runs. */\n"
            + Table("static const int32_t byn_path_upper_invariant[][4]", invariant, "{-1, -1, 1, 0}")
            + "/* `OrdinalCasing.ToUpper` (`OrdinalIgnoreCase` on a unit) where it differs from `char.ToUpperInvariant`: {unit, upper}. */\n"
            + Table("static const int32_t byn_path_upper_ordinal[][2]", ordinal, "{-1, -1}")
            + "/* `CharUnicodeInfo.ToUpper` (`OrdinalIgnoreCase` on a surrogate pair's scalar): {first, last, step, delta} runs. */\n"
            + Table("static const int32_t byn_path_upper_scalar[][4]", scalar, "{-1, -1, 1, 0}")
            + TablesEnd;
    }

    /// <summary>The rows of the C array named <paramref name="name"/> in <paramref name="block"/>.</summary>
    private static List<long[]> ParseTable(string block, string name)
    {
        var start = block.IndexOf(name + "[]", StringComparison.Ordinal);
        Assert.True(start >= 0, $"no {name} in the runtime");
        var body = block[block.IndexOf('{', start)..block.IndexOf("};", start, StringComparison.Ordinal)];
        return Regex.Matches(body, @"\{([^{}]*)\}")
            .Select(m => m.Groups[1].Value.Split(',').Select(v => v.Trim()).Select(v => v.StartsWith("0x") ? Convert.ToInt64(v[2..], 16) : long.Parse(v)).ToArray())
            .ToList();
    }

    /// <summary>What <c>byn_path_runs</c> gives for <paramref name="c"/>.</summary>
    private static long Lookup(List<long[]> runs, long c)
    {
        int lo = 0, hi = runs.Count;
        while (lo < hi)
        {
            var mid = (lo + hi) / 2;
            if (runs[mid][0] <= c) lo = mid + 1;
            else hi = mid;
        }
        return lo > 0 && runs[lo - 1] is var r && c <= r[1] && (c - r[0]) % r[2] == 0 ? c + r[3] : c;
    }

    /// <summary>
    /// The runtime's case tables (<c>src/runtime/bayan-rt.c</c>) give <c>char.ToUpperInvariant</c> and <c>OrdinalCasing.ToUpper</c> for
    /// every one of the 65,536 UTF-16 units, and <c>CharUnicodeInfo.ToUpper</c> for every scalar above U+FFFF, as this .NET does.
    /// Only the block for the running OS is checked and, with <c>BAYAN_UPDATE=1</c>, rewritten; the other is left as it is.
    /// </summary>
    [Fact]
    public void CaseTablesMatchDotNet()
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsMacOS()) return;
        var path = Path.Combine(Golden.RepoRoot, "src", "runtime", "bayan-rt.c");
        var text = File.ReadAllText(path);
        var begin = text.IndexOf(TablesBegin, StringComparison.Ordinal);
        var end = text.IndexOf(TablesEnd, StringComparison.Ordinal);
        Assert.True(begin >= 0 && end > begin, "no case tables in the runtime");
        var block = text[begin..(end + TablesEnd.Length)];
        var generated = GenerateTables();
        if (Golden.UpdateMode && block != generated)
        {
            File.WriteAllText(path, text[..begin] + generated + text[(end + TablesEnd.Length)..]);
            block = generated;
        }

        var invariant = ParseTable(block, "byn_path_upper_invariant");
        var ordinal = ParseTable(block, "byn_path_upper_ordinal").ToDictionary(r => r[0], r => r[1]);
        var scalar = ParseTable(block, "byn_path_upper_scalar");
        var wrong = new List<string>();
        for (var c = 0; c <= 0xFFFF; c++)
        {
            var upper = Lookup(invariant, c);
            if (upper != char.ToUpperInvariant((char)c)) wrong.Add($"ToUpperInvariant U+{c:X4}: C {upper:X4}, .NET {(int)char.ToUpperInvariant((char)c):X4}");
            var ord = ordinal.TryGetValue(c, out var o) ? o : upper;
            if (ord != OrdinalUpper((char)c)) wrong.Add($"OrdinalCasing U+{c:X4}: C {ord:X4}, .NET {(int)OrdinalUpper((char)c):X4}");
            if (!char.IsSurrogate((char)c) && !string.Equals(((char)c).ToString(), ((char)ord).ToString(), StringComparison.OrdinalIgnoreCase))
                wrong.Add($"OrdinalIgnoreCase U+{c:X4} vs U+{ord:X4}: not equal");
        }
        for (var c = 0x10000; c <= 0x10FFFF; c++)
        {
            var upper = Lookup(scalar, c);
            if (upper != ScalarUpper((uint)c)) wrong.Add($"CharUnicodeInfo.ToUpper U+{c:X}: C {upper:X}, .NET {ScalarUpper((uint)c):X}");
            else if (upper != c && !string.Equals(char.ConvertFromUtf32(c), char.ConvertFromUtf32((int)upper), StringComparison.OrdinalIgnoreCase))
                wrong.Add($"OrdinalIgnoreCase U+{c:X} vs U+{upper:X}: not equal");
        }
        Assert.True(wrong.Count == 0, $"{wrong.Count} differences (BAYAN_UPDATE=1 regenerates the tables):\n" + string.Join("\n", wrong.Take(20)));
        Assert.True(block == generated, "the case tables are not as generated (BAYAN_UPDATE=1 regenerates them)");
    }
}
