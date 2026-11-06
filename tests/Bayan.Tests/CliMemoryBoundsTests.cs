using Bayan.Backend.C;
using Bayan.Cli;

namespace Bayan.Tests;

/// <summary>Bounds on what the CLI keeps in memory and on disk (fix/cli-memory-bounds); these change environment variables, so they run alone.</summary>
[Collection(nameof(ToolchainEnvironment))]
public sealed class CliMemoryBoundsTests
{
    private static (int Exit, string Out, string Err) Run(params string[] args)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var exit = CliApp.Run(args, stdout, stderr);
        return (exit, stdout.ToString().ReplaceLineEndings("\n"), stderr.ToString().ReplaceLineEndings("\n"));
    }

    private static string Fresh(string name)
    {
        var dir = Path.Combine(Golden.RepoRoot, "tmp", "cli-memory-bounds", name);
        TestDirs.Delete(dir);
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>A stream of <paramref name="length"/> bytes that never exists in memory.</summary>
    private sealed class Endless(long length) : Stream
    {
        private long _left = length;
        public override int Read(byte[] buffer, int offset, int count)
        {
            var n = (int)Math.Min(count, _left);
            Array.Fill(buffer, (byte)'x', offset, n);
            _left -= n;
            return n;
        }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    // ── M1 ───────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task M1_TheTailKeepsTheLastBytesInOrderAcrossUnevenWrites()
    {
        const int keep = 10;
        var bytes = Enumerable.Range(0, 1000).Select(i => (byte)('a' + i % 26)).ToArray();
        var text = await BoundedCapture.ReadAsync(new Chunked(bytes, [3, 7, 1, 13, 25, 2]), keep);
        var expected = System.Text.Encoding.ASCII.GetString(bytes, 0, keep) + $"\n… {1000 - 2 * keep} bytes elided …\n"
            + System.Text.Encoding.ASCII.GetString(bytes, 1000 - keep, keep);
        Assert.Equal(expected, text);
    }

    [Fact]
    public async Task M1_ASnapshotOfAPipeStillHeldOpenKeepsWhatWasRead()
    {
        var server = new System.IO.Pipes.AnonymousPipeServerStream(System.IO.Pipes.PipeDirection.Out);
        var client = new System.IO.Pipes.AnonymousPipeClientStream(System.IO.Pipes.PipeDirection.In, server.ClientSafePipeHandle);
        var capture = BoundedCapture.Start(client, 1024);
        server.Write(Enumerable.Repeat((byte)'z', 10_000).ToArray());
        server.Flush();
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!capture.Snapshot().Contains("… 7952 bytes elided …") && DateTime.UtcNow < deadline) Thread.Sleep(20);
        Assert.False(capture.Completion.IsCompleted);
        Assert.Equal(new string('z', 1024) + "\n… 7952 bytes elided …\n" + new string('z', 1024), capture.Snapshot());
        // Closing the write end ends the read; the read end is disposed only once nothing reads it.
        server.Dispose();
        await capture.Completion.WaitAsync(TimeSpan.FromSeconds(10));
        client.Dispose();
    }

    /// <summary>Returns <paramref name="bytes"/> in reads of the given sizes, cycling.</summary>
    private sealed class Chunked(byte[] bytes, int[] sizes) : MemoryStream(bytes)
    {
        private int _i;
        public override int Read(byte[] buffer, int offset, int count) => base.Read(buffer, offset, Math.Min(count, sizes[_i++ % sizes.Length]));
    }

    [Fact]
    public async Task M1_CaptureKeepsTheFirstAndLastBytesAndCountsTheRest()
    {
        var text = await BoundedCapture.ReadAsync(new Endless(50_000_000));
        Assert.True(text.Length < 2 * BoundedCapture.Keep + 100, text.Length.ToString());
        Assert.Contains($"\n… {50_000_000 - 2 * BoundedCapture.Keep} bytes elided …\n", text);
    }

    [Fact]
    public async Task M1_CaptureUnderTheCapIsUnchanged()
    {
        var bytes = new byte[2 * BoundedCapture.Keep];
        Array.Fill(bytes, (byte)'y');
        Assert.Equal(new string('y', bytes.Length), await BoundedCapture.ReadAsync(new MemoryStream(bytes)));
    }

    [Fact]
    public async Task M1_ACharacterSplitAtTheHeadCapSurvivesWhenNothingIsElided()
    {
        var bytes = new byte[BoundedCapture.Keep + 2];
        Array.Fill(bytes, (byte)'a');
        bytes[BoundedCapture.Keep - 1] = 0xC3; // "é" straddles the head's end
        bytes[BoundedCapture.Keep] = 0xA9;
        var text = await BoundedCapture.ReadAsync(new MemoryStream(bytes));
        Assert.DoesNotContain('\uFFFD', text);
        Assert.Equal(bytes, System.Text.Encoding.UTF8.GetBytes(text));
    }

    [Fact]
    public async Task M1_ATestPrintingUntilKilledStaysSmallAndReportsTheElidedBytes()
    {
        var dir = Fresh("spam");
        var file = Path.Combine(dir, "main.byn");
        File.WriteAllText(file,
            "void main() {\n}\n\ntest \"spam\" {\n    while true {\n" +
            "        print(\"xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx\")\n" +
            "    }\n}\n");
        var psi = new System.Diagnostics.ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        psi.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "bayan.dll"));
        psi.ArgumentList.Add("test");
        psi.ArgumentList.Add(file);
        psi.Environment["BAYAN_TEST_TIMEOUT_MS"] = "3000";
        psi.Environment["BAYAN_CACHE"] = Path.Combine(dir, "cache");
        using var p = System.Diagnostics.Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        long peak = 0;
        while (!p.WaitForExit(100))
        {
            try
            {
                p.Refresh();
                peak = Math.Max(peak, Math.Max(p.WorkingSet64, p.PeakWorkingSet64));
            }
            catch (InvalidOperationException) { }
        }
        Assert.True(peak > 0, "no sample");
        Assert.True(peak < 300L * 1024 * 1024, $"peak working set {peak / 1024 / 1024} MB");
        Assert.Equal(CliApp.ExitTestFailure, p.ExitCode);
        Assert.Matches(@"… \d+ bytes elided …", await stdout);
    }

    // ── M2 ───────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task M2_DiscardedOutputIsDrainedWithoutBuffering()
    {
        var before = GC.GetTotalAllocatedBytes(precise: true);
        await Bayan.Testing.MutationRunner.Drain(new Endless(400_000_000));
        var allocated = GC.GetTotalAllocatedBytes(precise: true) - before;
        Assert.True(allocated < 100_000_000, allocated.ToString());
    }

    // ── M3 ───────────────────────────────────────────────────────────────────────

    [Fact]
    public void M3_FormatSkipsScratchDirectoriesAndOutwardLinks()
    {
        if (OperatingSystem.IsWindows()) return;
        var root = Fresh("format");
        const string Unformatted = "let a = true\nunless !a { print(\"y\") }\n";
        var fixture = Path.Combine(root, "fixture");
        var outside = Path.Combine(root, "outside");
        foreach (var file in new[] { ".worktrees/x/a.byn", "tmp/b.byn", "src/ok.byn" }.Select(f => Path.Combine(fixture, f)).Append(Path.Combine(outside, "c.byn")))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, Unformatted);
        }
        Directory.CreateSymbolicLink(Path.Combine(fixture, "link"), outside);
        Directory.CreateSymbolicLink(Path.Combine(fixture, "inner-link"), Path.Combine(fixture, "tmp"));
        var (exit, stdout, stderr) = Run("format", "--check", fixture);
        var output = stdout + stderr;
        Assert.Equal(CliApp.ExitFailure, exit);
        Assert.Contains("ok.byn", output);
        Assert.DoesNotContain("a.byn", output);
        Assert.DoesNotContain("b.byn", output);
        Assert.DoesNotContain("c.byn", output);
        Assert.DoesNotContain("inner-link", output);
    }

    [Fact]
    public void M3_FormatAndAuditFindFilesUnderNestedBuildNamedDirectories()
    {
        var root = Fresh("nested-bin");
        var file = Path.Combine(root, "src", "bin", "x.byn");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, "unsafe void poke() {\n}\nlet a = true\nunless !a { print(\"y\") }\n");
        var (_, formatOut, formatErr) = Run("format", "--check", root);
        Assert.Contains("x.byn", formatOut + formatErr);
        var (_, auditOut, _) = Run("audit", root);
        Assert.Contains("unsafe void poke()", auditOut);
    }

    // ── M4 ───────────────────────────────────────────────────────────────────────

    [Fact]
    public void M4_InputsHashDoesNotFollowLinks()
    {
        if (OperatingSystem.IsWindows()) return;
        var root = Fresh("dist");
        Directory.CreateDirectory(Path.Combine(root, "src"));
        File.WriteAllText(Path.Combine(root, "package.byn"), "p\n");
        File.WriteAllText(Path.Combine(root, "src", "main.byn"), "m\n");
        var plain = Dist.InputsHash(root);
        Directory.CreateSymbolicLink(Path.Combine(root, "src", "loop"), "..");
        Assert.Equal(plain, Dist.InputsHash(root));
    }

    private static void WithEnv(string name, string? value, Action body)
    {
        var saved = Environment.GetEnvironmentVariable(name);
        Environment.SetEnvironmentVariable(name, value);
        try
        {
            body();
        }
        finally
        {
            Environment.SetEnvironmentVariable(name, saved);
        }
    }

    // ── M5 ───────────────────────────────────────────────────────────────────────

    [Fact]
    public void M5_TheProgramCacheIsTrimmedLeastRecentlyUsedFirst()
    {
        var root = Fresh("evict");
        WithEnv("BAYAN_CACHE", root, () =>
        {
            var programs = Path.Combine(Toolchain.CacheData, "programs");
            var scripts = Path.Combine(Toolchain.CacheData, "scripts");
            Directory.CreateDirectory(scripts);
            for (var i = 0; i < 6; i++)
            {
                var entry = Path.Combine(programs, "k" + i);
                Directory.CreateDirectory(entry);
                File.WriteAllText(Path.Combine(entry, "program"), "x");
                Directory.SetLastWriteTimeUtc(entry, DateTime.UtcNow.AddHours(-10 + i));
            }
            File.WriteAllText(Path.Combine(scripts, "old"), "k0\n");
            File.WriteAllText(Path.Combine(scripts, "new"), "k5\n");
            Toolchain.Evict("k0", long.MaxValue, 3, TimeSpan.FromMinutes(10));
            var left = Directory.GetDirectories(programs).Select(d => Path.GetFileName(d)!).Order().ToArray();
            // k0 is the one just built, so it stays; the two newest stay; the oldest others go.
            Assert.Equal(["k0", "k4", "k5"], left);
            Assert.True(File.Exists(Path.Combine(scripts, "old")));
            Toolchain.Evict("k5", long.MaxValue, 1, TimeSpan.FromMinutes(10));
            Assert.Equal(["k5"], Directory.GetDirectories(programs).Select(d => Path.GetFileName(d)!).ToArray());
            Assert.False(File.Exists(Path.Combine(scripts, "old")));
            Assert.True(File.Exists(Path.Combine(scripts, "new")));
        });
    }

    [Fact]
    public void M5_AnEntryUsedJustNowIsNotEvicted()
    {
        var root = Fresh("evict-busy");
        WithEnv("BAYAN_CACHE", root, () =>
        {
            var programs = Path.Combine(Toolchain.CacheData, "programs");
            foreach (var key in new[] { "a", "b", "c" }) Directory.CreateDirectory(Path.Combine(programs, key));
            Toolchain.Evict("a", long.MaxValue, 1, TimeSpan.FromMinutes(10));
            Assert.Equal(3, Directory.GetDirectories(programs).Length);
        });
    }

    [Fact]
    public void M5_ABuildTrimsTheCacheToTheCap()
    {
        var root = Fresh("evict-build");
        WithEnv("BAYAN_CACHE", root, () => WithEnv("BAYAN_CACHE_MAX_MB", "1", () =>
        {
            var programs = Path.Combine(Toolchain.CacheData, "programs");
            for (var i = 0; i < 3; i++)
            {
                var entry = Path.Combine(programs, "old" + i);
                Directory.CreateDirectory(entry);
                File.WriteAllBytes(Path.Combine(entry, "program"), new byte[600_000]);
                Directory.SetLastWriteTimeUtc(entry, DateTime.UtcNow.AddHours(-3 + i));
            }
            var build = Toolchain.Build("int main(void) { return 0; }\n", BuildMode.Safe);
            Assert.True(build.Succeeded, build.Errors);
            var left = Directory.GetDirectories(programs).Select(d => Path.GetFileName(d)!).ToArray();
            Assert.Contains(build.Key!, left);
            Assert.DoesNotContain("old0", left);
            Assert.DoesNotContain("old1", left);
        }));
    }

    [Fact]
    public void M5_ABuildTagsTheCacheRootAsACacheDirectory()
    {
        var root = Fresh("cachedir-tag");
        WithEnv("BAYAN_CACHE", root, () =>
        {
            var build = Toolchain.Build("int main(void) { return 1; }\n", BuildMode.Safe);
            Assert.True(build.Succeeded, build.Errors);
            var tag = Path.Combine(root, "CACHEDIR.TAG");
            Assert.StartsWith("Signature: 8a477f597d28d172789f06886806bc55\n", File.ReadAllText(tag));
            Toolchain.RememberScript("s", build.Key!);
            Assert.Single(Directory.GetFiles(root));
        });
    }

    // ── M6 ───────────────────────────────────────────────────────────────────────

    [Fact]
    public void M6_StaleTemporaryDirectoriesAreSweptAndFreshOnesKept()
    {
        var root = Fresh("sweep");
        WithEnv("BAYAN_CACHE", Path.Combine(root, "cache"), () =>
        {
            var temp = Path.Combine(root, "tmp");
            var staging = Path.Combine(Toolchain.CacheData, "tmp");
            var stale = new[] { Path.Combine(temp, "bayan-coverage-old"), Path.Combine(temp, "bayan-mutate-old"), Path.Combine(temp, "bayan-asan-old"), Path.Combine(staging, "key-old") };
            var fresh = new[] { Path.Combine(temp, "bayan-coverage-new"), Path.Combine(staging, "key-new") };
            foreach (var dir in stale.Concat(fresh)) Directory.CreateDirectory(dir);
            foreach (var dir in stale) Directory.SetLastWriteTimeUtc(dir, DateTime.UtcNow.AddHours(-2));
            Toolchain.SweepStale(temp, TimeSpan.FromHours(1));
            Assert.All(stale, d => Assert.False(Directory.Exists(d), d));
            Assert.All(fresh, d => Assert.True(Directory.Exists(d), d));
        });
    }

    // ── M7 ───────────────────────────────────────────────────────────────────────

    [Fact]
    public void M7_AHungCCompilerIsKilledAndTheBuildFails()
    {
        if (OperatingSystem.IsWindows()) return;
        var root = Fresh("cc-timeout");
        var cc = Path.Combine(root, "slowcc");
        File.WriteAllText(cc, "#!/bin/sh\nsleep 1000\n");
        File.SetUnixFileMode(cc, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        WithEnv("BAYAN_CACHE", Path.Combine(root, "cache"), () =>
            WithEnv("BAYAN_CC", cc, () =>
                WithEnv("BAYAN_CC_TIMEOUT_S", "2", () =>
                {
                    var clock = System.Diagnostics.Stopwatch.StartNew();
                    var build = Toolchain.Build("int main(void) { return 0; }\n", BuildMode.Safe);
                    Assert.False(build.Succeeded);
                    Assert.Contains("timed out after 2 s", build.Errors);
                    Assert.True(clock.Elapsed < TimeSpan.FromSeconds(10), clock.Elapsed.ToString());
                })));
    }

    // ── Review fixes ─────────────────────────────────────────────────────────────

    /// <summary>Runs the CLI with a temp root where a watcher deletes every <paramref name="prefix"/> directory as soon as it appears, as another process's sweep would.</summary>
    private static int RunWithSweptWorkDir(string name, string prefix, string source, params string[] args)
    {
        var root = Fresh(name);
        var file = Path.Combine(root, "main.byn");
        File.WriteAllText(file, source);
        var temp = Path.Combine(root, "tmp");
        Directory.CreateDirectory(temp);
        using var stop = new CancellationTokenSource();
        var watcher = Task.Run(() =>
        {
            while (!stop.IsCancellationRequested)
            {
                foreach (var d in Directory.EnumerateDirectories(temp, prefix + "*"))
                    try { Directory.Delete(d, recursive: true); } catch (IOException) { }
                Thread.Sleep(5);
            }
        });
        var exit = 0;
        WithEnv("TMPDIR", temp, () => WithEnv("BAYAN_CACHE", Path.Combine(root, "cache"), () => WithEnv("BAYAN_TEST_TIMEOUT_MS", "2000", () =>
            exit = Run([.. args, file]).Exit)));
        stop.Cancel();
        watcher.Wait();
        return exit;
    }

    [Fact]
    public void F2_ACoverageWorkDirectoryAnotherProcessSweptDoesNotChangeTheExitCode()
    {
        if (OperatingSystem.IsWindows()) return;
        var exit = RunWithSweptWorkDir("swept-coverage", "bayan-coverage-",
            "void main() {\n}\n\ntest \"spin\" {\n    while true {\n    }\n}\n", "test", "--coverage");
        Assert.Equal(CliApp.ExitTestFailure, exit);
    }

    [Fact]
    public void F2_AMutateWorkDirectoryAnotherProcessSweptDoesNotChangeTheExitCode()
    {
        if (OperatingSystem.IsWindows()) return;
        var exit = RunWithSweptWorkDir("swept-mutate", "bayan-mutate-",
            "i64 inc(i64 n) {\n    return n + 1\n}\n\ntest \"inc\" {\n    assert inc(1) == 2\n}\n", "test", "--mutate");
        Assert.Equal(CliApp.ExitOk, exit);
    }

    [Fact]
    public void F13_AStagingDirectoryLivesAsLongAsTheCompileTimeoutPlusTenMinutes()
    {
        var root = Fresh("staging-age");
        WithEnv("BAYAN_CACHE", Path.Combine(root, "cache"), () =>
            WithEnv("BAYAN_CC_TIMEOUT_S", "7200", () =>
            {
                var staging = Path.Combine(Toolchain.CacheData, "tmp");
                var running = Path.Combine(staging, "key-running");
                var dead = Path.Combine(staging, "key-dead");
                foreach (var d in new[] { running, dead }) Directory.CreateDirectory(d);
                Directory.SetLastWriteTimeUtc(running, DateTime.UtcNow.AddMinutes(-125));
                Directory.SetLastWriteTimeUtc(dead, DateTime.UtcNow.AddHours(-3));
                Toolchain.SweepStale(Path.Combine(root, "tmp"), TimeSpan.FromHours(1));
                Assert.True(Directory.Exists(running));
                Assert.False(Directory.Exists(dead));
            }));
    }
}
