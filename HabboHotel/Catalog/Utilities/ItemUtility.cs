using Plus.HabboHotel.Items;
using Plus.HabboHotel.Users.Inventory.Furniture;

namespace Plus.HabboHotel.Catalog.Utilities;

public static class ItemUtility
{
    public static bool CanGiftItem(CatalogItem item)
    {
        if (item.HabbiconId > 0)
        {
            return false;
        }

        if (!item.Definition.AllowGift || item.IsLimited || item.Amount > 1 || item.Definition.InteractionType == InteractionType.Exchange ||
            item.Definition.InteractionType == InteractionType.Badge || item.Definition.Type != ItemType.Floor && item.Definition.Type != ItemType.Wall || item.CostDiamonds > 0 ||
            item.Definition.InteractionType == InteractionType.Teleport || item.Definition.InteractionType == InteractionType.Deal)
        {
            return false;
        }

        if (item.Definition.IsRare)
        {
            return false;
        }

        if (item.Definition.InteractionType == InteractionType.Pet)
        {
            return false;
        }

        return true;
    }

    public static bool CanSelectAmount(CatalogItem item)
    {
        if (item.HabbiconId > 0)
        {
            return false;
        }

        if (item.IsLimited || item.Amount > 1 || item.Definition.InteractionType == InteractionType.Exchange || !item.HaveOffer || item.Definition.InteractionType == InteractionType.Badge ||
            item.Definition.InteractionType == InteractionType.Deal)
        {
            return false;
        }

        return true;
    }

    public static uint GetSaddleId(int saddle)
    {
        switch (saddle)
        {
            default:
            case 9:
                return 4221;
            case 10:
                return 4450;
        }
    }

    public static bool IsRare(Item item)
    {
        if (item.UniqueNumber > 0)
        {
            return true;
        }

        if (item.Definition.IsRare)
        {
            return true;
        }

        return false;
    }
}
