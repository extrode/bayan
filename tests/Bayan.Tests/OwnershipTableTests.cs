using System.Diagnostics;
using Bayan.Backend.C;

namespace Bayan.Tests;

/// <summary>The debug runtime's ownership table, driven from C through the public byn_own_ calls.</summary>
public sealed class OwnershipTableTests
{
    private const string Harness = """
        #include "bayan-rt.h"
        #include <stdint.h>
        #include <stdio.h>

        static size_t home(uintptr_t a) { return (size_t)(((uint64_t)a * UINT64_C(0x9E3779B97F4A7C15)) >> 20) & 1023; }

        int main(void) {
            const void* a = (const void*)(uintptr_t)0x10000;
            uintptr_t k = 0x10040;
            while (home(k) != home(0x10000)) k += 0x40;
            const void* b = (const void*)k;
            int p1, p2, p3;
            /* a at its home slot, b right behind it in the same probe run */
            byn_own_claim(a, &p1, "List", "t.byn", 1);
            byn_own_claim(b, &p2, "List", "t.byn", 2);
            byn_own_rekey(b, a);
            /* new storage lands at b's old address */
            byn_own_claim(b, &p3, "List", "t.byn", 3);
            /* a holds b's record, and only that one */
            byn_own_release(a);
            byn_own_claim(a, &p1, "List", "t.byn", 4);
            byn_own_release(a);
            byn_own_release(b);
            puts("ok");
            return 0;
        }
        """;

    [Fact]
    public async Task RekeyOntoAnAddressInTheSameProbeRunLeavesNothingStale()
    {
        var build = Toolchain.Build(Harness, BuildMode.Debug);
        Assert.True(build.Succeeded, build.Errors);
        var psi = new ProcessStartInfo(build.ExecutablePath!)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        await p.WaitForExitAsync(new CancellationTokenSource(60_000).Token);
        Assert.Equal("", await stderr);
        Assert.Equal(0, p.ExitCode);
        Assert.Equal("ok", (await stdout).Trim());
    }
}
