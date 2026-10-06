using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms;

namespace Plus.Communication.Packets.Incoming.Rooms.Furni.Stickys;

internal class UpdateStickyNoteEvent(IRoomInteractionService interactions) : RoomPacketEvent
{
    public override Task Parse(Room room, GameClient session, IIncomingPacket packet)
    {
        var itemId = packet.ReadUInt();
        var colour = packet.ReadString();
        var text = packet.ReadString();
        interactions.UpdateSticky(room, session, itemId, colour, text);

        return Task.CompletedTask;
    }
}
