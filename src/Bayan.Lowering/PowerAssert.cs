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
    /// One piece of the diagram: <see cref="Text"/>, or the value of column <see cref="Value"/> when that is not -1. It is
    /// written when every column in <see cref="When"/> is shown, none in <see cref="Unless"/> is, and, when
    /// <see cref="AnyFrom"/> is set, some column from that index on is.
    /// </summary>
    public sealed record Piece(string Text, int Value, int[] When, int[] Unless, int? AnyFrom);

    /// <summary>
    /// The diagram after the statement, for ascending, distinct <paramref name="columns"/>: a row of bars, then each value
    /// hung from its bar, rightmost first, under the bars to its left. A column that is not <paramref name="always"/>
    /// shown (a side that may not run) is shown only when its value was computed; the guards of the pieces say so, and
    /// name only such columns.
    /// </summary>
    public static List<Piece> Diagram(IReadOnlyList<int> columns, IReadOnlyList<bool> always)
    {
        var pieces = new List<Piece>();
        void Add(string text, int value, int[] when, int[] unless, int? anyFrom)
        {
            if (anyFrom is { } from)
            {
                if (from >= columns.Count) return;
                if (Enumerable.Range(from, columns.Count - from).Any(j => always[j])) anyFrom = null;
            }
            pieces.Add(new(text, value, when.Where(k => !always[k]).ToArray(), unless, anyFrom));
        }
        string Gap(int k) => new(' ', columns[k] - (k == 0 ? 0 : columns[k - 1] + 1));

        // A missing column leaves a blank where its bar would be, but never at the end of the row.
        Add("\n" + Indent, -1, [], [], 0);
        for (var k = 0; k < columns.Count; k++)
        {
            Add(Gap(k) + Bar, -1, [k], [], null);
            if (!always[k]) Add(Gap(k) + " ", -1, [], [k], k + 1);
        }
        for (var k = columns.Count - 1; k >= 0; k--)
        {
            Add("\n" + Indent, -1, [k], [], null);
            for (var j = 0; j < k; j++)
            {
                Add(Gap(j) + Bar, -1, [k, j], [], null);
                if (!always[j]) Add(Gap(j) + " ", -1, [k], [j], null);
            }
            Add(Gap(k), -1, [k], [], null);
            Add("", k, [k], [], null);
        }
        return pieces;
    }
}
