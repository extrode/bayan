using Bayan.Cli;

namespace Bayan.Tests;

public sealed class ScaffoldTests
{
    private static (int Exit, string Out, string Err) Run(params string[] args)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var exit = CliApp.Run(args, stdout, stderr);
        return (exit, stdout.ToString().ReplaceLineEndings("\n"), stderr.ToString().ReplaceLineEndings("\n"));
    }

    /// <summary>A fresh, empty directory under <c>tmp/</c>.</summary>
    private static string Fresh(string name)
    {
        Environment.SetEnvironmentVariable("BAYAN_CACHE", Path.Combine(Golden.RepoRoot, "tmp", "cache"));
        var dir = Path.Combine(Golden.RepoRoot, "tmp", "scaffold", name);
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>Every file under <paramref name="root"/> with its contents, sorted by path.</summary>
    private static string Tree(string root)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                                      .Select(f => Path.GetRelativePath(root, f).Replace('\\', '/'))
                                      .Order(StringComparer.Ordinal))
        {
            sb.Append("=== ").Append(file).Append('\n');
            sb.Append(File.ReadAllText(Path.Combine(root, file)));
        }
        return sb.ToString();
    }

    [Theory]
    [InlineData("app", new[] { "--ci", "github" })]
    [InlineData("lib", new[] { "--lib" })]
    [InlineData("script", new[] { "--script" })]
    public void NewWritesTheLayout(string layout, string[] options)
    {
        var dir = Fresh("new-" + layout);
        var (exit, _, stderr) = Run(["new", Path.Combine(dir, "tally"), "--vcs", "none", .. options]);
        Assert.True(exit == CliApp.ExitOk, stderr);
        var result = Golden.Compare(Path.Combine(Golden.CasesRoot, "new", layout + ".tree"), Tree(dir), Golden.UpdateMode);
        if (result.Failure is { } failure) Assert.Fail(failure);
    }

    [Fact]
    public void AFreshAppRunsAndPassesItsTests()
    {
        var app = Path.Combine(Fresh("fresh-app"), "tally");
        Assert.Equal(CliApp.ExitOk, Run("new", app, "--vcs", "none").Exit);
        var stdout = new StringWriter();
        var psiExit = CliApp.Run(["build", "-o", Path.Combine(app, "tally-bin"), app], stdout, stdout);
        Assert.True(psiExit == CliApp.ExitOk, stdout.ToString());
        var psi = new System.Diagnostics.ProcessStartInfo(Path.Combine(app, "tally-bin")) { RedirectStandardOutput = true, UseShellExecute = false };
        using (var p = Utf8Child.Start(psi)!)
        {
            Assert.Equal("Hello, world!\n", p.StandardOutput.ReadToEnd().ReplaceLineEndings("\n"));
            p.WaitForExit();
        }
        var (exit, output, stderr) = Run("test", app);
        Assert.True(exit == CliApp.ExitOk, output + stderr);
        Assert.Contains("1 passed · ", output);
    }

    [Fact]
    public void AFreshLibraryPassesItsTestsButHasNothingToRun()
    {
        var lib = Path.Combine(Fresh("fresh-lib"), "my-lib");
        Assert.Equal(CliApp.ExitOk, Run("new", lib, "--lib", "--vcs", "none").Exit);
        Assert.True(File.Exists(Path.Combine(lib, "src", "myLib", "myLib.byn")));
        var (exit, output, stderr) = Run("test", lib);
        Assert.True(exit == CliApp.ExitOk, output + stderr);
        Assert.Contains("1 passed · ", output);
        Assert.Equal(CliApp.ExitOk, Run("check", lib).Exit);
        var (runExit, _, runErr) = Run("run", lib);
        Assert.Equal(CliApp.ExitUsage, runExit);
        Assert.Contains("`local/my-lib` is a library", runErr);
    }

    [Fact]
    public void AFreshScriptRunsByItself()
    {
        var dir = Fresh("fresh-script");
        File.WriteAllText(Path.Combine(dir, "other.byn"), "this is not Bayan\n");
        Assert.Equal(CliApp.ExitOk, Run("new", Path.Combine(dir, "hi"), "--script").Exit);
        var (exit, _, stderr) = Run(Path.Combine(dir, "hi.byn"));
        Assert.True(exit == CliApp.ExitOk, stderr);
        if (!OperatingSystem.IsWindows())
            Assert.True(File.GetUnixFileMode(Path.Combine(dir, "hi.byn")).HasFlag(UnixFileMode.UserExecute));
    }

    [Fact]
    public void ACamelScriptNameWritesAndChecksTheKebabFile()
    {
        var dir = Fresh("camel-script");
        var target = Path.Combine(dir, "myTool");
        var shown = Path.Combine(dir, "my-tool.byn");
        var (exit, stdout, _) = Run("new", target, "--script");
        Assert.Equal(CliApp.ExitOk, exit);
        Assert.Equal($"created script `{shown}`; run it with `bayan {shown}`\n", stdout);
        Assert.True(File.Exists(shown));
        var (again, _, stderr) = Run("new", target, "--script");
        Assert.Equal(CliApp.ExitUsage, again);
        Assert.Equal($"bayan: `{shown}` already exists\n", stderr);

        var dotted = Path.Combine(Fresh("camel-script-dot"), "myTool");
        var (_, dotOut, _) = Run("new", dotted + Path.DirectorySeparatorChar + ".", "--script");
        Assert.Equal($"created script `{Path.Combine(Path.GetDirectoryName(dotted)!, "my-tool.byn")}`; run it with `bayan {Path.Combine(Path.GetDirectoryName(dotted)!, "my-tool.byn")}`\n", dotOut);
    }

    [Fact]
    public void InitAddsOnlyMissingFilesAndListsWhatItSkipped()
    {
        var dir = Path.Combine(Fresh("init"), "tally");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "README.md"), "# mine\n");
        var (exit, stdout, _) = Run("init", dir, "--vcs", "none");
        Assert.Equal(CliApp.ExitOk, exit);
        Assert.Contains("skipped README.md (already exists)\n", stdout);
        Assert.Contains("created package.byn\n", stdout);
        Assert.Equal("# mine\n", File.ReadAllText(Path.Combine(dir, "README.md")));
        Assert.Equal(CliApp.ExitOk, Run("check", dir).Exit);

        var (again, _, stderr) = Run("init", dir);
        Assert.Equal(CliApp.ExitUsage, again);
        Assert.Contains("already has a `package.byn`", stderr);
    }

    [Fact]
    public void NewRefusesANonEmptyDirectory()
    {
        var dir = Fresh("non-empty");
        File.WriteAllText(Path.Combine(dir, "x.txt"), "");
        var (exit, _, stderr) = Run("new", dir);
        Assert.Equal(CliApp.ExitUsage, exit);
        Assert.Contains("run `bayan init` inside it", stderr);
    }

    [Theory]
    [InlineData("My_App", "can't be a package name")]
    [InlineData("2fast", "can't be a package name")]
    [InlineData("my--app", "can't be a package name")]
    public void NewRejectsBadNames(string name, string message)
    {
        var (exit, _, stderr) = Run("new", Path.Combine(Fresh("bad-" + name.Replace("-", "")), name), "--vcs", "none");
        Assert.Equal(CliApp.ExitUsage, exit);
        Assert.Contains(message, stderr);
        Assert.Contains("help: use lowercase words joined by `-`", stderr);
    }

    [Fact]
    public void NewAcceptsCamelCaseAndNamesThePackageInKebabCase()
    {
        var dir = Path.Combine(Fresh("camel"), "myApp");
        Assert.Equal(CliApp.ExitOk, Run("new", dir, "--vcs", "none").Exit);
        Assert.Contains("name: \"local/my-app\"", File.ReadAllText(Path.Combine(dir, "package.byn")));
    }

    [Fact]
    public void ALibraryNamedAfterAKeywordIsRejected()
    {
        var (exit, _, stderr) = Run("new", Path.Combine(Fresh("keyword"), "test"), "--lib", "--vcs", "none");
        Assert.Equal(CliApp.ExitUsage, exit);
        Assert.Contains("`test` is a keyword", stderr);
    }

    [Fact]
    public void NewInsideAGitWorkTreeDoesNotInitAnother()
    {
        var dir = Path.Combine(Fresh("git-inside"), "tally");
        var (exit, stdout, _) = Run("new", dir);
        Assert.Equal(CliApp.ExitOk, exit);
        Assert.Contains("already inside a git repository", stdout);
        Assert.False(Directory.Exists(Path.Combine(dir, ".git")));
    }

    [Fact]
    public void NewOutsideAGitWorkTreeInitsOneWithoutCommitting()
    {
        if (Bayan.Backend.C.Toolchain.FindOnPath("git") is null) return;
        var outside = Path.Combine(Path.GetTempPath(), "bayan-scaffold-" + Guid.NewGuid().ToString("N"));
        try
        {
            var dir = Path.Combine(outside, "tally");
            var (exit, stdout, _) = Run("new", dir);
            Assert.Equal(CliApp.ExitOk, exit);
            Assert.Contains("initialized a git repository", stdout);
            Assert.True(Directory.Exists(Path.Combine(dir, ".git")));
            Assert.Empty(Directory.GetFiles(Path.Combine(dir, ".git", "refs", "heads")));
        }
        finally
        {
            if (Directory.Exists(outside)) Directory.Delete(outside, recursive: true);
        }
    }

    [Fact]
    public void ABadManifestStopsTheBuild()
    {
        var dir = Path.Combine(Fresh("bad-manifest"), "tally");
        Assert.Equal(CliApp.ExitOk, Run("new", dir, "--vcs", "none").Exit);
        File.WriteAllText(Path.Combine(dir, "package.byn"), "Package { name: \"local/tally\" }\n");
        var (exit, _, stderr) = Run("check", dir);
        Assert.Equal(CliApp.ExitFailure, exit);
        Assert.Contains("error BYN3103: `package.byn` has no `version`", stderr);
    }
}
