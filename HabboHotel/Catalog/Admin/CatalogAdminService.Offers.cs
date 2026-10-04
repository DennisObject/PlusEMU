using System.Runtime.CompilerServices;
using Plus.HabboHotel.Users;

namespace Plus.HabboHotel.Catalog.Admin;

public sealed partial class CatalogAdminService
{
    private const int MaxReorderCount = 500;
    private const int MaxBoundOffers = 256;

    // The editor names offers by the id the catalog page sent (an official offer id is unique per page, not per
    // catalog). Loading or creating an offer binds that id to its row for this login session; saves only use bindings.
    private readonly ConditionalWeakTable<Habbo, Dictionary<int, int>> _offerBindings = new();

    public CatalogAdminOffer LoadOffer(Habbo actor, int offerId)
    {
        RequireEditor(actor);
        using var connection = _database.Connection();
        var store = new CatalogAdminStore(connection);
        var rowId = ResolveListedOffer(store, actor, offerId, useBinding: true);
        var row = store.Offer(rowId) ?? throw NotFound("Offer");
        var offer = CatalogAdminMapping.ToOffer(row, offerId, CatalogType(RequirePage(store, row.PageId, actor)));
        Bind(actor, offerId, row.Id);
        return offer;
    }

    public CatalogAdminOutcome CreateOffer(Habbo actor, CatalogAdminEnvelope envelope, CatalogAdminOffer offer) =>
        Mutate(actor, envelope, "createOffer", OfferEntity, 0, store =>
        {
            Reject(CatalogAdminValidation.Offer(offer, null, actor.Rank, store.Page, store.FurnitureExists));
            var row = CatalogAdminMapping.Apply(offer, null);
            if (offer.OrderNumber < 0)
                row.OrderNum = store.NextOfferOrder(row.PageId);
            row.Id = store.InsertOffer(row);
            var offerId = PageOfferId(store, row);
            Bind(actor, offerId, row.Id);
            var created = CatalogAdminMapping.ToOffer(row, offerId, CatalogType(store.Page(row.PageId)!));
            return new(new(OfferEntity, created.CatalogType, row.Id, "CREATE", null, created), created, "Offer created");
        });

    public CatalogAdminOutcome SaveOffer(Habbo actor, CatalogAdminEnvelope envelope, CatalogAdminOffer offer) =>
        Mutate(actor, envelope, "saveOffer", OfferEntity, offer.OfferId, store =>
        {
            var existing = store.Offer(BoundOffer(actor, offer.OfferId) ?? throw Unbound()) ?? throw NotFound("Offer");
            Reject(CatalogAdminValidation.Offer(offer, existing, actor.Rank, store.Page, store.FurnitureExists));
            var row = CatalogAdminMapping.Apply(offer, existing);
            store.UpdateOffer(row);
            var type = CatalogType(store.Page(row.PageId)!);
            var saved = CatalogAdminMapping.ToOffer(row, offer.OfferId, type);
            return new(new(OfferEntity, type, row.Id, "UPDATE", CatalogAdminMapping.ToOffer(existing, offer.OfferId, type), saved), saved, "Offer saved");
        });

    public CatalogAdminOutcome DeleteOffer(Habbo actor, CatalogAdminEnvelope envelope, int offerId) =>
        Mutate(actor, envelope, "deleteOffer", OfferEntity, offerId, store =>
        {
            var existing = store.Offer(ResolveListedOffer(store, actor, offerId, useBinding: true)) ?? throw NotFound("Offer");
            var type = CatalogType(RequirePage(store, existing.PageId, actor));
            store.DeleteOffer(existing.Id);
            return new(new(OfferEntity, type, existing.Id, "DELETE", CatalogAdminMapping.ToOffer(existing, offerId, type), null), null, "Offer deleted");
        });

    public CatalogAdminOutcome MoveOffer(Habbo actor, CatalogAdminEnvelope envelope, int offerId, int orderNumber) =>
        Mutate(actor, envelope, "moveOffer", OfferEntity, offerId, store =>
        {
            if (orderNumber < 0)
                throw new CatalogAdminRejected(CatalogAdminCodes.ValidationFailed, "Order cannot be negative.");
            var existing = store.Offer(ResolveListedOffer(store, actor, offerId, useBinding: true)) ?? throw NotFound("Offer");
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
            var rows = orders.Select(order => (order.OfferId, order.OrderNumber,
                Row: store.Offer(ResolveListedOffer(store, actor, order.OfferId, useBinding: false)) ?? throw NotFound("Offer"))).ToList();
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

    // Offers picked from the page list: the page being viewed decides first, then this session's binding, then an id
    // that only one page sells. Anything still ambiguous is refused rather than guessed.
    private int ResolveListedOffer(CatalogAdminStore store, Habbo actor, int offerId, bool useBinding)
    {
        var matches = _catalogManager.Pages.Where(page => page.Offers.ContainsKey(offerId))
            .Select(page => (PageId: page.Id, RowId: page.Offers[offerId].Id)).ToList();
        if (_viewedPages.TryGetValue(actor.Id, out var viewed) && matches.Where(match => match.PageId == viewed).ToList() is [var onViewedPage])
            return onViewedPage.RowId;
        if (useBinding && BoundOffer(actor, offerId) is { } bound)
            return bound;
        if (matches is [var only])
            return only.RowId;
        if (matches.Count > 1)
            throw new CatalogAdminRejected(CatalogAdminCodes.Conflict, $"Offer #{offerId} is on several pages; open its page and try again.");
        throw NotFound("Offer");
    }

    private void Bind(Habbo actor, int offerId, int rowId)
    {
        var bindings = _offerBindings.GetOrCreateValue(actor);
        lock (bindings)
        {
            if (bindings.Count >= MaxBoundOffers && !bindings.ContainsKey(offerId))
                bindings.Remove(bindings.Keys.First());
            bindings[offerId] = rowId;
        }
    }

    private int? BoundOffer(Habbo actor, int offerId)
    {
        if (!_offerBindings.TryGetValue(actor, out var bindings))
            return null;
        lock (bindings)
            return bindings.TryGetValue(offerId, out var rowId) ? rowId : null;
    }

    private static CatalogAdminRejected Unbound() =>
        new(CatalogAdminCodes.Conflict, "Reopen this offer before saving; the server cannot tell which offer this form belongs to.");

    // The id the catalog page will show for this row after the reload, by CatalogOfferIndex's rules.
    private static int PageOfferId(CatalogAdminStore store, CatalogOfferRow row)
    {
        var onPage = store.OfferIdsOnPage(row.PageId);
        if (row.OfferId > 0 && onPage.First(offer => offer.OfferId == row.OfferId).Id == row.Id)
            return row.OfferId;
        return onPage.Any(offer => offer.OfferId == row.Id) ? CatalogOfferIndex.ClashingRowIdBase + row.Id : row.Id;
    }

    private static string CatalogType(CatalogPageRow page) => CatalogAdminTypes.FromMode(page.CatalogMode);
}
