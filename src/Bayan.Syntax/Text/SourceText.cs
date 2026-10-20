using System.Text;

namespace Bayan.Syntax.Text;

/// <summary>
/// One source file as UTF-8 bytes. Positions everywhere else in the compiler are byte offsets
/// into <see cref="Bytes"/>; line and column are computed only when a human needs them.
/// </summary>
public sealed class SourceText
{
    private int[]? _lineStarts;

    public SourceText(string path, ReadOnlyMemory<byte> bytes)
    {
        Path = path;
        Bytes = bytes;
    }

    public string Path { get; }

    public ReadOnlyMemory<byte> Bytes { get; }

    public int Length => Bytes.Length;

    public static SourceText FromString(string path, string text) => new(path, Encoding.UTF8.GetBytes(text));

    public static SourceText FromFile(string path) => new(path, File.ReadAllBytes(path));

    public int LineCount => LineStarts.Length;

    private int[] LineStarts => _lineStarts ??= ComputeLineStarts(Bytes.Span);

    /// <summary>Zero-based line index containing <paramref name="offset"/>.</summary>
    public int GetLineIndex(int offset)
    {
        if ((uint)offset > (uint)Length)
            throw new ArgumentOutOfRangeException(nameof(offset));
        var index = Array.BinarySearch(LineStarts, offset);
        return index >= 0 ? index : ~index - 1;
    }

    /// <summary>The span of line <paramref name="lineIndex"/>, excluding its line terminator.</summary>
    public TextSpan GetLineSpan(int lineIndex)
    {
        var start = LineStarts[lineIndex];
        var end = lineIndex + 1 < LineStarts.Length ? LineStarts[lineIndex + 1] : Length;
        var bytes = Bytes.Span;
        if (end > start && bytes[end - 1] == (byte)'\n') end--;
        if (end > start && bytes[end - 1] == (byte)'\r') end--;
        return TextSpan.FromBounds(start, end);
    }

    /// <summary>One-based line and column; the column counts Unicode scalars, not bytes.</summary>
    public (int Line, int Column) GetLinePosition(int offset)
    {
        var line = GetLineIndex(offset);
        var start = LineStarts[line];
        var column = 1;
        var bytes = Bytes.Span[start..offset];
        foreach (var b in bytes)
            if ((b & 0xC0) != 0x80) column++;
        return (line + 1, column);
    }

    public string GetText(TextSpan span) => Encoding.UTF8.GetString(Bytes.Span.Slice(span.Start, span.Length));

    private static int[] ComputeLineStarts(ReadOnlySpan<byte> bytes)
    {
        var starts = new List<int> { 0 };
        for (var i = 0; i < bytes.Length; i++)
        {
            if (bytes[i] == (byte)'\n') starts.Add(i + 1);
        }
        return starts.ToArray();
    }
}
