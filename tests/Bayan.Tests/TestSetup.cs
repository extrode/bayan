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
        if (OperatingSystem.IsWindows()) SetConsoleCodePage(65001);
    }

    /// <summary>Windows picks the Unicode or ASCII symbols by console code page, not locale (TerminalStyle.Utf8); a CI runner's is 437.</summary>
    internal static void SetConsoleCodePage(int codePage)
    {
        try
        {
            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
            Console.OutputEncoding = System.Text.Encoding.GetEncoding(codePage);
        }
        catch (IOException)
        {
        }
    }
}
