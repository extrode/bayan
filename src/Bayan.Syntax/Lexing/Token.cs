using Bayan.Syntax.Diagnostics;
using Bayan.Syntax.Text;

namespace Bayan.Syntax.Lexing;

[Flags]
public enum TokenFlags : byte
{
    None = 0,
    /// <summary>Whitespace or a comment separates this token from the previous one.</summary>
    TriviaBefore = 1,
    /// <summary>A line break separates this token from the previous one.</summary>
    NewlineBefore = 2,
}

/// <summary>
/// A token is a kind plus byte offsets: no text is copied. A run of <c>///</c> lines directly
/// before the token is attached as <see cref="Doc"/>.
/// </summary>
public readonly struct Token
{
    public Token(TokenKind kind, int start, int length, TokenFlags flags, int docStart, int docLength)
    {
        Kind = kind;
        Flags = flags;
        Start = start;
        Length = length;
        DocStart = docStart;
        DocLength = docLength;
    }

    public TokenKind Kind { get; }
    public TokenFlags Flags { get; }
    public int Start { get; }
    public int Length { get; }
    public int DocStart { get; }
    public int DocLength { get; }

    public int End => Start + Length;
    public TextSpan Span => new(Start, Length);
    public TextSpan Doc => new(DocStart, DocLength);
    public bool HasDoc => DocLength > 0;
    public bool HasTriviaBefore => (Flags & TokenFlags.TriviaBefore) != 0;
    public bool HasNewlineBefore => (Flags & TokenFlags.NewlineBefore) != 0;

    public override string ToString() => $"{Kind} {Span}";
}

public sealed class LexResult
{
    internal LexResult(SourceText source, Token[] tokens, int count, IReadOnlyList<Diagnostic> diagnostics)
    {
        Source = source;
        Buffer = tokens;
        Count = count;
        Diagnostics = diagnostics;
    }

    public SourceText Source { get; }

    /// <summary>Backing array; only the first <see cref="Count"/> entries are tokens. The last is EndOfFile.</summary>
    public Token[] Buffer { get; }

    public int Count { get; }

    public ReadOnlySpan<Token> Tokens => Buffer.AsSpan(0, Count);

    public IReadOnlyList<Diagnostic> Diagnostics { get; }
}
