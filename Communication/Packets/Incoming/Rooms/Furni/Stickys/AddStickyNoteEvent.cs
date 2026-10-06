using Plus.Communication.Packets.Incoming.Rooms;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms;

namespace Plus.Communication.Packets.Incoming.Rooms.Furni.Stickys;

internal class AddStickyNoteEvent(IRoomItemPlacementService placement) : RoomPacketEvent
{
    public override Task Parse(Room room, GameClient session, IIncomingPacket packet)
    {
        var itemId = packet.ReadUInt();
        var location = packet.ReadString();
        placement.PlaceSticky(room, session, itemId, location);

        return Task.CompletedTask;
    }
}
