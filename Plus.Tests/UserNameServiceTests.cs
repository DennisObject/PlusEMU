using System.Collections.Concurrent;
using System.Data;
using System.Reflection;
using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using MySqlConnector;
using Plus.Communication.Packets;
using Plus.Communication.Packets.Incoming.Avatar;
using Plus.Communication.Packets.Outgoing;
using Plus.Database;
using Plus.HabboHotel.Achievements;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.Chat.Filter;
using Plus.HabboHotel.Users;
using Plus.HabboHotel.Users.Messenger;
using Plus.HabboHotel.Users.UserData;
using Xunit;

namespace Plus.Tests;

public sealed class UserNameServiceTests
{
    [Fact]
    public async Task HandlersDecodeNameAndDelegate()
    {
        var names = new RecordingNameService();

        await new CheckUserNameEvent(names).Parse(null!, HabbiconTestSupport.Incoming("CheckMe"));
        await new ChangeUserNameEvent(names).Parse(null!, HabbiconTestSupport.Incoming("ChangeMe"));

        Assert.Equal("CheckMe", names.Checked);
        Assert.Equal("ChangeMe", names.Changed);
    }

    [Fact]
    public async Task AvailabilityCheckPreservesFilterAndSuggestionBehavior()
    {
        var filtered = Context(wordFiltered: true);
        await filtered.Service.Check(filtered.Client, "ordinary");
        Assert.Single(filtered.Sent);
        Assert.Equal(ServerPacketHeader.NameChangeUpdateComposer, filtered.Sent[0].Header);

        var inUse = Context(nameExists: true);
        await inUse.Service.Check(inUse.Client, "Dennis");
        Assert.Single(inUse.Sent);
        Assert.Contains("Dennis100", System.Text.Encoding.UTF8.GetString(inUse.Sent[0].Payload));
    }

    [Fact]
    public async Task StoreFailureRollsBackReservationWithoutPublishingOrChangingHabbo()
    {
        var context = Context(storeSucceeds: false);
        DateTimeOffset? previous = null;
        context.Habbo.LastNameChangedAt = previous;

        await context.Service.Change(context.Client, "Renamed");

        Assert.Equal("Dennis", context.Habbo.Username);
        Assert.Equal(previous, context.Habbo.LastNameChangedAt);
        Assert.Equal(new[] { ("Dennis", "Renamed") }, context.ClientNames.Updates);
        Assert.Empty(context.Sent);
        Assert.Equal(1, context.Clock.Reads);
    }

    [Fact]
    public async Task SameNamePersistsCapturedUtcBeforeAcknowledgementWithoutRoomRejoin()
    {
        var context = Context();

        await context.Service.Change(context.Client, "Dennis");

        Assert.Equal(context.Clock.Now, context.Habbo.LastNameChangedAt);
        Assert.Equal((42, "Dennis", "Dennis", context.Clock.Now, false), Assert.Single(context.Store.Changes));
        Assert.Empty(context.ClientNames.Updates);
        Assert.Equal(new[] { ServerPacketHeader.UpdateUsernameComposer }, context.Sent.Select(packet => packet.Header));
        Assert.Equal(1, context.Clock.Reads);
    }

    [Fact]
    public async Task SuccessfulChangeCommitsBeforePublishingToTheDepartingActorAndRemainingObserver()
    {
        var context = Context();
        var room = context.Habbo.CurrentRoom!;
        var manager = room.GetRoomUserManager();
        var map = new Gamemap(room, new RoomModel("rename", 0, 0, 0, 0, "00\r00", 0, 0, true),
            TestLogging.Navigation, TestRoomSettings.Empty, TestGroupManager.Empty, TestNavigationDatabase.Instance, TestNavigationRewards.Instance);
        typeof(Room).GetField("_gamemap", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(room, map);
        var actor = manager.GetRoomUserByHabbo(context.Habbo.Id)!;
        map.AddUserToMap(actor, new(0, 0));
        var (observer, observerSent) = HabbiconTestSupport.Client(new Habbo
        {
            Id = 43,
            Username = "Observer",
            CurrentRoom = room
        });
        var observerVisit = new RoomUser(43, room.Id, 4, room, observer, TestChatEmotions.Unused, TestRewardProgress.Unused) { InternalRoomId = 4 };
        var visits = (ConcurrentDictionary<int, RoomUser>)typeof(RoomUserManager)
            .GetField("_users", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(manager)!;
        visits[4] = observerVisit;
        map.AddUserToMap(observerVisit, new(1, 0));

        foreach (var recipient in new[] { (Plus.Communication.Flash.FlashGameClient)context.Client, observer }) {
            var capture = recipient.SendCallback;
            recipient.SendCallback = args =>
            {
                Assert.Single(context.Store.Changes);
                Assert.Equal(new[] { ("Dennis", "Renamed") }, context.ClientNames.Updates);

                return capture!(args);
            };
        }

        await context.Service.Change(context.Client, "Renamed");

        Assert.Equal("Renamed", context.Habbo.Username);
        Assert.Equal(context.Clock.Now, context.Habbo.LastNameChangedAt);
        Assert.Equal((42, "Dennis", "Renamed", context.Clock.Now, true), Assert.Single(context.Store.Changes));
        Assert.Equal(new[] { ("Dennis", "Renamed") }, context.ClientNames.Updates);
        Assert.Equal(new uint[]
        {
            ServerPacketHeader.CloseConnectionComposer,
            ServerPacketHeader.UserRemoveComposer,
            ServerPacketHeader.UpdateUsernameComposer,
            ServerPacketHeader.RoomForwardComposer
        }, context.Sent.Select(packet => packet.Header));
        Assert.Equal(new uint[]
        {
            ServerPacketHeader.UserRemoveComposer,
            ServerPacketHeader.UserNameChangeComposer
        }, observerSent.Select(packet => packet.Header));
        var renamed = observerSent[1].Payload;
        Assert.Equal(room.Id, System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(renamed));
        Assert.Equal(actor.VirtualId, System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(renamed.AsSpan(4)));
        Assert.Equal("Renamed", System.Text.Encoding.UTF8.GetString(renamed.AsSpan(10)));
        Assert.Null(manager.GetRoomUserByHabbo(context.Habbo.Id));
        Assert.Null(actor.GetClient());
        Assert.False(actor.IsAttachedTo(room));
        Assert.Same(observer, observerVisit.GetClient());
    }

    [Fact]
    public void ClientNameReservationRefusesAnotherSessionAndCanBeRolledBack()
    {
        var manager = ClientManager();
        var first = Client("Alpha");
        var second = Client("Taken");
        manager.RegisterClient(first, 1, "Alpha");
        manager.RegisterClient(second, 2, "Taken");

        Assert.False(manager.TryChangeClientUsername(first, "Alpha", "Taken", () => true));
        Assert.Same(first, manager.GetClientByUsername("ALPHA"));
        Assert.Same(second, manager.GetClientByUsername("taken"));

        Assert.True(manager.TryChangeClientUsername(first, "Alpha", "Available", () => true));
        Assert.Null(manager.GetClientByUsername("Alpha"));
        Assert.Same(first, manager.GetClientByUsername("available"));

        Assert.True(manager.TryChangeClientUsername(first, "Available", "Alpha", () => true));
        Assert.Same(first, manager.GetClientByUsername("alpha"));
        Assert.Null(manager.GetClientByUsername("available"));
        Assert.Same(second, manager.GetClientByUsername("taken"));
    }

    [Fact]
    public async Task ConcurrentClientNameReservationsHaveExactlyOneWinner()
    {
        var manager = ClientManager();
        var first = Client("Alpha");
        var second = Client("Beta");
        manager.RegisterClient(first, 1, "Alpha");
        manager.RegisterClient(second, 2, "Beta");

        var results = await Task.WhenAll(
            Task.Run(() => manager.TryChangeClientUsername(first, "Alpha", "Target", () => true)),
            Task.Run(() => manager.TryChangeClientUsername(second, "Beta", "Target", () => true)));

        Assert.Single(results.Where(result => result));
        var winner = manager.GetClientByUsername("target");
        Assert.True(ReferenceEquals(winner, first) || ReferenceEquals(winner, second));
        Assert.Equal(ReferenceEquals(winner, second), ReferenceEquals(manager.GetClientByUsername("alpha"), first));
        Assert.Equal(ReferenceEquals(winner, first), ReferenceEquals(manager.GetClientByUsername("beta"), second));
    }

    [Fact]
    public async Task FailedPersistenceRetainsRegistrationAgainstConcurrentRegistration()
    {
        var manager = ClientManager();
        var original = Client("Alpha");
        var competing = Client("Alpha");
        manager.RegisterClient(original, 1, "Alpha");
        using var persistenceEntered = new ManualResetEventSlim();
        using var finishPersistence = new ManualResetEventSlim();
        using var registrationEntered = new ManualResetEventSlim();

        var change = Task.Run(() => manager.TryChangeClientUsername(original, "Alpha", "Target", () =>
        {
            persistenceEntered.Set();
            finishPersistence.Wait();

            return false;
        }));
        Assert.True(persistenceEntered.Wait(TimeSpan.FromSeconds(5)));
        var registration = Task.Run(() =>
        {
            registrationEntered.Set();
            manager.RegisterClient(competing, 2, "Alpha");
        });
        Assert.True(registrationEntered.Wait(TimeSpan.FromSeconds(5)));
        Assert.False(registration.IsCompleted);

        finishPersistence.Set();
        Assert.False(await change);
        await registration;

        Assert.Same(competing, manager.GetClientByUsername("Alpha"));
        Assert.Null(manager.GetClientByUsername("Target"));
    }

    [RoomComponentDatabaseFact]
    public void MigrationAndStorePreserveUtcAndRollbackFailedAuditWrite()
    {
        var connectionString = Environment.GetEnvironmentVariable("ROOM_COMPONENT_DATABASE")!;
        var schema = "task_refactor_tests_names_" + Guid.NewGuid().ToString("N");
        using var server = new MySqlConnection(connectionString);
        server.Execute($"CREATE DATABASE `{schema}`");

        try {
            var database = new ProbeDatabase(new MySqlConnectionStringBuilder(connectionString) { Database = schema }.ConnectionString);

            using (var connection = database.Connection()) {
                connection.Execute("CREATE TABLE users(id INT PRIMARY KEY,username VARCHAR(50) NOT NULL UNIQUE,last_change DATETIME(6) NULL)");
                connection.Execute("CREATE TABLE logs_client_namechange(id INT AUTO_INCREMENT PRIMARY KEY,user_id INT NOT NULL,new_name VARCHAR(50) NOT NULL,old_name VARCHAR(50) NOT NULL,`timestamp` DECIMAL(20,6) NULL)");
                connection.Execute("INSERT INTO users VALUES(42,'Dennis',NULL)");
                connection.Execute("INSERT INTO logs_client_namechange(user_id,new_name,old_name,`timestamp`) VALUES(1,'a','b',NULL),(1,'a','b',0),(1,'a','b',-1),(1,'a','b',2200000000.123456)");
                connection.Execute(File.ReadAllText(HabbiconPacketTests.Repo("Database/Migrations/30_UseUtcNameChangeLogTimes.sql")));
                Assert.Equal(3, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM logs_client_namechange WHERE `timestamp` IS NULL"));
                Assert.Equal("2039-09-18 23:06:40.123456", connection.ExecuteScalar<string>("SELECT CAST(`timestamp` AS CHAR) FROM logs_client_namechange WHERE id=4"));
            }

            var changedAt = new DateTimeOffset(2041, 1, 2, 3, 4, 5, TimeSpan.FromHours(2)).AddTicks(1234560);
            Assert.True(new NameChangeStore(database).Change(42, "Dennis", "Renamed", changedAt, true));

            using (var verify = database.Connection()) {
                Assert.Equal("Renamed", verify.ExecuteScalar<string>("SELECT username FROM users WHERE id=42"));
                Assert.Equal(changedAt.UtcDateTime, verify.ExecuteScalar<DateTime>("SELECT last_change FROM users WHERE id=42"));
                Assert.Equal(changedAt.UtcDateTime, verify.ExecuteScalar<DateTime>("SELECT `timestamp` FROM logs_client_namechange WHERE user_id=42"));
                verify.Execute("DROP TABLE logs_client_namechange");
            }

            Assert.ThrowsAny<Exception>(() => new NameChangeStore(database).Change(42, "Renamed", "Broken", changedAt, true));
            using var rollback = database.Connection();
            Assert.Equal("Renamed", rollback.ExecuteScalar<string>("SELECT username FROM users WHERE id=42"));
        }
        finally {
            server.Execute($"DROP DATABASE `{schema}`");
        }
    }

    private static TestContext Context(bool storeSucceeds = true, bool nameExists = false, bool wordFiltered = false)
    {
        var room = (Room)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Room));
        room.Id = 7;
        var manager = new RoomUserManager(room, TestRoomUserStore.Instance, TimeProvider.System, new TestRewardProgress(), TestChatEmotions.Unused, TestBotAiFactory.Inert, TestGameClientManager.Empty, TestItemRuntime.Travel);
        typeof(Room).GetField("_roomUserManager", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(room, manager);
        var habbo = new Habbo
        {
            Id = 42,
            Username = "Dennis",
            CurrentRoom = room,
            Messenger = new HabboMessenger([], [], [], new FixedTimeProvider(FixedTimeProvider.Epoch))
        };
        var (client, sent) = HabbiconTestSupport.Client(habbo);
        habbo.Client = client;
        var roomUser = new RoomUser(habbo.Id, room.Id, 3, room, client, TestChatEmotions.Unused, TestRewardProgress.Unused) { InternalRoomId = 3, UserId = habbo.Id };
        var users = (ConcurrentDictionary<int, RoomUser>)typeof(RoomUserManager)
            .GetField("_users", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(manager)!;
        users[3] = roomUser;
        var clientNames = new RecordingClientManager();
        var store = new RecordingStore(storeSucceeds);
        var clock = new CountingClock(new DateTimeOffset(2040, 2, 3, 4, 5, 6, TimeSpan.Zero));
        var service = new UserNameService(
            Proxy<IUserDataFactory>((method, _) => method == nameof(IUserDataFactory.HabboExists)
                ? Task.FromResult(nameExists)
                : throw new NotSupportedException(method)),
            Proxy<IWordFilterManager>((method, _) => method == nameof(IWordFilterManager.IsFiltered)
                ? wordFiltered
                : throw new NotSupportedException(method)),
            clientNames,
            Proxy<IRoomManager>((method, _) => method == nameof(IRoomManager.GetRooms)
                ? Array.Empty<Room>()
                : throw new NotSupportedException(method)),
            Proxy<IAchievementManager>((method, _) => method == nameof(IAchievementManager.ProgressAchievement)
                ? false
                : throw new NotSupportedException(method)),
            store,
            clock,
            NullLogger<UserNameService>.Instance);

        return new(service, client, habbo, sent, clientNames, store, clock);
    }

    private static GameClientManager ClientManager() =>
        new(new ProbeDatabase(string.Empty), NullLogger<GameClientManager>.Instance);

    private static GameClient Client(string username)
    {
        var (client, _) = HabbiconTestSupport.Client(new Habbo { Username = username });
        client.Id = Guid.NewGuid();

        return client;
    }

    private static T Proxy<T>(Func<string, object?[], object?> invoke) where T : class
    {
        var proxy = DispatchProxy.Create<T, TestProxy>();
        ((TestProxy)(object)proxy).InvokeMethod = invoke;

        return proxy;
    }

    public class TestProxy : DispatchProxy
    {
        public Func<string, object?[], object?> InvokeMethod { get; set; } = null!;
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => InvokeMethod(targetMethod!.Name, args!);
    }

    private sealed record TestContext(
        UserNameService Service,
        GameClient Client,
        Habbo Habbo,
        List<(uint Header, byte[] Payload)> Sent,
        RecordingClientManager ClientNames,
        RecordingStore Store,
        CountingClock Clock);

    private sealed class RecordingNameService : IUserNameService
    {
        public string? Checked { get; private set; }
        public string? Changed { get; private set; }
        public Task Check(GameClient session, string name)
        {
            Checked = name;

            return Task.CompletedTask;
        }
        public Task Change(GameClient session, string name)
        {
            Changed = name;

            return Task.CompletedTask;
        }
    }

    private sealed class RecordingStore(bool succeeds) : INameChangeStore
    {
        public List<(int, string, string, DateTimeOffset, bool)> Changes { get; } = [];
        public bool Change(int userId, string oldName, string newName, DateTimeOffset changedAt, bool writeLog)
        {
            Changes.Add((userId, oldName, newName, changedAt, writeLog));

            return succeeds;
        }
    }

    private sealed class RecordingClientManager : IGameClientManager
    {
        public List<(string Old, string New)> Updates { get; } = [];
        public bool TryChangeClientUsername(GameClient client, string oldUsername, string newUsername, Func<bool> persist)
        {
            Updates.Add((oldUsername, newUsername));

            return persist();
        }
        public int Count => 0;
        public ICollection<GameClient> GetClients => [];
        public void OnCycle() => throw new NotSupportedException();
        public GameClient? GetClientByUserId(int userId) => null;
        public GameClient? GetClientByUsername(string username) => null;
        public bool TryGetClient(Guid clientId, out GameClient? client)
        {
            client = null;

            return false;
        }
        public Task<string> GetNameById(int id) => throw new NotSupportedException();
        public IEnumerable<GameClient> GetClientsById(Dictionary<int, HabboHotel.Users.Messenger.MessengerBuddy>.KeyCollection users) => [];
        public void StaffAlert(IServerPacket message, int exclude = 0) => throw new NotSupportedException();
        public void ModAlert(string message) => throw new NotSupportedException();
        public void DoAdvertisingReport(GameClient reporter, GameClient target) => throw new NotSupportedException();
        public void SendPacket(IServerPacket packet, HabboHotel.Permissions.PermissionDefinition? permission = null) => throw new NotSupportedException();
        public void LogClonesOut(int userId) => throw new NotSupportedException();
        public void RegisterClient(GameClient client, int userId, string username) => throw new NotSupportedException();
        public void UnregisterClient(GameClient? client, int userId, string username) => throw new NotSupportedException();
        public void CloseAll() => throw new NotSupportedException();
    }

    private sealed class CountingClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; } = now;
        public int Reads { get; private set; }
        public override DateTimeOffset GetUtcNow()
        {
            Reads++;

            return Now;
        }
    }

    private sealed class ProbeDatabase(string connectionString) : IDatabase
    {
        public IDbConnection Connection() => new MySqlConnection(connectionString);
        public bool IsConnected() => true;
    }
}
