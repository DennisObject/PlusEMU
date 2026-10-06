using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms;

namespace Plus.Communication.Packets.Incoming.Rooms.Avatar;

internal class ApplySignEvent(IRoomAvatarActionService actions) : RoomPacketEvent
{
    public override Task Parse(Room room, GameClient session, IIncomingPacket packet)
    {
        actions.ApplySign(room, session, packet.ReadInt());

        return Task.CompletedTask;
    }
}
