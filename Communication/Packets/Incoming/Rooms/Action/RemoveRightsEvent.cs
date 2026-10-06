using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms;

namespace Plus.Communication.Packets.Incoming.Rooms.Action;

internal class RemoveRightsEvent(IRoomRightsService rights) : RoomPacketEvent
{
    public override Task Parse(Room room, GameClient session, IIncomingPacket packet)
    {
        var amount = packet.ReadInt();
        var userIds = new List<int>();
        for (var index = 0; index < amount && index <= 100; index++) userIds.Add(packet.ReadInt());
        rights.Remove(room, session, userIds);
        return Task.CompletedTask;
    }
}
