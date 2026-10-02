using Plus.Communication.Packets.Outgoing.WiredVariables;
using Plus.Communication.Packets.Incoming.Rooms;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items.Wired.Variables;
using Plus.HabboHotel.Rooms;

namespace Plus.Communication.Packets.Incoming.WiredVariables;

public sealed class WiredAllVariablesRequestEvent : RoomPacketEvent
{
    public override Task Parse(Room room, GameClient session, IIncomingPacket packet)
    {
        if (!room.CheckRights(session, false, true) || packet.HasDataRemaining()) return Task.CompletedTask;
        session.Send(new WiredAllVariablesHashComposer(room.GetWired().Variables.Catalog().Hash));
        return Task.CompletedTask;
    }
}

public sealed class WiredVariableHashesEvent : RoomPacketEvent
{
    public override Task Parse(Room room, GameClient session, IIncomingPacket packet)
    {
        if (!room.CheckRights(session, false, true)) return Task.CompletedTask;
        if (!TryReadHashes(packet, out var hashes)) return Task.CompletedTask;
        foreach (var diff in room.GetWired().Variables.Catalog().Diff(hashes)) session.Send(new WiredAllVariablesDiffComposer(diff));
        return Task.CompletedTask;
    }
    public static bool TryReadHashes(IIncomingPacket packet, out IReadOnlyDictionary<string, int> hashes)
    {
        var result = new Dictionary<string, int>(StringComparer.Ordinal); hashes = result;
        try
        {
            var count = packet.ReadInt(); if (count is < 0 or > 4096) return false;
            for (var i = 0; i < count; i++)
            {
                var id = packet.ReadString(); var hash = packet.ReadInt();
                if (id.Length is < 1 or > 64 || !result.TryAdd(id, hash)) return false;
            }
            return !packet.HasDataRemaining();
        }
        catch (ArgumentException) { return false; }
    }
}

public sealed class WiredVariableHoldersRequestEvent : RoomPacketEvent
{
    public override Task Parse(Room room, GameClient session, IIncomingPacket packet)
    {
        if (!room.CheckRights(session, false, true)) return Task.CompletedTask;
        string id;
        try { id = packet.ReadString(); } catch (ArgumentException) { return Task.CompletedTask; }
        if (id.Length is < 1 or > 64 || packet.HasDataRemaining()) return Task.CompletedTask;
        var menu = new WiredVariableMenu(room, room.GetWired().Variables);
        if (menu.Catalog().Find(id) is { } variable) session.Send(new WiredVariableHoldersComposer(room.Id, variable, menu.Live(variable)));
        return Task.CompletedTask;
    }
}

public sealed class WiredVariableHoldersPageEvent : RoomPacketEvent
{
    public override Task Parse(Room room, GameClient session, IIncomingPacket packet)
    {
        if (!room.CheckRights(session, false, true)) return Task.CompletedTask;
        string id; int page, size, users, sort;
        try { id = packet.ReadString(); page = packet.ReadInt(); size = packet.ReadInt(); users = packet.ReadInt(); sort = packet.ReadInt(); }
        catch (ArgumentException) { return Task.CompletedTask; }
        if (id.Length is < 1 or > 64 || users is not (0 or 1) || sort is < -1 or > 2 || packet.HasDataRemaining()) return Task.CompletedTask;
        var menu = new WiredVariableMenu(room, room.GetWired().Variables);
        if (menu.Catalog().Find(id) is { } variable)
            session.Send(new WiredVariableHoldersPageComposer(id, menu.Page(variable, page, size, users, sort), users, sort));
        return Task.CompletedTask;
    }
}
