using Plus.Communication.Attributes;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Housekeeping;

namespace Plus.Communication.Packets.Incoming.Housekeeping;

[RequiresPermission(HousekeepingRights.Access)]
internal class HousekeepingMuteRoomEvent : IPacketEvent
{
    private readonly IHousekeepingActionRunner _runner;
    private readonly IHousekeepingRoomActions _rooms;

    public HousekeepingMuteRoomEvent(IHousekeepingActionRunner runner, IHousekeepingRoomActions rooms)
    {
        _runner = runner;
        _rooms = rooms;
    }

    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        var roomId = packet.ReadInt();
        var minutes = packet.ReadInt();
        _runner.Run(session, "room.mute", HousekeepingRights.Rooms, actor => _rooms.Mute(actor, roomId, minutes));
        return Task.CompletedTask;
    }
}
