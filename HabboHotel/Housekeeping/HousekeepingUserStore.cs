using Dapper;
using Plus.Database;
using Plus.HabboHotel.Permissions;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Users;

namespace Plus.HabboHotel.Housekeeping;

/// <summary>Persisted account fields; online users' live values are overlaid by the caller.</summary>
public sealed class HousekeepingUserRecord
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Motto { get; set; } = string.Empty;
    public string Look { get; set; } = string.Empty;
    public int LastOnline { get; set; }
    public int Credits { get; set; }
    public int Duckets { get; set; }
    public int Diamonds { get; set; }
    public string Mail { get; set; } = string.Empty;
    public string IpLast { get; set; } = string.Empty;
    public double TimeMuted { get; set; }
    public double TradingLocked { get; set; }
}

public interface IHousekeepingUserStore
{
    HousekeepingUserRecord? Find(int userId);
    HousekeepingUserRecord? Find(string username);
}

public sealed class HousekeepingUserStore : IHousekeepingUserStore
{
    private const string Select =
        "SELECT u.`id`, u.`username`, COALESCE(u.`motto`, '') AS Motto, COALESCE(u.`look`, '') AS Look, " +
        "COALESCE(u.`last_online`, 0) AS LastOnline, COALESCE(u.`credits`, 0) AS Credits, COALESCE(u.`activity_points`, 0) AS Duckets, " +
        "COALESCE(u.`vip_points`, 0) AS Diamonds, COALESCE(u.`mail`, '') AS Mail, COALESCE(u.`ip_last`, '') AS IpLast, " +
        "COALESCE(u.`time_muted`, 0) AS TimeMuted, COALESCE(i.`trading_locked`, 0) AS TradingLocked " +
        "FROM `users` u LEFT JOIN `user_info` i ON i.`user_id` = u.`id` ";

    private readonly IDatabase _database;

    public HousekeepingUserStore(IDatabase database) => _database = database;

    public HousekeepingUserRecord? Find(int userId)
    {
        if (userId <= 0) return null;
        using var connection = _database.Connection();
        return connection.QuerySingleOrDefault<HousekeepingUserRecord>(Select + "WHERE u.`id` = @userId LIMIT 1", new { userId });
    }

    public HousekeepingUserRecord? Find(string username)
    {
        if (string.IsNullOrEmpty(username)) return null;
        using var connection = _database.Connection();
        return connection.QuerySingleOrDefault<HousekeepingUserRecord>(Select + "WHERE u.`username` = @username LIMIT 1", new { username });
    }
}

public static class HousekeepingUserTargets
{
    /// <summary>Resolves a target account and applies the rank hierarchy; returns the refusal, or null with the user set.</summary>
    public static HousekeepingOutcome? Target(this IHousekeepingUserStore users, Habbo actor, int userId, IAccessControl access, out HousekeepingUserRecord user)
    {
        user = null!;
        if (userId <= 0) return HousekeepingOutcome.Invalid(HousekeepingTarget.User(0));
        var found = users.Find(userId);
        if (found == null) return HousekeepingOutcome.Fail(HousekeepingErrors.UserNotFound, HousekeepingTarget.User(userId));
        // Equal ranks are refused too, which also stops staff acting on themselves.
        if (actor.Id == found.Id || !access.Outranks(actor.Id, found.Id))
            return HousekeepingOutcome.Fail(HousekeepingErrors.RankTooHigh, Label(found), $"targetUserId={found.Id}");
        user = found;
        return null;
    }

    public static HousekeepingTarget Label(HousekeepingUserRecord user) => HousekeepingTarget.User(user.Id, user.Username);

    public static GameClient? Online(this IGameClientManager clients, int userId) =>
        clients.GetClientByUserId(userId) is { } client && client.GetHabbo() != null ? client : null;
}
