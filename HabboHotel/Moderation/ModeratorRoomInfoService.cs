using System.Collections.Immutable;
using Plus.Communication.Packets.Outgoing.Moderation;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms;

namespace Plus.HabboHotel.Moderation;

public sealed record ModeratorRoomInfoSnapshot(
    uint Id, int UsersNow, bool OwnerInRoom, int OwnerId, string OwnerName,
    string Name, string Description, ImmutableArray<string> Tags)
{
    public static ModeratorRoomInfoSnapshot Capture(RoomData data, bool ownerInRoom) =>
        new(data.Id, data.UsersNow, ownerInRoom, data.OwnerId, data.OwnerName,
            data.Name, data.Description, data.Tags.ToImmutableArray());
}

public interface IModeratorRoomInfoService
{
    void Show(GameClient session, uint roomId);
}

public sealed class ModeratorRoomInfoService(IRoomDataLoader dataLoader, IRoomManager rooms) : IModeratorRoomInfoService
{
    public void Show(GameClient session, uint roomId)
    {
        if (!dataLoader.TryGetData(roomId, out var data) || !rooms.TryGetRoom(roomId, out var room))
            return;

        var snapshot = ModeratorRoomInfoSnapshot.Capture(data,
            room.GetRoomUserManager().GetRoomUserByHabbo(data.OwnerName) != null);
        session.Send(new ModeratorRoomInfoComposer(snapshot));
    }
}
