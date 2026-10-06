using Plus.Communication.Attributes;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Housekeeping;

namespace Plus.Communication.Packets.Incoming.Housekeeping;

[RequiresPermission(HousekeepingRights.Access)]
internal class HousekeepingDeleteRoomEvent : IPacketEvent
{
    private readonly IHousekeepingActionRunner _runner;
    private readonly IHousekeepingRoomActions _rooms;

    public HousekeepingDeleteRoomEvent(IHousekeepingActionRunner runner, IHousekeepingRoomActions rooms)
    {
        _runner = runner;
        _rooms = rooms;
    }

    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        var roomId = packet.ReadInt();
        _runner.Run(session, "room.delete", HousekeepingRights.RoomOwnership, actor => _rooms.Delete(actor, roomId));

        return Task.CompletedTask;
    }
}
