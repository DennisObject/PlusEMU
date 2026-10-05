using Dapper;
using Plus.Database;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Users;
using Plus.HabboHotel.Users.Messenger;

namespace Plus.HabboHotel.Friends;

internal class MessengerDataLoader : IMessengerDataLoader
{
    private readonly IDatabase _database;
    private readonly IGameClientManager _gameClientManager;
    private readonly Plus.HabboHotel.Permissions.IAccessControl _permissions;
    private readonly Plus.Core.Settings.ISettingsManager _settings;
    private readonly TimeProvider _clock;

    public MessengerDataLoader(IDatabase database, IGameClientManager gameClientManager, Plus.HabboHotel.Permissions.IAccessControl permissions, Plus.Core.Settings.ISettingsManager settings, TimeProvider clock)
    {
        _database = database;
        _gameClientManager = gameClientManager;
        _permissions = permissions; _settings = settings;
        _clock = clock;
    }

    public async Task<List<MessengerBuddy>> GetBuddiesForUser(int userId)
    {
        using var connection = _database.Connection();
        var query = "SELECT users.id,users.username,users.motto,users.look,users.last_online AS LastOnlineAt, messenger_friendships.relationship FROM users JOIN users_settings ON users_settings.user_id = users.id JOIN messenger_friendships ON users.id = messenger_friendships.user_two_id WHERE messenger_friendships.user_one_id = @userId";
        return (await connection.QueryAsync<MessengerBuddy>(query, new { userId })).ToList();
    }

    public async Task<List<MessengerRequest>> GetRequestsForUser(int userId)
    {
        using var connection = _database.Connection();
        return (await connection.QueryAsync<MessengerRequest>("SELECT messenger_requests.from_id,messenger_requests.to_id,users.username FROM users JOIN messenger_requests ON users.id = messenger_requests.from_id WHERE messenger_requests.to_id = @userId",
            new
            {
                userId
            })).ToList();
    }

    public async Task<List<int>> GetOutstandingRequestsForUser(int userId)
    {
        using var connection = _database.Connection();
        return (await connection.QueryAsync<int>("SELECT to_id FROM messenger_requests WHERE from_id = @userId", new { userId })).ToList();
    }

    /// <summary>
    /// Consumes exactly the request from <paramref name="fromId"/> and commits both friendship rows in one transaction.
    /// Both accounts are locked in ascending id order first; a self pair, a missing account, an absent request or a full list
    /// rolls back. Both buddy views are materialized inside the transaction, so a failure there also rolls back.
    /// </summary>
    public async Task<FriendAcceptResult> AcceptFriendRequest(int acceptorId, int fromId)
    {
        using var connection = _database.Connection();
        connection.Open();
        using var transaction = connection.BeginTransaction();
        if (!await LockExactPair(connection, transaction, acceptorId, fromId)) return new(FriendRequestError.NoFriendRequest);
        if (await connection.ExecuteAsync("DELETE FROM messenger_requests WHERE from_id = @fromId AND to_id = @acceptorId", new { fromId, acceptorId }, transaction) != 1)
            return new(FriendRequestError.NoFriendRequest);
        foreach (var id in new[] { acceptorId, fromId })
        {
            var access = _gameClientManager.GetClientByUserId(id)?.GetHabbo().Access ?? _permissions.Resolve(id);
            var friends = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM messenger_friendships WHERE user_one_id = @id", new { id }, transaction);
            if (friends >= Plus.HabboHotel.Subscriptions.ClubLimits.For(access, "friends", _settings)) return new(FriendRequestError.FriendLimitReached);
        }
        await InsertFriendshipIfMissing(connection, transaction, acceptorId, fromId);
        await InsertFriendshipIfMissing(connection, transaction, fromId, acceptorId);
        var from = await BuddyFor(connection, transaction, acceptorId, fromId) ?? throw new InvalidOperationException("Accepted friend view missing.");
        var to = await BuddyFor(connection, transaction, fromId, acceptorId) ?? throw new InvalidOperationException("Requester friend view missing.");
        transaction.Commit();
        return new(null, from, to);
    }

    public async Task<MessengerBuddy> CreateBuddy(int userId)
    {
        using var connection = _database.Connection();
        var buddy = await connection.QuerySingleAsync<MessengerBuddy>("SELECT users.id,users.username,users.motto,users.look,users.last_online AS LastOnlineAt FROM users WHERE id = @userId", new { userId });
        return buddy;
    }

    // Both accounts must exist and be distinct; their rows are locked in ascending id order.
    private static async Task<bool> LockExactPair(System.Data.IDbConnection connection, System.Data.IDbTransaction transaction, int first, int second)
    {
        if (first == second) return false;
        var ids = new[] { first, second }.OrderBy(id => id).ToArray();
        return (await connection.QueryAsync<int>("SELECT id FROM users WHERE id IN @ids ORDER BY id FOR UPDATE", new { ids }, transaction)).Count() == 2;
    }

    private static async Task InsertFriendshipIfMissing(System.Data.IDbConnection connection, System.Data.IDbTransaction transaction, int userOneId, int userTwoId) =>
        await connection.ExecuteAsync("INSERT INTO messenger_friendships (user_one_id, user_two_id) SELECT @userOneId, @userTwoId FROM DUAL WHERE NOT EXISTS (SELECT 1 FROM messenger_friendships WHERE user_one_id = @userOneId AND user_two_id = @userTwoId)", new { userOneId, userTwoId }, transaction);

    private static async Task<MessengerBuddy?> BuddyFor(System.Data.IDbConnection connection, System.Data.IDbTransaction transaction, int userId, int friendId) =>
        await connection.QuerySingleOrDefaultAsync<MessengerBuddy>("SELECT users.id,users.username,users.motto,users.look,users.last_online AS LastOnlineAt, messenger_friendships.relationship FROM users INNER JOIN messenger_friendships ON users.id = messenger_friendships.user_two_id WHERE users.id = @friendId AND messenger_friendships.user_one_id = @userId", new { userId, friendId }, transaction);

    public async Task<MessengerBuddy?> GetBuddy(int userId, int friendId)
    {
        using var connection = _database.Connection();
        var buddy = await connection.QuerySingleOrDefaultAsync<MessengerBuddy>("SELECT users.id,users.username,users.motto,users.look,users.last_online AS LastOnlineAt, messenger_friendships.relationship FROM users INNER JOIN messenger_friendships ON users.id = messenger_friendships.user_two_id WHERE users.id = @friendId AND messenger_friendships.user_one_id = @userId", new { userId, friendId });
        return buddy;
    }

    public void BroadcastStatusUpdate(Habbo habbo, MessengerEventTypes eventType, string value)
    {
        foreach (var client in habbo.Messenger.Friends.Keys.Select(f => _gameClientManager.GetClientByUserId(f)))
        {
            if (client == null) continue;
            var messenger = client.GetHabbo().Messenger;
            if (!messenger.Friends.TryGetValue(habbo.Id, out var buddy)) continue;
            messenger.UpdateFriendStatus(buddy, eventType, value);
        }
    }

    public async Task LogPrivateMessage(int fromId, int toId, string message)
    {
        using var connection = _database.Connection();
        await connection.ExecuteAsync("INSERT INTO chatlogs_console (from_id, to_id, message, timestamp) VALUES (@fromId, @toId, @message, @createdAtUtc)",
            new { fromId, toId, message, createdAtUtc = _clock.GetUtcNow().UtcDateTime });
    }

    public async Task LogPrivateOfflineMessage(int fromId, int toId, string message)
    {
        using var connection = _database.Connection();
        await connection.ExecuteAsync("INSERT INTO `messenger_offline_messages` (`to_id`, `from_id`, `message`, `timestamp`) VALUES (@toId, @fromId, @message, @createdAtUtc)",
            new { toId, fromId, message, createdAtUtc = _clock.GetUtcNow().UtcDateTime });
    }

    public async Task<Dictionary<int, List<(string Message, int SecondsAgo)>>> GetAndDeleteOfflineMessages(int userId)
    {
        using var connection = _database.Connection();
        connection.Open();
        using var transaction = connection.BeginTransaction();
        var readAt = _clock.GetUtcNow();
        // Rows are locked as they are read, then only those IDs are deleted, so messages that arrive after the read survive.
        var rows = (await connection.QueryAsync<OfflineMessageRow>("SELECT id AS Id, from_id AS FromId, message AS Message, timestamp AS SentAt FROM messenger_offline_messages WHERE to_id = @userId ORDER BY id FOR UPDATE", new { userId }, transaction)).ToList();
        await DeleteReadOfflineMessages(connection, transaction, rows.Select(row => row.Id).ToArray());
        transaction.Commit();
        return rows.GroupBy(row => row.FromId).ToDictionary(group => group.Key, group => group
            .OrderBy(row => row.SentAt).ThenBy(row => row.Id)
            .Select(row => (row.Message, MessengerTime.SecondsBetween(readAt, row.SentAt))).ToList());
    }

    private sealed class OfflineMessageRow
    {
        public int Id { get; set; }
        public int FromId { get; set; }
        public string Message { get; set; } = string.Empty;
        public DateTimeOffset? SentAt { get; set; }
    }

    internal static async Task DeleteReadOfflineMessages(System.Data.IDbConnection connection, System.Data.IDbTransaction transaction, int[] ids)
    {
        if (ids.Length == 0) return;
        await connection.ExecuteAsync("DELETE FROM messenger_offline_messages WHERE id IN @ids", new { ids }, transaction);
    }


    public async Task<int> GetFriendCount(int userId)
    {
        using var connection = _database.Connection();
        return await connection.ExecuteScalarAsync<int>("SELECT count(0) FROM messenger_friendships WHERE user_one_id = @userid OR user_two_id = @userid", new { userid = userId });
    }

    public async Task<int> DeleteFriendship(int userOneId, int userTwoId)
    {
        using var connection = _database.Connection();
        connection.Open();
        using var transaction = connection.BeginTransaction();
        if (!await LockExactPair(connection, transaction, userOneId, userTwoId)) return 0;
        var removed = await connection.ExecuteAsync("DELETE FROM messenger_friendships WHERE (user_one_id = @userOneId AND user_two_id = @userTwoId) OR (user_one_id = @userTwoId AND user_two_id = @userOneId)", new { userOneId, userTwoId }, transaction);
        transaction.Commit();
        return removed;
    }

    public async Task SetRelationship(int userOneId, int userTwoId, int relationship)
    {
        using var connection = _database.Connection();
        await connection.ExecuteAsync("UPDATE messenger_friendships SET relationship = @relationship WHERE user_one_id = @userOneId AND user_two_id = @userTwoId", new { userOneId, userTwoId, relationship });
    }

    // The schema has no unique pair index, so existence is checked under both sorted account locks before the insert.
    public async Task<bool> RegisterFriendRequest(int fromUserId, int toUserId)
    {
        using var connection = _database.Connection();
        connection.Open();
        using var transaction = connection.BeginTransaction();
        if (!await LockExactPair(connection, transaction, fromUserId, toUserId)) return false;
        if (await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM messenger_requests WHERE from_id = @fromUserId AND to_id = @toUserId", new { fromUserId, toUserId }, transaction) > 0) return false;
        await connection.ExecuteAsync("INSERT INTO messenger_requests (from_id, to_id) VALUES (@fromUserId, @toUserId)", new { fromUserId, toUserId }, transaction);
        transaction.Commit();
        return true;
    }

    // Removes only the request that goes from fromUserId to toUserId; the reverse direction is a different request.
    public async Task<int> DeleteFriendRequest(int fromUserId, int toUserId)
    {
        using var connection = _database.Connection();
        connection.Open();
        using var transaction = connection.BeginTransaction();
        if (!await LockExactPair(connection, transaction, fromUserId, toUserId)) return 0;
        var removed = await connection.ExecuteAsync("DELETE FROM messenger_requests WHERE from_id = @fromUserId AND to_id = @toUserId", new { fromUserId, toUserId }, transaction);
        transaction.Commit();
        return removed;
    }

    public async Task<(int userId, bool blockFriendRequests)> CanReceiveFriendRequests(string name)
    {
        using var connection = _database.Connection();
        var (userId, blocked) = await connection.QuerySingleOrDefaultAsync<(int, bool)>("SELECT users.`id`, settings.`block_newfriends` FROM `users` INNER JOIN `users_settings` settings ON settings.user_id = users.id WHERE users.`username` = @name LIMIT 1", new { name });
        return (userId, blocked);
    }

    public async Task<Dictionary<int, (MessengerBuddy buddy, int count)>> GetRelationshipsForUserAsync(int userId)
    {
        using var connection = _database.Connection();
        var query = "SELECT messenger_friendships.relationship, COUNT(*) as count, users.id, users.username, users.look FROM messenger_friendships JOIN users ON users.id = messenger_friendships.user_two_id WHERE messenger_friendships.user_one_id = @userId AND messenger_friendships.relationship > 0 GROUP BY messenger_friendships.relationship";
        var relationships = (await connection.QueryAsync<(int relationship, int count, int id, string username, string look)>(query, new { userId }))
            .GroupBy(r => r.relationship)
            .ToDictionary(g => g.Key, g => (new MessengerBuddy
            {
                Id = g.First().id,
                Username = g.First().username,
                Look = g.First().look
            }, g.First().count));
        return relationships;
    }
}
