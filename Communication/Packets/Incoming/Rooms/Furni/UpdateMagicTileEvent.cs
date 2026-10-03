using System.Drawing;
using Plus.Communication.Packets.Outgoing.Rooms.Engine;
using Plus.Communication.Packets.Outgoing.Rooms.Furni;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Rooms;

namespace Plus.Communication.Packets.Incoming.Rooms.Furni;

internal class UpdateMagicTileEvent : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        var room = AuthorizedRoom(session);
        if (room == null) return Task.CompletedTask;
        var itemId = packet.ReadUInt();
        var requestedHeight = packet.ReadInt();
        var item = room.GetRoomItemHandler().GetItem(itemId);
        if (item == null || item.IsTemporary || !MagicTileHeight.IsMagicTile(item.Definition.InteractionType))
            return Task.CompletedTask;

        bool? multiWalk = packet.HasDataRemaining() ? packet.ReadBool() : null;
        Apply(room, item, requestedHeight, multiWalk);
        return Task.CompletedTask;
    }

    internal static Room? AuthorizedRoom(GameClient session)
    {
        var habbo = session.GetHabbo();
        var room = habbo?.CurrentRoom;
        return habbo != null && room != null
            && (room.CheckRights(session, false, true) || habbo.Permissions.HasRight("room_item_use_any_stack_tile")) ? room : null;
    }

    internal static void Apply(Room room, Item item, int requestedHeight, bool? multiWalk = null)
    {
        var footprint = item.GetCoords.Distinct().ToArray();
        var floorZ = footprint.Max(tile => (double)room.GetGameMap().Model.SqFloorHeight[tile.X, tile.Y]);
        var stackBelowZ = footprint.Max(tile => room.GetGameMap().ResolvePlacement(tile.X, tile.Y, item.Id).PlacementZ);
        var height = MagicTileHeight.Resolve(requestedHeight, floorZ, stackBelowZ);

        if (!room.GetRoomItemHandler().SetFloorItem(item, item.GetX, item.GetY, height))
            return;
        if (item.Definition.InteractionType == InteractionType.WalkMagicTile && multiWalk.HasValue)
        {
            item.ExtraData = new Plus.HabboHotel.Items.DataFormat.LegacyDataFormat
                { Data = MagicTileHeight.ToWire(height).ToString(System.Globalization.CultureInfo.InvariantCulture) + (multiWalk.Value ? ";1" : "") };
            room.GetRoomItemHandler().UpdateItem(item);
        }
        room.GetRoomUserManager().UpdateUserStatusses();
        room.SendPacket(new ObjectUpdateComposer(item));
        room.SendPacket(new UpdateMagicTileComposer(item.Id, MagicTileHeight.ToWire(height)));
    }

}
