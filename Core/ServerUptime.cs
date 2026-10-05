using Plus.Utilities.DependencyInjection;

namespace Plus.Core;

[Singleton]
public interface IServerUptime
{
    DateTimeOffset? StartedAt { get; }
    TimeSpan Elapsed { get; }
    void Start();
}

public sealed class ServerUptime(TimeProvider clock) : IServerUptime
{
    private readonly object _gate = new();
    private RunStart? _start;

    public DateTimeOffset? StartedAt => Volatile.Read(ref _start)?.UtcTime;
    public TimeSpan Elapsed
    {
        get
        {
            var start = Volatile.Read(ref _start);
            if (start == null)
                return TimeSpan.Zero;
            var elapsed = clock.GetElapsedTime(start.Timestamp);
            return elapsed < TimeSpan.Zero ? TimeSpan.Zero : elapsed;
        }
    }

    public void Start()
    {
        lock (_gate)
        {
            if (_start != null)
                return;
            Volatile.Write(ref _start, new(clock.GetUtcNow(), clock.GetTimestamp()));
        }
    }

    private sealed record RunStart(DateTimeOffset UtcTime, long Timestamp);
}
