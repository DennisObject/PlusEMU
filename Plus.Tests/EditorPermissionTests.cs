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
    private static readonly CatalogAdminPage Page = new("NORMAL", 1, -1, "p", "Page", "default_3x3", 1, 1, 1, 0, true, true, false, "NORMAL", false,
        "", "", "", "", "", "", "", 0, "");
    private static readonly CatalogAdminOffer Offer = new("NORMAL", 1, "1", 1, "o", 0, 0, 0, 1, 0, 0, -1, 0, "", true, false);

    private static (CatalogAdminService Service, Recorder Refresher) Catalog()
    {
        var refresher = DispatchProxy.Create<ICatalogCacheRefresher, Recorder>();
        return (new CatalogAdminService(EditorTestSupport.UntouchableDatabase(), DispatchProxy.Create<ICatalogManager, Recorder>(),
            DispatchProxy.Create<IItemDataManager, Recorder>(), refresher, NullLogger<CatalogAdminService>.Instance), (Recorder)(object)refresher);
    }

    private static (FurniEditorService Service, Recorder Refresher) Furni()
    {
        var refresher = DispatchProxy.Create<ICatalogCacheRefresher, Recorder>();
        return (new FurniEditorService(EditorTestSupport.UntouchableDatabase(), new FurnidataStore(Options.Create(new FurniEditorConfiguration())),
            DispatchProxy.Create<IFurniEditorTextImporter, Recorder>(), refresher, DispatchProxy.Create<IGameClientManager, Recorder>(),
            NullLogger<FurniEditorService>.Instance), (Recorder)(object)refresher);
    }

    public static TheoryData<Habbo> Denied => new() { EditorTestSupport.Player(), EditorTestSupport.Staff(9, "mod_tool") };

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
        Assert.Throws<CatalogAdminRejected>(() => service.Revision(actor));
        Assert.Empty(refresher.Calls);
    }

    [Theory]
    [MemberData(nameof(Denied))]
    public async Task FurniEditorIsRefusedBeforeTouchingTheDatabase(Habbo actor)
    {
        var (service, refresher) = Furni();
        Assert.All(new[] { service.Update(actor, 1, "{}"), service.Delete(actor, 1), service.UpdateFurnidata(actor, 1, "{\"name\":\"x\"}"), service.RevertFurnidata(actor, 1) },
            result => Assert.Equal((false, "No permission"), (result.Success, result.Message)));
        Assert.Throws<FurniEditorRejected>(() => service.Search(actor, "", "", 1, "id", "asc"));
        Assert.Throws<FurniEditorRejected>(() => service.Detail(actor, 1));
        Assert.Throws<FurniEditorRejected>(() => service.DetailBySprite(actor, 1));
        Assert.Throws<FurniEditorRejected>(() => service.Interactions(actor));
        await Assert.ThrowsAsync<FurniEditorRejected>(() => service.ImportText(actor, 1));
        Assert.Empty(refresher.Calls);
    }

    [Fact]
    public void DestructiveFurniActionsNeedTheirOwnRight()
    {
        var (service, _) = Furni();
        var editorOnly = EditorTestSupport.Staff(9, EditorPermissions.CatalogFurni);
        Assert.False(service.Delete(editorOnly, 1).Success);
        Assert.False(service.UpdateFurnidata(editorOnly, 1, "{\"name\":\"x\"}").Success);
        Assert.False(service.RevertFurnidata(editorOnly, 1).Success);
        Assert.False(EditorPermissions.Allows(EditorTestSupport.Staff(9, EditorPermissions.FurniDelete), EditorPermissions.FurniDelete));
        Assert.True(EditorPermissions.Allows(EditorTestSupport.Staff(), EditorPermissions.FurniDelete));
        Assert.False(EditorPermissions.Allows(null));
    }

    [Fact]
    public void EveryEditorPacketHasAHeaderAndTheRevisionMapsIt()
    {
        var handlers = typeof(CatalogAdminSavePageEvent).Assembly.GetTypes()
            .Where(type => type.Namespace is "Plus.Communication.Packets.Incoming.Catalog.Admin" or "Plus.Communication.Packets.Incoming.FurniEditor"
                && type.IsAssignableTo(typeof(Plus.Communication.Packets.IPacketEvent)))
            .ToList();
        Assert.Equal(32, handlers.Count);
        var revision = System.Text.Json.JsonDocument.Parse(File.ReadAllText(HabbiconPacketTests.Repo("Resources/Revisions/OCTANE-3-6-0-FLOOR-20260909.json"))).RootElement;
        foreach (var handler in handlers)
        {
            var header = typeof(Plus.Communication.Packets.Incoming.ClientPacketHeader).GetField(handler.Name);
            Assert.NotNull(header);
            Assert.Equal((uint)header!.GetValue(null)!, revision.GetProperty("IncomingHeaders").GetProperty(handler.Name).GetUInt32());
        }
        foreach (var composer in new[] { "CatalogAdminResultComposer", "CatalogAdminOfferDetailsComposer", "CatalogAdminPageDetailsComposer",
                     "CatalogStudioSessionComposer", "CatalogStudioHistoryComposer",
                     "CatalogStudioOperationComposer", "CatalogStudioValidationComposer", "CatalogStudioDocumentResultComposer", "FurniEditorSearchResultComposer", "FurniEditorDetailResultComposer",
                     "FurniEditorInteractionsResultComposer", "FurniEditorResultComposer", "FurnitureDataReloadComposer", "FurniEditorImportTextResultComposer" })
        {
            var header = typeof(Plus.Communication.Packets.Outgoing.ServerPacketHeader).GetField(composer);
            Assert.Equal((uint)header!.GetValue(null)!, revision.GetProperty("OutgoingHeaders").GetProperty(composer).GetUInt32());
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
