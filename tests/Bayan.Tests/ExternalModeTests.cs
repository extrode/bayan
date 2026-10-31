using Bayan.Cli;

namespace Bayan.Tests;

/// <summary>Spec 013 T8: <c>bayan mutate --binary</c> runs a command once per site with <c>BAYAN_MUTANT</c> set.</summary>
public sealed class ExternalModeTests
{
    private const string Sites = """
        { "version": 1, "sites": [
          { "id": 1, "file": "a.byn", "line": 1, "column": 1, "operator": "arithmetic", "original": "a + b", "mutated": "a - b" },
          { "id": 2, "file": "a.byn", "line": 2, "column": 1, "operator": "arithmetic", "original": "a * b", "mutated": "a / b" },
          { "id": 3, "file": "a.byn", "line": 3, "column": 1, "operator": "bool", "original": "true", "mutated": "false", "emitted": false },
          { "id": 4, "file": "a.byn", "line": 4, "column": 1, "operator": "arithmetic", "original": "a + 0", "mutated": "a - 0", "unobservable": "no test can see it" },
          { "id": 5, "file": "a.byn", "line": 5, "column": 1, "operator": "statement", "original": "i += 1", "mutated": "" }
        ] }
        """;

    private static (int Exit, string Out, string Err) Mutate(string command, string sites = Sites)
    {
        var dir = Path.Combine(Golden.RepoRoot, "tmp", "external-mode");
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        Directory.CreateDirectory(dir);
        var binary = Path.Combine(dir, "prog");
        File.WriteAllText(binary, "");
        File.WriteAllText(binary + ".sites.json", sites);
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var exit = CliApp.Run(["mutate", "--binary", binary, "--sites", binary + ".sites.json", "--", "/bin/sh", "-c", command], stdout, stderr);
        return (exit, stdout.ToString(), stderr.ToString());
    }

    [Fact]
    public void ExitCodesAndTimeoutsDecideTheStatus()
    {
        if (OperatingSystem.IsWindows()) return;
        var (exit, output, _) = Mutate("case $BAYAN_MUTANT in 2) exit 1;; 5) sleep 30;; esac");
        Assert.Equal(0, exit);
        Assert.Contains("a.byn:1:1 arithmetic `a + b` -> `a - b`: survived", output);
        Assert.Contains("a.byn:2:1 arithmetic `a * b` -> `a / b`: killed", output);
        Assert.Contains("a.byn:3:1 bool `true` -> `false`: not emitted", output);
        Assert.Contains("a.byn:4:1 arithmetic `a + 0` -> `a - 0`: marked", output);
        Assert.Contains("a.byn:5:1 statement `i += 1` -> ``: timeout (counted as killed)", output);
        Assert.Contains("1 killed, 1 survived, 1 timeout, 0 not reached, 1 marked, 1 not emitted", output);
    }

    [Fact]
    public void ACommandThatFailsUnmutatedIsAnError()
    {
        if (OperatingSystem.IsWindows()) return;
        var (exit, _, err) = Mutate("exit 3");
        Assert.NotEqual(0, exit);
        Assert.Contains("unmutated", err);
    }
}
