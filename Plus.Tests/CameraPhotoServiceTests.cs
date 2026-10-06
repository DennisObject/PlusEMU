using Microsoft.Extensions.Logging.Abstractions;
using Plus.Communication.Packets.Incoming.Camera;
using Plus.Communication.Packets.Outgoing;
using Plus.HabboHotel.Achievements;
using Plus.HabboHotel.Camera;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Quests;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Users;
using Plus.HabboHotel.Users.Inventory;
using Plus.HabboHotel.Users.Inventory.Furniture;
using Xunit;

namespace Plus.Tests;

public sealed class CameraPhotoServiceTests
{
    private static readonly Guid MediaId = Guid.Parse("aaaaaaaa-bbbb-4ccc-8ddd-eeeeeeeeeeee");

    [Fact]
    public async Task HandlersDecodeOnlyTheMediaIdAndDelegateMalformedInput()
    {
        var calls = new List<(string, Guid?)>();
        var service = CatalogSnapshotTestSupport.Proxy<ICameraPhotoService>((method, args) =>
        {
            calls.Add((method, (Guid?)args[1]));
            return null;
        });
        await new PurchasePhotoEvent(service).Parse(null!, HabbiconTestSupport.Incoming(MediaId.ToString("D")));
        await new PublishPhotoEvent(service).Parse(null!, HabbiconTestSupport.Incoming(MediaId.ToString("N")));
        await new PhotoCompetitionEvent(service).Parse(null!, HabbiconTestSupport.Incoming("invalid"));
        Assert.Equal(new[] { ("Purchase", (Guid?)MediaId), ("Publish", (Guid?)MediaId), ("EnterCompetition", (Guid?)null) }, calls);
    }

    [Fact]
    public void PurchasePublishesInventoryAndPacketsOnlyAfterCheckoutReturns()
    {
        var habbo = Actor();
        var (client, sent) = HabbiconTestSupport.Client(habbo);
        var item = new InventoryItem { Id = 10, Definition = new ItemDefinition { Id = 1, Type = ItemType.Floor } };
        var achievementCalls = 0;
        var service = Service((method, args) =>
        {
            Assert.Equal("Purchase", method);
            Assert.Same(habbo, args[0]);
            Assert.Empty(habbo.Inventory.Furniture.AllItems);
            Assert.Empty(sent);
            habbo.Credits = 95;
            return new CameraCheckoutResult(true, Item: item);
        }, (_, _) => { achievementCalls++; return false; });

        service.Purchase(client, MediaId);

        Assert.Same(item, habbo.Inventory.Furniture.GetItem(10));
        Assert.Equal(new uint[] { ServerPacketHeader.FurniListNotificationComposer, ServerPacketHeader.FurniListUpdateComposer,
            ServerPacketHeader.CreditBalanceComposer, ServerPacketHeader.HabboActivityPointNotificationComposer,
            ServerPacketHeader.CameraPurchaseOKComposer }, sent.Select(packet => packet.Header));
        Assert.Equal(1, achievementCalls);
    }

    [Fact]
    public void FailedCheckoutDoesNotPublishInventoryOrPurchaseConfirmation()
    {
        var habbo = Actor();
        var (client, sent) = HabbiconTestSupport.Client(habbo);
        var service = Service((_, _) => throw new InvalidOperationException("store failure"));

        service.Purchase(client, MediaId);

        Assert.Empty(habbo.Inventory.Furniture.AllItems);
        Assert.Equal(100, habbo.Credits);
        Assert.DoesNotContain(sent, packet => packet.Header == ServerPacketHeader.CameraPurchaseOKComposer);
        Assert.Single(sent);
    }

    [Fact]
    public void PublicationProgressesRewardsOnlyForAChangedSuccessfulResult()
    {
        var (client, sent) = HabbiconTestSupport.Client(Actor());
        var calls = 0;
        var changed = false;
        var service = Service((_, _) => new CameraCheckoutResult(true, Changed: changed), rewards: (_, _) => { calls++; return null; });

        service.Publish(client, MediaId);
        Assert.Equal(0, calls);
        Assert.Single(sent);
        sent.Clear();
        changed = true;
        service.Publish(client, MediaId);
        Assert.Equal(1, calls);
        Assert.Equal(new uint[] { ServerPacketHeader.CameraPublishStatusComposer, ServerPacketHeader.HabboActivityPointNotificationComposer },
            sent.Select(packet => packet.Header));
    }

    [Fact]
    public void MissingMediaOrCurrentRoomNeverReachesCheckout()
    {
        var habbo = Actor();
        var (client, _) = HabbiconTestSupport.Client(habbo);
        var checkoutCalls = 0;
        var service = Service((_, _) => { checkoutCalls++; return new CameraCheckoutResult(false); });
        service.EnterCompetition(client, null);
        habbo.CurrentRoom = null;
        service.EnterCompetition(client, MediaId);
        Assert.Equal(0, checkoutCalls);
        Assert.Empty(habbo.Inventory.Furniture.AllItems);
    }

    private static CameraPhotoService Service(Func<string, object?[], object?> checkout,
        Func<string, object?[], object?>? achievements = null, Func<string, object?[], object?>? rewards = null)
    {
        var camera = CatalogSnapshotTestSupport.Proxy<ICameraService>((method, args) =>
        {
            Assert.Equal("Checkout", method);
            Assert.Equal(MediaId, args[1]);
            return ((Func<CameraCheckoutMedia, CameraCheckoutResult>)args[2]!)(new(MediaId, 2, DateTimeOffset.UnixEpoch));
        });
        return new(camera, CatalogSnapshotTestSupport.Proxy<ICameraCheckoutService>(checkout),
            CatalogSnapshotTestSupport.Proxy<IAchievementManager>(achievements ?? ((_, _) => throw new NotSupportedException())),
            CatalogSnapshotTestSupport.Proxy<IRewardTrackManager>(rewards ?? ((_, _) => throw new NotSupportedException())),
            NullLogger<CameraPhotoService>.Instance);
    }

    private static Habbo Actor() => new()
    {
        Id = 1, Credits = 100, CurrentRoom = new Room(new RoomData { Id = 2 }, [], TestLogging.Navigation, TestLogging.Logger),
        Inventory = new InventoryComponent { Furniture = new FurnitureInventoryComponent([], []) }
    };
}
