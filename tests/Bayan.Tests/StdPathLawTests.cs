using System.Diagnostics;
using System.Text;
using Bayan.Backend.C;
using Bayan.Cli;
using Bayan.Semantics;
using Bayan.Syntax.Text;

namespace Bayan.Tests;

/// <summary>Law L026: a compiled Bayan program calling <c>Path.combine</c> (both forms), <c>fileName</c>, <c>directoryName</c>,
/// <c>isRooted</c>, <c>fullPath</c> and <c>relative</c> prints what .NET's <c>Path</c> returns on the same host, over the
/// <see cref="StdPathTests"/> corpus. One program per seed runs every function's whole corpus. Where .NET throws, the program panics.</summary>
public sealed class StdPathLawTests
{
    /// <summary>Quick tier (unset): one seed, 300 inputs per corpus and 300 and 150 pairs (<c>StdPathTests.EachFunctionMatchesDotNet</c> uses 400 and 200). <c>BAYAN_LAW_TIER=full</c>: five seeds, 2000 inputs each.</summary>
    private static (int[] Seeds, int Count) Tier =>
        Environment.GetEnvironmentVariable("BAYAN_LAW_TIER") == "full" ? ([14, 15, 16, 17, 18], 2000) : ([14], 300);

    private static async Task<(int Exit, string Stdout, string Stderr)> BuildAndRun(string name, string source)
    {
        Environment.SetEnvironmentVariable("BAYAN_CACHE", Path.Combine(Golden.RepoRoot, "tmp", "cache"));
        var dir = Path.Combine(Golden.RepoRoot, "tmp", "law-l026", name);
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        Directory.CreateDirectory(dir);
        var c = Compilation.Create([new SourceText("paths.byn", Encoding.UTF8.GetBytes(source))], dir);
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
        return (proc.ExitCode, stdout, await stderr);
    }

    /// <summary>All six functions agree with .NET on every corpus input; a failure names the function, input, Bayan output and .NET result.</summary>
    [Fact]
    [Trait("Law", "L026")]
    public async Task EveryFunctionAgreesWithDotNet()
    {
        var (seeds, count) = Tier;
        foreach (var seed in seeds)
        {
            var source = new StringBuilder();
            var expected = new List<string>();
            var throwing = new Dictionary<string, string>();
            var shown = new List<string>();
            void Add(string call, string show, string result)
            {
                source.Append($"print({call})\n");
                shown.Add(show);
                expected.Add(result);
            }
            string L(string s) => StdPathTests.Literal(s);
            foreach (var p in StdPathTests.Corpus(seed, count))
            {
                Add($"Path.isRooted({L(p)}) ? \"true\" : \"false\"", $"isRooted({L(p)})", Path.IsPathRooted(p) ? "true" : "false");
                Add($"Path.fileName({L(p)})", $"fileName({L(p)})", Path.GetFileName(p));
                Add($"Path.directoryName({L(p)}) ?? \"<null>\"", $"directoryName({L(p)})", Path.GetDirectoryName(p) ?? "<null>");
                if (StdPathTests.FullPath(p) is { } full) Add($"Path.fullPath({L(p)})", $"fullPath({L(p)})", full);
                else throwing.TryAdd($"Path.fullPath({L(p)})", "Path.fullPath");
            }
            foreach (var (from, to) in StdPathTests.Pairs(seed + 1000, count))
            {
                if (StdPathTests.Relative(from, to) is { } r)
                    Add($"Path.relative(from: {L(from)}, to: {L(to)})", $"relative({L(from)}, {L(to)})", r);
                else throwing.TryAdd($"Path.relative(from: {L(from)}, to: {L(to)})", "Path.relative");
            }
            foreach (var (a, b) in StdPathTests.Pairs(seed + 2000, count / 2))
                Add($"$\"[{{Path.combine({L(a)}, {L(b)})}}]\"", $"combine({L(a)}, {L(b)})", $"[{Path.Combine(a, b)}]");
            var spanRandom = new Random(seed + 3000);
            var spanParts = StdPathTests.Atoms.Concat(StdPathTests.Shapes).ToArray();
            for (var i = 0; i < count; i++)
            {
                var parts = Enumerable.Range(0, i % 5).Select(_ => spanParts[spanRandom.Next(spanParts.Length)]).ToArray();
                Add($"$\"[{{Path.combine([{string.Join(", ", parts.Select(L))}])}}]\"", $"combine([{string.Join(", ", parts.Select(L))}])", $"[{Path.Combine(parts)}]");
            }

            var (exit, stdout, stderr) = await BuildAndRun($"seed{seed}", source.ToString());
            Assert.True(exit == 0, $"seed {seed}: exit {exit}\n{stderr}");
            var actual = stdout.Split('\n');
            var common = Math.Min(expected.Count, actual.Length - 1);
            for (var i = 0; i < common; i++)
                Assert.True(expected[i] == actual[i], $"seed {seed}: {shown[i]}\n.NET: [{expected[i]}]\nBayan: [{actual[i]}]");
            Assert.True(actual.Length - 1 == expected.Count,
                $"seed {seed}: {actual.Length - 1} lines, expected {expected.Count}; first unmatched: {(common < expected.Count ? $"{shown[common]}\n.NET: [{expected[common]}]\nBayan: missing" : $"extra Bayan line [{actual[common]}]")}");

            // Every distinct corpus input on which .NET throws (at most 20 per seed) is its own program, which must panic naming the function.
            var n = 0;
            foreach (var (call, function) in throwing.Take(20))
                await AssertPanics($"seed{seed}-throw{n++}", call, function);
        }
    }

    private static async Task AssertPanics(string name, string call, string function)
    {
        var (exit, stdout, stderr) = await BuildAndRun(name, $"print({call})\n");
        Assert.True(exit != 0 && stderr.Contains($"panic: {function}:", StringComparison.Ordinal),
            $"{call}: .NET throws, Bayan exits {exit} with [{stdout}] [{stderr}]");
    }

    /// <summary>Where .NET throws <c>ArgumentException</c>, the Bayan program exits nonzero with a panic naming the function.</summary>
    [Fact]
    [Trait("Law", "L026")]
    public async Task ThrowingInputsPanic()
    {
        (string Name, string Call, bool Throws)[] cases =
        [
            ("full-empty", "Path.fullPath(\"\")", StdPathTests.FullPath("") is null),
            ("full-nul", "Path.fullPath(\"a\\0b\")", StdPathTests.FullPath("a\0b") is null),
            ("relative-from-empty", "Path.relative(from: \"\", to: \"/a\")", StdPathTests.Relative("", "/a") is null),
            ("relative-to-empty", "Path.relative(from: \"/a\", to: \"\")", StdPathTests.Relative("/a", "") is null),
        ];
        foreach (var (name, call, throws) in cases)
        {
            Assert.True(throws, $"{call}: .NET does not throw ArgumentException");
            await AssertPanics(name, call, call.StartsWith("Path.fullPath", StringComparison.Ordinal) ? "Path.fullPath" : "Path.relative");
        }
    }
}
