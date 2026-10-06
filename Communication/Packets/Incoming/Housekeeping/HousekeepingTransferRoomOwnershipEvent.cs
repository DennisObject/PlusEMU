using Plus.Communication.Attributes;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Housekeeping;

namespace Plus.Communication.Packets.Incoming.Housekeeping;

[RequiresPermission(HousekeepingRights.Access)]
internal class HousekeepingTransferRoomOwnershipEvent : IPacketEvent
{
    private readonly IHousekeepingActionRunner _runner;
    private readonly IHousekeepingRoomActions _rooms;

    public HousekeepingTransferRoomOwnershipEvent(IHousekeepingActionRunner runner, IHousekeepingRoomActions rooms)
    {
        _runner = runner;
        _rooms = rooms;
    }

    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        var roomId = packet.ReadInt();
        var newOwnerId = packet.ReadInt();
        _runner.Run(session, "room.transfer", HousekeepingRights.RoomOwnership, actor => _rooms.TransferOwnership(actor, roomId, newOwnerId));

        return Task.CompletedTask;
    }
}
