using System.Reflection;
using System.Text.Json.Nodes;
using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
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
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(Variable))) {
            Skip = $"Set {Variable} to a disposable task_editor_tests_ schema holding the PlusEMU schema including the role-based access-control update.";
        }
    }
}

// Runs the editors against a real, disposable database: every row it creates is tagged "e3test" and removed again.
public sealed class EditorDatabaseTests : IDisposable
{
    private const string Tag = "e3test";
    private readonly HabbiconDatabaseTests.TestDatabase _database;
    private readonly EditorPermissionTests.Recorder _refresher = (EditorPermissionTests.Recorder)(object)DispatchProxy.Create<ICatalogCacheRefresher, EditorPermissionTests.Recorder>();
    private readonly CatalogAdminService _catalog = null!;

    public EditorDatabaseTests()
    {
        var builder = new MySqlConnectionStringBuilder(Environment.GetEnvironmentVariable(EditorDatabaseFactAttribute.Variable) ?? "Database=none") { AllowUserVariables = true };

        if (!builder.Database.StartsWith("task_editor_tests_", StringComparison.Ordinal)) {
            throw new InvalidOperationException("Editor database tests require a disposable task_editor_tests_ schema.");
        }

        _database = new(builder.ConnectionString);

        if (Environment.GetEnvironmentVariable(EditorDatabaseFactAttribute.Variable) == null) {
            return;
        }

        Cleanup();
        // Pages may only require permissions the hotel knows; the test actors' extra permission is registered for them.
        Execute($"INSERT IGNORE INTO acl_permissions (`key`, category) VALUES ('{EditorTestSupport.RestrictedPagePermission}', 'catalog')");
        _catalog = new CatalogAdminService(_database, (ICatalogCacheRefresher)(object)_refresher, NullLogger<CatalogAdminService>.Instance);
    }

    public void Dispose()
    {
        if (Environment.GetEnvironmentVariable(EditorDatabaseFactAttribute.Variable) != null) {
            Cleanup();
        }
    }

    [EditorDatabaseFact]
    public void MigratedEditorGrantsPreserveTheirExplicitRoles()
    {
        using var connection = _database.Connection();
        var granted = connection.Query<(string, int)>("""
            SELECT p.permission_key, r.weight FROM role_permissions p JOIN roles r ON r.id = p.role_id
            WHERE p.permission_key IN ('catalog.edit', 'furni.edit', 'furni.delete')
            """).Order().ToList();
        Assert.Equal([("catalog.edit", 80), ("catalog.edit", 90), ("furni.delete", 80), ("furni.delete", 90), ("furni.edit", 80), ("furni.edit", 90)], granted);
    }

    [EditorDatabaseFact]
    public void CreatePageTravelsFromPacketToDatabaseAndBack()
    {
        var staff = EditorTestSupport.Staff();
        var (client, sent) = HabbiconTestSupport.Client(staff);
        int revision = Revision();
        var packet = EditorTestSupport.Incoming($"{Tag} page", $"{Tag}_page", "default_3x3", 12, "", true, true, -1, -1, "NORMAL", "NORMAL", 1,
            false, "head", "", "", "text", "", "", "", 0, "", 1, revision, "", "Created page", "create-1");

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
            """
            SELECT p.caption, i.image, t.text FROM catalog_pages p
            JOIN catalog_page_images i ON i.page_id = p.id AND i.slot = 0 JOIN catalog_page_texts t ON t.page_id = p.id AND t.slot = 0
            WHERE p.id = @pageId
            """, new { pageId }));
        var audit = connection.QuerySingle<(int, string, string, string?, string)>(
            "SELECT user_id, action, operation, before_json, after_json FROM catalog_admin_log WHERE id = @id", new { id = revision + 1 });
        Assert.Equal((staff.Id, "createPage", "CREATE", (string?)null), (audit.Item1, audit.Item2, audit.Item3, audit.Item4));
        Assert.Contains($"\"caption\":\"{Tag} page\"", audit.Item5);
        Assert.Equal(["Schedule"], _refresher.Calls);
    }

    [EditorDatabaseFact]
    public void PageEditsCheckRevisionPermissionAndTreeAndAuditBeforeAndAfter()
    {
        var staff = EditorTestSupport.Staff();
        var parent = CreatePage(staff, "parent", -1);
        var child = CreatePage(staff, "child", parent.PageId);

        var stale = _catalog.SavePage(staff, Envelope(Revision() - 1), child with { Caption = "late" });
        Assert.Equal((false, CatalogAdminCodes.StaleRevision), (stale.Success, stale.Code));

        var invalid = _catalog.SavePage(staff, Envelope(Revision()), child with { Caption = "", RequiredPermission = EditorTestSupport.RestrictedPagePermission });
        Assert.Equal((false, CatalogAdminCodes.ValidationFailed), (invalid.Success, invalid.Code));
        Assert.Equal(["caption", "requiredPermission"], invalid.FieldErrors.Keys.Order());

        var cycle = _catalog.MovePage(staff, Envelope(Revision()), parent.PageId, child.PageId, 0);
        Assert.False(cycle.Success);

        int before = Revision();
        var saved = _catalog.SavePage(staff, Envelope(before), child with { Caption = $"{Tag} renamed", PageText2 = "second" });
        Assert.True(saved.Success);
        Assert.Equal(before + 1, saved.Revision);

        using (var connection = _database.Connection()) {
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
        Assert.DoesNotContain(session.Pages, page => !CatalogAdminValidation.Available(page.RequiredPermission, staff.Access));
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
        var offer = _catalog.CreateOffer(staff, Envelope(Revision()),
            new CatalogAdminOffer("NORMAL", 0, furni.ToString(), page.PageId, $"{Tag} undo offer", 3, 0, 0, 1, 0, -1, -1, 0, "", true, false));
        var offerId = offer.EntityId;
        var saved = _catalog.SaveOffer(staff, Envelope(Revision()), Assert.IsType<CatalogAdminOffer>(offer.Entity) with { CostCredits = 99 });
        Assert.True(saved.Success, saved.Message);
        var offerUndo = _catalog.Undo(staff, Envelope(Revision()), saved.Revision);
        Assert.True(offerUndo.Success, offerUndo.Message);
        Assert.Equal(3, Scalar<int>("SELECT cost_credits FROM catalog_offers WHERE id = @id", offerId));
    }

    [EditorDatabaseFact]
    public void OfferIdsAreUniqueAndAnOfferOnSeveralPagesIsEditedAsPlaced()
    {
        var staff = EditorTestSupport.Staff();
        var furni = InsertFurniture($"{Tag}_offer_chair", 990001);
        var first = CreatePage(staff, "first", -1);
        var second = CreatePage(staff, "second", -1);
        var offer = new CatalogAdminOffer("NORMAL", 0, furni.ToString(), first.PageId, $"{Tag} offer", 3, 2, 5, 1, 0, -1, 900001, 0, "", true, false);

        var created = _catalog.CreateOffer(staff, Envelope(Revision()), offer);
        Assert.True(created.Success, created.Message);
        var createdOffer = Assert.IsType<CatalogAdminOffer>(created.Entity);
        Assert.Equal((900001, 900001, 2, 5), (created.EntityId, createdOffer.OfferId, createdOffer.CostPoints, createdOffer.PointsType));
        var taken = _catalog.CreateOffer(staff, Envelope(Revision()), offer with { PageId = second.PageId });
        Assert.Equal("Offer #900001 already exists.", taken.FieldErrors["offerIdGroup"]);
        var plain = _catalog.CreateOffer(staff, Envelope(Revision()), offer with { OfferIdClient = -1, CatalogName = $"{Tag} plain" });
        Assert.True(plain.Success, plain.Message);
        Assert.True(plain.EntityId >= CatalogOfferIndex.CustomOfferIdBase);
        Assert.Equal(-1, Assert.IsType<CatalogAdminOffer>(plain.Entity).OfferIdClient);
        Execute($"INSERT INTO catalog_page_offers (page_id, offer_id, position) VALUES ({second.PageId}, 900001, 0)");

        // Offer 900001 is on both pages: a session that neither looks at a page nor loaded it cannot name one.
        Assert.Equal(CatalogAdminCodes.Conflict, _catalog.DeleteOffer(EditorTestSupport.Staff(), Envelope(Revision()), 900001).Code);
        _catalog.RecordViewedPage(staff, second.PageId);
        var details = _catalog.LoadOffer(staff, 900001);
        Assert.Equal((second.PageId, 900001, 2, 5), (details.PageId, details.OfferId, details.CostPoints, details.PointsType));

        var saved = _catalog.SaveOffer(staff, Envelope(Revision()), details with { CostCredits = 9, CatalogName = $"{Tag} saved" });
        Assert.True(saved.Success, saved.Message);
        Assert.Equal(900001, saved.EntityId);
        // One offer: both pages now show the saved name and price.
        Assert.Equal(($"{Tag} saved", 9), Scalar<(string, int)>("SELECT localization_key, cost_credits FROM catalog_offers WHERE id = @id", 900001));
        Assert.Equal([first.PageId, second.PageId], Query<int>("SELECT page_id FROM catalog_page_offers WHERE offer_id = 900001 ORDER BY page_id"));

        _catalog.RecordViewedPage(staff, first.PageId);
        var plainId = plain.EntityId;
        var reordered = _catalog.ReorderOffers(staff, Envelope(Revision()), [(plainId, 0), (900001, 1)]);
        Assert.True(reordered.Success, reordered.Message);
        Assert.Equal([plainId, 900001], Query<int>($"SELECT offer_id FROM catalog_page_offers WHERE page_id = {first.PageId} ORDER BY position"));
        Assert.False(_catalog.ReorderOffers(staff, Envelope(Revision()), [(plainId, 0), (plainId, 1)]).Success);
        Assert.Equal("[{\"id\":" + plainId + ",\"orderNumber\":0},{\"id\":900001,\"orderNumber\":1}]", Scalar<string>("SELECT after_json FROM catalog_admin_log WHERE id = @id", reordered.Revision));

        // Deleting takes an offer off the page being viewed; an offer left on no page is gone.
        Assert.True(_catalog.DeleteOffer(staff, Envelope(Revision()), plainId).Success);
        Assert.Equal(0, Scalar<int>("SELECT COUNT(*) FROM catalog_offers WHERE id = @id", plainId));
        Assert.True(_catalog.DeleteOffer(staff, Envelope(Revision()), 900001).Success);
        Assert.Equal([second.PageId], Query<int>("SELECT page_id FROM catalog_page_offers WHERE offer_id = 900001"));
        Assert.Equal(1, Scalar<int>("SELECT COUNT(*) FROM catalog_offer_products WHERE offer_id = @id", 900001));
    }

    [EditorDatabaseFact]
    public void FurniEditorUpdatesDeletesAndRevertsFurnidataWithAudit()
    {
        var staff = EditorTestSupport.Staff();
        var chair = InsertFurniture($"{Tag}_chair", 990002);
        var placed = InsertFurniture($"{Tag}_placed", 990003);
        Execute($"INSERT INTO items (user_id, room_id, base_item, extra_data, x, y, z, rot, wall_pos) VALUES (0, 0, {placed}, '', 0, 0, 0, 0, '')");
        GiveFurnidata(chair, "Old chair", "Old");
        var furni = Furni();

        var detail = furni.Detail(staff, chair);
        Assert.Contains("matched_classname", detail.Furnidata.DiagnosticJson);
        Assert.Equal(0, detail.UsageCount);
        var search = furni.Search(staff, $"{Tag}_ch", "s", 1, "itemName", "desc");
        Assert.Equal(chair, Assert.Single(search.Items).Id);
        Assert.Empty(furni.Search(staff, "e3test%chair", "", 1, "id", "asc").Items);

        var updated = furni.Update(staff, chair, "{\"width\":2,\"allowStack\":true,\"interactionType\":\"gate\",\"vendingIds\":\"1,2\"}");
        Assert.True(updated.Success, updated.Message);
        Assert.Equal((2, true, "gate", "1,2"), Scalar<(int, bool, string, string)>("SELECT width, can_stack, interaction_type, vending_ids FROM furniture WHERE id = @id", (int)chair));
        Assert.False(furni.Update(staff, chair, "{\"vendingIds\":\"1;2\"}").Success);

        Assert.Equal("Cannot delete: still used by 1 placed or owned items", furni.Delete(staff, placed).Message);
        var gifted = InsertFurniture($"{Tag}_gifted", 990005);
        Execute($"""
            INSERT INTO user_presents (item_id, base_id, extra_data) VALUES (0, {gifted}, '');
            INSERT INTO catalog_offers (id, localization_key) VALUES (1999999901, 'e3test bundle');
            INSERT INTO catalog_offer_products (offer_id, position, product_type, furniture_id, amount) VALUES (1999999901, 0, 'furni', {chair}, 2), (1999999901, 1, 'furni', {gifted}, 3);
            """);
        Assert.Equal("Cannot delete: still used by 1 catalog offers, 1 unopened gifts", furni.Delete(staff, gifted).Message);
        var unused = InsertFurniture($"{Tag}_unused", 990004);
        Assert.True(furni.Delete(staff, unused).Success);

        var edited = furni.UpdateFurnidata(staff, chair, "{\"name\":\"New chair\",\"description\":\"New\"}");
        Assert.True(edited.Success, edited.Message);
        Assert.Equal(("New chair", "New"), Scalar<(string, string)>("SELECT name, description FROM furniture WHERE id = @id", (int)chair));
        Assert.Equal("New chair", Scalar<string>("SELECT public_name FROM furniture WHERE id = @id", (int)chair));
        Thread.Sleep(1100);
        var reverted = furni.RevertFurnidata(staff, chair);
        Assert.True(reverted.Success, reverted.Message);
        Assert.Equal(("Old chair", "Old"), Scalar<(string, string)>("SELECT name, description FROM furniture WHERE id = @id", (int)chair));
        Assert.Equal("Old chair", Scalar<string>("SELECT public_name FROM furniture WHERE id = @id", (int)chair));
        Thread.Sleep(1100);
        Assert.False(furni.RevertFurnidata(staff, chair).Success);
        Thread.Sleep(1100);
        Assert.Equal("The furnidata entry has no height", furni.UpdateFurnidata(staff, chair, "{\"structure\":{\"height\":1}}").Message);
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

    [EditorDatabaseFact]
    public void PageMovesAndParentsRespectPermissionsAndMoveUndoRestoresEverySibling()
    {
        var owner = EditorTestSupport.Owner();
        var staff = EditorTestSupport.Staff();
        var parent = CreatePage(owner, "rank_parent", -1);
        var hidden = CreatePage(owner, "rank_hidden", parent.PageId, requiredPermission: EditorTestSupport.RestrictedPagePermission, order: 0);
        var ordinary = CreatePage(owner, "rank_ordinary", -1);

        var refused = _catalog.MovePage(staff, Envelope(Revision()), ordinary.PageId, parent.PageId, 0);
        Assert.Equal((false, CatalogAdminCodes.Forbidden), (refused.Success, refused.Code));
        Assert.Equal(0, Scalar<int>("SELECT position FROM catalog_pages WHERE id = @id", hidden.PageId));
        var underHidden = _catalog.CreatePage(staff, Envelope(Revision()), ordinary with { PageId = 0, ParentId = hidden.PageId, CaptionSave = $"{Tag}_under" });
        Assert.Equal("You cannot use a page requiring a permission you do not have as parent.", underHidden.FieldErrors["parentId"]);

        var a = CreatePage(owner, "move_a", parent.PageId, order: 2);
        var b = CreatePage(owner, "move_b", parent.PageId, order: 3);
        var c = CreatePage(owner, "move_c", parent.PageId, order: 4);
        var moved = _catalog.MovePage(owner, Envelope(Revision()), c.PageId, parent.PageId, 0);
        Assert.True(moved.Success, moved.Message);
        Assert.Equal(1, Scalar<int>("SELECT position FROM catalog_pages WHERE id = @id", hidden.PageId));
        Assert.Equal(CatalogAdminCodes.Forbidden, _catalog.Undo(staff, Envelope(Revision()), moved.Revision).Code);

        var undone = _catalog.Undo(owner, Envelope(Revision()), moved.Revision);
        Assert.True(undone.Success, undone.Message);
        Assert.Equal([(hidden.PageId, 0), (a.PageId, 2), (b.PageId, 3), (c.PageId, 4)], Query<(int, int)>(
            $"SELECT id, position FROM catalog_pages WHERE parent_id = {parent.PageId} ORDER BY position, id"));

        var again = _catalog.MovePage(owner, Envelope(Revision()), c.PageId, parent.PageId, 0);
        Execute($"UPDATE catalog_pages SET position = 9 WHERE id = {hidden.PageId}");
        Assert.Equal(CatalogAdminCodes.Conflict, _catalog.Undo(owner, Envelope(Revision()), again.Revision).Code);
    }

    [EditorDatabaseFact]
    public void MoveUndoIsRefusedWhenASiblingLeftTheParentSince()
    {
        var owner = EditorTestSupport.Owner();
        var p = CreatePage(owner, "left_p", -1);
        var q = CreatePage(owner, "left_q", -1);
        CreatePage(owner, "left_x", q.PageId, order: 0);
        var a = CreatePage(owner, "left_a", p.PageId, order: 0);
        CreatePage(owner, "left_b", p.PageId, order: 1);
        var c = CreatePage(owner, "left_c", p.PageId, order: 2);
        var first = _catalog.MovePage(owner, Envelope(Revision()), c.PageId, p.PageId, 0);
        Assert.True(first.Success, first.Message);
        var away = _catalog.MovePage(owner, Envelope(Revision()), a.PageId, q.PageId, 1);
        Assert.True(away.Success, away.Message);
        Assert.Equal((q.PageId, 1), Scalar<(int, int)>("SELECT parent_id, position FROM catalog_pages WHERE id = @id", a.PageId));

        var undo = _catalog.Undo(owner, Envelope(Revision()), first.Revision);

        Assert.Equal((false, CatalogAdminCodes.Conflict, "This entity changed again later; undo the newer change first."), (undo.Success, undo.Code, undo.Message));
        Assert.Equal((q.PageId, 1), Scalar<(int, int)>("SELECT parent_id, position FROM catalog_pages WHERE id = @id", a.PageId));
    }

    [EditorDatabaseFact]
    public void FurniDetailShowsOnlyOffersOnPagesTheActorCanOpen()
    {
        var owner = EditorTestSupport.Owner();
        var staff = EditorTestSupport.Staff();
        var hiddenPage = CreatePage(owner, "refs_hidden", -1, requiredPermission: EditorTestSupport.RestrictedPagePermission);
        var visiblePage = CreatePage(owner, "refs_visible", -1);
        var furni = InsertFurniture($"{Tag}_refs", 990011);

        foreach (var page in new[] { hiddenPage, visiblePage }) {
            Assert.True(_catalog.CreateOffer(owner, Envelope(Revision()), Offer(furni, page.PageId, -1)).Success);
        }

        var editor = Furni();

        Assert.Equal([visiblePage.PageId], editor.Detail(staff, furni).CatalogRefs.Select(reference => reference.PageId));
        Assert.Equal(2, editor.Detail(owner, furni).CatalogRefs.Count);
        Assert.Equal("Cannot delete: still used by 2 catalog offers", editor.Delete(staff, furni).Message);
    }

    [EditorDatabaseFact]
    public void OfferSavesGoToThePlacementTheEditorLoadedAndCanRenumberTheOffer()
    {
        var owner = EditorTestSupport.Staff();
        var furni = InsertFurniture($"{Tag}_bound", 990012);
        var pageA = CreatePage(owner, "bound_a", -1);
        var pageB = CreatePage(owner, "bound_b", -1);
        var created = _catalog.CreateOffer(owner, Envelope(Revision()), Offer(furni, pageA.PageId, 900091));
        Assert.Equal(900091, created.EntityId);
        Execute($"INSERT INTO catalog_page_offers (page_id, offer_id, position) VALUES ({pageB.PageId}, 900091, 0)");

        _catalog.RecordViewedPage(owner, pageA.PageId);
        var form = _catalog.LoadOffer(owner, 900091);
        _catalog.RecordViewedPage(owner, pageB.PageId);
        var moved = _catalog.SaveOffer(owner, Envelope(Revision()), form with { PageId = pageB.PageId });
        Assert.Equal("This offer is already on that page.", moved.FieldErrors["pageId"]);
        var saved = _catalog.SaveOffer(owner, Envelope(Revision()), form with { CostCredits = 77, OrderNumber = 4 });
        Assert.True(saved.Success, saved.Message);
        Assert.Equal(77, Scalar<int>("SELECT cost_credits FROM catalog_offers WHERE id = @id", 900091));
        Assert.Equal([(pageA.PageId, 4), (pageB.PageId, 0)], Query<(int, int)>("SELECT page_id, position FROM catalog_page_offers WHERE offer_id = 900091 ORDER BY page_id"));

        var otherSession = EditorTestSupport.Staff();
        Assert.Equal(CatalogAdminCodes.Conflict, _catalog.SaveOffer(otherSession, Envelope(Revision()), form with { CostCredits = 1 }).Code);

        // A new official id renumbers the offer everywhere it is placed.
        var renumbered = _catalog.SaveOffer(owner, Envelope(Revision()), form with { CostCredits = 77, OfferIdClient = 900092 });
        Assert.True(renumbered.Success, renumbered.Message);
        Assert.Equal(900092, renumbered.EntityId);
        Assert.Equal((0, 2, 1), (Scalar<int>("SELECT COUNT(*) FROM catalog_offers WHERE id = @id", 900091),
            Scalar<int>("SELECT COUNT(*) FROM catalog_page_offers WHERE offer_id = @id", 900092), Scalar<int>("SELECT COUNT(*) FROM catalog_offer_products WHERE offer_id = @id", 900092)));
        var custom = _catalog.SaveOffer(owner, Envelope(Revision()), Assert.IsType<CatalogAdminOffer>(renumbered.Entity) with { OfferIdClient = -1 });
        Assert.True(custom.Success, custom.Message);
        Assert.True(custom.EntityId >= CatalogOfferIndex.CustomOfferIdBase);
    }

    [EditorDatabaseFact]
    public void OffersCannotUseFurnitureDeletedSinceTheCacheWasLoaded()
    {
        var owner = EditorTestSupport.Staff();
        var page = CreatePage(owner, "deleted_furni", -1);
        var furni = InsertFurniture($"{Tag}_deleted", 990013);
        Assert.True(Furni().Delete(owner, furni).Success);

        var created = _catalog.CreateOffer(owner, Envelope(Revision()), Offer(furni, page.PageId, -1));
        Assert.Equal($"Furniture #{furni} does not exist.", created.FieldErrors["itemIds"]);
    }

    [EditorDatabaseFact]
    public void FurnidataRevertIsRefusedWhenTheSharedEntryChangedSince()
    {
        var owner = EditorTestSupport.Staff();
        var first = InsertFurniture($"{Tag}_duplicate", 990020);
        var second = InsertFurniture($"{Tag}_duplicate", 990020);
        GiveFurnidata(first, "old name", "old description");
        var editor = Furni();

        Assert.True(editor.UpdateFurnidata(owner, first, "{\"name\":\"new name\"}").Success);
        Thread.Sleep(1100);
        Assert.True(editor.UpdateFurnidata(owner, second, "{\"description\":\"new description\"}").Success);
        Thread.Sleep(1100);

        var revert = editor.RevertFurnidata(owner, first);
        Assert.Equal((false, "The furnidata entry changed since that edit; revert refused", first), (revert.Success, revert.Message, revert.ItemId));
        Assert.Equal(("new name", "new description"), Scalar<(string, string)>("SELECT name, description FROM furniture WHERE id = @id", (int)first));
        Assert.Equal((990020, "roomitemtypes"), Scalar<(int, string)>("SELECT entry_id, entry_section FROM furni_editor_log WHERE item_id = @id ORDER BY id DESC LIMIT 1", (int)first));
        // The row holding the shared entry stays while another row uses it.
        Assert.Equal("Cannot delete: still used by 1 other furniture using its furnidata entry", editor.Delete(owner, first).Message);
        Assert.True(editor.Delete(owner, second).Success);
    }

    [EditorDatabaseFact]
    public void FurnidataSpriteIdsAreUniquePerKindAndClassnamesUnique()
    {
        var chair = InsertFurniture($"{Tag}_unique", 990030);
        var copy = InsertFurniture($"{Tag}_UNIQUE", 990031);
        var sameSprite = InsertFurniture($"{Tag}_other", 990030);
        var wall = InsertFurniture($"{Tag}_wall", 990030);
        Execute($"UPDATE furniture SET type = 'i' WHERE id = {wall}");
        GiveFurnidata(chair, "Chair", "");

        Assert.Throws<MySqlException>(() => GiveFurnidata(copy, "Copy", ""));
        Assert.Throws<MySqlException>(() => GiveFurnidata(sameSprite, "Other", ""));
        GiveFurnidata(wall, "Wall", "");
        var effect = InsertFurniture($"{Tag}_effect", 990032);
        Execute($"UPDATE furniture SET type = 'e' WHERE id = {effect}");
        Assert.Throws<MySqlException>(() => GiveFurnidata(effect, "Effect", ""));
    }

    [EditorDatabaseFact]
    public void FurnidataIsGeneratedFromTheDatabaseAndRebuiltAfterEdits()
    {
        var staff = EditorTestSupport.Staff();
        var lamp = InsertFurniture($"{Tag}_lamp", 990040);
        var furnidata = Furnidata();
        Assert.Null(furnidata.Current());

        GiveFurnidata(lamp, "Lamp", "Bright");
        furnidata.Invalidate();
        var first = furnidata.Current()!;
        Assert.Same(first, furnidata.Current());
        var entry = Assert.Single(JsonNode.Parse(first.Content)!["roomitemtypes"]!["furnitype"]!.AsArray())!;
        Assert.Equal((990040, $"{Tag}_lamp", "Lamp", -1), ((int)entry["id"]!, (string)entry["classname"]!, (string)entry["name"]!, (int)entry["offerid"]!));

        Assert.True(Furni(furnidata).UpdateFurnidata(staff, lamp, "{\"name\":\"Lava lamp\"}").Success);
        var second = furnidata.Current()!;
        Assert.Equal("Lava lamp", (string)JsonNode.Parse(second.Content)!["roomitemtypes"]!["furnitype"]![0]!["name"]!);
        Assert.NotEqual(first.ETag, second.ETag);
    }

    [EditorDatabaseFact]
    public void SessionRevisionAndPagesComeFromOneSnapshot()
    {
        var owner = EditorTestSupport.Staff();
        int before = Revision();
        _catalog.BetweenSessionReads = () => Execute("""
            INSERT INTO catalog_pages (parent_id, caption, link, position) VALUES (NULL, 'e3test snapshot', 'e3test_snapshot', 0);
            INSERT INTO catalog_admin_log (user_id, username, action, entity_type, catalog_type, entity_id, operation) VALUES (1, 'other', 'createPage', 'PAGE', 'NORMAL', 0, 'CREATE');
            """);

        var session = _catalog.OpenSession(owner);

        Assert.Equal(before, session.Revision);
        Assert.DoesNotContain(session.Pages, page => page.CaptionSave == "e3test_snapshot");
        _catalog.BetweenSessionReads = null;
        Assert.Contains(_catalog.OpenSession(owner).Pages, page => page.CaptionSave == "e3test_snapshot");
    }

    [EditorDatabaseFact]
    public void ConcurrentLimitedPurchasesGetDistinctSerialsUpToTheStack()
    {
        const int offerId = 1999999902;
        Execute($"INSERT INTO catalog_offers (id, localization_key) VALUES ({offerId}, 'e3test ltd'); INSERT INTO catalog_offer_limited (offer_id, stack) VALUES ({offerId}, 5)");
        var serials = new System.Collections.Concurrent.ConcurrentBag<int?>();

        Parallel.For(0, 20, new ParallelOptions { MaxDegreeOfParallelism = 20 }, _ =>
        {
            using var connection = _database.Connection();
            serials.Add(CatalogLimitedStock.Reserve(connection, offerId));
        });

        Assert.Equal([1, 2, 3, 4, 5], serials.Where(serial => serial != null).Select(serial => serial!.Value).Order());
        Assert.Equal(15, serials.Count(serial => serial == null));
        Assert.Equal(5, Scalar<int>("SELECT sold FROM catalog_offer_limited WHERE offer_id = @id", offerId));
    }

    private static CatalogAdminOffer Offer(uint furni, int pageId, int offerId) =>
        new("NORMAL", 0, furni.ToString(), pageId, $"{Tag} offer", 3, 0, 0, 1, 0, -1, offerId, 0, "", true, false);

    private FurniEditorService Furni(ICatalogFurnidata? furnidata = null)
    {
        var clients = DispatchProxy.Create<IGameClientManager, CatalogProxy>();

        return new FurniEditorService(_database, furnidata ?? Furnidata(),
            DispatchProxy.Create<IFurniEditorTextImporter, EditorPermissionTests.Recorder>(), (ICatalogCacheRefresher)(object)_refresher, clients,
            NullLogger<FurniEditorService>.Instance, TimeProvider.System);
    }

    private CatalogFurnidata Furnidata() => new(_database, CatalogSnapshotTestSupport.Proxy<ICatalogManager>((method, _) => method switch
    {
        "get_Revision" => 1,
        "get_Pages" => new List<CatalogPage>(),
        _ => throw new NotSupportedException(method)
    }));

    private void GiveFurnidata(uint id, string name, string description) => Execute(
        $"UPDATE furniture SET has_furnidata = TRUE, name = '{name}', description = '{description}', part_colors = '' WHERE id = {id}");

    private CatalogAdminPage CreatePage(Habbo staff, string name, int parentId, string requiredPermission = "", int order = -1)
    {
        var page = new CatalogAdminPage("NORMAL", 0, parentId, $"{Tag}_{name}", $"{Tag} {name}", "default_3x3", 1, 1, requiredPermission, order, true, true, false, "NORMAL",
            "", "", "", "", "", "", "", 0, "");
        var outcome = _catalog.CreatePage(staff, Envelope(Revision()), page);
        Assert.True(outcome.Success, outcome.Message);

        return Assert.IsType<CatalogAdminPage>(outcome.Entity);
    }

    private static CatalogAdminEnvelope Envelope(int revision) => new("NORMAL", CatalogAdminEnvelope.LiveVersionId, revision, "", "test", "op");

    private int Revision() => Scalar<int>("SELECT CAST(COALESCE(MAX(id), 0) AS SIGNED) FROM catalog_admin_log", 0);

    private uint InsertFurniture(string name, int sprite) => Scalar<uint>(
        $"INSERT INTO furniture (item_name, public_name, sprite_id) VALUES ('{name}', '', {sprite}); SELECT CAST(LAST_INSERT_ID() AS UNSIGNED)", 0);

    private void Cleanup() => Execute("""
        DELETE FROM items WHERE base_item IN (SELECT id FROM furniture WHERE item_name LIKE 'e3test%');
        DELETE FROM user_presents WHERE base_id IN (SELECT id FROM furniture WHERE item_name LIKE 'e3test%');
        DELETE FROM catalog_offers WHERE localization_key LIKE 'e3test%';
        UPDATE catalog_pages SET parent_id = NULL WHERE link LIKE 'e3test%';
        DELETE FROM catalog_pages WHERE link LIKE 'e3test%';
        DELETE FROM acl_permissions WHERE `key` = 'catalog.pages.owner';
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
