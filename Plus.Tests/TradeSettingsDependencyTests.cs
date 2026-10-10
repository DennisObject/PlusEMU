using System.Reflection;
using Plus.Communication.Flash;
using Plus.Communication.Packets.Outgoing;
using Plus.HabboHotel.Catalog.Marketplace;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Users.Inventory.Furniture;
using Xunit;

namespace Plus.Tests;

[Collection("Trade game fixture")]
public sealed class TradeSettingsDependencyTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CompletionUsesInjectedLiveRedemptionSettingWithGlobalUnavailable(bool redeem)
    {
        var settings = new TestRoomSettings(new() { ["trading.auto_exchange_redeemables"] = redeem ? "0" : "1" });
        using var fixture = new TradeConfirmationServiceTests.TradeFixture(settings);
        var alice = fixture.Join(1, 7);
        var bob = fixture.Join(2, 8);
        bob.Habbo.Credits = 7;
        var voucher = new InventoryItem
        {
            Id = 100,
            OwnerId = 1,
            Definition = new ItemDefinition
            {
                Type = ItemType.Floor,
                InteractionType = InteractionType.Exchange,
                SpriteId = 11,
                BehaviourData = 10
            }
        };
        Assert.True(Assert.IsType<Plus.HabboHotel.Users.Inventory.InventoryComponent>(alice.Habbo.Inventory).Furniture.AddItem(voucher));
        alice.Packets.Clear();
        bob.Packets.Clear();
        var trade = fixture.Start(alice, bob);
        trade.Users[0].OfferedItems.Add(voucher.Id, voucher);
        settings.Values["trading.auto_exchange_redeemables"] = redeem ? "1" : "0";
        var global = typeof(PlusEnvironment).GetField("_settingsManager", BindingFlags.Static | BindingFlags.NonPublic)!;
        var previous = global.GetValue(null);

        try {
            global.SetValue(null, null);
            trade.Finish();
        }
        finally {
            global.SetValue(null, previous);
        }

        Assert.Null(alice.Habbo.Inventory.Furniture.GetItem(voucher.Id));
        Assert.Equal(redeem ? 17 : 7, bob.Habbo.Credits);

        if (redeem) {
            Assert.Null(Assert.IsType<Plus.HabboHotel.Users.Inventory.InventoryComponent>(bob.Habbo.Inventory).Furniture.GetItem(voucher.Id));
            Assert.Equal(new uint[] { ServerPacketHeader.CreditBalanceComposer, ServerPacketHeader.TradingFinishComposer }, bob.Sent);
            var packet = new FlashIncomingPacket { Buffer = bob.Packets[0].Payload };
            Assert.Equal("17.0", packet.ReadString());
            Assert.False(packet.HasDataRemaining());
        }
        else {
            Assert.Same(voucher, Assert.IsType<Plus.HabboHotel.Users.Inventory.InventoryComponent>(bob.Habbo.Inventory).Furniture.GetItem(voucher.Id));
            Assert.Equal(new uint[] { ServerPacketHeader.FurniListAddComposer, ServerPacketHeader.FurniListNotificationComposer,
                ServerPacketHeader.TradingFinishComposer }, bob.Sent);
        }

        Assert.Equal(new uint[] { ServerPacketHeader.FurniListRemoveComposer, ServerPacketHeader.TradingFinishComposer }, alice.Sent);
        Assert.Equal(new[] { (1, 2, "100;", "") }, fixture.Store.Logged);
        Assert.False(fixture.Trading.TryGetTrade(trade.Id, out _));
    }

    [Fact]
    public void RecipientCanListReceivedItemWithoutReauthenticating()
    {
        using var fixture = new TradeConfirmationServiceTests.TradeFixture(new TestRoomSettings());
        var alice = fixture.Join(1, 7);
        var bob = fixture.Join(2, 8);
        var item = new InventoryItem
        {
            Id = 101,
            OwnerId = 1,
            Definition = new ItemDefinition
            {
                Id = 900,
                Type = ItemType.Floor,
                SpriteId = 11,
                PublicName = "Chair",
                AllowTrade = true,
                AllowMarketplaceSell = true
            }
        };
        Assert.True(alice.Habbo.Inventory.Furniture.AddItem(item));
        var trade = fixture.Start(alice, bob);
        trade.Users[0].OfferedItems.Add(item.Id, item);

        trade.Finish();

        var store = new SameSessionMarketplaceStore();
        Assert.True(new MarketplaceListingService(store, new MarketplaceFeePolicy(TestRoomSettings.Empty), TimeProvider.System, TestRoomSettings.Empty).TryList(bob.Habbo, [item.Id], 1, 50));
        Assert.Equal((uint)bob.Habbo.Id, item.OwnerId);
        Assert.Single(store.Listings);
    }

    [Fact]
    public void AnItemOfferedInAnOpenTradeCannotBeListedOnTheMarketplace()
    {
        using var fixture = new TradeConfirmationServiceTests.TradeFixture(new TestRoomSettings());
        var alice = fixture.Join(1, 7);
        var bob = fixture.Join(2, 8);
        var item = new InventoryItem
        {
            Id = 101,
            OwnerId = 1,
            Definition = new ItemDefinition { Id = 900, Type = ItemType.Floor, SpriteId = 11, PublicName = "Chair", AllowTrade = true, AllowMarketplaceSell = true }
        };
        Assert.True(alice.Habbo.Inventory.Furniture.AddItem(item));
        var trade = fixture.Start(alice, bob);
        trade.Users[0].OfferedItems.Add(item.Id, item);
        var store = new SameSessionMarketplaceStore();
        var listing = new MarketplaceListingService(store, new MarketplaceFeePolicy(TestRoomSettings.Empty), TimeProvider.System, TestRoomSettings.Empty);

        Assert.False(listing.TryList(alice.Habbo, [item.Id], 1, 50));
        Assert.Empty(store.Listings);

        trade.Users[0].OfferedItems.Remove(item.Id);

        Assert.True(listing.TryList(alice.Habbo, [item.Id], 1, 50));
    }

    private sealed class SameSessionMarketplaceStore : IMarketplaceOfferStore
    {
        public List<MarketplaceListing> Listings { get; } = [];
        public bool ListFurni(MarketplaceListing listing) => ListFurni([listing]);

        public bool ListFurni(IReadOnlyList<MarketplaceListing> listings)
        {
            Listings.AddRange(listings);

            return true;
        }

        public int? ClaimSold(int userId, Func<int, bool> accepts) => null;
    }
}
