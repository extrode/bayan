namespace Bayan.Syntax.Diagnostics;

public enum Role
{
    Error,
    Warning,
    Help,
    Success,
    Note,
}

/// <summary>SGR prefixes per role; colours come from <c>docs/bayan-terminal-style.md</c> §2.</summary>
public sealed record Palette(string Error, string Warning, string Help, string Success, string Note)
{
    /// <summary>(role, light, dark) hex values; the contrast test checks these against the guide.</summary>
    public static readonly (string Role, string Light, string Dark)[] Hex =
    [
        ("Error", "#B42318", "#FF8A80"),
        ("Warning", "#8A5300", "#F2C14E"),
        ("Help", "#1F3A5F", "#8DB8F2"),
        ("Success", "#2F6B1F", "#8FD694"),
        ("Note", "#5F5E5A", "#B4B2A9"),
    ];

    public static readonly Palette TruecolorLight = Truecolor(1);
    public static readonly Palette TruecolorDark = Truecolor(2);
    public static readonly Palette Ansi16 = new("\e[31m", "\e[33m", "\e[34m", "\e[32m", "\e[2m");

    public string Prefix(Role role) => role switch
    {
        Role.Error => Error,
        Role.Warning => Warning,
        Role.Help => Help,
        Role.Success => Success,
        _ => Note,
    };

    private static Palette Truecolor(int column) => new(
        Sgr(Hex[0], column), Sgr(Hex[1], column), Sgr(Hex[2], column), Sgr(Hex[3], column), Sgr(Hex[4], column));

    private static string Sgr((string Role, string Light, string Dark) row, int column)
    {
        var hex = column == 1 ? row.Light : row.Dark;
        var r = Convert.ToInt32(hex.Substring(1, 2), 16);
        var g = Convert.ToInt32(hex.Substring(3, 2), 16);
        var b = Convert.ToInt32(hex.Substring(5, 2), 16);
        return $"\e[38;2;{r};{g};{b}m";
    }
}

public sealed record Symbols(string Dot, string Cross, string Bar, string Caret, string Ellipsis, string[] Progress)
{
    public static readonly Symbols Unicode = new("◆", "✗", "│", "^", "…", ["◆◇◇", "◇◆◇", "◇◇◆"]);
    public static readonly Symbols Ascii = new("*", "x", "|", "^", "...", ["..."]);
}

/// <summary>How output is painted: optional colours, symbol set and wrap width.</summary>
public sealed record Style(Palette? Colors, Symbols Symbols, int Width)
{
    public static readonly Style Plain = new(null, Symbols.Unicode, 100);

    public string Paint(Role role, string text) =>
        Colors is null ? text : Colors.Prefix(role) + text + "\e[0m";
}
