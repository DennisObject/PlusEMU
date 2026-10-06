using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms;

namespace Plus.Communication.Packets.Incoming.Rooms.Action;

internal class AssignRightsEvent(IRoomRightsService rights) : RoomPacketEvent
{
    public override Task Parse(Room room, GameClient session, IIncomingPacket packet)
    {
        rights.Assign(room, session, packet.ReadInt());

        return Task.CompletedTask;
    }
}
