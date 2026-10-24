using System.Diagnostics;
using Bayan.Backend.C;
using Bayan.Cli;
using Bayan.Semantics;
using Bayan.Syntax.Diagnostics;
using Bayan.Syntax.Text;

namespace Bayan.Tests;

/// <summary>Runtime functions driven directly from C (<c>tests/native/*.c</c>, built against <c>bayan-rt.c</c>), and <c>Io.runBatch</c> on Windows.</summary>
public sealed class RuntimeNativeTests
{
    [Fact]
    public void BatchCommandLine()
    {
        var driver = File.ReadAllText(Path.Combine(Golden.RepoRoot, "tests", "native", "batch-cmdline.c"));
        Assert.Equal(
            """"
            cmd.exe /d /e:ON /v:OFF /s /c "".\echo.cmd" "a b""
            cmd.exe /d /e:ON /v:OFF /s /c "".\echo.cmd" """
            refused
            refused
            cmd.exe /d /e:ON /v:OFF /s /c "".\echo.cmd" "^&|<>()!""
            cmd.exe /d /e:ON /v:OFF /s /c "".\echo.cmd" "a\""
            refused
            refused
            refused
            cmd.exe /d /e:ON /v:OFF /s /c ""C:\dir\X.CMD" "a""
            cmd.exe /d /e:ON /v:OFF /s /c ""./run.Bat" "a""
            refused
            refused
            refused
            cmd.exe /d /e:ON /v:OFF /s /c "".\x.bat" "a""
            cmd.exe /d /e:ON /v:OFF /s /c ""C:\x\y.cmd" "a""
            cmd.exe /d /e:ON /v:OFF /s /c "".\echo.cmd" "x" "y z""
            cmd.exe /d /e:ON /v:OFF /s /c "".\echo.cmd""
            refused

            """".ReplaceLineEndings("\n"),
            Run(Build(driver), []).Replace("\r\n", "\n"));
    }

    /// <summary>Each argument reaches the batch file as <c>%~N</c> unchanged.</summary>
    [Fact]
    public void RunBatchRoundTrips()
    {
        var work = Path.Combine(Golden.RepoRoot, "tmp", "native", "run-batch");
        if (Directory.Exists(work)) Directory.Delete(work, recursive: true);
        Directory.CreateDirectory(work);
        var script = Path.Combine(work, "echo.cmd");
        File.WriteAllText(script, "@echo off\r\necho \"%~1\"\r\necho \"%~2\"\r\necho \"%~3\"\r\necho \"%~4\"\r\n");
        var program = $$"""
            void main() fails IoError {
                let args: string[4] = ["a b", "", "^&|<>()!", "a\\"]
                let r = try Io.runBatch("{{script.Replace("\\", "\\\\")}}", args.asSpan())
                print($"exit {r.exitCode}")
                Io.stdout.write(r.stdout)
            }
            """;
        var c = Compilation.Create([SourceText.FromString("run-batch.byn", program)], work);
        Assert.False(c.Diagnostics.HasErrors, DiagnosticRenderer.RenderAll(c.Diagnostics.Items, includePath: true));
        if (!OperatingSystem.IsWindows()) return;
        Assert.Equal("exit 0\n\"a b\"\n\"\"\n\"^&|<>()!\"\n\"a\\\"\n", Run(Build(CliApp.EmitC(c, BuildMode.Debug)), []).Replace("\r\n", "\n"));
    }

    private static string Build(string cSource)
    {
        var build = Toolchain.Build(cSource, BuildMode.Debug);
        Assert.True(build.Succeeded, build.Errors);
        return build.ExecutablePath!;
    }

    private static string Run(string exe, string[] args)
    {
        var psi = new ProcessStartInfo(exe) { RedirectStandardOutput = true, UseShellExecute = false };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        Assert.Equal(0, p.ExitCode);
        return stdout;
    }
}
