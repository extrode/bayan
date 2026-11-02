using System.Diagnostics;
using System.Text.RegularExpressions;
using Xunit;

namespace Bayan.Tests;

/// <summary>`.zig-version` pins the zig every host and CI job uses: the workflows install it, and a host whose zig differs fails here.</summary>
public sealed class ZigVersionPinTests
{
    private static string Pinned => File.ReadAllText(Path.Combine(Golden.RepoRoot, ".zig-version")).Trim();

    [Fact]
    public void PinIsAVersionNumber() => Assert.Matches(@"^\d+\.\d+\.\d+$", Pinned);

    [Theory]
    [InlineData("ci.yml")]
    [InlineData("mutation.yml")]
    public void WorkflowsInstallThePinnedZig(string workflow)
    {
        var text = File.ReadAllText(Path.Combine(Golden.RepoRoot, ".github", "workflows", workflow));
        var versions = Regex.Matches(text, @"uses: mlugg/setup-zig@\S+\s+with:\s+version: (\S+)").Select(m => m.Groups[1].Value).ToList();
        Assert.NotEmpty(versions);
        Assert.All(versions, v => Assert.Equal(Pinned, v));
    }

    [Fact]
    public void HostZigIsThePinnedZig()
    {
        using var p = Process.Start(new ProcessStartInfo("zig", "version") { RedirectStandardOutput = true, RedirectStandardError = true })!;
        var version = p.StandardOutput.ReadToEnd().Trim();
        p.WaitForExit();
        Assert.True(Pinned == version, $"this host's zig is {version}, the repo pins {Pinned} (.zig-version); install {Pinned}");
    }
}
