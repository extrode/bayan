using Bayan.Syntax.Diagnostics;

namespace Bayan.Cli;

/// <summary>
/// The single progress line on stderr (spec 005 R13): drawn only once the operation outlives the threshold,
/// rewritten in place, and removed by <see cref="Done"/>. Never drawn when stderr is not a terminal, in JSON
/// mode, or when <c>CI</c> is set. The ASCII form prints <c>... phase</c> once without rewriting.
/// </summary>
internal sealed class Progress : IDisposable
{
    public static readonly TimeSpan DefaultThreshold = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan s_interval = TimeSpan.FromMilliseconds(250);

    private readonly object _gate = new();
    private readonly TextWriter _writer;
    private readonly string[] _frames;
    private readonly string _phase;
    private readonly Timer? _timer;
    private int _frame;
    private bool _shown;
    private bool _done;

    private Progress(TextWriter writer, Style style, string phase, TimeSpan threshold)
    {
        (_writer, _frames, _phase) = (writer, style.Symbols.Progress, phase);
        if (threshold <= TimeSpan.Zero) Tick();
        else _timer = new Timer(_ => Tick(), null, threshold, _frames.Length > 1 ? s_interval : Timeout.InfiniteTimeSpan);
    }

    /// <summary>Starts the line, or returns an inert instance when it must not be shown.</summary>
    public static Progress Start(TextWriter writer, Style style, bool isTty, bool json, Func<string, string?> env, string phase, TimeSpan? threshold = null)
    {
        var off = !isTty || json || env("CI") is { Length: > 0 };
        return new Progress(off ? TextWriter.Null : writer, style, phase, threshold ?? DefaultThreshold);
    }

    private void Tick()
    {
        lock (_gate)
        {
            if (_done) return;
            if (_shown && _frames.Length == 1) return;
            _writer.Write($"\r{_frames[_frame++ % _frames.Length]} {_phase}");
            _writer.Flush();
            _shown = true;
        }
    }

    /// <summary>Stops the timer and clears the line; call before any other stderr write. Safe to call twice.</summary>
    public void Done()
    {
        lock (_gate)
        {
            if (_done) return;
            _done = true;
            _timer?.Dispose();
            if (!_shown) return;
            _writer.Write(_frames.Length > 1 ? "\r\e[2K" : "\n");
            _writer.Flush();
        }
    }

    public void Dispose() => Done();
}
