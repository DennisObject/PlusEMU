using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items;

namespace Plus.Communication.Packets.Incoming.Rooms.Furni;

internal class UpdateMagicTileAdjacentEvent : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        var room = UpdateMagicTileEvent.AuthorizedRoom(session);
        if (room == null) return Task.CompletedTask;
        var itemId = packet.ReadUInt();
        var moveDown = packet.ReadBool();
        var item = room.GetRoomItemHandler().GetItem(itemId);
        if (item == null || item.IsTemporary || !MagicTileHeight.IsMagicTile(item.Definition.InteractionType))
            return Task.CompletedTask;
        // TODO capture official adjacent-height semantics. Polaris uses one hundredth as a stand-in.
        UpdateMagicTileEvent.Apply(room, item, MagicTileHeight.ToWire(item.GetZ) + (moveDown ? -1 : 1));
        return Task.CompletedTask;
    }
}
