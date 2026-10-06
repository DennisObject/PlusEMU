using Dapper;
using Plus.Database;
using Plus.Core;
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
    private readonly IAccessControl _permissions;
    private readonly IModerationManager _moderation;
    private readonly IRoomManager _rooms;
    private readonly IDatabase _database;
    private readonly TimeProvider _clock;
    private readonly IServerUptime _uptime;

    public HousekeepingLookups(IGameClientManager clients, IAccessControl permissions, IModerationManager moderation, IRoomManager rooms,
        IDatabase database, TimeProvider clock, IServerUptime uptime)
    {
        _clients = clients;
        _permissions = permissions;
        _moderation = moderation;
        _rooms = rooms;
        _database = database;
        _clock = clock;
        _uptime = uptime;
    }

    public HousekeepingUserDetail? User(Habbo actor, HousekeepingUserRecord? record)
    {
        if (record == null) return null;
        var online = _clients.Online(record.Id)?.GetHabbo();
        var role = (online?.Access ?? _permissions.Resolve(record.Id)).PrimaryRole;
        var now = _clock.GetUtcNow();
        // Email and IP are personal data; only ranks granted the private-data right see them.
        var showPrivate = actor.Access.Can(HousekeepingRights.PrivateData);
        return new(record.Id, record.Username, online?.Motto ?? record.Motto, online?.Look ?? record.Look, role?.Id ?? 0,
            role?.Name ?? string.Empty, online != null, (int)Math.Clamp(record.LastOnlineAt?.ToUnixTimeSeconds() ?? 0, 0, int.MaxValue),
            online?.Credits ?? record.Credits, online?.Duckets ?? record.Duckets, online?.Diamonds ?? record.Diamonds,
            showPrivate ? record.Mail : string.Empty, showPrivate ? record.IpLast : string.Empty,
            _moderation.IsBanned(record.Username, out _), (online?.TimeMuted ?? record.TimeMuted) > 0,
            (online != null ? online.TradingLockExpiresAt : record.TradingLockExpiresAt) > now);
    }

    public HousekeepingDashboard Dashboard()
    {
        var now = _clock.GetUtcNow();
        var sinceUtc = now.AddDays(-1).UtcDateTime;
        using var connection = _database.Connection();
        var counts = connection.QuerySingle<DashboardCounts>(
            "SELECT (SELECT COUNT(*) FROM `users`) AS TotalUsers, (SELECT COUNT(*) FROM `rooms`) AS TotalRooms, " +
            "(SELECT COALESCE(MAX(`peak`), 0) FROM `housekeeping_online_peaks` WHERE `day` = UTC_DATE()) AS PeakToday, " +
            "(SELECT COALESCE(MAX(`peak`), 0) FROM `housekeeping_online_peaks`) AS PeakAllTime, " +
            "(SELECT COUNT(*) FROM `bans` WHERE `added_date` > @sinceUtc) + " +
            "(SELECT COUNT(*) FROM `housekeeping_log` WHERE `timestamp` > @sinceUtc AND `success` = 1 AND `action` IN ('user.mute', 'user.trade_lock')) AS Sanctions",
            new { sinceUtc });
        var online = _clients.Count;
        return new(online, counts.TotalUsers, _rooms.GetRooms().Count(room => room.UsersNow > 0), counts.TotalRooms,
            Math.Max(online, counts.PeakToday), Math.Max(online, counts.PeakAllTime),
            _moderation.GetTickets.Count(ticket => !ticket.Answered), counts.Sanctions,
            (int)Math.Clamp(_uptime.Elapsed.TotalSeconds, 0, int.MaxValue), $"{PlusEnvironment.PrettyVersion} {PlusEnvironment.PrettyBuild}");
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
