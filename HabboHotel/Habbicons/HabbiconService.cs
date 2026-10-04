using Plus.Communication.Packets;
using System.Data;
using Dapper;
using Plus.Database;
using Plus.HabboHotel.Users;
using Plus.HabboHotel.Subscriptions;

namespace Plus.HabboHotel.Habbicons;

public sealed class HabbiconService(IDatabase database) : IHabbiconService
{
    public const int UnseenCategory = 8;

    public HabbiconSnapshot Load(int userId)
    {
        using var connection = database.Connection();
        return Load(connection, userId, null);
    }

    private static HabbiconSnapshot Load(IDbConnection connection, int userId, IDbTransaction? transaction)
    {
        var items = new Dictionary<int, HabbiconItem>();
        var unseen = new List<int>();
        var rows = connection.Query("""
            SELECT h.*, u.state, u.unseen, c.reward_id
            FROM habbicons h JOIN habbicon_collections c ON c.id = h.collection_id
            LEFT JOIN users_habbicons u ON u.habbicon_id = h.id AND u.user_id = @userId
            ORDER BY h.collection_id, h.id
            """, new { userId }, transaction);
        foreach (var row in rows)
        {
            int id = row.id, collectionId = row.collection_id, rewardId = row.reward_id;
            int credits = checked((int)row.cost_credits), points = checked((int)row.cost_points), pointsType = checked((int)row.points_type);
            int state = row.state != null ? (int)row.state
                : Convert.ToBoolean(row.default_owned) ? HabbiconState.Owned
                : rewardId == id ? HabbiconState.Reward
                : !Convert.ToBoolean(row.available) ? HabbiconState.Unavailable
                : credits == 0 && points == 0 ? HabbiconState.Claimable : HabbiconState.NotOwned;
            items.Add(id, new(id, (string)row.name, collectionId, state, credits, points, pointsType));
            if (row.unseen != null && Convert.ToBoolean(row.unseen)) unseen.Add(id);
        }
        var collections = new List<HabbiconCollection>();
        foreach (var row in connection.Query("SELECT * FROM habbicon_collections ORDER BY id", transaction: transaction))
        {
            int id = row.id, rewardId = row.reward_id;
            var members = items.Values.Where(item => item.CollectionId == id && item.Id != rewardId).ToArray();
            bool complete = members.Length > 0 && members.All(item => item.Collected);
            items.TryGetValue(rewardId, out var reward);
            if (reward != null && reward.CollectionId == id && complete && reward.State == HabbiconState.Reward)
                items[rewardId] = reward = reward with { State = HabbiconState.Claimable };
            collections.Add(new(id, (string)row.name, complete, rewardId, reward?.State ?? HabbiconState.Unavailable,
                checked((int)row.cost_credits), checked((int)row.cost_points), checked((int)row.points_type), members));
        }
        var recent = connection.Query<int>("""
            SELECT habbicon_id FROM users_habbicons WHERE user_id = @userId AND last_used > 0 AND state IN (2, 3)
            ORDER BY last_used DESC, habbicon_id DESC LIMIT 10
            """, new { userId }, transaction).Where(items.ContainsKey).ToArray();
        return new(collections.AsReadOnly(), new System.Collections.ObjectModel.ReadOnlyDictionary<int, HabbiconItem>(items), recent, unseen.AsReadOnly());
    }

    public HabbiconChange Change(Habbo habbo, HabbiconAction action, int id) => WithWallet(habbo,
        balances => Change(habbo.Id, action, id, balances, habbo.Access.Membership));

    // The row lock also serializes ownership changes from other connections and catalog purchases.
    internal HabbiconChange Change(int userId, HabbiconAction action, int id, HabbiconBalances? balances = null, ClubMembership? membership = null) =>
        Transact(userId, (connection, transaction, stored) => Change(connection, transaction, userId, action, id, balances ?? stored, membership));

    private static HabbiconChange WithWallet(Habbo habbo, Func<HabbiconBalances, HabbiconChange> work)
    {
        // Plus packets are serialized per client. Its background currency timer and logout use the same lock.
        lock (habbo.WalletSync)
        {
            if (habbo.WalletClosed) throw new HabbiconRejected(HabbiconActionError.InvalidRequest);
            var change = work(new(habbo.Credits, habbo.Duckets, habbo.Diamonds));
            if (change.Balances is { } balances)
            {
                habbo.Credits = balances.Credits;
                habbo.Duckets = balances.Duckets;
                habbo.Diamonds = balances.Diamonds;
            }
            return change;
        }
    }

    public HabbiconChange BuyCatalog(Habbo habbo, int id, int credits, int duckets, int diamonds) => WithWallet(habbo,
        balances => BuyCatalog(habbo.Id, id, credits, duckets, diamonds, balances, habbo.Access.Membership));

    internal HabbiconChange BuyCatalog(int userId, int id, int credits, int duckets, int diamonds, HabbiconBalances? balances = null, ClubMembership? membership = null) =>
        Transact(userId, (connection, transaction, stored) =>
        {
            var before = Load(connection, userId, transaction);
            var item = before.RequireItem(id);
            if (item.Collected) throw new HabbiconRejected(HabbiconActionError.AlreadyOwned);
            if (item.State != HabbiconState.NotOwned) throw new HabbiconRejected(HabbiconActionError.InvalidRequest);
            var afterBalances = Charge(connection, transaction, userId, balances ?? stored, credits, duckets, diamonds, membership);
            Save(connection, transaction, userId, id, HabbiconState.Owned, true);
            return Changed(connection, transaction, userId, before, afterBalances);
        });

    private static HabbiconChange Change(IDbConnection connection, IDbTransaction transaction, int userId,
        HabbiconAction action, int id, HabbiconBalances balances, ClubMembership? membership)
    {
        var before = Load(connection, userId, transaction);
        HabbiconBalances? afterBalances = null;
        if (action == HabbiconAction.BuyCollection)
        {
            var collection = before.Collections.FirstOrDefault(set => set.Id == id) ?? throw new HabbiconRejected(HabbiconActionError.InvalidRequest);
            var missing = collection.Items.Where(item => !item.Collected).ToArray();
            if (missing.Length == 0) throw new HabbiconRejected(HabbiconActionError.AlreadyOwned);
            if ((collection.Credits <= 0 && collection.Points <= 0) || missing.Any(item => item.State != HabbiconState.NotOwned))
                throw new HabbiconRejected(HabbiconActionError.InvalidRequest);
            afterBalances = ChargePoints(connection, transaction, userId, balances, collection.Credits, collection.Points, collection.PointsType, membership);
            foreach (var item in missing) Save(connection, transaction, userId, item.Id, HabbiconState.Owned, true);
        }
        else
        {
            var item = before.RequireItem(id);
            int state;
            switch (action)
            {
                case HabbiconAction.Buy:
                    if (item.Collected) throw new HabbiconRejected(HabbiconActionError.AlreadyOwned);
                    if (!item.Purchasable) throw new HabbiconRejected(HabbiconActionError.InvalidRequest);
                    afterBalances = ChargePoints(connection, transaction, userId, balances, item.Credits, item.Points, item.PointsType, membership);
                    state = HabbiconState.Owned;
                    break;
                case HabbiconAction.Claim:
                    if (item.State != HabbiconState.Claimable) throw new HabbiconRejected(HabbiconActionError.AlreadyOwned);
                    state = HabbiconState.Owned;
                    break;
                case HabbiconAction.Favorite:
                case HabbiconAction.Unfavorite:
                    if (!item.Owned) throw new HabbiconRejected(HabbiconActionError.AlreadyOwned);
                    state = action == HabbiconAction.Favorite ? HabbiconState.Favorite : HabbiconState.Owned;
                    break;
                default: throw new HabbiconRejected(HabbiconActionError.InvalidRequest);
            }
            Save(connection, transaction, userId, id, state, action is HabbiconAction.Buy or HabbiconAction.Claim);
        }
        return Changed(connection, transaction, userId, before, afterBalances);
    }

    private static HabbiconChange Changed(IDbConnection connection, IDbTransaction transaction, int userId,
        HabbiconSnapshot before, HabbiconBalances? balances)
    {
        var after = Load(connection, userId, transaction);
        var changed = after.Items.Values.Where(item => before.RequireItem(item.Id).State != item.State).ToArray();
        foreach (var item in changed)
            if (item.State == HabbiconState.Claimable && before.RequireItem(item.Id).State == HabbiconState.Reward)
                Save(connection, transaction, userId, item.Id, HabbiconState.Claimable, true);
        return new(Load(connection, userId, transaction), changed, balances);
    }

    public bool Use(int userId, int id) => Transact(userId, (connection, transaction, _) =>
    {
        var item = Load(connection, userId, transaction).Items.GetValueOrDefault(id);
        if (item == null || !item.Owned) return false;
        Save(connection, transaction, userId, id, item.State, false);
        long lastUsed = connection.QuerySingle<long>("SELECT COALESCE(MAX(last_used), 0) FROM users_habbicons WHERE user_id = @userId",
            new { userId }, transaction);
        connection.Execute("UPDATE users_habbicons SET last_used = @lastUsed WHERE user_id = @userId AND habbicon_id = @id",
            new { lastUsed = Math.Max(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), lastUsed + 1), userId, id }, transaction);
        return true;
    });

    public void ClearUnseen(int userId, IReadOnlyList<int> ids)
    {
        if (ids.Count > 1000) throw new HabbiconRejected(HabbiconActionError.InvalidRequest);
        using var connection = database.Connection();
        connection.Execute(ids.Count == 0
            ? "UPDATE users_habbicons SET unseen = FALSE WHERE user_id = @userId"
            : "UPDATE users_habbicons SET unseen = FALSE WHERE user_id = @userId AND habbicon_id IN @ids", new { userId, ids });
    }

    private static HabbiconBalances ChargePoints(IDbConnection connection, IDbTransaction transaction, int userId,
        HabbiconBalances balances, int credits, int points, int pointsType, ClubMembership? membership)
    {
        if (pointsType is not (0 or 5)) throw new HabbiconRejected(HabbiconActionError.InvalidRequest);
        return Charge(connection, transaction, userId, balances, credits, pointsType == 0 ? points : 0, pointsType == 5 ? points : 0, membership);
    }

    private static HabbiconBalances Charge(IDbConnection connection, IDbTransaction transaction, int userId,
        HabbiconBalances balances, int credits, int duckets, int diamonds, ClubMembership? membership)
    {
        if (credits < 0 || duckets < 0 || diamonds < 0) throw new HabbiconRejected(HabbiconActionError.InvalidRequest);
        if (balances.Credits < credits) throw new HabbiconRejected(HabbiconActionError.InsufficientCredits);
        if (balances.Duckets < duckets || balances.Diamonds < diamonds) throw new HabbiconRejected(HabbiconActionError.InsufficientActivityPoints);
        var after = new HabbiconBalances(balances.Credits - credits, balances.Duckets - duckets, balances.Diamonds - diamonds);
        connection.Execute("UPDATE users SET credits = @Credits, activity_points = @Duckets, vip_points = @Diamonds WHERE id = @userId",
            new { after.Credits, after.Duckets, after.Diamonds, userId }, transaction);
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        ClubRewards.RecordSpending(connection, transaction, userId, credits, now, membership?.Active(now) == true);
        return after;
    }

    private static void Save(IDbConnection connection, IDbTransaction transaction, int userId, int id, int state, bool unseen) =>
        connection.Execute("""
            INSERT INTO users_habbicons (user_id, habbicon_id, state, unseen) VALUES (@userId, @id, @state, @unseen)
            ON DUPLICATE KEY UPDATE state = VALUES(state), unseen = unseen OR VALUES(unseen)
            """, new { userId, id, state, unseen }, transaction);

    private T Transact<T>(int userId, Func<IDbConnection, IDbTransaction, HabbiconBalances, T> work)
    {
        using var connection = database.Connection();
        connection.Open();
        using var transaction = connection.BeginTransaction();
        var balances = connection.QuerySingleOrDefault<HabbiconBalances>("""
            SELECT credits AS Credits, activity_points AS Duckets, vip_points AS Diamonds FROM users WHERE id = @userId FOR UPDATE
            """, new { userId }, transaction) ?? throw new HabbiconRejected(HabbiconActionError.InvalidRequest);
        var result = work(connection, transaction, balances);
        transaction.Commit();
        return result;
    }
}
