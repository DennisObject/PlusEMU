using Plus.Communication.Attributes;
using Plus.Communication.Packets.Outgoing.Housekeeping;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Housekeeping;

namespace Plus.Communication.Packets.Incoming.Housekeeping;

[RequiresPermission(HousekeepingRights.Access)]
internal class HousekeepingFindRoomByIdEvent : IPacketEvent
{
    private readonly IHousekeepingActionRunner _runner;
    private readonly IHousekeepingRoomStore _rooms;

    public HousekeepingFindRoomByIdEvent(IHousekeepingActionRunner runner, IHousekeepingRoomStore rooms)
    {
        _runner = runner;
        _rooms = rooms;
    }

    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        session.Send(new HousekeepingRoomDetailComposer(_rooms.Find(packet.ReadInt())));
        return Task.CompletedTask;
    }
}
