using System.Data;
using System.Buffers.Binary;
using System.Reflection;
using System.Text;
using Plus.Communication.Flash;
using Plus.Communication.Packets.Incoming.Marketplace;
using Plus.Communication.Packets.Outgoing;
using Plus.HabboHotel.Catalog.Marketplace;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.DataFormat;
using Plus.HabboHotel.Users;
using Plus.HabboHotel.Users.Inventory;
using Plus.HabboHotel.Users.Inventory.Furniture;
using Xunit;

namespace Plus.Tests;

[Collection("Group purchase")]
public class MarketplaceListingTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);

    [Fact]
    public async Task OwnedFurniIsListedAndRemovedOnlyAfterTheStoreAccepts()
    {
        var store = new RecordingStore();
        var (habbo, item) = Owner(ItemType.Floor);
        store.BeforeWrite = () => store.OwnedAtWrite = habbo.Inventory.Furniture.GetItem(item.Id) != null;
        var (client, sent) = HabbiconTestSupport.Client(habbo);

        await Offer(store).Parse(client, Packet(100, 0, (int)item.Id));

        Assert.True(store.OwnedAtWrite);
        Assert.Null(habbo.Inventory.Furniture.GetItem(item.Id));
        Assert.Equal(new[] { ServerPacketHeader.FurniListRemoveComposer, ServerPacketHeader.MarketplaceMakeOfferResultComposer }, sent.Select(message => message.Header));
        var listing = Assert.Single(store.Listings);
        Assert.Equal(item.Id, listing.FurniId);
        Assert.Equal(7, listing.UserId);
        Assert.Equal(100, listing.AskingPrice);
        Assert.Equal(101, listing.TotalPrice);
        Assert.Equal("1", listing.ItemType);
        Assert.Equal("", listing.ExtraData);
        Assert.Equal(Now, listing.ListedAt);
    }

    [Fact]
    public async Task WallFurniIsListedAsTheSecondItemType()
    {
        var store = new RecordingStore();
        var (habbo, item) = Owner(ItemType.Wall);
        var (client, _) = HabbiconTestSupport.Client(habbo);

        await Offer(store).Parse(client, Packet(100, 0, (int)item.Id));

        Assert.Equal("2", Assert.Single(store.Listings).ItemType);
    }

    [Theory]
    [InlineData("unowned", 100)]
    [InlineData("zero", 0)]
    [InlineData("negative", -1)]
    [InlineData("over", 70000001)]
    public async Task IneligibleOffersAreRejectedBeforeAnyStoreWrite(string reason, int price)
    {
        var store = new RecordingStore();
        var (habbo, item) = Owner(ItemType.Floor);
        var itemId = reason == "unowned" ? item.Id + 1 : item.Id;
        var (client, sent) = HabbiconTestSupport.Client(habbo);

        await Offer(store).Parse(client, Packet(price, 0, (int)itemId));

        Assert.Empty(store.Listings);
        Assert.Equal(new[] { ServerPacketHeader.MarketplaceMakeOfferResultComposer }, sent.Select(message => message.Header));
        Assert.NotNull(habbo.Inventory.Furniture.GetItem(item.Id));
    }

    [Theory]
    [InlineData("foreign-owner")]
    [InlineData("trade-locked")]
    [InlineData("not-marketable")]
    [InlineData("closed-wallet")]
    public void FurniThatIsNotTheSellersToMarketIsRejected(string reason)
    {
        var store = new RecordingStore();
        var (habbo, item) = Owner(ItemType.Floor);
        if (reason == "foreign-owner") item.OwnerId = 8;
        if (reason == "trade-locked") item.Definition.AllowTrade = false;
        if (reason == "not-marketable") item.Definition.AllowMarketplaceSell = false;
        if (reason == "closed-wallet") typeof(Habbo).GetField("_disconnected", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(habbo, true);

        Assert.False(new MarketplaceListingService(store, Manager(), new FixedClock(Now)).TryList(habbo, item.Id, 100));

        Assert.Empty(store.Listings);
        Assert.NotNull(habbo.Inventory.Furniture.GetItem(item.Id));
    }

    [Fact]
    public void TotalPriceThatOverflowsIsRejected()
    {
        var store = new RecordingStore();
        var (habbo, item) = Owner(ItemType.Floor);
        var listing = new MarketplaceListingService(store, Manager(comission: int.MaxValue), new FixedClock(Now));

        Assert.False(listing.TryList(habbo, item.Id, 100));

        Assert.Empty(store.Listings);
    }

    [Fact]
    public async Task MaximumSellingPriceIsStillListed()
    {
        var store = new RecordingStore();
        var (habbo, item) = Owner(ItemType.Floor);
        var (client, _) = HabbiconTestSupport.Client(habbo);

        await Offer(store).Parse(client, Packet(70000000, 0, (int)item.Id));

        Assert.Equal(70700000, Assert.Single(store.Listings).TotalPrice);
    }

    [Fact]
    public async Task ForcedPersistenceFailurePublishesNothingAndKeepsTheFurni()
    {
        var store = new RecordingStore { Fail = true };
        var (habbo, item) = Owner(ItemType.Floor);
        var (client, sent) = HabbiconTestSupport.Client(habbo);

        await Assert.ThrowsAsync<InvalidOperationException>(() => Offer(store).Parse(client, Packet(100, 0, (int)item.Id)));

        Assert.NotNull(habbo.Inventory.Furniture.GetItem(item.Id));
        Assert.Empty(sent);
    }

    [Fact]
    public void RealStoreCommitsTheOfferAndFurniDeleteTogether()
    {
        var database = new GroupManagementTests.RecordingDatabase();
        var (habbo, item) = Owner(ItemType.Floor);
        var listing = new MarketplaceListingService(new MarketplaceOfferStore(database), Manager(), new FixedClock(Now));

        Assert.True(listing.TryList(habbo, item.Id, 100));

        Assert.Equal(new[] { "commit", "dispose" }, database.Transactions);
        Assert.Collection(database.Writes,
            write => Assert.StartsWith("INSERT INTO `catalog_marketplace_offers`", write.Sql),
            write => Assert.StartsWith("DELETE FROM `items`", write.Sql));
        Assert.Equal(7, database.Writes[0].Parameters["UserId"]);
        Assert.Equal(101, database.Writes[0].Parameters["TotalPrice"]);
    }

    [Fact]
    public void RealStoreRollsBackWhenTheOfferInsertFails()
    {
        var database = new GroupManagementTests.RecordingDatabase { FailInsert = true };
        var (habbo, item) = Owner(ItemType.Floor);
        var listing = new MarketplaceListingService(new MarketplaceOfferStore(database), Manager(), new FixedClock(Now));

        Assert.Throws<InvalidOperationException>(() => listing.TryList(habbo, item.Id, 100));

        Assert.DoesNotContain("commit", database.Transactions);
        Assert.DoesNotContain(database.Writes, write => write.Sql.StartsWith("DELETE FROM `items`", StringComparison.Ordinal));
        Assert.NotNull(habbo.Inventory.Furniture.GetItem(item.Id));
    }

    private static MakeOfferEvent Offer(IMarketplaceOfferStore store) =>
        new(new MarketplaceListingService(store, Manager(), new FixedClock(Now)));

    private static IMarketplaceManager Manager(int? comission = null) => CatalogSnapshotTestSupport.Proxy<IMarketplaceManager>((method, args) => method switch
    {
        "CalculateComissionPrice" => comission ?? Convert.ToInt32(Math.Ceiling((float)args[0]! / 100 * 1)),
        _ => throw new InvalidOperationException(method),
    });

    private static (Habbo Habbo, InventoryItem Item) Owner(ItemType type)
    {
        var item = new InventoryItem
        {
            Id = 41, OwnerId = 7, ExtraData = FurniObjectData.Empty, UniqueNumber = 3, UniqueSeries = 4,
            Definition = new ItemDefinition { Id = 900, SpriteId = 55, PublicName = "Rare Chair", ItemName = "chair", Type = type, AllowTrade = true, AllowMarketplaceSell = true },
        };
        var furniture = new FurnitureInventoryComponent(type == ItemType.Floor ? [item] : [], type == ItemType.Wall ? [item] : []);
        return (new Habbo { Id = 7, Username = "seller", Inventory = new InventoryComponent { Furniture = furniture } }, item);
    }

    private static FlashIncomingPacket Packet(params int[] values)
    {
        using var stream = new MemoryStream();
        foreach (var value in values)
        {
            var bytes = new byte[4];
            BinaryPrimitives.WriteInt32BigEndian(bytes, value);
            stream.Write(bytes);
        }
        return new FlashIncomingPacket { Buffer = stream.ToArray() };
    }

    private sealed class RecordingStore : IMarketplaceOfferStore
    {
        public List<MarketplaceListing> Listings { get; } = new();
        public bool Fail { get; set; }
        public bool OwnedAtWrite { get; set; }
        public Action? BeforeWrite { get; set; }

        public void ListFurni(MarketplaceListing listing)
        {
            BeforeWrite?.Invoke();
            if (Fail) throw new InvalidOperationException("forced persistence failure");
            Listings.Add(listing);
        }

        public int? ClaimSold(int userId, Func<int, bool> accepts) => throw new NotSupportedException();
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
