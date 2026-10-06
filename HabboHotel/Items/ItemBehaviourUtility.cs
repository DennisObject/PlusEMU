using Plus.HabboHotel.Users;
using Plus.HabboHotel.Users.Inventory.Furniture;

namespace Plus.HabboHotel.Items;

internal static class ItemBehaviourUtility
{
    public static bool ShouldStackInInventory(this InventoryItem item)
    {
        if (item.IsLimited())
        {
            return false;
        }

        return item.Definition.AllowInventoryStack;
    }

    public static bool IsLimited(this InventoryItem item) => item.UniqueSeries > 0;

    /// <summary>
    /// The habbo whose inventory holds the item owns it (items.user_id); InventoryItem.OwnerId can be stale after a
    /// trade or zero after a pickup.
    /// </summary>
    public static Item ToRoomObject(this InventoryItem item, Habbo owner) => new()
    {
        Id = item.Id,
        OwnerId = (uint)owner.Id,
        UserId = owner.Id,
        Username = owner.Username,
        Definition = item.Definition,
        ExtraData = item.ExtraData,
        UniqueNumber = item.UniqueNumber,
        UniqueSeries = item.UniqueSeries,
    };

    public static InventoryItem ToInventoryItem(this Item item) => new()
    {
        Id = item.Id,
        OwnerId = item.OwnerId,
        Definition = item.Definition,
        ExtraData = item.ExtraData,
        UniqueNumber = item.UniqueNumber,
        UniqueSeries = item.UniqueSeries
    };

}
