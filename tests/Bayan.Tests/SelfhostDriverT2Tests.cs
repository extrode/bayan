using System.Text.RegularExpressions;
using Bayan.Cli;

namespace Bayan.Tests;

/// <summary>
/// 012 T2: <c>lex</c> and <c>parse --ast</c> of the driver against <see cref="CliApp.Run"/> (R2).
/// The expected exit code and streams come from the C# run in-process; only the elapsed seconds of <c>check</c>'s summary are masked.
/// No style flag is passed: the dumps and diagnostics render plain in the driver (Q941).
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

    public static TheoryData<string> LexCases => Golden.Cases("lex");
    public static TheoryData<string> ParseCases => Golden.Cases("parse");

    [Theory]
    [MemberData(nameof(LexCases))]
    public void LexAsCliApp(string name) => AssertSameAsCs("lex", Case(name));

    [Theory]
    [MemberData(nameof(ParseCases))]
    public void ParseAstAsCliApp(string name) => AssertSameAsCs("parse", "--ast", Case(name));

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
        var script = Path.Combine(SelfhostDriverHarness.DriverDir, "main.byn");
        foreach (var command in new[] { "build", "run", "doctor", "format", "audit", "features", "explain", "test", "mutate", "new", "dist", "cache", "clean", "init", script })
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
        foreach (var args in new[] { new[] { "lex", "--json", file }, new[] { "parse", file }, new[] { "types", file }, new[] { "check", file } })
        {
            var (exit, stdout, stderr) = SelfhostDriverHarness.Run(args);
            Assert.Equal(CliApp.ExitUsage, exit); // no-oracle: Q940, Q944 the C# runs these (lex --json, parse without --ast, types, check); the refusal text is R3's (Q4)
            Assert.Equal("", stdout);
            Assert.StartsWith("error: `", stderr);
        }
    }
}
