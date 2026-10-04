using System.Data;
using System.Reflection;
using System.Text;
using Plus.Communication.Flash;
using Plus.Communication.Packets.Incoming.Marketplace;
using Plus.Communication.Packets.Outgoing.Marketplace;
using Plus.Database;
using Plus.Database.Interfaces;
using Plus.HabboHotel;
using Plus.HabboHotel.Catalog;
using Plus.HabboHotel.Catalog.Marketplace;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Users;
using Xunit;

namespace Plus.Tests;

[Collection("Group purchase")]
public class MarketplaceOfferSearchTests
{
    // SHA-256 of the pre-migration GetOffers payloads and SQL for the scenarios below.
    private const string BaselineSha256 = "43be31a5e8b894121daf0bf7e0a7d6df0450c9842d2e13ac0fda4d19fbfc7a4a";

    [Fact]
    public async Task ComposedOffersMatchPreMigrationBaseline()
    {
        var gameField = typeof(PlusEnvironment).GetField("_game", BindingFlags.Static | BindingFlags.NonPublic)!;
        var previous = gameField.GetValue(null);
        var sb = new StringBuilder();
        try
        {
            var items = new List<MarketOffer>();
            var keys = new List<int>();
            var manager = Proxy<IMarketplaceManager>((m, args) => m switch
            {
                "get_MarketItems" => items,
                "get_MarketItemKeys" => keys,
                "FormatTimestampString" => "1000",
                "AvgPriceForSprite" => (int)args[0]! * 2,
                _ => throw new InvalidOperationException(m),
            });
            var catalog = Proxy<ICatalogManager>((m, _) => m == "get_Marketplace" ? manager : throw new InvalidOperationException(m));
            gameField.SetValue(null, Proxy<IGame>((m, _) => m == "get_Catalog" ? catalog : throw new InvalidOperationException(m)));

            var rows = new object[][]
            {
                [1, 1, 100, 50, 0, 0], [2, 1, 100, 30, 0, 0], [3, 1, 100, 30, 0, 0], [4, 2, 200, 70, 0, 0],
                [5, 1, 300, 10, 9, 4], [6, 1, 300, 5, 3, 1], [1, 1, 100, 1, 0, 0], [7, 2, 200, 20, 0, 0],
            };
            var scenarios = new (string Name, int Min, int Max, string Query, int Mode, object[][]? Rows)[]
            {
                ("mixed-asc", -1, -1, "", 0, rows),
                ("mixed-desc-bounds", 10, 500, "x", 1, rows),
                ("empty", -1, -1, "", 0, []),
                ("null-table", -1, -1, "", 0, null),
            };
            foreach (var (name, min, max, query, mode, data) in scenarios)
            {
                items.Clear();
                keys.Clear();
                var sql = new List<string>();
                var database = Proxy<IDatabase>((m, _) => m == "GetQueryReactor" ? Adapter(sql, data) : throw new InvalidOperationException(m));
                var (client, sent) = HabbiconTestSupport.Client(new Habbo { Id = 1, Username = "u" });
                var packet = Packet(min, max, query, mode);
                await new GetOffersEvent(new MarketplaceOfferSearchService(database, manager)).Parse(client, packet);
                sb.AppendLine($"{name}: {string.Join(" ## ", sql)}");
                sb.AppendLine($"{name}: {Convert.ToHexString(sent.Single().Payload)}");
            }
            Assert.Equal(BaselineSha256, Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()))));
        }
        finally
        {
            gameField.SetValue(null, previous);
        }
    }

    [Fact]
    public void SnapshotDoesNotFollowLaterMarketStateMutation()
    {
        var items = new List<MarketOffer>();
        var keys = new List<int>();
        var manager = Proxy<IMarketplaceManager>((m, args) => m switch
        {
            "get_MarketItems" => items,
            "get_MarketItemKeys" => keys,
            "FormatTimestampString" => "1000",
            "AvgPriceForSprite" => (int)args[0]! * 2,
            _ => throw new InvalidOperationException(m),
        });
        var rows = new object[][] { [1, 1, 100, 50, 0, 0], [2, 1, 100, 30, 0, 0], [5, 1, 300, 10, 9, 4] };
        var snapshot = new MarketplaceOfferSearchService(Database(new List<string>(), rows), manager).Search(-1, -1, "", 0);
        var before = Writes(snapshot);

        items.Clear();
        keys.Clear();
        items.Add(new MarketOffer(9, 900, 1, 1, 0, 0));

        Assert.Equal(before, Writes(snapshot));
        Assert.Equal(2, snapshot.Offers.Length);
    }

    private static string Writes(MarketplaceOffersSnapshot snapshot)
    {
        var packet = new HabbiconTestSupport.RecordingPacket();
        new MarketPlaceOffersComposer(snapshot).Compose(packet);
        return string.Join("|", packet.Writes.Select(write => $"{write.GetType().Name}:{write}"));
    }

    private static IDatabase Database(List<string> sql, object[][]? data) =>
        Proxy<IDatabase>((m, _) => m == "GetQueryReactor" ? Adapter(sql, data) : throw new InvalidOperationException(m));

    private static IQueryAdapter Adapter(List<string> sql, object[][]? data) => Proxy<IQueryAdapter>((m, args) => m switch
    {
        "SetQuery" => Record(sql, (string)args[0]!),
        "AddParameter" => null,
        "GetTable" => Table(data),
        "Dispose" => null,
        _ => throw new InvalidOperationException(m),
    });

    private static object? Record(List<string> sql, string query)
    {
        sql.Add(query);
        return null;
    }

    private static DataTable? Table(object[][]? data)
    {
        if (data == null) return null;
        var table = new DataTable();
        foreach (var column in new[] { "offer_id", "item_type", "sprite_id", "total_price", "limited_number", "limited_stack" })
            table.Columns.Add(column, typeof(int));
        foreach (var row in data) table.Rows.Add(row);
        return table;
    }

    private static FlashIncomingPacket Packet(params object[] values)
    {
        using var stream = new MemoryStream();
        foreach (var value in values)
        {
            if (value is int number)
            {
                var bytes = new byte[4];
                System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(bytes, number);
                stream.Write(bytes);
            }
            else if (value is string text)
            {
                var raw = Encoding.UTF8.GetBytes(text);
                var length = new byte[2];
                System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian(length, (ushort)raw.Length);
                stream.Write(length);
                stream.Write(raw);
            }
        }
        return new FlashIncomingPacket { Buffer = stream.ToArray() };
    }

    private static T Proxy<T>(Func<string, object?[], object?> call) where T : class
    {
        var proxy = System.Reflection.DispatchProxy.Create<T, TestProxy>();
        ((TestProxy)(object)proxy).Call = call;
        return proxy;
    }

    public class TestProxy : System.Reflection.DispatchProxy
    {
        public Func<string, object?[], object?> Call = null!;
        protected override object? Invoke(MethodInfo? method, object?[]? args) => Call(method!.Name, args!);
    }
}
