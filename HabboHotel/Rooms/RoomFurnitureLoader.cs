using Dapper;
using Plus.Database;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.DataFormat;
using Plus.Utilities.DependencyInjection;

namespace Plus.HabboHotel.Rooms;

[Singleton]
public interface IRoomFurnitureLoader
{
    IReadOnlyList<Item> Load(uint roomId);
}

public sealed class RoomFurnitureLoader(IDatabase database, IItemDataManager definitions) : IRoomFurnitureLoader
{
    public IReadOnlyList<Item> Load(uint roomId)
    {
        using var connection = database.Connection();
        return connection.Query<ItemRow>("""
            SELECT items.id, items.user_id AS UserId, items.base_item AS BaseItem, users.username,
                   COALESCE(items_groups.group_id, 0) AS GroupId,
                   items.extra_data AS ExtraData, items.x, items.y, items.z, items.rot AS Rotation,
                   items.limited_number AS LimitedNumber, items.limited_stack AS LimitedStack,
                   items.wall_pos AS WallPosition
            FROM items
            LEFT JOIN items_groups ON items_groups.id = items.id
            LEFT JOIN users ON users.id = items.user_id
            WHERE items.room_id = @roomId
            """, new { roomId }).Select(row => Materialize(row, roomId)).Where(item => item != null).Select(item => item!).ToArray();
    }

    private Item? Materialize(ItemRow row, uint roomId)
    {
        if (!definitions.Items.TryGetValue(row.BaseItem, out var definition)) return null;
        var item = new Item
        {
            Id = row.Id, OwnerId = row.UserId, UserId = (int)row.UserId, Username = row.Username ?? "",
            Definition = definition, ExtraData = FurniExtraData.Load(definition, row.ExtraData ?? "", keepLegacy: true),
            GetX = row.X, GetY = row.Y, GetZ = row.Z, Rotation = row.Rotation,
            UniqueNumber = row.LimitedNumber, UniqueSeries = row.LimitedStack,
            WallCoordinates = row.WallPosition ?? "", GroupId = row.GroupId, RoomId = roomId
        };
        MagicTileHeight.Sync(item);
        return item;
    }

    private sealed class ItemRow
    {
        public uint Id { get; set; }
        public uint UserId { get; set; }
        public uint BaseItem { get; set; }
        public int GroupId { get; set; }
        public string? Username { get; set; }
        public string? ExtraData { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public double Z { get; set; }
        public int Rotation { get; set; }
        public uint LimitedNumber { get; set; }
        public uint LimitedStack { get; set; }
        public string? WallPosition { get; set; }
    }
}
