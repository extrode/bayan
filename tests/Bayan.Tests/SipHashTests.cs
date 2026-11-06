using Bayan.Backend.C;

namespace Bayan.Tests;

/// <summary>
/// The runtime's map and symbol hash is SipHash-1-3, checked from a C harness that includes bayan-rt.c against the
/// reference vectors in Rust's <c>library/coretests/tests/hash/sip.rs</c> (<c>test_siphash_1_3</c>): key 00..0f, message
/// 00..(n-1) for length n, each output read as a little-endian u64.
/// </summary>
public sealed class SipHashTests
{
    private const string Harness = """
        #include "bayan-rt.c"

        static const struct { int len; uint64_t want; } vectors[] = {
            {0, UINT64_C(0xabac0158050fc4dc)},
            {1, UINT64_C(0xc9f49bf37d57ca93)},
            {7, UINT64_C(0xd3927d989bb11140)},
            {8, UINT64_C(0x369095118d299a8e)},
            {15, UINT64_C(0xd320d86d2a519956)},
            {16, UINT64_C(0xcc4fdd1a7d908b66)},
            {63, UINT64_C(0x9d199062b7bbb3a8)},
        };

        int main(void) {
            uint8_t msg[64];
            for (int i = 0; i < 64; i++) msg[i] = (uint8_t)i;
            int bad = 0;
            for (size_t i = 0; i < sizeof vectors / sizeof vectors[0]; i++) {
                uint64_t got = byn_siphash13(UINT64_C(0x0706050403020100), UINT64_C(0x0f0e0d0c0b0a0908), msg, vectors[i].len);
                if (got != vectors[i].want) {
                    fprintf(stderr, "length %d: got %016llx\n", vectors[i].len, (unsigned long long)got);
                    bad = 1;
                }
            }
            /* The fixed --test key is all zeros, and byn_hash_bytes is SipHash-1-3 under the key byn_hash_seed_init sets. */
            byn_hash_seed_init(true);
            if (byn_hash_bytes(msg, 15) != byn_siphash13(0, 0, msg, 15)) bad = 1, fputs("fixed key is not zero\n", stderr);
            byn_hash_seed_init(false);
            if (byn_hash_seed == 0 && byn_hash_seed_hi == 0) bad = 1, fputs("random key is zero\n", stderr);
            if (!bad) puts("ok");
            return bad;
        }
        """;

    [Fact]
    public async Task SipHash13MatchesTheReferenceVectors()
    {
        var compiler = Toolchain.FindCompiler();
        Assert.NotNull(compiler);
        var dir = Path.Combine(Golden.RepoRoot, "tmp", "siphash", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var runtime = Path.Combine(Golden.RepoRoot, "src", "runtime");
            File.WriteAllText(Path.Combine(dir, "harness.c"), Harness);
            var exe = Path.Combine(dir, OperatingSystem.IsWindows() ? "harness.exe" : "harness");
            var args = new List<string>(compiler[1..]) { "-std=c11", "-DBYN_NDEBUG", "-w", "-I", runtime, "harness.c", "-o", exe };
            args.Add(OperatingSystem.IsWindows() ? "-lshell32" : "-lm");
            var build = await OwnershipTableTests.RunAsync(compiler[0], args, dir, timeoutMs: 300_000);
            Assert.True(build.Exit == 0, build.Stdout + build.Stderr);
            var run = await OwnershipTableTests.RunAsync(exe, [], dir);
            Assert.Equal("", run.Stderr);
            Assert.Equal(0, run.Exit);
            Assert.Equal("ok", run.Stdout.Trim());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
