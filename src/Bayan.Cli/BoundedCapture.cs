using System.Text;

namespace Bayan.Cli;

/// <summary>
/// Reads a stream to its end while keeping only the first and the last <see cref="Keep"/> bytes. What lies between
/// is counted, not stored, and shows as one <c>… N bytes elided …</c> line.
/// </summary>
internal static class BoundedCapture
{
    internal const int Keep = 64 * 1024;

    internal static Task<string> ReadAsync(Stream stream, int keep = Keep) => Task.Run(() =>
    {
        var head = new byte[keep];
        var tail = new byte[keep];
        var buffer = new byte[16 * 1024];
        int headLength = 0, tailStart = 0, tailLength = 0;
        long total = 0;
        int n;
        while ((n = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            total += n;
            var at = 0;
            if (headLength < keep)
            {
                at = Math.Min(n, keep - headLength);
                Array.Copy(buffer, 0, head, headLength, at);
                headLength += at;
            }
            for (; at < n; at++)
            {
                tail[(tailStart + tailLength) % keep] = buffer[at];
                if (tailLength < keep) tailLength++;
                else tailStart = (tailStart + 1) % keep;
            }
        }
        var last = new byte[tailLength];
        for (var i = 0; i < tailLength; i++) last[i] = tail[(tailStart + i) % keep];
        var elided = total - headLength - tailLength;
        // Nothing elided: decode the bytes as one, so a character is never cut at the head's end.
        if (elided == 0) return Encoding.UTF8.GetString([.. head.AsSpan(0, headLength), .. last]);
        return Encoding.UTF8.GetString(head, 0, headLength) + $"\n… {elided} bytes elided …\n" + Encoding.UTF8.GetString(last);
    });
}
