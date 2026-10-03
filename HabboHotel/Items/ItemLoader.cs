using System.Data;
using Plus.HabboHotel.Items.DataFormat;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Users.Inventory.Furniture;

namespace Plus.HabboHotel.Items;

public static class ItemLoader
{

    public static List<Item> GetItemsForRoom(uint roomId, Room room)
    {
        var items = new List<Item>();
        using var dbClient = PlusEnvironment.DatabaseManager.GetQueryReactor();
        dbClient.SetQuery(
            "SELECT `items`.*, COALESCE(`items_groups`.`group_id`, 0) AS `group_id`, `users`.`username` AS `username` FROM `items` LEFT OUTER JOIN `items_groups` ON `items`.`id` = `items_groups`.`id` LEFT OUTER JOIN `users` ON `users`.`id` = `items`.`user_id` WHERE `items`.`room_id` = @rid;");
        dbClient.AddParameter("rid", roomId);
        var table = dbClient.GetTable();
        if (table != null)
        {
            foreach (DataRow row in table.Rows)
            {
                if (PlusEnvironment.Game.ItemManager.Items.TryGetValue(Convert.ToUInt32(row["base_item"]), out var data))
                    items.Add(ReadRoomItem(row, roomId, data));
            }
        }
        return items;
    }

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
        DataTable items = null;
        var I = new List<InventoryItem>();
        using var dbClient = PlusEnvironment.DatabaseManager.GetQueryReactor();
        dbClient.SetQuery(
            "SELECT `items`.*, COALESCE(`items_groups`.`group_id`, 0) AS `group_id` FROM `items` LEFT OUTER JOIN `items_groups` ON `items`.`id` = `items_groups`.`id` WHERE `items`.`room_id` = 0 AND `items`.`user_id` = @uid;");
        dbClient.AddParameter("uid", userId);
        items = dbClient.GetTable();
        if (items != null)
        {
            foreach (DataRow row in items.Rows)
            {
                if (PlusEnvironment.Game.ItemManager.Items.TryGetValue(Convert.ToUInt32(row["base_item"]), out var data))
                {
                    I.Add(new()
                    {
                        Id = Convert.ToUInt32(row["id"]),
                        OwnerId = userId,
                        Definition = data,
                        ExtraData = FurniExtraData.Load(data, Convert.ToString(row["extra_data"]) ?? "", keepLegacy: false),
                        UniqueNumber = Convert.ToUInt32(row["limited_number"]),
                        UniqueSeries = Convert.ToUInt32(row["limited_stack"])
                    });
                }
            }
        }
        return I;
    }

    public static void DeleteAllInventoryItemsForUser(int userId)
    {
        using var dbClient = PlusEnvironment.DatabaseManager.GetQueryReactor();
        dbClient.RunQuery($"DELETE FROM items WHERE room_id='0' AND user_id = {userId}"); //Do join
    }
}