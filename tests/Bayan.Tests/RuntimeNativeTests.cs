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
                let r = try Io.runBatch("{{script.Replace("\\", "\\\\")}}", args.span)
                print($"exit {r.exitCode}")
                Io.stdout.write(r.stdout)
            }
            """;
        var c = Compilation.Create([SourceText.FromString("run-batch.byn", program)], work);
        Assert.False(c.Diagnostics.HasErrors, DiagnosticRenderer.RenderAll(c.Diagnostics.Items, includePath: true));
        if (!OperatingSystem.IsWindows()) return;
        Assert.Equal("exit 0\n\"a b\"\n\"\"\n\"^&|<>()!\"\n\"a\\\"\n", Run(Build(CliApp.EmitC(c, BuildMode.Debug)), []).Replace("\r\n", "\n"));
    }

    /// <summary>A 0-byte request is out of memory to an allocator whose <c>realloc</c> returns NULL for it.</summary>
    [Fact]
    public void EmptyInputAsksNoZeroByteBlock()
    {
        var driver = File.ReadAllText(Path.Combine(Golden.RepoRoot, "tests", "native", "zero-resize.c"));
        Assert.Equal("hex 0, zero-byte requests 0\n", Run(Build(driver), []).Replace("\r\n", "\n"));
    }

    /// <summary>A growing <c>StrBuf</c> never frees or overwrites a block it outgrew: <c>append</c> after <c>finish()</c> is allowed (Q745).</summary>
    [Fact]
    public void StrBufGrowthKeepsTheOldBlock()
    {
        var driver = File.ReadAllText(Path.Combine(Golden.RepoRoot, "tests", "native", "strbuf-grow.c"));
        Assert.Equal("cap 128, taken text unchanged 1, old blocks handed back 0\n", Run(Build(driver), []).Replace("\r\n", "\n"));
    }

    /// <summary><c>replace</c> with items read from the list itself releases the copy it takes of them.</summary>
    [Fact]
    public void AliasingReplaceReleasesItsCopy()
    {
        const string program = """
            var xs = List<i64>()
            xs.append(1)
            xs.append(2)
            xs.replace(at: 0..1, by: xs.span[1..2])
            print($"{xs[0]} {xs[1]}")
            """;
        var c = Compilation.Create([SourceText.FromString("replace.byn", program)], Path.Combine(Golden.RepoRoot, "tmp"));
        Assert.False(c.Diagnostics.HasErrors, DiagnosticRenderer.RenderAll(c.Diagnostics.Items, includePath: true));
        var code = CliApp.EmitC(c, BuildMode.Debug);
        const string counters = """
            #include <stdio.h>
            #include <stdlib.h>
            static int live_copies;
            static void report(void) { printf("live %d\n", live_copies); }
            static void* counted_copy(const void* p, int64_t n, byn_alloc* to) {
                if (live_copies++ == 0) atexit(report);
                return byn_copy_bytes(p, n, to);
            }
            static void counted_release(byn_alloc* from, void* p) {
                live_copies--;
                byn_release(from, p);
            }

            """;
        Assert.Contains("byn_copy_bytes(items.ptr", code);
        code = code.Replace("#include \"bayan-rt.h\"\n", "#include \"bayan-rt.h\"\n" + counters)
            .Replace("byn_copy_bytes(items.ptr", "counted_copy(items.ptr").Replace("byn_release(l->alloc", "counted_release(l->alloc");
        Assert.Equal("2 2\nlive 0\n", Run(Build(code), []).Replace("\r\n", "\n"));
    }

    /// <summary><c>byn_cov_flush</c> runs once when a panic aborts the program.</summary>
    [Fact]
    public void CovFlushRunsOnPanic()
    {
        var driver = File.ReadAllText(Path.Combine(Golden.RepoRoot, "tests", "native", "cov-flush.c"));
        var psi = new ProcessStartInfo(Build(driver)) { RedirectStandardOutput = true, StandardOutputEncoding = System.Text.Encoding.UTF8, RedirectStandardError = true, StandardErrorEncoding = System.Text.Encoding.UTF8, UseShellExecute = false };
        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEndLf();
        p.StandardError.ReadToEndLf();
        p.WaitForExit();
        Assert.NotEqual(0, p.ExitCode);
        Assert.Equal("flushed\n", stdout.Replace("\r\n", "\n"));
    }

    /// <summary>Seeded random checks of float and int formatting, UTF-8, search, sort, maps and hex against reference models.</summary>
    [Fact]
    public void RuntimePropertiesHold()
    {
        var driver = File.ReadAllText(Path.Combine(Golden.RepoRoot, "tests", "native", "runtime-properties.c"));
        Assert.Equal("ok\n", Run(Build(driver), []).Replace("\r\n", "\n"));
    }

    private static string Build(string cSource)
    {
        var build = Toolchain.Build(cSource, BuildMode.Debug);
        Assert.True(build.Succeeded, build.Errors);
        return build.ExecutablePath!;
    }

    private static string Run(string exe, string[] args)
    {
        var psi = new ProcessStartInfo(exe) { RedirectStandardOutput = true, StandardOutputEncoding = System.Text.Encoding.UTF8, UseShellExecute = false };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEndLf();
        p.WaitForExit();
        Assert.Equal(0, p.ExitCode);
        return stdout;
    }
}
