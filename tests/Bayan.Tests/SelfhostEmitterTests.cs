namespace Bayan.Tests;

/// <summary>011 T1 smoke: <c>selfhost/emitter</c> builds with its nested imports and the CLI shell runs.</summary>
public sealed class SelfhostEmitterTests
{
    [Fact]
    public void Builds() => Assert.True(File.Exists(SelfhostEmitterHarness.Build()));

    [Fact]
    public void MainExitsZero()
    {
        var dir = Path.Combine(Path.GetTempPath(), "bayan-emit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "a.byn");
            File.WriteAllText(path, "i32 main() => 0\n");
            var (exit, _, err) = SelfhostEmitterHarness.Run(path);
            Assert.True(exit == 0, err);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void BadModeIsUsage()
    {
        var (exit, _, err) = SelfhostEmitterHarness.Run("--mode", "turbo", "x.byn");
        Assert.Equal(2, exit);
        Assert.Contains("bayan: unknown mode `turbo`", err);
    }
}
