using System.Data;
using System.Text.Json;
using Dapper;
using Plus.Database;
using Plus.HabboHotel.Permissions;
using Plus.HabboHotel.Catalog;
using Plus.HabboHotel.Users;

namespace Plus.HabboHotel.Subscriptions;

public class ClubMembershipService(IDatabase database, IAccessControl permissions, TimeProvider clock) : IClubMembershipService
{
    public DateTimeOffset? GetExpiry(int userId)
    {
        using var connection = database.Connection();

        return connection.ExecuteScalar<DateTimeOffset?>("SELECT expires_at FROM user_club_memberships WHERE user_id = @userId", new { userId });
    }

    public DateTimeOffset? Purchase(Habbo habbo, ClubOffer offer, int? recipientId = null)
    {
        if (offer.Days <= 0 || offer.Days > 36500 || offer.Credits < 0 || offer.Points < 0 || recipientId.HasValue && !offer.Giftable) {
            return null;
        }

        DateTimeOffset? expiry;

        lock (habbo.WalletSync) {
            if (habbo.WalletClosed) {
                return null;
            }

            var credits = (long)habbo.Credits - offer.Credits;
            var duckets = (long)habbo.Duckets - (offer.PointsType == 0 ? offer.Points : 0);
            var diamonds = (long)habbo.Diamonds - (offer.PointsType == 5 ? offer.Points : 0);

            if (offer.Points > 0 && offer.PointsType is not (0 or 5) || credits < 0 || duckets < 0 || diamonds < 0) {
                return null;
            }

            using var connection = database.Connection();
            connection.Open();
            using var transaction = connection.BeginTransaction();
            var storedOffer = connection.QuerySingleOrDefault<ClubOffer>("SELECT id, name, days, credits, points, points_type AS PointsType, giftable AS Giftable FROM catalog_club_offers WHERE id = @id AND enabled = 1", new { id = offer.Id }, transaction);

            if (storedOffer == null || storedOffer.Days is <= 0 or > 36500 || storedOffer.Credits < 0 || storedOffer.Points < 0 || recipientId.HasValue && !storedOffer.Giftable) {
                return null;
            }

            offer = storedOffer;
            credits = (long)habbo.Credits - offer.Credits;
            duckets = (long)habbo.Duckets - (offer.PointsType == 0 ? offer.Points : 0);
            diamonds = (long)habbo.Diamonds - (offer.PointsType == 5 ? offer.Points : 0);

            if (credits < 0 || duckets < 0 || diamonds < 0 || offer.Points > 0 && offer.PointsType is not (0 or 5)) {
                return null;
            }

            var userId = recipientId ?? habbo.Id;
            // Lock accounts even when no membership row exists, in a consistent order for gifts.
            var ids = connection.Query<int>("SELECT id FROM users WHERE id IN @ids ORDER BY id FOR UPDATE",
                new { ids = new[] { habbo.Id, userId }.Distinct().ToArray() }, transaction).ToArray();

            if (ids.Length != (habbo.Id == userId ? 1 : 2)) {
                return null;
            }

            var now = clock.GetUtcNow();
            expiry = Extend(connection, transaction, userId, now, offer.Days);

            if (expiry == null) {
                return null;
            }

            connection.Execute("UPDATE users SET credits = @credits, activity_points = @duckets, vip_points = @diamonds WHERE id = @id",
                new { id = habbo.Id, credits, duckets, diamonds }, transaction);
            // HC purchase itself is spending only if the purchaser was already a member.
            ClubRewards.RecordSpending(connection, transaction, habbo.Id, offer.Credits, now, habbo.Access.Membership.Active(now));
            connection.Execute("INSERT INTO acl_audit_log (actor_id, action, target_type, target_id, payload) VALUES (@actor, 'club.purchase', 'user', @userId, @payload)",
                new { actor = habbo.Id, userId, payload = JsonSerializer.Serialize(new { offer.Id, offer.Days, expiry = expiry.Value.ToUnixTimeSeconds(), offer.Credits, offer.Points, offer.PointsType }) }, transaction);
            transaction.Commit();
            habbo.Credits = (int)credits;
            habbo.Duckets = (int)duckets;
            habbo.Diamonds = (int)diamonds;
        }

        permissions.Refresh(recipientId ?? habbo.Id);

        return expiry;
    }

    public DateTimeOffset? Grant(Habbo actor, int userId, int days)
    {
        if (days < 0 || days > 36500 || actor.Id == userId || !actor.Access.Can(PermissionKeys.HousekeepingEconomy) || !permissions.Outranks(actor.Id, userId)) {
            return null;
        }

        using var connection = database.Connection();
        connection.Open();
        using var transaction = connection.BeginTransaction();

        if (connection.ExecuteScalar<int?>("SELECT id FROM users WHERE id = @userId FOR UPDATE", new { userId }, transaction) == null) {
            return null;
        }

        var now = clock.GetUtcNow();
        var expiry = Extend(connection, transaction, userId, now, days);

        if (expiry == null) {
            return null;
        }

        connection.Execute("INSERT INTO acl_audit_log (actor_id, action, target_type, target_id, payload) VALUES (@actorId, 'club.grant', 'user', @userId, @payload)",
            new { actorId = actor.Id, userId, payload = JsonSerializer.Serialize(new { days, expiry = expiry.Value.ToUnixTimeSeconds() }) }, transaction);
        transaction.Commit();
        permissions.Refresh(userId);

        return expiry;
    }

    // Returns null before any write when the new expiry cannot be represented.
    private static DateTimeOffset? Extend(IDbConnection connection, IDbTransaction transaction, int userId, DateTimeOffset now, int days)
    {
        var row = connection.QuerySingleOrDefault<ClubMembershipRow>("SELECT " + ClubMembership.Columns + " FROM user_club_memberships WHERE user_id = @userId FOR UPDATE", new { userId }, transaction);
        var old = row?.ToMembership() ?? ClubMembership.None;
        var expiry = days == 0 ? now : ClubMembership.Extend(now, old.ExpiresAt, days);

        if (expiry == null) {
            return null;
        }

        // Split elapsed history from the new running interval; prepaid time earns no gifts yet.
        var started = days == 0 ? (DateTimeOffset?)null : old.Active(now) && old.StartedAt is { } running ? running : now;
        var past = days == 0 || !old.Active(now) ? old.Elapsed(now) : old.PastSeconds;
        var first = old.FirstStartedAt ?? (days > 0 ? now : null);
        connection.Execute("INSERT INTO user_club_memberships (user_id, expires_at, started_at, first_started_at, past_seconds, modified_at) VALUES (@userId, @expiry, @started, @first, @past, @now) ON DUPLICATE KEY UPDATE expires_at = @expiry, started_at = @started, first_started_at = @first, past_seconds = @past, modified_at = @now",
            new { userId, expiry = expiry.Value.UtcDateTime, started = started?.UtcDateTime, first = first?.UtcDateTime, past, now = now.UtcDateTime }, transaction);

        if (days > 0) {
            connection.Execute("INSERT INTO club_membership_intervals (user_id, started_at, expires_at) VALUES (@userId, @started, @expiry) ON DUPLICATE KEY UPDATE expires_at = @expiry",
                new { userId, started = started!.Value.UtcDateTime, expiry = expiry.Value.UtcDateTime }, transaction);
        }
        else if (old.StartedAt is { } previous) {
            connection.Execute("UPDATE club_membership_intervals SET expires_at = LEAST(expires_at, @now) WHERE user_id = @userId AND started_at = @started",
                new { userId, now = now.UtcDateTime, started = previous.UtcDateTime }, transaction);
        }

        return expiry;
    }
}
