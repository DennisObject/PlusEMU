using Dapper;
using Plus.Database;

namespace Plus.HabboHotel.Moderation;

public sealed class ModerationUserData
{
    public int Id { get; init; }
    public string Username { get; init; } = "";
    public string Look { get; init; } = "";
    public string Mail { get; init; } = "";
    public int Rank { get; init; }
    public int Credits { get; init; }
    public int Duckets { get; init; }
    public int Diamonds { get; init; }
    public int GotwPoints { get; init; }
    public DateTimeOffset? AccountCreatedAt { get; init; }
    public DateTimeOffset? LastOnlineAt { get; init; }
    public DateTimeOffset? TradingLockExpiresAt { get; init; }
    public int HelpRequests { get; init; }
    public int AbusiveHelpRequests { get; init; }
    public int Cautions { get; init; }
    public int Bans { get; init; }
    public int TradingLockCount { get; init; }
}

public interface IModerationUserStore
{
    ModerationUserData? Find(int userId);
    ModerationUserData? Find(string username);
}

public sealed class ModerationUserStore(IDatabase database) : IModerationUserStore
{
    private const string Select = "SELECT u.id,u.username,u.look,u.mail,u.`rank`,u.credits,COALESCE(d.amount,0) AS Duckets,COALESCE(v.amount,0) AS Diamonds,COALESCE(g.amount,0) AS GotwPoints," +
        "u.account_created AS AccountCreatedAt,u.last_online AS LastOnlineAt,i.trading_locked AS TradingLockExpiresAt," +
        "COALESCE(i.cfhs,0) AS HelpRequests,COALESCE(i.cfhs_abusive,0) AS AbusiveHelpRequests,COALESCE(i.cautions,0) AS Cautions," +
        "COALESCE(i.bans,0) AS Bans,COALESCE(i.trading_locks_count,0) AS TradingLockCount FROM users u LEFT JOIN user_info i ON i.user_id=u.id " +
        "LEFT JOIN user_currencies d ON d.user_id=u.id AND d.type=0 LEFT JOIN user_currencies v ON v.user_id=u.id AND v.type=5 " +
        "LEFT JOIN user_currencies g ON g.user_id=u.id AND g.type=103 ";

    public ModerationUserData? Find(int userId)
    {
        using var connection = database.Connection();

        return connection.QuerySingleOrDefault<ModerationUserData>(Select + "WHERE u.id=@userId LIMIT 1", new { userId });
    }

    public ModerationUserData? Find(string username)
    {
        using var connection = database.Connection();

        return connection.QuerySingleOrDefault<ModerationUserData>(Select + "WHERE u.username=@username LIMIT 1", new { username });
    }
}
