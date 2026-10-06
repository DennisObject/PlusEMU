using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms;

namespace Plus.Communication.Packets.Incoming.Rooms.Avatar;

internal class LookToEvent(IRoomAvatarActionService actions) : RoomPacketEvent
{
    public override Task Parse(Room room, GameClient session, IIncomingPacket packet)
    {
        var x = packet.ReadInt();
        var y = packet.ReadInt();
        actions.LookTo(room, session, x, y);

        return Task.CompletedTask;
    }
}
