using Plus.Communication.Packets.Outgoing.WiredVariables;
using Plus.Communication.Packets.Incoming.Rooms;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items.Wired.Variables;
using Plus.HabboHotel.Rooms;

namespace Plus.Communication.Packets.Incoming.WiredVariables;

public sealed class WiredAllVariablesRequestEvent(IWiredVariableMenuService menus) : RoomPacketEvent
{
    public override Task Parse(Room room, GameClient session, IIncomingPacket packet)
    {
        if (!room.GetWired().Settings.CanInspect(session) || packet.HasDataRemaining()) return Task.CompletedTask;
        session.Send(new WiredAllVariablesHashComposer(menus.CatalogHash(room)));
        return Task.CompletedTask;
    }
}

public sealed class WiredVariableHashesEvent(IWiredVariableMenuService menus) : RoomPacketEvent
{
    public override Task Parse(Room room, GameClient session, IIncomingPacket packet)
    {
        if (!room.GetWired().Settings.CanInspect(session)) return Task.CompletedTask;
        if (!TryReadHashes(packet, out var hashes)) return Task.CompletedTask;
        foreach (var diff in menus.CatalogDiff(room, hashes)) session.Send(new WiredAllVariablesDiffComposer(diff));
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

public sealed class WiredVariableHoldersRequestEvent(IWiredVariableMenuService menus) : RoomPacketEvent
{
    public override Task Parse(Room room, GameClient session, IIncomingPacket packet)
    {
        if (!room.GetWired().Settings.CanInspect(session)) return Task.CompletedTask;
        string id;
        try { id = packet.ReadString(); } catch (ArgumentException) { return Task.CompletedTask; }
        if (id.Length is < 1 or > 64 || packet.HasDataRemaining()) return Task.CompletedTask;
        if (menus.Holders(room, id) is { } holders) session.Send(new WiredVariableHoldersComposer(room.Id, holders.Variable, holders.Holders));
        return Task.CompletedTask;
    }
}

public sealed class WiredVariableHoldersPageEvent(IWiredVariableMenuService menus) : RoomPacketEvent
{
    public override Task Parse(Room room, GameClient session, IIncomingPacket packet)
    {
        if (!room.GetWired().Settings.CanInspect(session)) return Task.CompletedTask;
        string id; int page, size, users, sort;
        try { id = packet.ReadString(); page = packet.ReadInt(); size = packet.ReadInt(); users = packet.ReadInt(); sort = packet.ReadInt(); }
        catch (ArgumentException) { return Task.CompletedTask; }
        if (id.Length is < 1 or > 64 || users is not (0 or 1) || sort is < -1 or > 2 || packet.HasDataRemaining()) return Task.CompletedTask;
        if (menus.HolderPage(room, id, page, size, users, sort) is { } view)
            session.Send(new WiredVariableHoldersPageComposer(id, view.Page, users, sort));
        return Task.CompletedTask;
    }
}
