using System.Text.Json;
using Plus.HabboHotel.Users;

namespace Plus.HabboHotel.Catalog.Admin;

public sealed partial class CatalogAdminService
{
    private static readonly JsonSerializerOptions SnapshotReader = new(CatalogAdminTypes.Json) { PropertyNameCaseInsensitive = true };

    // Puts back the logged "before" image through the normal validation and audit path. Everything the change touched
    // must still look exactly like its logged "after" image; otherwise something changed since and undo is refused.
    // Creates, deletes and reorders cannot be undone.
    public CatalogAdminOutcome Undo(Habbo actor, CatalogAdminEnvelope envelope, int groupId) =>
        Mutate(actor, envelope, "undo", PageEntity, 0, store =>
        {
            var entry = store.UndoRow(groupId) ?? throw NotFound("History entry");

            if (entry.Operation is not ("UPDATE" or "MOVE") || entry.BeforeJson == null || entry.AfterJson == null) {
                throw new CatalogAdminRejected(CatalogAdminCodes.Unsupported, "Only edits and moves can be undone; delete or recreate the entity instead.");
            }

            if (entry.EntityType == OfferEntity) {
                return UndoOffer(store, actor, entry);
            }

            return entry.Operation == "MOVE" ? UndoPageMove(store, actor, entry) : UndoPage(store, actor, entry);
        });

    private static CatalogAdminMutation UndoPage(CatalogAdminStore store, Habbo actor, CatalogAdminUndoRow entry)
    {
        var existing = RequirePage(store, entry.EntityId, actor);

        if (CatalogAdminMapping.ToPage(existing) != Snapshot<CatalogAdminPage>(entry.AfterJson!)) {
            throw ChangedSince();
        }

        var before = Snapshot<CatalogAdminPage>(entry.BeforeJson!) with { PageId = existing.Id };
        Reject(CatalogAdminValidation.Page(before, existing, actor.Access, store.Page));
        var row = CatalogAdminMapping.Apply(before, existing);
        store.UpdatePage(row);

        return PageChange("UPDATE", existing, row, "Change undone");
    }

    private static CatalogAdminMutation UndoPageMove(CatalogAdminStore store, Habbo actor, CatalogAdminUndoRow entry)
    {
        var before = Snapshot<CatalogAdminMove>(entry.BeforeJson!);
        var after = Snapshot<CatalogAdminMove>(entry.AfterJson!);
        var existing = RequirePage(store, entry.EntityId, actor);

        if (CatalogAdminMapping.ToPage(existing) != after.Page) {
            throw ChangedSince();
        }

        foreach (var sibling in after.Siblings) {
            var current = store.Page(sibling.PageId);

            // A sibling that moved elsewhere since may keep the same order number; its place is parent and order.
            if (current == null || current.ParentId != sibling.ParentId || current.OrderNum != sibling.OrderNum) {
                throw ChangedSince();
            }

            if (!CatalogAdminValidation.Available(current.RequiredPermission, actor.Access)) {
                throw new CatalogAdminRejected(CatalogAdminCodes.Forbidden, "This move reordered pages requiring a permission you do not have.");
            }
        }

        Reject(CatalogAdminValidation.Move(existing.Id, before.Page.ParentId, CatalogAdminTypes.Normal, actor.Access, store.Page));
        var restored = existing.Copy();
        restored.ParentId = before.Page.ParentId;
        restored.OrderNum = before.Page.OrderNum;
        store.UpdatePage(restored);

        foreach (var sibling in before.Siblings) {
            store.SetPageOrder(sibling.PageId, sibling.OrderNum);
        }

        var page = CatalogAdminMapping.ToPage(restored);

        return new(new(PageEntity, page.CatalogType, existing.Id, "MOVE", after, before with { Page = page }), page, "Move undone");
    }

    private static CatalogAdminMutation UndoOffer(CatalogAdminStore store, Habbo actor, CatalogAdminUndoRow entry)
    {
        var existing = store.Offer(entry.EntityId) ?? throw NotFound("Offer");
        var type = CatalogType(RequirePage(store, existing.PageId, actor));
        var after = Snapshot<CatalogAdminOffer>(entry.AfterJson!);

        if (CatalogAdminMapping.ToOffer(existing, after.OfferId, type) with { LimitedSells = 0 } != after) {
            throw ChangedSince();
        }

        var before = Snapshot<CatalogAdminOffer>(entry.BeforeJson!);
        Reject(CatalogAdminValidation.Offer(before, existing, actor.Access, store.Page, store.FurnitureExists));
        var row = CatalogAdminMapping.Apply(before, existing);
        store.UpdateOffer(row);
        type = CatalogType(store.Page(row.PageId)!);
        var restored = CatalogAdminMapping.ToOffer(row, before.OfferId, type);

        return new(new(OfferEntity, type, row.Id, entry.Operation, CatalogAdminMapping.ToOffer(existing, before.OfferId, type), restored), restored, "Change undone");
    }

    private static CatalogAdminRejected ChangedSince() =>
        new(CatalogAdminCodes.Conflict, "This entity changed again later; undo the newer change first.");

    private static T Snapshot<T>(string json)
    {
        try {
            if (JsonSerializer.Deserialize<T>(json, SnapshotReader) is { } snapshot) {
                return snapshot;
            }
        }
        catch (JsonException) {
        }

        throw new CatalogAdminRejected(CatalogAdminCodes.Unsupported, "The logged change cannot be read.");
    }
}
