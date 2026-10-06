using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms;

namespace Plus.Communication.Packets.Incoming.Rooms.Furni.Stickys;

internal sealed class DeleteStickyNoteEvent(IRoomInteractionService interactions) : RoomPacketEvent
{
    public override Task Parse(Room room, GameClient session, IIncomingPacket packet)
    {
        interactions.DeleteSticky(room, session, packet.ReadUInt());
        return Task.CompletedTask;
    }
}
