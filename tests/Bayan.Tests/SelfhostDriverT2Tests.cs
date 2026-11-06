using System.Text.RegularExpressions;
using Bayan.Cli;

namespace Bayan.Tests;

/// <summary>
/// 012 T2: <c>lex</c>, <c>parse --ast</c>, <c>types</c> and <c>check</c> of the driver against <see cref="CliApp.Run"/> (R2).
/// The expected exit code and streams come from the C# run in-process; only the elapsed seconds of <c>check</c>'s summary are masked.
/// No style flag is passed: the dumps and diagnostics render plain in the driver (no-style: Q947).
/// </summary>
public sealed class SelfhostDriverT2Tests
{
    private static string Mask(string text) =>
        Regex.Replace(Regex.Replace(text, @" in \d+\.\d\ds", " in masked"), @"^(bayan \S+) \(.*\)$", "$1 (masked)", RegexOptions.Multiline);

    private static (int Exit, string Out, string Err) Oracle(string[] args)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var exit = CliApp.Run(args, stdout, stderr);
        return (exit, stdout.ToString().ReplaceLineEndings("\n"), stderr.ToString().ReplaceLineEndings("\n"));
    }

    private static void AssertSameAsCs(params string[] args)
    {
        var expected = Oracle(args);
        var actual = SelfhostDriverHarness.Run(args);
        var label = string.Join(' ', args);
        Assert.Equal(expected.Exit, actual.Exit);
        SelfhostEmitterHarness.AssertSame($"stdout of `{label}`", Mask(expected.Out), Mask(actual.Out));
        SelfhostEmitterHarness.AssertSame($"stderr of `{label}`", Mask(expected.Err), Mask(actual.Err));
    }

    private static string Case(string name) => Path.Combine(Golden.CasesRoot, name);

    /// <summary>
    /// `types` and `check` compile the whole directory of a file as one entry module, so a case among its siblings in
    /// `tests/cases/<kind>/` would fail on their top-level statements. The case is copied alone (a multi-module case with
    /// its directory) under `tmp/t2/`, and both sides run on the copy.
    /// </summary>
    private static string Isolated(string name)
    {
        var source = Case(name);
        var dir = Path.Combine(Golden.RepoRoot, "tmp", "t2", name.Replace(".byn", ""));
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        Directory.CreateDirectory(dir);
        var from = name.Split('/').Length > 2 ? Path.GetDirectoryName(source) : null;
        if (from is null)
        {
            File.Copy(source, Path.Combine(dir, Path.GetFileName(source)));
            return Path.Combine(dir, Path.GetFileName(source));
        }
        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(dir, Path.GetRelativePath(from, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
        return Path.Combine(dir, "main.byn");
    }

    public static TheoryData<string> LexCases => Golden.Cases("lex");
    public static TheoryData<string> ParseCases => Golden.Cases("parse");

    [Theory]
    [MemberData(nameof(LexCases))]
    public void LexAsCliApp(string name) => AssertSameAsCs("lex", Case(name));

    [Theory]
    [MemberData(nameof(ParseCases))]
    public void ParseAstAsCliApp(string name) => AssertSameAsCs("parse", "--ast", Case(name));

    public static TheoryData<string> TypedCases => Golden.Cases("typed");
    public static TheoryData<string> DiagCases => Golden.Cases("diag");

    [Theory]
    [MemberData(nameof(TypedCases))]
    public void TypesAsCliApp(string name) => AssertSameAsCs("types", Isolated(name));

    /// <summary>`check` prints `checked <name> in <n.nn>s` on success; <see cref="Mask"/> replaces the seconds on both sides.</summary>
    [Theory]
    [MemberData(nameof(DiagCases))]
    public void CheckAsCliApp(string name) => AssertSameAsCs("check", Isolated(name));

    [Fact]
    public void ACleanProgramAsCliApp()
    {
        var dir = Path.Combine(Golden.RepoRoot, "tmp", "t2", "clean");
        Directory.CreateDirectory(dir);
        var main = Path.Combine(dir, "main.byn");
        File.WriteAllText(main, "void main() {\n    print(\"hi\")\n}\n");
        AssertSameAsCs("check", main);
        AssertSameAsCs("check", main, "--diagnostics", "json");
        AssertSameAsCs("types", main);
        AssertSameAsCs("types", Isolated("diag/ambiguous-variant.byn"));
    }

    [Fact]
    public void ArgumentErrorsAsCliApp()
    {
        var file = Case("lex/bad-chars.byn");
        AssertSameAsCs("lex");
        AssertSameAsCs("lex", file, file);
        AssertSameAsCs("lex", Case("lex/missing.byn"));
        AssertSameAsCs("parse", "--ast");
        AssertSameAsCs("parse", "--ast", Case("parse/missing.byn"));
        AssertSameAsCs("parse", "--ast", "-");
        AssertSameAsCs("types");
        AssertSameAsCs("types", Case("typed/missing.byn"));
        AssertSameAsCs("types", file, "--bogus");
        AssertSameAsCs("check", Case("diag/missing.byn"));
        AssertSameAsCs("check", file, "--json");
        AssertSameAsCs("check", file, "--diagnostics", "json");
        AssertSameAsCs("check", file, "--all");
    }

    /// <summary>The cases of <c>run</c> that precede and surround the T2 commands (the dispatch the T2 stubs were replaced in).</summary>
    public static IEnumerable<object[]> Dispatch() => new[]
    {
        new string[] { },
        new[] { "version" },
        new[] { "--version" },
        new[] { "help" },
        new[] { "-h" },
        new[] { "--help" },
        new[] { "frobnicate" },
        new[] { "fmt" },
        new[] { "missing-script.byn" },
        new[] { "--color", "bad", "help" },
    }.Select(a => new object[] { a });

    [Theory]
    [MemberData(nameof(Dispatch))]
    public void DispatchAsCliApp(string[] args) => AssertSameAsCs(args);

    [Fact]
    public void RefusesTheOtherCommands()
    {
        foreach (var command in new[] { "doctor", "format", "audit", "features", "explain", "test", "mutate", "new", "dist", "cache", "clean", "init" })
        {
            var (exit, stdout, stderr) = SelfhostDriverHarness.Run(command);
            Assert.Equal(CliApp.ExitUsage, exit); // no-oracle: Q917 the C# runs these commands; the refusal text is R3's (Q4)
            Assert.Equal("", stdout);
            Assert.StartsWith("error: `", stderr);
        }
    }

    [Fact]
    public void LexReadsStandardInput()
    {
        var file = Case("lex/bad-chars.byn");
        var expected = Oracle(["lex", file]);
        var (exit, stdout, _, _, _) = SelfhostLexerTests.RunOnStdin(SelfhostDriverHarness.Build(), ["lex", "-"], File.ReadAllBytes(file), 30_000);
        Assert.Equal(expected.Exit, exit);
        SelfhostEmitterHarness.AssertSame("stdout of `lex -`", expected.Out, System.Text.Encoding.UTF8.GetString(stdout).ReplaceLineEndings("\n"));
    }

    [Fact]
    public void RefusesWhatItDoesNotPort()
    {
        var file = Case("lex/bad-chars.byn");
        foreach (var args in new[] { new[] { "lex", "--json", file }, new[] { "parse", file } })
        {
            var (exit, stdout, stderr) = SelfhostDriverHarness.Run(args);
            Assert.Equal(CliApp.ExitUsage, exit); // no-oracle: Q917, Q940 the C# runs these (lex --json, parse without --ast); the refusal text is R3's (Q4)
            Assert.Equal("", stdout);
            Assert.StartsWith("error: `", stderr);
        }
    }
}
