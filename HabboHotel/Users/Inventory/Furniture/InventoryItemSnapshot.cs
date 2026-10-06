using Plus.HabboHotel.Items;

namespace Plus.HabboHotel.Users.Inventory.Furniture;

public sealed record InventoryItemSnapshot(uint Id, string Type, int SpriteId, FurniCategory Category,
    FurnitureDataSnapshot Data, uint UniqueNumber, uint UniqueSeries, bool Recyclable, bool Tradeable,
    bool Stackable, bool StackableWhenAdded, bool MarketplaceSellable, bool IsWallItem)
{
    public static InventoryItemSnapshot Capture(InventoryItem item)
    {
        var definition = item.Definition;

        return new(item.Id, definition.Type.ToCharCode(), definition.SpriteId, definition.Category,
            FurnitureDataSnapshot.Capture(item.ExtraData), item.UniqueNumber, item.UniqueSeries,
            definition.AllowEcotronRecycle, definition.AllowTrade, item.ShouldStackInInventory(),
            item.UniqueNumber == 0 && definition.AllowInventoryStack, definition.AllowMarketplaceSell, item.IsWallItem);
    }
}
