using Plus.Communication.Packets.Incoming.Rooms;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items.Wired.Variables;
using Plus.HabboHotel.Rooms;
using Plus.Communication.Packets.Outgoing.WiredVariables;

namespace Plus.Communication.Packets.Incoming.WiredVariables;

public sealed class WiredAllVariablesRequestEvent(IWiredVariableMenuService menus) : RoomPacketEvent
{
    public override Task Parse(Room room, GameClient session, IIncomingPacket packet)
    {
        if (packet.HasDataRemaining()) {
            return Task.CompletedTask;
        }

        menus.ShowCatalogHash(room, session);

        return Task.CompletedTask;
    }
}

public sealed class WiredVariableHashesEvent(IWiredVariableMenuService menus) : RoomPacketEvent
{
    public override Task Parse(Room room, GameClient session, IIncomingPacket packet)
    {
        if (!TryReadHashes(packet, out var hashes)) {
            return Task.CompletedTask;
        }

        menus.ShowCatalogDiff(room, session, hashes);

        return Task.CompletedTask;
    }
    public static bool TryReadHashes(IIncomingPacket packet, out IReadOnlyDictionary<string, int> hashes)
    {
        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        hashes = result;

        try {
            var count = packet.ReadInt();

            if (count is < 0 or > 4096) {
                return false;
            }

            for (var i = 0; i < count; i++) {
                var id = packet.ReadString();
                var hash = packet.ReadInt();

                if (id.Length is < 1 or > 64 || !result.TryAdd(id, hash)) {
                    return false;
                }
            }

            return !packet.HasDataRemaining();
        }
        catch (ArgumentException) {
            return false;
        }
    }
}

public sealed class WiredVariableHoldersRequestEvent(IWiredVariableMenuService menus) : RoomPacketEvent
{
    public override Task Parse(Room room, GameClient session, IIncomingPacket packet)
    {
        string id;

        try {
            id = packet.ReadString();
        }
        catch (ArgumentException) {
            return Task.CompletedTask;
        }

        if (id.Length is < 1 or > 64 || !WiredVariableWireProtocol.TryReadRequestVersion(packet, out var exact)) {
            return Task.CompletedTask;
        }

        if (exact) {
            WiredVariableWireProtocol.Enable(session);
        }

        menus.ShowHolders(room, session, id);

        return Task.CompletedTask;
    }
}

public sealed class WiredVariableHoldersPageEvent(IWiredVariableMenuService menus) : RoomPacketEvent
{
    public override Task Parse(Room room, GameClient session, IIncomingPacket packet)
    {
        string id;
        int page, size, users, sort;

        try {
            id = packet.ReadString();
            page = packet.ReadInt();
            size = packet.ReadInt();
            users = packet.ReadInt();
            sort = packet.ReadInt();
        }
        catch (ArgumentException) {
            return Task.CompletedTask;
        }

        if (id.Length is < 1 or > 64 || users is not (0 or 1) || sort is < -1 or > 2 || !WiredVariableWireProtocol.TryReadRequestVersion(packet, out var exact)) {
            return Task.CompletedTask;
        }

        if (exact) {
            WiredVariableWireProtocol.Enable(session);
        }

        menus.ShowHolderPage(room, session, id, page, size, users, sort);

        return Task.CompletedTask;
    }
}
