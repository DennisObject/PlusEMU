using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Plus.Communication.Packets.Incoming;
using Plus.Communication.Packets.Incoming.Habbicons;
using Plus.Communication.Packets.Outgoing;
using Plus.Communication.Packets.Outgoing.Catalog;
using Plus.Communication.Packets.Outgoing.FriendList;
using Plus.Communication.Packets.Outgoing.Habbicons;
using Plus.HabboHotel.Catalog;
using Plus.HabboHotel.Catalog.Utilities;
using Plus.HabboHotel.Users;
using Plus.HabboHotel.Habbicons;
using Xunit;
using static Plus.Tests.HabbiconTestSupport;

namespace Plus.Tests;

public class HabbiconPacketTests
{
    [Fact]
    public void ShopAndUserPayloadsMatchOctaneFieldOrderAndExcludeUnownedItems()
    {
        var snapshot = Snapshot();
        var shop = new RecordingPacket();
        new HabbiconShopDataComposer(snapshot).Compose(shop);
        Assert.Equal(new object[] { 1, 6, "toast", false, 71, 1, 40, 0, 0, 2,
            61, "toast_toast", 6, 3, 5, 2, 5, 62, "toast_happy", 6, 0, 5, 0, 0 }, shop.Writes);
        var user = new RecordingPacket();
        new UserHabbiconsComposer(snapshot).Compose(user);
        Assert.Equal(new object[] { 2, 61, 3, 71, 1, 2, 61, 28 }, user.Writes);
        var info = new RecordingPacket();
        new HabbiconInfoComposer(snapshot.RequireItem(61)).Compose(info);
        Assert.Equal(new object[] { 61, "toast_toast", 6, 3, 5, 2, 5 }, info.Writes);
    }

    [Fact]
    public void HabbiconComposersCopyNestedCollectionsDictionaryAndRecentIds()
    {
        var item = new HabbiconItem(61, "toast", 6, HabbiconState.Owned, 3, 5, 2);
        var members = new List<HabbiconItem> { item };
        var collections = new List<HabbiconCollection> { new(6, "collection", false, 71, 0, 1, 2, 5, members) };
        var items = new Dictionary<int, HabbiconItem> { [61] = item };
        var recent = new List<int> { 61 };
        var snapshot = new HabbiconSnapshot(collections, items, recent, []);
        var shop = new HabbiconShopDataComposer(snapshot);
        var user = new UserHabbiconsComposer(snapshot);
        var shopBefore = new RecordingPacket();
        shop.Compose(shopBefore);
        var userBefore = new RecordingPacket();
        user.Compose(userBefore);
        members.Clear();
        collections.Clear();
        items.Clear();
        recent.Clear();

        for (var i = 0; i < 2; i++)
        {
            var shopAfter = new RecordingPacket();
            shop.Compose(shopAfter);
            var userAfter = new RecordingPacket();
            user.Compose(userAfter);
            Assert.Equal(shopBefore.Writes, shopAfter.Writes);
            Assert.Equal(userBefore.Writes, userAfter.Writes);
        }
    }

    [Fact]
    public async Task InfoHandlerDelegatesAndMissingInfoDoesNotPublish()
    {
        var (client, sent) = Client(new Habbo { Id = 1 });
        var presentation = new RecordingPresentation();
        var packet = Incoming(61);
        await new GetHabbiconInfoEvent(presentation).Parse(client, packet);
        Assert.Equal(61, presentation.Info);
        Assert.False(packet.HasDataRemaining());
        Assert.Empty(sent);
        new HabbiconPresentationService(new Service(), NullLogger<HabbiconPresentationService>.Instance).ShowInfo(client, 999);
        Assert.Empty(sent);
    }

    private sealed class RecordingPresentation : IHabbiconPresentationService
    {
        public int? Info;
        public List<(int Category, IReadOnlyList<int>? Ids)> Resets { get; } = [];
        public void ResetUnseenItems(Plus.HabboHotel.GameClients.GameClient session, int category, IReadOnlyList<int> ids) => Resets.Add((category, ids));
        public void ResetUnseenCategory(Plus.HabboHotel.GameClients.GameClient session, int category) => Resets.Add((category, null));
        public void ShowInfo(Plus.HabboHotel.GameClients.GameClient session, int id) => Info = id;
        public void ShowShop(Plus.HabboHotel.GameClients.GameClient session) => throw new InvalidOperationException();
        public void Change(Plus.HabboHotel.GameClients.GameClient session, HabbiconAction action, int id) => throw new InvalidOperationException();
    }

    [Fact]
    public void StatusRoomAndUnseenPayloadsMatchClientParsers()
    {
        var status = new RecordingPacket();
        new UserHabbiconStatusChangedComposer(71, 1).Compose(status);
        Assert.Equal(new object[] { 71, 1 }, status.Writes);
        var room = new RecordingPacket();
        new RoomUseHabbiconComposer(8, 61).Compose(room);
        Assert.Equal(new object[] { 8, 61 }, room.Writes);
        var unseen = new RecordingPacket();
        new HabbiconUnseenComposer(new[] { 61, 71 }).Compose(unseen);
        Assert.Equal(new object[] { 1, 8, 2, 61, 71 }, unseen.Writes);
    }

    [Fact]
    public void CurrentHeadersAreMappedInEveryRevisionWithoutCollisions()
    {
        var expectedIncoming = new Dictionary<string, uint>
        {
            [nameof(TriggerHabbiconEvent)] = 9417,
            [nameof(GetHabbiconShopDataEvent)] = 9460,
            [nameof(GetHabbiconInfoEvent)] = 9461,
            [nameof(BuyHabbiconEvent)] = 9462,
            [nameof(BuyHabbiconCollectionEvent)] = 9463,
            [nameof(ClaimHabbiconEvent)] = 9464,
            [nameof(FavoriteHabbiconEvent)] = 9465,
            [nameof(UnfavoriteHabbiconEvent)] = 9466,
            [nameof(UnseenResetCategoryEvent)] = 3493,
            [nameof(UnseenResetItemsEvent)] = 2343,
            ["SendMessengerMessageEvent"] = 4902
        };
        var expectedOutgoing = new Dictionary<string, uint>
        {
            [nameof(RoomUseHabbiconComposer)] = 9410,
            [nameof(UserHabbiconsComposer)] = 9465,
            [nameof(UserHabbiconStatusChangedComposer)] = 9466,
            [nameof(HabbiconShopDataComposer)] = 9467,
            [nameof(HabbiconInfoComposer)] = 9463,
            [nameof(MessengerMessageAckComposer)] = 4902,
            [nameof(MessengerMessageFailedComposer)] = 4903,
            [nameof(MessengerMessageComposer)] = 4904
        };

        foreach (var (type, expected) in new[] { (typeof(ClientPacketHeader), expectedIncoming), (typeof(ServerPacketHeader), expectedOutgoing) })
        {
            foreach (var (name, id) in expected)
            {
                Assert.Equal(id, (uint)type.GetField(name)!.GetRawConstantValue()!);
            }

            var ids = type.GetFields(BindingFlags.Public | BindingFlags.Static).Select(f => (uint)f.GetRawConstantValue()!).Where(id => id > 0).ToArray();
            Assert.Equal(ids.Length, ids.Distinct().Count());
        }

        foreach (var file in Directory.GetFiles(Repo("Resources/Revisions"), "*.json"))
        {
            using var json = JsonDocument.Parse(File.ReadAllText(file));

            foreach (var (key, expected) in new[] { ("IncomingHeaders", expectedIncoming), ("OutgoingHeaders", expectedOutgoing) })
            {
                foreach (var (name, id) in expected)
                {
                    Assert.Equal(id, json.RootElement.GetProperty(key).GetProperty(name).GetUInt32());
                }
            }
        }
    }

    [Fact]
    public async Task ShopRequestSendsOwnedShopAndCategoryEightUnseenData()
    {
        var service = new Service();
        var (client, sent) = Client(new Habbo { Id = 1 });
        await new GetHabbiconShopDataEvent(new HabbiconPresentationService(service, NullLogger<HabbiconPresentationService>.Instance)).Parse(client, Incoming());
        Assert.Equal(new uint[] { ServerPacketHeader.UserHabbiconsComposer, ServerPacketHeader.FurniListNotificationComposer,
            ServerPacketHeader.HabbiconShopDataComposer }, sent.Select(p => p.Header));
    }

    [Theory]
    [InlineData(HabboHotel.Habbicons.HabbiconAction.Buy)]
    [InlineData(HabboHotel.Habbicons.HabbiconAction.BuyCollection)]
    [InlineData(HabboHotel.Habbicons.HabbiconAction.Claim)]
    [InlineData(HabboHotel.Habbicons.HabbiconAction.Favorite)]
    [InlineData(HabboHotel.Habbicons.HabbiconAction.Unfavorite)]
    public async Task ActionHandlersPublishStatusAndOnlyPurchasesWaitForPurchaseReplies(HabboHotel.Habbicons.HabbiconAction action)
    {
        var service = new Service();
        var (client, sent) = Client(new Habbo { Id = 1 });
        var typeName = action == HabboHotel.Habbicons.HabbiconAction.BuyCollection ? "BuyHabbiconCollection" : action + "Habbicon";
        var type = typeof(HabbiconRequest).Assembly.GetType("Plus.Communication.Packets.Incoming.Habbicons." + typeName + "Event")!;
        var handler = (HabbiconRequest)Activator.CreateInstance(type, new HabbiconPresentationService(service, NullLogger<HabbiconPresentationService>.Instance))!;
        await handler.Parse(client, Incoming(61));
        Assert.Equal((action, 61), Assert.Single(service.Actions));
        Assert.Equal(ServerPacketHeader.UserHabbiconStatusChangedComposer, sent[0].Header);
        bool purchase = action is HabboHotel.Habbicons.HabbiconAction.Buy or HabboHotel.Habbicons.HabbiconAction.BuyCollection or HabboHotel.Habbicons.HabbiconAction.Claim;
        Assert.Equal(purchase, sent.Any(p => p.Header == ServerPacketHeader.PurchaseOKComposer));
        sent.Clear();
        service.Rejection = 3;
        await handler.Parse(client, Incoming(61));

        if (purchase)
        {
            Assert.Equal(ServerPacketHeader.PurchaseErrorComposer, Assert.Single(sent).Header);
            Assert.Equal(3, new Communication.Flash.FlashIncomingPacket { Buffer = sent[0].Payload }.ReadInt());
        }
        else
        {
            Assert.Empty(sent);
        }
    }

    [Fact]
    public async Task UnseenResetsIgnoreOtherCategoriesEmptyOversizedAndTruncatedLists()
    {
        var service = new Service();
        var (client, _) = Client(new Habbo { Id = 1 });
        var presentation = new HabbiconPresentationService(service, NullLogger<HabbiconPresentationService>.Instance);
        var items = new UnseenResetItemsEvent(presentation);
        await items.Parse(client, Incoming(4, 1, 61));
        await items.Parse(client, Incoming(8, 0));
        await items.Parse(client, Incoming(8, 1001));
        await items.Parse(client, Incoming(8, 2, 61));
        Assert.Empty(service.Clears);
        await items.Parse(client, Incoming(8, 3, 61, 61, 71));
        Assert.Equal(new[] { 61, 71 }, Assert.Single(service.Clears));
        await new UnseenResetCategoryEvent(presentation).Parse(client, Incoming(4));
        Assert.Single(service.Clears);
        await new UnseenResetCategoryEvent(presentation).Parse(client, Incoming(8));
        Assert.Empty(service.Clears[1]);
    }

    [Fact]
    public async Task UnseenHandlersFullyDecodeAndDelegateCategoryPolicy()
    {
        var (client, sent) = Client(new Habbo { Id = 1 });
        var presentation = new RecordingPresentation();
        var packet = Incoming(4, 3, 61, 61, 71);
        await new UnseenResetItemsEvent(presentation).Parse(client, packet);
        Assert.False(packet.HasDataRemaining());
        Assert.Equal(4, presentation.Resets[0].Category);
        Assert.Equal(new[] { 61, 61, 71 }, presentation.Resets[0].Ids);
        var category = Incoming(4);
        await new UnseenResetCategoryEvent(presentation).Parse(client, category);
        Assert.False(category.HasDataRemaining());
        Assert.Equal((4, (IReadOnlyList<int>?)null), presentation.Resets[1]);
        Assert.Empty(sent);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void HabiconOfferHasOneProductWithoutFurnitureDefinitionOrGiftAndBulkOptions(bool haveOffer)
    {
        var item = new CatalogItem
        {
            Id = 10,
            OfferId = 12,
            CatalogName = "toast_toast",
            HabbiconId = 61,
            CostCredits = 5,
            Amount = 1,
            HaveOffer = haveOffer,
            Badge = "ignored",
            Definition = null!
        };
        var packet = new RecordingPacket();
        new CatalogOfferComposer(CatalogSnapshotTestSupport.Snapshots().CaptureOffer(item)).Compose(packet);
        Assert.Equal(new object[] { 12, "toast_toast", false, 5, 0, 0, false, 1,
            "habbicon", 61, "61", 1, false, 0, false, false, "", "", haveOffer }, packet.Writes);
        Assert.False(ItemUtility.CanGiftItem(item));
        Assert.False(ItemUtility.CanSelectAmount(item));
    }

    [Fact]
    public void MessengerHabiconPayloadsMatchAcknowledgementFailureAndTypedMessageParsers()
    {
        var ack = new RecordingPacket();
        new MessengerMessageAckComposer(2, 9, 123).Compose(ack);
        Assert.Equal(new object[] { 2, 0, 9, 123 }, ack.Writes);
        var fail = new RecordingPacket();
        new MessengerMessageFailedComposer(2, 6).Compose(fail);
        Assert.Equal(new object[] { 2, 6 }, fail.Writes);
        var message = new RecordingPacket();
        new MessengerMessageComposer(9, 4, 61, 123).Compose(message);
        Assert.Equal(new object[] { 0, 9, 4, 4, "61", "", 123 }, message.Writes);
    }

    [Theory]
    [InlineData(0, 2, 4, "61", "", 6)] // Nonfriend.
    [InlineData(9, 2, 4, "61", "", 1)] // Unsupported group conversation.
    [InlineData(0, 2, 4, "unknown", "", 1)]
    [InlineData(0, 1, 4, "61", "", 1)] // Cannot send to self.
    [InlineData(0, 2, 1, "hello", "", 1)] // Legacy text uses its original header.
    public async Task InvalidDirectMessengerRequestsFailWithoutRecentUse(int conversation, int recipient, int type, string text, string metadata, int error)
    {
        var service = new Service();
        var habbo = new Habbo { Id = 1, Messenger = new HabboHotel.Users.Messenger.HabboMessenger(new(), new(), new(), new FixedTimeProvider(FixedTimeProvider.Epoch)) };
        var (client, sent) = Client(habbo);
        var handler = new Plus.Communication.Packets.Incoming.FriendList.SendMessengerMessageEvent(new Plus.HabboHotel.Friends.HabbiconMessengerService(service, null!,
            new Plus.HabboHotel.Friends.HabbiconMessengerStore(null!), NullLogger<Plus.HabboHotel.Friends.HabbiconMessengerService>.Instance,
            new FixedTimeProvider(FixedTimeProvider.Epoch)));
        await handler.Parse(client, Incoming(conversation, recipient, 7, type, text, metadata));
        Assert.Equal(ServerPacketHeader.MessengerMessageFailedComposer, Assert.Single(sent).Header);
        var payload = new Communication.Flash.FlashIncomingPacket { Buffer = sent[0].Payload };
        Assert.Equal(7, payload.ReadInt());
        Assert.Equal(error, payload.ReadInt());
        Assert.Empty(service.Used);
    }

    internal static string Repo(string path) => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", path));
}
