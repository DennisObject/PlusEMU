using System.Collections.Concurrent;
using Plus.Utilities;

namespace Plus.HabboHotel.Rooms.Instance;

public class BansComponent
{
    private ConcurrentDictionary<int, double> _bans;
    private Room _instance;
    private readonly IRoomBanStore _store;

    internal BansComponent(Room instance, IRoomBanStore store, IEnumerable<RoomBan> bans)
    {
        _instance = instance;
        _store = store;
        Load(bans);
    }

    internal void Load(IEnumerable<RoomBan> bans) => _bans = new(bans.Select(ban => new KeyValuePair<int, double>(ban.UserId, ban.ExpiresAt)));

    public int Count => _bans.Count;

    public void Ban(RoomUser avatar, double time)
    {
        if (avatar == null || _instance.CheckRights(avatar.GetClient(), true) || IsBanned(avatar.UserId)) return;
        var banTime = UnixTimestamp.GetNow() + time;
        _store.Save(_instance.Id, avatar.UserId, banTime);
        _bans[avatar.UserId] = banTime;
        _instance.GetRoomUserManager().RemoveUserFromRoom(avatar.GetClient(), true, true);
    }

    public bool IsBanned(int userId)
    {
        if (!_bans.ContainsKey(userId)) return false;
        if (_bans[userId] - UnixTimestamp.GetNow() > 0) return true;
        _store.Delete(_instance.Id, userId);
        _bans.TryRemove(userId, out _);
        return false;
    }

    public bool Unban(int userId)
    {
        if (!_bans.ContainsKey(userId)) return false;
        _store.Delete(_instance.Id, userId);
        _bans.TryRemove(userId, out _);
        return true;
    }

    public List<int> BannedUsers() => _store.ActiveUserIds(_instance.Id).ToList();

    public void Cleanup()
    {
        _bans.Clear();
        _instance = null;
        _bans = null;
    }
}
