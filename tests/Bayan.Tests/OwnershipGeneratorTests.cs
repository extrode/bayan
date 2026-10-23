using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Bayan.Backend.C;
using Bayan.Semantics;
using Bayan.Syntax.Text;
using Xunit.Abstractions;

namespace Bayan.Tests;

/// <summary>
/// <see cref="OwnershipGenerator"/> is deterministic and its programs are mostly accepted (spec 003 R9);
/// <c>Toolchain.Build(..., sanitize: true)</c> catches a heap use-after-free when ASan is available.
/// </summary>
public sealed class OwnershipGeneratorTests(ITestOutputHelper output)
{
    [Fact]
    public void SameSeedSameText()
    {
        var text = OwnershipGenerator.Generate(1, 8);
        Assert.Equal(text, OwnershipGenerator.Generate(1, 8));
        Assert.NotEqual(text, OwnershipGenerator.Generate(2, 8));
        Assert.Equal("aa1fb0d524f1e953dda147d3d726038184357422192f0ea9f794e375ebfc808c", Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text))));
    }

    [Fact]
    public void FewerThanHalfRejected()
    {
        const int seeds = 24;
        var rejected = 0;
        for (var seed = 1UL; seed <= seeds; seed++)
        {
            var source = OwnershipGenerator.Generate(seed, 16);
            var c = Compilation.Create([SourceText.FromString($"gen-{seed}.byn", source)], ".");
            var errors = c.Diagnostics.Items.Where(d => d.IsError).ToList();
            if (errors.Count == 0) continue;
            rejected++;
            output.WriteLine($"seed {seed}: {string.Join(" ", errors.Select(e => e.Code).Distinct().Order())}");
        }
        output.WriteLine($"rejected {rejected} of {seeds}");
        Assert.True(rejected * 2 < seeds, $"rejected {rejected} of {seeds}");
    }

    [Fact]
    public void SanitizeCatchesUseAfterFree()
    {
        if (!Toolchain.AsanAvailable) return;
        var build = Toolchain.Build("""
            #include <stdlib.h>
            int main(void) {
                int *p = malloc(4 * sizeof(int));
                free(p);
                return p[1];
            }
            """, BuildMode.Debug, sanitize: true);
        Assert.True(build.Succeeded, build.Errors);
        var psi = new ProcessStartInfo(build.ExecutablePath!) { RedirectStandardError = true, UseShellExecute = false };
        using var p = Process.Start(psi)!;
        var stderr = p.StandardError.ReadToEnd();
        p.WaitForExit();
        Assert.NotEqual(0, p.ExitCode);
        Assert.Contains("AddressSanitizer", stderr);
    }
}
