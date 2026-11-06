using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Bayan.Backend.C;
using Bayan.Cli;

namespace Bayan.Tests;

/// <summary>
/// 012 T7 (R10): the golden cases of <c>lex</c>, <c>parse</c>, <c>typed</c>, <c>diag</c> and <c>run</c> through stage 2, the executable
/// <see cref="BootstrapTests"/> builds. Commands, per C# test: <c>LexerTests</c> <c>lex FILE</c> (<c>.tokens</c>); <c>ParserTests</c>
/// <c>parse --ast FILE</c> (<c>.ast</c>); <c>TypedDumpTests</c> <c>types --package DIR FILE</c> (<c>.types</c>), run in <c>DIR</c> on the
/// bare file name as <c>Golden.Load</c> names it; <c>RunTests</c> <c>run --package DIR [--mode M] FILE [-- ARGS]</c> (<c>.stdout</c>,
/// <c>.exitcode</c> and the optional <c>.stderr</c>). The comparison is <see cref="Golden.Compare"/> as in the C# tests.
/// <c>DiagTests</c> renders with <c>RenderAll</c>, which no command prints (the CLI adds a path line and a summary), so the
/// dispatcher's ruling (Q1052) is: stage 2's <c>check</c> and the C# CLI's (<c>dotnet bayan.dll</c>), same arguments and directory, agree on
/// exit code, stdout and stderr (elapsed seconds masked); the <c>.diag</c> files stay <c>DiagTests</c>'. The same ruling for <c>run</c>
/// stderr: stage 2's equals the C# CLI's on the case, since both print the compile warnings before the program's own output.
/// A <c>run/</c> input is left out when its file name ends in <c>.test.byn</c> (<c>RunTests</c> runs those with <c>bayan test -v</c>) or a
/// <c>.stdin</c> file with the same name beside it exists (spec Q5: stage 2's <c>run</c> feeds no stdin). The only skips are the two <c>diag/</c> cases in <see cref="DiagSkips"/> (Q1052).
/// </summary>
[Collection(nameof(ToolchainEnvironment))]
[Trait("Law", "L028")]
public sealed class SelfhostDriverGoldenTests
{
    private const ulong Seed = 0x9E3779B97F4A7C15;

    private const int QuickDivisor = 12;

    private static bool Full => Environment.GetEnvironmentVariable("BAYAN_LAW_TIER") == "full";

    internal static List<string> Pick(List<string> names)
    {
        if (Full) return names;
        var order = Enumerable.Range(0, names.Count).ToArray();
        var state = Seed;
        for (var i = order.Length - 1; i > 0; i--)
        {
            state ^= state << 13;
            state ^= state >> 7;
            state ^= state << 17;
            var j = (int)(state % (ulong)(i + 1));
            (order[i], order[j]) = (order[j], order[i]);
        }
        var keep = order.Take((names.Count + QuickDivisor - 1) / QuickDivisor).ToHashSet();
        return names.Where((_, i) => keep.Contains(i)).ToList();
    }

    private static TheoryData<string> Rows(string kind, Func<string, bool>? keep = null)
    {
        var data = new TheoryData<string>();
        foreach (var name in Pick([.. Golden.Cases(kind).Cast<object[]>().Select(r => (string)r[0]).Where(n => keep?.Invoke(n) ?? true)]))
            data.Add(name);
        return data;
    }

    private static bool InScope(string name) =>
        !name.EndsWith(".test.byn", StringComparison.Ordinal) && !File.Exists(Path.ChangeExtension(Path_(name), ".stdin"));

    public static TheoryData<string> Lex => Rows("lex");
    public static TheoryData<string> Parse => Rows("parse");
    public static TheoryData<string> Typed => Rows("typed");
    public static TheoryData<string> Diag => Rows("diag", name => !DiagSkips.ContainsKey(name));

    /// <summary>
    /// Listed skips of <c>diag/</c>: cases whose <c>.diag</c> holds a code that no C# CLI <c>check</c> invocation prints (Q1052), so comparing
    /// stage 2 with the CLI would pass without covering it. <see cref="DiagSkipsAreGapsInTheCli"/> checks each is still such a gap.
    /// </summary>
    private static readonly Dictionary<string, string> DiagSkips = new()
    {
        ["diag/cases-type-mismatch.test.byn"] = "Q1052", // BYN2202 needs IncludeTests: `check` never sets it; C# `test --all` does, stage 2 refuses `test`
        ["diag/capability-exceeded/main.byn"] = "Q1052", // BYN6004 needs the manifest's capabilities; `check .` stops at the missing src/app, `--package` ignores package.byn
    };
    /// <summary>
    /// <c>BAYAN_GOLDEN_SHARD=i/n</c> (0 &lt;= i &lt; n) keeps the <c>run/</c> cases whose index among the in-scope ones is i modulo n, so the slow
    /// theory can run in slices that each fit a time limit; unset keeps them all. Every case is built twice (stage 2 and the C# CLI).
    /// </summary>
    public static TheoryData<string> Run
    {
        get
        {
            var shard = Environment.GetEnvironmentVariable("BAYAN_GOLDEN_SHARD")?.Split('/');
            var data = new TheoryData<string>();
            var index = 0;
            foreach (var name in Pick([.. Golden.Cases("run").Cast<object[]>().Select(r => (string)r[0]).Where(InScope)]))
            {
                if (shard is not [var i, var n] || index++ % int.Parse(n) == int.Parse(i)) data.Add(name);
            }
            return data;
        }
    }

    private sealed record Result(int Exit, string Stdout, string Stderr);

    private static Result Start(string program, string[] prefix, string cwd, string[] args)
    {
        var psi = new ProcessStartInfo(program)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            StandardOutputEncoding = Encoding.UTF8,
            RedirectStandardError = true,
            StandardErrorEncoding = Encoding.UTF8,
            UseShellExecute = false,
            WorkingDirectory = cwd,
        };
        foreach (var a in prefix) psi.ArgumentList.Add(a);
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        p.StandardInput.Close();
        var stdout = p.StandardOutput.ReadToEndLfAsync();
        var stderr = p.StandardError.ReadToEndLfAsync();
        if (!p.WaitForExit(300_000))
        {
            p.Kill();
            Assert.Fail($"{program} {string.Join(' ', args)}: timed out after 300 s");
        }
        return new Result(p.ExitCode, stdout.Result, stderr.Result);
    }

    private static Result Stage2(string cwd, params string[] args) => Start(BootstrapTests.Stage2Executable(), [], cwd, args);

    private static Result CSharp(string cwd, params string[] args) => Start("dotnet", [typeof(CliApp).Assembly.Location], cwd, args);

    /// <summary>L028's mask: the elapsed seconds of <c>check</c>'s summary.</summary>
    private static string Mask(string text) => Regex.Replace(text, @"[0-9]+\.[0-9]{2}s", "<t>s");

    private static string Path_(string name) => Path.Combine(Golden.CasesRoot, name);

    /// <summary>A fresh empty directory under <c>tmp/golden/</c> for <paramref name="name"/>.</summary>
    private static string Work(string name)
    {
        var work = Path.Combine(Golden.RepoRoot, "tmp", "golden", Path.ChangeExtension(name, null).Replace('/', '_'));
        if (Directory.Exists(work)) Directory.Delete(work, recursive: true);
        Directory.CreateDirectory(work);
        return work;
    }

    private static string? Failure(string name, string extension, string actual, bool optional = false)
    {
        var expected = Path.ChangeExtension(Path_(name), extension);
        if (optional && actual.Length == 0 && !File.Exists(expected)) return null;
        // Never updates, whatever BAYAN_UPDATE says: the goldens are the C# tests' to write, stage 2 must not (R10).
        return Golden.Compare(expected, actual, update: false).Failure;
    }

    [Theory]
    [MemberData(nameof(Lex))]
    public void LexGolden(string name)
    {
        if (Failure(name, ".tokens", Stage2(Golden.RepoRoot, "lex", Path_(name)).Stdout) is { } f) Assert.Fail(f);
    }

    [Theory]
    [MemberData(nameof(Parse))]
    public void ParseGolden(string name)
    {
        if (Failure(name, ".ast", Stage2(Golden.RepoRoot, "parse", "--ast", Path_(name)).Stdout) is { } f) Assert.Fail(f);
    }

    [Theory]
    [MemberData(nameof(Typed))]
    public void TypedGolden(string name)
    {
        var dir = Path.GetDirectoryName(Path_(name))!;
        var r = Stage2(dir, "types", "--package", dir, Path.GetFileName(name));
        Assert.True(r.Exit == 0, r.Stderr);
        if (Failure(name, ".types", r.Stdout) is { } f) Assert.Fail(f);
    }

    /// <summary>The <c>check</c> arguments and directory for a diag case: a package directory is checked as a project, a file with <c>--package</c> on its directory.</summary>
    private static (string Cwd, string[] Args) DiagCommand(string name)
    {
        var path = Path_(name);
        var dir = Path.GetDirectoryName(path)!;
        // `--all` lifts the 20-diagnostic cap that hid map-copy's and input-paren-braceless-body's codes. A package directory without `src/`
        // (the CLI stops at the missing entry module) is checked as its `main.byn` with `--package`, as DiagTests compiles it.
        if (name.Count(c => c == '/') > 1) return Directory.Exists(Path.Combine(dir, "src")) ? (dir, ["check", "--all", "."]) : (dir, ["check", "--all", "--package", dir, "main.byn"]);
        return (dir, ["check", "--all", "--package", dir, Path.GetFileName(path)]);
    }

    private static HashSet<string> Codes(string text) => [.. Regex.Matches(text, @"BYN[0-9]+").Select(m => m.Value)];

    [Theory]
    [MemberData(nameof(Diag))]
    public void DiagAsCSharpCli(string name)
    {
        var (cwd, args) = DiagCommand(name);
        var expected = CSharp(cwd, args);
        var actual = Stage2(cwd, args);
        Assert.Equal(expected.Exit, actual.Exit);
        Assert.Equal(Mask(expected.Stdout), Mask(actual.Stdout));
        Assert.Equal(Mask(expected.Stderr), Mask(actual.Stderr));
        var missing = Codes(File.ReadAllText(Path.ChangeExtension(Path_(name), ".diag"))).Except(Codes(expected.Stdout + expected.Stderr)).ToList();
        Assert.True(missing.Count == 0, $"{name}: the C# CLI's check does not print {string.Join(", ", missing)} from its .diag");
    }

    [Fact]
    public void DiagSkipsAreGapsInTheCli()
    {
        foreach (var name in DiagSkips.Keys)
        {
            var (cwd, args) = DiagCommand(name);
            var expected = CSharp(cwd, args);
            Assert.NotEmpty(Codes(File.ReadAllText(Path.ChangeExtension(Path_(name), ".diag"))).Except(Codes(expected.Stdout + expected.Stderr)));
        }
        // The C# `test --all` (CliApp.cs:618, IncludeTests) does print cases-type-mismatch's BYN2202, but stage 2 refuses `test` (main.byn:206).
        var tests = Path_("diag/cases-type-mismatch.test.byn");
        var dir = Path.GetDirectoryName(tests)!;
        string[] testArgs = ["test", "--all", "--package", dir, Path.GetFileName(tests)];
        var cs = CSharp(dir, testArgs);
        Assert.Empty(Codes(File.ReadAllText(Path.ChangeExtension(tests, ".diag"))).Except(Codes(cs.Stdout + cs.Stderr)));
        var s2 = Stage2(dir, testArgs);
        Assert.Equal("error: `test` is not available in the self-hosted compiler; use the C# `bayan`\n", s2.Stderr);
    }

    /// <summary>BAYAN_UPDATE=1 must not make a comparison write its expected file (R10).</summary>
    [Fact]
    public void ComparisonsNeverWriteGoldens()
    {
        var name = "lex/bad-chars.byn";
        var golden = Path.ChangeExtension(Path_(name), ".tokens");
        var before = File.ReadAllBytes(golden);
        var saved = Environment.GetEnvironmentVariable("BAYAN_UPDATE");
        Environment.SetEnvironmentVariable("BAYAN_UPDATE", "1");
        try
        {
            Assert.True(Golden.UpdateMode);
            Assert.NotNull(Failure(name, ".tokens", "not the tokens\n"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("BAYAN_UPDATE", saved);
        }
        Assert.Equal(before, File.ReadAllBytes(golden));
    }

    /// <summary>The usage, failure and <c>--diagnostics json</c> paths of <c>types</c> and <c>check</c>, against <c>CliApp.Run</c> in this process, through the harness build the coverage gate measures; exit code, stdout and stderr (the usage and failure rows write only to stderr).</summary>
    [Theory]
    [InlineData("types")]
    [InlineData("check")]
    [InlineData("types", "--package", "diag", "diag/ambiguous-variant.byn")]
    [InlineData("check", "--diagnostics", "json", "--package", "typed", "typed/constrained-by-argument.byn")]
    public void TypesAndCheckAsCliApp(params string[] args)
    {
        args = [.. args.Select(a => a is "diag" or "typed" || a.EndsWith(".byn") ? Path.Combine(Golden.CasesRoot, a) : a)];
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var exit = CliApp.Run(args, stdout, stderr);
        var actual = SelfhostDriverHarness.Run(args);
        Assert.Equal(exit, actual.Exit);
        Assert.Equal(Mask(stdout.ToString().ReplaceLineEndings("\n")), Mask(actual.Out));
        Assert.Equal(Mask(stderr.ToString().ReplaceLineEndings("\n")), Mask(actual.Err));
    }

    [Theory]
    [MemberData(nameof(Run))]
    public void RunGolden(string name)
    {
        var input = Path_(name);
        var head = File.ReadLines(input).Take(2).ToList();
        if (OperatingSystem.IsWindows() ? head.Contains("// os: unix") : head.Contains("// os: windows")) return;
        var args = new List<string> { "run", "--package", Path.GetDirectoryName(input)! };
        if ((head.FirstOrDefault() ?? "") is { } first && first.StartsWith("// mode: ", StringComparison.Ordinal)) args.AddRange(["--mode", first["// mode: ".Length..].Trim()]);
        args.Add(input);
        var argsFile = Path.ChangeExtension(input, ".args");
        if (File.Exists(argsFile)) args.AddRange(["--", .. File.ReadAllLines(argsFile)]);
        var r = Stage2(Work(name), [.. args]);
        var pending = File.Exists(Path.ChangeExtension(input, ".pending"));
        var code = PanicExit.Portable(r.Exit);
        var failures = new[]
        {
            Failure(name, ".stdout", r.Stdout),
            Failure(name, ".exitcode", code == 0 ? "" : code + "\n", optional: true),
        }.Where(f => f is not null).ToList();
        // The stderr ruling (Q1052): the C# CLI's `run` on the same case.
        var expected = CSharp(Work(name), [.. args]);
        if (expected.Stderr != r.Stderr) failures.Add($"{name}: stderr differs from the C# CLI's\n--- C#\n{expected.Stderr}\n--- stage 2\n{r.Stderr}");
        if (pending)
        {
            if (failures.Count == 0) Assert.Fail($"{name} now matches its expected output; delete its .pending file");
            return;
        }
        if (failures.Count > 0) Assert.Fail(string.Join("\n", failures));
    }
}
