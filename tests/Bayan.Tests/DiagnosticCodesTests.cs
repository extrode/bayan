using Bayan.Cli;
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
        var missing = DiagnosticCodes.All
            .Where(d => !expected.Any(text => text.Contains($" {d.Code}:", StringComparison.Ordinal)))
            .Select(d => d.Code)
            .ToList();
        Assert.True(missing.Count == 0, "no golden case for " + string.Join(", ", missing));
    }
}
