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
/// Spec 014's slice (011 P2): <c>Path.isRooted</c>, <c>Path.fileName</c> and <c>Path.relative</c> give what .NET's
/// <c>Path.IsPathRooted</c>, <c>Path.GetFileName</c> and <c>Path.GetRelativePath</c> give on the same OS, over a seeded corpus
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
            // `OrdinalIgnoreCase` for a Windows root, where `ſ` and a surrogate pair fold differently. Bayan text is always valid
            // UTF-8 (literals, `Utf8.decode`, arguments and `Io` all check), so U+FFFD stands in for an invalid byte.
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
        }
        foreach (var (from, to) in Pairs(1014, 400))
        {
            // .NET throws on an empty path (and on Windows a blank one); the runtime panics there instead, so neither is in the program.
            if (Relative(from, to) is not { } r) continue;
            source.Append($"print(Path.relative(from: {Literal(from)}, to: {Literal(to)}))\n");
            expected.Add(r);
        }
        Assert.True(expected.Count > 900, $"only {expected.Count} lines");

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

    // The casing .NET's path comparisons use. `OrdinalCasing.ToUpper` (a unit outside a surrogate pair) and `CharUnicodeInfo.ToUpper`
    // (a pair's scalar) are what `OrdinalIgnoreCase` calls and are internal, so they are read by reflection; the test also checks
    // each against the public `OrdinalIgnoreCase` comparison.
    private static readonly Func<char, char> OrdinalUpper = typeof(object).Assembly.GetType("System.Globalization.OrdinalCasing")!
        .GetMethod("ToUpper", BindingFlags.Static | BindingFlags.NonPublic, [typeof(char)])!.CreateDelegate<Func<char, char>>();

    private static readonly Func<uint, uint> ScalarUpper = typeof(CharUnicodeInfo)
        .GetMethod("ToUpper", BindingFlags.Static | BindingFlags.NonPublic, [typeof(uint)])!.CreateDelegate<Func<uint, uint>>();

    private const string TablesBegin = "/* BEGIN path case tables";
    private const string TablesEnd = "/* END path case tables */";

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
    /// </summary>
    [Fact]
    public void CaseTablesMatchDotNet()
    {
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
