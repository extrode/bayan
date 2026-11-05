using System.Text;

namespace Bayan.Cli;

/// <summary>
/// Reads a stream to its end while keeping only the first and the last <see cref="Keep"/> bytes. What lies between
/// is counted, not stored, and shows as one <c>… N bytes elided …</c> line. The read runs on its own thread, so a
/// busy thread pool cannot delay it, and <see cref="Snapshot"/> gives what was read so far when the reader cannot be
/// waited for (a grandchild still holding the pipe open after a kill).
/// </summary>
internal sealed class BoundedCapture
{
    internal const int Keep = 64 * 1024;

    private readonly object _gate = new();
    private readonly int _keep;
    private readonly byte[] _head;
    private readonly byte[] _tail;
    private int _headLength, _tailStart, _tailLength;
    private long _total;

    private BoundedCapture(int keep)
    {
        _keep = keep;
        _head = new byte[keep];
        _tail = new byte[keep];
    }

    /// <summary>The capture, filled as the stream is read; <see cref="Completion"/> ends with the stream.</summary>
    internal Task<string> Completion { get; private set; } = null!;

    internal static BoundedCapture Start(Stream stream, int keep = Keep)
    {
        var capture = new BoundedCapture(keep);
        capture.Completion = Task.Factory.StartNew(() =>
        {
            var buffer = new byte[16 * 1024];
            int n;
            while ((n = stream.Read(buffer, 0, buffer.Length)) > 0) capture.Append(buffer.AsSpan(0, n));
            return capture.Snapshot();
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        return capture;
    }

    internal static Task<string> ReadAsync(Stream stream, int keep = Keep) => Start(stream, keep).Completion;

    private void Append(ReadOnlySpan<byte> data)
    {
        lock (_gate)
        {
            _total += data.Length;
            if (_headLength < _keep)
            {
                var take = Math.Min(data.Length, _keep - _headLength);
                data[..take].CopyTo(_head.AsSpan(_headLength));
                _headLength += take;
                data = data[take..];
            }
            if (data.Length >= _keep)
            {
                data[^_keep..].CopyTo(_tail);
                _tailStart = 0;
                _tailLength = _keep;
                return;
            }
            while (data.Length > 0)
            {
                var end = (_tailStart + _tailLength) % _keep;
                var take = Math.Min(data.Length, _keep - end);
                data[..take].CopyTo(_tail.AsSpan(end));
                data = data[take..];
                var overflow = _tailLength + take - _keep;
                if (overflow > 0)
                {
                    _tailStart = (_tailStart + overflow) % _keep;
                    _tailLength = _keep;
                }
                else _tailLength += take;
            }
        }
    }

    /// <summary>What has been read so far, in the same form as the finished capture.</summary>
    internal string Snapshot()
    {
        lock (_gate)
        {
            var last = new byte[_tailLength];
            for (var i = 0; i < _tailLength; i++) last[i] = _tail[(_tailStart + i) % _keep];
            var elided = _total - _headLength - _tailLength;
            // Nothing elided: decode the bytes as one, so a character is never cut at the head's end.
            if (elided == 0) return Encoding.UTF8.GetString([.. _head.AsSpan(0, _headLength), .. last]);
            return Encoding.UTF8.GetString(_head, 0, _headLength) + $"\n… {elided} bytes elided …\n" + Encoding.UTF8.GetString(last);
        }
    }
}
