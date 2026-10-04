using Dapper;
using Plus.Database;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Moderation;
using Plus.HabboHotel.Permissions;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Users;
using Plus.Utilities;

namespace Plus.HabboHotel.Housekeeping;

public interface IHousekeepingLookups
{
    HousekeepingUserDetail? User(Habbo actor, HousekeepingUserRecord? record);
    HousekeepingDashboard Dashboard();
}

public sealed class HousekeepingLookups : IHousekeepingLookups
{
    private readonly IGameClientManager _clients;
    private readonly IPermissionManager _permissions;
    private readonly IModerationManager _moderation;
    private readonly IRoomManager _rooms;
    private readonly IDatabase _database;

    public HousekeepingLookups(IGameClientManager clients, IPermissionManager permissions, IModerationManager moderation, IRoomManager rooms, IDatabase database)
    {
        _clients = clients;
        _permissions = permissions;
        _moderation = moderation;
        _rooms = rooms;
        _database = database;
    }

    public HousekeepingUserDetail? User(Habbo actor, HousekeepingUserRecord? record)
    {
        if (record == null) return null;
        var online = _clients.Online(record.Id)?.GetHabbo();
        var rank = online?.Rank ?? record.Rank;
        // Ban and trade lock expiries are stored on the emulator's local clock (UnixTimestamp.GetNow).
        var now = UnixTimestamp.GetNow();
        // Email and IP are personal data; only ranks granted the private-data right see them.
        var showPrivate = actor.Permissions.HasRight(HousekeepingRights.PrivateData);
        return new(record.Id, record.Username, online?.Motto ?? record.Motto, online?.Look ?? record.Look, rank,
            _permissions.TryGetGroup(rank, out var group) ? group.Name : string.Empty, online != null, record.LastOnline,
            online?.Credits ?? record.Credits, online?.Duckets ?? record.Duckets, online?.Diamonds ?? record.Diamonds,
            showPrivate ? record.Mail : string.Empty, showPrivate ? record.IpLast : string.Empty,
            _moderation.IsBanned(record.Username, out _), (online?.TimeMuted ?? record.TimeMuted) > 0,
            (online?.TradingLockExpiry ?? record.TradingLocked) > now);
    }

    public HousekeepingDashboard Dashboard()
    {
        var since = PlusEnvironment.GetUnixTimestamp() - 86400;
        using var connection = _database.Connection();
        var counts = connection.QuerySingle<DashboardCounts>(
            "SELECT (SELECT COUNT(*) FROM `users`) AS TotalUsers, (SELECT COUNT(*) FROM `rooms`) AS TotalRooms, " +
            "(SELECT COALESCE(MAX(`peak`), 0) FROM `housekeeping_online_peaks` WHERE `day` = UTC_DATE()) AS PeakToday, " +
            "(SELECT COALESCE(MAX(`peak`), 0) FROM `housekeeping_online_peaks`) AS PeakAllTime, " +
            "(SELECT COUNT(*) FROM `bans` WHERE CAST(`added_date` AS DECIMAL(20, 3)) > @since) + " +
            "(SELECT COUNT(*) FROM `housekeeping_log` WHERE `timestamp` > @since AND `success` = 1 AND `action` IN ('user.mute', 'user.trade_lock')) AS Sanctions",
            new { since });
        var online = _clients.Count;
        return new(online, counts.TotalUsers, _rooms.GetRooms().Count(room => room.UsersNow > 0), counts.TotalRooms,
            Math.Max(online, counts.PeakToday), Math.Max(online, counts.PeakAllTime),
            _moderation.GetTickets.Count(ticket => !ticket.Answered), counts.Sanctions,
            (int)Math.Clamp((DateTime.Now - PlusEnvironment.ServerStarted).TotalSeconds, 0, int.MaxValue), $"{PlusEnvironment.PrettyVersion} {PlusEnvironment.PrettyBuild}");
    }

    private sealed class DashboardCounts
    {
        public int TotalUsers { get; set; }
        public int TotalRooms { get; set; }
        public int PeakToday { get; set; }
        public int PeakAllTime { get; set; }
        public int Sanctions { get; set; }
    }
}
