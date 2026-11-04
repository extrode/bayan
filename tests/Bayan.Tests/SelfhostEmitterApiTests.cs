using System.Text.RegularExpressions;

namespace Bayan.Tests;

/// <summary>
/// The 011 cross-file skeleton: every entry point <c>docs/specs/011-self-hosted-emitter/011-api.md</c> indexes is
/// declared, with exactly that signature, in its file under <c>selfhost/</c>.
/// </summary>
public sealed class SelfhostEmitterApiTests
{
    private static readonly Regex Entry = new(@"^- (T\d+) (\S+\.byn): (.+?)  ← (\S+) (.+)$");

    [Fact]
    public void EveryIndexedEntryPointExists()
    {
        var index = Path.Combine(Golden.RepoRoot, "docs", "specs", "011-self-hosted-emitter", "011-api.md");
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
                files[file] = lines = File.ReadAllLines(Path.Combine(SelfhostSemanticsHarness.SelfhostDir, file));
            var signature = m.Groups[3].Value;
            if (!lines.Any(l => SelfhostSemanticsApiTests.SignatureStarts(l.StartsWith("public ", StringComparison.Ordinal) ? l["public ".Length..] : l, signature)))
                missing.Add($"{m.Groups[1].Value} {file}: {signature}");
        }
        Assert.True(missing.Count == 0, $"not declared verbatim in its file:\n{string.Join('\n', missing)}");
    }

    [Fact]
    public void IndexCarriesTheRoundFourTableAndTheMarker()
    {
        var index = File.ReadAllText(Path.Combine(Golden.RepoRoot, "docs", "specs", "011-self-hosted-emitter", "011-api.md"));
        Assert.Contains("`LoweredNodes.cs:118`", index);
        Assert.Contains("`CEmitter.cs:1937`, `1942-1945`, `1961`", index);
        var tree = File.ReadAllText(Path.Combine(SelfhostEmitterHarness.EmitterDir, "ltree", "ltree.byn"));
        Assert.Matches(@"// Q1: round 4 redoes this[^\n]*\n\s*public LocalId env", tree);
    }
}
