using System.Data;
using System.Reflection;
using System.Runtime.CompilerServices;
using Dapper;
using MySqlConnector;
using Plus.Communication.Packets.Incoming.Rooms.Settings;
using Plus.Communication.Packets.Outgoing;
using Plus.Communication.Packets.Outgoing.Rooms.Settings;
using Plus.Database;
using Plus.HabboHotel.Achievements;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.Instance;
using Plus.HabboHotel.Users;
using Xunit;

namespace Plus.Tests;

public sealed class RoomFilterServiceTests
{
    [Fact]
    public async Task HandlersDecodeAndDelegateOnly()
    {
        var service = new RecordingService();
        await new GetRoomFilterListEvent(service).Parse(null!, HabbiconTestSupport.Incoming());
        await new ModifyRoomFilterListEvent(service).Parse(null!, HabbiconTestSupport.Incoming(42, true, "blocked"));

        Assert.Equal(1, service.ShowCalls);
        Assert.Equal((42, true, "blocked"), service.Modification);
    }

    [Fact]
    public void ComposerCapturesSourceAndPreservesExactBytes()
    {
        var words = new List<string> { "one", "two" };
        var composer = new GetRoomFilterListComposer(words);
        words[0] = "changed";
        words.Add("three");
        var (client, sent) = HabbiconTestSupport.Client(new Habbo());

        client.Send(composer);

        var packet = Assert.Single(sent);
        Assert.Equal(ServerPacketHeader.GetRoomFilterListComposer, packet.Header);
        Assert.Equal(new byte[] { 0, 0, 0, 2, 0, 3, (byte)'o', (byte)'n', (byte)'e', 0, 3, (byte)'t', (byte)'w', (byte)'o' }, packet.Payload);
    }

    [Fact]
    public void ShowRequiresCurrentRoomRightsAndPublishesBeforeAchievement()
    {
        var world = new World();
        var order = new List<string>();
        world.Client.SendCallback = args => { order.Add("packet"); return true; };
        var achievements = Achievement(order);
        var service = new RoomFilterService(achievements);

        service.Show(world.Client);
        Assert.Equal(["packet", "achievement"], order);

        order.Clear();
        world.Room.OwnerName = "somebody-else";
        service.Show(world.Client);
        Assert.Empty(order);

        world.Room.OwnerName = world.Habbo.Username;
        world.Habbo.CurrentRoom = null;
        service.Show(world.Client);
        Assert.Empty(order);
    }

    [Fact]
    public void ModifyPreservesNoOpAndPersistenceBeforeMemoryContracts()
    {
        var store = new RecordingStore();
        var world = new World(store);
        var service = new RoomFilterService(Achievement([]));

        service.Modify(world.Client, 42, true, "new");
        Assert.Equal(["new"], world.Room.WordFilterList);
        Assert.Equal((42u, "new"), store.Added);

        store.Added = null;
        service.Modify(world.Client, 42, true, "new");
        Assert.Null(store.Added);
        Assert.Equal(["new"], world.Room.WordFilterList);

        service.Modify(world.Client, 42, false, "missing");
        Assert.Null(store.Removed);

        store.BeforeRemove = () => Assert.Equal(["new"], world.Room.WordFilterList);
        service.Modify(world.Client, 42, false, "new");
        Assert.Empty(world.Room.WordFilterList);
        Assert.Equal((42u, "new"), store.Removed);

        service.Modify(world.Client, 99, true, "wrong-room");
        Assert.Empty(world.Room.WordFilterList);
        world.Room.OwnerName = "somebody-else";
        service.Modify(world.Client, 42, true, "denied");
        Assert.Empty(world.Room.WordFilterList);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void FailedPersistenceDoesNotMutateOrPublish(bool add)
    {
        var store = new RecordingStore { Fail = true };
        var world = new World(store);
        if (!add) world.Room.WordFilterList.Add("existing");
        var service = new RoomFilterService(Achievement([]));

        Assert.Throws<InvalidOperationException>(() => service.Modify(world.Client, 42, add, add ? "new" : "existing"));
        Assert.Equal(add ? [] : ["existing"], world.Room.WordFilterList);
        Assert.Empty(world.Sent);
    }

    [RoomComponentDatabaseFact]
    public void StoreCommitsAddAndRemoveWithProductionConnectionOptions()
    {
        var root = Environment.GetEnvironmentVariable("ROOM_COMPONENT_DATABASE")!;
        using var connection = new MySqlConnection(root);
        connection.Open();
        var schema = "room_filter_" + Guid.NewGuid().ToString("N");
        connection.Execute($"CREATE DATABASE `{schema}`");
        try
        {
            var connectionString = new MySqlConnectionStringBuilder(root)
            {
                Database = schema,
                AllowZeroDateTime = true,
                ConvertZeroDateTime = true
            }.ConnectionString;
            using (var schemaConnection = new MySqlConnection(connectionString))
            {
                schemaConnection.Open();
                schemaConnection.Execute("CREATE TABLE room_filter (room_id INT UNSIGNED NOT NULL, word VARCHAR(100) NOT NULL, UNIQUE KEY room_word (room_id, word))");
            }
            var component = new RoomFilterComponent(new ProbeDatabase(connectionString));
            var room = (Room)RuntimeHelpers.GetUninitializedObject(typeof(Room));
            room.Id = 42;
            room.WordFilterList = [];
            component.Initiate(room);

            Assert.True(room.GetFilter().AddFilter("blocked"));
            using (var probe = new MySqlConnection(connectionString))
                Assert.Equal("blocked", probe.QuerySingle<string>("SELECT word FROM room_filter WHERE room_id = 42"));
            component.Initiated();
            Assert.Equal(["blocked"], room.WordFilterList);
            Assert.True(room.GetFilter().RemoveFilter("blocked"));
            using (var probe = new MySqlConnection(connectionString))
                Assert.Equal(0, probe.QuerySingle<int>("SELECT COUNT(*) FROM room_filter"));
        }
        finally
        {
            connection.Execute($"DROP DATABASE IF EXISTS `{schema}`");
        }
    }

    private sealed class World
    {
        public Room Room { get; }
        public Habbo Habbo { get; }
        public GameClient Client { get; }
        public List<(uint Header, byte[] Payload)> Sent { get; }

        public World(RecordingStore? store = null)
        {
            Room = (Room)RuntimeHelpers.GetUninitializedObject(typeof(Room));
            Room.Id = 42;
            Room.Type = "private";
            Room.OwnerName = "owner";
            Room.UsersWithRights = [];
            Room.WordFilterList = [];
            Room.SetFilter(new FilterComponent(Room, store ?? new RecordingStore()));
            Habbo = new() { Id = 7, Username = "owner", CurrentRoom = Room, Access = EditorTestSupport.Access([]) };
            (Client, Sent) = HabbiconTestSupport.Client(Habbo);
            Habbo.Client = Client;
        }
    }

    private sealed class RecordingStore : IRoomFilterStore
    {
        public bool Fail { get; init; }
        public (uint RoomId, string Word)? Added { get; set; }
        public (uint RoomId, string Word)? Removed { get; set; }
        public Action? BeforeRemove { get; set; }
        public void Add(uint roomId, string word)
        {
            if (Fail) throw new InvalidOperationException("forced add failure");
            Added = (roomId, word);
        }
        public void Remove(uint roomId, string word)
        {
            BeforeRemove?.Invoke();
            if (Fail) throw new InvalidOperationException("forced remove failure");
            Removed = (roomId, word);
        }
    }

    private sealed class RecordingService : IRoomFilterService
    {
        public int ShowCalls { get; private set; }
        public (int RoomId, bool Added, string Word)? Modification { get; private set; }
        public void Show(GameClient session) => ShowCalls++;
        public void Modify(GameClient session, int roomId, bool added, string word) => Modification = (roomId, added, word);
    }

    private static IAchievementManager Achievement(List<string> order)
    {
        var proxy = DispatchProxy.Create<IAchievementManager, AchievementProxy>();
        ((AchievementProxy)(object)proxy).Order = order;
        return proxy;
    }

    public class AchievementProxy : DispatchProxy
    {
        public List<string> Order { get; set; } = null!;
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == nameof(IAchievementManager.ProgressAchievement))
                Order.Add("achievement");
            return targetMethod?.ReturnType == typeof(bool) ? true : null;
        }
    }

    private sealed class ProbeDatabase(string connectionString) : IDatabase
    {
        public bool IsConnected() => true;
        public IDbConnection Connection() => new MySqlConnection(connectionString);
    }
}
