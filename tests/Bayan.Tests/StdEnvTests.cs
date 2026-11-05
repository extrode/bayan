using System.Diagnostics;
using System.Text;
using Bayan.Backend.C;
using Bayan.Cli;
using Bayan.Semantics;
using Bayan.Syntax.Diagnostics;
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

    private static string? Dotnet(string name) => Environment.GetEnvironmentVariable(name);

    private static string Literal(string s) => StdPathTests.Literal(s).Replace("\0", "\\0");

    private static string Build(string source, string dir)
    {
        Environment.SetEnvironmentVariable("BAYAN_CACHE", Path.Combine(Golden.RepoRoot, "tmp", "cache"));
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        Directory.CreateDirectory(dir);
        var c = Compilation.Create([new SourceText("env.byn", Encoding.UTF8.GetBytes(source))], dir);
        Assert.False(c.Diagnostics.HasErrors, string.Join("\n", c.Diagnostics.Items.Select(d => d.Message)));
        var build = Toolchain.Build(CliApp.EmitC(c, BuildMode.Safe), BuildMode.Safe);
        Assert.True(build.Succeeded, build.Errors);
        return build.ExecutablePath!;
    }

    private static async Task<(int Code, string Out, string Err)> RunAsync(string source, string dir)
    {
        var psi = new ProcessStartInfo(Build(source, dir))
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
        try
        {
            foreach (var (n, v) in Set) Environment.SetEnvironmentVariable(n, v);
            await CompareAsync();
        }
        finally
        {
            foreach (var (n, _) in Set) Environment.SetEnvironmentVariable(n, null);
        }
    }

    private static async Task CompareAsync()
    {
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

    /// <summary>The compiler's diagnostics as <c>code line:col message</c>, with the help line when there is one.</summary>
    private static List<string> Rendered(Compilation c) =>
        c.Diagnostics.Items.Select(d =>
        {
            var (line, col) = d.Source!.GetLinePosition(d.Span.Start);
            return $"{d.Code} {line}:{col} {d.Message}" + (d.Help is null ? "" : $" | {d.Help}");
        }).ToList();

    private static string Expected(DiagnosticDescriptor descriptor, string source, string at, string help, params object[] args)
    {
        var offset = source.IndexOf(at, StringComparison.Ordinal);
        var line = source[..offset].Count(ch => ch == '\n') + 1;
        var col = offset - (source.LastIndexOf('\n', offset) + 1) + 1;
        return $"{descriptor.Code} {line}:{col} {descriptor.Format(args)}" + (help.Length == 0 ? "" : $" | {help}");
    }

    [Fact]
    public void GetNeedsEnvEffect()
    {
        var src = "void f() uses console {\n    print(Env.get(\"PATH\") ?? \"\")\n}\n";
        var c = Compilation.Create([new SourceText("e.byn", Encoding.UTF8.GetBytes(src))], Root);
        // The fix help is the compiler's own text and is not compared.
        var rendered = Rendered(c).Where(r => r.StartsWith("BYN6002")).Select(r => r.Split(" | ")[0]).ToList();
        Assert.Equal(
            [Expected(DiagnosticCodes.EffectNotDeclared, src, "Env.get", "", "f", "env"), Expected(DiagnosticCodes.EffectNotDeclared, src, "Env.get", "", "f", "alloc")],
            rendered);
        var ok = Compilation.Create([new SourceText("e.byn", Encoding.UTF8.GetBytes(src.Replace("uses console", "uses console, alloc, env")))], Root);
        Assert.DoesNotContain(Rendered(ok), r => r.StartsWith("BYN6002"));
    }

    [Fact]
    public void UnknownMemberIsReported()
    {
        var src = "void f() uses env {\n    let x = Env.set(\"A\")\n}\n";
        var c = Compilation.Create([new SourceText("e.byn", Encoding.UTF8.GetBytes(src))], Root);
        Assert.Equal([Expected(DiagnosticCodes.UnknownMember, src, "set", "did you mean `get`?", "set", "Env")], Rendered(c).Where(r => r.StartsWith("BYN2003")).ToList());
    }

    [Fact]
    public void FarMemberHasNoSuggestion()
    {
        var src = "void f() uses env {\n    let x = Env.zzzzzzzz(\"A\")\n}\n";
        var c = Compilation.Create([new SourceText("e.byn", Encoding.UTF8.GetBytes(src))], Root);
        Assert.Equal([Expected(DiagnosticCodes.UnknownMember, src, "zzzzzzzz", "", "zzzzzzzz", "Env")], Rendered(c).Where(r => r.StartsWith("BYN2003")).ToList());
    }

    private const string ChildMarker = "BYN_ENV_T16_ORACLE_OUT";

    /// <summary>Raw values as printf octal escapes: truncated, overlong, surrogate, out-of-range and stray bytes, and valid ones between.</summary>
    private static readonly string[] RawValues =
    [
        "a\\377b", "\\300\\200", "\\355\\240\\200", "\\340\\200\\200", "\\340\\200", "\\360\\237", "\\360\\237\\230", "\\360\\200\\200\\200",
        "\\364\\220\\200\\200", "\\365", "\\200", "\\302", "\\302\\302", "\\355\\240", "\\355\\200", "\\355\\237\\277", "\\356\\200", "\\357\\277\\275",
        "\\360\\220\\200", "\\364\\217\\277", "\\344\\270", "\\344\\270\\255", "\\302\\200", "\\301\\277", "x\\344\\270y", "\\355\\240\\200\\355\\260\\200", "\\240\\240",
        "\\370\\200\\200\\200\\200", "\\360\\237\\230\\200", "\\343\\201\\377", "\\355\\277",
    ];

    /// <summary>The .NET side of <see cref="InvalidUtf8ValueMatchesDotNet"/>: run as a child, it writes the UTF-8 bytes (hex) .NET returns for each <c>BYN_ENV_T16_RAW</c> variable.</summary>
    [Fact]
    public void OracleChild()
    {
        if (Environment.GetEnvironmentVariable(ChildMarker) is not { } outFile) return;
        File.WriteAllLines(outFile, RawValues.Select((_, i) =>
            Convert.ToHexString(Encoding.UTF8.GetBytes(Environment.GetEnvironmentVariable($"BYN_ENV_T16_RAW{i}") ?? "<null>"))));
    }

    /// <summary>
    /// A value holding raw invalid UTF-8 cannot be set from .NET, so a shell sets it for two children: this test assembly (the .NET
    /// oracle, re-run as a child) and the Bayan program. Both must give the same bytes.
    /// </summary>
    [Fact]
    public async Task InvalidUtf8ValueMatchesDotNet()
    {
        if (OperatingSystem.IsWindows()) return; // no /bin/sh to put raw bytes in the environment; a Windows value is UTF-16, so it holds no invalid UTF-8 bytes
        var dir = Path.Combine(Golden.RepoRoot, "tmp", "std-env-raw");
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        Directory.CreateDirectory(dir);
        var oracleFile = Path.Combine(dir, "oracle.txt");
        var assignments = string.Join(" ", RawValues.Select((v, i) => $"BYN_ENV_T16_RAW{i}=$(printf '{v}')"));

        string Sh(params string[] args)
        {
            var psi = new ProcessStartInfo("/bin/sh") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, StandardOutputEncoding = Encoding.Latin1 };
            psi.ArgumentList.Add("-c");
            psi.ArgumentList.Add($"{assignments} exec \"$@\"");
            psi.ArgumentList.Add("sh");
            foreach (var a in args) psi.ArgumentList.Add(a);
            psi.Environment[ChildMarker] = oracleFile;
            using var proc = Process.Start(psi)!;
            var err = proc.StandardError.ReadToEndAsync();
            var o = proc.StandardOutput.ReadToEnd();
            Assert.True(proc.WaitForExit(120_000), "timed out");
            Assert.True(proc.ExitCode == 0, o + err.Result);
            return o;
        }

        Sh("dotnet", "test", typeof(StdEnvTests).Assembly.Location, "--no-build", "--filter", "FullyQualifiedName~StdEnvTests.OracleChild");
        var expected = File.ReadAllLines(oracleFile);
        Assert.Equal(RawValues.Length, expected.Length);

        var source = string.Concat(RawValues.Select((_, i) => $"print(Env.get(\"BYN_ENV_T16_RAW{i}\") ?? \"<null>\")\n"));
        var actual = Sh(Build(source, Path.Combine(dir, "prog"))).Split('\n')[..RawValues.Length]
            .Select(l => Convert.ToHexString(Encoding.Latin1.GetBytes(l))).ToArray();
        for (var i = 0; i < expected.Length; i++)
            Assert.True(expected[i] == actual[i], $"value {RawValues[i]}\n.NET: {expected[i]}\nBayan: {actual[i]}");
    }
}
