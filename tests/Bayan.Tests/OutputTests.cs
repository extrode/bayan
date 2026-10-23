using System.Text.RegularExpressions;
using Bayan.Backend.C;
using Bayan.Cli;

namespace Bayan.Tests;

public sealed partial class OutputTests
{
    private static (int Exit, string Out, string Err) Run(params string[] args)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var exit = CliApp.Run(args, stdout, stderr);
        return (exit, stdout.ToString().ReplaceLineEndings("\n"), stderr.ToString().ReplaceLineEndings("\n"));
    }

    /// <summary>A new app (or library) `tally` in a fresh directory under <c>tmp/</c>.</summary>
    private static string NewProject(string name, params string[] options)
    {
        Environment.SetEnvironmentVariable("BAYAN_CACHE", Path.Combine(Golden.RepoRoot, "tmp", "cache"));
        var dir = Path.Combine(Golden.RepoRoot, "tmp", "output", name);
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        Directory.CreateDirectory(dir);
        var project = Path.Combine(dir, "tally");
        var (exit, _, err) = Run(["new", project, "--vcs", "none", .. options]);
        Assert.True(exit == CliApp.ExitOk, err);
        return project;
    }

    private static void SetVersion(string project, string version)
    {
        var path = Path.Combine(project, "package.byn");
        File.WriteAllText(path, VersionField().Replace(File.ReadAllText(path), $"version: \"{version}\""));
    }

    private static void Ok((int Exit, string Out, string Err) r) => Assert.True(r.Exit == CliApp.ExitOk, r.Out + r.Err);

    /// <summary>Paths under <paramref name="dir"/>; text files with their contents, timings and hashes masked.</summary>
    private static string Tree(string dir, bool maskHost = true)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
                                      .Select(f => Path.GetRelativePath(dir, f).Replace('\\', '/'))
                                      .Order(StringComparer.Ordinal))
        {
            sb.Append("=== ").Append(maskHost ? file.Replace(Targets.Host!, "<host>") : file).Append('\n');
            if (Path.GetFileName(file) is "results.json" or "SHA256SUMS" or "BUILDINFO.json" or "VERSION")
                sb.Append(Mask(File.ReadAllText(Path.Combine(dir, file)), maskHost));
        }
        return sb.ToString();
    }

    private static string Mask(string text, bool maskHost = true) =>
        Sha().Replace(Duration().Replace(maskHost ? text.Replace(Targets.Host!, "<host>") : text, "\"durationMs\": 0"), "<sha256>  ");

    private static void Compare(string name, string actual)
    {
        var result = Golden.Compare(Path.Combine(Golden.CasesRoot, "out", name), actual, Golden.UpdateMode);
        if (result.Failure is { } failure) Assert.Fail(failure);
    }

    [Fact]
    public void BuildRunAndTestWriteToOut()
    {
        var p = NewProject("out-layout");
        Ok(Run("build", p));
        Ok(Run("run", p));
        Ok(Run("test", p));
        Compare("out.tree", Tree(Path.Combine(p, "out")));
        Assert.Equal(File.ReadAllText(Path.Combine(p, "out", "safe", Targets.Host!, "c", "bayan-rt.h")), Toolchain.RuntimeHeader);
    }

    [Fact]
    public void ModeAndTargetPickTheOutDirectory()
    {
        var p = NewProject("out-mode-target");
        Ok(Run("build", p, "--mode", "debug"));
        Ok(Run("build", p, "--target", "linux-x64"));
        Ok(Run("build", p, "--target", "windows-x64"));
        Assert.True(File.Exists(Path.Combine(p, "out", "debug", Targets.Host!, "tally")));
        Assert.True(File.Exists(Path.Combine(p, "out", "safe", "linux-x64", "tally")));
        Assert.True(File.Exists(Path.Combine(p, "out", "safe", "windows-x64", "tally.exe")));
    }

    [Fact]
    public void AnUnknownTargetIsAUsageError()
    {
        var p = NewProject("out-unknown-target");
        var (exit, _, err) = Run("build", p, "--target", "plan9-mips");
        Assert.Equal(CliApp.ExitUsage, exit);
        Assert.Contains("unknown target `plan9-mips`", err);
    }

    [Fact]
    public void TestJsonPrintsTheResultsFile()
    {
        var p = NewProject("out-test-json");
        var (exit, stdout, _) = Run("test", p, "--json");
        Assert.Equal(CliApp.ExitOk, exit);
        Assert.Equal(File.ReadAllText(Path.Combine(p, "out", "test", "results.json")), stdout);
        Assert.Contains("\"status\": \"pass\"", stdout);
        Assert.DoesNotContain("running", stdout);
    }

    [Fact]
    public void FailingTestsAreReportedInTheResultsFile()
    {
        var p = NewProject("out-test-fail");
        File.WriteAllText(Path.Combine(p, "tests", "broken.test.byn"), "test \"wrong\" {\n    assert(1 == 2)\n}\n\ntest \"boom\" {\n    panic(\"boom\")\n}\n");
        Assert.Equal(CliApp.ExitTestFailure, Run("test", p).Exit);
        Compare("results-fail.json", Mask(File.ReadAllText(Path.Combine(p, "out", "test", "results.json"))));
    }

    [Fact]
    public void DistWritesAVersionDirectoryAndLatest()
    {
        var p = NewProject("dist-layout");
        var manifest = Path.Combine(p, "package.byn");
        File.WriteAllText(manifest, File.ReadAllText(manifest).Replace("[.host]", "[.linuxX64, .macosArm64, .windowsX64]"));
        var (exit, stdout, err) = Run("dist", p);
        Assert.True(exit == CliApp.ExitOk, err);
        Assert.Equal("built local/tally 0.1.0 (safe) for linux-x64, macos-arm64, windows-x64 in dist/0.1.0/\ndist/latest/ is 0.1.0\n", stdout);
        Compare("dist.tree", Tree(Path.Combine(p, "dist"), maskHost: false));
        var sums = File.ReadAllLines(Path.Combine(p, "dist", "0.1.0", "SHA256SUMS"));
        foreach (var line in sums)
        {
            var file = line[66..];
            Assert.Equal(Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(Path.Combine(p, "dist", "0.1.0", file)))), line[..64]);
        }
    }

    [Fact]
    public void AVersionDirectoryIsImmutableWithoutForce()
    {
        var p = NewProject("dist-immutable");
        Ok(Run("dist", p));
        var marker = Path.Combine(p, "dist", "0.1.0", "marker");
        File.WriteAllText(marker, "");
        var (exit, _, err) = Run("dist", p);
        Assert.Equal(CliApp.ExitUsage, exit);
        Assert.Contains("`dist/0.1.0/` already exists", err);
        Assert.Contains("bump `version`", err);
        Assert.True(File.Exists(marker));
        Ok(Run("dist", p, "--force"));
        Assert.False(File.Exists(marker));
    }

    [Fact]
    public void LatestIsTheHighestVersionNotTheNewestBuild()
    {
        var p = NewProject("dist-latest");
        var dist = Path.Combine(p, "dist");
        string Latest() => File.ReadAllText(Path.Combine(dist, "latest", "VERSION"));
        foreach (var (version, expected) in new[] { ("0.1.0", "0.1.0"), ("0.2.0-rc.1", "0.2.0-rc.1"), ("0.0.9", "0.2.0-rc.1"), ("0.2.0", "0.2.0"), ("0.10.0", "0.10.0") })
        {
            SetVersion(p, version);
            Ok(Run("dist", p));
            Assert.Equal(expected + "\n", Latest());
        }
        Assert.Equal(File.ReadAllText(Path.Combine(dist, "0.10.0", "BUILDINFO.json")), File.ReadAllText(Path.Combine(dist, "latest", "BUILDINFO.json")));
        Assert.Equal(["0.0.9", "0.1.0", "0.10.0", "0.2.0", "0.2.0-rc.1", "latest"], Directory.EnumerateDirectories(dist).Select(Path.GetFileName).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void ALibraryHasNoDistYet()
    {
        var p = NewProject("dist-lib", "--lib");
        var (exit, _, err) = Run("dist", p);
        Assert.Equal(CliApp.ExitUsage, exit);
        Assert.Contains("library distribution arrives later", err);
        Assert.False(Directory.Exists(Path.Combine(p, "dist")));
    }

    [Fact]
    public void AScriptHasNoDist()
    {
        var dir = Path.GetDirectoryName(NewProject("dist-script"))!;
        var (exit, _, err) = Run("dist", Path.Combine(dir, "tally", "src", "app", "main.byn"));
        Assert.Equal(CliApp.ExitUsage, exit);
        Assert.Contains("have no dist", err);
    }

    [Fact]
    public void ADirtyGitTreeNeedsAllowDirty()
    {
        if (Toolchain.FindOnPath("git") is not { } git) return;
        var p = NewProject("dist-dirty");
        Assert.Equal(0, Scaffold.Git(git, p, "init", "--quiet").Code);
        var (exit, _, err) = Run("dist", p);
        Assert.Equal(CliApp.ExitUsage, exit);
        Assert.Contains("--allow-dirty", err);
        Assert.False(Directory.Exists(Path.Combine(p, "dist")));
        Ok(Run("dist", p, "--allow-dirty"));
        Assert.Contains("\"source\": {\n    \"commit\": null,\n    \"dirty\": true\n  }", File.ReadAllText(Path.Combine(p, "dist", "0.1.0", "BUILDINFO.json")).ReplaceLineEndings("\n"));
    }

    [Fact]
    public void TheInputsHashCoversSourcesOnly()
    {
        var p = NewProject("dist-inputs");
        var before = Dist.InputsHash(p);
        Ok(Run("build", p));
        File.WriteAllText(Path.Combine(p, "README.md"), "changed");
        Assert.Equal(before, Dist.InputsHash(p));
        File.AppendAllText(Path.Combine(p, "src", "app", "main.byn"), "\n");
        Assert.NotEqual(before, Dist.InputsHash(p));
    }

    [Fact]
    public void CleanRemovesOutAndOnRequestDist()
    {
        var p = NewProject("clean");
        Ok(Run("build", p));
        Ok(Run("dist", p));
        var (exit, stdout, _) = Run("clean", p);
        Assert.Equal(CliApp.ExitOk, exit);
        Assert.Equal("removed out/\n", stdout);
        Assert.False(Directory.Exists(Path.Combine(p, "out")));
        Assert.True(Directory.Exists(Path.Combine(p, "dist")));
        Assert.Equal("removed dist/\n", Run("clean", p, "--dist").Out);
        Assert.False(Directory.Exists(Path.Combine(p, "dist")));
        Assert.True(File.Exists(Path.Combine(p, "package.byn")));
        Assert.Equal(CliApp.ExitUsage, Run("clean", Path.Combine(p, "src")).Exit);
    }

    [GeneratedRegex("version: \"[^\"]*\"")]
    private static partial Regex VersionField();

    [GeneratedRegex("\"durationMs\": [0-9.]+")]
    private static partial Regex Duration();

    [GeneratedRegex("^[0-9a-f]{64}  ", RegexOptions.Multiline)]
    private static partial Regex Sha();
}
