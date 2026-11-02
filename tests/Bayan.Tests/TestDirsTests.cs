namespace Bayan.Tests;

public sealed class TestDirsTests
{
    [Fact]
    public void DeleteRemovesATreeHoldingReadOnlyFiles()
    {
        var dir = Path.Combine(Golden.RepoRoot, "tmp", "test-dirs", "read-only");
        Directory.CreateDirectory(Path.Combine(dir, "objects", "ab"));
        var file = Path.Combine(dir, "objects", "ab", "cdef");
        File.WriteAllText(file, "x");
        File.SetAttributes(file, FileAttributes.ReadOnly);

        TestDirs.Delete(dir);

        Assert.False(Directory.Exists(dir));
    }

    [Fact]
    public void DeleteOfAMissingDirectoryDoesNothing()
    {
        var dir = Path.Combine(Golden.RepoRoot, "tmp", "test-dirs", "missing");
        TestDirs.Delete(dir);
        TestDirs.Delete(dir);
        Assert.False(Directory.Exists(dir));
    }
}
