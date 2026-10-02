using System.Data;
using System.Text.Json;
using System.Text.Json.Nodes;
using Dapper;
using Plus.Database;
using Plus.HabboHotel.Items.Wired.Configuration;

namespace Plus.HabboHotel.Items.Wired.Variables;

internal sealed record WiredVariableDefinitionCommit(WiredVariableDefinition Definition, WiredVariableWrite? ValueWrite);

/// <summary>Atomically persists a definition and its explicit global editor value before runtime publication.</summary>
public sealed class WiredVariableConfigurationPersistence(IDatabase database, WiredVariableModule variables, Func<long> nowMs)
{
    public void Persist(WiredVariableDefinitionBox box, WiredConfiguration validated)
    {
        var roomId = box.Instance.Id;
        var ownerId = box.Instance.OwnerId > 0 ? (uint)box.Instance.OwnerId : 0;
        if (roomId != variables.RoomId) throw new InvalidOperationException("Invalid variable room.");
        if (!WiredVariableDefinitions.TryDecode(box.Descriptor.CanonicalName, box.Item.Id,
            roomId, ownerId, validated, out var definition, out var error)) throw new InvalidOperationException(error);
        var expected = box.HasPersistedConfiguration ? box.Configuration : null;
        variables.PersistDefinitionConfiguration(box.Item.Id, active => Save(box.Item.Id, box.Descriptor.CanonicalName,
            roomId, ownerId, expected, validated, definition!, active));
    }

    private WiredVariableDefinitionCommit Save(uint itemId, string name, uint roomId, uint ownerId, WiredConfiguration? expected,
        WiredConfiguration proposed, WiredVariableDefinition definition, WiredVariableValue? active)
    {
        using var connection = database.Connection(); connection.Open();
        using var transaction = connection.BeginTransaction(IsolationLevel.ReadCommitted);
        // Same order as ordinary value writers: rooms, item/configuration, definition guard, value.
        var owner = DatabaseWiredVariableDirectory.ParseOwner(connection.QuerySingleOrDefault<string>(
            "SELECT owner FROM rooms WHERE id=@roomId FOR UPDATE", new { roomId }, transaction));
        if (owner != ownerId) throw new InvalidOperationException("The variable room owner changed before saving.");
        var placement = connection.QuerySingleOrDefault<uint?>("SELECT room_id FROM items WHERE id=@itemId FOR UPDATE", new { itemId }, transaction);
        if (placement != roomId) throw new InvalidOperationException("The variable item left its room before saving.");
        var saved = connection.QuerySingleOrDefault<ConfigurationRow>(
            "SELECT box_name AS Name,configuration AS Configuration FROM wired_item_configurations WHERE item_id=@itemId FOR UPDATE", new { itemId }, transaction);
        if (expected is null ? saved is not null : saved is null || saved.Name != name || !SameConfiguration(saved.Configuration, expected))
            throw new InvalidOperationException("The variable configuration changed before saving.");
        if (DatabaseWiredVariableStore.LockDefinition(connection, transaction, itemId))
            throw new InvalidOperationException("The variable definition was deleted.");

        WiredVariableWrite? write = null;
        if (definition.Target == WiredVariableTarget.Global)
        {
            var before = active;
            if (definition.IsDurable)
            {
                before = connection.QuerySingleOrDefault<WiredVariableValue>("""
                    SELECT value AS Value,created_at_ms AS CreatedAtMs,updated_at_ms AS UpdatedAtMs
                    FROM wired_variable_values WHERE definition_id=@itemId AND target_kind=3 AND holder_id=0 FOR UPDATE
                    """, new { itemId }, transaction);
            }
            var now = nowMs();
            write = new(before, before?.Value == definition.InitialValue ? before : new(definition.InitialValue, before?.CreatedAtMs ?? now, now));
        }
        connection.Execute("""
            INSERT INTO wired_item_configurations (item_id,box_name,schema_version,configuration)
            VALUES (@itemId,@name,@Version,@configuration)
            ON DUPLICATE KEY UPDATE box_name=@name,schema_version=@Version,configuration=@configuration
            """, new { itemId, name, proposed.Version, configuration = JsonSerializer.Serialize(proposed) }, transaction);
        if (definition.IsDurable && write is { Changed: true, After: { } after })
            connection.Execute("""
                INSERT INTO wired_variable_values (definition_id,target_kind,holder_id,value,created_at_ms,updated_at_ms)
                VALUES (@itemId,3,0,@Value,@CreatedAtMs,@UpdatedAtMs)
                ON DUPLICATE KEY UPDATE value=@Value,created_at_ms=@CreatedAtMs,updated_at_ms=@UpdatedAtMs
                """, new { itemId, after.Value, after.CreatedAtMs, after.UpdatedAtMs }, transaction);
        transaction.Commit();
        return new(definition, write);
    }
    private static bool SameConfiguration(string json, WiredConfiguration expected)
    {
        try
        {
            var saved = JsonSerializer.Deserialize<WiredConfiguration>(json, DatabaseWiredVariableDirectory.JsonOptions);
            return saved is not null && JsonNode.DeepEquals(JsonSerializer.SerializeToNode(saved), JsonSerializer.SerializeToNode(expected));
        }
        catch (JsonException) { return false; }
    }
    private sealed class ConfigurationRow { public string Name { get; set; } = ""; public string Configuration { get; set; } = ""; }
}
