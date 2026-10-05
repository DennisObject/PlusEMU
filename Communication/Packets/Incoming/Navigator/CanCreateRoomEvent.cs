using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms;

namespace Plus.Communication.Packets.Incoming.Navigator;

internal class CanCreateRoomEvent(IRoomCreationService rooms) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet) => rooms.SendCreationAvailability(session);
}
