using System.Collections.Immutable;
using Plus.Communication.Packets.Outgoing.Rooms.Settings;
using Plus.HabboHotel.Cache;
using Plus.HabboHotel.GameClients;

namespace Plus.HabboHotel.Rooms;

public sealed record BannedRoomUser(int Id, string Username);
public sealed record RoomBannedUsersSnapshot(uint RoomId, ImmutableArray<BannedRoomUser> Users);

public interface IRoomBannedUsersService
{
    void Send(GameClient client);
}

public sealed class RoomBannedUsersService(ICacheManager cache) : IRoomBannedUsersService
{
    public void Send(GameClient client)
    {
        var room = client.GetHabbo().CurrentRoom;

        if (room == null || !room.CheckRights(client, true)) {
            return;
        }

        var ids = room.GetBans().BannedUsers().ToArray();

        if (ids.Length == 0) {
            return;
        }

        var users = ids.Select(id => cache.GenerateUser(id) is { } user
            ? new BannedRoomUser(user.Id, user.Username)
            : new BannedRoomUser(0, "Unknown Error")).ToImmutableArray();
        client.Send(new GetRoomBannedUsersComposer(new(room.Id, users)));
    }
}
