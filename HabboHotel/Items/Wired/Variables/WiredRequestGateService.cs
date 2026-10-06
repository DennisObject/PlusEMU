using System.Runtime.CompilerServices;
using Plus.HabboHotel.GameClients;

namespace Plus.HabboHotel.Items.Wired.Variables;

/// <summary>Independent per-session request kinds; a poll cannot swallow a clear or a page request.</summary>
public enum WiredRequestKind { MonitorFetch, MonitorClear, RoomLogPage }

public interface IWiredRequestGateService
{
    bool TryPass(GameClient session, WiredRequestKind kind);
}

/// <summary>Per-session minimum intervals on the monotonic clock. Sessions are held weakly, and the first request passes.</summary>
public sealed class WiredRequestGateService(TimeProvider clock) : IWiredRequestGateService
{
    private static readonly TimeSpan[] Intervals = [TimeSpan.FromMilliseconds(200), TimeSpan.FromMilliseconds(200), TimeSpan.FromMilliseconds(250)];
    private readonly ConditionalWeakTable<GameClient, StrongBox<long?>>[] _last = [new(), new(), new()];

    public bool TryPass(GameClient session, WiredRequestKind kind)
    {
        var now = clock.GetTimestamp();
        var last = _last[(int)kind].GetValue(session, _ => new StrongBox<long?>());
        lock (last)
        {
            if (last.Value is { } previous && clock.GetElapsedTime(previous, now) < Intervals[(int)kind]) return false;
            last.Value = now;
            return true;
        }
    }
}
