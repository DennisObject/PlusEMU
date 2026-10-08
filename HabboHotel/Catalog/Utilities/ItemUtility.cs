using Plus.HabboHotel.Items;
using Plus.HabboHotel.Users;
using Plus.HabboHotel.Users.Inventory.Furniture;

namespace Plus.HabboHotel.Catalog.Utilities;

public static class ItemUtility
{
    // Only one piece of furniture, without badges, can be wrapped as a gift.
    public static bool CanGiftItem(CatalogOffer offer)
    {
        if (offer.Products is not [{ Type: CatalogProductType.Furni, Definition: { } definition } product]) {
            return false;
        }

        return definition.AllowGift && !offer.IsLimited && product.Amount <= 1 && (offer.CostPoints == 0 || offer.PointsType == ActivityPointType.Duckets) && !definition.IsRare &&
            definition.InteractionType is not (InteractionType.Exchange or InteractionType.Badge or InteractionType.Teleport or InteractionType.Deal or InteractionType.Pet);
    }

    public static bool CanSelectAmount(CatalogOffer offer) =>
        offer is { BulkPurchase: true, IsLimited: false, IsBundle: false } && offer.Product is { Type: CatalogProductType.Furni, Amount: <= 1 } product &&
        product.Definition!.InteractionType is not (InteractionType.Exchange or InteractionType.Badge or InteractionType.Deal);

    public static uint GetSaddleId(int saddle)
    {
        switch (saddle) {
            default:
            case 9:
                return 4221;
            case 10:
                return 4450;
        }
    }

    public static bool IsRare(Item item)
    {
        if (item.UniqueNumber > 0) {
            return true;
        }

        if (item.Definition.IsRare) {
            return true;
        }

        return false;
    }
}
