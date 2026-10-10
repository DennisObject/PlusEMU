using System.Buffers.Binary;
using Plus.Communication.Flash;
using Plus.Communication.Packets.Incoming.Marketplace;
using Plus.Communication.Packets.Outgoing;
using Plus.Communication.Packets.Outgoing.Catalog;
using Plus.Core.Settings;
using Plus.HabboHotel.Catalog.Marketplace;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.DataFormat;
using Plus.HabboHotel.Users;
using Plus.HabboHotel.Users.Inventory;
using Plus.HabboHotel.Users.Inventory.Furniture;
using Xunit;

namespace Plus.Tests;

// What the official client (WIN63-202609161723) shows and sends for the marketplace: MarketplaceView.calculateFinalPrice for the seller price, the 11 fields of the configuration
// message, and MakeOfferMessageComposer (price, furniType, count, ids).
public class MarketplaceOfferWireTests
{
    [Theory]
    [InlineData(1, 1)]
    [InlineData(100, 1)]
    [InlineData(70000000, 1840871)]
    [InlineData(99999999, 3328307)]
    public void TheFeeFollowsTheClientFormulaWithItsDefaults(int price, int fee) => Assert.Equal(fee, Policy().Fee(price));

    [Fact]
    public void SettingsChangeTheFeeAndTheDivisorStaysPositive()
    {
        var policy = Policy(new() { ["catalog.marketplace.fee.percentage"] = "10", ["catalog.marketplace.fee.half_tax_limit"] = "0", ["catalog.marketplace.fee.revenue_limit"] = "-5" });

        Assert.Equal((10, 0, 1), (policy.SellingFeePercentage, policy.RevenueLimit, policy.HalfTaxLimit));
        Assert.Equal(13, Policy(new() { ["catalog.marketplace.fee.percentage"] = "10", ["catalog.marketplace.fee.half_tax_limit"] = "2000" }).Fee(100));
        Assert.Equal(100, Policy(new() { ["catalog.marketplace.fee.percentage"] = "250" }).SellingFeePercentage);
        Assert.Equal(1, Policy(new() { ["catalog.marketplace.fee.percentage"] = "garbage" }).SellingFeePercentage);
    }

    [Theory]
    [InlineData(99999999, 100, 1, int.MaxValue)]
    [InlineData(99999999, 100, 1000, int.MaxValue)]
    [InlineData(1, 100, 1, 2)]
    public void TheFeeSaturatesInsteadOfOverflowingAtTheExtremeSettings(int price, int percentage, int halfTaxLimit, int fee) =>
        Assert.Equal(fee, MarketplaceFeePolicy.Fee(price, percentage, halfTaxLimit));

    [Fact]
    public void TheConfigurationCarriesTheFeeTheClientReadsAfterTheAveragePricePeriod()
    {
        var packet = new HabbiconTestSupport.RecordingPacket();

        new MarketplaceConfigurationComposer(Policy(new() { ["catalog.marketplace.fee.percentage"] = "3", ["catalog.marketplace.fee.half_tax_limit"] = "50000", ["catalog.marketplace.fee.revenue_limit"] = "20" })).Compose(packet);

        Assert.Equal(new object[] { true, 3, 0, 0, 1, 99999999, 48, 7, 3, 20, 50000 }, packet.Writes);
    }

    [Fact]
    public async Task AnOfferOfSeveralIdenticalItemsIsListedTogetherAndEachLeavesTheInventory()
    {
        var store = new Store();
        var (habbo, items) = Owner(ItemType.Floor, 3);
        var (client, sent) = HabbiconTestSupport.Client(habbo);

        await Offer(store).Parse(client, Packet(100, 1, 3, 41, 42, 43));

        var batch = Assert.Single(store.Batches);
        Assert.Equal(new uint[] { 41, 42, 43 }, batch.Select(listing => listing.FurniId).ToArray());
        Assert.All(batch, listing => Assert.Equal((99, 100, "1"), (listing.AskingPrice, listing.TotalPrice, listing.ItemType)));
        Assert.All(items, item => Assert.Null(Assert.IsType<InventoryComponent>(habbo.Inventory).Furniture.GetItem(item.Id)));
        Assert.Equal(new[] { ServerPacketHeader.FurniListRemoveComposer, ServerPacketHeader.FurniListRemoveComposer, ServerPacketHeader.FurniListRemoveComposer, ServerPacketHeader.MarketplaceMakeOfferResultComposer },
            sent.Select(message => message.Header));
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(1, 501)]
    [InlineData(3, 1)]
    [InlineData(2, 1)]
    public async Task AnOfferThatDoesNotMatchWhatItClaimsListsNothing(int furniType, int count)
    {
        var store = new Store();
        var (habbo, _) = Owner(ItemType.Floor, 1);
        var (client, sent) = HabbiconTestSupport.Client(habbo);
        var ids = count == 1 ? new[] { 41 } : [];

        await Offer(store).Parse(client, Packet(new[] { 100, furniType, count }.Concat(count is 0 or > 500 ? [] : ids).ToArray()));

        Assert.Empty(store.Batches);
        Assert.Equal(new[] { ServerPacketHeader.MarketplaceMakeOfferResultComposer }, sent.Select(message => message.Header));
        Assert.NotNull(Assert.IsType<InventoryComponent>(habbo.Inventory).Furniture.GetItem(41));
    }

    [Fact]
    public async Task DifferentItemsDuplicatedIdsAndOthersItemsAreNotListedAsOneOffer()
    {
        var store = new Store();
        var (habbo, items) = Owner(ItemType.Floor, 2);
        items[1].Definition = new ItemDefinition { Id = 901, SpriteId = 56, PublicName = "Other", ItemName = "other", Type = ItemType.Floor, AllowTrade = true, AllowMarketplaceSell = true };
        var (client, _) = HabbiconTestSupport.Client(habbo);

        await Offer(store).Parse(client, Packet(100, 1, 2, 41, 42));
        await Offer(store).Parse(client, Packet(100, 1, 2, 41, 41));
        await Offer(store).Parse(client, Packet(100, 1, 2, 41, 99));

        Assert.Empty(store.Batches);
        Assert.All(items, item => Assert.NotNull(Assert.IsType<InventoryComponent>(habbo.Inventory).Furniture.GetItem(item.Id)));
    }

    [Fact]
    public async Task AFailedBatchWriteKeepsEveryItemInTheInventoryAndPublishesNothing()
    {
        var store = new Store { Fail = true };
        var (habbo, items) = Owner(ItemType.Floor, 2);
        var (client, sent) = HabbiconTestSupport.Client(habbo);

        await Assert.ThrowsAsync<InvalidOperationException>(() => Offer(store).Parse(client, Packet(100, 1, 2, 41, 42)));

        Assert.All(items, item => Assert.NotNull(Assert.IsType<InventoryComponent>(habbo.Inventory).Furniture.GetItem(item.Id)));
        Assert.Empty(sent);
    }

    private static MarketplaceFeePolicy Policy(Dictionary<string, string>? values = null) => new(new TestRoomSettings(values));

    private static MakeOfferEvent Offer(IMarketplaceOfferStore store) => new(new MarketplaceListingService(store, Policy(), TimeProvider.System, TestRoomSettings.Empty));

    private static (Habbo Habbo, InventoryItem[] Items) Owner(ItemType type, int count)
    {
        var definition = new ItemDefinition { Id = 900, SpriteId = 55, PublicName = "Chair", ItemName = "chair", Type = type, AllowTrade = true, AllowMarketplaceSell = true };
        var items = Enumerable.Range(0, count).Select(index => new InventoryItem { Id = (uint)(41 + index), OwnerId = 7, ExtraData = FurniObjectData.Empty, Definition = definition }).ToArray();
        var furniture = new FurnitureInventoryComponent(type == ItemType.Floor ? items : [], type == ItemType.Wall ? items : []);

        return (new Habbo { Id = 7, Username = "seller", Inventory = new InventoryComponent { Furniture = furniture } }, items);
    }

    private static FlashIncomingPacket Packet(params int[] values)
    {
        using var stream = new MemoryStream();

        foreach (var value in values) {
            var bytes = new byte[4];
            BinaryPrimitives.WriteInt32BigEndian(bytes, value);
            stream.Write(bytes);
        }

        return new FlashIncomingPacket { Buffer = stream.ToArray() };
    }

    private sealed class Store : IMarketplaceOfferStore
    {
        public List<MarketplaceListing[]> Batches { get; } = [];
        public bool Fail { get; set; }

        public bool ListFurni(MarketplaceListing listing) => ListFurni([listing]);

        public bool ListFurni(IReadOnlyList<MarketplaceListing> listings)
        {
            if (Fail) {
                throw new InvalidOperationException("forced persistence failure");
            }

            Batches.Add(listings.ToArray());

            return true;
        }

        public int? ClaimSold(int userId, Func<int, bool> accepts) => throw new NotSupportedException();
    }
}
