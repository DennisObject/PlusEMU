using Plus.HabboHotel.Items.Wired.Modern.Actions;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.Instance;

namespace Plus.HabboHotel.Items.Wired.Variables;

public sealed record WiredRoomLogView(WiredRoomLogPage Page, int Level, int Source, string Query);

public interface IWiredMonitorService
{
    WiredMonitorSnapshot Monitor(Room room);
    void ClearLogs(Room room);
    WiredRoomLogView Logs(Room room, int page, int size, int level, int source, string query);
}

/// <summary>Monitor and room log inspection; callers check inspect and manage rights and the rate gates first.</summary>
public sealed class WiredMonitorService : IWiredMonitorService
{
    public WiredMonitorSnapshot Monitor(Room room) => room.GetWired().ReadMonitor();

    public void ClearLogs(Room room) => room.GetWired().ClearLogs();

    // The client's pages are 1-based, and the filter text is trimmed before it is matched.
    public WiredRoomLogView Logs(Room room, int page, int size, int level, int source, string query)
    {
        query = query.Trim();
        var result = room.GetWired().ReadLogs(Math.Max(1, page) - 1, size, level, query, source);
        return new(result, level, source, query);
    }
}
