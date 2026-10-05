using System.Collections.Concurrent;
using System.Data;
using System.Reflection;
using System.Runtime.CompilerServices;
using Dapper;
using MySqlConnector;
using Plus.Communication.Flash;
using Plus.Communication.Packets.Outgoing;
using Plus.Database;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.Wired;
using Plus.HabboHotel.Items.Wired.Boxes.Effects;
using Plus.HabboHotel.Items.Wired.Configuration;
using Plus.HabboHotel.Items.Wired.Modern.Actions;
using Plus.HabboHotel.Items.Wired.Runtime;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.AI;
using Plus.HabboHotel.Users;
using Xunit;

namespace Plus.Tests;

public sealed class WiredBotAppearancePersistenceTests
{
    [Fact]
    public void ModernActionPersistsBeforePublishingNewAppearance()
    {
        using var fixture = new Fixture();
        var store = new RecordingStore(() => fixture.AssertUnpublished());
        var action = fixture.Modern(store);

        Assert.True(action.Execute(fixture.Context()));

        Assert.Equal((31, 42u, "hd-200-1", "F"), Assert.Single(store.Writes));
        fixture.AssertPublished("hd-200-1", "F");
    }

    [Fact]
    public void ModernStoreFailureLeavesStateAndPacketsUnchanged()
    {
        using var fixture = new Fixture();
        var store = new RecordingStore(() => fixture.AssertUnpublished()) { Fail = true };

        Assert.Throws<InvalidOperationException>(() => fixture.Modern(store).Execute(fixture.Context()));

        fixture.AssertUnpublished();
    }

    [Fact]
    public void LegacyActionPersistsBeforePublishingNewAppearance()
    {
        using var fixture = new Fixture();
        var store = new RecordingStore(() => fixture.AssertUnpublished());
        var box = new BotChangesClothesBox(fixture.Room, new Item(), store)
        {
            StringData = "Alice\thd-300-2"
        };

        Assert.True(box.Execute(new object()));

        Assert.Equal((31, 42u, "hd-300-2", "M"), Assert.Single(store.Writes));
        fixture.AssertPublished("hd-300-2", "M");
    }

    [Fact]
    public void LegacyStoreFailureLeavesStateAndPacketsUnchanged()
    {
        using var fixture = new Fixture();
        var store = new RecordingStore(() => fixture.AssertUnpublished()) { Fail = true };
        var box = new BotChangesClothesBox(fixture.Room, new Item(), store)
        {
            StringData = "Alice\thd-300-2"
        };

        Assert.Throws<InvalidOperationException>(() => box.Execute(new object()));

        fixture.AssertUnpublished();
    }

    [RoomComponentDatabaseFact]
    public void CanonicalStoreRequiresExactCurrentRoomBeforeUpdatingAppearance()
    {
        using var server = new MySqlConnection(ProductionConnection());
        server.Open();
        var schema = "task_refactor_tests_wired_bot_" + Guid.NewGuid().ToString("N");
        server.Execute($"CREATE DATABASE `{schema}`");
        try
        {
            var connectionString = new MySqlConnectionStringBuilder(ProductionConnection())
            {
                Database = schema
            }.ConnectionString;
            using var connection = new MySqlConnection(connectionString);
            connection.Open();
            connection.Execute("""
                CREATE TABLE bots (
                    id INT PRIMARY KEY,
                    room_id INT UNSIGNED NOT NULL,
                    look VARCHAR(512) NOT NULL,
                    gender VARCHAR(1) NOT NULL
                ) ENGINE=InnoDB;
                INSERT INTO bots VALUES (31, 42, 'hd-180-1', 'F');
                """);
            var store = new BotManagementStore(new ProbeDatabase(connectionString));

            Assert.Throws<InvalidOperationException>(() =>
                store.SaveAppearance(31, 43, "hd-200-1", "M"));
            Assert.Equal(("hd-180-1", "F"), connection.QuerySingle<(string Look, string Gender)>(
                "SELECT look AS `Look`, gender AS Gender FROM bots WHERE id=31"));
            Assert.Throws<InvalidOperationException>(() =>
                store.SaveAppearance(99, 42, "hd-200-1", "M"));

            store.SaveAppearance(31, 42, "hd-200-1", "M");

            Assert.Equal(("hd-200-1", "M"), connection.QuerySingle<(string Look, string Gender)>(
                "SELECT look AS `Look`, gender AS Gender FROM bots WHERE id=31"));
        }
        finally
        {
            server.Execute($"DROP DATABASE `{schema}`");
        }
    }

    private sealed class Fixture : IDisposable
    {
        private readonly List<(uint Header, byte[] Payload)> _sent;
        public Room Room { get; }
        public RoomUser Bot { get; }

        public Fixture()
        {
            Room = (Room)RuntimeHelpers.GetUninitializedObject(typeof(Room));
            Room.Id = 42;
            var users = new RoomUserManager(Room, TestRoomUserStore.Instance, TimeProvider.System);
            Set(Room, "_roomUserManager", users);
            var botData = (RoomBot)RuntimeHelpers.GetUninitializedObject(typeof(RoomBot));
            botData.Id = 31;
            botData.RoomId = Room.Id;
            botData.Name = "Alice";
            botData.Look = "hd-180-1";
            botData.Gender = "F";
            botData.AiType = BotAiType.Generic;
            botData.VirtualId = 31;
            Bot = new(0, Room.Id, 31, Room, null) { BotData = botData };
            var bots = (ConcurrentDictionary<int, RoomUser>)typeof(RoomUserManager)
                .GetField("_bots", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(users)!;
            bots[botData.Id] = Bot;

            var viewer = new Habbo { Id = 7, CurrentRoom = Room };
            var (client, sent) = HabbiconTestSupport.Client(viewer);
            _sent = sent;
            var roomUser = new RoomUser(viewer.Id, Room.Id, 7, Room, client);
            var people = (ConcurrentDictionary<int, RoomUser>)typeof(RoomUserManager)
                .GetField("_users", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(users)!;
            people[roomUser.VirtualId] = roomUser;
        }

        public WiredModernAction Modern(IBotManagementStore store)
        {
            var descriptor = WiredBoxRegistry.All.Single(value => value.CanonicalName == "wf_act_bot_clothes");
            var action = new WiredModernAction(Room, new Item { Id = 1 }, descriptor, new(), _ => { },
                (_, _, _) => { }, new(), TestLogging.Logger, TimeProvider.System,
                TestWiredRewardService.Instance, store, TestWiredClients.Empty, TestWiredDefinitions.Unused);
            Assert.True(action.TryValidateConfiguration(
                new() { IntParams = [0], Text = "\thd-200-1" }, out var configuration, out var error), error);
            action.ApplyConfiguration(configuration);
            return action;
        }

        public WiredRuntimeContext Context() => new(Room,
            new WiredRuntimeEvent(WiredEventKind.Use) { Actor = Bot },
            new WiredTargetResolver(() => Array.Empty<Item>(), () => new[] { Bot }),
            new NoWiredOperations());

        public void AssertUnpublished()
        {
            Assert.Equal("hd-180-1", Bot.BotData.Look);
            Assert.Equal("F", Bot.BotData.Gender);
            Assert.Empty(_sent);
        }

        public void AssertPublished(string look, string gender)
        {
            Assert.Equal(look, Bot.BotData.Look);
            Assert.Equal(gender, Bot.BotData.Gender);
            var packet = Assert.Single(_sent);
            Assert.Equal(ServerPacketHeader.UserChangeComposer, packet.Header);
            var body = new FlashIncomingPacket { Buffer = packet.Payload };
            Assert.Equal(Bot.VirtualId, body.ReadInt());
            Assert.Equal(look, body.ReadString());
            Assert.Equal(gender, body.ReadString());
        }

        public void Dispose() { }
    }

    private sealed class RecordingStore(Action beforeWrite) : IBotManagementStore
    {
        public bool Fail { get; set; }
        public List<(int BotId, uint RoomId, string Look, string Gender)> Writes { get; } = [];
        public void SaveAppearance(int botId, uint roomId, string look, string gender)
        {
            beforeWrite();
            if (Fail) throw new InvalidOperationException("forced persistence failure");
            Writes.Add((botId, roomId, look, gender));
        }
        public BotPlacementData Place(int botId, int ownerId, uint roomId, int x, int y) => throw new NotSupportedException();
        public void PickUp(int botId, uint roomId) => throw new NotSupportedException();
        public IReadOnlyList<string> SaveSpeech(int botId, uint roomId, IReadOnlyList<string> speech,
            bool automatic, int interval, bool mix) => throw new NotSupportedException();
        public void SaveWalkingMode(int botId, uint roomId, string mode) => throw new NotSupportedException();
        public void SaveName(int botId, uint roomId, string name) => throw new NotSupportedException();
    }

    private sealed class NoWiredOperations : IWiredRuntimeOperations
    {
        public bool CallStacks(WiredRuntimeContext context, IEnumerable<Item> targets, bool negative = false) =>
            throw new NotSupportedException();
        public bool SendSignal(WiredRuntimeContext context, IEnumerable<Item> receivers,
            WiredSelection selection, bool negative = false) => throw new NotSupportedException();
        public void ResetTimers(IEnumerable<Item> targets) => throw new NotSupportedException();
    }

    private sealed class ProbeDatabase(string connectionString) : IDatabase
    {
        public bool IsConnected() => true;
        public IDbConnection Connection() => new MySqlConnection(connectionString);
    }

    private static string ProductionConnection() => new MySqlConnectionStringBuilder(
        Environment.GetEnvironmentVariable("ROOM_COMPONENT_DATABASE")!)
    {
        AllowZeroDateTime = true,
        ConvertZeroDateTime = true
    }.ConnectionString;

    private static void Set(object target, string field, object value) =>
        target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);
}
