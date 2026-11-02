using System.Diagnostics;
using Bayan.Backend.C;

namespace Bayan.Tests;

/// <summary>The debug runtime's ownership table, driven from a C harness that includes bayan-rt.c.</summary>
public sealed class OwnershipTableTests
{
    /// <summary>Includes the runtime itself, so the table's count is visible.</summary>
    private static string Harness(string runtime) => $$"""
        #include "{{runtime}}"
        #include <stdint.h>

        static size_t home(uintptr_t a) { return (size_t)(((uint64_t)a * UINT64_C(0x9E3779B97F4A7C15)) >> 20) & 1023; }

        static size_t live(void) {
            size_t n = 0;
            for (size_t i = 0; i < byn_own_cap; i++) if (byn_own_table[i].storage) n++;
            return n;
        }

        #define COUNT(n) do { if (byn_own_count != (n) || live() != (n)) { fprintf(stderr, "line %d: count %zu live %zu, want %d\n", __LINE__, byn_own_count, live(), (n)); return 1; } } while (0)

        int main(void) {
            const void* a = (const void*)(uintptr_t)0x10000;
            uintptr_t k = 0x10040;
            while (home(k) != home(0x10000)) k += 0x40;
            const void* b = (const void*)k;
            int p1, p2, p3;
            /* a at its home slot, b right behind it in the same probe run */
            byn_own_claim(a, &p1, "List", "t.byn", 1);
            byn_own_claim(b, &p2, "List", "t.byn", 2);
            COUNT(2);
            byn_own_rekey(b, a);
            COUNT(1);
            /* new storage lands at b's old address */
            byn_own_claim(b, &p3, "List", "t.byn", 3);
            COUNT(2);
            byn_own_release(a);
            COUNT(1);
            byn_own_claim(a, &p1, "List", "t.byn", 4);
            COUNT(2);
            byn_own_release(a);
            byn_own_release(b);
            COUNT(0);
            puts("ok");
            return 0;
        }
        """;

    [Fact]
    public async Task ARunPastItsTimeoutIsKilledAndReported()
    {
        var dir = Path.Combine(Golden.RepoRoot, "tmp", "own-table", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var (file, args) = OperatingSystem.IsWindows() ? ("ping", new[] { "-n", "30", "127.0.0.1" }) : ("sleep", new[] { "30" });
        try
        {
            var clock = Stopwatch.StartNew();
            var e = await Assert.ThrowsAsync<TimeoutException>(() => RunAsync(file, args, dir, timeoutMs: 500));
            Assert.Contains($"{file} did not exit within 500 ms", e.Message);
            Assert.True(clock.Elapsed < TimeSpan.FromSeconds(20), $"took {clock.Elapsed}");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task RekeyOntoAnAddressInTheSameProbeRunLeavesNothingStale()
    {
        var compiler = Toolchain.FindCompiler();
        Assert.NotNull(compiler);
        var dir = Path.Combine(Golden.RepoRoot, "tmp", "own-table", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var runtime = Path.Combine(Golden.RepoRoot, "src", "runtime");
            File.WriteAllText(Path.Combine(dir, "harness.c"), Harness(Path.Combine(runtime, "bayan-rt.c").Replace('\\', '/')));
            var exe = Path.Combine(dir, OperatingSystem.IsWindows() ? "harness.exe" : "harness");
            var args = new List<string>(compiler[1..]) { "-std=c11", "-DBYN_DEBUG", "-w", "-I", runtime, "harness.c", "-o", exe };
            args.Add(OperatingSystem.IsWindows() ? "-lshell32" : "-lm");
            var build = await RunAsync(compiler[0], args, dir, timeoutMs: 300_000); // a cold zig cache builds Windows libc first
            Assert.True(build.Exit == 0, build.Stdout + build.Stderr);
            var run = await RunAsync(exe, [], dir);
            Assert.Equal("", run.Stderr);
            Assert.Equal(0, run.Exit);
            Assert.Equal("ok", run.Stdout.Trim());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    internal static async Task<(int Exit, string Stdout, string Stderr)> RunAsync(string file, IEnumerable<string> args, string dir, int timeoutMs = 60_000)
    {
        var psi = new ProcessStartInfo(file)
        {
            RedirectStandardOutput = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            RedirectStandardError = true,
            StandardErrorEncoding = System.Text.Encoding.UTF8,
            UseShellExecute = false,
            WorkingDirectory = dir,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEndLfAsync();
        var stderr = p.StandardError.ReadToEndLfAsync();
        using var timeout = new CancellationTokenSource(timeoutMs);
        try { await p.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            // Killed so the finally's Directory.Delete can run: on Windows a live process keeps its directory, and that IOException hid this timeout (CI 41396961495).
            p.Kill(entireProcessTree: true);
            await p.WaitForExitAsync();
            throw new TimeoutException($"{file} did not exit within {timeoutMs} ms");
        }
        return (p.ExitCode, await stdout, await stderr);
    }
}
