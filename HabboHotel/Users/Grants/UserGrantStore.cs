using System.Data;
using System.Text.Json;
using Dapper;
using MySqlConnector;
using Plus.Database;

namespace Plus.HabboHotel.Users.Grants;

public sealed class GrantReceipt
{
    public int UserId { get; init; }
    public string PayloadSha256 { get; init; } = string.Empty;
    public string ResultJson { get; init; } = string.Empty;
}

public sealed class BadgeDefinitionRow
{
    public string Code { get; init; } = string.Empty;
    public string RequiredRight { get; init; } = string.Empty;
}

public interface IUserGrantStore
{
    GrantReceipt? FindReceipt(string key);

    /// <summary>Validates and applies a bundle for an offline account in one transaction, recording the receipt in it.
    /// Returns null when another grant claimed the key first; nothing is applied then.</summary>
    GrantOutcome? ApplyBundle(int userId, string key, GrantBundle bundle, Func<string, bool> hasRight);

    BadgeDefinitionRow? FindBadge(string code);

    /// <summary>Inserts the badge row; false when the account already owns it.</summary>
    bool InsertBadge(int userId, string code);

    bool UserExists(int userId);

    /// <summary>Deletes an owned badge row; false when the account does not own it.</summary>
    bool DeleteBadge(int userId, string code);

    /// <summary>Writes the given settings (null keeps the stored value) in one locked transaction and returns the stored result.</summary>
    (string? Error, uint HomeRoom, int FriendBarState) UpdateSettings(int userId, uint? homeRoom, int? friendBarState);
}

/// <summary>
/// Offline grant SQL. Each grant locks the users row first, so it serializes with every other locked write to the account.
/// Logins never overlap it, because the caller holds the account session gate.
/// </summary>
public sealed class UserGrantStore(IDatabase database) : IUserGrantStore
{
    private static readonly JsonSerializerOptions ResultJson = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public GrantReceipt? FindReceipt(string key)
    {
        using var connection = database.Connection();

        return connection.QuerySingleOrDefault<GrantReceipt>("SELECT user_id AS UserId, payload_sha256 AS PayloadSha256, result_json AS ResultJson " +
            "FROM rcon_grants WHERE idempotency_key = @key AND status = 'applied'", new { key });
    }

    public GrantOutcome? ApplyBundle(int userId, string key, GrantBundle bundle, Func<string, bool> hasRight)
    {
        using var connection = database.Connection();
        connection.Open();
        using var transaction = connection.BeginTransaction();

        if (connection.QuerySingleOrDefault<int?>("SELECT credits FROM users WHERE id = @userId FOR UPDATE", new { userId }, transaction) is not { } credits) {
            return GrantOutcome.Fail(GrantOutcome.UserNotFound);
        }

        var newCredits = (long)credits + bundle.Credits;

        if (GrantOutcome.CheckBalance(newCredits) is { } creditError) {
            return GrantOutcome.Fail(creditError, "credits");
        }

        var types = bundle.Currencies.Select(currency => currency.Key).ToArray();
        var held = types.Length == 0 ? new Dictionary<int, int>() :
            connection.Query<(int Type, int Amount)>("SELECT type, amount FROM user_currencies WHERE user_id = @userId AND type IN @types FOR UPDATE",
                new { userId, types }, transaction).ToDictionary(row => row.Type, row => row.Amount);
        var currencies = new SortedDictionary<int, int>();

        foreach (var (type, delta) in bundle.Currencies) {
            var balance = (long)held.GetValueOrDefault(type) + delta;

            if (GrantOutcome.CheckBalance(balance) is { } error) {
                return GrantOutcome.Fail(error, $"currency {type}");
            }

            currencies[type] = (int)balance;
        }

        var definitions = bundle.Badges.Count == 0 ? [] :
            connection.Query<BadgeDefinitionRow>("SELECT code AS Code, required_right AS RequiredRight FROM badge_definitions WHERE code IN @codes",
                new { codes = bundle.Badges }, transaction).ToArray();
        var badges = new List<BadgeDefinitionRow>();

        foreach (var code in bundle.Badges) {
            if (definitions.FirstOrDefault(row => string.Equals(row.Code, code, StringComparison.OrdinalIgnoreCase)) is not { } definition) {
                return GrantOutcome.Fail(GrantOutcome.UnknownBadge, code);
            }

            if (definition.RequiredRight.Length > 0 && !hasRight(definition.RequiredRight)) {
                return GrantOutcome.Fail(GrantOutcome.RestrictedBadge, code);
            }

            badges.Add(definition);
        }

        RankRole? role = null;

        if (bundle.Rank is { } rank) {
            role = Role(connection, rank, transaction);

            if (role == null) {
                return GrantOutcome.Fail(GrantOutcome.InvalidRank, $"No role has security level {rank}.");
            }
        }

        // Everything is validated; from here on only writes run.
        if (bundle.Credits != 0) {
            connection.Execute("UPDATE users SET credits = @credits WHERE id = @userId", new { userId, credits = (int)newCredits }, transaction);
        }

        UserCurrencyStore.SetMany(connection, userId, currencies, transaction);

        var itemIds = new List<uint>();

        foreach (var (baseId, amount) in bundle.Furniture) {
            for (var i = 0; i < amount; i++) {
                itemIds.Add(connection.QuerySingle<uint>("INSERT INTO items (base_item, user_id, room_id, x, y, z, wall_pos, rot, extra_data) " +
                    "VALUES (@baseId, @userId, 0, 0, 0, 0, '', 0, ''); SELECT LAST_INSERT_ID()", new { baseId, userId }, transaction));
            }
        }

        var granted = new List<string>();
        var skipped = new List<string>();

        foreach (var badge in badges) {
            (InsertBadge(connection, userId, badge.Code, transaction) ? granted : skipped).Add(badge.Code);
        }

        RankResult? rankResult = null;

        if (role != null) {
            var assigned = role.Slug != "default" && AssignRole(connection, userId, role.Id, key, transaction);
            rankResult = new RankResult(role.SecurityLevel, role.Id, role.Slug, assigned);
        }

        var result = JsonSerializer.Serialize(new BundleResult(userId, key, (int)newCredits, currencies, new FurnitureResult(itemIds.Count, itemIds),
            new BadgeResult(granted, skipped), rankResult), ResultJson);

        try {
            connection.Execute("INSERT INTO rcon_grants (idempotency_key, user_id, payload_sha256, status, result_json) VALUES (@key, @userId, @sha, 'applied', @result)",
                new { key, userId, sha = bundle.Sha256, result }, transaction);
        }
        catch (MySqlException exception) when (exception.ErrorCode == MySqlErrorCode.DuplicateKeyEntry) {
            transaction.Rollback();

            return null;
        }

        transaction.Commit();

        return GrantOutcome.Success(JsonDocument.Parse(result).RootElement.Clone());
    }

    public BadgeDefinitionRow? FindBadge(string code)
    {
        using var connection = database.Connection();

        return connection.QuerySingleOrDefault<BadgeDefinitionRow>("SELECT code AS Code, required_right AS RequiredRight FROM badge_definitions WHERE code = @code", new { code });
    }

    public bool InsertBadge(int userId, string code)
    {
        using var connection = database.Connection();

        return InsertBadge(connection, userId, code, null);
    }

    public bool UserExists(int userId)
    {
        using var connection = database.Connection();

        return connection.ExecuteScalar<int?>("SELECT id FROM users WHERE id = @userId", new { userId }) != null;
    }

    public bool DeleteBadge(int userId, string code)
    {
        using var connection = database.Connection();

        return connection.Execute("DELETE FROM user_badges WHERE user_id = @userId AND badge_id = @code", new { userId, code }) > 0;
    }

    public (string? Error, uint HomeRoom, int FriendBarState) UpdateSettings(int userId, uint? homeRoom, int? friendBarState)
    {
        using var connection = database.Connection();
        connection.Open();
        using var transaction = connection.BeginTransaction();

        if (connection.ExecuteScalar<int?>("SELECT id FROM users WHERE id = @userId FOR UPDATE", new { userId }, transaction) == null) {
            return (GrantOutcome.UserNotFound, 0, 0);
        }

        if (homeRoom > 0 && connection.ExecuteScalar<int?>("SELECT id FROM rooms WHERE id = @homeRoom", new { homeRoom }, transaction) == null) {
            return (GrantOutcome.UnknownRoom, 0, 0);
        }

        connection.Execute("INSERT INTO users_settings (user_id, home_room, friend_bar_state) VALUES (@userId, COALESCE(@homeRoom, 0), COALESCE(@friendBarState, 1)) " +
            "ON DUPLICATE KEY UPDATE home_room = COALESCE(@homeRoom, home_room), friend_bar_state = COALESCE(@friendBarState, friend_bar_state)",
            new { userId, homeRoom, friendBarState }, transaction);
        var stored = connection.QuerySingle<(uint HomeRoom, int FriendBarState)>("SELECT home_room, CAST(friend_bar_state AS SIGNED) FROM users_settings WHERE user_id = @userId",
            new { userId }, transaction);
        transaction.Commit();

        return (null, stored.HomeRoom, stored.FriendBarState);
    }

    private static bool InsertBadge(IDbConnection connection, int userId, string code, IDbTransaction? transaction) =>
        connection.Execute("INSERT IGNORE INTO user_badges (user_id, badge_id, badge_slot) VALUES (@userId, @code, 0)", new { userId, code }, transaction) > 0;

    // Rank N is the base role for security level N: the implicit default role for 1, else the lowest-weight staff role at N.
    private static RankRole? Role(IDbConnection connection, int rank, IDbTransaction transaction) => connection.QueryFirstOrDefault<RankRole>(
        "SELECT id AS Id, slug AS Slug, security_level AS SecurityLevel FROM roles WHERE security_level = @rank AND " +
        (rank == 1 ? "slug = 'default'" : "is_staff = 1 ORDER BY weight, id"), new { rank }, transaction);

    // Adds the role permanently; existing roles are never removed, so a grant cannot demote. users.rank is the derived cache.
    private static bool AssignRole(IDbConnection connection, int userId, int roleId, string key, IDbTransaction transaction)
    {
        if (connection.ExecuteScalar<int>("SELECT COUNT(*) FROM user_roles WHERE user_id = @userId AND role_id = @roleId AND expires_at IS NULL FOR UPDATE",
                new { userId, roleId }, transaction) > 0) {
            return false;
        }

        connection.Execute("INSERT INTO user_roles (user_id, role_id, granted_by, expires_at) VALUES (@userId, @roleId, NULL, NULL) " +
            "ON DUPLICATE KEY UPDATE granted_by = NULL, expires_at = NULL", new { userId, roleId }, transaction);
        connection.Execute("UPDATE users SET `rank` = GREATEST((SELECT security_level FROM roles WHERE slug = 'default'), COALESCE((SELECT MAX(r.security_level) " +
            "FROM user_roles ur JOIN roles r ON r.id = ur.role_id WHERE ur.user_id = @userId AND (ur.expires_at IS NULL OR ur.expires_at > UTC_TIMESTAMP(6))), 0)) " +
            "WHERE id = @userId", new { userId }, transaction);
        connection.Execute("INSERT INTO acl_audit_log (actor_id, action, target_type, target_id, payload) VALUES (NULL, 'role.assign', 'user', @userId, @payload)",
            new { userId, payload = JsonSerializer.Serialize(new { roleId, source = "rcon", idempotencyKey = key }) }, transaction);

        return true;
    }

    private sealed class RankRole
    {
        public int Id { get; init; }
        public string Slug { get; init; } = string.Empty;
        public int SecurityLevel { get; init; }
    }

    private sealed record BundleResult(int UserId, string IdempotencyKey, int Credits, SortedDictionary<int, int> Currencies, FurnitureResult Furniture,
        BadgeResult Badges, RankResult? Rank);

    private sealed record FurnitureResult(int Items, IReadOnlyList<uint> ItemIds);

    private sealed record BadgeResult(IReadOnlyList<string> Granted, IReadOnlyList<string> Skipped);

    private sealed record RankResult(int SecurityLevel, int RoleId, string Role, bool Assigned);
}
