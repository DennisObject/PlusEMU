using System.Data;
using Dapper;

namespace Plus.HabboHotel.Catalog.Admin;

// SQL for the catalog editor. Every write runs inside the caller's transaction together with its audit row.
internal sealed class CatalogAdminStore
{
    private const string PageColumns = "id AS Id, COALESCE(parent_id, -1) AS ParentId, caption AS Caption, COALESCE(link, '') AS PageLink, icon AS IconImage, " +
        "visible AS Visible, enabled AS Enabled, required_permission AS RequiredPermission, required_club_level AS RequiredClubLevel, position AS OrderNum, " +
        "layout AS PageLayout";

    private const string HistorySelect = "SELECT id AS Id, user_id AS UserId, username AS Username, entity_type AS EntityType, catalog_type AS CatalogType, " +
        "entity_id AS EntityId, operation AS Operation, summary AS Summary, created_at AS CreatedAt FROM catalog_admin_log";

    private readonly IDbConnection _connection;
    private readonly IDbTransaction? _transaction;

    public CatalogAdminStore(IDbConnection connection, IDbTransaction? transaction = null)
    {
        _connection = connection;
        _transaction = transaction;
    }

    public int Revision() => _connection.QuerySingle<int>("SELECT CAST(COALESCE(MAX(id), 0) AS SIGNED) FROM catalog_admin_log", transaction: _transaction);

    public DateTime? LastChangeAt() => _connection.QuerySingleOrDefault<DateTime?>("SELECT MAX(created_at) FROM catalog_admin_log", transaction: _transaction);

    public List<CatalogPageRow> Pages() => WithStrings(_connection.Query<CatalogPageRow>($"SELECT {PageColumns} FROM catalog_pages ORDER BY position, id",
        transaction: _transaction).ToList(), null);

    public CatalogPageRow? Page(int id) =>
        WithStrings(_connection.Query<CatalogPageRow>($"SELECT {PageColumns} FROM catalog_pages WHERE id = @id", new { id }, _transaction).ToList(), id).SingleOrDefault();

    // Root pages have parent -1.
    public List<CatalogPageRow> Children(int parentId) =>
        _connection.Query<CatalogPageRow>($"SELECT {PageColumns} FROM catalog_pages WHERE parent_id <=> NULLIF(@parentId, -1) ORDER BY position, id",
            new { parentId }, _transaction).ToList();

    public bool PermissionExists(string key) =>
        _connection.QuerySingle<int>("SELECT COUNT(*) FROM acl_permissions WHERE `key` = @key", new { key }, _transaction) > 0;

    public int? PageWithLink(string link) =>
        _connection.QuerySingleOrDefault<int?>("SELECT id FROM catalog_pages WHERE link = @link", new { link }, _transaction);

    // The offer as placed on a page, with what it sells.
    public CatalogOfferRow? Offer(int offerId, int pageId)
    {
        var row = _connection.QuerySingleOrDefault<CatalogOfferRow>("""
            SELECT o.id AS Id, po.page_id AS PageId, po.position AS OrderNum, o.localization_key AS CatalogName, o.cost_credits AS CostCredits,
            IF(o.points_type = 0, o.cost_points, 0) AS CostPixels, IF(o.points_type = 5, o.cost_points, 0) AS CostDiamonds, o.bulk_purchase AS BulkPurchase,
            o.enabled AS Enabled, o.club_level AS ClubLevel, COALESCE(l.stack, 0) AS LimitedStack, COALESCE(l.sold, 0) AS LimitedSells
            FROM catalog_offers o INNER JOIN catalog_page_offers po ON po.offer_id = o.id AND po.page_id = @pageId
            LEFT JOIN catalog_offer_limited l ON l.offer_id = o.id WHERE o.id = @offerId
            """, new { offerId, pageId }, _transaction);

        if (row == null) {
            return null;
        }

        var products = _connection.Query<(int Position, string Type, uint? FurnitureId, int? HabbiconId, int Amount, string ExtraParam)>(
            "SELECT position, product_type, furniture_id, habbicon_id, amount, extra_param FROM catalog_offer_products WHERE offer_id = @offerId ORDER BY position",
            new { offerId }, _transaction).ToList();
        var sold = products.Where(product => product.Type != "badge").ToList();

        if (sold is [var product]) {
            row.Amount = product.Amount;
            row.Extradata = product.ExtraParam;
            row.HabbiconId = product.HabbiconId ?? 0;

            if (product.FurnitureId is { } furnitureId) {
                row.ItemId = furnitureId.ToString(System.Globalization.CultureInfo.InvariantCulture);
                row.ProductPosition = product.Position;
            }
        }
        else {
            row.Amount = 1;
        }

        return row;
    }

    // Pages an offer is placed on.
    public List<int> OfferPages(int offerId) =>
        _connection.Query<int>("SELECT page_id FROM catalog_page_offers WHERE offer_id = @offerId ORDER BY page_id", new { offerId }, _transaction).ToList();

    public bool OfferExists(int offerId) => _connection.QuerySingle<int>("SELECT COUNT(*) FROM catalog_offers WHERE id = @offerId", new { offerId }, _transaction) > 0;

    // Read under a shared lock: a furniture delete takes the row's write lock, so it cannot slip in before this commits.
    public bool FurnitureExists(uint id) =>
        _connection.QuerySingle<int>("SELECT COUNT(*) FROM furniture WHERE id = @id LOCK IN SHARE MODE", new { id }, _transaction) > 0;

    public int CountChildren(int pageId) => _connection.QuerySingle<int>("SELECT COUNT(*) FROM catalog_pages WHERE parent_id = @pageId", new { pageId }, _transaction);

    public int CountOffers(int pageId) => _connection.QuerySingle<int>("SELECT COUNT(*) FROM catalog_page_offers WHERE page_id = @pageId", new { pageId }, _transaction);

    public int NextOfferOrder(int pageId) =>
        _connection.QuerySingle<int>("SELECT CAST(COALESCE(MAX(position) + 1, 0) AS SIGNED) FROM catalog_page_offers WHERE page_id = @pageId", new { pageId }, _transaction);

    public int NextPageOrder(int parentId) =>
        _connection.QuerySingle<int>("SELECT CAST(COALESCE(MAX(position) + 1, 0) AS SIGNED) FROM catalog_pages WHERE parent_id <=> NULLIF(@parentId, -1)",
            new { parentId }, _transaction);

    // The next id for an offer that has no official Habbo offer id.
    public int NextCustomOfferId() => _connection.QuerySingle<int>(
        "SELECT CAST(GREATEST(COALESCE(MAX(id), 0) + 1, @first) AS SIGNED) FROM catalog_offers WHERE id >= @first",
        new { first = CatalogOfferIndex.CustomOfferIdBase }, _transaction);

    public int InsertPage(CatalogPageRow row)
    {
        int id = _connection.QuerySingle<int>("""
            INSERT INTO catalog_pages (parent_id, caption, link, icon, visible, enabled, required_permission, required_club_level, position, layout)
            VALUES (NULLIF(@ParentId, -1), @Caption, NULLIF(@PageLink, ''), @IconImage, @Visible, @Enabled, @RequiredPermission, @RequiredClubLevel, @OrderNum, @PageLayout);
            SELECT CAST(LAST_INSERT_ID() AS SIGNED);
            """, row, _transaction);
        WriteStrings(id, row);

        return id;
    }

    public void UpdatePage(CatalogPageRow row)
    {
        _connection.Execute("""
            UPDATE catalog_pages SET parent_id = NULLIF(@ParentId, -1), caption = @Caption, link = NULLIF(@PageLink, ''), icon = @IconImage, visible = @Visible,
            enabled = @Enabled, required_permission = @RequiredPermission, required_club_level = @RequiredClubLevel, position = @OrderNum, layout = @PageLayout
            WHERE id = @Id
            """, row, _transaction);
        WriteStrings(row.Id, row);
    }

    public void SetPageOrder(int id, int orderNum) =>
        _connection.Execute("UPDATE catalog_pages SET position = @orderNum WHERE id = @id", new { id, orderNum }, _transaction);

    public void DeletePage(int id) => _connection.Execute("DELETE FROM catalog_pages WHERE id = @id", new { id }, _transaction);

    // A new offer selling one piece of furniture, placed on its page. Id 0 numbers it as this hotel's own offer.
    public int InsertOffer(CatalogOfferRow row)
    {
        row.Id = row.Id > 0 ? row.Id : NextCustomOfferId();
        _connection.Execute("""
            INSERT INTO catalog_offers (id, localization_key, cost_credits, cost_points, points_type, club_level, bulk_purchase, enabled)
            VALUES (@Id, @CatalogName, @CostCredits, GREATEST(@CostPixels, @CostDiamonds), IF(@CostDiamonds > 0, 5, 0), @ClubLevel, @BulkPurchase, @Enabled);
            INSERT INTO catalog_offer_products (offer_id, position, product_type, furniture_id, amount, extra_param)
            VALUES (@Id, 0, 'furni', @ItemId, @Amount, @Extradata);
            INSERT INTO catalog_page_offers (page_id, offer_id, position) VALUES (@PageId, @Id, @OrderNum);
            """, row, _transaction);
        row.ProductPosition = 0;
        WriteLimited(row);

        return row.Id;
    }

    // Saves an offer edited on page fromPageId. A new official id renumbers the offer everywhere it is used.
    public void UpdateOffer(CatalogOfferRow row, int fromId, int fromPageId)
    {
        _connection.Execute("""
            UPDATE catalog_offers SET id = @Id, localization_key = @CatalogName, cost_credits = @CostCredits, cost_points = GREATEST(@CostPixels, @CostDiamonds),
            points_type = IF(@CostDiamonds > 0, 5, 0), club_level = @ClubLevel, bulk_purchase = @BulkPurchase, enabled = @Enabled WHERE id = @fromId
            """, new { row.Id, row.CatalogName, row.CostCredits, row.CostPixels, row.CostDiamonds, row.ClubLevel, row.BulkPurchase, row.Enabled, fromId }, _transaction);
        _connection.Execute("UPDATE catalog_page_offers SET page_id = @PageId, position = @OrderNum WHERE offer_id = @Id AND page_id = @fromPageId",
            new { row.PageId, row.OrderNum, row.Id, fromPageId }, _transaction);

        if (row.ProductPosition >= 0) {
            _connection.Execute("UPDATE catalog_offer_products SET furniture_id = @ItemId, amount = @Amount, extra_param = @Extradata WHERE offer_id = @Id AND position = @ProductPosition",
                row, _transaction);
        }

        WriteLimited(row);
    }

    public void SetOfferOrder(int offerId, int pageId, int orderNum) =>
        _connection.Execute("UPDATE catalog_page_offers SET position = @orderNum WHERE offer_id = @offerId AND page_id = @pageId", new { offerId, pageId, orderNum }, _transaction);

    // Takes the offer off a page; an offer left on no page is deleted with its products.
    public void DeleteOffer(int offerId, int pageId)
    {
        _connection.Execute("DELETE FROM catalog_page_offers WHERE offer_id = @offerId AND page_id = @pageId", new { offerId, pageId }, _transaction);
        _connection.Execute("DELETE FROM catalog_offers WHERE id = @offerId AND NOT EXISTS (SELECT 1 FROM catalog_page_offers WHERE offer_id = @offerId)",
            new { offerId }, _transaction);
    }

    // The sold count is counted by purchases and never written by the editor.
    private void WriteLimited(CatalogOfferRow row)
    {
        if (row.LimitedStack > 0) {
            _connection.Execute("INSERT INTO catalog_offer_limited (offer_id, stack) VALUES (@Id, @LimitedStack) ON DUPLICATE KEY UPDATE stack = @LimitedStack",
                row, _transaction);
        }
        else {
            _connection.Execute("DELETE FROM catalog_offer_limited WHERE offer_id = @Id", row, _transaction);
        }
    }

    private List<CatalogPageRow> WithStrings(List<CatalogPageRow> pages, int? pageId)
    {
        var byId = pages.ToDictionary(page => page.Id);
        var filter = pageId == null ? "" : " WHERE page_id = @pageId";

        foreach (var (id, slot, image) in _connection.Query<(int, int, string)>($"SELECT page_id, slot, image FROM catalog_page_images{filter} ORDER BY page_id, slot", new { pageId }, _transaction)) {
            if (byId.TryGetValue(id, out var page)) {
                SetSlot(page.Images, slot, image);
            }
        }

        foreach (var (id, slot, text) in _connection.Query<(int, int, string)>($"SELECT page_id, slot, text FROM catalog_page_texts{filter} ORDER BY page_id, slot", new { pageId }, _transaction)) {
            if (byId.TryGetValue(id, out var page)) {
                SetSlot(page.Texts, slot, text);
            }
        }

        return pages;
    }

    private static void SetSlot(List<string> values, int slot, string value)
    {
        while (values.Count <= slot) {
            values.Add(string.Empty);
        }

        values[slot] = value;
    }

    private void WriteStrings(int pageId, CatalogPageRow row)
    {
        _connection.Execute("DELETE FROM catalog_page_images WHERE page_id = @pageId", new { pageId }, _transaction);
        _connection.Execute("DELETE FROM catalog_page_texts WHERE page_id = @pageId", new { pageId }, _transaction);
        _connection.Execute("INSERT INTO catalog_page_images (page_id, slot, image) VALUES (@pageId, @slot, @value)",
            row.Images.Select((value, slot) => new { pageId, slot, value }), _transaction);
        _connection.Execute("INSERT INTO catalog_page_texts (page_id, slot, text) VALUES (@pageId, @slot, @value)",
            row.Texts.Select((value, slot) => new { pageId, slot, value }), _transaction);
    }

    public CatalogAdminLogEntry Log(int userId, string username, string action, CatalogAdminChange change, string summary)
    {
        int id = _connection.QuerySingle<int>("""
            INSERT INTO catalog_admin_log (user_id, username, action, entity_type, catalog_type, entity_id, operation, summary, before_json, after_json)
            VALUES (@userId, @username, @action, @EntityType, @CatalogType, @EntityId, @Operation, @summary, @before, @after);
            SELECT CAST(LAST_INSERT_ID() AS SIGNED);
            """, new
        {
            userId,
            username,
            action,
            change.EntityType,
            change.CatalogType,
            change.EntityId,
            change.Operation,
            summary,
            before = change.Before == null ? null : System.Text.Json.JsonSerializer.Serialize(change.Before, CatalogAdminTypes.Json),
            after = change.After == null ? null : System.Text.Json.JsonSerializer.Serialize(change.After, CatalogAdminTypes.Json)
        }, _transaction);

        return _connection.QuerySingle<CatalogAdminLogEntry>($"{HistorySelect} WHERE id = @id", new { id }, _transaction);
    }

    public CatalogAdminUndoRow? UndoRow(int id) => _connection.QuerySingleOrDefault<CatalogAdminUndoRow>(
        "SELECT id AS Id, entity_type AS EntityType, entity_id AS EntityId, operation AS Operation, before_json AS BeforeJson, after_json AS AfterJson FROM catalog_admin_log WHERE id = @id",
        new { id }, _transaction);

    public int HistoryCount() => _connection.QuerySingle<int>("SELECT COUNT(*) FROM catalog_admin_log", transaction: _transaction);

    public List<CatalogAdminLogEntry> History(int offset, int limit) =>
        _connection.Query<CatalogAdminLogEntry>($"{HistorySelect} ORDER BY id DESC LIMIT @limit OFFSET @offset", new { offset, limit }, _transaction).ToList();
}

// What a mutation changed, for the audit row and the editor's history.
public sealed record CatalogAdminChange(string EntityType, string CatalogType, int EntityId, string Operation, object? Before, object? After);

public sealed class CatalogAdminUndoRow
{
    public int Id { get; set; }
    public string EntityType { get; set; } = string.Empty;
    public int EntityId { get; set; }
    public string Operation { get; set; } = string.Empty;
    public string? BeforeJson { get; set; }
    public string? AfterJson { get; set; }
}
