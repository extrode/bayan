using Bayan.Cli;
using Bayan.Semantics;
using Bayan.Syntax.Diagnostics;

namespace Bayan.Tests;

public sealed class DiagnosticCodesTests
{
    [Fact]
    public void CodesAreUniqueAndWellFormed()
    {
        var codes = DiagnosticCodes.All.Select(d => d.Code).ToList();
        Assert.Equal(codes.Count, codes.Distinct().Count());
        Assert.All(codes, c => Assert.Matches("^BYN[0-9]{4}$", c));
    }

    [Fact]
    public void EveryCodeHasAnEmbeddedExplanation()
    {
        foreach (var d in DiagnosticCodes.All)
        {
            var text = CliApp.ExplainText(d.Code);
            Assert.True(text is not null, $"{d.Code} has no docs/diagnostics/{d.Code}.md");
            Assert.StartsWith($"# {d.Code}:", text);
        }
    }

    [Fact]
    public void EveryDocBelongsToACode()
    {
        var known = DiagnosticCodes.All.Select(d => d.Code).ToHashSet();
        foreach (var file in Directory.GetFiles(Path.Combine(Golden.RepoRoot, "docs", "diagnostics"), "*.md"))
            Assert.Contains(Path.GetFileNameWithoutExtension(file), known);
    }

    [Fact]
    public void EveryCodeAppearsInAGoldenCase()
    {
        var expected = Directory.EnumerateFiles(Golden.CasesRoot, "*", SearchOption.AllDirectories)
            .Where(f => !f.EndsWith(".byn", StringComparison.Ordinal) && !f.EndsWith(".md", StringComparison.Ordinal))
            .Select(File.ReadAllText)
            .ToList();
        // A retired code keeps its descriptor and docs page but can no longer appear in output; a reserved one cannot yet.
        var missing = DiagnosticCodes.All
            .Where(d => !CliApp.ExplainText(d.Code)!.Contains("No longer reported", StringComparison.Ordinal))
            .Where(d => !CliApp.ExplainText(d.Code)!.Contains("Not reported yet", StringComparison.Ordinal))
            .Where(d => !expected.Any(text => text.Contains($" {d.Code}:", StringComparison.Ordinal)))
            .Select(d => d.Code)
            .ToList();
        Assert.True(missing.Count == 0, "no golden case for " + string.Join(", ", missing));
    }

    [Fact]
    public void NoLiveCodeReusesAReservedNumber()
    {
        var lines = File.ReadAllLines(Path.Combine(Golden.RepoRoot, "src", "Bayan.Syntax", "Diagnostics", "DiagnosticCodes.cs"))
            .Select(l => l.Trim()).ToList();
        var start = lines.FindIndex(l => l.StartsWith("// Reserved: round 4", StringComparison.Ordinal));
        var end = lines.FindIndex(l => l == "// End reserved: round 4.");
        Assert.True(start >= 0 && end > start, "no `// Reserved: round 4` block");
        var entries = lines.Skip(start + 1).Take(end - start - 1).Where(l => l.StartsWith("// BYN", StringComparison.Ordinal)).ToList();
        Assert.NotEmpty(entries);
        Assert.All(entries, l => Assert.Matches("^// BYN[0-9]{4} .*// target: \\S", l));
        var reserved = entries.Select(l => l[3..10]).ToList();
        Assert.Equal(reserved.Count, reserved.Distinct().Count());
        Assert.Empty(DiagnosticCodes.All.Select(d => d.Code).Intersect(reserved));
    }

    // Docs whose example cannot be compiled standalone, each with the reason.
    private static readonly Dictionary<string, string> UncompilableExamples = new()
    {
        ["BYN0001"] = "needs a literal bidi control character, which the doc spells as <U+202E>",
        ["BYN0005"] = "needs a decomposed character that a doc code block does not preserve",
        ["BYN0015"] = "needs invalid UTF-8 bytes; a doc code block is text",
        ["BYN0016"] = "needs 17 nested interpolated strings; the doc has no failing example",
        ["BYN1030"] = "retired: the C# primitive spellings are accepted now",
        ["BYN1032"] = "retired: `if !x` is accepted",
        ["BYN1049"] = "retired: merged into BYN1007",
        ["BYN2005"] = "multi-file: needs a `money` module",
        ["BYN2006"] = "stale: the example now reports BYN5004 and BYN5006",
        ["BYN2010"] = "multi-file: two modules importing each other",
        ["BYN2011"] = "circular alias example reports no diagnostic standalone",
        ["BYN2015"] = "multi-file: the module's file is imported elsewhere",
        ["BYN2026"] = "retired: the example now reports BYN1017 and BYN1018",
        ["BYN2113"] = "superseded by BYN2761",
        ["BYN2116"] = "not reported yet: operator declarations are spec-only",
        ["BYN2224"] = "no longer reported since M14",
        ["BYN2307"] = "retired code",
        ["BYN2322"] = "retired: `requires` is checked at construction",
        ["BYN2707"] = "no longer reported since M21: now BYN2761",
        ["BYN2762"] = "retired: replaced by BYN2780",
        ["BYN2787"] = "the `trust(x, reason:)` form is now BYN1062",
        ["BYN3100"] = "project-level: the example is a package.byn manifest",
        ["BYN3101"] = "project-level: the example is a package.byn manifest",
        ["BYN3102"] = "project-level: the example is a package.byn manifest",
        ["BYN3103"] = "project-level: the example is a package.byn manifest",
        ["BYN3104"] = "project-level: the example is a package.byn manifest",
        ["BYN3201"] = "a stray `-2` line is BYN1012 now; BYN3201 needs an unused call value",
        ["BYN3213"] = "not reported yet: reserved code with no example",
        ["BYN3215"] = "not reported yet: reserved code with no example",
        ["BYN3217"] = "not reported yet: reserved code with no example",
        ["BYN4001"] = "backend error from the C compiler, not reachable from source",
        ["BYN6004"] = "project-level: needs a package.byn and a second file",
    };

    [Fact]
    public void EveryDocExampleReportsItsOwnCode()
    {
        var docs = Directory.GetFiles(Path.Combine(Golden.RepoRoot, "docs", "diagnostics"), "*.md");
        Assert.True(UncompilableExamples.Count * 4 < docs.Length, "the skip list is over 25% of the docs");
        var failures = new List<string>();
        foreach (var file in docs)
        {
            var code = Path.GetFileNameWithoutExtension(file);
            if (UncompilableExamples.ContainsKey(code)) continue;
            var lines = File.ReadAllLines(file);
            var heading = Array.FindIndex(lines, l => l.StartsWith("**Example", StringComparison.Ordinal));
            var open = heading < 0 ? -1 : Array.FindIndex(lines, heading, l => l.StartsWith("```", StringComparison.Ordinal));
            var close = open < 0 ? -1 : Array.FindIndex(lines, open + 1, l => l.StartsWith("```", StringComparison.Ordinal));
            if (close < 0) { failures.Add($"{code}: no example block"); continue; }
            var source = string.Join("\n", lines[(open + 1)..close]) + "\n";
            var c = Compilation.Create([new Bayan.Syntax.Text.SourceText(code + ".byn", System.Text.Encoding.UTF8.GetBytes(source))], Golden.RepoRoot, new CompilationOptions());
            var reported = c.Diagnostics.Items.Select(d => d.Code).Distinct().ToList();
            if (!reported.Contains(code)) failures.Add($"{code}: reported [{string.Join(", ", reported)}]");
            else if (code != "BYN1040" && reported.Contains("BYN1040")) failures.Add($"{code}: example uses `;`");
        }
        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }
}
