using System.Collections.Concurrent;

namespace Plus.HabboHotel.Rooms.Instance;

public class BansComponent
{
    private readonly ConcurrentDictionary<int, DateTimeOffset> _bans;
    private Room? _instance;
    private readonly IRoomBanStore _store;
    private readonly TimeProvider _clock;

    internal BansComponent(Room instance, IRoomBanStore store, TimeProvider clock, IEnumerable<RoomBan> bans)
    {
        _instance = instance;
        _store = store;
        _clock = clock;
        _bans = new(bans.Select(ban => new KeyValuePair<int, DateTimeOffset>(ban.UserId, ban.ExpiresAt)));
    }

    public int Count => _bans.Count;

    internal void LoadPrepared(IEnumerable<RoomBan> bans)
    {
        _ = RequireRoom();
        foreach (var ban in bans)
            _bans[ban.UserId] = ban.ExpiresAt;
    }

    public void Ban(RoomUser avatar, TimeSpan duration)
    {
        var room = RequireRoom();
        if (avatar == null || room.CheckRights(avatar.GetClient(), true) || IsBanned(avatar.UserId)) return;
        var expiresAt = _clock.GetUtcNow() + duration;
        _store.Save(room.Id, avatar.UserId, expiresAt);
        _bans[avatar.UserId] = expiresAt;
        room.GetRoomUserManager().RemoveUserFromRoom(avatar.GetClient(), true, true);
    }

    public bool IsBanned(int userId)
    {
        var room = RequireRoom();
        if (!_bans.TryGetValue(userId, out var expiresAt)) return false;
        var now = _clock.GetUtcNow();
        if (expiresAt > now) return true;
        _store.Delete(room.Id, userId);
        _bans.TryRemove(userId, out _);
        return false;
    }

    public bool Unban(int userId)
    {
        var room = RequireRoom();
        if (!_bans.ContainsKey(userId)) return false;
        _store.Delete(room.Id, userId);
        _bans.TryRemove(userId, out _);
        return true;
    }

    public List<int> BannedUsers() => _store.ActiveUserIds(RequireRoom().Id).ToList();

    public void Cleanup()
    {
        _bans.Clear();
        _instance = null;
    }

    private Room RequireRoom() => _instance ?? throw new ObjectDisposedException(nameof(BansComponent));
}
