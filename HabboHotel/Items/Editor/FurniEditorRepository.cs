using System.Data;
using Dapper;

namespace Plus.HabboHotel.Items.Editor;

// SQL for the furni editor. Sort columns and updated columns come from fixed maps, values are always parameters.
internal sealed class FurniEditorRepository
{
    public const string FloorSection = "roomitemtypes";
    public const string WallSection = "wallitemtypes";
    public const int PageSize = 20;
    public const int MaxPage = 10_000;
    private const int MaxCatalogRefs = 100;

    private const string ItemColumns = "id AS Id, sprite_id AS SpriteId, item_name AS ItemName, public_name AS PublicName, type AS Type, " +
        "width AS Width, length AS Length, stack_height AS StackHeight, can_stack = '1' AS AllowStack, is_walkable = '1' AS AllowWalk, " +
        "can_sit = '1' AS AllowSit, interaction_type AS InteractionType, interaction_modes_count AS InteractionModesCount, " +
        "allow_gift = '1' AS AllowGift, allow_trade = '1' AS AllowTrade, allow_recycle = '1' AS AllowRecycle, " +
        "allow_marketplace_sell = '1' AS AllowMarketplaceSell, allow_inventory_stack = '1' AS AllowInventoryStack, " +
        "vending_ids AS VendingIds, effect_id AS EffectId, height_adjustable AS Multiheight";

    private static readonly Dictionary<string, string> SortColumns = new()
    {
        ["id"] = "id",
        ["spriteId"] = "sprite_id",
        ["itemName"] = "item_name",
        ["publicName"] = "public_name",
        ["type"] = "type",
        ["interactionType"] = "interaction_type"
    };

    private static readonly HashSet<string> Types = ["s", "i", "e", "h", "v", "r", "b", "p"];

    private readonly IDbConnection _connection;
    private readonly IDbTransaction? _transaction;

    public FurniEditorRepository(IDbConnection connection, IDbTransaction? transaction = null)
    {
        _connection = connection;
        _transaction = transaction;
    }

    public FurniEditorSearchResult Search(string query, string type, int page, string sortField, string sortDirection)
    {
        query = query.Trim();
        if (query.Length > 100)
            query = query[..100];
        page = Math.Clamp(page, 1, MaxPage);
        var parameters = new DynamicParameters();
        var where = new List<string>();
        if (query.Length > 0)
        {
            parameters.Add("like", "%" + query.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%");
            var match = "item_name LIKE @like OR public_name LIKE @like";
            if (int.TryParse(query, out var number))
            {
                parameters.Add("number", number);
                match += " OR id = @number OR sprite_id = @number";
            }
            where.Add($"({match})");
        }
        if (Types.Contains(type))
        {
            parameters.Add("type", type);
            where.Add("type = @type");
        }
        var filter = where.Count == 0 ? string.Empty : " WHERE " + string.Join(" AND ", where);
        var order = SortColumns.GetValueOrDefault(sortField, "id") + (sortDirection == "desc" ? " DESC" : " ASC");
        parameters.Add("limit", PageSize);
        parameters.Add("offset", (page - 1) * PageSize);
        int total = _connection.QuerySingle<int>($"SELECT COUNT(*) FROM furniture{filter}", parameters, _transaction);
        var items = _connection.Query<FurniEditorItem>($"SELECT {ItemColumns} FROM furniture{filter} ORDER BY {order}, id LIMIT @limit OFFSET @offset",
            parameters, _transaction).ToList();
        return new(items, total, page);
    }

    public FurniEditorItem? Item(uint id, bool forUpdate = false) =>
        _connection.QuerySingleOrDefault<FurniEditorItem>($"SELECT {ItemColumns} FROM furniture WHERE id = @id{(forUpdate ? " FOR UPDATE" : "")}", new { id }, _transaction);

    public uint? ItemBySprite(int spriteId) =>
        _connection.QueryFirstOrDefault<uint?>("SELECT id FROM furniture WHERE sprite_id = @spriteId ORDER BY id LIMIT 1", new { spriteId }, _transaction);

    public int UsageCount(uint id) => _connection.QuerySingle<int>("SELECT COUNT(*) FROM items WHERE base_item = @id", new { id }, _transaction);

    // catalog_items.item_id is text; comparing with text keeps its index usable.
    // Offers on pages above actorRank are left out, as the catalog hides those pages from the actor.
    public List<FurniEditorCatalogRef> CatalogRefs(uint id, int actorRank) => _connection.Query<FurniEditorCatalogRef>("""
        SELECT ci.id AS Id, ci.catalog_name AS CatalogName, ci.cost_credits AS CostCredits, ci.cost_pixels AS CostPixels,
        ci.cost_diamonds AS CostDiamonds, ci.page_id AS PageId, COALESCE(cp.caption, '') AS PageName
        FROM catalog_items ci LEFT JOIN catalog_pages cp ON cp.id = ci.page_id
        WHERE ci.item_id = @itemId AND (cp.id IS NULL OR cp.min_rank <= @actorRank) ORDER BY ci.id LIMIT @limit
        """, new { itemId = id.ToString(), actorRank, limit = MaxCatalogRefs }, _transaction).ToList();

    // Everything that still needs this definition: placed or owned furni, catalog offers and deals ("id*amount;..."),
    // unopened gifts and open marketplace listings (a listed item only exists as its definition id).
    public List<string> References(uint id)
    {
        var references = new List<string>();
        var parameters = new { id, itemId = id.ToString() };
        void Count(string sql, string label)
        {
            int count = _connection.QuerySingle<int>(sql, parameters, _transaction);
            if (count > 0)
                references.Add($"{count} {label}");
        }
        Count("SELECT COUNT(*) FROM items WHERE base_item = @id", "placed or owned items");
        Count("SELECT COUNT(*) FROM catalog_items WHERE item_id = @itemId", "catalog offers");
        Count("SELECT COUNT(*) FROM user_presents WHERE base_id = @id", "unopened gifts");
        Count("SELECT COUNT(*) FROM catalog_marketplace_offers WHERE item_id = @id AND state = '1'", "open marketplace offers");
        var deals = _connection.Query<(int Id, string Items)>("SELECT id, items FROM catalog_deals WHERE items LIKE CONCAT('%', @itemId, '%')", parameters, _transaction)
            .Where(deal => deal.Items.Split(';').Any(entry => entry.Split('*')[0].Trim() == parameters.itemId))
            .Select(deal => $"#{deal.Id}").ToList();
        if (deals.Count > 0)
            references.Add($"catalog deals {string.Join(", ", deals)}");
        return references;
    }

    public List<string> InteractionTypes() =>
        _connection.Query<string>("SELECT DISTINCT interaction_type FROM furniture WHERE interaction_type <> ''", transaction: _transaction).ToList();

    public void Update(uint id, IReadOnlyList<FurniEditorColumnChange> changes)
    {
        var parameters = new DynamicParameters();
        parameters.Add("id", id);
        var assignments = changes.Select((change, index) =>
        {
            parameters.Add($"v{index}", change.Value);
            return $"`{change.Column}` = @v{index}";
        });
        _connection.Execute($"UPDATE furniture SET {string.Join(", ", assignments)} WHERE id = @id", parameters, _transaction);
    }

    public void SetPublicName(uint id, string publicName) =>
        _connection.Execute("UPDATE furniture SET public_name = @publicName WHERE id = @id", new { id, publicName }, _transaction);

    public void Delete(uint id) => _connection.Execute("DELETE FROM furniture WHERE id = @id", new { id }, _transaction);

    // Furnidata rows also name the entry (id and section), since several entries can share a classname.
    public void Log(int userId, string username, string action, uint itemId, string classname, string? before, string? after,
        int? entryId = null, string? entrySection = null) =>
        _connection.Execute("""
            INSERT INTO furni_editor_log (user_id, username, action, item_id, classname, entry_id, entry_section, before_json, after_json)
            VALUES (@userId, @username, @action, @itemId, @classname, @entryId, @entrySection, @before, @after)
            """, new { userId, username, action, itemId, classname, entryId, entrySection, before, after }, _transaction);

    // The newest furnidata edit of this item that has not been reverted yet.
    public FurnidataLogRow? LastFurnidataEdit(uint itemId) =>
        _connection.QueryFirstOrDefault<FurnidataLogRow>("""
            SELECT id AS Id, classname AS Classname, entry_id AS EntryId, entry_section AS EntrySection, before_json AS BeforeJson, after_json AS AfterJson FROM furni_editor_log
            WHERE item_id = @itemId AND action = 'furnidata_update' AND reverted = 0 ORDER BY id DESC LIMIT 1
            """, new { itemId }, _transaction);

    public void MarkReverted(int logId) => _connection.Execute("UPDATE furni_editor_log SET reverted = 1 WHERE id = @logId", new { logId }, _transaction);
}

internal sealed class FurnidataLogRow
{
    public int Id { get; set; }
    public string Classname { get; set; } = string.Empty;
    public int EntryId { get; set; }
    public string EntrySection { get; set; } = string.Empty;
    public string BeforeJson { get; set; } = string.Empty;
    public string AfterJson { get; set; } = string.Empty;
}
