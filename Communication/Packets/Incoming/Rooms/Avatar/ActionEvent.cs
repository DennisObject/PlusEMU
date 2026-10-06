using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms;

namespace Plus.Communication.Packets.Incoming.Rooms.Avatar;

public class ActionEvent(IRoomAvatarActionService actions) : RoomPacketEvent
{
    public override Task Parse(Room room, GameClient session, IIncomingPacket packet)
    {
        actions.PerformAction(room, session, packet.ReadInt());
        return Task.CompletedTask;
    }
}
