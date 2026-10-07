using Dapper;
using Plus.Database;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Recycler;

namespace Plus.HabboHotel.Users.Inventory.Furniture;

public interface IFurnitureInventoryLoader
{
    Task<IReadOnlyList<InventoryItem>> Load(int userId);
}

public sealed class FurnitureInventoryLoader(IDatabase database, IItemDataManager definitions) : IFurnitureInventoryLoader
{
    public async Task<IReadOnlyList<InventoryItem>> Load(int userId)
    {
        using var connection = database.Connection();
        var rows = await connection.QueryAsync<InventoryItemRow>(
            "SELECT items.id,items.base_item AS BaseItem,items.user_id AS UserId,items.extra_data AS ExtraData," +
            "items.limited_number AS LimitedNumber,items.limited_stack AS LimitedStack,COALESCE(items_groups.group_id,0) AS GroupId " +
            "FROM items LEFT JOIN items_groups ON items.id=items_groups.id WHERE items.room_id=0 AND items.user_id=@userId " +
            "AND NOT EXISTS (SELECT 1 FROM room_music_playlist link WHERE link.disc_id=items.id)",
            new { userId });
        var items = new List<InventoryItem>();

        foreach (var row in rows) {
            if (!definitions.Items.TryGetValue(row.BaseItem, out var definition)) {
                continue;
            }

            items.Add(new()
            {
                Id = row.Id,
                OwnerId = (uint)userId,
                Definition = definition,
                ExtraData = FurniExtraData.Load(definition, row.ExtraData, keepLegacy: RecyclerBox.IsDefinition(definition) || Plus.HabboHotel.Rooms.Music.RoomMusicDefinition.IsDisc(definition)),
                UniqueNumber = row.LimitedNumber,
                UniqueSeries = row.LimitedStack
            });
        }

        return items;
    }

    private sealed class InventoryItemRow
    {
        public uint Id { get; init; }
        public uint BaseItem { get; init; }
        public int UserId { get; init; }
        public string ExtraData { get; init; } = "";
        public uint LimitedNumber { get; init; }
        public uint LimitedStack { get; init; }
        public int GroupId { get; init; }
    }
}
