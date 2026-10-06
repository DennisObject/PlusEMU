using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms;

namespace Plus.Communication.Packets.Incoming.Rooms.Furni.Stickys;

internal class GetStickyNoteEvent(IRoomInteractionService interactions) : RoomPacketEvent
{
    public override Task Parse(Room room, GameClient session, IIncomingPacket packet)
    {
        var itemId = packet.ReadUInt();
        interactions.ShowSticky(room, session, itemId);

        return Task.CompletedTask;
    }
}
