using System.Diagnostics.CodeAnalysis;
using Plus.HabboHotel.Users;

namespace Plus.HabboHotel.Catalog;

// Gives every page offer an id that is unique on its page and remembers which pages sell each official offer id.
public class CatalogOfferIndex
{
    // Wire ids for legacy rows whose own id is taken by an official offer id on the same page.
    public const int ClashingRowIdBase = 1_000_000_000;

    private readonly Dictionary<int, List<CatalogPage>> _pagesByOffer = new();

    // pages must be in catalog order.
    public void Build(IEnumerable<CatalogPage> pages)
    {
        _pagesByOffer.Clear();

        foreach (var page in pages) {
            page.Offers.Clear();
            // The client finds furni by official offer id, so the first row naming an id keeps it; later rows
            // naming the same id and legacy rows fall back to their row id, moved aside if an official id holds it.
            var official = page.Items.Values.Where(item => item.OfferId > 0).Select(item => item.OfferId).ToHashSet();

            foreach (var item in page.Items.Values) {
                if (item.OfferId > 0 && !page.Offers.ContainsKey(item.OfferId)) {
                    item.WireOfferId = item.OfferId;
                }
                else {
                    item.WireOfferId = official.Contains(item.Id) ? ClashingRowIdBase + item.Id : item.Id;
                }

                page.Offers.Add(item.WireOfferId, item);
            }

            foreach (var offerId in official) {
                if (!_pagesByOffer.TryGetValue(offerId, out var offerPages)) {
                    _pagesByOffer[offerId] = offerPages = new();
                }

                offerPages.Add(page);
            }
        }
    }

    // Official offer ids a page can be found by.
    public static IEnumerable<int> OfficialOfferIds(CatalogPage page) =>
        page.Offers.Values.Where(item => item.OfferId > 0 && item.WireOfferId == item.OfferId).Select(item => item.OfferId);

    // First page the user can open that sells this official offer id.
    public bool TryGet(int offerId, Habbo habbo, [NotNullWhen(true)] out CatalogPage? page, [NotNullWhen(true)] out CatalogItem? item)
    {
        foreach (var candidate in _pagesByOffer.GetValueOrDefault(offerId) ?? []) {
            if (candidate.CanOpen(habbo) && candidate.Offers.TryGetValue(offerId, out item!) && item.CanPurchase(habbo)) {
                page = candidate;

                return true;
            }
        }

        page = null!;
        item = null!;

        return false;
    }
}
