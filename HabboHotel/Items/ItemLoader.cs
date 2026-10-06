using System.Data;
using Plus.HabboHotel.Items.DataFormat;
using Plus.HabboHotel.Rooms;

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

}
