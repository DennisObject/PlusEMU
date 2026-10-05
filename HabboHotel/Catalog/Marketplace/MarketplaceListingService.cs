using Plus.HabboHotel.Items;
using Plus.HabboHotel.Users;
using Plus.HabboHotel.Users.Inventory.Furniture;

namespace Plus.HabboHotel.Catalog.Marketplace;

public interface IMarketplaceListingService
{
    // True when the furni was listed and removed from the inventory; false when the offer is not eligible.
    bool TryList(Habbo habbo, uint itemId, int sellingPrice);
}

public sealed class MarketplaceListingService(IMarketplaceOfferStore store, IMarketplaceManager marketplace, TimeProvider time) : IMarketplaceListingService
{
    private const int MaximumSellingPrice = 70000000;

    public bool TryList(Habbo habbo, uint itemId, int sellingPrice)
    {
        var item = habbo.Inventory.Furniture.GetItem(itemId);
        if (item == null || sellingPrice > MaximumSellingPrice || sellingPrice == 0)
            return false;
        var comission = marketplace.CalculateComissionPrice(sellingPrice);
        var totalPrice = sellingPrice + comission;
        var itemType = item.Definition.Type == ItemType.Wall ? "2" : "1";
        // Same clock as the legacy UnixTimestamp.GetNow: local time seconds since 1970.
        var timestamp = (time.GetLocalNow().DateTime - new DateTime(1970, 1, 1)).TotalSeconds;
        store.ListFurni(new MarketplaceListing(itemId, item.Definition.Id, habbo.Id, sellingPrice, totalPrice, item.Definition.PublicName,
            item.Definition.SpriteId, itemType, timestamp, item.ExtraData.Serialize(), item.UniqueNumber, item.UniqueSeries));
        habbo.Inventory.Furniture.RemoveItem(itemId);
        return true;
    }
}
