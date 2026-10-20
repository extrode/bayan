using System.Runtime.CompilerServices;

namespace Bayan.Tests;

internal static class TestSetup
{
    /// <summary>Tests never touch the user's real build cache.</summary>
    [ModuleInitializer]
    internal static void UseRepoCache() =>
        Environment.SetEnvironmentVariable("BAYAN_CACHE", Path.Combine(Golden.RepoRoot, "tmp", "cache"));
}
