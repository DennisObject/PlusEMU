namespace Plus.HabboHotel.Catalog;

public sealed record CatalogPurchaseConfirmation(int OfferId, string Name, int Credits, int Points, string ProductType, int ClassId)
{
    public static CatalogPurchaseConfirmation Capture(CatalogOffer offer) => Capture(offer, offer.Product);

    // The client reads the offer it bought; a gift confirms the wrapped product.
    public static CatalogPurchaseConfirmation Capture(CatalogOffer offer, CatalogProduct product) =>
        new(offer.Id, product.Type == CatalogProductType.Badge ? product.BadgeCode : product.Definition?.ItemName ?? offer.LocalizationKey,
            offer.CostCredits, offer.CostPixels, product.WireType, product.ClassId);
}
