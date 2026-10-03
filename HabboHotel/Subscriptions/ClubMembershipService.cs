using Dapper;
using Plus.Database;

namespace Plus.HabboHotel.Subscriptions;

public class ClubMembershipService : IClubMembershipService
{
    private readonly IDatabase _database;

    public ClubMembershipService(IDatabase database) => _database = database;

    public int GetExpiry(int userId)
    {
        using var connection = _database.Connection();
        return connection.ExecuteScalar<int?>("SELECT `expires_at` FROM `user_club_memberships` WHERE `user_id` = @userId", new { userId }) ?? 0;
    }

    // Time bought on top of a running membership is added to its end, not to now.
    public int Extend(int userId, int days)
    {
        var now = (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var expiry = Math.Max(GetExpiry(userId), now) + days * 86400;
        using var connection = _database.Connection();
        connection.Execute("INSERT INTO `user_club_memberships` (`user_id`, `expires_at`) VALUES (@userId, @expiry) ON DUPLICATE KEY UPDATE `expires_at` = @expiry", new { userId, expiry });
        return expiry;
    }
}
