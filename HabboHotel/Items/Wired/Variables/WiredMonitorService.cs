using Plus.Communication.Packets.Outgoing.Rooms.Furni.Wired;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms;

namespace Plus.HabboHotel.Items.Wired.Variables;

public static class WiredMonitorActions
{
    public const int Fetch = 0;
    public const int Clear = 1;
}

public interface IWiredMonitorService
{
    void ShowMonitor(Room room, GameClient session, int action);
    void ShowLogs(Room room, GameClient session, int page, int size, int level, int source, string query);
}

/// <summary>Monitor and room log inspection; owns the inspect and manage rights, the rate gates and every response it sends.</summary>
public sealed class WiredMonitorService(IWiredRequestGateService gates) : IWiredMonitorService
{
    public void ShowMonitor(Room room, GameClient session, int action)
    {
        var settings = room.GetWired().Settings;

        if (!settings.CanInspect(session))
        {
            return;
        }

        if (action == WiredMonitorActions.Clear)
        {
            // A clear has its own gate so a poll cannot swallow it.
            if (!settings.CanManage(session) || !gates.TryPass(session, WiredRequestKind.MonitorClear))
            {
                return;
            }

            room.GetWired().ClearLogs();
        }
        else if (!gates.TryPass(session, WiredRequestKind.MonitorFetch))
        {
            return;
        }

        session.Send(new WiredMonitorDataComposer(room.GetWired().ReadMonitor()));
    }

    // The client's pages are 1-based, and the filter text is trimmed before it is matched.
    public void ShowLogs(Room room, GameClient session, int page, int size, int level, int source, string query)
    {
        if (!room.GetWired().Settings.CanInspect(session) || !gates.TryPass(session, WiredRequestKind.RoomLogPage))
        {
            return;
        }

        query = query.Trim();
        var result = room.GetWired().ReadLogs(Math.Max(1, page) - 1, size, level, query, source);
        session.Send(new WiredRoomLogPageComposer(result, level, source, query));
    }
}
