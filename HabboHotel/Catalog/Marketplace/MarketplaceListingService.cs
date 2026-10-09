using Plus.Core.Settings;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Users;
using Plus.HabboHotel.Users.Inventory.Furniture;

namespace Plus.HabboHotel.Catalog.Marketplace;

public interface IMarketplaceListingService
{
    // True when the furni was listed and removed from the inventory; false when the offer is not eligible.
    bool TryList(Habbo habbo, uint itemId, int sellingPrice);
}

public sealed class MarketplaceListingService(IMarketplaceOfferStore store, IMarketplaceManager marketplace, TimeProvider time, ISettingsManager settings) : IMarketplaceListingService
{
    private const int MaximumSellingPrice = 70000000;

    public bool TryList(Habbo habbo, uint itemId, int sellingPrice)
    {
        // One listing at a time per account: the inventory check, the committed offer and the in-memory removal stay together.
        lock (habbo.WalletSync) {
            if (habbo.WalletClosed || habbo.Inventory is not { } inventory) {
                return false;
            }

            var item = inventory.Furniture.GetItem(itemId);

            if (item == null || (long)item.OwnerId != habbo.Id || !item.Definition.AllowTrade || !item.Definition.AllowMarketplaceSell) {
                return false;
            }

            if (settings.TryGetValue("catalog.marketplace.only_rare_ltd") == "1" &&
                !item.Definition.IsRare && item.UniqueNumber == 0) {
                return false;
            }

            if (sellingPrice < 1 || sellingPrice > MaximumSellingPrice) {
                return false;
            }

            var comission = marketplace.CalculateComissionPrice(sellingPrice);
            var totalPrice = (long)sellingPrice + comission;

            if (totalPrice > int.MaxValue) {
                return false;
            }

            var itemType = item.Definition.Type == ItemType.Wall ? "2" : "1";
            var listedAt = time.GetUtcNow();

            if (!store.ListFurni(new MarketplaceListing(itemId, item.Definition.Id, habbo.Id, sellingPrice, (int)totalPrice, item.Definition.PublicName,
                item.Definition.SpriteId, itemType, listedAt, item.ExtraData.Serialize(), item.UniqueNumber, item.UniqueSeries))) {
                return false;
            }

            inventory.Furniture.RemoveItem(itemId);

            return true;
        }
    }
}
