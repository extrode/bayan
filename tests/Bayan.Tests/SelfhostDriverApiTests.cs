using System.Text.RegularExpressions;
using Bayan.Cli;

namespace Bayan.Tests;

/// <summary>
/// The 012 cross-file skeleton: every entry point <c>docs/specs/012-self-hosted-bootstrap/012-api.md</c> indexes is
/// declared, with exactly that signature, in its owner file under <c>selfhost/</c>.
/// </summary>
public sealed class SelfhostDriverApiTests
{
    private static readonly Regex Entry = new(@"^- (T\d+) (\S+\.byn): (.+?)  ← (\S+) (.+)$");

    [Fact]
    public void EveryIndexedEntryPointExists()
    {
        var index = Path.Combine(Golden.RepoRoot, "docs", "specs", "012-self-hosted-bootstrap", "012-api.md");
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
    public void VersionConstantIsTheCompilersVersion()
    {
        var main = File.ReadAllText(Path.Combine(SelfhostDriverHarness.DriverDir, "main.byn"));
        var version = Regex.Match(main, @"^const version: string = ""([^""]+)""$", RegexOptions.Multiline).Groups[1].Value;
        var stdout = new StringWriter();
        Assert.Equal(CliApp.ExitOk, CliApp.Run(["--version"], stdout, new StringWriter()));
        Assert.StartsWith($"bayan {version} (", stdout.ToString());
    }
}
