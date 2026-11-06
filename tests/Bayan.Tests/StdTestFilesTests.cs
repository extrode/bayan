using Bayan.Cli;

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
}
