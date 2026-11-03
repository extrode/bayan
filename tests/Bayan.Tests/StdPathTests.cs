using System.Diagnostics;
using System.Text;
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
}
