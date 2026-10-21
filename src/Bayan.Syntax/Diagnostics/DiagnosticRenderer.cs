using System.Globalization;
using System.Text;
using Bayan.Syntax.Text;

namespace Bayan.Syntax.Diagnostics;

/// <summary>
/// Renders a diagnostic as text, optionally with ANSI colors that add nothing else. The format is
/// snapshot-tested; change it deliberately.
/// <code>
/// error BYN2104: `total` is immutable
///
///   12 │     total += line.price;
///      │     ^^^^^ declared with `let` on line 9
///      │
///   help: declare it with `var` if it should change
///      │  9 │     var total = 0;
///      │    │     +++
/// </code>
/// </summary>
public static class DiagnosticRenderer
{
    public const string Reset = "\u001b[0m";
    public const string Bold = "\u001b[1m";
    public const string Red = "\u001b[31m";
    public const string Green = "\u001b[32m";
    public const string Yellow = "\u001b[33m";
    public const string Blue = "\u001b[34m";
    public const string Cyan = "\u001b[36m";

    private static readonly Style Ansi = new(Palette.Ansi16, Symbols.Unicode, 100);

    /// <param name="includePath">Adds a <c>--> file:line:col</c> line; used when a compilation spans several files.</param>
    /// <param name="color">Wraps the parts in ANSI escapes; removing them gives the plain text.</param>
    public static string Render(Diagnostic diagnostic, bool includePath = false, bool color = false) =>
        Render(diagnostic, includePath, color ? Ansi : Style.Plain);

    /// <param name="style">Colours, symbols and the width prose lines wrap at; colour adds escapes and nothing else.</param>
    public static string Render(Diagnostic diagnostic, bool includePath, Style style)
    {
        var sb = new StringBuilder();
        var accent = diagnostic.Severity switch
        {
            Severity.Error => Role.Error,
            Severity.Warning => Role.Warning,
            _ => Role.Note,
        };
        var wrap = style.Width > 0 ? style.Width : 100;
        string Paint(Role role, string text, bool bold = false) =>
            style.Colors is null ? text : (bold ? Bold : "") + style.Colors.Prefix(role) + text + Reset;
        var bar = Paint(Role.Note, style.Symbols.Bar);
        var head = SeverityName(diagnostic.Severity) + " " + diagnostic.Code;
        sb.Append(Paint(accent, head, bold: true)).Append(": ")
          .Append(string.Join("\n", Wrap(diagnostic.Message, wrap - head.Length - 2, wrap))).Append('\n');
        if (diagnostic.Source is not { } source) return sb.ToString();
        if (includePath)
        {
            var (l, c) = source.GetLinePosition(diagnostic.Span.Start);
            sb.Append(Paint(Role.Note, "  -->")).Append(' ').Append(source.Path).Append(':').Append(l).Append(':').Append(c).Append('\n');
        }
        sb.Append('\n');

        var spans = new List<(int Line, TextSpan Span, string? Label, bool Primary)>
        {
            (source.GetLineIndex(diagnostic.Span.Start), diagnostic.Span, diagnostic.Label, true),
        };
        foreach (var related in diagnostic.Related ?? [])
            spans.Add((source.GetLineIndex(related.Span.Start), related.Span, related.Label, false));
        spans = spans.OrderBy(s => s.Line).ToList();

        string[]? fixLines = null;
        var fixFirstLine = 0;
        var markStart = 0;
        var markEnd = 0;
        var marker = '+';
        if (diagnostic.Fix is { } fix) fixLines = ApplyFix(source, fix, out fixFirstLine, out markStart, out markEnd, out marker);

        var widestLine = Math.Max(spans[^1].Line + 1, fixLines is null ? 0 : fixFirstLine + fixLines.Length);
        var width = widestLine.ToString(CultureInfo.InvariantCulture).Length;
        var blank = "  " + new string(' ', width) + " " + bar;

        int? previous = null;
        foreach (var s in spans)
        {
            var lineSpan = source.GetLineSpan(s.Line);
            if (previous != s.Line)
            {
                if (previous is not null) sb.Append(blank).Append('\n');
                sb.Append("  ").Append(Paint(Role.Note, LineNumber(s.Line + 1, width))).Append(' ').Append(bar).Append(' ')
                  .Append(Display(source.GetText(lineSpan))).Append('\n');
            }
            previous = s.Line;

            var underlineEnd = Math.Min(s.Span.End, lineSpan.End);
            var prefix = source.GetText(TextSpan.FromBounds(lineSpan.Start, s.Span.Start));
            var underlined = source.GetText(TextSpan.FromBounds(s.Span.Start, underlineEnd));
            var marks = new string(s.Primary ? '^' : '-', Math.Max(1, RuneCount(underlined)));
            var role = s.Primary ? accent : Role.Note;
            var lead = blank + " " + Indent(prefix);
            if (s.Label is null)
            {
                sb.Append(lead).Append(Paint(role, marks, s.Primary)).Append('\n');
                continue;
            }
            var room = wrap - (2 + width + 3 + RuneCount(prefix) + marks.Length + 1);
            var label = Wrap(s.Label, room, room);
            sb.Append(lead).Append(Paint(role, marks + " " + label[0], s.Primary)).Append('\n');
            for (var i = 1; i < label.Count; i++)
                sb.Append(lead).Append(new string(' ', marks.Length + 1)).Append(Paint(role, label[i], s.Primary)).Append('\n');
        }

        if (diagnostic.Help is null && fixLines is null) return sb.ToString();

        sb.Append(blank).Append('\n');
        if (diagnostic.Help is { } help)
            sb.Append("  ").Append(Paint(Role.Help, "help:", bold: true)).Append(' ')
              .Append(string.Join("\n        ", Wrap(help, wrap - 8, wrap - 8))).Append('\n');
        if (fixLines is not null)
        {
            var offset = 0;
            for (var i = 0; i < fixLines.Length; i++)
            {
                var text = fixLines[i];
                sb.Append(blank).Append(' ').Append(Paint(Role.Note, LineNumber(fixFirstLine + i + 1, width)))
                  .Append(' ').Append(bar).Append(' ').Append(Display(text)).Append('\n');
                var from = Math.Max(markStart - offset, 0);
                var to = Math.Min(markEnd - offset, text.Length);
                if (to > from)
                {
                    sb.Append(blank).Append(' ').Append(new string(' ', width)).Append(' ').Append(bar).Append(' ')
                      .Append(Indent(text[..from])).Append(Paint(Role.Help, new string(marker, RuneCount(text[from..to])))).Append('\n');
                }
                offset += text.Length + 1;
            }
        }
        return sb.ToString();
    }

    /// <summary>Renders diagnostics in source order, separated by blank lines.</summary>
    public static string RenderAll(IEnumerable<Diagnostic> diagnostics, bool includePath = false, bool color = false) =>
        RenderAll(diagnostics, includePath, color ? Ansi : Style.Plain);

    public static string RenderAll(IEnumerable<Diagnostic> diagnostics, bool includePath, Style style)
    {
        var sb = new StringBuilder();
        foreach (var d in Sort(diagnostics))
        {
            if (sb.Length > 0) sb.Append('\n');
            sb.Append(Render(d, includePath, style));
        }
        return sb.ToString();
    }

    public static IEnumerable<Diagnostic> Sort(IEnumerable<Diagnostic> diagnostics) =>
        diagnostics.OrderBy(d => d.Source?.Path ?? "", StringComparer.Ordinal).ThenBy(d => d.Span.Start);

    private static string SeverityName(Severity severity) => severity switch
    {
        Severity.Error => "error",
        Severity.Warning => "warning",
        Severity.Note => "note",
        _ => throw new ArgumentOutOfRangeException(nameof(severity)),
    };

    private static string LineNumber(int number, int width) =>
        number.ToString(CultureInfo.InvariantCulture).PadLeft(width);

    /// <summary>
    /// The lines touched by <paramref name="fix"/> with the replacement applied, and the range of
    /// inserted text to mark with <c>+</c>. A pure removal returns the original lines and marks the
    /// removed text with <c>-</c>.
    /// </summary>
    private static string[] ApplyFix(SourceText source, Fix fix, out int firstLine, out int markStart, out int markEnd, out char marker)
    {
        firstLine = source.GetLineIndex(fix.Span.Start);
        var lastLine = source.GetLineIndex(fix.Span.End);
        var before = source.GetText(TextSpan.FromBounds(source.GetLineSpan(firstLine).Start, fix.Span.Start));
        var after = source.GetText(TextSpan.FromBounds(fix.Span.End, source.GetLineSpan(lastLine).End));
        var original = source.GetText(fix.Span);
        var replacement = fix.Replacement;

        var common = Math.Min(original.Length, replacement.Length);
        var head = 0;
        while (head < common && original[head] == replacement[head]) head++;
        if (head > 0 && char.IsHighSurrogate(original[head - 1])) head--;
        var tail = 0;
        while (tail < common - head && original[^(tail + 1)] == replacement[^(tail + 1)]) tail++;
        if (tail > 0 && char.IsLowSurrogate(original[^tail])) tail--;

        var inserted = replacement.Length - head - tail > 0;
        var shown = inserted ? replacement : original;
        marker = inserted ? '+' : '-';
        markStart = before.Length + head;
        markEnd = before.Length + shown.Length - tail;
        return (before + shown + after).Split('\n');
    }

    /// <summary>
    /// Splits prose into lines that fit: the first gets <paramref name="first"/> columns, the rest
    /// <paramref name="rest"/>. Words are never broken, so a long word overflows its line.
    /// </summary>
    private static List<string> Wrap(string text, int first, int rest)
    {
        var lines = new List<string>();
        foreach (var paragraph in text.Split('\n'))
        {
            var line = new StringBuilder();
            var started = false;
            foreach (var word in paragraph.Split(' '))
            {
                var room = lines.Count == 0 ? first : rest;
                if (started && RuneCount(line.ToString()) + 1 + RuneCount(word) > room)
                {
                    lines.Add(line.ToString());
                    line.Clear();
                    started = false;
                }
                if (started) line.Append(' ');
                line.Append(word);
                started = true;
            }
            lines.Add(line.ToString());
        }
        return lines;
    }

    /// <summary>
    /// Source text as shown in a diagnostic: control and bidi characters become U+FFFD, one
    /// scalar for one, so carets still line up and the terminal cannot be made to reorder text.
    /// </summary>
    private static string Display(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var rune in text.EnumerateRunes())
        {
            var v = rune.Value;
            var hidden = (v < 0x20 && v != '\t') || v == 0x7F || v is >= 0x202A and <= 0x202E or >= 0x2066 and <= 0x2069;
            if (hidden) sb.Append('\uFFFD');
            else sb.Append(rune.ToString());
        }
        return sb.ToString();
    }

    /// <summary>Whitespace that lines up with <paramref name="prefix"/>, keeping its tabs.</summary>
    private static string Indent(string prefix)
    {
        var sb = new StringBuilder(prefix.Length);
        foreach (var rune in prefix.EnumerateRunes())
            sb.Append(rune.Value == '\t' ? '\t' : ' ');
        return sb.ToString();
    }

    private static int RuneCount(string text)
    {
        var count = 0;
        foreach (var _ in text.EnumerateRunes()) count++;
        return count;
    }
}
