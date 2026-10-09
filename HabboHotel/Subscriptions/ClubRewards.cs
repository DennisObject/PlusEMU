using System.Data;
using System.Globalization;
using Dapper;
using Plus.Core.Settings;
using Plus.Database;
using Plus.HabboHotel.Catalog;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Users;
using Plus.HabboHotel.Users.Authentication;
using Plus.HabboHotel.Users.Inventory.Furniture;
using Plus.Communication.Packets.Outgoing.Inventory.Purse;

namespace Plus.HabboHotel.Subscriptions;

public class ClubRewards(IDatabase database, ICatalogManager catalog, IGameClientManager clients,
    ISettingsManager settings, IAccountSessionGate sessionGate, TimeProvider clock, Plus.HabboHotel.Permissions.IAccessControl permissions) : IClubRewards
{
    // Paydays and month windows follow the UTC calendar, whatever offset the captured instant carries.
    internal static DateTimeOffset NextPayday(DateTimeOffset now)
    {
        var utc = now.ToUniversalTime();

        return new DateTimeOffset(utc.Year, utc.Month, 1, 0, 0, 0, TimeSpan.Zero).AddMonths(1);
    }
    internal static int StreakBonus(int days) => days >= 365 ? 30 : days >= 180 ? 25 : days >= 90 ? 20 : days >= 60 ? 15 : days >= 30 ? 10 : days >= 7 ? 5 : 0;
    internal static int SpendingBonus(long spent, double percentage) => (int)Math.Clamp(Math.Floor(spent * percentage), 0, int.MaxValue);
    // Polaris excludes redeemable credit furniture from its shop-spending query.
    internal static bool EligibleCatalogPurchase(string name) => !name.StartsWith("CF_", StringComparison.OrdinalIgnoreCase) && !name.StartsWith("CFC_", StringComparison.OrdinalIgnoreCase);
    private double Percentage => int.TryParse(settings.GetOptionalValue("club.payday.percentage"), out var percent) ? Math.Clamp(percent, 0, 100) / 100.0 : 0.1;

    // A gift is one plain piece of furniture, given for free.
    private IReadOnlyList<ClubGift> GiftOffers(Habbo habbo)
    {
        using var connection = database.Connection();
        var requirements = connection.Query<(int Id, int Days)>("SELECT offer_id, days_required FROM club_gift_offers WHERE enabled = 1 AND days_required >= 0").ToDictionary(row => row.Id, row => row.Days);

        return catalog.Pages.Where(page => page.CanOpen(habbo)).SelectMany(page => page.Offers.Values).DistinctBy(offer => offer.Id)
            .Where(offer => requirements.ContainsKey(offer.Id) && offer.Enabled && offer.CanPurchase(habbo) && !offer.IsLimited &&
                offer.Products is [{ Type: CatalogProductType.Furni, Amount: > 0 and <= 100, Definition.InteractionType: InteractionType.None }])
            .Select(offer => new ClubGift(offer, requirements[offer.Id])).ToArray();
    }

    public ClubGiftInfo Gifts(Habbo habbo)
    {
        var now = clock.GetUtcNow();
        var membership = habbo.Access.Membership;
        var elapsed = membership.Elapsed(now);
        var next = (int)((ClubMembership.Period - elapsed % ClubMembership.Period + ClubMembership.Day - 1) / ClubMembership.Day);

        return new(membership.Active(now) ? next : 0, membership.Active(now) ? membership.AvailableGifts(now) : 0, elapsed / ClubMembership.Day, GiftOffers(habbo));
    }

    public ClubGiftClaim? Claim(Habbo habbo, string productCode)
    {
        lock (habbo.WalletSync) {
            return habbo.WalletClosed ? null : ClaimLocked(habbo, productCode);
        }
    }
    private ClubGiftClaim? ClaimLocked(Habbo habbo, string productCode)
    {
        // The gift furniture is handed into the loaded inventory, so nothing is claimed without one.
        if (productCode.Length is 0 or > 100 || ClubAccess.LevelFor(habbo.Access) == 0 || habbo.Inventory is not { } inventory) {
            return null;
        }

        var gift = GiftOffers(habbo).FirstOrDefault(gift => gift.Offer.LocalizationKey == productCode);

        if (gift == null) {
            return null;
        }

        var received = new List<InventoryItem>();
        using var connection = database.Connection();
        connection.Open();
        using var transaction = connection.BeginTransaction();
        connection.ExecuteScalar<int>("SELECT id FROM users WHERE id = @id FOR UPDATE", new { id = habbo.Id }, transaction);
        var row = connection.QuerySingleOrDefault<ClubMembershipRow>("SELECT " + ClubMembership.Columns + " FROM user_club_memberships WHERE user_id = @id FOR UPDATE", new { id = habbo.Id }, transaction);
        var membership = row?.ToMembership();
        var now = clock.GetUtcNow();

        if (membership == null || !membership.Active(now) ||
            membership.AvailableGifts(now) < 1 || membership.Elapsed(now) / ClubMembership.Day < gift.DaysRequired) {
            return null;
        }

        // Recheck enabled state under the transaction as well as the catalog snapshot.
        var required = connection.ExecuteScalar<int?>("SELECT days_required FROM club_gift_offers WHERE offer_id = @id AND enabled = 1 FOR UPDATE", new { id = gift.Offer.Id }, transaction);

        if (required == null || required < 0 || membership.Elapsed(now) / ClubMembership.Day < required) {
            return null;
        }

        connection.Execute("INSERT INTO club_gift_claims (user_id, gift_number, offer_id, claimed_at) VALUES (@id, @number, @offer, @now)",
            new { id = habbo.Id, number = membership.GiftsClaimed + 1, offer = gift.Offer.Id, now = now.UtcDateTime }, transaction);

        var definition = gift.Offer.Definition!;

        for (var i = 0; i < gift.Offer.Amount; i++) {
            var id = connection.ExecuteScalar<uint>("INSERT INTO items (user_id, base_item, extra_data) VALUES (@user, @item, ''); SELECT LAST_INSERT_ID()",
                new { user = habbo.Id, item = definition.Id }, transaction);
            received.Add(new InventoryItem { Id = id, OwnerId = (uint)habbo.Id, Definition = definition });
        }

        connection.Execute("UPDATE user_club_memberships SET gifts_claimed = gifts_claimed + 1 WHERE user_id = @id", new { id = habbo.Id }, transaction);
        transaction.Commit();
        // Refresh only the membership holder; the caller publishes the gift and updated status.
        permissions.Refresh(habbo.Id);

        foreach (var item in received) {
            inventory.Furniture.AddItem(item);
        }

        return new(gift, received);
    }

    internal static void RecordSpending(IDbConnection connection, IDbTransaction? transaction, int userId, int credits, DateTimeOffset now, bool member)
    {
        if (member && credits > 0) {
            connection.Execute("INSERT INTO club_credit_spending (user_id, credits, spent_at) VALUES (@userId, @credits, @now)", new { userId, credits, now = now.UtcDateTime }, transaction);
        }
    }
    public bool Charge(Habbo habbo, int credits, int points = 0, int pointsType = ActivityPointType.Duckets, Func<IDbConnection, IDbTransaction, bool>? deliver = null, bool kickbackEligible = true)
    {
        if (credits < 0 || points < 0 || !ActivityPointType.IsValid(pointsType)) {
            return false;
        }

        lock (habbo.WalletSync) {
            if (habbo.WalletClosed || habbo.Credits < credits || habbo.Currencies[pointsType] < points) {
                return false;
            }

            using var connection = database.Connection();
            connection.Open();
            using var transaction = connection.BeginTransaction();

            if (connection.ExecuteScalar<int?>("SELECT id FROM users WHERE id = @id FOR UPDATE", new { id = habbo.Id }, transaction) == null) {
                return false;
            }

            var remainingCredits = habbo.Credits - credits;
            var remainingPoints = habbo.Currencies[pointsType] - points;
            var now = clock.GetUtcNow();
            connection.Execute("UPDATE users SET credits = @remainingCredits WHERE id = @id", new { id = habbo.Id, remainingCredits }, transaction);

            if (points > 0) {
                UserCurrencyStore.Set(connection, habbo.Id, pointsType, remainingPoints, transaction);
            }

            RecordSpending(connection, transaction, habbo.Id, credits, now, kickbackEligible && habbo.Access.Membership.Active(now));

            if (deliver != null && !deliver(connection, transaction)) {
                return false;
            }

            transaction.Commit();
            habbo.Credits = remainingCredits;

            if (points > 0) {
                habbo.Currencies[pointsType] = remainingPoints;
            }

            return true;
        }
    }

    public ClubKickback Kickback(Habbo habbo)
    {
        var now = clock.GetUtcNow().ToUniversalTime();
        var start = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero).UtcDateTime;
        var membership = habbo.Access.Membership;
        var streak = membership.Active(now) && membership.StartedAt is { } running ? (int)(ClubMembership.WholeSeconds(now - running) / ClubMembership.Day) : 0;
        using var connection = database.Connection();
        var spent = connection.ExecuteScalar<long>("SELECT COALESCE(SUM(credits), 0) FROM club_credit_spending WHERE user_id = @id AND spent_at >= @start AND spent_at < @end", new { id = habbo.Id, start, end = NextPayday(now).UtcDateTime });
        var rewarded = connection.ExecuteScalar<long>("SELECT COALESCE(SUM(streak_bonus + spending_bonus), 0) FROM club_paydays WHERE user_id = @id AND paid = 1", new { id = habbo.Id });
        var missed = connection.ExecuteScalar<long>("SELECT COALESCE(SUM(streak_bonus + spending_bonus), 0) FROM club_paydays WHERE user_id = @id AND paid = 0", new { id = habbo.Id });

        return new(streak, membership.FirstStartedAt is { } first ? first.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture) : "", Percentage,
            (int)Math.Min(int.MaxValue, missed), (int)Math.Min(int.MaxValue, rewarded), (int)Math.Min(int.MaxValue, spent), StreakBonus(streak), SpendingBonus(spent, Percentage), (int)((NextPayday(now) - now).TotalMinutes));
    }

    public void RunPaydays()
    {
        var now = clock.GetUtcNow();
        using var connection = database.Connection();
        var members = connection.Query<IntervalStartRow>("SELECT user_id AS UserId, MIN(started_at) AS Started FROM club_membership_intervals GROUP BY user_id").ToArray();

        foreach (var member in members) {
            var due = NextPayday(member.Started);
            var last = connection.ExecuteScalar<DateTimeOffset?>("SELECT MAX(payday) FROM club_paydays WHERE user_id = @id", new { id = member.UserId });

            if (last is { } previous) {
                due = previous.AddMonths(1);
            }

            while (due <= now) {
                Pay(member.UserId, due);
                due = due.AddMonths(1);
            }
        }
    }

    private sealed class IntervalStartRow
    {
        public int UserId { get; set; }
        public DateTimeOffset Started { get; set; }
    }

    private void Pay(int userId, DateTimeOffset payday)
    {
        using var account = sessionGate.Enter(userId);
        var habbo = clients.GetClientByUserId(userId)?.GetHabbo();

        // Gate excludes login/logout; wallet lock excludes in-session purchases and currency saves.
        lock (habbo?.WalletSync ?? new object()) {
            using var connection = database.Connection();
            connection.Open();
            using var transaction = connection.BeginTransaction();
            var balance = connection.ExecuteScalar<int?>("SELECT credits FROM users WHERE id = @userId FOR UPDATE", new { userId }, transaction);
            var due = payday.UtcDateTime;

            if (balance == null || connection.ExecuteScalar<int>("SELECT COUNT(*) FROM club_paydays WHERE user_id = @userId AND payday = @due", new { userId, due }, transaction) > 0) {
                return;
            }

            var interval = connection.ExecuteScalar<DateTimeOffset?>("SELECT MIN(started_at) FROM club_membership_intervals WHERE user_id = @userId AND started_at <= @due AND expires_at > @due", new { userId, due }, transaction);
            var spent = connection.ExecuteScalar<long>("SELECT COALESCE(SUM(credits), 0) FROM club_credit_spending WHERE user_id = @userId AND spent_at >= @start AND spent_at < @due", new { userId, start = payday.AddMonths(-1).UtcDateTime, due }, transaction);
            var streak = interval is { } started ? (int)(ClubMembership.WholeSeconds(payday - started) / ClubMembership.Day) : 0;
            var streakBonus = StreakBonus(streak);
            var spendingBonus = SpendingBonus(spent, Percentage);
            var paid = interval.HasValue;
            var reward = paid ? (long)streakBonus + spendingBonus : 0;
            var credits = Math.Min(int.MaxValue, (habbo?.Credits ?? balance.Value) + reward);
            connection.Execute("INSERT INTO club_paydays (user_id, payday, spent, streak_bonus, spending_bonus, paid) VALUES (@userId, @due, @spent, @streakBonus, @spendingBonus, @paid)", new { userId, due, spent = (int)Math.Min(int.MaxValue, spent), streakBonus, spendingBonus, paid }, transaction);
            connection.Execute("UPDATE users SET credits = @credits WHERE id = @userId", new { userId, credits }, transaction);
            transaction.Commit();

            if (habbo != null && reward > 0) {
                habbo.Credits = (int)credits;
                habbo.Client?.Send(new CreditBalanceComposer(habbo.Credits));
            }
        }
    }
}
