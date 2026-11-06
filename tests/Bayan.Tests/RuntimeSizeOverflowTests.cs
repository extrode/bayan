using Bayan.Backend.C;

namespace Bayan.Tests;

/// <summary>
/// <c>replaced</c>, <c>joined</c> and list growth panic "too long" when their size arithmetic would overflow (audit
/// docs/security/2026-11-06-runtime-audit.md). Sizes near 2^63 are out of reach from Bayan, so a C harness that includes
/// bayan-rt.c passes texts whose lengths are that large; each panics before it reads or allocates them.
/// </summary>
public sealed class RuntimeSizeOverflowTests
{
    private const string Harness = """
        #include "bayan-rt.c"

        int main(int argc, char** argv) {
            (void)argc;
            byn_str huge = {(const uint8_t*)"", INT64_MAX / 2 + 1};
            if (strcmp(argv[1], "replaced") == 0) {
                byn_str s = {(const uint8_t*)"aaa", 3}, x = {(const uint8_t*)"a", 1};
                byn_str_replaced(s, x, huge, &byn_general, "t.byn", 1);
            } else if (strcmp(argv[1], "joined") == 0) {
                byn_str xs[2] = {huge, huge};
                byn_str_joined(xs, 2, (byn_str){NULL, 0}, &byn_general);
            } else {
                void* p = NULL;
                int64_t cap = 0;
                byn_list_reserve(&p, &cap, INT64_MAX / 8, 16, &byn_general);
            }
            puts("no panic");
            return 0;
        }
        """;

    [Fact]
    public async Task SizeOverflowPanicsTooLong()
    {
        var compiler = Toolchain.FindCompiler();
        Assert.NotNull(compiler);
        var dir = Path.Combine(Golden.RepoRoot, "tmp", "size-overflow", Guid.NewGuid().ToString("N"));
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
            foreach (var (what, message) in new[] { ("replaced", "replaced text is too long"), ("joined", "joined text is too long"), ("reserve", "list is too long") })
            {
                var run = await OwnershipTableTests.RunAsync(exe, [what], dir);
                Assert.Equal("", run.Stdout);
                Assert.StartsWith($"panic: {message}", run.Stderr);
                Assert.NotEqual(0, run.Exit);
            }
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
