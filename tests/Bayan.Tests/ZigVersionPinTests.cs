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
        Assert.Contains("bash scripts/ci/install-zig.sh", text);
        Assert.DoesNotContain("setup-zig", text);
    }

    [Fact]
    public void InstallScriptHoldsThePinnedSums()
    {
        var script = File.ReadAllText(Path.Combine(Golden.RepoRoot, "scripts", "ci", "install-zig.sh"));
        Assert.Equal(Pinned, Regex.Match(script, @"(?m)^sums_for=(\S+)").Groups[1].Value);
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
