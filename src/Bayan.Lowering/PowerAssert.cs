using System.Text;

namespace Bayan.Lowering;

/// <summary>
/// The text of a failed <c>assert</c> (HANDOFF §16, plan J5): the statement on one line, a row of bars under the
/// first character of each shown sub-expression, then the values hung from the bars, rightmost first.
/// </summary>
public static class PowerAssert
{
    /// <summary>The column bar; the CLI draws <c>|</c> in its ASCII form.</summary>
    public const char Bar = '│';

    /// <summary>Starts every diagram line, so the bars sit under the statement after the runtime's <c>panic: </c>.</summary>
    public const string Indent = "       ";

    /// <summary>Source text on one line: each line break, with the blanks around it, becomes one space.</summary>
    public static string Flatten(string text)
    {
        if (!text.Contains('\n')) return text;
        var lines = text.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            if (i > 0) lines[i] = lines[i].TrimStart(' ', '\t');
            if (i < lines.Length - 1) lines[i] = lines[i].TrimEnd(' ', '\t', '\r');
        }
        return string.Join(' ', lines);
    }

    /// <summary>The column of the character after <paramref name="before"/>, the statement's text up to it.</summary>
    public static int Column(string before) => Flatten(before).EnumerateRunes().Count();

    /// <summary>
    /// For ascending, distinct <paramref name="columns"/>: the row of bars, and one prefix per column, rightmost first,
    /// holding the bars to its left and padded to the column, after which the value is written.
    /// </summary>
    public static (string Bars, string[] Prefixes) Layout(IReadOnlyList<int> columns)
    {
        var prefixes = new string[columns.Count];
        for (var k = columns.Count - 1; k >= 0; k--)
            prefixes[columns.Count - 1 - k] = Bars(columns, k).PadRight(columns[k]);
        return (Bars(columns, columns.Count), prefixes);
    }

    private static string Bars(IReadOnlyList<int> columns, int count)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < count; i++) sb.Append(' ', columns[i] - sb.Length).Append(Bar);
        return sb.ToString();
    }
}
