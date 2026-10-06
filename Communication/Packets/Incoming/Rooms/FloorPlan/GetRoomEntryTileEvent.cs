using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms;

namespace Plus.Communication.Packets.Incoming.Rooms.FloorPlan;

internal sealed class GetRoomEntryTileEvent(IFloorPlanUpdateService service) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        service.ShowEntryTile(session);

        return Task.CompletedTask;
    }
}
