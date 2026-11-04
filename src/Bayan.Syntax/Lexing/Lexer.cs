using System.Globalization;
using System.Text;
using Bayan.Syntax.Diagnostics;
using Bayan.Syntax.Text;

namespace Bayan.Syntax.Lexing;

/// <summary>
/// Hand-written lexer over UTF-8 bytes. The only allocation proportional to input size is the
/// token array; diagnostics are allocated only when reported.
/// </summary>
public static class Lexer
{
    internal const int MaxInterpolationDepth = 32;

    public static LexResult Lex(SourceText source)
    {
        var diagnostics = new DiagnosticBag();
        Span<ModeEntry> modes = stackalloc ModeEntry[MaxInterpolationDepth + 1];
        var state = new LexerState(source, diagnostics, modes);
        state.Run();
        return new LexResult(source, state.Tokens, state.Count, diagnostics.Items);
    }

    internal static bool IsIdentifierStart(Rune r) => Nfc.InRanges(UnicodeTables.IdentStartRanges, r.Value);

    internal static bool IsIdentifierContinue(Rune r) =>
        IsIdentifierStart(r) || Nfc.InRanges(UnicodeTables.IdentContinueRanges, r.Value);

    /// <summary>Length of the bidi control character at <paramref name="pos"/>, or 0.</summary>
    internal static int BidiControlAt(ReadOnlySpan<byte> text, int pos, out int codePoint)
    {
        codePoint = 0;
        if (pos + 2 >= text.Length || text[pos] != 0xE2) return 0;
        var b1 = text[pos + 1];
        var b2 = text[pos + 2];
        if ((b1 == 0x80 && b2 is >= 0xAA and <= 0xAE) || (b1 == 0x81 && b2 is >= 0xA6 and <= 0xA9))
        {
            codePoint = 0x2000 | ((b1 & 0x0F) << 6) | (b2 & 0x3F);
            return 3;
        }
        return 0;
    }
}

internal struct ModeEntry
{
    /// <summary>True while scanning the text part of an interpolated string.</summary>
    public bool InText;
    /// <summary>Unclosed `{` inside an interpolation hole.</summary>
    public int BraceDepth;
    /// <summary>Offset of the `$"` that opened this string.</summary>
    public int Start;
}

internal ref struct LexerState
{
    private readonly ReadOnlySpan<byte> _text;
    private readonly SourceText _source;
    private readonly DiagnosticBag _diagnostics;
    private readonly Span<ModeEntry> _modes;
    private int _depth;
    private bool _reportedTooDeep;
    private int _pos;
    private TokenFlags _flags;
    private int _docStart;
    private int _docEnd;

    public Token[] Tokens;
    public int Count;

    public LexerState(SourceText source, DiagnosticBag diagnostics, Span<ModeEntry> modes)
    {
        _source = source;
        _text = source.Bytes.Span;
        _diagnostics = diagnostics;
        _modes = modes;
        _modes[0] = default;
        _depth = 0;
        _pos = 0;
        _flags = TokenFlags.None;
        _docStart = -1;
        _docEnd = -1;
        Tokens = new Token[_text.Length / 4 + 16];
        Count = 0;
    }

    public void Run()
    {
        if (_text.Length >= 3 && _text[0] == 0xEF && _text[1] == 0xBB && _text[2] == 0xBF) _pos = 3;
        while (true)
        {
            if (_modes[_depth].InText)
            {
                LexInterpolatedText();
                continue;
            }
            SkipTrivia();
            if (_pos >= _text.Length)
            {
                if (_depth > 0)
                    Report(DiagnosticCodes.UnterminatedString, new TextSpan(_modes[_depth].Start, 2), label: "this string never ends",
                        help: "close the `{` and the string");
                Add(TokenKind.EndOfFile, _pos, 0);
                return;
            }
            LexToken();
        }
    }

    // ── Plumbing ─────────────────────────────────────────────────────────────────────

    private readonly byte Peek(int offset = 0) => _pos + offset < _text.Length ? _text[_pos + offset] : (byte)0;

    private void Add(TokenKind kind, int start, int length)
    {
        if (Count == Tokens.Length) Array.Resize(ref Tokens, Tokens.Length * 2);
        var docLength = _docStart >= 0 ? _docEnd - _docStart : 0;
        Tokens[Count++] = new Token(kind, start, length, _flags, _docStart >= 0 ? _docStart : 0, docLength);
        _flags = TokenFlags.None;
        _docStart = -1;
    }

    private void Report(DiagnosticDescriptor descriptor, TextSpan span, object?[]? args = null,
        string? label = null, string? help = null, Fix? fix = null) =>
        _diagnostics.Report(descriptor, _source, span, args, label, help, fix);

    private static bool IsDigit(byte b) => b is >= (byte)'0' and <= (byte)'9';
    private static bool IsAsciiIdentStart(byte b) => b is >= (byte)'a' and <= (byte)'z' or >= (byte)'A' and <= (byte)'Z' or (byte)'_';
    private static bool IsAsciiIdentContinue(byte b) => IsAsciiIdentStart(b) || IsDigit(b);

    private readonly bool IsIdentifierStartAt(int pos)
    {
        if (pos >= _text.Length) return false;
        var b = _text[pos];
        if (b < 0x80) return IsAsciiIdentStart(b);
        return Rune.DecodeFromUtf8(_text[pos..], out var r, out _) == System.Buffers.OperationStatus.Done
               && Lexer.IsIdentifierStart(r);
    }

    /// <summary>Advances over one code point, reporting bidi controls and invalid UTF-8 unless allowed.</summary>
    private void AdvanceCodePoint(bool allowBidi)
    {
        var b = _text[_pos];
        if (b < 0x80)
        {
            _pos++;
            return;
        }
        var bidi = Lexer.BidiControlAt(_text, _pos, out var cp);
        if (bidi > 0)
        {
            if (!allowBidi) ReportBidi(_pos, cp);
            _pos += bidi;
            return;
        }
        if (Rune.DecodeFromUtf8(_text[_pos..], out _, out var consumed) != System.Buffers.OperationStatus.Done)
        {
            Report(DiagnosticCodes.InvalidUtf8, new TextSpan(_pos, 1), [b.ToString("X2", CultureInfo.InvariantCulture)]);
            _pos++;
            return;
        }
        _pos += consumed;
    }

    private void ReportBidi(int pos, int codePoint) =>
        Report(DiagnosticCodes.BidiControl, new TextSpan(pos, 3), [codePoint.ToString("X4", CultureInfo.InvariantCulture)],
            label: "invisible character that reorders how the text displays",
            help: "remove it; bidi controls may only appear inside string literals (Trojan Source, CVE-2021-42574)",
            fix: new Fix(new TextSpan(pos, 3), ""));

    // ── Trivia ───────────────────────────────────────────────────────────────────────

    private void SkipTrivia()
    {
        while (_pos < _text.Length)
        {
            var b = _text[_pos];
            switch (b)
            {
                case (byte)' ' or (byte)'\t' or (byte)'\r':
                    _pos++;
                    _flags |= TokenFlags.TriviaBefore;
                    continue;
                case (byte)'\n':
                    _pos++;
                    _flags |= TokenFlags.TriviaBefore | TokenFlags.NewlineBefore;
                    continue;
                case (byte)'/' when Peek(1) == '/':
                {
                    var start = _pos;
                    var isDoc = Peek(2) == '/' && Peek(3) != '/';
                    while (_pos < _text.Length && _text[_pos] != '\n') AdvanceCodePoint(allowBidi: false);
                    if (isDoc)
                    {
                        if (_docStart < 0) _docStart = start;
                        _docEnd = _pos;
                    }
                    _flags |= TokenFlags.TriviaBefore;
                    continue;
                }
                case (byte)'/' when Peek(1) == '*':
                {
                    var start = _pos;
                    _pos += 2;
                    while (_pos < _text.Length && !(_text[_pos] == '*' && Peek(1) == '/'))
                    {
                        if (_text[_pos] == '\n') _flags |= TokenFlags.NewlineBefore;
                        AdvanceCodePoint(allowBidi: false);
                    }
                    if (_pos >= _text.Length)
                        Report(DiagnosticCodes.UnterminatedComment, new TextSpan(start, 2), label: "comment starts here",
                            help: "close it with `*/`");
                    else
                    {
                        _pos += 2;
                        var span = new TextSpan(start, _pos - start);
                        Report(DiagnosticCodes.BlockComment, span, label: "block comment", help: "write each line with `//`",
                            fix: new Fix(span, LineComments(start, _pos)));
                    }
                    _flags |= TokenFlags.TriviaBefore;
                    continue;
                }
                case (byte)'#' when Count == 0 && (Peek(1) == '!' || Peek(1) == ':') && AtLineStart():
                    // Script shebang and `#:` directives at the top of a file.
                    while (_pos < _text.Length && _text[_pos] != '\n') AdvanceCodePoint(allowBidi: false);
                    _flags |= TokenFlags.TriviaBefore;
                    continue;
                default:
                    return;
            }
        }
    }

    /// <summary>
    /// BYN1047's fix for the comment <c>[start, end)</c>: one <c>//</c> line per trimmed content line, the outer
    /// blank lines dropped, at the indent of the <c>/*</c> line; code after <c>*/</c> moves to its own line.
    /// </summary>
    private readonly string LineComments(int start, int end)
    {
        var lineStart = start;
        while (lineStart > 0 && _text[lineStart - 1] != '\n') lineStart--;
        var indentEnd = lineStart;
        while (indentEnd < start && _text[indentEnd] is (byte)' ' or (byte)'\t') indentEnd++;
        var indent = Encoding.UTF8.GetString(_text[lineStart..indentEnd]);
        var lines = new List<string>();
        var content = _text[(start + 2)..(end - 2)];
        while (true)
        {
            var newline = content.IndexOf((byte)'\n');
            var line = (newline < 0 ? content : content[..newline]).Trim(" \t\r"u8);
            lines.Add(Encoding.UTF8.GetString(line));
            if (newline < 0) break;
            content = content[(newline + 1)..];
        }
        if (lines.Count > 1 && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);
        if (lines.Count > 1 && lines[0].Length == 0) lines.RemoveAt(0);
        var sb = new StringBuilder();
        for (var i = 0; i < lines.Count; i++)
        {
            if (i > 0) sb.Append('\n').Append(indent);
            sb.Append(lines[i].Length == 0 ? "//" : "// " + lines[i]);
        }
        var after = end;
        while (after < _text.Length && _text[after] is (byte)' ' or (byte)'\t' or (byte)'\r') after++;
        if (after < _text.Length && _text[after] != '\n') sb.Append('\n').Append(indent);
        return sb.ToString();
    }

    private readonly bool AtLineStart() => _pos == 0 || _text[_pos - 1] == '\n';

    // ── Tokens ───────────────────────────────────────────────────────────────────────

    private void LexToken()
    {
        var start = _pos;
        var c = _text[_pos];

        if (IsAsciiIdentStart(c))
        {
            LexIdentifier();
            return;
        }
        if (IsDigit(c))
        {
            LexNumber();
            return;
        }
        if (c >= 0x80)
        {
            var bidi = Lexer.BidiControlAt(_text, _pos, out var cp);
            if (bidi > 0)
            {
                ReportBidi(_pos, cp);
                _pos += bidi;
                _flags |= TokenFlags.TriviaBefore;
                return;
            }
            if (Rune.DecodeFromUtf8(_text[_pos..], out var rune, out var consumed) != System.Buffers.OperationStatus.Done)
            {
                Report(DiagnosticCodes.InvalidUtf8, new TextSpan(_pos, 1), [c.ToString("X2", CultureInfo.InvariantCulture)]);
                _pos++;
                return;
            }
            if (Lexer.IsIdentifierStart(rune))
            {
                LexIdentifier();
                return;
            }
            ReportUnexpected(rune, consumed);
            return;
        }

        switch (c)
        {
            case (byte)'"':
                LexString(start, start, TokenKind.StringLiteral);
                return;
            case (byte)'\'':
                LexChar();
                return;
            case (byte)'$' when Peek(1) == '"':
                if (_depth + 1 >= _modes.Length)
                {
                    if (!_reportedTooDeep) Report(DiagnosticCodes.InterpolationTooDeep, new TextSpan(start, 2));
                    _reportedTooDeep = true;
                    _pos++;
                    LexString(start, _pos, TokenKind.StringLiteral);
                    return;
                }
                Add(TokenKind.InterpolatedStringStart, start, 2);
                _pos += 2;
                _modes[++_depth] = new ModeEntry { InText = true, Start = start };
                return;
            case (byte)'{':
                if (_depth > 0) _modes[_depth].BraceDepth++;
                Single(TokenKind.OpenBrace);
                return;
            case (byte)'}':
                if (_depth > 0 && _modes[_depth].BraceDepth == 0)
                {
                    Add(TokenKind.InterpolationEnd, start, 1);
                    _pos++;
                    _depth--;
                    return;
                }
                if (_depth > 0) _modes[_depth].BraceDepth--;
                Single(TokenKind.CloseBrace);
                return;
            case (byte)'(': Single(TokenKind.OpenParen); return;
            case (byte)')': Single(TokenKind.CloseParen); return;
            case (byte)'[': Single(TokenKind.OpenBracket); return;
            case (byte)']': Single(TokenKind.CloseBracket); return;
            case (byte)';': Single(TokenKind.Semicolon); return;
            case (byte)',': Single(TokenKind.Comma); return;
            case (byte)'@': Single(TokenKind.At); return;
            case (byte)'#': Single(TokenKind.Hash); return;
            case (byte)'~': Single(TokenKind.Tilde); return;
            case (byte)':': Single(TokenKind.Colon); return;
            case (byte)'.':
                if (Peek(1) == '.') Fixed(Peek(2) == '=' ? TokenKind.DotDotEquals : TokenKind.DotDot, Peek(2) == '=' ? 3 : 2);
                else Single(TokenKind.Dot);
                return;
            case (byte)'?':
                if (Peek(1) == '?') Fixed(Peek(2) == '=' ? TokenKind.QuestionQuestionEquals : TokenKind.QuestionQuestion, Peek(2) == '=' ? 3 : 2);
                else if (Peek(1) == '.' && !IsDigit(Peek(2))) Fixed(TokenKind.QuestionDot, 2);
                else Single(TokenKind.Question);
                return;
            case (byte)'+':
                Fixed(Peek(1) switch
                {
                    (byte)'+' => TokenKind.PlusPlus,
                    (byte)'=' => TokenKind.PlusEquals,
                    (byte)'%' => TokenKind.PlusPercent,
                    (byte)'|' => TokenKind.PlusBar,
                    _ => TokenKind.Plus,
                });
                return;
            case (byte)'-':
                Fixed(Peek(1) switch
                {
                    (byte)'-' => TokenKind.MinusMinus,
                    (byte)'=' => TokenKind.MinusEquals,
                    (byte)'%' => TokenKind.MinusPercent,
                    (byte)'|' => TokenKind.MinusBar,
                    (byte)'>' => TokenKind.Arrow,
                    _ => TokenKind.Minus,
                });
                return;
            case (byte)'*':
                Fixed(Peek(1) switch
                {
                    (byte)'=' => TokenKind.StarEquals,
                    (byte)'%' => TokenKind.StarPercent,
                    (byte)'|' => TokenKind.StarBar,
                    _ => TokenKind.Star,
                });
                return;
            case (byte)'/': Fixed(Peek(1) == '=' ? TokenKind.SlashEquals : TokenKind.Slash); return;
            case (byte)'%': Fixed(Peek(1) == '=' ? TokenKind.PercentEquals : TokenKind.Percent); return;
            case (byte)'^': Fixed(Peek(1) == '=' ? TokenKind.CaretEquals : TokenKind.Caret); return;
            case (byte)'!': Fixed(Peek(1) == '=' ? TokenKind.BangEquals : TokenKind.Bang); return;
            case (byte)'&':
                Fixed(Peek(1) switch { (byte)'&' => TokenKind.AmpAmp, (byte)'=' => TokenKind.AmpEquals, _ => TokenKind.Amp });
                return;
            case (byte)'|':
                Fixed(Peek(1) switch { (byte)'|' => TokenKind.BarBar, (byte)'=' => TokenKind.BarEquals, _ => TokenKind.Bar });
                return;
            case (byte)'=':
                Fixed(Peek(1) switch { (byte)'=' => TokenKind.EqualsEquals, (byte)'>' => TokenKind.FatArrow, _ => TokenKind.Equals });
                return;
            case (byte)'<':
                if (Peek(1) == '<') Fixed(Peek(2) == '=' ? TokenKind.LessLessEquals : TokenKind.LessLess, Peek(2) == '=' ? 3 : 2);
                else Fixed(Peek(1) == '=' ? TokenKind.LessEquals : TokenKind.Less);
                return;
            case (byte)'>':
                Fixed(Peek(1) == '=' ? TokenKind.GreaterEquals : TokenKind.Greater);
                return;
        }

        ReportUnexpected(new Rune(c), 1);
    }

    private void ReportUnexpected(Rune rune, int length)
    {
        var shown = rune.Value < 0x20 || rune.Value == 0x7F
            ? $"\\u{{{rune.Value:X}}}"
            : rune.ToString();
        Report(DiagnosticCodes.UnexpectedCharacter, new TextSpan(_pos, length), [shown]);
        _pos += length;
        _flags |= TokenFlags.TriviaBefore;
    }

    private void Single(TokenKind kind) => Fixed(kind, 1);

    /// <summary>Adds a fixed token; with no explicit length, two-character kinds are inferred.</summary>
    private void Fixed(TokenKind kind, int length = 0)
    {
        if (length == 0) length = kind.Text().Length;
        Add(kind, _pos, length);
        _pos += length;
    }

    private void LexIdentifier()
    {
        var start = _pos;
        var ascii = true;
        while (_pos < _text.Length)
        {
            var b = _text[_pos];
            if (b < 0x80)
            {
                if (!IsAsciiIdentContinue(b)) break;
                _pos++;
                continue;
            }
            if (Lexer.BidiControlAt(_text, _pos, out _) > 0) break;
            if (Rune.DecodeFromUtf8(_text[_pos..], out var r, out var n) != System.Buffers.OperationStatus.Done
                || !Lexer.IsIdentifierContinue(r))
                break;
            ascii = false;
            _pos += n;
        }

        var span = _text[start.._pos];
        if (!ascii)
        {
            var text = Encoding.UTF8.GetString(span);
            if (!Nfc.IsNfc(text))
            {
                var normalized = Nfc.ToNfc(text);
                Report(DiagnosticCodes.IdentifierNotNfc, new TextSpan(start, _pos - start), [text],
                    label: "same letters, different encoding",
                    help: "use the composed (NFC) form; identifiers that look the same must be byte-identical",
                    fix: new Fix(new TextSpan(start, _pos - start), normalized));
            }
            Add(TokenKind.Identifier, start, _pos - start);
            return;
        }

        var kind = Keywords.Lookup(span);
        if (kind == TokenKind.Identifier && Peek() == '"')
        {
            if (span.Length == 1 && span[0] == 'c')
            {
                LexString(start, _pos, TokenKind.CStringLiteral);
                return;
            }
            if (span.SequenceEqual("u16"u8))
            {
                LexString(start, _pos, TokenKind.U16StringLiteral);
                return;
            }
        }
        if (kind.IsReservedKeyword())
        {
            Report(DiagnosticCodes.ReservedKeyword, new TextSpan(start, _pos - start), [Encoding.ASCII.GetString(span)],
                label: "reserved for a future version of Bayan", help: "choose another name");
            kind = TokenKind.Identifier;
        }
        Add(kind, start, _pos - start);
    }

    private void LexNumber()
    {
        var start = _pos;
        string? radix = null;
        var isFloat = false;

        if (_text[_pos] == '0' && (Peek(1) | 0x20) == 'x')
        {
            radix = "hexadecimal";
            _pos += 2;
            ScanDigits(start, static b => b is >= (byte)'0' and <= (byte)'9' or >= (byte)'a' and <= (byte)'f' or >= (byte)'A' and <= (byte)'F', "0x");
        }
        else if (_text[_pos] == '0' && (Peek(1) | 0x20) == 'b' && (Peek(2) is (byte)'0' or (byte)'1' or (byte)'_'))
        {
            radix = "binary";
            _pos += 2;
            ScanDigits(start, static b => b is (byte)'0' or (byte)'1', "0b");
            if (IsDigit(Peek()))
            {
                var bad = _pos;
                while (IsDigit(Peek())) _pos++;
                Report(DiagnosticCodes.InvalidNumber, new TextSpan(bad, _pos - bad), ["binary literals use only `0` and `1`"]);
            }
        }
        else
        {
            ScanDigits(start, IsDigit, null);
            if (Peek() == '.' && IsDigit(Peek(1)))
            {
                isFloat = true;
                _pos++;
                ScanDigits(start, IsDigit, null);
            }
            if ((Peek() | 0x20) == 'e' && (IsDigit(Peek(1)) || (Peek(1) is (byte)'+' or (byte)'-' && IsDigit(Peek(2)))))
            {
                isFloat = true;
                _pos += Peek(1) is (byte)'+' or (byte)'-' ? 2 : 1;
                ScanDigits(start, IsDigit, null);
            }
        }

        Add(isFloat ? TokenKind.FloatLiteral : TokenKind.IntegerLiteral, start, _pos - start);

        if (IsIdentifierStartAt(_pos))
        {
            var suffixStart = _pos;
            while (_pos < _text.Length)
            {
                var b = _text[_pos];
                if (b < 0x80)
                {
                    if (!IsAsciiIdentContinue(b)) break;
                    _pos++;
                    continue;
                }
                if (Rune.DecodeFromUtf8(_text[_pos..], out var r, out var n) != System.Buffers.OperationStatus.Done
                    || !Lexer.IsIdentifierContinue(r))
                    break;
                _pos += n;
            }
            Add(TokenKind.NumericSuffix, suffixStart, _pos - suffixStart);
            var suffix = _text[suffixStart.._pos];
            var builtIn = suffix.SequenceEqual("u"u8) || suffix.SequenceEqual("l"u8) || suffix.SequenceEqual("ul"u8);
            if (radix is not null && !builtIn)
            {
                Report(DiagnosticCodes.SuffixOnHexOrBinary, new TextSpan(suffixStart, _pos - suffixStart),
                    [Encoding.UTF8.GetString(suffix), radix],
                    help: "write the value in decimal, or wrap it with a cast to the unit type");
            }
        }
    }

    private void ScanDigits(int literalStart, Func<byte, bool> isDigit, string? prefix)
    {
        var first = _pos;
        var digitsEnd = first;
        while (_pos < _text.Length && (isDigit(_text[_pos]) || _text[_pos] == '_'))
        {
            if (_text[_pos] != '_') digitsEnd = _pos + 1;
            _pos++;
        }
        // Only underscores after a prefix is still no digits; deleting them would not help. Trailing underscores are
        // reported and deleted as one run, so the fix leaves a valid literal.
        if (digitsEnd == first && prefix is not null)
            Report(DiagnosticCodes.InvalidNumber, new TextSpan(literalStart, _pos - literalStart), [$"expected digits after `{prefix}`"]);
        else if (digitsEnd < _pos)
            Report(DiagnosticCodes.InvalidNumber, TextSpan.FromBounds(digitsEnd, _pos), ["`_` must sit between digits"],
                fix: new Fix(TextSpan.FromBounds(digitsEnd, _pos), ""));
    }

    /// <summary>Lexes a string whose opening quote is at <paramref name="quote"/>.</summary>
    private void LexString(int start, int quote, TokenKind kind)
    {
        _pos = quote;
        if (kind == TokenKind.StringLiteral && Peek(1) == '"' && Peek(2) == '"')
        {
            _pos += 3;
            while (_pos < _text.Length && !(_text[_pos] == '"' && Peek(1) == '"' && Peek(2) == '"'))
                AdvanceCodePoint(allowBidi: true);
            if (_pos >= _text.Length)
                Report(DiagnosticCodes.UnterminatedString, new TextSpan(start, 3), label: "raw string starts here",
                    help: "close it with `\"\"\"`");
            else
                _pos += 3;
            Add(TokenKind.RawStringLiteral, start, _pos - start);
            return;
        }

        _pos++;
        while (true)
        {
            if (_pos >= _text.Length || _text[_pos] == '\n')
            {
                var end = _pos > start && _pos <= _text.Length && _pos > 0 && _text[_pos - 1] == '\r' ? _pos - 1 : _pos;
                Report(DiagnosticCodes.UnterminatedString, TextSpan.FromBounds(start, end),
                    label: "string ends at the end of the line",
                    help: "close it with `\"`; for text spanning lines use a raw `\"\"\"` string");
                Add(kind, start, end - start);
                return;
            }
            var b = _text[_pos];
            if (b == '"')
            {
                _pos++;
                Add(kind, start, _pos - start);
                return;
            }
            if (b == '\\') ScanEscape();
            else AdvanceCodePoint(allowBidi: true);
        }
    }

    private void LexInterpolatedText()
    {
        ref var mode = ref _modes[_depth];
        var start = _pos;
        while (true)
        {
            if (_pos >= _text.Length || _text[_pos] == '\n')
            {
                AddText(start);
                Report(DiagnosticCodes.UnterminatedString, new TextSpan(mode.Start, 2), label: "this string never ends",
                    help: "close it with `\"`");
                Add(TokenKind.InterpolatedStringEnd, _pos, 0);
                _depth--;
                return;
            }
            var b = _text[_pos];
            switch (b)
            {
                case (byte)'"':
                    AddText(start);
                    Add(TokenKind.InterpolatedStringEnd, _pos, 1);
                    _pos++;
                    _depth--;
                    return;
                case (byte)'\\':
                    ScanEscape();
                    continue;
                case (byte)'{' when Peek(1) == '{':
                case (byte)'}' when Peek(1) == '}':
                    _pos += 2;
                    continue;
                case (byte)'{':
                    if (_depth + 1 >= _modes.Length)
                    {
                        if (!_reportedTooDeep) Report(DiagnosticCodes.InterpolationTooDeep, new TextSpan(_pos, 1));
                        _reportedTooDeep = true;
                        _pos++;
                        continue;
                    }
                    AddText(start);
                    Add(TokenKind.InterpolationStart, _pos, 1);
                    _pos++;
                    _modes[++_depth] = new ModeEntry { InText = false, BraceDepth = 0, Start = mode.Start };
                    return;
                case (byte)'}':
                    Report(DiagnosticCodes.UnescapedCloseBrace, new TextSpan(_pos, 1), fix: new Fix(new TextSpan(_pos, 1), "}}"));
                    _pos++;
                    continue;
                default:
                    AdvanceCodePoint(allowBidi: true);
                    continue;
            }
        }
    }

    private void AddText(int start)
    {
        if (_pos > start) Add(TokenKind.InterpolatedStringText, start, _pos - start);
    }

    private void LexChar()
    {
        var start = _pos;
        _pos++;
        if (Peek() == '\'')
        {
            _pos++;
            Report(DiagnosticCodes.EmptyCharLiteral, new TextSpan(start, 2), help: "write the character between the quotes, e.g. `' '`");
            Add(TokenKind.CharLiteral, start, 2);
            return;
        }
        var scalars = 0;
        while (_pos < _text.Length && _text[_pos] != '\'' && _text[_pos] != '\n')
        {
            if (_text[_pos] == '\\') ScanEscape();
            else AdvanceCodePoint(allowBidi: false);
            scalars++;
        }
        if (_pos >= _text.Length || _text[_pos] != '\'')
        {
            Report(DiagnosticCodes.UnterminatedCharLiteral, TextSpan.FromBounds(start, _pos), help: "close it with `'`");
            Add(TokenKind.CharLiteral, start, _pos - start);
            return;
        }
        _pos++;
        if (scalars > 1)
            Report(DiagnosticCodes.CharLiteralTooLong, TextSpan.FromBounds(start, _pos),
                help: "use a string literal (\"…\") for text");
        Add(TokenKind.CharLiteral, start, _pos - start);
    }

    private static int HexValue(byte b) => b switch
    {
        >= (byte)'0' and <= (byte)'9' => b - '0',
        >= (byte)'a' and <= (byte)'f' => b - 'a' + 10,
        >= (byte)'A' and <= (byte)'F' => b - 'A' + 10,
        _ => -1,
    };

    private void ScanEscape()
    {
        var start = _pos;
        _pos++;
        if (_pos >= _text.Length || _text[_pos] == '\n')
        {
            Report(DiagnosticCodes.InvalidEscape, new TextSpan(start, 1), ["\\"]);
            return;
        }
        var e = _text[_pos];
        switch (e)
        {
            case (byte)'n' or (byte)'r' or (byte)'t' or (byte)'0' or (byte)'\\' or (byte)'"' or (byte)'\'':
                _pos++;
                return;
            case (byte)'u' when Peek(1) == '{':
            {
                _pos += 2;
                var digitsStart = _pos;
                var value = 0L;
                while (_pos < _text.Length && HexValue(_text[_pos]) >= 0 && _pos - digitsStart < 8)
                {
                    value = value * 16 + HexValue(_text[_pos]);
                    _pos++;
                }
                var digits = _pos - digitsStart;
                if (digits == 0 || Peek() != '}')
                {
                    Report(DiagnosticCodes.InvalidEscape, TextSpan.FromBounds(start, _pos),
                        [Encoding.UTF8.GetString(_text[start.._pos])], help: "write `\\u{` followed by 1–6 hex digits and `}`");
                    return;
                }
                _pos++;
                if (digits > 6 || value > 0x10FFFF || value is >= 0xD800 and <= 0xDFFF)
                    Report(DiagnosticCodes.InvalidUnicodeEscape, TextSpan.FromBounds(start, _pos),
                        [Encoding.UTF8.GetString(_text[start.._pos])],
                        help: "use a scalar value in U+0000–U+D7FF or U+E000–U+10FFFF");
                return;
            }
            default:
            {
                AdvanceCodePoint(allowBidi: true);
                Report(DiagnosticCodes.InvalidEscape, TextSpan.FromBounds(start, _pos),
                    [Encoding.UTF8.GetString(_text[start.._pos])],
                    help: "use a valid escape: \\n \\r \\t \\0 \\\\ \\\" \\' and \\u{…}");
                return;
            }
        }
    }
}
