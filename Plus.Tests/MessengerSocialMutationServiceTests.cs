using System.Buffers.Binary;
using System.Data;
using Dapper;
using MySqlConnector;
using Plus.Communication.Packets.Incoming.FriendList;
using Plus.Communication.Packets.Incoming.Users;
using Plus.Communication.Packets.Outgoing;
using Plus.Database;
using Plus.HabboHotel.Friends;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Quests;
using Plus.HabboHotel.Users;
using Plus.HabboHotel.Users.Messenger;
using Xunit;

namespace Plus.Tests;

public sealed class SocialMutationDatabaseFactAttribute : FactAttribute
{
    public SocialMutationDatabaseFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("ROOM_COMPONENT_DATABASE") is null)
            Skip = "Opt-in isolated messenger social mutation MariaDB probe.";
    }
}

public sealed class MessengerSocialMutationServiceTests
{
    [Fact]
    public async Task InviteHandlerConsumesAllIdsButDelegatesOnlyFirstHundred()
    {
        var social = new RecordingSocial();
        var values = new List<object> { 105 };
        values.AddRange(Enumerable.Range(1, 105).Cast<object>());
        values.Add("hello");
        var packet = HabbiconTestSupport.Incoming(values.ToArray());

        await new SendRoomInviteEvent(social).Parse(null!, packet);

        var request = Assert.IsType<RoomInvitationRequest>(social.Invitation);
        Assert.Equal(Enumerable.Range(1, 100), request.RecipientIds);
        Assert.Equal("hello", request.Message);
        Assert.False(packet.HasDataRemaining());
    }

    [Fact]
    public async Task InviteHandlerRejectsOversizedAndTruncatedRequests()
    {
        var social = new RecordingSocial();

        await new SendRoomInviteEvent(social).Parse(null!, HabbiconTestSupport.Incoming(501));
        await new SendRoomInviteEvent(social).Parse(null!, HabbiconTestSupport.Incoming(2, 7));

        Assert.Null(social.Invitation);
    }

    [Fact]
    public async Task RelationshipHandlerDecodesPrimitiveValues()
    {
        var social = new RecordingSocial();

        await new SetRelationshipEvent(social).Parse(null!, HabbiconTestSupport.Incoming(42, 3));

        Assert.Equal((42, 3), social.Relationship);
    }

    [Fact]
    public async Task RelationshipFailureLeavesBuddyAndPacketsUnchanged()
    {
        var buddy = new MessengerBuddy { Id = 2, Username = "friend", Relationship = 1 };
        var (client, sent) = Client(1, new Dictionary<int, MessengerBuddy> { [2] = buddy });
        var updates = 0;
        client.GetHabbo().Messenger.FriendUpdated += (_, _) => updates++;
        var loader = CatalogSnapshotTestSupport.Proxy<IMessengerDataLoader>((method, _) =>
            method == nameof(IMessengerDataLoader.SetRelationship)
                ? Task.FromException(new InvalidOperationException("write failed"))
                : throw new NotSupportedException(method));
        var service = Service(loader: loader);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SetRelationship(client, 2, 3));

        Assert.Equal(1, buddy.Relationship);
        Assert.Equal(0, updates);
        Assert.Empty(sent);
    }

    [Fact]
    public async Task RelationshipPersistsBeforePublishingAndRewardsOnce()
    {
        var buddy = new MessengerBuddy { Id = 2, Username = "friend", Relationship = 1 };
        var (client, _) = Client(1, new Dictionary<int, MessengerBuddy> { [2] = buddy });
        var published = false;
        client.GetHabbo().Messenger.FriendUpdated += (_, _) => published = true;
        var rewards = new RecordingRewards();
        var loader = CatalogSnapshotTestSupport.Proxy<IMessengerDataLoader>((method, arguments) =>
        {
            if (method != nameof(IMessengerDataLoader.SetRelationship))
                throw new NotSupportedException(method);
            Assert.False(published);
            Assert.Equal(1, buddy.Relationship);
            Assert.Equal(new object[] { 1, 2, 3 }, arguments);
            return Task.CompletedTask;
        });
        var service = Service(loader: loader, rewards: rewards);

        await service.SetRelationship(client, 2, 3);

        Assert.Equal(3, buddy.Relationship);
        Assert.True(published);
        Assert.Equal([RewardTrackActions.SetRelationshipStatus], rewards.Actions);
    }

    [Fact]
    public async Task InvitationPersistsBeforeDeliveryAndRewardsExactlyOnce()
    {
        var friends = new Dictionary<int, MessengerBuddy>
        {
            [2] = new() { Id = 2 },
            [3] = new() { Id = 3 },
            [4] = new() { Id = 4 },
        };
        var (sender, _) = Client(1, friends);
        var (recipient, recipientPackets) = Client(2);
        var (blocked, blockedPackets) = Client(3);
        blocked.GetHabbo().AllowMessengerInvites = true;
        var clients = new Dictionary<int, GameClient> { [2] = recipient, [3] = blocked };
        var rewards = new RecordingRewards();
        var store = new RecordingInvitationStore(() =>
        {
            Assert.Empty(recipientPackets);
            Assert.Empty(rewards.Actions);
        });
        var service = Service(store: store, rewards: rewards, clients: ClientManager(clients));

        await service.SendRoomInvites(sender, new RoomInvitationRequest([2, 3, 4], new string('x', 125) + "\n"));

        var invitation = Assert.Single(store.Logs);
        Assert.Equal(1, invitation.UserId);
        Assert.Equal(new string('x', 121), invitation.Message);
        Assert.Equal(FixedTimeProvider.Epoch, invitation.InvitedAt);
        Assert.Single(recipientPackets);
        Assert.Equal(ServerPacketHeader.RoomInviteComposer, recipientPackets[0].Header);
        Assert.Equal(1, BinaryPrimitives.ReadInt32BigEndian(recipientPackets[0].Payload.AsSpan(0, 4)));
        Assert.Empty(blockedPackets);
        Assert.Equal([RewardTrackActions.SendMessengerInvite], rewards.Actions);
    }

    [Fact]
    public async Task InvitationStoreFailurePublishesNothing()
    {
        var (sender, _) = Client(1, new Dictionary<int, MessengerBuddy> { [2] = new() { Id = 2 } });
        var (recipient, recipientPackets) = Client(2);
        var rewards = new RecordingRewards();
        var service = Service(
            store: new ThrowingInvitationStore(),
            rewards: rewards,
            clients: ClientManager(new Dictionary<int, GameClient> { [2] = recipient }));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SendRoomInvites(sender, new RoomInvitationRequest([2], "hello")));

        Assert.Empty(recipientPackets);
        Assert.Empty(rewards.Actions);
    }

    [Fact]
    public async Task MutedInvitationIsDeniedBeforeAuditOrPublication()
    {
        var (sender, senderPackets) = Client(1, new Dictionary<int, MessengerBuddy> { [2] = new() { Id = 2 } });
        sender.GetHabbo().TimeMuted = 1;
        var (recipient, recipientPackets) = Client(2);
        var store = new RecordingInvitationStore();
        var rewards = new RecordingRewards();
        var service = Service(store: store, rewards: rewards,
            clients: ClientManager(new Dictionary<int, GameClient> { [2] = recipient }));

        await service.SendRoomInvites(sender, new RoomInvitationRequest([2], "hello"));

        Assert.Empty(store.Logs);
        Assert.Empty(recipientPackets);
        Assert.Empty(rewards.Actions);
        Assert.Single(senderPackets);
    }

    [SocialMutationDatabaseFact]
    public async Task MigrationAndStorePreserveLegacyValuesAndWriteNativeUtc()
    {
        SqlMapper.AddTypeHandler(new UtcDateTimeOffsetHandler());
        var rootBuilder = new MySqlConnectionStringBuilder(Environment.GetEnvironmentVariable("ROOM_COMPONENT_DATABASE"))
        {
            AllowZeroDateTime = true,
            ConvertZeroDateTime = true,
        };
        using var root = new MySqlConnection(rootBuilder.ConnectionString);
        await root.OpenAsync();
        var schema = "task_refactor_tests_social_" + Guid.NewGuid().ToString("N");
        await root.ExecuteAsync($"CREATE DATABASE `{schema}`");
        try
        {
            var builder = new MySqlConnectionStringBuilder(rootBuilder.ConnectionString) { Database = schema };
            await using var connection = new MySqlConnection(builder.ConnectionString);
            await connection.OpenAsync();
            await connection.ExecuteAsync("""
                CREATE TABLE chatlogs_console_invitations (
                    id INT AUTO_INCREMENT PRIMARY KEY,
                    user_id INT NOT NULL,
                    message TEXT NOT NULL,
                    `timestamp` DOUBLE NULL);
                INSERT INTO chatlogs_console_invitations (user_id, message, `timestamp`) VALUES
                    (1, 'null', NULL), (2, 'zero', 0), (3, 'negative', -1),
                    (4, 'fraction', 1700000000.123456), (5, 'future', 2200000000.654321);
                """);
            var migration = await File.ReadAllTextAsync(Path.GetFullPath(Path.Join(AppContext.BaseDirectory,
                "../../../../Database/Migrations/33_UseUtcRoomInvitationTimes.sql")));
            await connection.ExecuteAsync(migration);

            Assert.Equal(("datetime", 6L, "YES"), await connection.QuerySingleAsync<(string, long, string)>("""
                SELECT DATA_TYPE, DATETIME_PRECISION, IS_NULLABLE
                FROM information_schema.columns
                WHERE table_schema = DATABASE() AND table_name = 'chatlogs_console_invitations' AND column_name = 'timestamp'
                """));
            Assert.Equal(3, await connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM chatlogs_console_invitations WHERE timestamp IS NULL"));
            Assert.Equal(new DateTime(2023, 11, 14, 22, 13, 20, 123, DateTimeKind.Utc).AddTicks(4560),
                DateTime.SpecifyKind(await connection.QuerySingleAsync<DateTime>(
                    "SELECT timestamp FROM chatlogs_console_invitations WHERE user_id = 4"), DateTimeKind.Utc));
            Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(2_200_000_000).UtcDateTime.AddTicks(6_543_210),
                DateTime.SpecifyKind(await connection.QuerySingleAsync<DateTime>(
                    "SELECT timestamp FROM chatlogs_console_invitations WHERE user_id = 5"), DateTimeKind.Utc));

            var database = new ProbeDatabase(builder.ConnectionString);
            var store = new RoomInvitationStore(database);
            var writtenAt = new DateTimeOffset(2041, 4, 5, 6, 7, 8, TimeSpan.Zero).AddTicks(1234560);
            await store.Log(6, "stored", writtenAt);
            Assert.Equal((6, "stored", writtenAt), await connection.QuerySingleAsync<(int, string, DateTimeOffset)>(
                "SELECT user_id, message, timestamp FROM chatlogs_console_invitations WHERE user_id = 6"));
        }
        finally
        {
            await root.ExecuteAsync($"DROP DATABASE IF EXISTS `{schema}`");
        }
    }

    private static MessengerSocialMutationService Service(
        IMessengerDataLoader? loader = null,
        IRoomInvitationStore? store = null,
        IGameClientManager? clients = null,
        IRewardTrackManager? rewards = null) => new(
            loader ?? CatalogSnapshotTestSupport.Proxy<IMessengerDataLoader>((method, _) => throw new NotSupportedException(method)),
            store ?? new RecordingInvitationStore(),
            clients ?? ClientManager(new Dictionary<int, GameClient>()),
            rewards ?? new RecordingRewards(),
            new FixedTimeProvider(FixedTimeProvider.Epoch));

    private static (GameClient Client, List<(uint Header, byte[] Payload)> Sent) Client(
        int id,
        Dictionary<int, MessengerBuddy>? friends = null)
    {
        var habbo = new Habbo
        {
            Id = id,
            Username = $"user{id}",
            AllowConsoleMessages = true,
            Messenger = new HabboMessenger(friends ?? new(), new(), new(), new FixedTimeProvider(FixedTimeProvider.Epoch)),
        };
        var result = HabbiconTestSupport.Client(habbo);
        return (result.Client, result.Sent);
    }

    private static IGameClientManager ClientManager(IReadOnlyDictionary<int, GameClient> clients) =>
        CatalogSnapshotTestSupport.Proxy<IGameClientManager>((method, arguments) =>
            method == nameof(IGameClientManager.GetClientByUserId)
                ? clients.GetValueOrDefault((int)arguments![0]!)
                : throw new NotSupportedException(method));

    private sealed class RecordingSocial : IMessengerSocialMutationService
    {
        public RoomInvitationRequest? Invitation { get; private set; }
        public (int FriendId, int Relationship)? Relationship { get; private set; }
        public Task SetRelationship(GameClient session, int friendId, int relationship)
        {
            Relationship = (friendId, relationship);
            return Task.CompletedTask;
        }
        public Task SendRoomInvites(GameClient session, RoomInvitationRequest request)
        {
            Invitation = request;
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingRewards : IRewardTrackManager
    {
        public List<string> Actions { get; } = new();
        public void Progress(GameClient session, string actionType, int amount = 1) => Actions.Add(actionType);
        public void SendTracks(GameClient session) => throw new NotSupportedException();
        public Task Claim(GameClient session, string trackId, string prizeId) => throw new NotSupportedException();
        public void PurchasePremium(GameClient session, string trackId) => throw new NotSupportedException();
    }

    private sealed class RecordingInvitationStore(Action? beforeLog = null) : IRoomInvitationStore
    {
        public List<(int UserId, string Message, DateTimeOffset InvitedAt)> Logs { get; } = new();
        public Task Log(int userId, string message, DateTimeOffset invitedAt)
        {
            beforeLog?.Invoke();
            Logs.Add((userId, message, invitedAt));
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingInvitationStore : IRoomInvitationStore
    {
        public Task Log(int userId, string message, DateTimeOffset invitedAt) =>
            Task.FromException(new InvalidOperationException("write failed"));
    }

    private sealed class ProbeDatabase(string connectionString) : IDatabase
    {
        public bool IsConnected() => true;
        public IDbConnection Connection() => new MySqlConnection(connectionString);
    }
}
