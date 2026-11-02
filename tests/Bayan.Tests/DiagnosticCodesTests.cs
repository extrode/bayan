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
}
