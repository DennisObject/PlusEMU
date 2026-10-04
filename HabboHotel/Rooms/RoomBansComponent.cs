using Dapper;
using Plus.Database;
using Plus.HabboHotel.Rooms.Instance;

namespace Plus.HabboHotel.Rooms;

internal sealed record RoomBan(int UserId, double ExpiresAt);

internal interface IRoomBanStore
{
    IEnumerable<RoomBan> Load(uint roomId);
    void Save(uint roomId, int userId, double expiresAt);
    void Delete(uint roomId, int userId);
    IEnumerable<int> ActiveUserIds(uint roomId);
}

public sealed class RoomBansComponent(IDatabase database) : IRoomComponent, IRoomBanStore
{
    private Room _room = null!;
    public int Order => 220;
    public void Initiate(Room room) => _room = room;
    public void Initiated() => _room.SetBans(new BansComponent(_room, this, ((IRoomBanStore)this).Load(_room.Id)));

    IEnumerable<RoomBan> IRoomBanStore.Load(uint roomId)
    {
        using var connection = database.Connection();
        return connection.Query<RoomBan>(
            "SELECT user_id AS UserId, expire AS ExpiresAt FROM room_bans WHERE room_id = @roomId AND expire > UNIX_TIMESTAMP()",
            new { roomId }).ToArray();
    }

    void IRoomBanStore.Save(uint roomId, int userId, double expiresAt)
    {
        using var connection = database.Connection();
        connection.Execute("REPLACE INTO room_bans (user_id, room_id, expire) VALUES (@userId, @roomId, @expiresAt)",
            new { userId, roomId, expiresAt });
    }

    void IRoomBanStore.Delete(uint roomId, int userId)
    {
        using var connection = database.Connection();
        connection.Execute("DELETE FROM room_bans WHERE room_id = @roomId AND user_id = @userId", new { roomId, userId });
    }

    IEnumerable<int> IRoomBanStore.ActiveUserIds(uint roomId)
    {
        using var connection = database.Connection();
        return connection.Query<int>(
            "SELECT DISTINCT user_id FROM room_bans WHERE room_id = @roomId AND expire > UNIX_TIMESTAMP()", new { roomId }).ToArray();
    }
}
