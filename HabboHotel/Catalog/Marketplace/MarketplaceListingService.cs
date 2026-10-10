using Plus.Core.Settings;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Users;
using Plus.HabboHotel.Users.Inventory.Furniture;

namespace Plus.HabboHotel.Catalog.Marketplace;

public interface IMarketplaceListingService
{
    // True when every furni was listed and removed from the inventory together; false when any of them is not eligible. The price is what the buyer pays: the official client
    // (MakeOfferMessageComposer: price, furniType 1 floor or 2 wall, then the item ids) shows the seller price minus the fee, and the seller is paid that.
    bool TryList(Habbo habbo, IReadOnlyList<uint> itemIds, int furniType, int price);
}

public sealed class MarketplaceListingService(IMarketplaceOfferStore store, IMarketplaceFeePolicy fee, TimeProvider time, ISettingsManager settings) : IMarketplaceListingService
{
    public const int MaximumOfferCount = 500;
    public const int MinimumPrice = 1;
    public const int MaximumPrice = 99999999;

    public bool TryList(Habbo habbo, IReadOnlyList<uint> itemIds, int furniType, int price)
    {
        if (furniType is not (1 or 2) || price is < MinimumPrice or > MaximumPrice || itemIds.Count is < 1 or > MaximumOfferCount || itemIds.Distinct().Count() != itemIds.Count) {
            return false;
        }

        // The inventory lock first, then the wallet lock, as a trade does. One listing at a time per account: the inventory check, the committed offers and the in-memory removal stay together.
        lock (habbo.InventoryMutationSync)
            lock (habbo.WalletSync) {
                if (habbo.WalletClosed || habbo.Inventory is not { } inventory) {
                    return false;
                }

                var onlyRareAndLimited = settings.TryGetValue("catalog.marketplace.only_rare_ltd") == "1";
                var items = new List<InventoryItem>(itemIds.Count);

                foreach (var itemId in itemIds) {
                    var item = inventory.Furniture.GetItem(itemId);

                    if (item == null || (long)item.OwnerId != habbo.Id || !item.Definition.AllowTrade || !item.Definition.AllowMarketplaceSell || IsOffered(habbo, itemId)) {
                        return false;
                    }

                    if (onlyRareAndLimited && !item.Definition.IsRare && item.UniqueNumber == 0) {
                        return false;
                    }

                    items.Add(item);
                }

                if (!IsOneKindOf(furniType, items)) {
                    return false;
                }

                var seller = price - fee.Fee(price);

                if (seller < 0) {
                    return false;
                }

                var listedAt = time.GetUtcNow();

                if (!store.ListFurni(items.Select(item => new MarketplaceListing(item.Id, item.Definition.Id, habbo.Id, seller, price, item.Definition.PublicName,
                    item.Definition.SpriteId, furniType == 2 ? "2" : "1", listedAt, item.ExtraData.Serialize(), item.UniqueNumber, item.UniqueSeries)).ToArray())) {
                    return false;
                }

                foreach (var item in items) {
                    inventory.Furniture.RemoveItem(item.Id);
                }

                return true;
            }
    }

    // An item offered in the seller's open trade stays with the trade.
    private static bool IsOffered(Habbo habbo, uint itemId)
    {
        if (!habbo.InRoom) {
            return false;
        }

        var roomUser = habbo.CurrentRoom.GetRoomUserManager().GetRoomUserByHabbo(habbo.Id);

        if (roomUser == null || !roomUser.IsTrading || !habbo.CurrentRoom.GetTrading().TryGetTrade(roomUser.TradeId, out var trade)) {
            return false;
        }

        return trade.Users.FirstOrDefault(user => user.RoomUser == roomUser)?.OfferedItems.ContainsKey(itemId) == true;
    }

    // One offer lists identical items: the declared type holds for every one of them and they share their definition and sprite.
    private static bool IsOneKindOf(int furniType, IReadOnlyList<InventoryItem> items)
    {
        var type = furniType == 2 ? ItemType.Wall : ItemType.Floor;
        var first = items[0].Definition;

        return items.All(item => item.Definition.Type == type && item.Definition.Id == first.Id && item.Definition.SpriteId == first.SpriteId);
    }
}
