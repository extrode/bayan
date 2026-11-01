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
        if (!OperatingSystem.IsWindows()) return;
        var original = Console.OutputEncoding.CodePage;
        SetConsoleCodePage(65001);
        AppDomain.CurrentDomain.ProcessExit += (_, _) => SetConsoleCodePage(original); // the console is the developer's shell's
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

/// <summary>Child-process text. Only on Windows, where the C runtime's text-mode stdout turns every <c>\n</c> into <c>\r\n</c> (Q742), is <c>\r\n</c> read as <c>\n</c> (the <c>Golden.Compare</c> rule); elsewhere the bytes are kept, so a lost or added <c>\r</c> is still caught.</summary>
internal static class ProcessText
{
    internal static string Lf(string text) => OperatingSystem.IsWindows() ? text.Replace("\r\n", "\n") : text;

    internal static string ReadToEndLf(this StreamReader reader) => Lf(reader.ReadToEnd());

    internal static async Task<string> ReadToEndLfAsync(this StreamReader reader) => Lf(await reader.ReadToEndAsync());
}
