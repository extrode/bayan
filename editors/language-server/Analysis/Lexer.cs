namespace Bayan.LanguageServer.Analysis;

public enum TokenKind : byte
{
    Ident, Keyword, Number, String, Rune, Comment, DocComment, BlockComment,
    Punct, Newline, At, Eof
}

/// <summary>A token as a slice of the document text (no string allocated).</summary>
public readonly record struct Token(TokenKind Kind, int Start, int Length)
{
    public int End => Start + Length;
}

/// <summary>
/// Error-tolerant lexer for editor features. It never throws: an unclosed string
/// or comment ends at the line or file end and is reported through <see cref="Problems"/>.
/// Replaced by the compiler's lexer once Bayan.Syntax is referenced (HANDOFF §3).
/// </summary>
public sealed class Lexer
{
    public readonly List<Token> Tokens = new(256);
    public readonly List<(int Start, int Length, string Message, string? Code)> Problems = [];

    /// <summary>Problems kept per file; the rest are only counted in <see cref="Hidden"/>.</summary>
    public const int MaxProblems = 1000;
    public int Hidden { get; private set; }

    void Problem((int, int, string, string?) p)
    {
        if (Problems.Count < MaxProblems) Problems.Add(p);
        else Hidden++;
    }

    /// <summary>Interpolation holes nested deeper than this lex as plain string content.</summary>
    public const int MaxInterpolationDepth = 256;
    int _depth;
    bool _tooDeep;

    public static Lexer Run(string text)
    {
        var lx = new Lexer();
        lx.Scan(text, 0, text.Length);
        lx.Add(TokenKind.Eof, text.Length, 0);
        return lx;
    }

    void Scan(string s, int i, int n)
    {
        while (i < n)
        {
            char c = s[i];
            if (c == '\n') { Add(TokenKind.Newline, i, 1); i++; continue; }
            if (c is ' ' or '\t' or '\r') { i++; continue; }

            if (c == '/' && i + 1 < n && s[i + 1] == '/')
            {
                int start = i;
                bool doc = i + 2 < n && s[i + 2] == '/' && !(i + 3 < n && s[i + 3] == '/');
                while (i < n && s[i] != '\n') i++;
                Add(doc ? TokenKind.DocComment : TokenKind.Comment, start, i - start);
                continue;
            }
            if (c == '/' && i + 1 < n && s[i + 1] == '*')
            {
                int start = i;
                int close = s.IndexOf("*/", i + 2, StringComparison.Ordinal);
                i = close < 0 ? n : close + 2;
                Add(TokenKind.BlockComment, start, i - start);
                Problem((start, 2, close < 0 ? "unclosed `/*` comment" : "`/* */` is an input form; write `//`", "BYN1047"));
                continue;
            }
            if (c == '"' && i + 2 < n && s[i + 1] == '"' && s[i + 2] == '"')
            {
                int start = i;
                int close = s.IndexOf("\"\"\"", i + 3, StringComparison.Ordinal);
                i = close < 0 ? n : close + 3;
                if (close < 0) Problem((start, 3, "unclosed raw string literal", null));
                Add(TokenKind.String, start, i - start);
                continue;
            }
            if (c == '$' && i + 1 < n && s[i + 1] == '"')
            {
                i = ScanInterpolated(s, i, n);
                continue;
            }
            // Plain `"…"` and byte `b"…"` strings (`$"…"` is handled above).
            if (c == '"' || (c == 'b' && i + 1 < n && s[i + 1] == '"' && !IsIdentPart(Prev(s, i))))
            {
                int start = i;
                i += c == '"' ? 1 : 2;
                bool closed = false;
                while (i < n && s[i] != '\n')
                {
                    char d = s[i];
                    if (d == '\\') { i += i + 1 < n && s[i + 1] != '\n' ? 2 : 1; continue; }
                    if (d == '"') { i++; closed = true; break; }
                    i++;
                }
                if (!closed) Problem((start, Math.Max(1, i - start), "unclosed string literal", null));
                Add(TokenKind.String, start, Math.Min(i, n) - start);
                i = Math.Min(i, n);
                continue;
            }
            if (c == '\'')
            {
                int start = i++;
                if (i < n && s[i] == '\\') i += i + 1 < n && s[i + 1] != '\n' ? 2 : 1;
                else if (i < n && s[i] != '\n') i++;
                // `'\u{1F600}'` is the longest form (10 chars + quotes).
                while (i < n && s[i] != '\'' && s[i] != '\n' && i - start < 14) i++;
                i = Math.Min(i, n);
                if (i < n && s[i] == '\'') i++; else Problem((start, 1, "unclosed rune literal", null));
                Add(TokenKind.Rune, start, Math.Min(i, n) - start);
                continue;
            }
            if (char.IsAsciiDigit(c))
            {
                int start = i;
                while (i < n && (char.IsAsciiLetterOrDigit(s[i]) || s[i] == '_' ||
                       (s[i] == '.' && i + 1 < n && char.IsAsciiDigit(s[i + 1])))) i++;
                Add(TokenKind.Number, start, i - start);
                continue;
            }
            if (IsIdentStart(c))
            {
                int start = i;
                while (i < n && IsIdentPart(s[i])) i++;
                var word = s.AsSpan(start, i - start);
                Add(Keywords.IsKeyword(word) ? TokenKind.Keyword : TokenKind.Ident, start, i - start);
                continue;
            }
            if (c == '@') { Add(TokenKind.At, i, 1); i++; continue; }
            if (c == ';') Problem((i, 1, "`;` is not needed; a newline ends the statement", "BYN1040"));

            // Multi-char punctuation the analyzer cares about.
            int len = 1;
            if (i + 1 < n)
            {
                var two = s.AsSpan(i, 2);
                if (two is "->" or "=>" or "?." or "??" or "==" or "!=" or "<=" or ">=" or "&&" or "||" or "..") len = 2;
            }
            Add(TokenKind.Punct, i, len);
            i += len;
        }
    }

    /// <summary>`$"a {x, as: .hex} b"`: string fragments as String tokens, holes lexed as code.</summary>
    int ScanInterpolated(string s, int i, int n)
    {
        int frag = i;
        i += 2;
        while (i < n && s[i] != '\n')
        {
            char d = s[i];
            if (d == '\\') { i += i + 1 < n && s[i + 1] != '\n' ? 2 : 1; continue; }
            if (d == '{' && i + 1 < n && s[i + 1] == '{') { i += 2; continue; }
            if (d == '"') { i++; Add(TokenKind.String, frag, i - frag); return i; }
            if (d == '{' && _depth >= MaxInterpolationDepth)
            {
                // Too deep to lex the holes without risking the stack: the rest of the line is string content.
                int end = s.IndexOf('\n', i, n - i);
                end = end < 0 ? n : end;
                if (!_tooDeep) Problem((frag, end - frag, $"string interpolation nested deeper than {MaxInterpolationDepth} levels", null));
                _tooDeep = true;
                Add(TokenKind.String, frag, end - frag);
                return end;
            }
            if (d == '{')
            {
                Add(TokenKind.String, frag, i + 1 - frag);
                int hole = i + 1, depth = 1, j = hole;
                while (j < n && s[j] != '\n' && depth > 0)
                {
                    if (s[j] == '"') { j++; while (j < n && s[j] != '"' && s[j] != '\n') j += s[j] == '\\' && j + 1 < n && s[j + 1] != '\n' ? 2 : 1; }
                    else if (s[j] == '{') depth++;
                    else if (s[j] == '}') depth--;
                    if (depth > 0) j++;
                }
                _depth++;
                Scan(s, hole, Math.Min(j, n));
                _depth--;
                i = frag = Math.Min(j, n);
                continue;
            }
            i++;
        }
        Problem((frag, Math.Max(1, i - frag), "unclosed string literal", null));
        Add(TokenKind.String, frag, Math.Min(i, n) - frag);
        return Math.Min(i, n);
    }

    static char Prev(string s, int i) => i > 0 ? s[i - 1] : ' ';
    static bool IsIdentStart(char c) => char.IsAsciiLetter(c) || c == '_' || (c > 127 && char.IsLetter(c));
    static bool IsIdentPart(char c) => char.IsAsciiLetterOrDigit(c) || c == '_' || (c > 127 && char.IsLetterOrDigit(c));
    void Add(TokenKind k, int start, int len) => Tokens.Add(new Token(k, start, len));
}
