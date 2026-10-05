using System.Data;
using System.Buffers.Binary;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Plus.Communication.Flash;
using Plus.Communication.Packets.Incoming.Marketplace;
using Plus.Communication.Packets.Outgoing.Marketplace;
using Plus.HabboHotel.Catalog.Marketplace;
using Plus.HabboHotel.Users;
using Xunit;

namespace Plus.Tests;

[Collection("Group purchase")]
public class MarketplaceOfferSearchTests
{
    // SHA-256 of the pre-migration GetOffers payloads (hex) for the scenarios below.
    private const string BaselinePayloadSha256 = "469dfb6d4672beb5dbf80088b825e95b5390c38fbeb7830e3c96a9e9d0165106";

    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);

    [Fact]
    public async Task ComposedOffersMatchPreMigrationBaseline()
    {
        var lines = new List<string>();
        foreach (var scenario in Scenarios())
        {
            var (database, manager) = Fixture(scenario.Rows);
            var (client, sent) = HabbiconTestSupport.Client(new Habbo { Id = 1, Username = "u" });
            await new GetOffersEvent(new MarketplaceOfferSearchService(database, manager, new FixedClock(Now))).Parse(client, Packet(scenario.Min, scenario.Max, scenario.Query, scenario.Mode));
            lines.Add($"{scenario.Name}: {Convert.ToHexString(sent.Single().Payload)}");
        }

        Assert.Equal(BaselinePayloadSha256, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Concat(lines.Select(line => line + "\n"))))));
    }

    [Theory]
    [InlineData(1, "DESC", -1, -1)]
    [InlineData(0, "ASC", 10, 500)]
    public async Task FilterModeChoosesFixedOrderAndBindsBounds(int mode, string order, int min, int max)
    {
        var (database, manager) = Fixture(Rows());
        var (client, _) = HabbiconTestSupport.Client(new Habbo { Id = 1, Username = "u" });

        await new GetOffersEvent(new MarketplaceOfferSearchService(database, manager, new FixedClock(Now))).Parse(client, Packet(min, max, "ignored", mode));

        var query = Assert.Single(database.OfferQueries);
        Assert.Contains($"ORDER BY `asking_price` {order} LIMIT 500", query.Sql);
        Assert.DoesNotContain(min.ToString(), query.Sql);
        Assert.Equal(min, query.Parameters["minCost"]);
        Assert.Equal(max, query.Parameters["maxCost"]);
        Assert.Equal(Now.AddSeconds(-172800), query.Parameters["threshold"]);
    }

    [Fact]
    public async Task SnapshotDoesNotFollowLaterMarketStateMutation()
    {
        var (database, manager) = Fixture(Rows());
        var items = (List<MarketOffer>)manager.MarketItems;
        var snapshot = new MarketplaceOfferSearchService(database, manager, new FixedClock(Now)).Search(-1, -1, "", 0);
        var before = Writes(snapshot);

        items.Clear();
        ((List<int>)manager.MarketItemKeys).Clear();
        items.Add(new MarketOffer(9, 900, 1, 1, 0, 0));

        Assert.Equal(before, Writes(snapshot));
        Assert.Equal(4, snapshot.Offers.Length);
    }

    private static List<object[]> Rows() =>
    [
        [1u, "1", 100, 50, 0, 0], [2u, "1", 100, 30, 0, 0], [3u, "1", 100, 30, 0, 0], [4u, "2", 200, 70, 0, 0],
        [5u, "1", 300, 10, 9, 4], [6u, "1", 300, 5, 3, 1], [1u, "1", 100, 1, 0, 0], [7u, "2", 200, 20, 0, 0],
    ];

    private static IEnumerable<(string Name, int Min, int Max, string Query, int Mode, List<object[]>? Rows)> Scenarios() =>
    [
        ("mixed-asc", -1, -1, "", 0, Rows()),
        ("mixed-desc-bounds", 10, 500, "x", 1, Rows()),
        ("empty", -1, -1, "", 0, []),
        ("null-table", -1, -1, "", 0, null),
    ];

    private static (GroupManagementTests.RecordingDatabase Database, IMarketplaceManager Manager) Fixture(List<object[]>? rows)
    {
        var database = new GroupManagementTests.RecordingDatabase { OfferRows = Table(rows) };
        var items = new List<MarketOffer>();
        var keys = new List<int>();
        var manager = Proxy<IMarketplaceManager>((method, args) => method switch
        {
            "get_MarketItems" => items,
            "get_MarketItemKeys" => keys,
            "AvgPriceForSprite" => (int)args[0]! * 2,
            _ => throw new InvalidOperationException(method),
        });
        return (database, manager);
    }

    private static DataTable Table(List<object[]>? rows)
    {
        var table = new DataTable();
        table.Columns.Add("OfferId", typeof(uint));
        table.Columns.Add("ItemType", typeof(string));
        table.Columns.Add("SpriteId", typeof(int));
        table.Columns.Add("TotalPrice", typeof(int));
        table.Columns.Add("LimitedNumber", typeof(int));
        table.Columns.Add("LimitedStack", typeof(int));
        foreach (var row in rows ?? [])
            table.Rows.Add(row);
        return table;
    }

    private static string Writes(MarketplaceOffersSnapshot snapshot)
    {
        var packet = new HabbiconTestSupport.RecordingPacket();
        new MarketPlaceOffersComposer(snapshot).Compose(packet);
        return string.Join("|", packet.Writes.Select(write => $"{write.GetType().Name}:{write}"));
    }

    private static FlashIncomingPacket Packet(params object[] values)
    {
        using var stream = new MemoryStream();
        foreach (var value in values)
        {
            if (value is int number)
            {
                var bytes = new byte[4];
                BinaryPrimitives.WriteInt32BigEndian(bytes, number);
                stream.Write(bytes);
            }
            else if (value is string text)
            {
                var raw = Encoding.UTF8.GetBytes(text);
                var length = new byte[2];
                BinaryPrimitives.WriteUInt16BigEndian(length, (ushort)raw.Length);
                stream.Write(length);
                stream.Write(raw);
            }
        }
        return new FlashIncomingPacket { Buffer = stream.ToArray() };
    }

    private static T Proxy<T>(Func<string, object?[], object?> call) where T : class => CatalogSnapshotTestSupport.Proxy<T>(call);

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
