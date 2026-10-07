using System.Data;
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Plus.Database;
using Plus.HabboHotel.Permissions;
using Plus.HabboHotel.Users;

namespace Plus.HabboHotel.Catalog.Admin;

public interface ICatalogAdminService
{
    // Reads throw CatalogAdminRejected when the actor may not see the data.
    CatalogAdminSession OpenSession(Habbo actor);
    CatalogAdminHistory History(Habbo actor, int offset, int limit);
    CatalogAdminPage LoadPage(Habbo actor, int pageId);
    CatalogAdminOffer LoadOffer(Habbo actor, int offerId);

    CatalogAdminOutcome CreatePage(Habbo actor, CatalogAdminEnvelope envelope, CatalogAdminPage page);
    CatalogAdminOutcome SavePage(Habbo actor, CatalogAdminEnvelope envelope, CatalogAdminPage page);
    CatalogAdminOutcome DeletePage(Habbo actor, CatalogAdminEnvelope envelope, int pageId);
    CatalogAdminOutcome MovePage(Habbo actor, CatalogAdminEnvelope envelope, int pageId, int parentId, int index);
    CatalogAdminOutcome SetPageEnabled(Habbo actor, CatalogAdminEnvelope envelope, int pageId, bool enabled);
    CatalogAdminOutcome SetPageVisible(Habbo actor, CatalogAdminEnvelope envelope, int pageId, bool visible);
    CatalogAdminOutcome SavePageImages(Habbo actor, CatalogAdminEnvelope envelope, int pageId, string headerImage, string teaserImage);
    CatalogAdminOutcome SavePageIcon(Habbo actor, CatalogAdminEnvelope envelope, int pageId, int iconId);

    CatalogAdminOutcome CreateOffer(Habbo actor, CatalogAdminEnvelope envelope, CatalogAdminOffer offer);
    CatalogAdminOutcome SaveOffer(Habbo actor, CatalogAdminEnvelope envelope, CatalogAdminOffer offer);
    CatalogAdminOutcome DeleteOffer(Habbo actor, CatalogAdminEnvelope envelope, int offerId);
    CatalogAdminOutcome MoveOffer(Habbo actor, CatalogAdminEnvelope envelope, int offerId, int orderNumber);
    CatalogAdminOutcome ReorderOffers(Habbo actor, CatalogAdminEnvelope envelope, IReadOnlyList<(int OfferId, int OrderNumber)> orders);

    // Puts an edited or moved page or offer back as it was before history group groupId, as a new audited change.
    CatalogAdminOutcome Undo(Habbo actor, CatalogAdminEnvelope envelope, int groupId);

    bool Publish(Habbo actor);

    // The page an editor last opened; offer ids are only unique per page, so it disambiguates them.
    void RecordViewedPage(Habbo habbo, int pageId);
}

public sealed partial class CatalogAdminService : ICatalogAdminService
{
    private const string PageEntity = "PAGE";
    private const string OfferEntity = "OFFER";
    private const int MaxHistoryPage = 100;

    private readonly IDatabase _database;
    private readonly ICatalogCacheRefresher _refresher;
    private readonly ILogger<CatalogAdminService> _logger;
    private readonly ConcurrentDictionary<int, int> _viewedPages = new();
    // One emulator process owns the catalog, so serialising here keeps revision checks and writes atomic.
    private readonly object _sync = new();

    public CatalogAdminService(IDatabase database, ICatalogCacheRefresher refresher, ILogger<CatalogAdminService> logger)
    {
        _database = database;
        _refresher = refresher;
        _logger = logger;
    }

    public CatalogAdminSession OpenSession(Habbo actor)
    {
        RequireEditor(actor);
        using var connection = _database.Connection();
        connection.Open();
        // One snapshot: the revision the editor gets must describe exactly the pages it gets.
        using var transaction = connection.BeginTransaction(IsolationLevel.RepeatableRead);
        var store = new CatalogAdminStore(connection, transaction);
        int revision = store.Revision();
        var updatedAt = store.LastChangeAt();
        BetweenSessionReads?.Invoke();
        var pages = store.Pages().Where(page => CatalogAdminValidation.Available(page.RequiredPermission, actor.Access)).Select(CatalogAdminMapping.ToPage).ToList();
        transaction.Commit();

        return new(revision, updatedAt, pages);
    }

    // Test seam: runs after the session's revision is read and before its pages are.
    internal Action? BetweenSessionReads { get; set; }

    public CatalogAdminHistory History(Habbo actor, int offset, int limit)
    {
        RequireEditor(actor);
        using var connection = _database.Connection();
        var store = new CatalogAdminStore(connection);

        return new(store.Revision(), store.HistoryCount(), store.History(Math.Max(0, offset), Math.Clamp(limit, 1, MaxHistoryPage)));
    }

    public CatalogAdminPage LoadPage(Habbo actor, int pageId)
    {
        RequireEditor(actor);
        using var connection = _database.Connection();

        return CatalogAdminMapping.ToPage(RequirePage(new CatalogAdminStore(connection), pageId, actor));
    }

    public bool Publish(Habbo actor)
    {
        if (actor?.Access?.Can(PermissionKeys.CatalogEdit) != true) {
            return false;
        }

        _logger.LogInformation("Catalog editor: {User} reloaded the catalog", actor.Username);
        _refresher.Schedule();

        return true;
    }

    public void RecordViewedPage(Habbo habbo, int pageId)
    {
        if (habbo?.Access?.Can(PermissionKeys.CatalogEdit) == true) {
            _viewedPages[habbo.Id] = pageId;
        }
    }

    public CatalogAdminOutcome CreatePage(Habbo actor, CatalogAdminEnvelope envelope, CatalogAdminPage page) =>
        Mutate(actor, envelope, "createPage", PageEntity, 0, store =>
        {
            var draft = page with { PageId = 0 };
            Reject(CatalogAdminValidation.Page(draft, null, actor.Access, store.Page));
            var row = CatalogAdminMapping.Apply(draft, null);
            RequireStoredReferences(store, row);

            if (draft.OrderNum < 0) {
                row.OrderNum = store.NextPageOrder(row.ParentId);
            }

            row.Id = store.InsertPage(row);
            var created = CatalogAdminMapping.ToPage(row);

            return new(new(PageEntity, created.CatalogType, row.Id, "CREATE", null, created), created, "Page created");
        });

    public CatalogAdminOutcome SavePage(Habbo actor, CatalogAdminEnvelope envelope, CatalogAdminPage page) =>
        Mutate(actor, envelope, "savePage", PageEntity, page.PageId, store =>
        {
            var existing = store.Page(page.PageId) ?? throw NotFound("Page");
            Reject(CatalogAdminValidation.Page(page, existing, actor.Access, store.Page));
            var row = CatalogAdminMapping.Apply(page, existing);
            RequireStoredReferences(store, row);
            store.UpdatePage(row);

            return PageChange("UPDATE", existing, row, "Page saved");
        });

    public CatalogAdminOutcome DeletePage(Habbo actor, CatalogAdminEnvelope envelope, int pageId) =>
        Mutate(actor, envelope, "deletePage", PageEntity, pageId, store =>
        {
            var existing = RequirePage(store, pageId, actor);

            if (store.CountChildren(pageId) > 0) {
                throw new CatalogAdminRejected(CatalogAdminCodes.ValidationFailed, "Move or delete the child pages first.");
            }

            if (store.CountOffers(pageId) > 0) {
                throw new CatalogAdminRejected(CatalogAdminCodes.ValidationFailed, "Move or delete the offers on this page first.");
            }

            store.DeletePage(pageId);
            var before = CatalogAdminMapping.ToPage(existing);

            return new(new(PageEntity, before.CatalogType, pageId, "DELETE", before, null), null, "Page deleted");
        });

    public CatalogAdminOutcome MovePage(Habbo actor, CatalogAdminEnvelope envelope, int pageId, int parentId, int index) =>
        Mutate(actor, envelope, "movePage", PageEntity, pageId, store =>
        {
            var existing = RequirePage(store, pageId, actor);
            Reject(CatalogAdminValidation.Move(pageId, parentId, CatalogAdminTypes.Normal, actor.Access, store.Page));
            var siblings = store.Children(parentId).Where(page => page.Id != pageId).ToList();
            var moved = existing.Copy();
            moved.ParentId = parentId;
            siblings.Insert(Math.Clamp(index, 0, siblings.Count), moved);
            moved.OrderNum = siblings.IndexOf(moved);
            var renumbered = siblings.Select((page, order) => (Page: page, Order: order))
                .Where(entry => entry.Page.Id != pageId && entry.Page.OrderNum != entry.Order).ToList();

            if (renumbered.Any(entry => !CatalogAdminValidation.Available(entry.Page.RequiredPermission, actor.Access))) {
                throw new CatalogAdminRejected(CatalogAdminCodes.Forbidden, "This move would reorder pages requiring a permission you do not have.");
            }

            store.UpdatePage(moved);

            foreach (var (page, order) in renumbered) {
                store.SetPageOrder(page.Id, order);
            }

            var before = new CatalogAdminMove(CatalogAdminMapping.ToPage(existing),
                renumbered.Select(entry => new CatalogAdminOrder(entry.Page.Id, entry.Page.ParentId, CatalogAdminTypes.Normal, entry.Page.OrderNum)).ToList());
            var after = new CatalogAdminMove(CatalogAdminMapping.ToPage(moved),
                renumbered.Select(entry => new CatalogAdminOrder(entry.Page.Id, entry.Page.ParentId, CatalogAdminTypes.Normal, entry.Order)).ToList());

            return new(new(PageEntity, after.Page.CatalogType, pageId, "MOVE", before, after), after.Page, "Page moved");
        });

    public CatalogAdminOutcome SetPageEnabled(Habbo actor, CatalogAdminEnvelope envelope, int pageId, bool enabled) =>
        EditPage(actor, envelope, "toggleEnabled", pageId, row => row.Enabled = enabled);

    public CatalogAdminOutcome SetPageVisible(Habbo actor, CatalogAdminEnvelope envelope, int pageId, bool visible) =>
        EditPage(actor, envelope, "toggleVisible", pageId, row => row.Visible = visible);

    public CatalogAdminOutcome SavePageImages(Habbo actor, CatalogAdminEnvelope envelope, int pageId, string headerImage, string teaserImage) =>
        Mutate(actor, envelope, "savePageImages", PageEntity, pageId, store =>
        {
            var existing = RequirePage(store, pageId, actor);
            Reject(CatalogAdminValidation.PageImages(headerImage, teaserImage));
            var row = CatalogAdminMapping.WithImages(existing, headerImage, teaserImage);
            store.UpdatePage(row);

            return PageChange("UPDATE", existing, row, "Page images saved");
        });

    public CatalogAdminOutcome SavePageIcon(Habbo actor, CatalogAdminEnvelope envelope, int pageId, int iconId)
    {
        if (iconId is < 0 or > 1_000_000) {
            return Failure(CatalogAdminCodes.ValidationFailed, "Icon must be between 0 and 1000000.", envelope.ExpectedRevision, PageEntity, envelope.CatalogType, pageId);
        }

        return EditPage(actor, envelope, "savePageIcon", pageId, row => row.IconImage = iconId);
    }

    private CatalogAdminOutcome EditPage(Habbo actor, CatalogAdminEnvelope envelope, string action, int pageId, Action<CatalogPageRow> edit) =>
        Mutate(actor, envelope, action, PageEntity, pageId, store =>
        {
            var existing = RequirePage(store, pageId, actor);
            var row = existing.Copy();
            edit(row);
            store.UpdatePage(row);

            return PageChange("UPDATE", existing, row, "Page saved");
        });

    private static CatalogAdminMutation PageChange(string operation, CatalogPageRow before, CatalogPageRow after, string message)
    {
        var page = CatalogAdminMapping.ToPage(after);

        return new(new(PageEntity, page.CatalogType, after.Id, operation, CatalogAdminMapping.ToPage(before), page), page, message);
    }

    private CatalogAdminOutcome Mutate(Habbo actor, CatalogAdminEnvelope envelope, string action, string entityType, int entityId,
        Func<CatalogAdminStore, CatalogAdminMutation> apply)
    {
        if (actor?.Access?.Can(PermissionKeys.CatalogEdit) != true) {
            return Failure(CatalogAdminCodes.Forbidden, "No permission.", envelope.ExpectedRevision, entityType, envelope.CatalogType, entityId);
        }

        if (envelope.OperationId.Length > CatalogAdminEnvelope.MaxOperationIdLength) {
            return Failure(CatalogAdminCodes.ValidationFailed, "Operation id is too long.", envelope.ExpectedRevision, entityType, envelope.CatalogType, entityId);
        }

        lock (_sync) {
            using var connection = _database.Connection();
            connection.Open();
            using var transaction = connection.BeginTransaction();
            var store = new CatalogAdminStore(connection, transaction);
            int revision = store.Revision();

            if (envelope.DraftVersionId != CatalogAdminEnvelope.LiveVersionId || envelope.ExpectedRevision != revision) {
                return Failure(CatalogAdminCodes.StaleRevision, "The catalog changed since you opened it. Reload and try again.", revision, entityType, envelope.CatalogType, entityId);
            }

            CatalogAdminMutation mutation;

            try {
                mutation = apply(store);
            }
            catch (CatalogAdminRejected rejected) {
                return Failure(rejected.Code, rejected.Message, revision, entityType, envelope.CatalogType, entityId, rejected.FieldErrors);
            }

            var history = store.Log(actor.Id, actor.Username, action, mutation.Change, Summary(envelope.Summary, action));
            transaction.Commit();
            _logger.LogInformation("Catalog editor: {User} {Action} {Entity} #{Id} (revision {Revision})",
                actor.Username, action, mutation.Change.EntityType, mutation.Change.EntityId, history.Id);
            _refresher.Schedule();
            var change = mutation.Change;
            // The audit row keeps the row id; the editor checks the acknowledgement against the id it knows the entity by.
            int entityKey = mutation.Entity switch
            {
                CatalogAdminOffer offer => offer.OfferId,
                CatalogAdminPage page => page.PageId,
                _ => change.EntityId
            };

            return new(true, CatalogAdminCodes.Saved, mutation.Message, history.Id, change.EntityType, change.CatalogType, entityKey,
                mutation.Entity, history, new Dictionary<string, string>());
        }
    }

    private static CatalogAdminOutcome Failure(string code, string message, int revision, string entityType, string catalogType, int entityId,
        IReadOnlyDictionary<string, string>? fieldErrors = null) =>
        new(false, code, message, Math.Max(0, revision), entityType, CatalogAdminTypes.Parse(catalogType) ?? CatalogAdminTypes.Normal,
            Math.Max(0, entityId), null, null, fieldErrors ?? new Dictionary<string, string>());

    private static void RequireEditor(Habbo actor)
    {
        if (actor?.Access?.Can(PermissionKeys.CatalogEdit) != true) {
            throw new CatalogAdminRejected(CatalogAdminCodes.Forbidden, "No permission.");
        }
    }

    private static CatalogPageRow RequirePage(CatalogAdminStore store, int pageId, Habbo actor)
    {
        var page = store.Page(pageId) ?? throw NotFound("Page");

        if (!CatalogAdminValidation.Available(page.RequiredPermission, actor.Access)) {
            throw new CatalogAdminRejected(CatalogAdminCodes.Forbidden, "You cannot edit a page requiring a permission you do not have.");
        }

        return page;
    }

    // A page link is unique and a required permission must be a known one.
    private static void RequireStoredReferences(CatalogAdminStore store, CatalogPageRow row)
    {
        if (row.PageLink.Length > 0 && store.PageWithLink(row.PageLink) is { } other && other != row.Id) {
            throw new CatalogAdminRejected(CatalogAdminCodes.ValidationFailed, "Another page already uses this link.",
                new Dictionary<string, string> { ["captionSave"] = "Another page already uses this link." });
        }

        if (row.RequiredPermission != null && !store.PermissionExists(row.RequiredPermission)) {
            throw new CatalogAdminRejected(CatalogAdminCodes.ValidationFailed, "Unknown permission.",
                new Dictionary<string, string> { ["requiredPermission"] = "Unknown permission." });
        }
    }

    private static CatalogAdminRejected NotFound(string what) => new(CatalogAdminCodes.NotFound, $"{what} not found.");

    private static void Reject(Dictionary<string, string> errors)
    {
        if (CatalogAdminValidation.Summary(errors) is { } message) {
            throw new CatalogAdminRejected(CatalogAdminCodes.ValidationFailed, message, errors);
        }
    }

    private static string Summary(string summary, string action)
    {
        var text = string.IsNullOrWhiteSpace(summary) ? action : new string(summary.Where(c => !char.IsControl(c)).ToArray());

        return text.Length > CatalogAdminEnvelope.MaxSummaryLength ? text[..CatalogAdminEnvelope.MaxSummaryLength] : text;
    }

    private sealed record CatalogAdminMutation(CatalogAdminChange Change, object? Entity, string Message);
}
