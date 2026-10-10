using System.Data;
using System.Text.Json;
using Plus.HabboHotel.Items.Wired.Configuration;
using Dapper;
using Plus.Database;

namespace Plus.HabboHotel.Items.Wired.Variables;

/// <summary>Write-through values. Database errors propagate; callers must not acknowledge or emit a change on failure.</summary>
public sealed class DatabaseWiredVariableStore(IDatabase database) : IWiredVariableStore
{
    private const string ReadSql = """
        SELECT value AS Value, created_at AS CreatedAt, updated_at AS UpdatedAt
        FROM wired_variable_values WHERE definition_id=@DefinitionId AND target_kind=@Target AND holder_id=@HolderId
        """;

    public WiredVariableValue? Read(WiredVariableKey key)
    {
        using var connection = database.Connection();
        var row = connection.QuerySingleOrDefault<Row>(ReadSql, key);

        return row is null ? null : new(row.Value, row.CreatedAt, row.UpdatedAt);
    }

    public IReadOnlyDictionary<WiredVariableKey, WiredVariableValue> ReadMany(IReadOnlyCollection<WiredVariableKey> keys)
    {
        var result = new Dictionary<WiredVariableKey, WiredVariableValue>();

        if (keys.Count == 0) {
            return result;
        }

        var requested = keys.ToHashSet();
        using var connection = database.Connection();
        connection.Open();

        foreach (var group in keys.GroupBy(x => x.DefinitionId)) {
            var rows = connection.Query<Row>("""
                SELECT target_kind AS Target,holder_id AS HolderId,value AS Value,created_at AS CreatedAt,updated_at AS UpdatedAt
                FROM wired_variable_values WHERE definition_id=@definitionId AND holder_id IN @holderIds
                """, new { definitionId = group.Key, holderIds = group.Select(x => x.HolderId).Distinct().ToArray() });

            foreach (var row in rows) {
                var key = new WiredVariableKey(group.Key, row.Target, row.HolderId);

                if (requested.Contains(key)) {
                    result[key] = new(row.Value, row.CreatedAt, row.UpdatedAt);
                }
            }
        }

        return result;
    }

    public WiredVariableWrite Mutate(WiredVariableKey key, Func<WiredVariableValue?, WiredVariableValue?> update, WiredVariableAuthorization? authorization = null)
    {
        using var connection = database.Connection();
        connection.Open();
        using var transaction = connection.BeginTransaction(IsolationLevel.ReadCommitted);

        if (authorization is null || authorization.Lineage.IsEmpty
            || authorization.Lineage[^1].ItemId != key.DefinitionId || authorization.Lineage[^1].Target != key.Target
            || !Authorize(connection, transaction, authorization)) {
            return new(null, null);
        }

        if (LockDefinition(connection, transaction, key.DefinitionId)) {
            throw new InvalidOperationException("The owning variable definition was deleted.");
        }

        var beforeRow = connection.QuerySingleOrDefault<Row>(ReadSql, key, transaction);
        var before = beforeRow is null ? null : new WiredVariableValue(beforeRow.Value, beforeRow.CreatedAt, beforeRow.UpdatedAt);
        var after = update(before);

        if (after != before) {
            if (after is null) {
                connection.Execute("DELETE FROM wired_variable_values WHERE definition_id=@DefinitionId AND target_kind=@Target AND holder_id=@HolderId", key, transaction);
            }
            else {
                connection.Execute("""
                    INSERT INTO wired_variable_values (definition_id,target_kind,holder_id,value,created_at,updated_at)
                    VALUES (@DefinitionId,@Target,@HolderId,@Value,@CreatedAt,@UpdatedAt)
                    ON DUPLICATE KEY UPDATE value=@Value,created_at=@CreatedAt,updated_at=@UpdatedAt
                    """, new { key.DefinitionId, key.Target, key.HolderId, after.Value, CreatedAt = after.CreatedAt?.UtcDateTime, UpdatedAt = after.UpdatedAt?.UtcDateTime }, transaction);
            }
        }

        transaction.Commit();

        return new(before, after);
    }

    public IReadOnlyDictionary<WiredVariableKey, WiredVariableValue> GetHolders(uint definitionId)
    {
        using var connection = database.Connection();

        return connection.Query<Row>("""
            SELECT target_kind AS Target,holder_id AS HolderId,value AS Value,created_at AS CreatedAt,updated_at AS UpdatedAt
            FROM wired_variable_values WHERE definition_id=@definitionId
            """, new { definitionId }).ToDictionary(x => new WiredVariableKey(definitionId, x.Target, x.HolderId),
                x => new WiredVariableValue(x.Value, x.CreatedAt, x.UpdatedAt));
    }

    public int DeleteDefinition(uint definitionId)
    {
        using var connection = database.Connection();
        connection.Open();
        using var transaction = connection.BeginTransaction(IsolationLevel.ReadCommitted);
        LockDefinition(connection, transaction, definitionId);
        connection.Execute("UPDATE wired_variable_locks SET retired=1 WHERE definition_id=@definitionId", new { definitionId }, transaction);
        var removed = connection.Execute("DELETE FROM wired_variable_values WHERE definition_id=@definitionId", new { definitionId }, transaction);
        // Retain the tiny lock row: deleting/recreating it can defeat concurrent first-write serialization.
        transaction.Commit();

        return removed;
    }

    public IReadOnlyDictionary<WiredVariableKey, WiredVariableValue> ClearValues(uint definitionId, WiredVariableAuthorization authorization)
    {
        using var connection = database.Connection();
        connection.Open();
        using var transaction = connection.BeginTransaction(IsolationLevel.ReadCommitted);

        if (authorization.Lineage.IsEmpty || authorization.Lineage[^1].ItemId != definitionId || !Authorize(connection, transaction, authorization)) {
            return new Dictionary<WiredVariableKey, WiredVariableValue>();
        }

        if (LockDefinition(connection, transaction, definitionId)) {
            throw new InvalidOperationException("The owning variable definition was deleted.");
        }

        var removed = connection.Query<Row>("""
            SELECT target_kind AS Target,holder_id AS HolderId,value AS Value,created_at AS CreatedAt,updated_at AS UpdatedAt
            FROM wired_variable_values WHERE definition_id=@definitionId
            """, new { definitionId }, transaction).ToDictionary(x => new WiredVariableKey(definitionId, x.Target, x.HolderId),
                x => new WiredVariableValue(x.Value, x.CreatedAt, x.UpdatedAt));
        var count = connection.Execute("DELETE FROM wired_variable_values WHERE definition_id=@definitionId", new { definitionId }, transaction);

        if (count != removed.Count) {
            throw new InvalidOperationException("Variable clear count changed under the definition lock.");
        }

        transaction.Commit();

        return removed;
    }

    public WiredVariableHolderPage ReadPage(uint definitionId, WiredVariableTarget target, int page, int size, int sort,
        IReadOnlyCollection<long>? holderFilter = null, IReadOnlyDictionary<long, string>? names = null)
    {
        page = Math.Max(1, page);
        size = Math.Clamp(size, 1, 200);

        if (holderFilter is { Count: 0 }) {
            return new(0, page, size, []);
        }

        var where = "v.definition_id=@definitionId AND v.target_kind=@target" + (holderFilter is null ? "" : " AND v.holder_id IN @holderFilter");
        var order = sort switch { 0 => "v.value ASC,v.holder_id ASC", 1 => "v.value DESC,v.holder_id ASC", 2 => "Name ASC,v.holder_id ASC", _ => "v.holder_id ASC" };
        var arguments = new { definitionId, target, holderFilter, size, offset = (long)(page - 1) * size };
        using var connection = database.Connection();
        connection.Open();
        var total = connection.ExecuteScalar<long>("SELECT COUNT(*) FROM wired_variable_values v WHERE " + where, arguments);
        var rows = connection.Query<PageRow>("""
            SELECT v.target_kind AS Target,v.holder_id AS HolderId,v.value AS Value,v.created_at AS CreatedAt,v.updated_at AS UpdatedAt,
            COALESCE(u.username,f.public_name,CAST(v.holder_id AS CHAR)) AS Name
            FROM wired_variable_values v
            LEFT JOIN users u ON v.target_kind=0 AND v.holder_id=u.id
            LEFT JOIN items i ON v.target_kind=1 AND v.holder_id=i.id
            LEFT JOIN furniture f ON i.base_item=f.id
            WHERE
            """ + " " + where + " ORDER BY " + order + " LIMIT @size OFFSET @offset", arguments);

        return new((int)Math.Min(int.MaxValue, total), page, size, rows.Select(x => new WiredVariableStoredHolder(
            new(definitionId, x.Target, x.HolderId), x.Name, new(x.Value, x.CreatedAt, x.UpdatedAt))).ToArray());
    }

    // Lock order is rooms ascending, then definition items/configs ascending, then the value guard.
    // Every room and reference is checked on this same transaction, including an unloaded source room.
    private static bool Authorize(IDbConnection connection, IDbTransaction transaction, WiredVariableAuthorization authorization)
    {
        var roomIds = authorization.Lineage.Select(x => x.RoomId).Append(authorization.RequestRoomId).Distinct().Order().ToArray();

        foreach (var roomId in roomIds) {
            var owner = DatabaseWiredVariableDirectory.ParseOwner(connection.QuerySingleOrDefault<string>("SELECT owner FROM rooms WHERE id=@roomId FOR UPDATE", new { roomId }, transaction));

            if (owner != authorization.OwnerId) {
                return false;
            }
        }

        foreach (var expected in authorization.Lineage.OrderBy(x => x.ItemId)) {
            var placedRoom = connection.QuerySingleOrDefault<uint?>("SELECT room_id FROM items WHERE id=@id FOR UPDATE", new { id = expected.ItemId }, transaction);

            if (placedRoom != expected.RoomId) {
                return false;
            }

            var row = connection.QuerySingleOrDefault<ConfigurationRow>("SELECT box_name AS BoxName,configuration AS Configuration FROM wired_item_configurations WHERE item_id=@id FOR UPDATE", new { id = expected.ItemId }, transaction);

            if (row is null) {
                return false;
            }

            WiredConfiguration? configuration;

            try {
                configuration = JsonSerializer.Deserialize<WiredConfiguration>(row.Configuration, DatabaseWiredVariableDirectory.JsonOptions);
            }
            catch (JsonException) {
                return false;
            }

            if (configuration is null || !WiredVariableDefinitions.TryDecode(row.BoxName, expected.ItemId, expected.RoomId, authorization.OwnerId,
                    configuration, out var current, out _) || current != expected) {
                return false;
            }
        }

        return authorization.Lineage.Length > 0;
    }
    private sealed class ConfigurationRow
    {
        public string BoxName { get; set; } = "";
        public string Configuration { get; set; } = "";
    }

    internal static bool LockDefinition(IDbConnection connection, IDbTransaction transaction, uint definitionId)
    {
        connection.Execute("INSERT IGNORE INTO wired_variable_locks (definition_id) VALUES (@definitionId)", new { definitionId }, transaction);

        return connection.ExecuteScalar<bool>("SELECT retired FROM wired_variable_locks WHERE definition_id=@definitionId FOR UPDATE", new { definitionId }, transaction);
    }

    private class Row
    {
        public WiredVariableTarget Target { get; set; }
        public long HolderId { get; set; }
        public long Value { get; set; }
        public DateTimeOffset? CreatedAt { get; set; }
        public DateTimeOffset? UpdatedAt { get; set; }
    }
    private sealed class PageRow : Row
    {
        public string Name { get; set; } = "";
    }
}
