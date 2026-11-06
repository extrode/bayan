namespace Bayan.Tests;

public sealed class TempSweepTests
{
    [Fact]
    public void SweepRemovesOldDirectoriesWithTestPrefixesAndKeepsTheRest()
    {
        var root = Directory.CreateTempSubdirectory("bayan-sweep-test-").FullName;
        try
        {
            var old = Directory.CreateDirectory(Path.Combine(root, "bayan-t8-old")).FullName;
            File.WriteAllText(Path.Combine(old, "f.txt"), "x");
            Directory.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddDays(-2));
            var oldOther = Directory.CreateDirectory(Path.Combine(root, "unrelated-old")).FullName;
            Directory.SetLastWriteTimeUtc(oldOther, DateTime.UtcNow.AddDays(-2));
            var fresh = Directory.CreateDirectory(Path.Combine(root, "bayan-t8-fresh")).FullName;

            TestSetup.SweepStaleTempDirs(root, TimeSpan.FromHours(24));

            Assert.False(Directory.Exists(old));
            Assert.True(Directory.Exists(fresh));
            Assert.True(Directory.Exists(oldOther));
        }
        finally { TestDirs.Delete(root); }
    }
}
