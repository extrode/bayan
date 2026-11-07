using System.Globalization;
using Bayan.Cli;
using Bayan.Lowering;
using Bayan.Semantics;

namespace Bayan.Tests;

/// <summary>016 T4: the <c>*.test.byn</c> files beside each <c>lib/std/</c> module pass under <c>bayan test</c>.</summary>
public sealed class StdTestFilesTests
{
    [Theory]
    [InlineData("json")]
    public void ModuleTestsPass(string module)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var exit = CliApp.Run(["test", "--ascii", Path.Combine(Golden.RepoRoot, "lib", "std", module)], stdout, stderr);
        Assert.True(exit == CliApp.ExitOk, $"{stdout}\n{stderr}");
        Assert.Matches(@"\* [1-9][0-9]* passed - [0-9.]+s", stdout.ToString());
    }

    /// <summary>The same tests built through the selfhost harness, each run as <c>bayan test</c> runs it, so a
    /// <c>BAYAN_COVERAGE</c> run (the guard's coverage of a <c>lib/std/</c> change) counts the module's own source.</summary>
    [Theory]
    [InlineData("json")]
    public void ModuleTestsPassInAHarnessBuild(string module)
    {
        if (OperatingSystem.IsWindows()) return;
        var c = Compilation.FromPath(Path.Combine(Golden.RepoRoot, "lib", "std", module), new CompilationOptions(IncludeTests: true));
        Assert.False(c.Diagnostics.HasErrors, string.Join("\n", c.Diagnostics.Items.Select(d => d.Message)));
        var tests = Lowerer.Lower(c).Tests.Count;
        Assert.True(tests > 0, $"lib/std/{module} has no tests");
        var build = SelfhostSemanticsHarness.BuildFast(c, new HashSet<string>(StringComparer.Ordinal));
        Assert.True(build.Succeeded, build.Errors);
        for (var i = 0; i < tests; i++)
        {
            var (code, _, err) = SelfhostParserTests.Run(build.ExecutablePath!, "--test", i.ToString(CultureInfo.InvariantCulture));
            Assert.True(code == 0, $"test {i} exited {code}:\n{err}");
        }
    }
}
