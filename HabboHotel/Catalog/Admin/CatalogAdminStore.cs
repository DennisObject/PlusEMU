using System.Data;
using Dapper;

namespace Plus.HabboHotel.Catalog.Admin;

// SQL for the catalog editor. Every write runs inside the caller's transaction together with its audit row.
internal sealed class CatalogAdminStore
{
    private const string PageColumns = "id AS Id, parent_id AS ParentId, caption AS Caption, page_link AS PageLink, icon_image AS IconImage, " +
        "visible = 1 AS Visible, enabled = 1 AS Enabled, required_permission AS RequiredPermission, required_club_level AS RequiredClubLevel, order_num AS OrderNum, " +
        "page_layout AS PageLayout, page_strings_1 AS PageStrings1, page_strings_2 AS PageStrings2";

    private const string OfferColumns = "id AS Id, page_id AS PageId, item_id AS ItemId, catalog_name AS CatalogName, cost_credits AS CostCredits, " +
        "cost_pixels AS CostPixels, cost_diamonds AS CostDiamonds, amount AS Amount, limited_sells AS LimitedSells, limited_stack AS LimitedStack, " +
        "offer_active = '1' AS OfferActive, extradata AS Extradata, offer_id AS OfferId, club_level AS ClubLevel, order_num AS OrderNum, habbicon_id AS HabbiconId";

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

    public List<CatalogPageRow> Pages() =>
        _connection.Query<CatalogPageRow>($"SELECT {PageColumns} FROM catalog_pages ORDER BY order_num, id", transaction: _transaction).ToList();

    public CatalogPageRow? Page(int id) =>
        _connection.QuerySingleOrDefault<CatalogPageRow>($"SELECT {PageColumns} FROM catalog_pages WHERE id = @id", new
        {
            id
        }, _transaction);

    public List<CatalogPageRow> Children(int parentId) =>
        _connection.Query<CatalogPageRow>($"SELECT {PageColumns} FROM catalog_pages WHERE parent_id = @parentId ORDER BY order_num, id",
            new
            {
                parentId
            }, _transaction).ToList();

    public CatalogOfferRow? Offer(int id) =>
        _connection.QuerySingleOrDefault<CatalogOfferRow>($"SELECT {OfferColumns} FROM catalog_items WHERE id = @id", new
        {
            id
        }, _transaction);

    // Read under a shared lock: a furniture delete takes the row's write lock, so it cannot slip in before this commits.
    public bool FurnitureExists(uint id) =>
        _connection.QuerySingle<int>("SELECT COUNT(*) FROM furniture WHERE id = @id LOCK IN SHARE MODE", new
        {
            id
        }, _transaction) > 0;

    // Offers of a page in catalog order, for working out the id the page will give a new offer.
    public List<(int Id, int OfferId)> OfferIdsOnPage(int pageId) =>
        _connection.Query<(int, int)>("SELECT id, offer_id FROM catalog_items WHERE page_id = @pageId ORDER BY order_num, id", new
        {
            pageId
        }, _transaction).ToList();

    public int CountChildren(int pageId) => _connection.QuerySingle<int>("SELECT COUNT(*) FROM catalog_pages WHERE parent_id = @pageId", new { pageId }, _transaction);

    public int CountOffers(int pageId) => _connection.QuerySingle<int>("SELECT COUNT(*) FROM catalog_items WHERE page_id = @pageId", new { pageId }, _transaction);

    public int NextOfferOrder(int pageId) =>
        _connection.QuerySingle<int>("SELECT CAST(COALESCE(MAX(order_num) + 1, 0) AS SIGNED) FROM catalog_items WHERE page_id = @pageId", new
        {
            pageId
        }, _transaction);

    public int NextPageOrder(int parentId) =>
        _connection.QuerySingle<int>("SELECT CAST(COALESCE(MAX(order_num) + 1, 0) AS SIGNED) FROM catalog_pages WHERE parent_id = @parentId",
            new
            {
                parentId
            }, _transaction);

    public int InsertPage(CatalogPageRow row) => _connection.QuerySingle<int>("""
        INSERT INTO catalog_pages (parent_id, caption, page_link, icon_image, visible, enabled, required_permission, required_club_level, order_num, page_layout, page_strings_1, page_strings_2)
        VALUES (@ParentId, @Caption, @PageLink, @IconImage, @Visible, @Enabled, @RequiredPermission, @RequiredClubLevel, @OrderNum, @PageLayout, @PageStrings1, @PageStrings2);
        SELECT CAST(LAST_INSERT_ID() AS SIGNED);
        """, row, _transaction);

    public void UpdatePage(CatalogPageRow row) => _connection.Execute("""
        UPDATE catalog_pages SET parent_id = @ParentId, caption = @Caption, page_link = @PageLink, icon_image = @IconImage, visible = @Visible,
        enabled = @Enabled, required_permission = @RequiredPermission, required_club_level = @RequiredClubLevel, order_num = @OrderNum, page_layout = @PageLayout,
        page_strings_1 = @PageStrings1, page_strings_2 = @PageStrings2 WHERE id = @Id
        """, row, _transaction);

    public void SetPageOrder(int id, int orderNum) =>
        _connection.Execute("UPDATE catalog_pages SET order_num = @orderNum WHERE id = @id", new
        {
            id,
            orderNum
        }, _transaction);

    public void DeletePage(int id) => _connection.Execute("DELETE FROM catalog_pages WHERE id = @id", new { id }, _transaction);

    public int InsertOffer(CatalogOfferRow row) => _connection.QuerySingle<int>("""
        INSERT INTO catalog_items (page_id, item_id, catalog_name, cost_credits, cost_pixels, cost_diamonds, amount, limited_sells, limited_stack, offer_active, extradata, offer_id, club_level, order_num)
        VALUES (@PageId, @ItemId, @CatalogName, @CostCredits, @CostPixels, @CostDiamonds, @Amount, 0, @LimitedStack, IF(@OfferActive, '1', '0'), @Extradata, @OfferId, @ClubLevel, @OrderNum);
        SELECT CAST(LAST_INSERT_ID() AS SIGNED);
        """, row, _transaction);

    // limited_sells is counted by purchases and never written by the editor.
    public void UpdateOffer(CatalogOfferRow row) => _connection.Execute("""
        UPDATE catalog_items SET page_id = @PageId, item_id = @ItemId, catalog_name = @CatalogName, cost_credits = @CostCredits, cost_pixels = @CostPixels,
        cost_diamonds = @CostDiamonds, amount = @Amount, limited_stack = @LimitedStack, offer_active = IF(@OfferActive, '1', '0'), extradata = @Extradata,
        offer_id = @OfferId, club_level = @ClubLevel, order_num = @OrderNum WHERE id = @Id
        """, row, _transaction);

    public void SetOfferOrder(int id, int orderNum) =>
        _connection.Execute("UPDATE catalog_items SET order_num = @orderNum WHERE id = @id", new
        {
            id,
            orderNum
        }, _transaction);

    public void DeleteOffer(int id) => _connection.Execute("DELETE FROM catalog_items WHERE id = @id", new { id }, _transaction);

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

        return _connection.QuerySingle<CatalogAdminLogEntry>($"{HistorySelect} WHERE id = @id", new
        {
            id
        }, _transaction);
    }

    public CatalogAdminUndoRow? UndoRow(int id) => _connection.QuerySingleOrDefault<CatalogAdminUndoRow>(
        "SELECT id AS Id, entity_type AS EntityType, entity_id AS EntityId, operation AS Operation, before_json AS BeforeJson, after_json AS AfterJson FROM catalog_admin_log WHERE id = @id",
        new
        {
            id
        }, _transaction);

    public int HistoryCount() => _connection.QuerySingle<int>("SELECT COUNT(*) FROM catalog_admin_log", transaction: _transaction);

    public List<CatalogAdminLogEntry> History(int offset, int limit) =>
        _connection.Query<CatalogAdminLogEntry>($"{HistorySelect} ORDER BY id DESC LIMIT @limit OFFSET @offset", new
        {
            offset,
            limit
        }, _transaction).ToList();
}

// What a mutation changed, for the audit row and the editor's history.
public sealed record CatalogAdminChange(string EntityType, string CatalogType, int EntityId, string Operation, object? Before, object? After);

public sealed class CatalogAdminUndoRow
{
    public int Id
    {
        get; set;
    }
    public string EntityType { get; set; } = string.Empty;
    public int EntityId
    {
        get; set;
    }
    public string Operation { get; set; } = string.Empty;
    public string? BeforeJson
    {
        get; set;
    }
    public string? AfterJson
    {
        get; set;
    }
}
