using System.Data;
using Dapper;
using Plus.HabboHotel.Items.DataFormat;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Users.Inventory.Furniture;

namespace Plus.HabboHotel.Items;

public static class ItemLoader
{

    internal static Item ReadRoomItem(DataRow row, uint roomId, ItemDefinition definition)
    {
        var item = new Item
        {
        Id = Convert.ToUInt32(row["id"]),
        OwnerId = Convert.ToUInt32(row["user_id"]),
        UserId = Convert.ToInt32(row["user_id"]),
        Username = Convert.ToString(row["username"]) ?? "",
        Definition = definition,
        ExtraData = FurniExtraData.Load(definition, Convert.ToString(row["extra_data"]) ?? "", keepLegacy: true),
        GetX = Convert.ToInt32(row["x"]),
        GetY = Convert.ToInt32(row["y"]),
        GetZ = Convert.ToDouble(row["z"]),
        Rotation = Convert.ToInt32(row["rot"]),
        UniqueNumber = Convert.ToUInt32(row["limited_number"]),
        UniqueSeries = Convert.ToUInt32(row["limited_stack"]),
        WallCoordinates = Convert.ToString(row["wall_pos"]) ?? "",
        RoomId = roomId
        };
        MagicTileHeight.Sync(item);
        return item;
    }

    public static List<InventoryItem> GetItemsForUser(uint userId)
    {
        var items = new List<InventoryItem>();
        using var connection = PlusEnvironment.DatabaseManager.Connection();
        var rows = connection.Query<ItemRow>(
            "SELECT items.id,items.base_item AS BaseItem,items.user_id AS UserId,items.extra_data AS ExtraData,items.limited_number AS LimitedNumber,items.limited_stack AS LimitedStack,COALESCE(items_groups.group_id,0) AS GroupId FROM items LEFT JOIN items_groups ON items.id=items_groups.id WHERE items.room_id=0 AND items.user_id=@userId",
            new { userId });
        foreach (var row in rows)
        {
            if (PlusEnvironment.Game.ItemManager.Items.TryGetValue(row.BaseItem, out var data))
            {
                items.Add(new()
                {
                    Id = row.Id,
                    OwnerId = userId,
                    Definition = data,
                    ExtraData = FurniExtraData.Load(data, row.ExtraData, keepLegacy: false),
                    UniqueNumber = row.LimitedNumber,
                    UniqueSeries = row.LimitedStack
                });
            }
        }
        return items;
    }

    public static void DeleteAllInventoryItemsForUser(int userId)
    {
        using var connection = PlusEnvironment.DatabaseManager.Connection();
        connection.Execute("DELETE FROM items WHERE room_id=0 AND user_id=@userId", new { userId });
    }

    private sealed class ItemRow
    {
        public uint Id { get; init; }
        public uint BaseItem { get; init; }
        public uint UserId { get; init; }
        public string ExtraData { get; init; } = "";
        public int X { get; init; }
        public int Y { get; init; }
        public double Z { get; init; }
        public int Rot { get; init; }
        public uint LimitedNumber { get; init; }
        public uint LimitedStack { get; init; }
        public string WallPos { get; init; } = "";
        public int GroupId { get; init; }
        public string Username { get; init; } = "";
    }
}
