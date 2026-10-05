using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Plus.Communication.Packets.Incoming.Catalog.Admin;
using Plus.Communication.Packets.Incoming.FurniEditor;
using Plus.HabboHotel.Catalog;
using Plus.HabboHotel.Catalog.Admin;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.Editor;
using Plus.HabboHotel.Permissions;
using Plus.HabboHotel.Users;
using Xunit;

namespace Plus.Tests;

public class EditorPermissionTests
{
    private static readonly CatalogAdminEnvelope Envelope = new("NORMAL", 1, 0, "", "", "op-1");
    private static readonly CatalogAdminPage Page = new("NORMAL", 1, -1, "p", "Page", "default_3x3", 1, 1, "", 0, true, true, false, "NORMAL",
        "", "", "", "", "", "", "", 0, "");
    private static readonly CatalogAdminOffer Offer = new("NORMAL", 1, "1", 1, "o", 0, 0, 0, 1, 0, 0, -1, 0, "", true, false);

    private static (CatalogAdminService Service, Recorder Refresher) Catalog()
    {
        var refresher = DispatchProxy.Create<ICatalogCacheRefresher, Recorder>();
        return (new CatalogAdminService(EditorTestSupport.UntouchableDatabase(), DispatchProxy.Create<ICatalogManager, Recorder>(),
            refresher, NullLogger<CatalogAdminService>.Instance), (Recorder)(object)refresher);
    }

    private static (FurniEditorService Service, Recorder Refresher) Furni()
    {
        var refresher = DispatchProxy.Create<ICatalogCacheRefresher, Recorder>();
        return (new FurniEditorService(EditorTestSupport.UntouchableDatabase(), new FurnidataStore(Options.Create(new FurniEditorConfiguration())),
            DispatchProxy.Create<IFurniEditorTextImporter, Recorder>(), refresher, DispatchProxy.Create<IGameClientManager, Recorder>(),
            NullLogger<FurniEditorService>.Instance, TimeProvider.System), (Recorder)(object)refresher);
    }

    public static TheoryData<Habbo> Denied => new()
    {
        EditorTestSupport.Player(), EditorTestSupport.Staff(90, PermissionKeys.ModerationTool),
        new Habbo { Id = 7003, Username = "denied editor", Access = EditorTestSupport.Access(
            [PermissionKeys.CatalogEdit, PermissionKeys.FurniEdit, PermissionKeys.FurniDelete],
            overrides: [new UserPermissionOverride(PermissionKeys.CatalogEdit, true)]) }
    };

    [Theory]
    [MemberData(nameof(Denied))]
    public void CatalogMutationsAreRefusedBeforeTouchingTheDatabase(Habbo actor)
    {
        var (service, refresher) = Catalog();
        CatalogAdminOutcome[] outcomes =
        [
            service.CreatePage(actor, Envelope, Page), service.SavePage(actor, Envelope, Page), service.DeletePage(actor, Envelope, 1),
            service.MovePage(actor, Envelope, 1, -1, 0), service.SetPageEnabled(actor, Envelope, 1, false), service.SetPageVisible(actor, Envelope, 1, false),
            service.SavePageImages(actor, Envelope, 1, "a", "b"), service.SavePageIcon(actor, Envelope, 1, 2), service.CreateOffer(actor, Envelope, Offer),
            service.SaveOffer(actor, Envelope, Offer), service.DeleteOffer(actor, Envelope, 1), service.MoveOffer(actor, Envelope, 1, 2),
            service.ReorderOffers(actor, Envelope, [(1, 0)]), service.Undo(actor, Envelope, 1)
        ];
        Assert.All(outcomes, outcome => Assert.Equal((false, CatalogAdminCodes.Forbidden), (outcome.Success, outcome.Code)));
        Assert.False(service.Publish(actor));
        Assert.Throws<CatalogAdminRejected>(() => service.OpenSession(actor));
        Assert.Throws<CatalogAdminRejected>(() => service.History(actor, 0, 50));
        Assert.Throws<CatalogAdminRejected>(() => service.LoadPage(actor, 1));
        Assert.Throws<CatalogAdminRejected>(() => service.LoadOffer(actor, 1));
        Assert.Empty(refresher.Calls);
    }

    [Theory]
    [MemberData(nameof(Denied))]
    public async Task FurniEditorIsRefusedBeforeTouchingTheDatabase(Habbo actor)
    {
        var (service, refresher) = Furni();
        // Refusals name the furniture the request was about, so the editor can match them to it.
        Assert.All(new[] { service.Update(actor, 41, "{}"), service.Delete(actor, 41), service.UpdateFurnidata(actor, 41, "{\"name\":\"x\"}"), service.RevertFurnidata(actor, 41) },
            result => Assert.Equal((false, "No permission", 41u), (result.Success, result.Message, result.ItemId)));
        Assert.Equal(0u, Assert.Throws<FurniEditorRejected>(() => service.Search(actor, "", "", 1, "id", "asc")).ItemId);
        Assert.Equal(41u, Assert.Throws<FurniEditorRejected>(() => service.Detail(actor, 41)).ItemId);
        Assert.Equal(0u, Assert.Throws<FurniEditorRejected>(() => service.DetailBySprite(actor, 1)).ItemId);
        Assert.Throws<FurniEditorRejected>(() => service.Interactions(actor));
        Assert.Equal(41u, (await Assert.ThrowsAsync<FurniEditorRejected>(() => service.ImportText(actor, 41))).ItemId);
        Assert.Empty(refresher.Calls);
    }

    [Fact]
    public async Task ImportWithoutAnImportUrlSaysItIsNotConfigured()
    {
        var service = new FurniEditorService(EditorTestSupport.UntouchableDatabase(), new FurnidataStore(Options.Create(new FurniEditorConfiguration())),
            new FurniEditorTextImporter(Options.Create(new FurniEditorConfiguration()), TimeProvider.System), DispatchProxy.Create<ICatalogCacheRefresher, Recorder>(),
            DispatchProxy.Create<IGameClientManager, Recorder>(), NullLogger<FurniEditorService>.Instance, TimeProvider.System);
        var refused = await Assert.ThrowsAsync<FurniEditorRejected>(() => service.ImportText(EditorTestSupport.Staff(), 41));
        Assert.Equal(("Import from Habbo is not configured", 41u), (refused.Message, refused.ItemId));
    }

    [Fact]
    public void DestructiveFurniActionsNeedTheirOwnRight()
    {
        var (service, _) = Furni();
        var editorOnly = EditorTestSupport.Staff(90, PermissionKeys.CatalogEdit);
        Assert.Equal(1u, service.Delete(editorOnly, 1).ItemId);
        Assert.False(service.Delete(editorOnly, 1).Success);
        Assert.False(service.UpdateFurnidata(editorOnly, 1, "{\"name\":\"x\"}").Success);
        Assert.False(service.RevertFurnidata(editorOnly, 1).Success);
        var deleteOnly = EditorTestSupport.Staff(90, PermissionKeys.FurniDelete);
        Assert.False(service.Delete(deleteOnly, 1).Success);
        Assert.True(EditorTestSupport.Staff().Access.Can(PermissionKeys.FurniDelete));
    }

    [Fact]
    public void EveryEditorPacketHasAHeaderAndTheRevisionMapsIt()
    {
        var handlers = typeof(CatalogAdminSavePageEvent).Assembly.GetTypes()
            .Where(type => type.Namespace is "Plus.Communication.Packets.Incoming.Catalog.Admin" or "Plus.Communication.Packets.Incoming.FurniEditor"
                && type.IsAssignableTo(typeof(Plus.Communication.Packets.IPacketEvent)))
            .ToList();
        Assert.Equal(28, handlers.Count);
        var revision = System.Text.Json.JsonDocument.Parse(File.ReadAllText(HabbiconPacketTests.Repo("Resources/Revisions/OCTANE-3-6-0-FLOOR-20260909.json"))).RootElement;
        foreach (var handler in handlers)
        {
            var permission = handler.GetCustomAttribute<Plus.Communication.Attributes.RequiresPermissionAttribute>();
            Assert.NotNull(permission);
            string[] expected = handler.Name switch
            {
                nameof(FurniEditorDeleteEvent) => [PermissionKeys.CatalogEdit, PermissionKeys.FurniDelete],
                nameof(FurniEditorUpdateFurnidataEvent) or nameof(FurniEditorRevertFurnidataEvent) => [PermissionKeys.CatalogEdit, PermissionKeys.FurniEdit],
                _ => [PermissionKeys.CatalogEdit]
            };
            Assert.Equal(expected, permission!.Permissions);
            var header = typeof(Plus.Communication.Packets.Incoming.ClientPacketHeader).GetField(handler.Name);
            Assert.NotNull(header);
            Assert.Equal((uint)header!.GetValue(null)!, revision.GetProperty("IncomingHeaders").GetProperty(handler.Name).GetUInt32());
        }
        foreach (var composer in new[] { "CatalogAdminResultComposer", "CatalogAdminOfferDetailsComposer", "CatalogAdminPageDetailsComposer",
                     "CatalogStudioSessionComposer", "CatalogStudioHistoryComposer",
                     "CatalogStudioOperationComposer", "FurniEditorSearchResultComposer", "FurniEditorDetailResultComposer",
                     "FurniEditorInteractionsResultComposer", "FurniEditorResultComposer", "FurnitureDataReloadComposer", "FurniEditorImportTextResultComposer" })
        {
            var header = typeof(Plus.Communication.Packets.Outgoing.ServerPacketHeader).GetField(composer);
            Assert.Equal((uint)header!.GetValue(null)!, revision.GetProperty("OutgoingHeaders").GetProperty(composer).GetUInt32());
        }
    }

    // Clients must never be able to send catalog SQL (import/export) or have the server validate documents for them:
    // those packets are unknown to the server and dropped.
    [Fact]
    public void CatalogSqlDocumentAndValidationPacketsAreNotRegistered()
    {
        string[] incoming = ["CatalogStudioValidateEvent", "CatalogStudioExportEvent", "CatalogStudioDocumentDryRunEvent", "CatalogStudioDocumentApplyEvent"];
        string[] outgoing = ["CatalogStudioValidationComposer", "CatalogStudioDocumentResultComposer"];
        uint[] wireIds = [10073, 10078, 10079, 10080];
        var assembly = typeof(CatalogAdminSavePageEvent).Assembly;

        Assert.DoesNotContain(assembly.GetTypes(), type => incoming.Contains(type.Name) || outgoing.Contains(type.Name));
        Assert.DoesNotContain(typeof(Plus.Communication.Packets.Incoming.ClientPacketHeader).GetFields(),
            field => incoming.Contains(field.Name) || (field.GetRawConstantValue() is uint id && wireIds.Contains(id)));
        Assert.DoesNotContain(typeof(Plus.Communication.Packets.Outgoing.ServerPacketHeader).GetFields(), field => outgoing.Contains(field.Name));
        foreach (var file in new[] { "OCTANE-3-6-0-FLOOR-20260909.json", "1.6.6.json", "3.6.0.json" })
        {
            var revision = System.Text.Json.JsonDocument.Parse(File.ReadAllText(HabbiconPacketTests.Repo($"Resources/Revisions/{file}"))).RootElement;
            var incomingHeaders = revision.GetProperty("IncomingHeaders").EnumerateObject().ToList();
            Assert.DoesNotContain(incomingHeaders, header => incoming.Contains(header.Name) || wireIds.Contains(header.Value.GetUInt32()));
            Assert.DoesNotContain(revision.GetProperty("OutgoingHeaders").EnumerateObject(), header => outgoing.Contains(header.Name));
        }
    }

    public class Recorder : DispatchProxy
    {
        public List<string> Calls { get; } = new();

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            Calls.Add(targetMethod!.Name);
            var type = targetMethod.ReturnType;
            return type.IsValueType && type != typeof(void) ? Activator.CreateInstance(type) : null;
        }
    }
}
