using Plus.HabboHotel.Permissions;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms;

namespace Plus.Communication.Packets.Incoming.Rooms.Settings;

internal class DeleteRoomEvent : IPacketEvent
{
    private readonly IRoomManager _roomManager;
    private readonly IRoomDeletionService _roomDeletion;

    public DeleteRoomEvent(IRoomManager roomManager, IRoomDeletionService roomDeletion)
    {
        _roomManager = roomManager;
        _roomDeletion = roomDeletion;
    }

    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        var roomId = packet.ReadUInt();

        if (roomId == 0)
        {
            return Task.CompletedTask;
        }

        if (!_roomManager.TryGetRoom(roomId, out var room))
        {
            return Task.CompletedTask;
        }

        if (room.OwnerId != session.GetHabbo().Id && !session.GetHabbo().Access.Can(PermissionKeys.RoomDeleteAny))
        {
            return Task.CompletedTask;
        }

        _roomDeletion.Delete(room);

        return Task.CompletedTask;
    }
}
