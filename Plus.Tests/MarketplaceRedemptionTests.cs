using System.Data;
using System.Buffers.Binary;
using System.Reflection;
using Plus.Communication.Flash;
using Plus.Communication.Packets.Incoming.Marketplace;
using Plus.Communication.Packets.Outgoing;
using Plus.HabboHotel.Catalog.Marketplace;
using Plus.HabboHotel.Users;
using Xunit;

namespace Plus.Tests;

[Collection("Group purchase")]
public class MarketplaceRedemptionTests
{
    [Fact]
    public void SoldOffersArePaidOnceAndDuplicateRedeemsPayNothing()
    {
        var store = new SoldStore(25, 5);
        var (client, sent) = HabbiconTestSupport.Client(new Habbo { Id = 7, Username = "seller", Credits = 1000 });

        Redeem(store).Parse(client, Packet());
        Redeem(store).Parse(client, Packet());

        Assert.Equal(1030, client.GetHabbo().Credits);
        Assert.Single(sent, message => message.Header == ServerPacketHeader.CreditBalanceComposer);
        Assert.Empty(store.Sold);
    }

    [Fact]
    public void CapacityExceededLeavesTheSalesQueuedAndTheWalletUntouched()
    {
        var store = new SoldStore(25);
        var (client, sent) = HabbiconTestSupport.Client(new Habbo { Id = 7, Username = "seller", Credits = int.MaxValue - 10 });

        Redeem(store).Parse(client, Packet());

        Assert.Equal(int.MaxValue - 10, client.GetHabbo().Credits);
        Assert.Empty(sent);
        Assert.Single(store.Sold);
    }

    [Fact]
    public void ClosedWalletLeavesTheSalesQueuedForTheNextLogin()
    {
        var store = new SoldStore(25);
        var habbo = new Habbo { Id = 7, Username = "seller", Credits = 1000 };
        typeof(Habbo).GetField("_disconnected", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(habbo, true);
        var (client, sent) = HabbiconTestSupport.Client(habbo);

        Redeem(store).Parse(client, Packet());

        Assert.Equal(1000, client.GetHabbo().Credits);
        Assert.Empty(sent);
        Assert.Single(store.Sold);
    }

    [Fact]
    public void RealStoreCommitsTheClaimAndDeletesOnlyTheClaimedSales()
    {
        var database = new GroupManagementTests.RecordingDatabase { OfferRows = Sold(10u, 25, 11u, 5) };
        var (client, sent) = HabbiconTestSupport.Client(new Habbo { Id = 7, Username = "seller", Credits = 1000 });

        Redeem(new MarketplaceOfferStore(database)).Parse(client, Packet());

        Assert.Equal(1030, client.GetHabbo().Credits);
        Assert.Equal(new[] { "commit", "dispose" }, database.Transactions);
        var delete = Assert.Single(database.Writes);
        Assert.StartsWith("DELETE FROM `catalog_marketplace_offers`", delete.Sql);
        Assert.Single(sent);
    }

    [Fact]
    public void RealStoreRollsBackWhenTheWalletCannotHoldTheTotal()
    {
        var database = new GroupManagementTests.RecordingDatabase { OfferRows = Sold(10u, 25) };
        var (client, sent) = HabbiconTestSupport.Client(new Habbo { Id = 7, Username = "seller", Credits = int.MaxValue - 10 });

        Redeem(new MarketplaceOfferStore(database)).Parse(client, Packet());

        Assert.Equal(int.MaxValue - 10, client.GetHabbo().Credits);
        Assert.Contains("rollback", database.Transactions);
        Assert.DoesNotContain("commit", database.Transactions);
        Assert.Empty(database.Writes);
        Assert.Empty(sent);
    }

    private static RedeemOfferCreditsEvent Redeem(IMarketplaceOfferStore store) =>
        new(new MarketplaceRedemptionService(store));

    private static FlashIncomingPacket Packet() => new() { Buffer = Array.Empty<byte>() };

    private static DataTable Sold(params object[] values)
    {
        var table = new DataTable();
        table.Columns.Add("OfferId", typeof(uint));
        table.Columns.Add("AskingPrice", typeof(int));
        for (var i = 0; i < values.Length; i += 2)
            table.Rows.Add(values[i], values[i + 1]);
        return table;
    }

    private sealed class SoldStore(params int[] prices) : IMarketplaceOfferStore
    {
        public List<int> Sold { get; } = prices.ToList();

        public void ListFurni(MarketplaceListing listing) => throw new NotSupportedException();

        public int? ClaimSold(int userId, Func<int, bool> accepts)
        {
            var owed = Sold.Sum();
            if (!accepts(owed)) return null;
            Sold.Clear();
            return owed;
        }
    }
}
