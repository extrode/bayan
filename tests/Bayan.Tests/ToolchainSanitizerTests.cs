using Bayan.Backend.C;

namespace Bayan.Tests;

/// <summary>The AddressSanitizer probe and the compiler named in a failed start.</summary>
[Collection(nameof(ToolchainEnvironment))]
public sealed class ToolchainSanitizerTests : IDisposable
{
    private readonly string dir = Path.Combine(Golden.RepoRoot, "tmp", "asan-tests", Guid.NewGuid().ToString("N"));

    public ToolchainSanitizerTests() => Directory.CreateDirectory(dir);

    public void Dispose()
    {
        Toolchain.SanitizerOverride = null;
        Directory.Delete(dir, recursive: true);
    }

    private string Script(string name, string body)
    {
        var path = Path.Combine(dir, name);
        File.WriteAllText(path, "#!/bin/sh\n" + body + "\n");
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return path;
    }

    [Fact]
    public void ProbeRejectsACompilerThatFailsAndTakesTheNextOne()
    {
        if (OperatingSystem.IsWindows()) return;
        var failing = Script("failing.sh", "exit 1");
        var missing = Path.Combine(dir, "missing");
        Assert.Null(Toolchain.ProbeSanitizer([[failing], [missing]]));
        // A "compiler" that succeeds without producing a program does not pass either.
        Assert.Null(Toolchain.ProbeSanitizer([[Script("empty.sh", "exit 0")]]));
        var working = Script("working.sh", "while [ $# -gt 0 ]; do if [ \"$1\" = -o ]; then printf '#!/bin/sh\\nexit 0\\n' > \"$2\"; chmod +x \"$2\"; fi; shift; done");
        Assert.Equal(working, Assert.Single(Toolchain.ProbeSanitizer([[failing], [working]])!));
    }

    [Fact]
    public void ProbeIsCachedAndAvailabilityFollowsIt()
    {
        Assert.Equal(Toolchain.SanitizerCompiler is not null, Toolchain.AsanAvailable);
        Assert.Same(Toolchain.SanitizerCompiler, Toolchain.SanitizerCompiler);
    }

    [Fact]
    public void StartFailureNamesTheSanitizerCompilerThatWasStarted()
    {
        var missing = Path.Combine(dir, "no-such-clang");
        Toolchain.SanitizerOverride = [missing];
        var build = Toolchain.Build("int main(void) { return 0; }\n", BuildMode.Debug, sanitize: true);
        Assert.False(build.Succeeded);
        Assert.Contains($"`{missing}`", build.Errors);
    }
}
