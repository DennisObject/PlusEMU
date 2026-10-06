using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms;

namespace Plus.Communication.Packets.Incoming.Rooms.Action;

internal sealed class GiveRoomScoreEvent(IRoomInteractionService interactions) : RoomPacketEvent
{
    public override Task Parse(Room room, GameClient session, IIncomingPacket packet)
    {
        interactions.Rate(room, session, packet.ReadInt());

        return Task.CompletedTask;
    }
}
