using System.Text.RegularExpressions;

namespace Bayan.Tests;

/// <summary>
/// The 010 cross-file skeleton: every entry point <c>docs/specs/010-self-hosted-semantics/010-api.md</c>
/// indexes is declared, with exactly that signature, in its owner file under <c>selfhost/semantics</c>.
/// </summary>
public sealed class SelfhostSemanticsApiTests
{
    private static readonly Regex Entry = new(@"^- (T\d+) (\S+\.byn): (.+?)  ← (\S+) (.+)$");

    [Fact]
    public void EveryIndexedEntryPointExists()
    {
        var index = Path.Combine(Golden.RepoRoot, "docs", "specs", "010-self-hosted-semantics", "010-api.md");
        var entries = File.ReadLines(index).Where(l => l.StartsWith("- T", StringComparison.Ordinal)).ToList();
        Assert.NotEmpty(entries);
        var files = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var missing = new List<string>();
        foreach (var line in entries)
        {
            var m = Entry.Match(line);
            Assert.True(m.Success, $"malformed index line: {line}");
            var file = m.Groups[2].Value;
            if (!files.TryGetValue(file, out var lines))
                files[file] = lines = File.ReadAllLines(Path.Combine(SelfhostSemanticsHarness.SemanticsDir, file));
            var signature = m.Groups[3].Value;
            if (!lines.Any(l => l.StartsWith(signature, StringComparison.Ordinal)
                    && (l.Length == signature.Length || l[signature.Length..].StartsWith(" =>", StringComparison.Ordinal)
                        || l[signature.Length..].StartsWith(" {", StringComparison.Ordinal))))
                missing.Add($"{m.Groups[1].Value} {file}: {signature}");
        }
        Assert.True(missing.Count == 0, $"not declared verbatim in the owner file:\n{string.Join('\n', missing)}");
    }
}
