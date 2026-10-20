using Bayan.Syntax.Diagnostics;
using Bayan.Syntax.Packages;

namespace Bayan.Tests;

/// <summary>
/// <c>tests/cases/manifest/*.byn</c> → <c>*.diag</c>: the manifest's diagnostics, or the fields
/// it read when there are none.
/// </summary>
public sealed class ManifestTests
{
    public static TheoryData<string> Cases => Golden.Cases("manifest");

    [Theory]
    [MemberData(nameof(Cases))]
    public void ManifestGolden(string name) =>
        Golden.Check(name, ".diag", path =>
        {
            var m = Manifest.Parse(Golden.Load(path));
            if (m.Diagnostics.Count > 0) return DiagnosticRenderer.RenderAll(m.Diagnostics);
            return $"name {m.Name}\nversion {m.Version}\nedition {m.Edition}\nkind {m.Kind}\n"
                + $"targets {string.Join(" ", m.DistTargets)}\nmode {m.DistMode}\n";
        });

    [Theory]
    [InlineData("tally", true)]
    [InlineData("my-app2", true)]
    [InlineData("My-app", false)]
    [InlineData("my--app", false)]
    [InlineData("my-app-", false)]
    [InlineData("2app", false)]
    [InlineData("my_app", false)]
    public void KebabNames(string name, bool valid) => Assert.Equal(valid, Manifest.IsKebabName(name));

    [Theory]
    [InlineData("0.1.0", true)]
    [InlineData("10.20.30-rc.1", true)]
    [InlineData("1.0", false)]
    [InlineData("01.0.0", false)]
    [InlineData("1.0.0-", false)]
    [InlineData("1.0.0-rc..1", false)]
    [InlineData("1.0.0-a/b", false)]
    [InlineData("1.0.0-a\\b", false)]
    [InlineData("1.0.0-x-y.2", true)]
    [InlineData("123456789012345678.0.0", true)]
    public void SemanticVersions(string version, bool valid) => Assert.Equal(valid, Manifest.IsSemanticVersion(version));

    /// <summary>A version names <c>dist/&lt;version&gt;/</c>, so it can't hold a path or overflow a <c>long</c>.</summary>
    [Theory]
    [InlineData("1.0.0-x/../..", false)]
    [InlineData("99999999999999999999.0.0", false)]
    [InlineData("1.0.0-rc.1", true)]
    public void ManifestVersions(string version, bool valid)
    {
        var text = $"Package {{ name = \"local/tally\", version = \"{version}\", edition = 2027, kind = .app }}";
        var m = Manifest.Parse(Bayan.Syntax.Text.SourceText.FromString("package.byn", text));
        if (valid)
        {
            Assert.Empty(m.Diagnostics);
            Assert.Equal(version, m.Version);
        }
        else Assert.Contains(m.Diagnostics, d => d.Message.Contains("`version` must be a semantic version string", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("0.2.0", "0.10.0", -1)]
    [InlineData("1.0.0", "1.0.0-rc.1", 1)]
    [InlineData("1.0.0-alpha", "1.0.0-beta", -1)]
    [InlineData("2.0.0", "2.0.0", 0)]
    public void VersionOrder(string a, string b, int sign) => Assert.Equal(sign, Math.Sign(Manifest.CompareVersions(a, b)));

    [Fact]
    public void NamesConvertBetweenKebabAndCamel()
    {
        Assert.Equal("macos-arm64", Manifest.KebabOf("macosArm64"));
        Assert.Equal("myLib", Manifest.CamelOf("my-lib"));
    }
}
