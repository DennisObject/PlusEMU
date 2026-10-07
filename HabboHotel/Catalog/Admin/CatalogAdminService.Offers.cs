using System.Runtime.CompilerServices;
using Plus.HabboHotel.Users;

namespace Plus.HabboHotel.Catalog.Admin;

public sealed partial class CatalogAdminService
{
    private const int MaxReorderCount = 500;
    private const int MaxBoundOffers = 256;

    // An offer can be on several pages, and the editor edits it as placed on one of them. Loading or creating an
    // offer binds its id to that page for this login session; saves only use bindings.
    private readonly ConditionalWeakTable<Habbo, Dictionary<int, int>> _offerBindings = new();

    public CatalogAdminOffer LoadOffer(Habbo actor, int offerId)
    {
        RequireEditor(actor);
        using var connection = _database.Connection();
        var store = new CatalogAdminStore(connection);
        var pageId = ResolveListedOffer(store, actor, offerId, useBinding: true);
        var row = store.Offer(offerId, pageId) ?? throw NotFound("Offer");
        var offer = CatalogAdminMapping.ToOffer(row, offerId, CatalogType(RequirePage(store, row.PageId, actor)));
        Bind(actor, offerId, row.PageId);

        return offer;
    }

    public CatalogAdminOutcome CreateOffer(Habbo actor, CatalogAdminEnvelope envelope, CatalogAdminOffer offer) =>
        Mutate(actor, envelope, "createOffer", OfferEntity, 0, store =>
        {
            Reject(CatalogAdminValidation.Offer(offer, null, actor.Access, store.Page, store.FurnitureExists));

            if (offer.OfferIdClient > 0 && store.OfferExists(offer.OfferIdClient)) {
                throw OfferIdTaken(offer.OfferIdClient);
            }

            var row = CatalogAdminMapping.Apply(offer, null);
            row.Id = Math.Max(offer.OfferIdClient, 0);

            if (offer.OrderNumber < 0) {
                row.OrderNum = store.NextOfferOrder(row.PageId);
            }

            store.InsertOffer(row);
            Bind(actor, row.Id, row.PageId);
            var created = CatalogAdminMapping.ToOffer(row, row.Id, CatalogType(store.Page(row.PageId)!));

            return new(new(OfferEntity, created.CatalogType, row.Id, "CREATE", null, created), created, "Offer created");
        });

    public CatalogAdminOutcome SaveOffer(Habbo actor, CatalogAdminEnvelope envelope, CatalogAdminOffer offer) =>
        Mutate(actor, envelope, "saveOffer", OfferEntity, offer.OfferId, store =>
        {
            var existing = store.Offer(offer.OfferId, BoundOffer(actor, offer.OfferId) ?? throw Unbound()) ?? throw NotFound("Offer");
            Reject(CatalogAdminValidation.Offer(offer, existing, actor.Access, store.Page, store.FurnitureExists));
            var row = Save(store, actor, offer, existing);
            var type = CatalogType(store.Page(row.PageId)!);
            var saved = CatalogAdminMapping.ToOffer(row, row.Id, type);

            return new(new(OfferEntity, type, row.Id, "UPDATE", CatalogAdminMapping.ToOffer(existing, existing.Id, type), saved), saved, "Offer saved");
        });

    public CatalogAdminOutcome DeleteOffer(Habbo actor, CatalogAdminEnvelope envelope, int offerId) =>
        Mutate(actor, envelope, "deleteOffer", OfferEntity, offerId, store =>
        {
            var existing = store.Offer(offerId, ResolveListedOffer(store, actor, offerId, useBinding: true)) ?? throw NotFound("Offer");
            var type = CatalogType(RequirePage(store, existing.PageId, actor));
            store.DeleteOffer(offerId, existing.PageId);

            return new(new(OfferEntity, type, offerId, "DELETE", CatalogAdminMapping.ToOffer(existing, offerId, type), null), null, "Offer deleted");
        });

    public CatalogAdminOutcome MoveOffer(Habbo actor, CatalogAdminEnvelope envelope, int offerId, int orderNumber) =>
        Mutate(actor, envelope, "moveOffer", OfferEntity, offerId, store =>
        {
            if (orderNumber < 0) {
                throw new CatalogAdminRejected(CatalogAdminCodes.ValidationFailed, "Order cannot be negative.");
            }

            var existing = store.Offer(offerId, ResolveListedOffer(store, actor, offerId, useBinding: true)) ?? throw NotFound("Offer");
            var type = CatalogType(RequirePage(store, existing.PageId, actor));
            store.SetOfferOrder(offerId, existing.PageId, orderNumber);
            var moved = existing.Copy();
            moved.OrderNum = orderNumber;

            return new(new(OfferEntity, type, offerId, "MOVE", CatalogAdminMapping.ToOffer(existing, offerId, type),
                CatalogAdminMapping.ToOffer(moved, offerId, type)), null, "Offer moved");
        });

    public CatalogAdminOutcome ReorderOffers(Habbo actor, CatalogAdminEnvelope envelope, IReadOnlyList<(int OfferId, int OrderNumber)> orders) =>
        Mutate(actor, envelope, "reorder", PageEntity, 0, store =>
        {
            if (orders.Count is 0 or > MaxReorderCount || orders.Select(order => order.OfferId).Distinct().Count() != orders.Count || orders.Any(order => order.OrderNumber < 0)) {
                throw new CatalogAdminRejected(CatalogAdminCodes.ValidationFailed, $"Send 1 to {MaxReorderCount} distinct offers with non-negative order numbers.");
            }

            var rows = orders.Select(order => (order.OfferId, order.OrderNumber,
                Row: store.Offer(order.OfferId, ResolveListedOffer(store, actor, order.OfferId, useBinding: false)) ?? throw NotFound("Offer"))).ToList();
            int pageId = rows[0].Row.PageId;

            if (rows.Any(entry => entry.Row.PageId != pageId)) {
                throw new CatalogAdminRejected(CatalogAdminCodes.ValidationFailed, "Reorder offers one page at a time.");
            }

            var page = RequirePage(store, pageId, actor);

            foreach (var entry in rows.Where(entry => entry.Row.OrderNum != entry.OrderNumber)) {
                store.SetOfferOrder(entry.OfferId, pageId, entry.OrderNumber);
            }

            var before = rows.Select(entry => new { id = entry.OfferId, orderNumber = entry.Row.OrderNum });
            var after = rows.Select(entry => new { id = entry.OfferId, orderNumber = entry.OrderNumber });

            return new(new(PageEntity, CatalogType(page), pageId, "REORDER", before, after), null, "Offers reordered");
        });

    // Writes an edited offer. Setting an official id renumbers the offer; clearing it gives the offer an id of this hotel's own.
    private CatalogOfferRow Save(CatalogAdminStore store, Habbo actor, CatalogAdminOffer offer, CatalogOfferRow existing)
    {
        var row = CatalogAdminMapping.Apply(offer, existing);
        row.Id = offer.OfferIdClient > 0 ? offer.OfferIdClient : existing.OfficialOfferId > 0 ? store.NextCustomOfferId() : existing.Id;

        if (row.Id != existing.Id && store.OfferExists(row.Id)) {
            throw OfferIdTaken(row.Id);
        }

        if (row.PageId != existing.PageId && store.OfferPages(existing.Id).Contains(row.PageId)) {
            throw new CatalogAdminRejected(CatalogAdminCodes.ValidationFailed, "This offer is already on that page.",
                new Dictionary<string, string> { ["pageId"] = "This offer is already on that page." });
        }

        store.UpdateOffer(row, existing.Id, existing.PageId);
        Bind(actor, row.Id, row.PageId);

        return row;
    }

    // The page an offer picked from the page list is on: the page being viewed decides first, then this session's
    // binding, then the only page showing it. Anything still ambiguous is refused rather than guessed.
    private int ResolveListedOffer(CatalogAdminStore store, Habbo actor, int offerId, bool useBinding)
    {
        var pages = store.OfferPages(offerId);

        if (_viewedPages.TryGetValue(actor.Id, out var viewed) && pages.Contains(viewed)) {
            return viewed;
        }

        if (useBinding && BoundOffer(actor, offerId) is { } bound && pages.Contains(bound)) {
            return bound;
        }

        if (pages is [var only]) {
            return only;
        }

        if (pages.Count > 1) {
            throw new CatalogAdminRejected(CatalogAdminCodes.Conflict, $"Offer #{offerId} is on several pages; open its page and try again.");
        }

        throw NotFound("Offer");
    }

    private void Bind(Habbo actor, int offerId, int pageId)
    {
        var bindings = _offerBindings.GetOrCreateValue(actor);

        lock (bindings) {
            if (bindings.Count >= MaxBoundOffers && !bindings.ContainsKey(offerId)) {
                bindings.Remove(bindings.Keys.First());
            }

            bindings[offerId] = pageId;
        }
    }

    private int? BoundOffer(Habbo actor, int offerId)
    {
        if (!_offerBindings.TryGetValue(actor, out var bindings)) {
            return null;
        }

        lock (bindings) {
            return bindings.TryGetValue(offerId, out var pageId) ? pageId : null;
        }
    }

    private static CatalogAdminRejected Unbound() =>
        new(CatalogAdminCodes.Conflict, "Reopen this offer before saving; the server cannot tell which page this form belongs to.");

    private static CatalogAdminRejected OfferIdTaken(int offerId) =>
        new(CatalogAdminCodes.ValidationFailed, $"Offer #{offerId} already exists.",
            new Dictionary<string, string> { ["offerIdGroup"] = $"Offer #{offerId} already exists." });

    private static string CatalogType(CatalogPageRow page) => CatalogAdminTypes.Normal;
}
