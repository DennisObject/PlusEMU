using System.Buffers.Binary;
using System.Data;
using System.Reflection;
using System.Runtime.CompilerServices;
using Dapper;
using MySqlConnector;
using Plus.Communication.Flash;
using Plus.Communication.Packets;
using Plus.Communication.Packets.Incoming.FriendList;
using Plus.Communication.Packets.Outgoing;
using Plus.Communication.Packets.Outgoing.FriendList;
using Plus.Database;
using Plus.HabboHotel.Friends;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Users;
using Plus.HabboHotel.Users.Messenger;
using Xunit;

namespace Plus.Tests;

public sealed class MessengerNavigationServiceTests
{
    [Fact]
    public async Task HandlersDecodeOnlyTheirPrimitiveAndDelegateWithoutReadingAccountState()
    {
        var service = new RecordingNavigation();
        var session = new FlashGameClient(TestGameServer.Instance, new FlashPacketFactory(), TestLogging.GameClient);
        await new HabboSearchEvent(service).Parse(session, HabbiconTestSupport.Incoming("%Alice"));
        await new FollowFriendEvent(service).Parse(session, HabbiconTestSupport.Incoming(123));
        Assert.Equal(new[] { "search %Alice", "follow 123" }, service.Calls);
        service.Calls.Clear();
        await Assert.ThrowsAnyAsync<ArgumentException>(() => new FollowFriendEvent(service).Parse(session, HabbiconTestSupport.Incoming()));
        Assert.Empty(service.Calls);
    }

    [Fact]
    public void SearchNormalizesOnceAndKeepsFriendAndOtherOrderAndOnlineWireFields()
    {
        var (client, sent) = Client(new Habbo
        {
            Id = 1,
            Messenger = new HabboMessenger(
            new()
            {
                [3] = new MessengerBuddy { Id = 3 },
                [4] = new MessengerBuddy { Id = 4 }
            }, new(), [], TimeProvider.System)
        });
        var search = new RecordingSearch
        {
            Results = [new(2, "Other", "o", "look2", null), new(3, "Friend", "f", "look3", DateTimeOffset.FromUnixTimeSeconds(2200000000)),
                new(4, "OfflineFriend", "off", "look4", null)]
        };
        var lookups = new List<int>();
        var service = new MessengerNavigationService(search, Clients(id =>
        {
            lookups.Add(id);

            return id == 3 ? client : null;
        }));
        service.Search(client, "%Ali\n\u0001ce%");
        Assert.Equal(new[] { "Ali ce" }, search.Queries);
        Assert.Equal(new[] { 2, 3, 4 }, lookups);
        var wire = new FlashIncomingPacket { Buffer = Assert.Single(sent).Payload };
        Assert.Equal(ServerPacketHeader.HabboSearchResultComposer, sent[0].Header);
        Assert.Equal(2, wire.ReadInt());
        ReadSearchRow(wire, 3, "Friend", "f", true, "look3", "2200000000");
        ReadSearchRow(wire, 4, "OfflineFriend", "off", false, "", "0");
        Assert.Equal(1, wire.ReadInt());
        ReadSearchRow(wire, 2, "Other", "o", false, "", "0");
        Assert.False(wire.HasDataRemaining());
    }

    [Theory]
    [InlineData("")]
    [InlineData("%%%")]
    [InlineData("   ")]
    public void EmptyNormalizedSearchDoesNotQueryOrSend(string query)
    {
        var (client, sent) = Client(new Habbo { Id = 1 });
        var search = new RecordingSearch();
        var service = new MessengerNavigationService(search, Clients(_ => throw new InvalidOperationException()));
        service.Search(client, query);
        service.Search(client, new string('x', 101));
        Assert.Empty(search.Queries);
        Assert.Empty(sent);
    }

    [Fact]
    public void FollowPreservesSilentGatesUnavailableNoticeAndPreparedForward()
    {
        var (client, sent) = Client(new Habbo { Id = 1 });
        var (target, _) = Client(new Habbo { Id = 2 });
        var lookups = 0;
        var service = new MessengerNavigationService(new RecordingSearch(), Clients(id =>
        {
            lookups++;

            return id == 2 ? target : null;
        }));
        service.Follow(client, 0);
        service.Follow(client, 1);
        Assert.Equal(0, lookups);
        service.Follow(client, 3);
        Assert.Empty(sent);
        service.Follow(client, 2);
        var failed = new FlashIncomingPacket { Buffer = Assert.Single(sent).Payload };
        Assert.Equal(ServerPacketHeader.FollowFriendFailedComposer, sent[0].Header);
        Assert.Equal((int)FriendFollowError.Unavailable, failed.ReadInt());
        Assert.False(failed.HasDataRemaining());
        Assert.Equal((uint)0, client.GetHabbo().PendingFollowRoomId);
        sent.Clear();
        var room = (Room)RuntimeHelpers.GetUninitializedObject(typeof(Room));
        room.Id = 99;
        target.GetHabbo().CurrentRoom = room;
        client.GetHabbo().CurrentRoom = room;
        service.Follow(client, 2);
        Assert.Empty(sent);
        client.GetHabbo().CurrentRoom = null;
        service.Follow(client, 2);
        Assert.Equal((uint)99, client.GetHabbo().PendingFollowRoomId);
        Assert.Equal(ServerPacketHeader.RoomForwardComposer, Assert.Single(sent).Header);
        var forward = new FlashIncomingPacket { Buffer = sent[0].Payload };
        Assert.Equal(99, forward.ReadInt());
        Assert.False(forward.HasDataRemaining());
    }

    [RoomComponentDatabaseFact]
    public void SearchMaterializesNullableFractionalAndFutureUtcUnderProductionOptions()
    {
        var root = Environment.GetEnvironmentVariable("ROOM_COMPONENT_DATABASE")!;
        using var server = new MySqlConnection(root);
        server.Open();
        var schema = "task_refactor_tests_search_" + Guid.NewGuid().ToString("N");
        server.Execute($"CREATE DATABASE `{schema}`");

        try
        {
            var connectionString = new MySqlConnectionStringBuilder(root)
            {
                Database = schema,
                AllowZeroDateTime = true,
                ConvertZeroDateTime = true
            }.ConnectionString;
            using var connection = new MySqlConnection(connectionString);
            connection.Open();
            connection.Execute("CREATE TABLE users(id INT PRIMARY KEY,username VARCHAR(100),motto VARCHAR(100),look VARCHAR(100),last_online DATETIME(6) NULL); INSERT INTO users VALUES(1,'Alice','a','look1',NULL),(2,'Alice2','b','look2','2039-12-31 23:59:59.123456'),(3,'Bob','c','look3',NULL)");
            var search = new SearchResultFactory(new SearchDatabase(connectionString));
            var rows = search.GetSearchResult("Alice");
            Assert.Equal(2, rows.Count);
            var missing = Assert.Single(rows, row => row.UserId == 1);
            Assert.Null(missing.LastOnlineAt);
            var future = Assert.Single(rows, row => row.UserId == 2);
            Assert.Equal(DateTimeOffset.Parse("2039-12-31T23:59:59.123456Z"), future.LastOnlineAt);
            Assert.Equal("look2", future.Figure);
            Assert.Equal("b", future.Motto);
            Assert.Empty(search.GetSearchResult("nobody"));
        }
        finally { server.Execute($"DROP DATABASE `{schema}`"); }
    }

    private static void ReadSearchRow(FlashIncomingPacket wire, int id, string name, string motto, bool online, string look, string lastOnline)
    {
        Assert.Equal(id, wire.ReadInt());
        Assert.Equal(name, wire.ReadString());
        Assert.Equal(motto, wire.ReadString());
        Assert.Equal(online, wire.ReadBool());
        Assert.False(wire.ReadBool());
        Assert.Equal("", wire.ReadString());
        Assert.Equal(0, wire.ReadInt());
        Assert.Equal(look, wire.ReadString());
        Assert.Equal(lastOnline, wire.ReadString());
    }

    private static (FlashGameClient Client, List<(uint Header, byte[] Payload)> Sent) Client(Habbo habbo)
    {
        var (client, sent) = HabbiconTestSupport.Client(habbo);
        client.SendCallback = args =>
        {
            var bytes = args.MemoryBuffer.Span.Slice(args.Offset, args.Count).ToArray();
            sent.Add((BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(4, 2)), bytes[6..]));

            return true;
        };

        return (client, sent);
    }

    private static IGameClientManager Clients(Func<int, GameClient?> lookup)
    {
        var proxy = DispatchProxy.Create<IGameClientManager, ClientProxy>();
        ((ClientProxy)(object)proxy).Lookup = lookup;

        return proxy;
    }

    public class ClientProxy : DispatchProxy
    {
        public Func<int, GameClient?> Lookup = _ => throw new NotSupportedException();
        protected override object? Invoke(MethodInfo? method, object?[]? args) =>
            method!.Name == nameof(IGameClientManager.GetClientByUserId) ? Lookup((int)args![0]!) : throw new NotSupportedException(method.Name);
    }
    private sealed class RecordingSearch : ISearchResultFactory
    {
        public List<SearchResult> Results = [];
        public List<string> Queries = [];
        public List<SearchResult> GetSearchResult(string query)
        {
            Queries.Add(query);

            return Results;
        }
    }
    private sealed class RecordingNavigation : IMessengerNavigationService
    {
        public List<string> Calls = [];
        public void Search(GameClient session, string query) => Calls.Add("search " + query);
        public void Follow(GameClient session, int buddyId) => Calls.Add("follow " + buddyId);
    }
    private sealed class SearchDatabase(string connectionString) : IDatabase
    {
        public bool IsConnected() => true;
        public IDbConnection Connection() => new MySqlConnection(connectionString);
    }
}
