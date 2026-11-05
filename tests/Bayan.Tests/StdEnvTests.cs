using System.Diagnostics;
using System.Text;
using Bayan.Backend.C;
using Bayan.Cli;
using Bayan.Semantics;
using Bayan.Syntax.Text;

namespace Bayan.Tests;

/// <summary>
/// Q970 item 2: <c>Env.get</c> gives what .NET's <c>Environment.GetEnvironmentVariable</c> gives on the same OS, over a corpus of
/// set, empty-valued, non-ASCII and absent names, and needs <c>uses env</c>.
/// </summary>
public sealed class StdEnvTests
{
    private static readonly (string Name, string Value)[] Set =
    [
        ("BYN_ENV_T16_A", "plain"), ("BYN_ENV_T16_EMPTY", ""), ("BYN_ENV_T16_SPACE", " a  b "), ("BYN_ENV_T16_UNI", "é ü \U0001F600 日本"),
        ("BYN_ENV_T16_EQ", "x=y=z"), ("BYN_ENV_T16_LONG", new string('q', 40000)), ("BYN_ENV_T16_SEMI", "a;b:c"), ("BYN_ENV_T16_é", "uni-name"),
        ("BYN_ENV_T16_QUOTE", "say \"hi\" \\ done"), ("byn_env_t16_case", "lower"),
    ];

    private static readonly string[] Names =
    [
        "BYN_ENV_T16_A", "BYN_ENV_T16_EMPTY", "BYN_ENV_T16_SPACE", "BYN_ENV_T16_UNI", "BYN_ENV_T16_EQ", "BYN_ENV_T16_LONG", "BYN_ENV_T16_SEMI",
        "BYN_ENV_T16_é", "BYN_ENV_T16_QUOTE", "byn_env_t16_case", "BYN_ENV_T16_CASE", "BYN_ENV_T16_A ", " BYN_ENV_T16_A", "BYN_ENV_T16_MISSING",
        "BYN_ENV_T16_MISSING_é", "PATH", "Path", "HOME", "USERPROFILE", "PATHEXT", "=", "A=B", "BYN_ENV_T16_A=plain", "a\0b", "BYN_ENV_T16_A\0x", "\0",
        "BYN_ENV_T16_A\0", "x y", "-", "_", "1", "BYN_ENV_T16_EMPTY\0", " ", "", "A\0=", "BYN_ENV_T16_A\0=", "BYN_ENV_T16_A=\0", "=BYN_ENV_T16_A",
    ];

    private static readonly string Root = Path.Combine(Golden.RepoRoot, "tmp", "std-env-diag");

    private static string? Dotnet(string name)
    {
        try { return Environment.GetEnvironmentVariable(name); }
        catch (ArgumentException) { return null; }
    }

    private static string Literal(string s) => StdPathTests.Literal(s).Replace("\0", "\\0");

    private static async Task<(int Code, string Out, string Err)> RunAsync(string source, string dir)
    {
        Environment.SetEnvironmentVariable("BAYAN_CACHE", Path.Combine(Golden.RepoRoot, "tmp", "cache"));
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        Directory.CreateDirectory(dir);
        var c = Compilation.Create([new SourceText("env.byn", Encoding.UTF8.GetBytes(source))], dir);
        Assert.False(c.Diagnostics.HasErrors, string.Join("\n", c.Diagnostics.Items.Select(d => d.Message)));
        var build = Toolchain.Build(CliApp.EmitC(c, BuildMode.Safe), BuildMode.Safe);
        Assert.True(build.Succeeded, build.Errors);
        var psi = new ProcessStartInfo(build.ExecutablePath!)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardOutputEncoding = Encoding.UTF8,
        };
        using var proc = Process.Start(psi)!;
        var stderr = proc.StandardError.ReadToEndAsync();
        var stdout = await proc.StandardOutput.ReadToEndAsync();
        Assert.True(proc.WaitForExit(60_000), "program timed out");
        return (proc.ExitCode, stdout, await stderr);
    }

    [Fact]
    public async Task GetMatchesDotNet()
    {
        foreach (var (n, v) in Set) Environment.SetEnvironmentVariable(n, v);
        var source = new StringBuilder();
        var expected = new List<string>();
        foreach (var name in Names)
        {
            source.Append($"print($\"[{{Env.get({Literal(name)}) ?? \"<null>\"}}]\")\n");
            expected.Add($"[{Dotnet(name) ?? "<null>"}]");
        }
        var (code, stdout, stderr) = await RunAsync(source.ToString(), Path.Combine(Golden.RepoRoot, "tmp", "std-env"));
        Assert.True(code == 0, stderr);
        var actual = stdout.Split('\n');
        Assert.Equal(expected.Count + 1, actual.Length);
        for (var i = 0; i < expected.Count; i++)
            Assert.True(expected[i] == actual[i], $"line {i + 1}: {Literal(Names[i])}\n.NET: [{expected[i]}]\nBayan: [{actual[i]}]");
    }

    [Fact]
    public void GetNeedsEnvEffect()
    {
        var src = "void f() uses console {\n    print(Env.get(\"PATH\") ?? \"\")\n}\n";
        var c = Compilation.Create([new SourceText("e.byn", Encoding.UTF8.GetBytes(src))], Root);
        Assert.Contains(c.Diagnostics.Items, d => d.Code == "BYN6002" && d.Message.Contains("`env`"));
        var ok = Compilation.Create([new SourceText("e.byn", Encoding.UTF8.GetBytes(src.Replace("uses console", "uses console, alloc, env")))], Root);
        Assert.DoesNotContain(ok.Diagnostics.Items, d => d.Code == "BYN6002");
    }

    [Fact]
    public void UnknownMemberIsReported()
    {
        var c = Compilation.Create([new SourceText("e.byn", Encoding.UTF8.GetBytes("void f() uses env {\n    let x = Env.set(\"A\")\n}\n"))], Root);
        Assert.Contains(c.Diagnostics.Items, d => d.Code == "BYN2003" && d.Message.Contains("Env"));
    }
}
