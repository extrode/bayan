using System.Runtime.CompilerServices;

namespace Bayan.Tests;

internal static class TestSetup
{
    /// <summary>Tests never touch the user's real build cache.</summary>
    [ModuleInitializer]
    internal static void UseRepoCache() =>
        Environment.SetEnvironmentVariable("BAYAN_CACHE", Path.Combine(Golden.RepoRoot, "tmp", "cache"));

    /// <summary>CLI output does not depend on the developer's colour, terminal or locale settings.</summary>
    [ModuleInitializer]
    internal static void UseNeutralTerminal()
    {
        foreach (var name in (string[])["NO_COLOR", "FORCE_COLOR", "TERM", "COLORTERM", "BAYAN_THEME", "COLORFGBG", "LC_ALL", "LC_CTYPE"])
            Environment.SetEnvironmentVariable(name, null);
        Environment.SetEnvironmentVariable("LANG", "en_US.UTF-8");
    }
}
