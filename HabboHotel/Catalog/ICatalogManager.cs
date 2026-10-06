using System.Diagnostics.CodeAnalysis;
using Plus.HabboHotel.Users;
using Plus.HabboHotel.Catalog.Clothing;
using Plus.HabboHotel.Catalog.Marketplace;
using Plus.HabboHotel.Catalog.Pets;
using Plus.HabboHotel.Catalog.Vouchers;

namespace Plus.HabboHotel.Catalog;

public interface ICatalogManager
{
    Task Init();
    bool TryGetBot(uint itemId, [NotNullWhen(true)] out CatalogBot? bot);
    bool TryGetPage(int pageId, [NotNullWhen(true)] out CatalogPage? page);
    bool TryGetDeal(int dealId, [NotNullWhen(true)] out CatalogDeal? deal);
    // First page the user can open that sells this official offer id.
    bool TryGetOffer(int offerId, Habbo habbo, [NotNullWhen(true)] out CatalogPage? page, [NotNullWhen(true)] out CatalogItem? item);
    ICollection<CatalogPage> Pages
    {
        get;
    }
    ICollection<CatalogPromotion> Promotions
    {
        get;
    }
    ICollection<ClubOffer> ClubOffers
    {
        get;
    }
    bool TryGetClubOffer(int offerId, [NotNullWhen(true)] out ClubOffer? offer);

    [Obsolete("Use dependency injection instead.")]
    IMarketplaceManager Marketplace
    {
        get;
    }

    [Obsolete("Use dependency injection instead.")]
    IPetRaceManager PetRaceManager
    {
        get;
    }

    [Obsolete("Use dependency injection instead.")]
    IVoucherManager VoucherManager
    {
        get;
    }

    [Obsolete("Use dependency injection instead.")]
    IClothingManager ClothingManager
    {
        get;
    }
}
