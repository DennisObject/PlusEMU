using Plus.Communication.Packets.Outgoing.Rooms.Furni.Wired;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items.Wired.Modern.Actions;
using Plus.HabboHotel.Items.Wired.Variables;
using Plus.HabboHotel.Rooms;

namespace Plus.Communication.Packets.Incoming.Rooms.Furni.Wired;

/// <summary>Monitor snapshot (action 0) or clear the room log first (action 1, managers only).</summary>
public sealed class WiredMonitorRequestEvent(IWiredRequestGateService gates, IWiredMonitorService monitor) : RoomPacketEvent
{
    private const int Fetch = 0, ClearLogs = 1;

    public override Task Parse(Room room, GameClient session, IIncomingPacket packet)
    {
        int action;
        try { action = packet.HasDataRemaining() ? packet.ReadInt() : Fetch; }
        catch (ArgumentException) { return Task.CompletedTask; }
        if (action is not (Fetch or ClearLogs) || packet.HasDataRemaining()) return Task.CompletedTask;
        var settings = room.GetWired().Settings;
        if (!settings.CanInspect(session)) return Task.CompletedTask;
        if (action == ClearLogs)
        {
            if (!settings.CanManage(session) || !gates.TryPass(session, WiredRequestKind.MonitorClear)) return Task.CompletedTask;
            monitor.ClearLogs(room);
        }
        else if (!gates.TryPass(session, WiredRequestKind.MonitorFetch)) return Task.CompletedTask;
        session.Send(new WiredMonitorDataComposer(monitor.Monitor(room)));
        return Task.CompletedTask;
    }
}

/// <summary>Official AIR room log page request: 1-based page, page size, level and source filters (-1 for none) and text.</summary>
public sealed class WiredRoomLogsPageEvent(IWiredRequestGateService gates, IWiredMonitorService monitor) : RoomPacketEvent
{
    // The client's filter box holds at most this many characters.
    private const int MaxQuery = 400;

    public override Task Parse(Room room, GameClient session, IIncomingPacket packet)
    {
        int page, size, level, source; string query;
        try { page = packet.ReadInt(); size = packet.ReadInt(); level = packet.ReadInt(); source = packet.ReadInt(); query = packet.ReadString(); }
        catch (ArgumentException) { return Task.CompletedTask; }
        // Every source the client can filter by is accepted; the ones Plus never writes read as empty.
        if (packet.HasDataRemaining() || level is < -1 or > 3 || source is < -1 or > (int)WiredLogSource.WiredLog || query.Length > MaxQuery)
            return Task.CompletedTask;
        if (!room.GetWired().Settings.CanInspect(session) || !gates.TryPass(session, WiredRequestKind.RoomLogPage)) return Task.CompletedTask;
        var view = monitor.Logs(room, page, size, level, source, query);
        session.Send(new WiredRoomLogPageComposer(view.Page, view.Level, view.Source, view.Query));
        return Task.CompletedTask;
    }
}
