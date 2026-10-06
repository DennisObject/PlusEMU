using Plus.Communication.Attributes;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Housekeeping;

namespace Plus.Communication.Packets.Incoming.Housekeeping;

[RequiresPermission(HousekeepingRights.Access)]
internal class HousekeepingRoomStateEvent : IPacketEvent
{
    private readonly IHousekeepingActionRunner _runner;
    private readonly IHousekeepingRoomActions _rooms;

    public HousekeepingRoomStateEvent(IHousekeepingActionRunner runner, IHousekeepingRoomActions rooms)
    {
        _runner = runner;
        _rooms = rooms;
    }

    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        var roomId = packet.ReadInt();
        var open = packet.ReadBool();
        _runner.Run(session, open ? "room.open" : "room.close", HousekeepingRights.Rooms, actor => _rooms.SetState(actor, roomId, open));

        return Task.CompletedTask;
    }
}
