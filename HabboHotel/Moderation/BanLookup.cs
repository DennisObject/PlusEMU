using Dapper;
using Plus.Database;

namespace Plus.HabboHotel.Moderation;

public interface IBanLookup
{
    /// <summary>An active ban on the username or on the caller's IP address, if any.</summary>
    Task<LoginBan?> Find(string username, string address);
}

public sealed record LoginBan(string Reason, long ExpiresAt);

/// <summary>
/// Reads bans straight from the database for the login API. ModerationManager only caches
/// user and machine bans, so IP bans would otherwise be missed.
/// </summary>
public class BanLookup : IBanLookup
{
    private readonly IDatabase _database;
    private readonly TimeProvider _time;

    public BanLookup(IDatabase database, TimeProvider time)
    {
        _database = database;
        _time = time;
    }

    public async Task<LoginBan?> Find(string username, string address)
    {
        using var connection = _database.Connection();
        return await connection.QueryFirstOrDefaultAsync<LoginBan>(
            "SELECT `reason` AS Reason, CAST(`expire` AS SIGNED) AS ExpiresAt FROM `bans` " +
            "WHERE ((`bantype` = 'user' AND `value` = @username) OR (`bantype` = 'ip' AND `value` = @address)) AND `expire` > @now " +
            "ORDER BY `expire` DESC LIMIT 1",
            new { username, address, now = BanClock.Now(_time) });
    }
}
