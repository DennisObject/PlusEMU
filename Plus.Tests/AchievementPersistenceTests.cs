using System.Buffers.Binary;
using System.Reflection;
using System.Text.RegularExpressions;
using Dapper;
using MySqlConnector;
using Plus.Communication.Flash;
using Plus.Communication.Packets.Outgoing;
using Plus.HabboHotel;
using Plus.HabboHotel.Achievements;
using Plus.HabboHotel.Badges;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Permissions;
using Plus.HabboHotel.Users;
using Plus.HabboHotel.Talents;
using Xunit;

namespace Plus.Tests;

[Collection("HousekeepingDatabase")]
public sealed class AchievementPersistenceTests
{
    private const string Group = "ACH_Test";

    [RoomComponentDatabaseFact]
    public async Task LogoutDuringBadgeWaitDoesNotConsumeTheLevelOrAwardPoints()
    {
        using var fixture = new Fixture();
        using var enteredBadge = new ManualResetEventSlim();
        var releaseBadge = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var manager = fixture.Manager(() =>
        {
            enteredBadge.Set();

            return releaseBadge.Task;
        });
        var award = Task.Run(() => manager.ProgressAchievement(fixture.Client, Group, 1));
        Task? logout = null;

        try {
            Assert.True(enteredBadge.Wait(TimeSpan.FromSeconds(5)));
            logout = Task.Run(fixture.Habbo.OnDisconnect);
            await logout.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(fixture.Habbo.WalletClosed);
        }
        finally {
            releaseBadge.TrySetResult();
            await award.WaitAsync(TimeSpan.FromSeconds(5));

            if (logout != null) {
                await logout.WaitAsync(TimeSpan.FromSeconds(5));
            }
        }

        Assert.False(await award);
        Assert.Equal(0, fixture.StoredLevels());
        Assert.Equal((0, 0), fixture.StoredAward());
        Assert.Equal((0, 0, 0), (fixture.Habbo.GetAchievementData(Group)!.Level,
            fixture.Habbo.Duckets, fixture.Habbo.HabboStats.AchievementPoints));
        Assert.DoesNotContain(fixture.Sent, packet => packet.Header is
            ServerPacketHeader.HabboActivityPointNotificationComposer or ServerPacketHeader.AchievementScoreComposer);
    }

    [RoomComponentDatabaseFact]
    public async Task DisposalDuringBadgeWaitRejectsTheLevelCommitWithoutClosingTheWallet()
    {
        using var fixture = new Fixture();
        using var enteredBadge = new ManualResetEventSlim();
        var releaseBadge = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var manager = fixture.Manager(() =>
        {
            enteredBadge.Set();

            return releaseBadge.Task;
        });
        var writes = 0;
        fixture.Database.BeforeConnection = () => Interlocked.Increment(ref writes);
        var award = Task.Run(() => manager.ProgressAchievement(fixture.Client, Group, 1));

        try {
            Assert.True(enteredBadge.Wait(TimeSpan.FromSeconds(5)));
            fixture.Habbo.Dispose();
            Assert.True(fixture.Habbo.AccessClosed);
            Assert.False(fixture.Habbo.WalletClosed);
            Assert.Same(fixture.Client, fixture.Habbo.Client);
        }
        finally {
            releaseBadge.TrySetResult();
            await award.WaitAsync(TimeSpan.FromSeconds(5));
        }

        Assert.False(await award);
        Assert.Equal(0, writes);
        Assert.Equal(0, fixture.StoredLevels());
        Assert.Equal((0, 0, 0), (fixture.Habbo.GetAchievementData(Group)!.Level,
            fixture.Habbo.Duckets, fixture.Habbo.HabboStats.AchievementPoints));
        Assert.DoesNotContain(fixture.Sent, packet => packet.Header is
            ServerPacketHeader.HabboActivityPointNotificationComposer or ServerPacketHeader.AchievementScoreComposer
            or ServerPacketHeader.AchievementProgressedComposer);
    }

    [RoomComponentDatabaseFact]
    public async Task SaveWaitsForAnAdmittedLevelCommitAndIncludesItsAward()
    {
        using var fixture = new Fixture();
        var manager = fixture.Manager();
        using var enteredWrite = new ManualResetEventSlim();
        using var releaseWrite = new ManualResetEventSlim();
        using var startedSave = new ManualResetEventSlim();
        var connections = 0;
        fixture.Database.BeforeConnection = () =>
        {
            if (Interlocked.Increment(ref connections) != 1) {
                return;
            }

            enteredWrite.Set();
            Assert.True(releaseWrite.Wait(TimeSpan.FromSeconds(5)));
        };
        var award = Task.Run(() => manager.ProgressAchievement(fixture.Client, Group, 1));
        Task? save = null;
        bool savedWhileWritePaused;

        try {
            Assert.True(enteredWrite.Wait(TimeSpan.FromSeconds(5)));
            save = Task.Run(() => { startedSave.Set(); fixture.Habbo.Save(); });
            Assert.True(startedSave.Wait(TimeSpan.FromSeconds(5)));
            savedWhileWritePaused = await Task.WhenAny(save, Task.Delay(100)) == save;
        }
        finally {
            releaseWrite.Set();
            await award.WaitAsync(TimeSpan.FromSeconds(5));

            if (save != null) {
                await save.WaitAsync(TimeSpan.FromSeconds(5));
            }
        }

        Assert.False(savedWhileWritePaused);
        Assert.True(await award);
        Assert.Equal(1, fixture.StoredLevels());
        Assert.Equal((20, 7), fixture.StoredAward());
        Assert.Equal((1, 0), (fixture.Habbo.GetAchievementData(Group)!.Level,
            fixture.Habbo.GetAchievementData(Group)!.Progress));
    }

    [RoomComponentDatabaseFact]
    public async Task CurrencySendCanDisconnectAndSaveTheCompleteAwardInLegacyPacketOrder()
    {
        using var fixture = new Fixture();
        var manager = fixture.Manager();
        var send = fixture.Client.SendCallback;
        fixture.Client.SendCallback = args =>
        {
            var result = send(args);

            if (BinaryPrimitives.ReadUInt16BigEndian(args.MemoryBuffer.Span.Slice(4, 2)) ==
                ServerPacketHeader.HabboActivityPointNotificationComposer) {
                fixture.Habbo.OnDisconnect();
            }

            return result;
        };

        Assert.True(await Task.Run(() => manager.ProgressAchievement(fixture.Client, Group, 1))
            .WaitAsync(TimeSpan.FromSeconds(5)));

        Assert.True(fixture.Habbo.WalletClosed);
        Assert.Null(fixture.Habbo.Client);
        Assert.Equal(1, fixture.StoredLevels());
        Assert.Equal((20, 7), fixture.StoredAward());
        Assert.Equal(new[] { ServerPacketHeader.AchievementUnlockedComposer,
            ServerPacketHeader.HabboActivityPointNotificationComposer, ServerPacketHeader.AchievementScoreComposer,
            ServerPacketHeader.AchievementProgressedComposer }, fixture.Sent.Select(packet => packet.Header));
        var currency = new FlashIncomingPacket { Buffer = fixture.Sent[1].Payload };
        Assert.Equal((20, 20, 0), (currency.ReadInt(), currency.ReadInt(), currency.ReadInt()));
        Assert.False(currency.HasDataRemaining());
        Assert.Equal(7, BinaryPrimitives.ReadInt32BigEndian(fixture.Sent[2].Payload));
    }

    [RoomComponentDatabaseFact]
    public void FailedLevelWriteLeavesTheAwardAndLiveLevelUnchanged()
    {
        using var fixture = new Fixture();

        using (var connection = fixture.Database.Connection()) {
            connection.Execute("DROP TABLE user_achievements");
        }

        Assert.Throws<MySqlException>(() => fixture.Manager().ProgressAchievement(fixture.Client, Group, 1));

        Assert.Equal((0, 0, 0), (fixture.Habbo.GetAchievementData(Group)!.Level,
            fixture.Habbo.Duckets, fixture.Habbo.HabboStats.AchievementPoints));
        Assert.Equal(ServerPacketHeader.AchievementUnlockedComposer, Assert.Single(fixture.Sent).Header);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly MySqlConnection _admin;
        private readonly string _schema = "task_achievement_shutdown_" + Guid.NewGuid().ToString("N");
        private readonly FieldInfo _game = typeof(PlusEnvironment).GetField("_game", BindingFlags.Static | BindingFlags.NonPublic)!;
        private readonly object? _previousGame;
        public HabbiconDatabaseTests.TestDatabase Database { get; }
        public Habbo Habbo { get; }
        public FlashGameClient Client { get; }
        public List<(uint Header, byte[] Payload)> Sent { get; }

        public Fixture()
        {
            var root = Environment.GetEnvironmentVariable("ROOM_COMPONENT_DATABASE")!;
            _admin = new MySqlConnection(root);
            _admin.Execute($"CREATE DATABASE `{_schema}`");
            _previousGame = _game.GetValue(null);

            try {
                Database = new(new MySqlConnectionStringBuilder(root)
                {
                    Database = _schema,
                    AllowZeroDateTime = true,
                    ConvertZeroDateTime = true
                }.ConnectionString);
                using var connection = Database.Connection();
                var pristine = File.ReadAllText(HabbiconPacketTests.Repo("Resources/SQLs/Original Database.sql"));

                foreach (var table in new[] { "users", "users_settings", "user_stats", "user_achievements" }) {
                    var definition = Regex.Match(pristine, $@"CREATE TABLE `{table}` \([\s\S]*?\) ENGINE=[^;]+;").Value;
                    Assert.NotEmpty(definition);
                    connection.Execute(definition);
                }

                connection.Execute(File.ReadAllText(HabbiconPacketTests.Repo("Database/Migrations/59_UserCurrencies.sql")));
                connection.Execute("ALTER TABLE users ADD bubble_id TINYINT NOT NULL DEFAULT 0; " +
                    "ALTER TABLE user_stats RENAME TO user_statistics; " +
                    "INSERT INTO users(id,username,auth_ticket) VALUES(7,'user','ticket'); " +
                    "INSERT INTO users_settings(user_id) VALUES(7); INSERT INTO user_statistics(id) VALUES(7)");
                Habbo = new Habbo
                {
                    Id = 7,
                    Username = "user",
                    Access = UserAccess.Empty,
                    SessionStartedAt = DateTimeOffset.UtcNow,
                    HabboStats = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, "old", 0),
                    Persistence = new UserPersistenceService(Database, TimeProvider.System)
                };
                (Client, Sent) = HabbiconTestSupport.Client(Habbo);
                Habbo.Client = Client;
                var clients = CatalogSnapshotTestSupport.Proxy<IGameClientManager>((method, _) =>
                    method == "UnregisterClient" ? null : throw new InvalidOperationException(method));
                _game.SetValue(null, CatalogSnapshotTestSupport.Proxy<IGame>((method, _) =>
                    method == "get_ClientManager" ? clients : throw new InvalidOperationException(method)));
            }
            catch {
                Dispose();
                throw;
            }
        }

        public AchievementManager Manager(Func<Task>? giveBadge = null)
        {
            var badges = CatalogSnapshotTestSupport.Proxy<IBadgeManager>((method, _) =>
                method == "GiveBadge" ? giveBadge?.Invoke() ?? Task.CompletedTask : throw new InvalidOperationException(method));
            var manager = new AchievementManager(null!, Database, badges, CatalogSnapshotTestSupport.Proxy<ITalentTrackProgressionService>((method, _) => method == "Progress" ? null : throw new InvalidOperationException(method)));
            var achievement = new Achievement { Id = 1, GroupName = Group, Category = "identity" };
            achievement.AddLevel(new AchievementLevel(1, 20, 7, 1));
            manager.Achievements.Add(Group, achievement);

            return manager;
        }

        public int StoredLevels()
        {
            using var connection = Database.Connection();

            return connection.QuerySingle<int>("SELECT COUNT(*) FROM user_achievements WHERE userid=7 AND level=1");
        }

        public (int Duckets, int Score) StoredAward()
        {
            using var connection = Database.Connection();

            return connection.QuerySingle<(int, int)>(
                "SELECT COALESCE(c.amount,0),s.AchievementScore FROM users u JOIN user_statistics s ON s.id=u.id " +
                "LEFT JOIN user_currencies c ON c.user_id=u.id AND c.type=0 WHERE u.id=7");
        }

        public void Dispose()
        {
            _game.SetValue(null, _previousGame);

            try {
                _admin.Execute($"DROP DATABASE `{_schema}`");
            }
            finally {
                _admin.Dispose();
            }
        }
    }
}
