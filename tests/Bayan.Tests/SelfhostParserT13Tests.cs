using Xunit.Abstractions;

namespace Bayan.Tests;

/// <summary>Focused checks of <c>bayan-parse</c>'s CLI modes (T13), independent of the parsing tasks.</summary>
public sealed class SelfhostParserT13Tests(ITestOutputHelper output)
{
    [Fact]
    public void MissingPathExitsFour()
    {
        var (exit, stdout, stderr) = SelfhostParserTests.Run(SelfhostParserTests.Built, Path.Combine(Golden.RepoRoot, "tmp", "no-such-file.byn"));
        Assert.Equal(4, exit);
        Assert.Empty(stdout);
        Assert.NotEmpty(stderr);
    }

    [Fact]
    public void UnknownOptionExitsTwo()
    {
        var (exit, stdout, stderr) = SelfhostParserTests.Run(SelfhostParserTests.Built, "--bogus");
        Assert.Equal(2, exit);
        Assert.Empty(stdout);
        Assert.Contains("usage:", stderr);
    }

    [Fact]
    public void ManyPrintsHeadersInNameOrderAndSkipsNonByn()
    {
        var dir = Path.Combine(Golden.RepoRoot, "tmp", "selfhost-parser-t13", "many");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "b.byn"), "let b = 1;");
        File.WriteAllText(Path.Combine(dir, "a.byn"), "let a = 1;");
        File.WriteAllText(Path.Combine(dir, "c.txt"), "not a Bayan file");
        var (exit, stdout, stderr) = SelfhostParserTests.Run(SelfhostParserTests.Built, "--many", dir);
        output.WriteLine(stderr);
        Assert.Equal(0, exit);
        var a = stdout.IndexOf("== a.byn\n", StringComparison.Ordinal);
        var b = stdout.IndexOf("== b.byn\n", StringComparison.Ordinal);
        Assert.True(a >= 0 && b > a, stdout);
        Assert.DoesNotContain("c.txt", stdout);
    }

    [Fact]
    public void ManyOnAMissingDirectoryExitsTwo()
    {
        var (exit, stdout, stderr) = SelfhostParserTests.Run(SelfhostParserTests.Built, "--many", Path.Combine(Golden.RepoRoot, "tmp", "no-such-dir"));
        Assert.Equal(2, exit);
        Assert.Empty(stdout);
        Assert.NotEmpty(stderr);
    }

    [Fact]
    [Trait("Snapshot", "skip")]
    public void TimePrintsANumberAndExitsZero()
    {
        var path = Path.Combine(Golden.RepoRoot, "tmp", "selfhost-parser-t13", "time.byn");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "let x = 1;");
        var (exit, stdout, stderr) = SelfhostParserTests.Run(SelfhostParserTests.Built, "--time", path);
        output.WriteLine(stderr);
        Assert.Equal(0, exit);
        Assert.True(long.TryParse(stdout.Trim(), out _), $"not a number: {stdout}");
    }
}
