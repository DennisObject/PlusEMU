using System.Collections.Concurrent;
using Dapper;
using Plus.Utilities;

namespace Plus.HabboHotel.Rooms.Instance;

public class BansComponent
{
    /// <summary>
    /// The bans collection for storing them for this room.
    /// </summary>
    private ConcurrentDictionary<int, double> _bans;
    /// <summary>
    /// The RoomInstance that created this BanComponent.
    /// </summary>
    private Room _instance;

    /// <summary>
    /// Create the BanComponent for the RoomInstance.
    /// </summary>
    /// <param name="instance">The instance that created this component.</param>
    public BansComponent(Room instance)
    {
        if (instance == null)
            return;
        _instance = instance;
        _bans = new();
        using var connection = PlusEnvironment.DatabaseManager.Connection();
        foreach (var ban in connection.Query<RoomBanRow>(
                     "SELECT user_id AS UserId,expire AS ExpiresAt FROM room_bans WHERE room_id=@roomId AND expire>UNIX_TIMESTAMP()",
                     new { roomId = _instance.Id }))
            _bans.TryAdd(ban.UserId, ban.ExpiresAt);
    }

    public int Count => _bans.Count;

    public void Ban(RoomUser avatar, double time)
    {
        if (avatar == null || _instance.CheckRights(avatar.GetClient(), true) || IsBanned(avatar.UserId))
            return;
        var banTime = UnixTimestamp.GetNow() + time;
        using var connection = PlusEnvironment.DatabaseManager.Connection();
        connection.Execute("REPLACE INTO room_bans (user_id,room_id,expire) VALUES (@userId,@roomId,@banTime)",
            new { avatar.UserId, roomId = _instance.Id, banTime });
        _bans[avatar.UserId] = banTime;
        _instance.GetRoomUserManager().RemoveUserFromRoom(avatar.GetClient(), true, true);
    }

    public bool IsBanned(int userId)
    {
        if (!_bans.ContainsKey(userId))
            return false;
        var banTime = _bans[userId] - UnixTimestamp.GetNow();
        if (banTime <= 0)
        {
            using var connection = PlusEnvironment.DatabaseManager.Connection();
            connection.Execute("DELETE FROM room_bans WHERE room_id=@roomId AND user_id=@userId", new { roomId = _instance.Id, userId });
            _bans.TryRemove(userId, out _);
            return false;
        }
        return true;
    }

    public bool Unban(int userId)
    {
        if (!_bans.ContainsKey(userId))
            return false;
        if (_bans.ContainsKey(userId))
        {
            using var connection = PlusEnvironment.DatabaseManager.Connection();
            connection.Execute("DELETE FROM room_bans WHERE room_id=@roomId AND user_id=@userId", new { roomId = _instance.Id, userId });
            _bans.TryRemove(userId, out _);
            return true;
        }
        return false;
    }

    public List<int> BannedUsers()
    {
        using var connection = PlusEnvironment.DatabaseManager.Connection();
        return connection.Query<int>("SELECT DISTINCT user_id FROM room_bans WHERE room_id=@roomId AND expire>UNIX_TIMESTAMP()",
            new { roomId = _instance.Id }).ToList();
    }

    public void Cleanup()
    {
        _bans.Clear();
        _instance = null;
        _bans = null;
    }

    private sealed record RoomBanRow(int UserId, double ExpiresAt);
}
