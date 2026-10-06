using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms;

namespace Plus.Communication.Packets.Incoming.Rooms.Settings;

internal sealed class GetRoomBannedUsersEvent(IRoomBannedUsersService users) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        users.Send(session);
        return Task.CompletedTask;
    }
}
