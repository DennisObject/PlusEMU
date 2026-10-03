using Plus.HabboHotel.Users;

namespace Plus.HabboHotel.Catalog.Admin;

public sealed partial class CatalogAdminService
{
    private const int MaxReorderCount = 500;

    public CatalogAdminOffer LoadOffer(Habbo actor, int offerId)
    {
        RequireEditor(actor);
        using var connection = _database.Connection();
        var store = new CatalogAdminStore(connection);
        var row = store.Offer(ResolveOffer(store, actor, offerId)) ?? throw NotFound("Offer");
        return CatalogAdminMapping.ToOffer(row, offerId, CatalogType(RequirePage(store, row.PageId, actor)));
    }

    public CatalogAdminOutcome CreateOffer(Habbo actor, CatalogAdminEnvelope envelope, CatalogAdminOffer offer) =>
        Mutate(actor, envelope, "createOffer", OfferEntity, 0, store =>
        {
            Reject(CatalogAdminValidation.Offer(offer, null, actor.Rank, store.Page, ItemExists));
            var row = CatalogAdminMapping.Apply(offer, null);
            if (offer.OrderNumber < 0)
                row.OrderNum = store.NextOfferOrder(row.PageId);
            row.Id = store.InsertOffer(row);
            // A new offer is known by its row id until the catalog reload gives it its page offer id.
            var created = CatalogAdminMapping.ToOffer(row, row.Id, CatalogType(store.Page(row.PageId)!));
            return new(new(OfferEntity, created.CatalogType, row.Id, "CREATE", null, created), created, "Offer created");
        });

    public CatalogAdminOutcome SaveOffer(Habbo actor, CatalogAdminEnvelope envelope, CatalogAdminOffer offer) =>
        Mutate(actor, envelope, "saveOffer", OfferEntity, offer.OfferId, store =>
        {
            var existing = store.Offer(ResolveOffer(store, actor, offer.OfferId, offer.PageId)) ?? throw NotFound("Offer");
            Reject(CatalogAdminValidation.Offer(offer, existing, actor.Rank, store.Page, ItemExists));
            var row = CatalogAdminMapping.Apply(offer, existing);
            store.UpdateOffer(row);
            var type = CatalogType(store.Page(row.PageId)!);
            var saved = CatalogAdminMapping.ToOffer(row, offer.OfferId, type);
            return new(new(OfferEntity, type, row.Id, "UPDATE", CatalogAdminMapping.ToOffer(existing, offer.OfferId, type), saved), saved, "Offer saved");
        });

    public CatalogAdminOutcome DeleteOffer(Habbo actor, CatalogAdminEnvelope envelope, int offerId) =>
        Mutate(actor, envelope, "deleteOffer", OfferEntity, offerId, store =>
        {
            var existing = store.Offer(ResolveOffer(store, actor, offerId)) ?? throw NotFound("Offer");
            var type = CatalogType(RequirePage(store, existing.PageId, actor));
            store.DeleteOffer(existing.Id);
            return new(new(OfferEntity, type, existing.Id, "DELETE", CatalogAdminMapping.ToOffer(existing, offerId, type), null), null, "Offer deleted");
        });

    public CatalogAdminOutcome MoveOffer(Habbo actor, CatalogAdminEnvelope envelope, int offerId, int orderNumber) =>
        Mutate(actor, envelope, "moveOffer", OfferEntity, offerId, store =>
        {
            if (orderNumber < 0)
                throw new CatalogAdminRejected(CatalogAdminCodes.ValidationFailed, "Order cannot be negative.");
            var existing = store.Offer(ResolveOffer(store, actor, offerId)) ?? throw NotFound("Offer");
            var type = CatalogType(RequirePage(store, existing.PageId, actor));
            store.SetOfferOrder(existing.Id, orderNumber);
            var moved = existing.Copy();
            moved.OrderNum = orderNumber;
            return new(new(OfferEntity, type, existing.Id, "MOVE", CatalogAdminMapping.ToOffer(existing, offerId, type),
                CatalogAdminMapping.ToOffer(moved, offerId, type)), null, "Offer moved");
        });

    public CatalogAdminOutcome ReorderOffers(Habbo actor, CatalogAdminEnvelope envelope, IReadOnlyList<(int OfferId, int OrderNumber)> orders) =>
        Mutate(actor, envelope, "reorder", PageEntity, 0, store =>
        {
            if (orders.Count is 0 or > MaxReorderCount || orders.Select(order => order.OfferId).Distinct().Count() != orders.Count || orders.Any(order => order.OrderNumber < 0))
                throw new CatalogAdminRejected(CatalogAdminCodes.ValidationFailed, $"Send 1 to {MaxReorderCount} distinct offers with non-negative order numbers.");
            var rows = orders.Select(order => (order.OfferId, order.OrderNumber, Row: store.Offer(ResolveOffer(store, actor, order.OfferId)) ?? throw NotFound("Offer"))).ToList();
            int pageId = rows[0].Row.PageId;
            if (rows.Any(entry => entry.Row.PageId != pageId))
                throw new CatalogAdminRejected(CatalogAdminCodes.ValidationFailed, "Reorder offers one page at a time.");
            var page = RequirePage(store, pageId, actor);
            foreach (var entry in rows.Where(entry => entry.Row.OrderNum != entry.OrderNumber))
                store.SetOfferOrder(entry.Row.Id, entry.OrderNumber);
            var before = rows.Select(entry => new { id = entry.OfferId, orderNumber = entry.Row.OrderNum });
            var after = rows.Select(entry => new { id = entry.OfferId, orderNumber = entry.OrderNumber });
            return new(new(PageEntity, CatalogType(page), pageId, "REORDER", before, after), null, "Offers reordered");
        });

    // The editor names offers by the id the catalog page sent: an official offer id (unique per page, not
    // per catalog), a row id, or a moved-aside row id. Pages the editor is looking at decide between pages.
    private int ResolveOffer(CatalogAdminStore store, Habbo actor, int offerId, int pageHint = 0)
    {
        var matches = _catalogManager.Pages.Where(page => page.Offers.ContainsKey(offerId))
            .Select(page => (PageId: page.Id, RowId: page.Offers[offerId].Id)).ToList();
        foreach (var hint in new[] { _viewedPages.GetValueOrDefault(actor.Id), pageHint })
        {
            var onPage = matches.Where(match => match.PageId == hint).ToList();
            if (hint > 0 && onPage.Count == 1)
                return onPage[0].RowId;
        }
        if (matches.Count == 1)
            return matches[0].RowId;
        if (matches.Count > 1)
            throw new CatalogAdminRejected(CatalogAdminCodes.ValidationFailed, $"Offer #{offerId} is on several pages; open its page and try again.");
        // Not in the loaded catalog yet (just created): the editor holds the row id.
        return store.Offer(offerId)?.Id ?? throw NotFound("Offer");
    }

    private bool ItemExists(uint itemId) => _itemDataManager.Items.ContainsKey(itemId);

    private static string CatalogType(CatalogPageRow page) => CatalogAdminTypes.FromMode(page.CatalogMode);
}
