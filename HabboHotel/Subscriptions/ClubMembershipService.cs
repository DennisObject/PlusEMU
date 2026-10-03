using Dapper;
using Plus.Database;
using Plus.HabboHotel.Catalog;
using Plus.HabboHotel.Users;

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

    public int? Purchase(Habbo habbo, ClubOffer offer)
    {
        if (offer.Days <= 0 || offer.Credits < 0 || offer.Points < 0)
            return null;
        // Logout and the currency timer save the wallet under the same lock, so the charge cannot be lost to them.
        lock (habbo.WalletSync)
        {
            if (habbo.WalletClosed)
                return null;
            var credits = habbo.Credits - offer.Credits;
            var duckets = habbo.Duckets - (offer.PointsType == 0 ? offer.Points : 0);
            var diamonds = habbo.Diamonds - (offer.PointsType == 5 ? offer.Points : 0);
            if (offer.Points > 0 && offer.PointsType is not (0 or 5) || credits < 0 || duckets < 0 || diamonds < 0)
                return null;

            using var connection = _database.Connection();
            connection.Open();
            using var transaction = connection.BeginTransaction();
            var now = (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var current = connection.ExecuteScalar<int?>("SELECT `expires_at` FROM `user_club_memberships` WHERE `user_id` = @userId FOR UPDATE", new { userId = habbo.Id }, transaction) ?? 0;
            // Time bought on top of a running membership is added to its end, not to now.
            var expiry = Math.Max(current, now) + offer.Days * 86400;
            connection.Execute("INSERT INTO `user_club_memberships` (`user_id`, `expires_at`) VALUES (@userId, @expiry) ON DUPLICATE KEY UPDATE `expires_at` = @expiry", new { userId = habbo.Id, expiry }, transaction);
            connection.Execute("UPDATE `users` SET `credits` = @credits, `activity_points` = @duckets, `vip_points` = @diamonds WHERE `id` = @userId", new { userId = habbo.Id, credits, duckets, diamonds }, transaction);
            transaction.Commit();

            habbo.Credits = credits;
            habbo.Duckets = duckets;
            habbo.Diamonds = diamonds;
            return expiry;
        }
    }
}
