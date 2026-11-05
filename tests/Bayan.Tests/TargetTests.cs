using System.Diagnostics;
using Bayan.Backend.C;
using Bayan.Cli;
using Bayan.Semantics;
using Bayan.Syntax.Text;

namespace Bayan.Tests;

/// <summary><c>Target.os</c> and <c>Target.arch</c>: compile-time string literals naming the target being built (012 Q970 item 1).</summary>
public sealed class TargetTests
{
    private const string Program = "print($\"{Target.os}-{Target.arch}\")\n";

    private static string Dir(string name)
    {
        var dir = Path.Combine(Golden.RepoRoot, "tmp", "target-tests", name);
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static (int Exit, string Out, string Err) Cli(params string[] args)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var exit = CliApp.Run(args, stdout, stderr);
        return (exit, stdout.ToString().ReplaceLineEndings("\n"), stderr.ToString().ReplaceLineEndings("\n"));
    }

    private static string Write(string name, string text)
    {
        var path = Path.Combine(Dir(name), "main.byn");
        File.WriteAllText(path, text);
        return path;
    }

    [Fact]
    public void ACompiledProgramPrintsTheHost()
    {
        var path = Write("host", Program);
        var c = Compilation.FromPath(path, wholeDirectory: false);
        Assert.False(c.Diagnostics.HasErrors, string.Join("\n", c.Diagnostics.Items.Select(d => d.Message)));
        var build = Toolchain.Build(CliApp.EmitC(c, BuildMode.Safe), BuildMode.Safe);
        Assert.True(build.Succeeded, build.Errors);
        var psi = new ProcessStartInfo(build.ExecutablePath!) { RedirectStandardOutput = true };
        using var process = Process.Start(psi)!;
        var output = process.StandardOutput.ReadToEnd().Trim();
        process.WaitForExit();
        Assert.Equal(Targets.Host ?? "-", output);
    }

    [Theory]
    [InlineData("windows-arm64", "windows", "arm64")]
    [InlineData("linux-x64", "linux", "x64")]
    [InlineData("macos-arm64", "macos", "arm64")]
    [InlineData("wasm32", "wasi", "wasm32")]
    public void EmitCFollowsTheTarget(string target, string os, string arch)
    {
        var (exit, stdout, stderr) = Cli("build", "--emit-c", "--target", target, Write("emit-" + target, Program));
        Assert.True(exit == 0, stderr);
        Assert.Contains($"\"{os}\"", stdout);
        Assert.Contains($"\"{arch}\"", stdout);
    }

    [Fact]
    public void WithoutATargetTheEmittedCIsHostNeutral()
    {
        var path = Write("default", Program);
        var (_, plain, _) = Cli("build", "--emit-c", path);
        var (_, host, _) = Cli("build", "--emit-c", "--target", "host", path);
        Assert.Equal(plain, host);
        Assert.Contains("BYN_HOST_OS", plain);
        Assert.Contains("BYN_HOST_ARCH", plain);
        foreach (var name in new[] { "windows", "macos", "linux", "wasi", "x64", "arm64", "wasm32" })
            Assert.DoesNotContain($"\"{name}\"", plain);
    }

    [Fact]
    public void ANullHostSplitsToEmptyStrings()
    {
        Assert.Equal(("", ""), HostTarget.Split(null));
        Assert.Equal(("linux", "x64"), HostTarget.Split("linux-x64"));
    }

    [Fact]
    public void AnUnknownMemberIsADiagnostic()
    {
        var c = Compilation.Create([new SourceText("main.byn", System.Text.Encoding.UTF8.GetBytes("print(Target.name)\n"))], Dir("unknown"));
        var d = Assert.Single(c.Diagnostics.Items);
        Assert.Equal("BYN2003", d.Code);
    }

    [Fact]
    public void AUserTypeNamedTargetShadowsIt()
    {
        var c = Compilation.Create([new SourceText("main.byn", System.Text.Encoding.UTF8.GetBytes("struct Target {\n    i32 os\n}\nlet t: Target = Target { os: 1 }\nprint($\"{t.os}\")\n"))], Dir("shadow"));
        Assert.False(c.Diagnostics.HasErrors, string.Join("\n", c.Diagnostics.Items.Select(d => d.Message)));
    }
}
