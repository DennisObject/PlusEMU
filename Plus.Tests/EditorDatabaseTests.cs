using System.Reflection;
using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MySqlConnector;
using Plus.Communication.Flash;
using Plus.Communication.Packets.Incoming.Catalog.Admin;
using Plus.Communication.Packets.Outgoing;
using Plus.HabboHotel.Catalog;
using Plus.HabboHotel.Catalog.Admin;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.Editor;
using Plus.HabboHotel.Users;
using Xunit;

namespace Plus.Tests;

public sealed class EditorDatabaseFactAttribute : FactAttribute
{
    public const string Variable = "PLUS_EDITOR_TEST_CONNECTION_STRING";

    public EditorDatabaseFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(Variable)))
            Skip = $"Set {Variable} to a disposable task_editor_tests_ schema holding the PlusEMU schema with updates 11, 14 and 17.";
    }
}

// Runs the editors against a real, disposable database: every row it creates is tagged "e3test" and removed again.
public sealed class EditorDatabaseTests : IDisposable
{
    private const string Tag = "e3test";
    private readonly HabbiconDatabaseTests.TestDatabase _database;
    private readonly List<CatalogPage> _cache = new();
    private readonly Dictionary<uint, ItemDefinition> _definitions = new();
    private readonly EditorPermissionTests.Recorder _refresher = (EditorPermissionTests.Recorder)(object)DispatchProxy.Create<ICatalogCacheRefresher, EditorPermissionTests.Recorder>();
    private readonly string _directory = Directory.CreateTempSubdirectory("editor-db-tests-").FullName;
    private readonly CatalogAdminService _catalog = null!;

    public EditorDatabaseTests()
    {
        var builder = new MySqlConnectionStringBuilder(Environment.GetEnvironmentVariable(EditorDatabaseFactAttribute.Variable) ?? "Database=none") { AllowUserVariables = true };
        if (!builder.Database.StartsWith("task_editor_tests_", StringComparison.Ordinal))
            throw new InvalidOperationException("Editor database tests require a disposable task_editor_tests_ schema.");
        _database = new(builder.ConnectionString);
        if (Environment.GetEnvironmentVariable(EditorDatabaseFactAttribute.Variable) == null)
            return;
        Execute(File.ReadAllText(HabbiconPacketTests.Repo("Resources/SQLs/Updates/21_CatalogAdminEditor.sql")));
        Cleanup();
        var catalogManager = DispatchProxy.Create<ICatalogManager, CatalogProxy>();
        ((CatalogProxy)(object)catalogManager).Pages = _cache;
        var items = DispatchProxy.Create<IItemDataManager, CatalogProxy>();
        ((CatalogProxy)(object)items).Items = _definitions;
        _catalog = new CatalogAdminService(_database, catalogManager, items, (ICatalogCacheRefresher)(object)_refresher, NullLogger<CatalogAdminService>.Instance);
    }

    public void Dispose()
    {
        if (Environment.GetEnvironmentVariable(EditorDatabaseFactAttribute.Variable) != null)
            Cleanup();
        Directory.Delete(_directory, recursive: true);
    }

    [EditorDatabaseFact]
    public void MigrationIsIdempotentAndGrantsOnlyTheTopRanks()
    {
        Execute(File.ReadAllText(HabbiconPacketTests.Repo("Resources/SQLs/Updates/21_CatalogAdminEditor.sql")));
        using var connection = _database.Connection();
        var granted = connection.Query<(string, int)>("""
            SELECT p.permission, r.group_id FROM permissions_rights r JOIN permissions p ON p.id = r.permission_id
            WHERE p.permission IN ('acc_catalogfurni', 'acc_furnidata_edit', 'acc_furni_delete')
            """).Order().ToList();
        Assert.Equal([("acc_catalogfurni", 8), ("acc_catalogfurni", 9), ("acc_furni_delete", 8), ("acc_furni_delete", 9), ("acc_furnidata_edit", 8), ("acc_furnidata_edit", 9)], granted);
    }

    [EditorDatabaseFact]
    public void CreatePageTravelsFromPacketToDatabaseAndBack()
    {
        var staff = EditorTestSupport.Staff();
        var (client, sent) = HabbiconTestSupport.Client(staff);
        int revision = Revision();
        var packet = EditorTestSupport.Incoming($"{Tag} page", $"{Tag}_page", "default_3x3", 12, 1, true, true, -1, -1, "NORMAL", "NORMAL", 1,
            false, false, "head", "", "", "text", "", "", "", 0, "", 1, revision, "", "Created page", "create-1");

        new CatalogAdminCreatePageEvent(_catalog).Parse(client, packet);

        var (header, payload) = Assert.Single(sent);
        Assert.Equal(ServerPacketHeader.CatalogAdminResultComposer, header);
        var answer = new FlashIncomingPacket { Buffer = payload };
        Assert.True(answer.ReadBool());
        answer.ReadString();
        Assert.Equal((1, "create-1", "createPage", "SAVED", 1, revision + 1, "PAGE", "NORMAL"),
            (answer.ReadInt(), answer.ReadString(), answer.ReadString(), answer.ReadString(), answer.ReadInt(), answer.ReadInt(), answer.ReadString(), answer.ReadString()));
        int pageId = answer.ReadInt();
        Assert.Contains($"\"pageId\":{pageId}", answer.ReadString());
        Assert.Contains("\"actorName\":\"editor\"", answer.ReadString());
        using var connection = _database.Connection();
        Assert.Equal(($"{Tag} page", "head", "text"), connection.QuerySingle<(string, string, string)>(
            "SELECT caption, page_strings_1, page_strings_2 FROM catalog_pages WHERE id = @pageId", new { pageId }));
        var audit = connection.QuerySingle<(int, string, string, string?, string)>(
            "SELECT user_id, action, operation, before_json, after_json FROM catalog_admin_log WHERE id = @id", new { id = revision + 1 });
        Assert.Equal((staff.Id, "createPage", "CREATE", (string?)null), (audit.Item1, audit.Item2, audit.Item3, audit.Item4));
        Assert.Contains($"\"caption\":\"{Tag} page\"", audit.Item5);
        Assert.Equal(["Schedule"], _refresher.Calls);
    }

    [EditorDatabaseFact]
    public void PageEditsCheckRevisionRankAndTreeAndAuditBeforeAndAfter()
    {
        var staff = EditorTestSupport.Staff(rank: 7);
        var parent = CreatePage(staff, "parent", -1);
        var child = CreatePage(staff, "child", parent.PageId);

        var stale = _catalog.SavePage(staff, Envelope(Revision() - 1), child with { Caption = "late" });
        Assert.Equal((false, CatalogAdminCodes.StaleRevision), (stale.Success, stale.Code));

        var invalid = _catalog.SavePage(staff, Envelope(Revision()), child with { Caption = "", MinRank = 9 });
        Assert.Equal((false, CatalogAdminCodes.ValidationFailed), (invalid.Success, invalid.Code));
        Assert.Equal(["caption", "minRank"], invalid.FieldErrors.Keys.Order());

        var cycle = _catalog.MovePage(staff, Envelope(Revision()), parent.PageId, child.PageId, 0);
        Assert.False(cycle.Success);

        int before = Revision();
        var saved = _catalog.SavePage(staff, Envelope(before), child with { Caption = $"{Tag} renamed", PageText2 = "second" });
        Assert.True(saved.Success);
        Assert.Equal(before + 1, saved.Revision);
        using (var connection = _database.Connection())
        {
            var (beforeJson, afterJson) = connection.QuerySingle<(string, string)>("SELECT before_json, after_json FROM catalog_admin_log WHERE id = @id", new { id = saved.Revision });
            Assert.Contains($"\"caption\":\"{Tag} child\"", beforeJson);
            Assert.Contains($"\"caption\":\"{Tag} renamed\"", afterJson);
        }

        Assert.True(_catalog.SetPageVisible(staff, Envelope(Revision()), child.PageId, false).Success);
        Assert.True(_catalog.SavePageIcon(staff, Envelope(Revision()), child.PageId, 77).Success);
        Assert.True(_catalog.SavePageImages(staff, Envelope(Revision()), child.PageId, "header_img", "teaser_img").Success);
        var loaded = _catalog.LoadPage(staff, child.PageId);
        Assert.Equal((false, 77, "header_img", "teaser_img", "second"), (loaded.Visible, loaded.IconImage, loaded.PageHeadline, loaded.PageTeaser, loaded.PageText2));

        Assert.False(_catalog.DeletePage(staff, Envelope(Revision()), parent.PageId).Success);
        Assert.True(_catalog.MovePage(staff, Envelope(Revision()), child.PageId, -1, 0).Success);
        Assert.Equal(-1, _catalog.LoadPage(staff, child.PageId).ParentId);
        Assert.True(_catalog.DeletePage(staff, Envelope(Revision()), parent.PageId).Success);
        Assert.Throws<CatalogAdminRejected>(() => _catalog.LoadPage(staff, parent.PageId));

        var session = _catalog.OpenSession(staff);
        Assert.Equal(Revision(), session.Revision);
        Assert.Contains(session.Pages, page => page.PageId == child.PageId);
        Assert.DoesNotContain(session.Pages, page => page.MinRank > staff.Rank);
        Assert.Equal(Revision(), _catalog.History(staff, 0, 5).Groups[0].Id);
    }

    [EditorDatabaseFact]
    public void UndoRestoresTheNewestEditOfAPageOrOfferAsANewAuditedChange()
    {
        var staff = EditorTestSupport.Staff();
        var page = CreatePage(staff, "undo", -1);
        var created = Revision();
        Assert.Equal(CatalogAdminCodes.Unsupported, _catalog.Undo(staff, Envelope(Revision()), created).Code);

        var first = _catalog.SavePage(staff, Envelope(Revision()), page with { Caption = $"{Tag} first", PageText1 = "one" });
        var second = _catalog.SavePage(staff, Envelope(Revision()), page with { Caption = $"{Tag} second", PageText1 = "two" });
        Assert.Equal(CatalogAdminCodes.Conflict, _catalog.Undo(staff, Envelope(Revision()), first.Revision).Code);
        Assert.Equal(CatalogAdminCodes.StaleRevision, _catalog.Undo(staff, Envelope(Revision() - 1), second.Revision).Code);

        var undone = _catalog.Undo(staff, Envelope(Revision()), second.Revision);
        Assert.True(undone.Success, undone.Message);
        Assert.Equal(("PAGE", page.PageId), (undone.EntityType, undone.EntityId));
        var restored = _catalog.LoadPage(staff, page.PageId);
        Assert.Equal(($"{Tag} first", "one"), (restored.Caption, restored.PageText1));
        var latest = _catalog.History(staff, 0, 1).Groups[0];
        Assert.Equal((undone.Revision, "UPDATE", page.PageId), (latest.Id, latest.Operation, latest.EntityId));

        var furni = InsertFurniture($"{Tag}_undo_chair", 990006);
        _definitions[furni] = new ItemDefinition { Id = furni };
        var offer = _catalog.CreateOffer(staff, Envelope(Revision()),
            new CatalogAdminOffer("NORMAL", 0, furni.ToString(), page.PageId, $"{Tag} undo offer", 3, 0, 0, 1, 0, -1, -1, 0, "", true, false));
        var offerId = offer.EntityId;
        var saved = _catalog.SaveOffer(staff, Envelope(Revision()), Assert.IsType<CatalogAdminOffer>(offer.Entity) with { CostCredits = 99 });
        Assert.True(saved.Success, saved.Message);
        var offerUndo = _catalog.Undo(staff, Envelope(Revision()), saved.Revision);
        Assert.True(offerUndo.Success, offerUndo.Message);
        Assert.Equal(3, Scalar<int>("SELECT cost_credits FROM catalog_items WHERE id = @id", offerId));
    }

    [EditorDatabaseFact]
    public void OffersResolvePageOfferIdsAndReorderOnePage()
    {
        var staff = EditorTestSupport.Staff();
        var furni = InsertFurniture($"{Tag}_offer_chair", 990001);
        _definitions[furni] = new ItemDefinition { Id = furni, ItemName = $"{Tag}_offer_chair" };
        var first = CreatePage(staff, "first", -1);
        var second = CreatePage(staff, "second", -1);
        var offer = new CatalogAdminOffer("NORMAL", 0, furni.ToString(), first.PageId, $"{Tag} offer", 3, 2, 5, 1, 0, -1, 900001, 0, "", true, false);

        var created = _catalog.CreateOffer(staff, Envelope(Revision()), offer);
        Assert.True(created.Success, created.Message);
        var createdOffer = Assert.IsType<CatalogAdminOffer>(created.Entity);
        Assert.Equal((created.EntityId, 2, 5), (createdOffer.OfferId, createdOffer.CostPoints, createdOffer.PointsType));
        Assert.True(_catalog.CreateOffer(staff, Envelope(Revision()), offer with { PageId = second.PageId }).Success);
        Assert.True(_catalog.CreateOffer(staff, Envelope(Revision()), offer with { OfferIdClient = -1, CatalogName = $"{Tag} plain" }).Success);
        Reload();

        // Both pages sell official offer 900001: without a page to look at, the editor's id is ambiguous.
        Assert.False(_catalog.DeleteOffer(staff, Envelope(Revision()), 900001).Success);
        _catalog.RecordViewedPage(staff, second.PageId);
        var details = _catalog.LoadOffer(staff, 900001);
        Assert.Equal((second.PageId, 900001, 2, 5), (details.PageId, details.OfferId, details.CostPoints, details.PointsType));

        var saved = _catalog.SaveOffer(staff, Envelope(Revision()), details with { CostCredits = 9, CatalogName = $"{Tag} saved" });
        Assert.True(saved.Success, saved.Message);
        Assert.Equal(900001, saved.EntityId);
        using (var connection = _database.Connection())
        {
            var names = connection.Query<(int, string, int)>("SELECT page_id, catalog_name, cost_credits FROM catalog_items WHERE offer_id = 900001 ORDER BY page_id").ToList();
            Assert.Equal([(first.PageId, $"{Tag} offer", 3), (second.PageId, $"{Tag} saved", 9)], names);
        }

        _catalog.RecordViewedPage(staff, first.PageId);
        var plainId = _cache.Single(page => page.Id == first.PageId).Offers.Values.Single(item => item.OfferId <= 0).WireOfferId;
        var reordered = _catalog.ReorderOffers(staff, Envelope(Revision()), [(plainId, 0), (900001, 1)]);
        Assert.True(reordered.Success, reordered.Message);
        Assert.False(_catalog.ReorderOffers(staff, Envelope(Revision()), [(plainId, 0), (plainId, 1)]).Success);
        Assert.True(_catalog.DeleteOffer(staff, Envelope(Revision()), plainId).Success);
        Assert.Equal("[{\"id\":" + plainId + ",\"orderNumber\":0},{\"id\":900001,\"orderNumber\":1}]", Scalar<string>("SELECT after_json FROM catalog_admin_log WHERE id = @id", reordered.Revision));
    }

    [EditorDatabaseFact]
    public void FurniEditorUpdatesDeletesAndRevertsFurnidataWithAudit()
    {
        var staff = EditorTestSupport.Staff();
        var chair = InsertFurniture($"{Tag}_chair", 990002);
        var placed = InsertFurniture($"{Tag}_placed", 990003);
        Execute($"INSERT INTO items (user_id, room_id, base_item, extra_data, x, y, z, rot, wall_pos) VALUES (0, 0, {placed}, '', 0, 0, 0, 0, '')");
        var path = Path.Combine(_directory, "FurnitureData.json");
        File.WriteAllText(path, $$"""
            {
              "roomitemtypes": {
                "furnitype": [
                  { "id": 990002, "classname": "{{Tag}}_chair", "name": "Old chair", "description": "Old", "xdim": 1, "ydim": 1, "canstandon": false }
                ]
              },
              "wallitemtypes": { "furnitype": [] }
            }
            """);
        var furni = Furni(path);

        var detail = furni.Detail(staff, chair);
        Assert.Contains("matched_classname", detail.Furnidata.DiagnosticJson);
        Assert.Equal(0, detail.UsageCount);
        var search = furni.Search(staff, $"{Tag}_ch", "s", 1, "itemName", "desc");
        Assert.Equal(chair, Assert.Single(search.Items).Id);
        Assert.Empty(furni.Search(staff, "e3test%chair", "", 1, "id", "asc").Items);

        var updated = furni.Update(staff, chair, "{\"width\":2,\"allowStack\":true,\"interactionType\":\"gate\",\"vendingIds\":\"1,2\"}");
        Assert.True(updated.Success, updated.Message);
        Assert.Equal((2, "1", "gate", "1,2"), Scalar<(int, string, string, string)>("SELECT width, can_stack, interaction_type, vending_ids FROM furniture WHERE id = @id", (int)chair));
        Assert.False(furni.Update(staff, chair, "{\"vendingIds\":\"1;2\"}").Success);

        Assert.Equal("Cannot delete: still used by 1 placed or owned items", furni.Delete(staff, placed).Message);
        var gifted = InsertFurniture($"{Tag}_gifted", 990005);
        Execute($"INSERT INTO user_presents (item_id, base_id, extra_data) VALUES (0, {gifted}, ''); INSERT INTO catalog_deals (items, name, room_id) VALUES ('1*2;{gifted}*3', 'e3test deal', 0)");
        var refused = furni.Delete(staff, gifted).Message;
        Assert.StartsWith("Cannot delete: still used by 1 unopened gifts, catalog deals #", refused);
        var unused = InsertFurniture($"{Tag}_unused", 990004);
        Assert.True(furni.Delete(staff, unused).Success);

        var edited = furni.UpdateFurnidata(staff, chair, "{\"name\":\"New chair\",\"description\":\"New\"}");
        Assert.True(edited.Success, edited.Message);
        Assert.Contains("\"name\": \"New chair\"", File.ReadAllText(path));
        Assert.Equal("New chair", Scalar<string>("SELECT public_name FROM furniture WHERE id = @id", (int)chair));
        Thread.Sleep(1100);
        var reverted = furni.RevertFurnidata(staff, chair);
        Assert.True(reverted.Success, reverted.Message);
        Assert.Contains("\"name\": \"Old chair\"", File.ReadAllText(path));
        Assert.Equal("Old chair", Scalar<string>("SELECT public_name FROM furniture WHERE id = @id", (int)chair));
        Thread.Sleep(1100);
        Assert.False(furni.RevertFurnidata(staff, chair).Success);
        var actions = Query<(string, int)>("SELECT action, reverted FROM furni_editor_log WHERE classname LIKE 'e3test%' ORDER BY id");
        Assert.Equal([("update", 0), ("delete", 0), ("furnidata_update", 1), ("furnidata_revert", 0)], actions);
    }

    [EditorDatabaseFact]
    public void ConcurrentCatalogReloadsKeepTheClothingTableWhole()
    {
        var clothing = new Plus.HabboHotel.Catalog.Clothing.ClothingManager(_database);
        Parallel.For(0, 8, _ => clothing.Init());
        Assert.Equal(Scalar<int>("SELECT COUNT(*) FROM catalog_clothing", 0), clothing.GetClothingAllParts.Count);
    }

    private FurniEditorService Furni(string furnidataPath)
    {
        var clients = DispatchProxy.Create<IGameClientManager, CatalogProxy>();
        return new FurniEditorService(_database, new FurnidataStore(Options.Create(new FurniEditorConfiguration { FurnidataPath = furnidataPath })),
            DispatchProxy.Create<IFurniEditorTextImporter, EditorPermissionTests.Recorder>(), (ICatalogCacheRefresher)(object)_refresher, clients,
            NullLogger<FurniEditorService>.Instance);
    }

    private CatalogAdminPage CreatePage(Habbo staff, string name, int parentId)
    {
        var page = new CatalogAdminPage("NORMAL", 0, parentId, $"{Tag}_{name}", $"{Tag} {name}", "default_3x3", 1, 1, 1, -1, true, true, false, "NORMAL",
            false, "", "", "", "", "", "", "", 0, "");
        var outcome = _catalog.CreatePage(staff, Envelope(Revision()), page);
        Assert.True(outcome.Success, outcome.Message);
        return Assert.IsType<CatalogAdminPage>(outcome.Entity);
    }

    // What the catalog reload would do: load the tagged pages and their offers and give offers their page ids.
    private void Reload()
    {
        using var connection = _database.Connection();
        var pages = connection.Query<CatalogPage>("SELECT id AS Id, parent_id AS ParentId, enabled = 1 AS Enabled, visible = 1 AS Visible, min_rank AS MinimumRank FROM catalog_pages WHERE page_link LIKE 'e3test%' ORDER BY id").ToList();
        foreach (var page in pages)
            page.Items = connection.Query<CatalogItem>("SELECT id AS Id, offer_id AS OfferId, page_id AS PageId FROM catalog_items WHERE page_id = @Id ORDER BY order_num, id", page)
                .ToDictionary(item => item.Id);
        new CatalogOfferIndex().Build(pages);
        _cache.Clear();
        _cache.AddRange(pages);
    }

    private static CatalogAdminEnvelope Envelope(int revision) => new("NORMAL", CatalogAdminEnvelope.LiveVersionId, revision, "", "test", "op");

    private int Revision() => Scalar<int>("SELECT CAST(COALESCE(MAX(id), 0) AS SIGNED) FROM catalog_admin_log", 0);

    private uint InsertFurniture(string name, int sprite) => Scalar<uint>(
        $"INSERT INTO furniture (item_name, public_name, sprite_id) VALUES ('{name}', '', {sprite}); SELECT CAST(LAST_INSERT_ID() AS UNSIGNED)", 0);

    private void Cleanup() => Execute("""
        DELETE FROM items WHERE base_item IN (SELECT id FROM furniture WHERE item_name LIKE 'e3test%');
        DELETE FROM user_presents WHERE base_id IN (SELECT id FROM furniture WHERE item_name LIKE 'e3test%');
        DELETE FROM catalog_deals WHERE name LIKE 'e3test%';
        DELETE FROM catalog_items WHERE catalog_name LIKE 'e3test%';
        DELETE FROM catalog_pages WHERE page_link LIKE 'e3test%';
        DELETE FROM furniture WHERE item_name LIKE 'e3test%';
        DELETE FROM furni_editor_log WHERE classname LIKE 'e3test%';
        """);

    private void Execute(string sql)
    {
        using var connection = _database.Connection();
        connection.Execute(sql);
    }

    private T Scalar<T>(string sql, int id)
    {
        using var connection = _database.Connection();
        return connection.QuerySingle<T>(sql, new { id });
    }

    private List<T> Query<T>(string sql)
    {
        using var connection = _database.Connection();
        return connection.Query<T>(sql).ToList();
    }

    public class CatalogProxy : DispatchProxy
    {
        public List<CatalogPage> Pages { get; set; } = new();
        public Dictionary<uint, ItemDefinition> Items { get; set; } = new();

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => targetMethod!.Name switch
        {
            "get_Pages" => Pages,
            "get_Items" => Items,
            "get_GetClients" => new List<GameClient>(),
            _ => throw new NotSupportedException(targetMethod.Name)
        };
    }
}
