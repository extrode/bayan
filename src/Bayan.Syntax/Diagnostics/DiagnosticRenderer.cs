using System.Globalization;
using System.Text;
using Bayan.Syntax.Text;

namespace Bayan.Syntax.Diagnostics;

/// <summary>
/// Renders a diagnostic as plain text. The format is snapshot-tested; change it deliberately.
/// <code>
/// error BYN2104: `total` is immutable
///
///   12 │     total += line.price;
///      │     ^^^^^ declared with `let` on line 9
///      │
///   help: declare it with `var` if it should change
///      │  9 │     var total = 0;
/// </code>
/// </summary>
public static class DiagnosticRenderer
{
    private const string Bar = "│";

    /// <param name="includePath">Adds a <c>--> file:line:col</c> line; used when a compilation spans several files.</param>
    public static string Render(Diagnostic diagnostic, bool includePath = false)
    {
        var sb = new StringBuilder();
        sb.Append(SeverityName(diagnostic.Severity)).Append(' ').Append(diagnostic.Code)
          .Append(": ").Append(diagnostic.Message).Append('\n');
        if (diagnostic.Source is not { } source) return sb.ToString();
        if (includePath)
        {
            var (l, c) = source.GetLinePosition(diagnostic.Span.Start);
            sb.Append("  --> ").Append(source.Path).Append(':').Append(l).Append(':').Append(c).Append('\n');
        }
        sb.Append('\n');

        var line = source.GetLineIndex(diagnostic.Span.Start);
        var lineSpan = source.GetLineSpan(line);

        string[]? fixLines = null;
        var fixFirstLine = 0;
        if (diagnostic.Fix is { } fix) fixLines = ApplyFix(source, fix, out fixFirstLine);

        var widestLine = Math.Max(line + 1, fixLines is null ? 0 : fixFirstLine + fixLines.Length);
        var width = widestLine.ToString(CultureInfo.InvariantCulture).Length;
        var blank = "  " + new string(' ', width) + " " + Bar;

        sb.Append("  ").Append(LineNumber(line + 1, width)).Append(' ').Append(Bar).Append(' ')
          .Append(Display(source.GetText(lineSpan))).Append('\n');

        var underlineStart = diagnostic.Span.Start;
        var underlineEnd = Math.Min(diagnostic.Span.End, lineSpan.End);
        var prefix = source.GetText(TextSpan.FromBounds(lineSpan.Start, underlineStart));
        var underlined = source.GetText(TextSpan.FromBounds(underlineStart, underlineEnd));
        sb.Append(blank).Append(' ').Append(Indent(prefix))
          .Append('^', Math.Max(1, RuneCount(underlined)));
        if (diagnostic.Label is { } label) sb.Append(' ').Append(label);
        sb.Append('\n');

        if (diagnostic.Help is null && fixLines is null) return sb.ToString();

        sb.Append(blank).Append('\n');
        if (diagnostic.Help is { } help) sb.Append("  help: ").Append(help.Replace("\n", "\n        ")).Append('\n');
        if (fixLines is not null)
        {
            for (var i = 0; i < fixLines.Length; i++)
            {
                sb.Append(blank).Append(' ').Append(LineNumber(fixFirstLine + i + 1, width))
                  .Append(' ').Append(Bar).Append(' ').Append(Display(fixLines[i])).Append('\n');
            }
        }
        return sb.ToString();
    }

    /// <summary>Renders diagnostics in source order, separated by blank lines.</summary>
    public static string RenderAll(IEnumerable<Diagnostic> diagnostics, bool includePath = false)
    {
        var sb = new StringBuilder();
        foreach (var d in Sort(diagnostics))
        {
            if (sb.Length > 0) sb.Append('\n');
            sb.Append(Render(d, includePath));
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

    /// <summary>The lines touched by <paramref name="fix"/>, with the replacement applied.</summary>
    private static string[] ApplyFix(SourceText source, Fix fix, out int firstLine)
    {
        firstLine = source.GetLineIndex(fix.Span.Start);
        var lastLine = source.GetLineIndex(fix.Span.End);
        var start = source.GetLineSpan(firstLine).Start;
        var end = source.GetLineSpan(lastLine).End;
        var text = source.GetText(TextSpan.FromBounds(start, fix.Span.Start))
                 + fix.Replacement
                 + source.GetText(TextSpan.FromBounds(fix.Span.End, end));
        return text.Split('\n');
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
