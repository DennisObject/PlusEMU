using System.Text.Json;
using System.Globalization;
using Dapper;
using Plus.Database;
using Plus.HabboHotel.Items.Wired.Configuration;

namespace Plus.HabboHotel.Items.Wired.Variables;

/// <summary>Fresh placement/ownership checks, including source rooms which are not loaded. Pickup makes a source unavailable.</summary>
public sealed class DatabaseWiredVariableDirectory(IDatabase database) : IWiredVariableDirectory
{
    internal static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    public uint? GetRoomOwner(uint roomId)
    {
        using var connection = database.Connection();

        return ParseOwner(connection.QuerySingleOrDefault<string>("SELECT owner FROM rooms WHERE id=@roomId", new
        {
            roomId
        }));
    }
    public WiredVariableDefinition? Find(uint itemId)
    {
        using var connection = database.Connection();
        var row = connection.QuerySingleOrDefault<DefinitionRow>("""
            SELECT i.room_id AS RoomId,r.owner AS Owner,c.box_name AS BoxName,c.configuration AS Configuration
            FROM items i JOIN rooms r ON r.id=i.room_id JOIN wired_item_configurations c ON c.item_id=i.id WHERE i.id=@itemId
            """, new
        {
            itemId
        });

        if (row is null || ParseOwner(row.Owner) is not { } ownerId)
        {
            return null;
        }

        WiredConfiguration? config;

        try
        {
            config = JsonSerializer.Deserialize<WiredConfiguration>(row.Configuration, JsonOptions);
        }
        catch (JsonException) { return null; }

        return config is not null && WiredVariableDefinitions.TryDecode(row.BoxName, itemId, row.RoomId, ownerId, config, out var definition, out _)
            ? definition : null;
    }
    // Plus stores numeric user IDs in this varchar column. Reject MySQL's loose numeric-prefix coercion.
    internal static uint? ParseOwner(string? owner) => uint.TryParse(owner, NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0 ? id : null;
    private sealed class DefinitionRow
    {
        public uint RoomId
        {
            get; set;
        }
        public string Owner { get; set; } = "";
        public string BoxName { get; set; } = "";
        public string Configuration { get; set; } = "";
    }
}
