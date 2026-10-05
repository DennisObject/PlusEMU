using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items.Wired.Modern.Actions;
using Plus.HabboHotel.Items.Wired.Variables;
using Plus.HabboHotel.Rooms;

namespace Plus.Communication.Packets.Incoming.Rooms.Furni.Wired;

/// <summary>Monitor snapshot (action 0) or clear the room log first (action 1, managers only).</summary>
public sealed class WiredMonitorRequestEvent(IWiredMonitorService monitor) : RoomPacketEvent
{
    public override Task Parse(Room room, GameClient session, IIncomingPacket packet)
    {
        int action;
        try { action = packet.HasDataRemaining() ? packet.ReadInt() : WiredMonitorActions.Fetch; }
        catch (ArgumentException) { return Task.CompletedTask; }
        if (action is not (WiredMonitorActions.Fetch or WiredMonitorActions.Clear) || packet.HasDataRemaining()) return Task.CompletedTask;
        monitor.ShowMonitor(room, session, action);
        return Task.CompletedTask;
    }
}

/// <summary>Official AIR room log page request: 1-based page, page size, level and source filters (-1 for none) and text.</summary>
public sealed class WiredRoomLogsPageEvent(IWiredMonitorService monitor) : RoomPacketEvent
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
        monitor.ShowLogs(room, session, page, size, level, source, query);
        return Task.CompletedTask;
    }
}
