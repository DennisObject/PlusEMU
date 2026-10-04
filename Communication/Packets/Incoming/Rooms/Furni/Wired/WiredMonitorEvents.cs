using System.Runtime.CompilerServices;
using Plus.Communication.Packets.Outgoing.Rooms.Furni.Wired;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items.Wired.Modern.Actions;
using Plus.HabboHotel.Items.Wired.Settings;
using Plus.HabboHotel.Rooms;

namespace Plus.Communication.Packets.Incoming.Rooms.Furni.Wired;

/// <summary>Monitor snapshot (action 0) or clear the room log first (action 1, managers only).</summary>
public sealed class WiredMonitorRequestEvent : RoomPacketEvent
{
    private const int Fetch = 0, ClearLogs = 1;
    // The client polls every 250 ms; a clear has its own gate so a poll cannot swallow it.
    private readonly WiredRequestGate _fetches = new(200), _clears = new(200);

    public override Task Parse(Room room, GameClient session, IIncomingPacket packet)
    {
        int action;
        try { action = packet.HasDataRemaining() ? packet.ReadInt() : Fetch; }
        catch (ArgumentException) { return Task.CompletedTask; }
        if (action is not (Fetch or ClearLogs) || packet.HasDataRemaining()) return Task.CompletedTask;
        var settings = WiredRoomSettings.For(room);
        if (!settings.CanInspect(session)) return Task.CompletedTask;
        if (action == ClearLogs)
        {
            if (!settings.CanManage(session) || !_clears.TryPass(session)) return Task.CompletedTask;
            room.GetWired().ClearLogs();
        }
        else if (!_fetches.TryPass(session)) return Task.CompletedTask;
        session.Send(new WiredMonitorDataComposer(room.GetWired().ReadMonitor()));
        return Task.CompletedTask;
    }
}

/// <summary>Official AIR room log page request: 1-based page, page size, level and source filters (-1 for none) and text.</summary>
public sealed class WiredRoomLogsPageEvent : RoomPacketEvent
{
    // The client's filter box holds at most this many characters.
    private const int MaxQuery = 400;
    private readonly WiredRequestGate _pages = new(250);

    public override Task Parse(Room room, GameClient session, IIncomingPacket packet)
    {
        int page, size, level, source; string query;
        try { page = packet.ReadInt(); size = packet.ReadInt(); level = packet.ReadInt(); source = packet.ReadInt(); query = packet.ReadString(); }
        catch (ArgumentException) { return Task.CompletedTask; }
        // Every source the client can filter by is accepted; the ones Plus never writes read as empty.
        if (packet.HasDataRemaining() || level is < -1 or > 3 || source is < -1 or > (int)WiredLogSource.WiredLog || query.Length > MaxQuery)
            return Task.CompletedTask;
        if (!WiredRoomSettings.For(room).CanInspect(session) || !_pages.TryPass(session)) return Task.CompletedTask;
        query = query.Trim();
        var result = room.GetWired().ReadLogs(Math.Max(1, page) - 1, size, level, query, source);
        session.Send(new WiredRoomLogPageComposer(result, level, source, query));
        return Task.CompletedTask;
    }
}

/// <summary>A per-session minimum interval between requests, forgotten with the session.</summary>
internal sealed class WiredRequestGate(long intervalMs)
{
    private readonly ConditionalWeakTable<GameClient, StrongBox<long>> _last = new();

    public bool TryPass(GameClient session)
    {
        var now = Environment.TickCount64;
        var last = _last.GetValue(session, _ => new(now - intervalMs));
        lock (last)
        {
            if (now - last.Value < intervalMs) return false;
            last.Value = now;
            return true;
        }
    }
}
