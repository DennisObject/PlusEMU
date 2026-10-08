namespace Plus.HabboHotel.Catalog;

public sealed record CatalogPurchaseConfirmation(int OfferId, string Name, int Credits, int Points, int PointsType, string ProductType, int ClassId)
{
    public static CatalogPurchaseConfirmation Capture(CatalogOffer offer) => Capture(offer, offer.Product);

    // The client reads the offer it bought; a gift confirms the wrapped product.
    public static CatalogPurchaseConfirmation Capture(CatalogOffer offer, CatalogProduct product) =>
        new(offer.Id, product.Type == CatalogProductType.Badge ? product.BadgeCode : product.Definition?.ItemName ?? offer.LocalizationKey,
            offer.CostCredits, offer.CostPoints, offer.PointsType, product.WireType, product.ClassId);
}
