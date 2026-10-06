using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Incoming.Rooms.Settings;

internal sealed class GetRoomFilterListEvent(IRoomFilterService filters) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        filters.Show(session);
        return Task.CompletedTask;
    }
}
