namespace Plus.HabboHotel.Items.Wired;

/// <summary>
/// The last full window of engine passes that ran at least one box: the busiest pass's executions,
/// the average and longest pass time and the deepest chain. Pending is the queue at the time of reading.
/// </summary>
public readonly record struct WiredEngineWindow(int WindowMs, int PeakExecutions, int AverageMs, int PeakMs, int PeakDepth, int Pending);

// Written at the end of each outermost pass and read under the engine lock, so it has its own
// lock, always taken after the engine's, and never calls back into the engine.
internal sealed class WiredEngineStats(int windowMs = 1000)
{
    private readonly object _gate = new();
    private long _windowStart = long.MinValue;
    private Window _current, _previous;

    public void Record(long now, double elapsedMs, int executions, int depth)
    {
        lock (_gate)
        {
            Roll(now);
            if (executions <= 0) return;
            _current.Passes++;
            _current.TotalMs += elapsedMs;
            _current.PeakMs = Math.Max(_current.PeakMs, elapsedMs);
            _current.PeakExecutions = Math.Max(_current.PeakExecutions, executions);
            _current.PeakDepth = Math.Max(_current.PeakDepth, depth);
        }
    }

    public WiredEngineWindow Read(long now, int pending)
    {
        lock (_gate)
        {
            Roll(now);
            var window = _previous;
            return new(windowMs, window.PeakExecutions, window.Passes == 0 ? 0 : (int)Math.Round(window.TotalMs / window.Passes),
                (int)Math.Round(window.PeakMs), window.PeakDepth, pending);
        }
    }

    private void Roll(long now)
    {
        if (_windowStart == long.MinValue) { _windowStart = now; return; }
        var elapsed = (now - _windowStart) / windowMs;
        if (elapsed <= 0) return;
        // A gap longer than one window means the window before this one saw no passes.
        _previous = elapsed == 1 ? _current : default;
        _current = default;
        _windowStart += elapsed * windowMs;
    }

    private struct Window
    {
        public int Passes, PeakExecutions, PeakDepth;
        public double TotalMs, PeakMs;
    }
}
