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

        return ParseOwner(connection.QuerySingleOrDefault<string>("SELECT owner FROM rooms WHERE id=@roomId", new { roomId }));
    }
    public WiredVariableDefinition? Find(uint itemId)
    {
        using var connection = database.Connection();
        var row = connection.QuerySingleOrDefault<DefinitionRow>("""
            SELECT i.room_id AS RoomId,r.owner AS Owner,c.box_name AS BoxName,c.configuration AS Configuration
            FROM items i JOIN rooms r ON r.id=i.room_id JOIN wired_item_configurations c ON c.item_id=i.id WHERE i.id=@itemId
            """, new { itemId });

        if (row is null || ParseOwner(row.Owner) is not { } ownerId) {
            return null;
        }

        var config = WiredConfigurationStore.DecodeRow(itemId, row.BoxName, row.Configuration);

        return config is not null && WiredVariableDefinitions.TryDecode(row.BoxName, itemId, row.RoomId, ownerId, config, out var definition, out _)
            ? definition : null;
    }
    public IReadOnlyList<WiredSharedVariable> ListShared(uint ownerId, uint exceptRoomId)
    {
        using var connection = database.Connection();
        var rows = connection.Query<SharedRow>("""
            SELECT i.id AS ItemId,i.room_id AS RoomId,r.caption AS RoomName,c.box_name AS BoxName,c.configuration AS Configuration
            FROM items i JOIN rooms r ON r.id=i.room_id JOIN wired_item_configurations c ON c.item_id=i.id
            WHERE r.owner=@owner AND i.room_id<>@room AND i.room_id>0 AND c.box_name IN ('wf_var_user','wf_var_room')
            ORDER BY r.caption,i.id LIMIT 500
            """, new { owner = ownerId.ToString(CultureInfo.InvariantCulture), room = exceptRoomId });
        var shared = new List<WiredSharedVariable>();

        foreach (var row in rows) {
            var config = WiredConfigurationStore.DecodeRow(row.ItemId, row.BoxName, row.Configuration);

            if (config is not null && WiredVariableDefinitions.TryDecode(row.BoxName, row.ItemId, row.RoomId, ownerId, config, out var definition, out _)
                && definition!.Availability == WiredVariableAvailability.Shared && definition.Link is null) {
                shared.Add(new(row.RoomId, row.RoomName, definition));
            }
        }

        return shared;
    }
    // Plus stores numeric user IDs in this varchar column. Reject MySQL's loose numeric-prefix coercion.
    internal static uint? ParseOwner(string? owner) => uint.TryParse(owner, NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0 ? id : null;
    private sealed class SharedRow
    {
        public uint ItemId { get; set; }
        public uint RoomId { get; set; }
        public string RoomName { get; set; } = "";
        public string BoxName { get; set; } = "";
        public string Configuration { get; set; } = "";
    }
    private sealed class DefinitionRow
    {
        public uint RoomId { get; set; }
        public string Owner { get; set; } = "";
        public string BoxName { get; set; } = "";
        public string Configuration { get; set; } = "";
    }
}
