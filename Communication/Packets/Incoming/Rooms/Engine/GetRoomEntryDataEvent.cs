using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms;

namespace Plus.Communication.Packets.Incoming.Rooms.Engine;

internal class GetRoomEntryDataEvent(IRoomEntryService entry) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        entry.Enter(session);

        return Task.CompletedTask;
    }
}
