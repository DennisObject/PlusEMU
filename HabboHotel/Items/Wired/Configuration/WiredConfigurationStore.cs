using System.Data;
using System.Text.Json;
using Dapper;
using Plus.Database;

namespace Plus.HabboHotel.Items.Wired.Configuration;

/// <summary>One atomic native-record row per configured box.</summary>
public sealed class WiredConfigurationStore(IDatabase database) : IWiredConfigurationStore
{
    public WiredConfiguration? Load(uint itemId, WiredBoxDescriptor descriptor)
    {
        using var connection = database.Connection();
        var row = connection.QuerySingleOrDefault<StoredConfiguration>(
            "SELECT box_name AS BoxName, schema_version AS Version, configuration AS Json "
            + "FROM wired_item_configurations WHERE item_id=@Id", new { Id = itemId });

        if (row == null) {
            return null;
        }

        // Unreleased server: only the current native record is readable; anything else is rejected and left to reset.
        if (!string.Equals(row.BoxName, descriptor.CanonicalName, StringComparison.Ordinal) || row.Version != 2) {
            throw new InvalidDataException($"Unsupported Wired configuration for item {itemId}.");
        }

        var native = JsonSerializer.Deserialize<WiredNativeEditorConfiguration>(row.Json);

        if (native == null || !WiredNativeEditorProjection.TryCompile(itemId, descriptor, native, out var runtime)) {
            throw new InvalidDataException($"Invalid native Wired configuration for item {itemId}.");
        }

        return runtime;
    }

    /// <summary>A stored native row as its compiled runtime, for readers that inspect rows outside the room that owns them.</summary>
    internal static WiredConfiguration? DecodeRow(uint itemId, string boxName, string json)
    {
        try {
            return WiredBoxRegistry.TryGet(boxName, out var descriptor) && JsonSerializer.Deserialize<WiredNativeEditorConfiguration>(json) is { } native
                && WiredNativeEditorProjection.TryCompile(itemId, descriptor, native, out var runtime) ? runtime : null;
        }
        catch (JsonException) {
            return null;
        }
    }

    public void Save(uint itemId, WiredBoxDescriptor descriptor, WiredConfiguration configuration)
    {
        if (itemId == 0 || !WiredNativeEditorProjection.IsBound(itemId, descriptor, configuration)) {
            throw new ArgumentException("Unbound or altered Wired runtime projection.", nameof(configuration));
        }

        var native = configuration.Origin!.Native;
        using var connection = database.Connection();
        connection.Execute("INSERT INTO wired_item_configurations (item_id, box_name, schema_version, configuration) "
            + "VALUES (@Id, @Name, @Version, @Configuration) ON DUPLICATE KEY UPDATE "
            + "box_name=@Name, schema_version=@Version, configuration=@Configuration", new
            {
                Id = itemId,
                Name = descriptor.CanonicalName,
                Version = native.Version,
                Configuration = JsonSerializer.Serialize(native)
            });
    }

    public void Reset(IReadOnlyCollection<uint> itemIds)
    {
        if (itemIds.Count == 0) {
            return;
        }

        var ids = new { Ids = itemIds.Distinct().Order().ToArray() };
        using var connection = database.Connection();
        connection.Open();
        using var transaction = connection.BeginTransaction(IsolationLevel.ReadCommitted);
        // Same order as the variable value writers: items/configuration, definition guard, values. Every value writer
        // holds the item row, so a box without a guard row cannot gain values before this commits. The guard stays
        // unretired: the same box placed again defines its variable afresh.
        connection.Execute("SELECT id FROM items WHERE id IN @Ids ORDER BY id FOR UPDATE", ids, transaction);
        connection.Execute("DELETE FROM wired_item_configurations WHERE item_id IN @Ids", ids, transaction);
        connection.Execute("SELECT definition_id FROM wired_variable_locks WHERE definition_id IN @Ids ORDER BY definition_id FOR UPDATE", ids, transaction);
        connection.Execute("DELETE FROM wired_variable_values WHERE definition_id IN @Ids", ids, transaction);
        connection.Execute("DELETE FROM wired_reward_state WHERE item_id IN @Ids", ids, transaction);
        transaction.Commit();
    }

    private sealed class StoredConfiguration
    {
        public string BoxName { get; set; } = string.Empty;
        public int Version { get; set; }
        public string Json { get; set; } = string.Empty;
    }
}
