using System.Collections.Immutable;
using System.Reflection;
using Plus.Communication.Packets.Outgoing.Catalog;
using Plus.Communication.Packets.Incoming.Catalog;
using Plus.Communication.Packets.Incoming.Navigator;
using Plus.Core.Settings;
using Plus.HabboHotel.Badges;
using Plus.HabboHotel.Friends;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.Chat.Filter;
using Plus.HabboHotel.Users;
using Plus.HabboHotel.Users.Inventory;
using Plus.HabboHotel.Users.Inventory.Badges;
using Xunit;

namespace Plus.Tests;

public sealed class RoomPromotionServiceTests
{
    private static readonly DateTimeOffset Now = new(2040, 12, 31, 23, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CatalogRoomsUseCanonicalOwnerLookupAndFreezeTheWireData()
    {
        var context = new Context();
        context.Data.Name = "captured";
        context.Service.ShowCatalogRooms(context.Client);
        Assert.Equal(7, context.Loader.LastOwnerId);
        Assert.Single(context.Sent);
        Assert.Equal(0, context.Store.Writes);
        var composer = new GetCatalogRoomPromotionComposer(
            ImmutableArray.Create(new PromotionRoomSnapshot(context.Data.Id, context.Data.Name)));
        var first = new HabbiconTestSupport.RecordingPacket();
        composer.Compose(first);
        Assert.Equal(new List<object> { true, 1, 42u, "captured", true }, first.Writes);
        context.Data.Name = "changed";
        var repeated = new HabbiconTestSupport.RecordingPacket();
        composer.Compose(repeated);
        Assert.Equal(first.Writes, repeated.Writes);
    }

    [Fact]
    public async Task NonOwnerAndMissingRoomDoNotPersistOrSend()
    {
        var context = new Context();
        context.Data.OwnerId = 99;
        await context.Service.Purchase(context.Client, new(42, "title", "details", 9));
        context.Service.Edit(context.Client, new(42, "title", "details"));
        context.Loader.Data = null;
        await context.Service.Purchase(context.Client, new(42, "title", "details", 9));
        Assert.Equal(0, context.Store.Writes);
        Assert.Equal(0, context.Clock.Reads);
        Assert.Empty(context.Sent);
    }

    [Fact]
    public async Task FailedPersistenceLeavesPromotionAndPacketsUnchanged()
    {
        var context = new Context();
        var existing = new RoomPromotion("old", "details", 3, Now, Now.AddMinutes(15), context.Clock);
        context.Data.Promotion = existing;
        context.Store.Fail = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => context.Service.Purchase(context.Client, new(42, "new", "new details", 9)));
        Assert.Throws<InvalidOperationException>(() => context.Service.Edit(context.Client, new(42, "edited", "edited details")));
        Assert.Same(existing, context.Data.Promotion);
        Assert.Equal("old", existing.Name);
        Assert.Equal(Now.AddMinutes(15), existing.ExpiresAt);
        Assert.Empty(context.Sent);
    }

    [Fact]
    public async Task PurchaseAndExtensionPublishAfterPersistenceUsingOneClockRead()
    {
        var context = new Context();
        context.Store.BeforeWrite = () =>
        {
            Assert.Null(context.Data.Promotion);
            Assert.Empty(context.Sent);
        };
        await context.Service.Purchase(context.Client, new(42, "title", "details", 9));
        var first = context.Data.Promotion;
        Assert.Equal(Now, first.StartedAt);
        Assert.Equal(Now.AddMinutes(120), first.ExpiresAt);
        Assert.Equal(9, first.CategoryId);
        Assert.Equal(1, context.Clock.Reads);
        Assert.Single(context.Sent);

        context.Store.BeforeWrite = () => Assert.Same(first, context.Data.Promotion);
        await context.Service.Purchase(context.Client, new(42, "extended", "details", 10));
        Assert.Equal(Now, context.Data.Promotion.StartedAt);
        Assert.Equal(Now.AddHours(4), context.Data.Promotion.ExpiresAt);
        Assert.Equal(10, context.Data.Promotion.CategoryId);
        Assert.Equal(2, context.Clock.Reads);
        Assert.Equal(2, context.Store.Writes);
    }

    [Fact]
    public void EditPersistsBeforeChangingTheExistingPromotion()
    {
        var context = new Context();
        var existing = new RoomPromotion("old", "details", 3, Now, Now.AddHours(1), context.Clock);
        context.Data.Promotion = existing;
        context.Store.BeforeWrite = () => Assert.Equal("old", existing.Name);
        context.Service.Edit(context.Client, new(42, "edited", "edited details"));
        Assert.Equal("edited", existing.Name);
        Assert.Equal("edited details", existing.Description);
        Assert.Equal(Now.AddHours(1), existing.ExpiresAt);
        Assert.Equal(0, context.Clock.Reads);
    }

    [Fact]
    public async Task HandlersDecodeEveryWireFieldAndDelegate()
    {
        var context = new Context();
        var capture = new CaptureService();
        await new GetCatalogRoomPromotionEvent(capture).Parse(context.Client, HabbiconTestSupport.Incoming());
        Assert.True(capture.CatalogRequested);
        await new PurchaseRoomAdEvent(capture).Parse(context.Client,
            HabbiconTestSupport.Incoming(7, 8, 42, "title", false, "details", 9));
        await new EditRoomPromotionEvent(capture).Parse(context.Client,
            HabbiconTestSupport.Incoming(42, "edited", "more details"));
        Assert.Equal(new PurchaseRoomPromotionRequest(42, "title", "details", 9), capture.PurchaseRequest);
        Assert.Equal(new EditRoomPromotionRequest(42, "edited", "more details"), capture.EditRequest);
    }

    private sealed class Context
    {
        public RoomData Data { get; } = new() { Id = 42, OwnerId = 7, OwnerName = "owner" };
        public Loader Loader { get; }
        public Store Store { get; } = new();
        public Clock Clock { get; } = new();
        public Plus.HabboHotel.GameClients.GameClient Client { get; }
        public List<(uint Header, byte[] Payload)> Sent { get; }
        public RoomPromotionService Service { get; }

        public Context()
        {
            Loader = new() { Data = Data };
            (Client, Sent) = HabbiconTestSupport.Client(new Habbo
            {
                Id = 7,
                Inventory = new InventoryComponent
                {
                    Badges = new BadgesInventoryComponent(new() { ["RADZZ"] = new("RADZZ", 0) })
                }
            });
            Service = new(Loader, Proxy<IRoomManager>((_, args) => { args[1] = null; return false; }), Store,
                Proxy<IWordFilterManager>((_, args) => args[0]),
                Proxy<ISettingsManager>((_, _) => "120"),
                Proxy<IBadgeManager>((_, _) => throw new InvalidOperationException("Unexpected badge write.")),
                Proxy<IMessengerDataLoader>((_, _) => null), Clock);
        }
    }

    private sealed class Loader : IRoomDataLoader
    {
        public RoomData? Data { get; set; }
        public bool TryGetData(uint roomId, out RoomData? data)
        {
            data = Data;

            return data != null;
        }
        public int? LastOwnerId { get; private set; }
        public List<RoomData> GetRoomsDataByOwnerSortByName(int ownerId)
        {
            LastOwnerId = ownerId;

            return Data == null ? [] : [Data];
        }
    }

    private sealed class Store : IRoomPromotionStore
    {
        public int Writes { get; private set; }
        public bool Fail { get; set; }
        public Action? BeforeWrite { get; set; }
        public void Save(uint roomId, int ownerId, RoomPromotion promotion) => Write();
        public void Edit(uint roomId, int ownerId, string name, string description) => Write();
        private void Write()
        {
            BeforeWrite?.Invoke();

            if (Fail) {
                throw new InvalidOperationException("Forced persistence failure.");
            }

            Writes++;
        }
    }

    private sealed class Clock : TimeProvider
    {
        public int Reads { get; private set; }
        public override DateTimeOffset GetUtcNow()
        {
            Reads++;

            return Now;
        }
    }

    private sealed class CaptureService : IRoomPromotionService
    {
        public bool CatalogRequested { get; private set; }
        public void ShowCatalogRooms(Plus.HabboHotel.GameClients.GameClient session) => CatalogRequested = true;
        public PurchaseRoomPromotionRequest? PurchaseRequest { get; private set; }
        public EditRoomPromotionRequest? EditRequest { get; private set; }
        public Task Purchase(Plus.HabboHotel.GameClients.GameClient session, PurchaseRoomPromotionRequest request)
        {
            PurchaseRequest = request;

            return Task.CompletedTask;
        }
        public void Edit(Plus.HabboHotel.GameClients.GameClient session, EditRoomPromotionRequest request) => EditRequest = request;
    }

    private static T Proxy<T>(Func<string, object?[], object?> callback) where T : class
    {
        var proxy = DispatchProxy.Create<T, CallbackProxy>();
        ((CallbackProxy)(object)proxy).Callback = callback;

        return proxy;
    }

    public class CallbackProxy : DispatchProxy
    {
        public Func<string, object?[], object?> Callback { get; set; } = null!;
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => Callback(targetMethod!.Name, args!);
    }
}
