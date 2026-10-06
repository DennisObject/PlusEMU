using Plus.HabboHotel.Items;

namespace Plus.HabboHotel.Catalog;

public sealed record CatalogPurchaseConfirmation(uint Id, string Name, int Credits, int Points, string ProductType, int SpriteId)
{
    public static CatalogPurchaseConfirmation Capture(CatalogItem item, ItemDefinition definition) =>
        new(definition.Id, definition.ItemName, item.CostCredits, item.CostPixels, definition.ProductType, definition.SpriteId);
}
