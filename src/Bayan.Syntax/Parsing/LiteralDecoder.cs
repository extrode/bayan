using System.Text;

namespace Bayan.Syntax.Parsing;

/// <summary>
/// Turns literal token text into values. The lexer has already reported malformed escapes, so
/// decoding here is tolerant: anything it cannot decode is kept as written.
/// </summary>
public static class LiteralDecoder
{
    /// <summary>Contents of a <c>"…"</c>, <c>c"…"</c> or <c>u16"…"</c> literal.</summary>
    public static string DecodeString(string token)
    {
        var start = token.IndexOf('"') + 1;
        var end = token.Length > start && token[^1] == '"' ? token.Length - 1 : token.Length;
        return Unescape(token.AsSpan(start, end - start), interpolated: false);
    }

    /// <summary>
    /// Contents of a raw <c>"""…"""</c> literal. When the opening quotes end their line, that line
    /// break is dropped, the closing line's indentation is removed from every line, and the line
    /// break before the closing quotes is dropped (as in C# raw strings).
    /// </summary>
    public static string DecodeRawString(string token)
    {
        var body = token.Length >= 6 && token.EndsWith("\"\"\"", StringComparison.Ordinal)
            ? token[3..^3]
            : token[3..];
        body = body.Replace("\r\n", "\n");
        if (!body.StartsWith('\n')) return body;
        body = body[1..];
        var lastBreak = body.LastIndexOf('\n');
        var indent = lastBreak >= 0 ? body[(lastBreak + 1)..] : "";
        if (indent.Any(c => c != ' ' && c != '\t')) return body;
        var lines = (lastBreak >= 0 ? body[..lastBreak] : "").Split('\n');
        for (var i = 0; i < lines.Length; i++)
            if (lines[i].StartsWith(indent, StringComparison.Ordinal)) lines[i] = lines[i][indent.Length..];
        return string.Join('\n', lines);
    }

    /// <summary>A literal text run inside <c>$"…"</c>: escapes plus doubled braces.</summary>
    public static string DecodeInterpolatedText(string text) => Unescape(text, interpolated: true);

    /// <summary>The scalar value of a char literal, or -1 if it does not hold exactly one.</summary>
    public static int DecodeChar(string token)
    {
        var end = token.Length > 1 && token[^1] == '\'' ? token.Length - 1 : token.Length;
        var text = Unescape(token.AsSpan(1, end - 1), interpolated: false);
        var e = text.EnumerateRunes();
        if (!e.MoveNext()) return -1;
        var value = e.Current.Value;
        return e.MoveNext() ? -1 : value;
    }

    /// <summary>Doc comment text without the <c>///</c> markers (and one following space).</summary>
    public static string DecodeDoc(string doc)
    {
        var lines = doc.Replace("\r\n", "\n").Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimStart();
            if (line.StartsWith("///", StringComparison.Ordinal)) line = line[3..];
            if (line.StartsWith(' ')) line = line[1..];
            lines[i] = line;
        }
        return string.Join('\n', lines);
    }

    private static string Unescape(ReadOnlySpan<char> text, bool interpolated)
    {
        if (text.IndexOfAny('\\', '{', '}') < 0) return text.ToString();
        var sb = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (interpolated && (c == '{' || c == '}') && i + 1 < text.Length && text[i + 1] == c)
            {
                sb.Append(c);
                i++;
                continue;
            }
            if (c != '\\' || i + 1 >= text.Length)
            {
                sb.Append(c);
                continue;
            }
            var next = text[i + 1];
            switch (next)
            {
                case 'n': sb.Append('\n'); i++; break;
                case 'r': sb.Append('\r'); i++; break;
                case 't': sb.Append('\t'); i++; break;
                case '0': sb.Append('\0'); i++; break;
                case '\\': sb.Append('\\'); i++; break;
                case '"': sb.Append('"'); i++; break;
                case '\'': sb.Append('\''); i++; break;
                case 'u' when i + 2 < text.Length && text[i + 2] == '{':
                    var close = text[(i + 3)..].IndexOf('}');
                    if (close > 0
                        && int.TryParse(text.Slice(i + 3, close), System.Globalization.NumberStyles.AllowHexSpecifier, null, out var value)
                        && Rune.IsValid(value))
                    {
                        sb.Append(new Rune(value).ToString());
                        i += 3 + close;
                    }
                    else
                    {
                        sb.Append(c);
                    }
                    break;
                default:
                    sb.Append(c);
                    break;
            }
        }
        return sb.ToString();
    }
}
