using Bayan.Backend.C;
using Bayan.Cli;

namespace Bayan.Tests;

/// <summary>Each test gets its own cache, so these run alone (they change <c>BAYAN_CACHE</c>).</summary>
[Collection(nameof(ToolchainEnvironment))]
public sealed class CacheTests
{
    private static string Root(string name)
    {
        var dir = Path.Combine(Golden.RepoRoot, "tmp", "cache-tests", name);
        TestDirs.Delete(dir);
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>Runs <paramref name="body"/> with an empty cache of its own and the given variables set.</summary>
    private static void WithCache(string root, Action body, params (string Name, string? Value)[] variables)
    {
        var all = variables.Append(("BAYAN_CACHE", Path.Combine(root, "cache"))).ToArray();
        var saved = all.Select(v => (v.Item1, Environment.GetEnvironmentVariable(v.Item1))).ToArray();
        foreach (var (name, value) in all) Environment.SetEnvironmentVariable(name, value);
        try
        {
            body();
        }
        finally
        {
            foreach (var (name, value) in saved) Environment.SetEnvironmentVariable(name, value);
        }
    }

    private static (int Exit, string Out, string Err) Run(params string[] args)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var exit = CliApp.Run(args, stdout, stderr);
        return (exit, stdout.ToString().ReplaceLineEndings("\n"), stderr.ToString().ReplaceLineEndings("\n"));
    }

    private static void Ok((int Exit, string Out, string Err) r) => Assert.True(r.Exit == CliApp.ExitOk, r.Out + r.Err);

    private static string NewApp(string dir)
    {
        var app = Path.Combine(dir, "tally");
        Ok(Run("new", app, "--vcs", "none"));
        return app;
    }

    private static string Output(string executable)
    {
        var psi = new System.Diagnostics.ProcessStartInfo(executable) { RedirectStandardOutput = true, StandardOutputEncoding = System.Text.Encoding.UTF8, UseShellExecute = false };
        using var p = System.Diagnostics.Process.Start(psi)!;
        var text = p.StandardOutput.ReadToEndLf();
        p.WaitForExit();
        return text;
    }

    private static string Exe(string project, string mode = "safe") => Path.Combine(project, "out", mode, Targets.Host!, Targets.ExecutableName("tally", CliApp.HostTarget));

    private static (int Code, string Out) Git(string dir, params string[] args) =>
        Scaffold.Git(Toolchain.FindOnPath("git")!, dir, ["-c", "core.hooksPath=" + Path.Combine(dir, ".no-hooks"), "-c", "user.name=test", "-c", "user.email=test@example.com", "-c", "commit.gpgsign=false", .. args]);

    [Fact]
    public void TheDefaultLocationIsThePlatformCacheDirectory()
    {
        string?[] saved = [Environment.GetEnvironmentVariable("BAYAN_CACHE"), Environment.GetEnvironmentVariable("XDG_CACHE_HOME")];
        try
        {
            Environment.SetEnvironmentVariable("BAYAN_CACHE", null);
            Environment.SetEnvironmentVariable("XDG_CACHE_HOME", null);
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (OperatingSystem.IsMacOS()) Assert.Equal(Path.Combine(home, "Library", "Caches", "bayan"), Toolchain.CacheDirectory);
            else if (OperatingSystem.IsWindows()) Assert.EndsWith(Path.Combine("bayan", "cache"), Toolchain.CacheDirectory);
            else
            {
                Assert.Equal(Path.Combine(home, ".cache", "bayan"), Toolchain.CacheDirectory);
                Environment.SetEnvironmentVariable("XDG_CACHE_HOME", "/xdg");
                Assert.Equal("/xdg/bayan", Toolchain.CacheDirectory);
                Environment.SetEnvironmentVariable("XDG_CACHE_HOME", "relative");
                Assert.Equal(Path.Combine(home, ".cache", "bayan"), Toolchain.CacheDirectory);
            }
            Environment.SetEnvironmentVariable("BAYAN_CACHE", "/elsewhere");
            Assert.Equal("/elsewhere", Toolchain.CacheDirectory);
        }
        finally
        {
            Environment.SetEnvironmentVariable("BAYAN_CACHE", saved[0]);
            Environment.SetEnvironmentVariable("XDG_CACHE_HOME", saved[1]);
        }
    }

    [Fact]
    public void ASecondWorktreeCompilesNothing()
    {
        if (Toolchain.FindOnPath("git") is null) return;
        var root = Root("worktrees");
        var app = NewApp(root);
        Assert.Equal(0, Git(app, "init", "--quiet", "-b", "work").Code);
        Assert.Equal(0, Git(app, "add", "-A").Code);
        Assert.Equal(0, Git(app, "commit", "--quiet", "-m", "start").Code);
        var second = Path.Combine(root, "second");
        Assert.Equal(0, Git(app, "worktree", "add", "--quiet", "-b", "second", second).Code);
        WithCache(root, () =>
        {
            Ok(Run("build", app));
            Ok(Run("test", app));
            var before = Toolchain.Stats();
            Assert.Equal(2, before.Programs);
            Ok(Run("build", second));
            Ok(Run("test", second));
            Assert.Equal(before, Toolchain.Stats());
            Assert.Equal("Hello, world!\n", Output(Exe(second)));
        });
    }

    [Fact]
    public void MovingAProjectKeepsItsCacheHits()
    {
        var root = Root("move");
        var app = NewApp(root);
        WithCache(root, () =>
        {
            Ok(Run("build", app, "--mode", "debug"));
            var before = Toolchain.Stats();
            var moved = Path.Combine(root, "elsewhere", "renamed");
            Directory.CreateDirectory(Path.GetDirectoryName(moved)!);
            Directory.Move(app, moved);
            Ok(Run("build", moved, "--mode", "debug"));
            Assert.Equal(before, Toolchain.Stats());
        });
    }

    [Fact]
    public void ACallerIsRecheckedWhenACalleeStartsOrStopsStoringAnArgument()
    {
        var root = Root("callee-summary");
        var app = NewApp(root);
        var callee = Path.Combine(app, "src", "keep", "keep.byn");
        Directory.CreateDirectory(Path.GetDirectoryName(callee)!);
        const string copies = "public void pass(string s, to ref string g) {\n    g = \"\"\n}\n";
        const string stores = "public void pass(string s, to ref string g) {\n    g = s\n}\n";
        File.WriteAllText(callee, copies);
        File.WriteAllText(Path.Combine(app, "src", "app", "main.byn"),
            "import keep\n\nvar kept = \"\"\n{\n    using let arena = Arena()\n    keep.pass(\"ann\".copy(to: arena), to: ref kept)\n}\nprint(kept)\n");
        WithCache(root, () =>
        {
            Ok(Run("build", app));
            File.WriteAllText(callee, stores);
            var refused = Run("build", app);
            Assert.Equal(CliApp.ExitFailure, refused.Exit);
            Assert.Contains("BYN3002", refused.Out + refused.Err);
            File.WriteAllText(callee, copies);
            Ok(Run("build", app));
        });
    }

    [Fact]
    public void ACallerIsRecheckedWhenACalleeStartsOrStopsReturningAnArgument()
    {
        var root = Root("callee-return-summary");
        var app = NewApp(root);
        var callee = Path.Combine(app, "src", "keep", "keep.byn");
        Directory.CreateDirectory(Path.GetDirectoryName(callee)!);
        const string fresh = "public string pass(string s) {\n    return \"\"\n}\n";
        const string returns = "public string pass(string s) {\n    return s\n}\n";
        File.WriteAllText(callee, fresh);
        File.WriteAllText(Path.Combine(app, "src", "app", "main.byn"),
            "import keep\n\nvar kept = \"\"\n{\n    using let arena = Arena()\n    kept = keep.pass(\"ann\".copy(to: arena))\n}\nprint(kept)\n");
        WithCache(root, () =>
        {
            Ok(Run("build", app));
            File.WriteAllText(callee, returns);
            var refused = Run("build", app);
            Assert.Equal(CliApp.ExitFailure, refused.Exit);
            Assert.Contains("BYN3002", refused.Out + refused.Err);
            File.WriteAllText(callee, fresh);
            Ok(Run("build", app));
        });
    }

    [Fact]
    public void TheWorkingDirectoryDoesNotChangeTheCacheKey()
    {
        var root = Root("cwd");
        var app = NewApp(root);
        var saved = Environment.CurrentDirectory;
        try
        {
            Environment.CurrentDirectory = app;
            var fromRoot = Run("build", app, "--emit-c").Out;
            Environment.CurrentDirectory = Path.Combine(app, "src");
            Assert.Equal(fromRoot, Run("build", app, "--emit-c").Out);
            Assert.Contains("\"src/app/main.byn\"", fromRoot);
            Assert.DoesNotContain(Golden.RepoRoot, fromRoot);
        }
        finally
        {
            Environment.CurrentDirectory = saved;
        }
    }

    [Fact]
    public void ParallelBuildsNeverCorruptTheCache()
    {
        var root = Root("parallel");
        var a = NewApp(Path.Combine(root, "a"));
        var b = NewApp(Path.Combine(root, "b"));
        WithCache(root, () =>
        {
            var results = new (int Exit, string Out, string Err)[24];
            Parallel.For(0, results.Length, new ParallelOptions { MaxDegreeOfParallelism = 8 }, i =>
                results[i] = Run("build", i % 2 == 0 ? a : b, "--mode", i % 3 == 0 ? "debug" : "safe"));
            foreach (var r in results) Ok(r);
            var stats = Toolchain.Stats();
            Assert.Equal(2, stats.Programs);
            Assert.False(Directory.Exists(Path.Combine(root, "cache", "v1", "tmp")) && Directory.EnumerateFileSystemEntries(Path.Combine(root, "cache", "v1", "tmp")).Any());
            foreach (var project in new[] { a, b })
                foreach (var mode in new[] { "debug", "safe" })
                    Assert.Equal("Hello, world!\n", Output(Exe(project, mode)));
        });
    }

    /// <summary>
    /// On Windows, ReplaceFile retries when another process holds the destination open with FileShare.Read|Delete.
    /// The reproduction runs on Windows; on non-Windows, the test runs ReplaceFile and asserts the content is replaced.
    /// </summary>
    [Fact]
    public void ReplaceFileRetriesWhileDestinationIsOpen()
    {
        var root = Root("replace-open");
        var path = Path.Combine(root, "target.txt");
        File.WriteAllText(path, "before");
        if (OperatingSystem.IsWindows())
        {
            using var open = new ManualResetEventSlim();
            var thread = new Thread(() =>
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
                open.Set();
                Thread.Sleep(200);
            });
            thread.Start();
            open.Wait();
            Toolchain.ReplaceFile(path, temp => File.WriteAllText(temp, "after"));
            thread.Join();
        }
        else
        {
            Toolchain.ReplaceFile(path, temp => File.WriteAllText(temp, "after"));
        }
        Assert.Equal("after", File.ReadAllText(path));
    }

    [Fact]
    public void ADamagedEntryIsRebuilt()
    {
        var root = Root("damaged");
        var app = NewApp(root);
        WithCache(root, () =>
        {
            Ok(Run("build", app));
            var entry = Directory.EnumerateDirectories(Path.Combine(root, "cache", "v1", "programs")).Single();
            var program = Directory.EnumerateFiles(entry).Single(f => !f.EndsWith(".sha256", StringComparison.Ordinal));
            File.WriteAllBytes(program, [1, 2, 3]);
            Ok(Run("build", app));
            Assert.Equal("Hello, world!\n", Output(Exe(app)));
            Assert.Equal(1, Toolchain.Stats().Programs);
        });
    }

    [Fact]
    public void ARememberedScriptStartsWithoutCompiling()
    {
        var root = Root("script");
        var script = Path.Combine(root, "hello.byn");
        File.WriteAllText(script, "#!/usr/bin/env bayan\n#:io threaded\n\nprint(\"hi\")\n");
        WithCache(root, () =>
        {
            Ok(Run(script));
            Assert.Equal(1, Toolchain.Stats().Scripts);
        });
        var noCompiler = ("BAYAN_CC", Path.Combine(root, "no-such-cc"));
        WithCache(root, () => Ok(Run(script)), noCompiler);
        File.AppendAllText(script, "print(\"changed\")\n");
        WithCache(root, () => Assert.Equal(CliApp.ExitToolchain, Run(script).Exit), noCompiler);
    }

    [Fact]
    public void CacheStatsAndCleanCache()
    {
        var root = Root("stats");
        var app = NewApp(root);
        WithCache(root, () =>
        {
            Ok(Run("build", app));
            var (exit, stdout, _) = Run("cache", "stats");
            Assert.Equal(CliApp.ExitOk, exit);
            Assert.StartsWith($"cache:    {Path.Combine(root, "cache")}\nprograms: 1 (", stdout);
            Assert.EndsWith("scripts:  0\n", stdout);
            Assert.Equal(CliApp.ExitUsage, Run("cache").Exit);
            var saved = Environment.CurrentDirectory;
            try
            {
                Environment.CurrentDirectory = root;
                Ok(Run("clean", "--cache"));
            }
            finally
            {
                Environment.CurrentDirectory = saved;
            }
            Assert.Equal(0, Toolchain.Stats().Programs);
            Assert.True(File.Exists(Exe(app)));
        });
    }

    [Fact]
    public void CleanCacheRemovesOnlyCacheData()
    {
        var root = Root("clean-only-data");
        var app = NewApp(root);
        var unrelated = Path.Combine(root, "cache", "keep.txt");
        WithCache(root, () =>
        {
            Ok(Run("build", app));
            File.WriteAllText(unrelated, "not bayan's");
            var (exit, stdout, _) = Run("clean", "--cache", app);
            Assert.Equal(CliApp.ExitOk, exit);
            Assert.Contains($"removed {Toolchain.CacheData}", stdout);
            Assert.False(Directory.Exists(Toolchain.CacheData));
            Assert.True(File.Exists(unrelated));
        });
    }

    [Fact]
    public void ScriptKeysDependOnTheCompilerBuild()
    {
        byte[] script = "print(\"hi\")\n"u8.ToArray();
        Assert.NotEqual(Toolchain.ScriptKey(script, BuildMode.Safe, "a"), Toolchain.ScriptKey(script, BuildMode.Safe, "b"));
        Assert.Equal(Toolchain.ScriptKey(script, BuildMode.Safe, "a"), Toolchain.ScriptKey(script, BuildMode.Safe, "a"));
        Assert.NotEmpty(Toolchain.CompilerBuild);
    }
}
