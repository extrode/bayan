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

/// <summary>Test scratch directories.</summary>
internal static class TestDirs
{
    /// <summary>Deletes <paramref name="dir"/> if it exists. git writes its objects read-only, and on Windows <c>Directory.Delete</c> refuses a read-only file, so a second run could not clear a repo the first one left.</summary>
    internal static void Delete(string dir)
    {
        if (!Directory.Exists(dir)) return;
        foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
        Directory.Delete(dir, recursive: true);
    }
}

/// <summary>Child-process text. Only on Windows, where the C runtime's text-mode stdout turns every <c>\n</c> into <c>\r\n</c> (Q742), is <c>\r\n</c> read as <c>\n</c> (the <c>Golden.Compare</c> rule); elsewhere the bytes are kept, so a lost or added <c>\r</c> is still caught.</summary>
internal static class ProcessText
{
    internal static string Lf(string text) => OperatingSystem.IsWindows() ? text.Replace("\r\n", "\n") : text;

    /// <summary>The same rule for bytes, for output that may not be valid UTF-8 and so is read raw.</summary>
    internal static byte[] Lf(byte[] bytes)
    {
        if (!OperatingSystem.IsWindows()) return bytes;
        var kept = new List<byte>(bytes.Length);
        for (var i = 0; i < bytes.Length; i++)
            if (!(bytes[i] == '\r' && i + 1 < bytes.Length && bytes[i + 1] == '\n')) kept.Add(bytes[i]);
        return kept.ToArray();
    }

    internal static string ReadToEndLf(this StreamReader reader) => Lf(reader.ReadToEnd());

    internal static async Task<string> ReadToEndLfAsync(this StreamReader reader) => Lf(await reader.ReadToEndAsync());
}

/// <summary>
/// The exit code of a panic (spec §7.6/§9). The runtime ends a panic with <c>abort()</c>: the shell reports SIGABRT as 134 (128 + 6), but on
/// Windows the C runtime's <c>abort()</c> is a fast-fail, so the process exit code is STATUS_FAIL_FAST_EXCEPTION (0xC0000409).
/// </summary>
internal static class PanicExit
{
    internal const int Windows = unchecked((int)0xC0000409);

    internal static readonly int Code = OperatingSystem.IsWindows() ? Windows : 134;

    /// <summary>The code in the 134 form the golden <c>.exitcode</c> files and <c>CliApp</c> use; every other code, and 134 on other systems, is kept.</summary>
    internal static int Portable(int code) => OperatingSystem.IsWindows() && code == Windows ? 134 : code;
}
