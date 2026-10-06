using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms;

namespace Plus.Communication.Packets.Incoming.Rooms.Settings;

internal class GetRoomRightsEvent(IRoomRightsService rights) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        rights.Show(session);
        return Task.CompletedTask;
    }
}
