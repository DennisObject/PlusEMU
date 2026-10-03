using System.Text.Json;
using Plus.HabboHotel.Users;

namespace Plus.HabboHotel.Catalog.Admin;

public sealed partial class CatalogAdminService
{
    private static readonly JsonSerializerOptions SnapshotReader = new(CatalogAdminTypes.Json) { PropertyNameCaseInsensitive = true };

    // Restores the logged "before" snapshot onto the current row through the normal validation and audit path.
    // Only the newest edit or move of an entity can be undone; creates, deletes and reorders are refused.
    public CatalogAdminOutcome Undo(Habbo actor, CatalogAdminEnvelope envelope, int groupId) =>
        Mutate(actor, envelope, "undo", PageEntity, 0, store =>
        {
            var entry = store.UndoRow(groupId) ?? throw NotFound("History entry");
            if (entry.Operation is not ("UPDATE" or "MOVE") || entry.BeforeJson == null)
                throw new CatalogAdminRejected(CatalogAdminCodes.Unsupported, "Only edits and moves can be undone; delete or recreate the entity instead.");
            if (store.LatestChange(entry.EntityType, entry.EntityId) != entry.Id)
                throw new CatalogAdminRejected(CatalogAdminCodes.Conflict, "This entity changed again later; undo the newer change first.");
            return entry.EntityType == OfferEntity ? UndoOffer(store, actor, entry) : UndoPage(store, actor, entry);
        });

    private static CatalogAdminMutation UndoPage(CatalogAdminStore store, Habbo actor, CatalogAdminUndoRow entry)
    {
        var existing = RequirePage(store, entry.EntityId, actor);
        var before = Snapshot<CatalogAdminPage>(entry.BeforeJson!) with { PageId = existing.Id };
        Reject(CatalogAdminValidation.Page(before, existing, actor.Rank, store.Page));
        var row = CatalogAdminMapping.Apply(before, existing);
        store.UpdatePage(row);
        return PageChange("UPDATE", existing, row, "Change undone");
    }

    private CatalogAdminMutation UndoOffer(CatalogAdminStore store, Habbo actor, CatalogAdminUndoRow entry)
    {
        var existing = store.Offer(entry.EntityId) ?? throw NotFound("Offer");
        var before = Snapshot<CatalogAdminOffer>(entry.BeforeJson!);
        Reject(CatalogAdminValidation.Offer(before, existing, actor.Rank, store.Page, ItemExists));
        var row = CatalogAdminMapping.Apply(before, existing);
        store.UpdateOffer(row);
        var type = CatalogType(store.Page(row.PageId)!);
        var restored = CatalogAdminMapping.ToOffer(row, before.OfferId, type);
        return new(new(OfferEntity, type, row.Id, "UPDATE", CatalogAdminMapping.ToOffer(existing, before.OfferId, type), restored), restored, "Change undone");
    }

    private static T Snapshot<T>(string json)
    {
        try
        {
            if (JsonSerializer.Deserialize<T>(json, SnapshotReader) is { } snapshot)
                return snapshot;
        }
        catch (JsonException)
        {
        }
        throw new CatalogAdminRejected(CatalogAdminCodes.Unsupported, "The logged change cannot be read.");
    }
}
