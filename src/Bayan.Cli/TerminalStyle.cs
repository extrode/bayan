using Bayan.Syntax.Diagnostics;

namespace Bayan.Cli;

/// <summary>Decides colour, palette, symbols and width for one output stream (spec 005 R1–R3, R16).</summary>
public static class TerminalStyle
{
    /// <param name="colorFlag"><c>auto</c>, <c>always</c> or <c>never</c> from <c>--color</c>.</param>
    /// <param name="ascii">Whether <c>--ascii</c> was given.</param>
    /// <param name="isTty">Whether the stream is a terminal.</param>
    /// <param name="env">Reads an environment variable.</param>
    /// <param name="width">The terminal's width; 0 when unknown.</param>
    public static Style Decide(string colorFlag, bool ascii, bool isTty, Func<string, string?> env, int width)
    {
        var dumb = env("TERM") == "dumb";
        var color = colorFlag switch
        {
            "always" => true,
            "never" => false,
            _ when env("NO_COLOR") is { Length: > 0 } => false,
            _ when env("FORCE_COLOR") is { Length: > 0 } force && force != "0" => true,
            _ when dumb => false,
            _ => isTty,
        };
        var palette = !color ? null
            : env("COLORTERM") is "truecolor" or "24bit" ? (Light(env) ? Palette.TruecolorLight : Palette.TruecolorDark)
            : Palette.Ansi16;
        var symbols = ascii || dumb || !Utf8(env) ? Symbols.Ascii : Symbols.Unicode;
        return new Style(palette, symbols, isTty && width > 0 ? width : 100);
    }

    /// <summary><c>BAYAN_THEME</c>, else the background field of <c>COLORFGBG</c>, else dark.</summary>
    private static bool Light(Func<string, string?> env)
    {
        switch (env("BAYAN_THEME"))
        {
            case "light": return true;
            case "dark": return false;
        }
        var fgbg = env("COLORFGBG");
        if (fgbg is null || !int.TryParse(fgbg[(fgbg.LastIndexOf(';') + 1)..], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var bg)) return false;
        return bg is 7 or (>= 9 and <= 15);
    }

    /// <summary>The first non-empty of <c>LC_ALL</c>, <c>LC_CTYPE</c>, <c>LANG</c> names UTF-8 (none set counts as UTF-8); on Windows, code page 65001.</summary>
    private static bool Utf8(Func<string, string?> env)
    {
        if (OperatingSystem.IsWindows())
        {
            try
            {
                return Console.OutputEncoding.CodePage == 65001;
            }
            catch (IOException)
            {
                return true;
            }
        }
        foreach (var name in (string[])["LC_ALL", "LC_CTYPE", "LANG"])
        {
            if (env(name) is not { Length: > 0 } locale) continue;
            return locale.Contains("UTF-8", StringComparison.OrdinalIgnoreCase) || locale.Contains("utf8", StringComparison.OrdinalIgnoreCase);
        }
        return true;
    }
}
