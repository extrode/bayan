namespace Bayan.Syntax.Text;

/// <summary>A half-open range of UTF-8 byte offsets into a <see cref="SourceText"/>.</summary>
public readonly record struct TextSpan(int Start, int Length)
{
    public int End => Start + Length;

    public static TextSpan FromBounds(int start, int end) => new(start, end - start);

    public override string ToString() => $"[{Start}..{End})";
}
