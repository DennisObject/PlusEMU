using System.Diagnostics.CodeAnalysis;
using Plus.HabboHotel.Users;

namespace Plus.HabboHotel.Catalog;

// Remembers which pages show each offer; an offer can be on several pages.
public class CatalogOfferIndex
{
    // Offers created without an official Habbo offer id are numbered from here.
    public const int CustomOfferIdBase = 1_000_000_000;

    private readonly Dictionary<int, List<CatalogPage>> _pagesByOffer = new();

    // pages must be in catalog order.
    public void Build(IEnumerable<CatalogPage> pages)
    {
        _pagesByOffer.Clear();

        foreach (var page in pages) {
            foreach (var offerId in page.Offers.Keys) {
                if (!_pagesByOffer.TryGetValue(offerId, out var offerPages)) {
                    _pagesByOffer[offerId] = offerPages = new();
                }

                offerPages.Add(page);
            }
        }
    }

    // First page the user can open that sells this offer.
    public bool TryGet(int offerId, Habbo habbo, [NotNullWhen(true)] out CatalogPage? page, [NotNullWhen(true)] out CatalogOffer? offer)
    {
        foreach (var candidate in _pagesByOffer.GetValueOrDefault(offerId) ?? []) {
            if (candidate.CanOpen(habbo) && candidate.Offers.TryGetValue(offerId, out offer!) && offer.CanPurchase(habbo)) {
                page = candidate;

                return true;
            }
        }

        page = null!;
        offer = null!;

        return false;
    }
}
