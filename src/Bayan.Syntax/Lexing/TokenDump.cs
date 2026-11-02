using System.Text;
using Bayan.Syntax.Diagnostics;

namespace Bayan.Syntax.Lexing;

/// <summary>Text form of a token stream, used by <c>bayan lex</c> and the <c>lex/</c> golden tests.</summary>
public static class TokenDump
{
    public static string Dump(LexResult result) => Dump(result, Style.Plain);

    /// <param name="style">How the diagnostics after the tokens render; the <c>lex/</c> goldens and the self-hosted lexer use <see cref="Style.Plain"/>.</param>
    public static string Dump(LexResult result, Style style)
    {
        var sb = new StringBuilder();
        var source = result.Source;
        foreach (var token in result.Tokens)
        {
            var (line, column) = source.GetLinePosition(token.Start);
            if (token.HasDoc) sb.Append("    doc ").Append(Escape(source.GetText(token.Doc))).Append('\n');
            sb.Append(line).Append(':').Append(column).Append(' ').Append(token.Kind);
            if (token.Length > 0) sb.Append(' ').Append(Escape(source.GetText(token.Span)));
            sb.Append('\n');
        }
        if (result.Diagnostics.Count > 0)
            sb.Append('\n').Append(DiagnosticRenderer.RenderAll(result.Diagnostics, includePath: false, style));
        return sb.ToString();
    }

    /// <summary>
    /// <c>bayan lex --json</c>: one object with <c>tokens</c> and <c>diagnostics</c> (in source order),
    /// no insignificant whitespace, then <c>\n</c>. Offsets are bytes; invalid UTF-8 in source text
    /// becomes U+FFFD per maximal subpart. <c>selfhost/lexer/dump.byn</c> prints the same bytes.
    /// </summary>
    public static string ToJson(LexResult result)
    {
        var sb = new StringBuilder("{\"tokens\":[");
        var source = result.Source;
        var first = true;
        foreach (var token in result.Tokens)
        {
            if (!first) sb.Append(',');
            first = false;
            sb.Append("{\"kind\":");
            JsonString(sb, token.Kind.ToString());
            sb.Append(",\"start\":").Append(token.Start).Append(",\"end\":").Append(token.Span.End).Append(",\"text\":");
            JsonString(sb, source.GetText(token.Span));
            sb.Append(",\"doc\":");
            if (token.HasDoc) sb.Append("{\"start\":").Append(token.Doc.Start).Append(",\"end\":").Append(token.Doc.End).Append('}');
            else sb.Append("null");
            sb.Append('}');
        }
        sb.Append("],\"diagnostics\":[");
        first = true;
        foreach (var d in DiagnosticRenderer.Sort(result.Diagnostics))
        {
            if (!first) sb.Append(',');
            first = false;
            sb.Append("{\"code\":");
            JsonString(sb, d.Code);
            sb.Append(",\"severity\":");
            JsonString(sb, d.Severity switch
            {
                Severity.Error => "error",
                Severity.Warning => "warning",
                _ => "note",
            });
            sb.Append(",\"start\":").Append(d.Span.Start).Append(",\"end\":").Append(d.Span.End).Append(",\"message\":");
            JsonString(sb, d.Message);
            sb.Append(",\"label\":");
            JsonOptional(sb, d.Label);
            sb.Append(",\"help\":");
            JsonOptional(sb, d.Help);
            sb.Append(",\"fixes\":[");
            if (d.Fix is { } fix)
            {
                sb.Append("{\"start\":").Append(fix.Span.Start).Append(",\"end\":").Append(fix.Span.End).Append(",\"replacement\":");
                JsonString(sb, fix.Replacement);
                sb.Append('}');
            }
            sb.Append("]}");
        }
        return sb.Append("]}\n").ToString();
    }

    private static void JsonOptional(StringBuilder sb, string? text)
    {
        if (text is null) sb.Append("null");
        else JsonString(sb, text);
    }

    /// <summary>A JSON string: <c>\"</c> <c>\\</c> <c>\n</c> <c>\r</c> <c>\t</c>, other controls and DEL as <c>\u00XX</c>, the rest raw.</summary>
    private static void JsonString(StringBuilder sb, string text)
    {
        sb.Append('"');
        foreach (var rune in text.EnumerateRunes())
        {
            switch (rune.Value)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                case < 0x20 or 0x7F: sb.Append("\\u").Append(rune.Value.ToString("X4")); break;
                default: sb.Append(rune.ToString()); break;
            }
        }
        sb.Append('"');
    }

    /// <summary>Backtick-quoted text with line breaks, tabs and invisible characters made visible.</summary>
    public static string Escape(string text)
    {
        var sb = new StringBuilder("`");
        foreach (var rune in text.EnumerateRunes())
        {
            switch (rune.Value)
            {
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                case < 0x20 or 0x7F or (>= 0x202A and <= 0x202E) or (>= 0x2066 and <= 0x2069) or 0xFEFF:
                    sb.Append("\\u{").Append(rune.Value.ToString("X")).Append('}');
                    break;
                default: sb.Append(rune.ToString()); break;
            }
        }
        return sb.Append('`').ToString();
    }
}
