using System.Text.RegularExpressions;
using Bayan.Backend.C;
using Bayan.Cli;

namespace Bayan.Tests;

/// <summary><c>build</c>, <c>check</c> and <c>run</c> summaries and failure footers, with the seconds masked.</summary>
public sealed partial class CliEnvironmentTests
{
    [GeneratedRegex("\\d+\\.\\d\\ds")]
    private static partial Regex SecondsValue();

    private static string Masked(string text) => SecondsValue().Replace(text, "#s");

    private static string Cache => Path.Combine(Golden.RepoRoot, "tmp", "cache");

    /// <summary>A program in a fresh directory named <paramref name="name"/>, unique so its first build misses the cache.</summary>
    private static (string File, string Output) Unique(string name)
    {
        var dir = Fresh(name);
        var file = Path.Combine(dir, "main.byn");
        File.WriteAllText(file, $"void main() {{\n    print(\"{Guid.NewGuid():N}\")\n}}\n");
        return (file, Path.Combine(dir, Targets.ExecutableName("out", CliApp.HostTarget)));
    }

    private static string Failing(string name)
    {
        var file = Path.Combine(Fresh(name), "main.byn");
        File.WriteAllText(file, "void main() {\n    let x = 1\n    x = 2\n    x = 3\n    var a = 1\n    var b = 2\n    print($\"{a} {b}\")\n}\n");
        return file;
    }

    [Fact]
    public void BuildPrintsOneSummaryLineAndNotesACacheHit()
    {
        var (file, output) = Unique("build-ok");
        WithTerminalEnv(() =>
        {
            var first = Run(["build", "-o", output, file]);
            Assert.True(first.Exit == CliApp.ExitOk, first.Err);
            Assert.Equal("", first.Err);
            Assert.Equal($"◆ built build-ok (safe, {CliApp.HostTarget}) in #s\n", Masked(first.Out));
            var cached = Run(["build", "-o", output, file]);
            Assert.Equal($"◆ built build-ok (safe, {CliApp.HostTarget}) in #s · from cache\n", Masked(cached.Out));
            var ascii = Run(["--ascii", "build", "-o", output, file]).Out;
            Assert.Equal($"* built build-ok (safe, {CliApp.HostTarget}) in #s - from cache\n", Masked(ascii));
            Assert.DoesNotContain("·", ascii, StringComparison.Ordinal);
            var colored = Run(["build", "--color", "always", "-o", output, file]).Out;
            Assert.StartsWith("\u001b[32m◆ built build-ok", colored, StringComparison.Ordinal);
            Assert.EndsWith("from cache\u001b[0m\n", colored, StringComparison.Ordinal);
        }, ("BAYAN_CACHE", Cache));
    }

    [Fact]
    public void BuildVerbosePrintsEachPhaseThenTheOutput()
    {
        var (file, output) = Unique("build-verbose");
        WithTerminalEnv(() =>
        {
            var (exit, stdout, stderr) = Run(["build", "-v", "-o", output, file]);
            Assert.True(exit == CliApp.ExitOk, stderr);
            Assert.Equal(
                $"  parse      #s\n  check      #s\n  emit       #s\n  compile    #s\n  {output}\n◆ built build-verbose (safe, {CliApp.HostTarget}) in #s\n",
                Masked(stdout));
            Assert.Equal(
                $"  parse      #s\n  check      #s\n  emit       #s\n  compile    from cache\n  {output}\n◆ built build-verbose (safe, {CliApp.HostTarget}) in #s · from cache\n",
                Masked(Run(["build", "--verbose", "-o", output, file]).Out));
        }, ("BAYAN_CACHE", Cache));
    }

    [Fact]
    public void ABuildFailureEndsWithCountsAndAnExplainHint()
    {
        WithTerminalEnv(() =>
        {
            var (exit, stdout, stderr) = Run(["build", Broken()]);
            Assert.Equal(CliApp.ExitFailure, exit);
            Assert.Equal("", stdout);
            var code = Regex.Match(stderr, "^error (BYN\\d{4})").Groups[1].Value;
            Assert.EndsWith($"\n\n✗ build failed: 1 error, 0 warnings\n  explain: bayan explain {code}\n", stderr, StringComparison.Ordinal);
            var many = Run(["check", Failing("check-fail")]).Err;
            Assert.Contains("\n✗ build failed: 2 errors, 2 warnings\n", many, StringComparison.Ordinal);
            Assert.StartsWith("\u001b[31m✗ build failed", Run(["check", "--color", "always", Broken()]).Err.Split("\n\n")[^1], StringComparison.Ordinal);
            Assert.StartsWith("x build failed", Run(["--ascii", "check", Broken()]).Err.Split("\n\n")[^1], StringComparison.Ordinal);
        });
    }

    [Fact]
    public void CheckPrintsASummaryAndJsonModesPrintNone()
    {
        var (file, output) = Unique("check-ok");
        WithTerminalEnv(() =>
        {
            var (exit, stdout, stderr) = Run(["check", file]);
            Assert.Equal(CliApp.ExitOk, exit);
            Assert.Equal("", stderr);
            Assert.Equal("◆ checked check-ok in #s\n", Masked(stdout));
            Assert.Equal("", Run(["check", "--diagnostics", "json", file]).Out);
            Assert.Equal("", Run(["build", "--diagnostics", "json", "-o", output, file]).Out);
            var json = Run(["check", "--diagnostics", "json", Broken()]).Err;
            Assert.DoesNotContain("build failed", json, StringComparison.Ordinal);
            Assert.All(json.Split('\n', StringSplitOptions.RemoveEmptyEntries), line => Assert.StartsWith("{\"code\"", line, StringComparison.Ordinal));
        }, ("BAYAN_CACHE", Cache));
    }

    [Fact]
    public void RunPrintsOnlyTheFailureFooterOfItsOwn()
    {
        var (file, _) = Unique("run-ok");
        WithTerminalEnv(() =>
        {
            var failed = Run(["run", Broken()]);
            Assert.Equal(CliApp.ExitFailure, failed.Exit);
            Assert.Equal("", failed.Out);
            Assert.Matches("\n\n✗ build failed: 1 error, 0 warnings\n  explain: bayan explain BYN\\d{4}\n$", failed.Err);
            var ok = Run(["run", file]);
            Assert.True(ok.Exit == CliApp.ExitOk, ok.Err);
            Assert.Equal("", ok.Out + ok.Err);
        }, ("BAYAN_CACHE", Cache));
    }
}
