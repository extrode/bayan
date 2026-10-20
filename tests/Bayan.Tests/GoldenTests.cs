namespace Bayan.Tests;

public sealed class GoldenTests : IDisposable
{
    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("bayan-golden-");

    public void Dispose() => _dir.Delete(recursive: true);

    private string PathOf(string name) => Path.Combine(_dir.FullName, name);

    [Fact]
    public void MatchingOutputPasses()
    {
        File.WriteAllText(PathOf("a.out"), "one\ntwo\n");
        Assert.Equal(GoldenResult.Match, Golden.Compare(PathOf("a.out"), "one\ntwo\n", update: false));
    }

    [Fact]
    public void CrlfInExpectedFileIsIgnored()
    {
        File.WriteAllText(PathOf("a.out"), "one\r\ntwo\r\n");
        Assert.Null(Golden.Compare(PathOf("a.out"), "one\ntwo\n", update: false).Failure);
    }

    [Fact]
    public void MismatchReportsFirstDifferingLine()
    {
        File.WriteAllText(PathOf("a.out"), "one\ntwo\n");
        var result = Golden.Compare(PathOf("a.out"), "one\nTWO\n", update: false);
        Assert.NotNull(result.Failure);
        Assert.Contains("first difference at line 2", result.Failure);
        Assert.Contains("expected: two", result.Failure);
        Assert.Contains("actual:   TWO", result.Failure);
    }

    [Fact]
    public void MissingExpectedFileFails()
    {
        var result = Golden.Compare(PathOf("missing.out"), "x", update: false);
        Assert.Contains("BAYAN_UPDATE=1", result.Failure);
        Assert.False(File.Exists(PathOf("missing.out")));
    }

    [Fact]
    public void UpdateModeWritesExpectedFile()
    {
        File.WriteAllText(PathOf("a.out"), "old\n");
        var result = Golden.Compare(PathOf("a.out"), "new\n", update: true);
        Assert.True(result.WasUpdated);
        Assert.Equal("new\n", File.ReadAllText(PathOf("a.out")));
    }

    [Fact]
    public void UpdateModeLeavesMatchingFileAlone()
    {
        File.WriteAllText(PathOf("a.out"), "same\n");
        Assert.Equal(GoldenResult.Match, Golden.Compare(PathOf("a.out"), "same\n", update: true));
    }

    [Fact]
    public void RepoRootContainsSolution()
    {
        Assert.True(File.Exists(Path.Combine(Golden.RepoRoot, "Bayan.slnx")));
    }
}
