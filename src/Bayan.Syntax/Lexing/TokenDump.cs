using System.Text;
using Bayan.Syntax.Diagnostics;

namespace Bayan.Syntax.Lexing;

/// <summary>Text form of a token stream, used by <c>bayan lex</c> and the <c>lex/</c> golden tests.</summary>
public static class TokenDump
{
    public static string Dump(LexResult result)
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
            sb.Append('\n').Append(DiagnosticRenderer.RenderAll(result.Diagnostics));
        return sb.ToString();
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
