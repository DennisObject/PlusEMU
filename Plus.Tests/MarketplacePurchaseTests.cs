using System.Reflection;
using Plus.Communication.Flash;
using Plus.Communication.Packets.Incoming.Marketplace;
using Plus.Communication.Packets.Outgoing;
using Plus.Communication.Packets.Outgoing.Marketplace;
using Plus.HabboHotel.Catalog.Marketplace;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.DataFormat;
using Plus.HabboHotel.Users;
using Plus.HabboHotel.Users.Inventory;
using Plus.HabboHotel.Users.Inventory.Furniture;
using Xunit;

namespace Plus.Tests;

[Collection("Group purchase")]
public class MarketplacePurchaseTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);

    [Fact]
    public async Task BoughtOfferChargesOnceDeliversAndRecordsTheSale()
    {
        var store = new ClaimStore(Claim());
        var (client, sent) = HabbiconTestSupport.Client(Buyer(credits: 1000));
        var averages = new Dictionary<int, int>();
        var counts = new Dictionary<int, int>();

        await Buy(store, averages, counts).Parse(client, Packet(5));

        Assert.Equal(899, client.GetHabbo().Credits);
        Assert.Equal(900u, Assert.IsType<InventoryComponent>(client.GetHabbo().Inventory).Furniture.GetItem(77)!.Definition.Id);
        Assert.Equal(new[]
        {
            ServerPacketHeader.CreditBalanceComposer, ServerPacketHeader.FurniListNotificationComposer, ServerPacketHeader.PurchaseOKComposer,
            ServerPacketHeader.FurniListAddComposer, ServerPacketHeader.FurniListUpdateComposer, ServerPacketHeader.MarketplaceBuyOfferResultComposer,
        }, sent.Select(message => message.Header));
        Assert.Equal(new byte[] { 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 5 }, sent[^1].Payload);
        Assert.Equal(101, averages[55]);
        Assert.Equal(1, counts[55]);
        Assert.Equal(5, store.Requests.Single().OfferId);
    }

    [Fact]
    public async Task BuyerWithoutALoadedInventoryClaimsNothing()
    {
        var store = new ClaimStore(Claim());
        var buyer = Buyer(credits: 1000);
        buyer.Inventory = null;
        var (client, sent) = HabbiconTestSupport.Client(buyer);

        await Assert.ThrowsAsync<InvalidOperationException>(() => Buy(store, new(), new()).Parse(client, Packet(5)));

        Assert.Empty(store.Requests);
        Assert.Equal(1000, buyer.Credits);
        Assert.Empty(sent);
    }

    [Theory]
    [InlineData(MarketplacePurchaseRefusal.Sold, MarketplaceBuyResult.OfferGone)]
    [InlineData(MarketplacePurchaseRefusal.Expired, MarketplaceBuyResult.OfferGone)]
    [InlineData(MarketplacePurchaseRefusal.UnknownItem, MarketplaceBuyResult.OfferGone)]
    [InlineData(MarketplacePurchaseRefusal.NotFound, MarketplaceBuyResult.OfferGone)]
    [InlineData(MarketplacePurchaseRefusal.InvalidOffer, MarketplaceBuyResult.OfferGone)]
    [InlineData(MarketplacePurchaseRefusal.InsufficientCredits, MarketplaceBuyResult.NotEnoughCredits)]
    [InlineData(MarketplacePurchaseRefusal.OwnOffer, MarketplaceBuyResult.RefreshOffers)]
    public async Task RefusedOffersChangeNothingAndAnswerWithTheTypedResult(MarketplacePurchaseRefusal refusal, MarketplaceBuyResult result)
    {
        var store = new ClaimStore(refusal);
        var (client, sent) = HabbiconTestSupport.Client(Buyer(credits: 1000));

        await Buy(store, new(), new()).Parse(client, Packet(5));

        Assert.Equal(1000, client.GetHabbo().Credits);
        Assert.Null(Assert.IsType<InventoryComponent>(client.GetHabbo().Inventory).Furniture.GetItem(77));
        Assert.DoesNotContain(ServerPacketHeader.CreditBalanceComposer, sent.Select(message => message.Header));
        Assert.DoesNotContain(ServerPacketHeader.MarketPlaceOffersComposer, sent.Select(message => message.Header));

        var answer = Assert.Single(sent, message => message.Header == ServerPacketHeader.MarketplaceBuyOfferResultComposer);

        Assert.Equal(new byte[] { 0, 0, 0, (byte)result, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 5 }, answer.Payload);
    }

    [Fact]
    public async Task ASecondBuyOfTheSameOfferIsRefusedAndDeliversNothing()
    {
        var store = new ClaimStore(Claim(), MarketplacePurchaseRefusal.Sold);
        var (client, _) = HabbiconTestSupport.Client(Buyer(credits: 1000));
        var parser = Buy(store, new(), new());

        await parser.Parse(client, Packet(5));
        await parser.Parse(client, Packet(5));

        Assert.Equal(899, client.GetHabbo().Credits);
        Assert.Equal(2, store.Requests.Count);
    }

    [Fact]
    public async Task ForcedDeliveryFailureChargesNothingAndSendsNothing()
    {
        var store = new ClaimStore(new InvalidOperationException("forced delivery failure"));
        var (client, sent) = HabbiconTestSupport.Client(Buyer(credits: 1000));

        await Assert.ThrowsAsync<InvalidOperationException>(() => Buy(store, new(), new()).Parse(client, Packet(5)));

        Assert.Equal(1000, client.GetHabbo().Credits);
        Assert.Empty(sent);
    }

    [Fact]
    public void ClosedWalletIsNotOfferedToTheStore()
    {
        var store = new ClaimStore(Claim());
        var habbo = Buyer(credits: 1000);
        typeof(Habbo).GetField("_disconnected", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(habbo, true);
        var (client, sent) = HabbiconTestSupport.Client(habbo);

        Assert.Equal(MarketplacePurchaseOutcome.WalletClosed, Service(store, new(), new()).Buy(client, 5));

        Assert.Empty(store.Requests);
        Assert.Empty(sent);
    }

    private static BuyOfferEvent Buy(IMarketplacePurchaseStore store, Dictionary<int, int> averages, Dictionary<int, int> counts) =>
        new(Service(store, averages, counts));

    private static MarketplacePurchaseService Service(IMarketplacePurchaseStore store, Dictionary<int, int> averages, Dictionary<int, int> counts)
    {
        var definitions = new Dictionary<uint, ItemDefinition> { [900] = Definition() };
        var items = CatalogSnapshotTestSupport.Proxy<IItemDataManager>((method, _) => method == "get_Items" ? definitions : throw new InvalidOperationException(method));
        var marketplace = CatalogSnapshotTestSupport.Proxy<IMarketplaceManager>((method, _) => method switch
        {
            "get_MarketAverages" => averages,
            "get_MarketCounts" => counts,
            _ => throw new InvalidOperationException(method),
        });

        return new MarketplacePurchaseService(store, items, marketplace, new FixedClock(Now));
    }

    private static MarketplaceClaimedOffer Claim() => new(101, 55, new InventoryItem { Id = 77, OwnerId = 8, Definition = Definition(), ExtraData = FurniObjectData.Empty });

    private static ItemDefinition Definition() => new() { Id = 900, SpriteId = 55, PublicName = "Probe", ItemName = "probe", Type = ItemType.Floor, AllowTrade = true, AllowMarketplaceSell = true };

    private static Habbo Buyer(int credits) => new() { Id = 8, Username = "buyer", Credits = credits, Inventory = new InventoryComponent { Furniture = new FurnitureInventoryComponent([], []) } };

    private static FlashIncomingPacket Packet(int offerId) => new() { Buffer = new byte[] { 0, 0, 0, (byte)offerId } };

    private sealed class ClaimStore : IMarketplacePurchaseStore
    {
        private readonly Queue<object> _results = new();

        public ClaimStore(params object[] results)
        {
            foreach (var result in results) {
                _results.Enqueue(result);
            }
        }

        public List<MarketplacePurchaseRequest> Requests { get; } = new();

        public MarketplacePurchaseResult Claim(MarketplacePurchaseRequest request)
        {
            Requests.Add(request);
            var next = _results.Count > 0 ? _results.Dequeue() : Refusal(MarketplacePurchaseRefusal.Sold);

            return next switch
            {
                Exception error => throw error,
                MarketplacePurchaseRefusal refusal => Refusal(refusal),
                MarketplaceClaimedOffer offer => new MarketplacePurchaseResult(null, offer),
                _ => throw new InvalidOperationException("unexpected result"),
            };
        }

        private static MarketplacePurchaseResult Refusal(MarketplacePurchaseRefusal refusal) => new(refusal, null);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
