using System.Data;
using System.Diagnostics.CodeAnalysis;
using Dapper;
using MySqlConnector;
using Plus.Communication.Packets.Incoming.Navigator;
using Plus.Communication.Packets.Incoming.Preferences;
using Plus.Communication.Packets.Incoming.Sound;
using Plus.Communication.Packets.Incoming.Users;
using Plus.HabboHotel.Users.UserData;
using Plus.Communication.Packets.Outgoing;
using Plus.Database;
using Plus.HabboHotel.Navigator;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.Chat.Styles;
using Plus.HabboHotel.Permissions;
using Plus.HabboHotel.Users.Messenger.FriendBar;
using Plus.Communication.Flash;
using Plus.HabboHotel.Users;
using Xunit;

namespace Plus.Tests;

public sealed class UserPreferencePersistenceTests
{
    [Fact]
    public async Task FailedWritesLeaveBothLivePreferencesAndPacketsUnchanged()
    {
        var user = new Habbo { Id = 7, HomeRoom = 20, ChatPreference = false, ClientVolume = [20, 30, 40] };
        var (session, sent) = HabbiconTestSupport.Client(user);
        var profiles = new UserProfileService(null!, null!, null!, null!, new FailingDatabase(), TimeProvider.System, null!, null!, null!, null!);
        var navigator = new NavigatorManager(new FailingDatabase(), TestLogging.For<NavigatorManager>(), new Rooms());
        await Assert.ThrowsAsync<InvalidOperationException>(() => new SetChatPreferenceEvent(profiles).Parse(session, HabbiconTestSupport.Incoming(true)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => new UpdateNavigatorSettingsEvent(navigator).Parse(session, HabbiconTestSupport.Incoming(42)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => new SetMessengerInviteStatusEvent(profiles).Parse(session, HabbiconTestSupport.Incoming(true)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => new SetSoundSettingsEvent(profiles).Parse(session, HabbiconTestSupport.Incoming(90, 80, 70)));
        Assert.False(user.AllowMessengerInvites);
        Assert.Equal(new[] { 20, 30, 40 }, user.ClientVolume);
        Assert.False(user.ChatPreference);
        Assert.Equal(20u, user.HomeRoom);
        Assert.Empty(sent);
    }

    [Theory]
    [InlineData(99)]
    [InlineData(7)]
    public async Task MissingOrRestrictedChatStylesDoNotWriteOrPublish(int bubbleId)
    {
        var user = new Habbo { Id = 7, CustomBubbleId = 3, Access = UserAccess.Empty };
        var (session, sent) = HabbiconTestSupport.Client(user);
        var styles = new Styles(new ChatStyle(7, "Restricted", "chat.style.special"));
        var profiles = new UserProfileService(null!, null!, null!, null!, new FailingDatabase(), TimeProvider.System, styles, null!, null!, null!);

        await profiles.SetChatStylePreference(session, bubbleId);

        Assert.Equal(3, user.CustomBubbleId);
        Assert.Empty(sent);
    }

    [Fact]
    public async Task FailedChatStyleWriteRetainsTheLiveValue()
    {
        var user = new Habbo { Id = 7, CustomBubbleId = 3 };
        var (session, sent) = HabbiconTestSupport.Client(user);
        var profiles = new UserProfileService(null!, null!, null!, null!, new FailingDatabase(), TimeProvider.System, null!, null!, null!, null!);

        await Assert.ThrowsAsync<InvalidOperationException>(() => profiles.SetChatStylePreference(session, 0));

        Assert.Equal(3, user.CustomBubbleId);
        Assert.Empty(sent);
    }

    [Fact]
    public async Task SavedWalletRejectsChatStyleWithoutOpeningAConnection()
    {
        var user = new Habbo
        {
            Id = 7,
            CustomBubbleId = 3,
            Access = UserAccess.Empty,
            Persistence = CatalogSnapshotTestSupport.Proxy<IUserPersistenceService>((_, _) => null)
        };
        var (session, sent) = HabbiconTestSupport.Client(user);
        user.Save();
        var profiles = new UserProfileService(null!, null!, null!, null!, new FailingDatabase(), TimeProvider.System, null!, null!, null!, null!);

        await profiles.SetChatStylePreference(session, 0);

        Assert.Equal(3, user.CustomBubbleId);
        Assert.Empty(sent);
    }

    [RoomComponentDatabaseFact]
    public async Task FinalSaveWaitsForChatStylePersistenceAndIncludesTheNewStyle()
    {
        var root = Environment.GetEnvironmentVariable("ROOM_COMPONENT_DATABASE")!;
        var schema = "task_preferences_style_shutdown_" + Guid.NewGuid().ToString("N");
        using var server = new MySqlConnection(root);
        server.Execute($"CREATE DATABASE `{schema}`");

        try {
            var database = new HabbiconDatabaseTests.TestDatabase(new MySqlConnectionStringBuilder(root)
            {
                Database = schema,
                AllowZeroDateTime = true,
                ConvertZeroDateTime = true
            }.ConnectionString);
            using var connection = database.Connection();
            var pristine = File.ReadAllText(HabbiconPacketTests.Repo("Resources/SQLs/Original Database.sql"));

            foreach (var table in new[] { "users", "users_settings", "user_stats" }) {
                var definition = System.Text.RegularExpressions.Regex.Match(pristine,
                    $@"CREATE TABLE `{table}` \([\s\S]*?\) ENGINE=[^;]+;").Value;
                Assert.NotEmpty(definition);
                connection.Execute(definition);
            }

            connection.Execute("ALTER TABLE users ADD bubble_id TINYINT NOT NULL DEFAULT 0; " +
                "ALTER TABLE user_stats RENAME TO user_statistics; " +
                "INSERT INTO users(id,username,auth_ticket,bubble_id) VALUES(7,'user','ticket',3); " +
                "INSERT INTO users_settings(user_id) VALUES(7); INSERT INTO user_statistics(id) VALUES(7)");
            var user = new Habbo
            {
                Id = 7,
                CustomBubbleId = 3,
                Access = UserAccess.Empty,
                SessionStartedAt = DateTimeOffset.UtcNow,
                HabboStats = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, "old", 0),
                Persistence = new UserPersistenceService(database, TimeProvider.System, TestGameClientManager.Empty)
            };
            var (session, sent) = HabbiconTestSupport.Client(user);
            var profiles = new UserProfileService(null!, null!, null!, null!, database, TimeProvider.System,
                new Styles(new ChatStyle(5, "Public", "")), null!, null!, null!);
            using var enteredWrite = new ManualResetEventSlim();
            using var releaseWrite = new ManualResetEventSlim();
            using var startedSave = new ManualResetEventSlim();
            var connections = 0;
            database.BeforeConnection = () =>
            {
                if (Interlocked.Increment(ref connections) != 1) {
                    return;
                }

                enteredWrite.Set();
                Assert.True(releaseWrite.Wait(TimeSpan.FromSeconds(5)));
            };
            var update = Task.Run(() => profiles.SetChatStylePreference(session, 5));
            Task? save = null;
            bool savedWhileWritePaused;

            try {
                Assert.True(enteredWrite.Wait(TimeSpan.FromSeconds(5)));
                save = Task.Run(() => { startedSave.Set(); user.Save(); });
                Assert.True(startedSave.Wait(TimeSpan.FromSeconds(5)));
                savedWhileWritePaused = await Task.WhenAny(save, Task.Delay(100)) == save;
            }
            finally {
                releaseWrite.Set();
                await update.WaitAsync(TimeSpan.FromSeconds(5));

                if (save != null) {
                    await save.WaitAsync(TimeSpan.FromSeconds(5));
                }
            }

            Assert.False(savedWhileWritePaused);
            Assert.Equal(5, user.CustomBubbleId);
            Assert.Equal(5, connection.QuerySingle<int>("SELECT bubble_id FROM users WHERE id=7"));
            Assert.True(user.WalletClosed);
            database.BeforeConnection = () => throw new InvalidOperationException("Closed wallet opened a connection.");
            await profiles.SetChatStylePreference(session, 0);
            Assert.Equal(5, user.CustomBubbleId);
            Assert.Equal(5, connection.QuerySingle<int>("SELECT bubble_id FROM users WHERE id=7"));
            Assert.Empty(sent);
        }
        finally {
            server.Execute($"DROP DATABASE `{schema}`");
        }
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(0, 0)]
    [InlineData(99, 0)]
    public void FriendBarChangesEchoNormalizedStateAndCurrentSettings(int input, int expected)
    {
        var user = new Habbo
        {
            Id = 7,
            ClientVolume = [20, 30, 40],
            ChatPreference = true,
            AllowMessengerInvites = false,
            FocusPreference = true
        };
        var (session, sent) = HabbiconTestSupport.Client(user);
        var profiles = new UserProfileService(null!, null!, null!, null!, new FailingDatabase(), TimeProvider.System, null!, null!, null!, null!);

        profiles.SetFriendBarState(session, input);

        Assert.Equal(expected, FriendBarStateUtility.GetInt(user.FriendbarState));
        var response = Assert.Single(sent);
        Assert.Equal(ServerPacketHeader.SoundSettingsComposer, response.Header);
        var body = new FlashIncomingPacket { Buffer = response.Payload };
        Assert.Equal((20, 30, 40, true, false, true, expected, 0, true, true, true),
            (body.ReadInt(), body.ReadInt(), body.ReadInt(), body.ReadBool(), body.ReadBool(), body.ReadBool(),
             body.ReadInt(), body.ReadInt(), body.ReadBool(), body.ReadBool(), body.ReadBool()));
        Assert.False(body.HasDataRemaining());
    }

    [RoomComponentDatabaseFact]
    public async Task ChatStyleWritesTheCanonicalAccountAndRefusesMissingRows()
    {
        var root = Environment.GetEnvironmentVariable("ROOM_COMPONENT_DATABASE")!;
        var schema = "task_preferences_style_" + Guid.NewGuid().ToString("N");
        using var server = new MySqlConnection(root);
        server.Execute($"CREATE DATABASE `{schema}`");

        try {
            var database = new ProbeDatabase(new MySqlConnectionStringBuilder(root)
            {
                Database = schema,
                AllowZeroDateTime = true,
                ConvertZeroDateTime = true
            }.ConnectionString);
            using var connection = database.Connection();
            connection.Execute("CREATE TABLE users(id INT PRIMARY KEY,bubble_id TINYINT NOT NULL); INSERT INTO users VALUES(7,3),(8,4)");
            var user = new Habbo { Id = 7, CustomBubbleId = 3, Access = UserAccess.Empty };
            var (session, sent) = HabbiconTestSupport.Client(user);
            var profiles = new UserProfileService(null!, null!, null!, null!, database, TimeProvider.System,
                new Styles(new ChatStyle(5, "Public", "")), null!, null!, null!);

            await profiles.SetChatStylePreference(session, 5);
            Assert.Equal(5, user.CustomBubbleId);
            Assert.Equal(5, connection.QuerySingle<int>("SELECT bubble_id FROM users WHERE id=7"));
            Assert.Equal(4, connection.QuerySingle<int>("SELECT bubble_id FROM users WHERE id=8"));
            await profiles.SetChatStylePreference(session, 0);
            Assert.Equal(0, user.CustomBubbleId);
            Assert.Equal(0, connection.QuerySingle<int>("SELECT bubble_id FROM users WHERE id=7"));
            connection.Execute("DELETE FROM users WHERE id=7");
            await Assert.ThrowsAsync<DBConcurrencyException>(() => profiles.SetChatStylePreference(session, 5));
            Assert.Equal(0, user.CustomBubbleId);
            Assert.Empty(sent);
        }
        finally {
            server.Execute($"DROP DATABASE `{schema}`");
        }
    }

    [RoomComponentDatabaseFact]
    public async Task SettingsWritesUseCanonicalUserIdAndPublishOnlyExistingRows()
    {
        var root = Environment.GetEnvironmentVariable("ROOM_COMPONENT_DATABASE")!;
        var schema = "task_preferences_" + Guid.NewGuid().ToString("N");
        using var server = new MySqlConnection(root);
        server.Execute($"CREATE DATABASE `{schema}`");

        try {
            var database = new ProbeDatabase(new MySqlConnectionStringBuilder(root) { Database = schema }.ConnectionString);

            using (var connection = database.Connection()) {
                connection.Execute("CREATE TABLE users_settings(user_id INT PRIMARY KEY, home_room INT UNSIGNED NOT NULL, chat_preference BOOL NOT NULL, ignore_invites BOOL NOT NULL, volume VARCHAR(15) NOT NULL); INSERT INTO users_settings VALUES(7,20,false,false,'20,30,40')");
            }

            var user = new Habbo { Id = 7, HomeRoom = 20 };
            var (session, sent) = HabbiconTestSupport.Client(user);
            var profiles = new UserProfileService(null!, null!, null!, null!, database, TimeProvider.System, null!, null!, null!, null!);
            var navigator = new NavigatorManager(database, TestLogging.For<NavigatorManager>(), new Rooms());
            await new SetChatPreferenceEvent(profiles).Parse(session, HabbiconTestSupport.Incoming(true));
            await new UpdateNavigatorSettingsEvent(navigator).Parse(session, HabbiconTestSupport.Incoming(42));
            await new SetMessengerInviteStatusEvent(profiles).Parse(session, HabbiconTestSupport.Incoming(true));
            await new SetSoundSettingsEvent(profiles).Parse(session, HabbiconTestSupport.Incoming(-1, 50, 101));
            Assert.True(user.AllowMessengerInvites);
            Assert.Equal(new[] { 100, 50, 100 }, user.ClientVolume);
            Assert.True(user.ChatPreference);
            Assert.Equal(42u, user.HomeRoom);
            Assert.Equal(ServerPacketHeader.NavigatorSettingsComposer, Assert.Single(sent).Header);
            using var verify = database.Connection();
            Assert.Equal((42u, true), verify.QuerySingle<(uint, bool)>("SELECT home_room,chat_preference FROM users_settings WHERE user_id=7"));
            Assert.Equal((true, "100,50,100"), verify.QuerySingle<(bool, string)>("SELECT ignore_invites,volume FROM users_settings WHERE user_id=7"));
            verify.Execute("DELETE FROM users_settings WHERE user_id=7");
            sent.Clear();
            await Assert.ThrowsAsync<DBConcurrencyException>(() => profiles.SetChatPreference(session, false));
            await Assert.ThrowsAsync<DBConcurrencyException>(() => navigator.SaveHomeRoom(session, 43));
            await Assert.ThrowsAsync<DBConcurrencyException>(() => profiles.SetMessengerInvitePreference(session, false));
            await Assert.ThrowsAsync<DBConcurrencyException>(() => profiles.SetSoundVolumes(session, new(10, 20, 30)));
            Assert.True(user.AllowMessengerInvites);
            Assert.Equal(new[] { 100, 50, 100 }, user.ClientVolume);
            Assert.True(user.ChatPreference);
            Assert.Equal(42u, user.HomeRoom);
            Assert.Empty(sent);
        }
        finally {
            server.Execute($"DROP DATABASE `{schema}`");
        }
    }

    [RoomComponentDatabaseFact]
    public async Task AccountLoaderRestoresSoundAndInviteSettingsFromTheSameJoinedRow()
    {
        var root = Environment.GetEnvironmentVariable("ROOM_COMPONENT_DATABASE")!;
        var schema = "task_preferences_load_" + Guid.NewGuid().ToString("N");
        using var server = new MySqlConnection(root);
        server.Execute($"CREATE DATABASE `{schema}`");

        try {
            var database = new ProbeDatabase(new MySqlConnectionStringBuilder(root)
            {
                Database = schema,
                AllowZeroDateTime = true,
                ConvertZeroDateTime = true
            }.ConnectionString);

            using (var connection = database.Connection()) {
                var pristine = File.ReadAllText(HabbiconPacketTests.Repo("Resources/SQLs/Original Database.sql"));

                foreach (var table in new[] { "users", "users_settings", "user_stats", "user_info" }) {
                    var definition = System.Text.RegularExpressions.Regex.Match(pristine,
                        $@"CREATE TABLE `{table}` \([\s\S]*?\) ENGINE=[^;]+;").Value;
                    Assert.NotEmpty(definition);
                    connection.Execute(definition);
                }

                connection.Execute("ALTER TABLE users ADD bubble_id TINYINT NOT NULL DEFAULT 0; ALTER TABLE user_stats RENAME TO user_statistics; INSERT INTO users(id,username,auth_ticket,account_created) VALUES(7,'VolumeUser','ticket','2042-01-01 00:00:00'); INSERT INTO users_settings(user_id,home_room,ignore_invites,volume) VALUES(7,42,true,'20,50,80')");
                connection.Execute(File.ReadAllText(HabbiconPacketTests.Repo("Database/Migrations/59_UserCurrencies.sql")));
            }

            var loader = new UserDataFactory(null!, database, [], null!, null!, null!, null!, TimeProvider.System, TestRoomAchievements.Unused);
            var user = Assert.IsType<Habbo>(await loader.GetUserDataByIdAsync(7));
            Assert.Equal(new[] { 20, 50, 80 }, user.ClientVolume);
            Assert.Equal(42u, user.HomeRoom);
            Assert.True(user.AllowMessengerInvites);
            Assert.Equal(new DateTimeOffset(2042, 1, 1, 0, 0, 0, TimeSpan.Zero), user.AccountCreatedAt);
            Assert.Null(await loader.GetUserDataByIdAsync(8));
        }
        finally {
            server.Execute($"DROP DATABASE `{schema}`");
        }
    }

    [RoomComponentDatabaseFact]
    public void FinalSaveAccumulatesOnlineTimePastTheOldIntegerLimit()
    {
        var root = Environment.GetEnvironmentVariable("ROOM_COMPONENT_DATABASE")!;
        var schema = "task_logout_duration_" + Guid.NewGuid().ToString("N");
        using var server = new MySqlConnection(root);
        server.Execute($"CREATE DATABASE `{schema}`");

        try {
            var database = new ProbeDatabase(new MySqlConnectionStringBuilder(root) { Database = schema }.ConnectionString);
            using var connection = database.Connection();
            var pristine = File.ReadAllText(HabbiconPacketTests.Repo("Resources/SQLs/Original Database.sql"));

            foreach (var table in new[] { "users", "users_settings", "user_stats" }) {
                var definition = System.Text.RegularExpressions.Regex.Match(pristine,
                    $@"CREATE TABLE `{table}` \([\s\S]*?\) ENGINE=[^;]+;").Value;
                Assert.NotEmpty(definition);
                connection.Execute(definition);
            }

            connection.Execute("ALTER TABLE users ADD bubble_id TINYINT NOT NULL DEFAULT 0; " +
                "ALTER TABLE user_stats RENAME TO user_statistics; " +
                "ALTER TABLE user_statistics MODIFY OnlineTime INT NOT NULL DEFAULT 0; " +
                "INSERT INTO users(id,username,auth_ticket,credits) VALUES(7,'DurationUser','ticket',11); " +
                "INSERT INTO users_settings(user_id) VALUES(7); " +
                "INSERT INTO user_statistics(id,OnlineTime) VALUES(7,2147483647)");
            var now = new DateTimeOffset(2042, 1, 1, 0, 0, 0, TimeSpan.Zero);
            var user = new Habbo
            {
                Id = 7,
                Credits = 25,
                SessionStartedAt = now.AddSeconds(-12),
                HabboStats = new(0, int.MaxValue, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, "", 0)
            };
            var clock = new SaveClock(now);
            var persistence = new UserPersistenceService(database, clock, TestGameClientManager.Empty);

            Assert.Throws<MySqlException>(() => persistence.Save(user));
            Assert.Equal(11, connection.ExecuteScalar<int>("SELECT credits FROM users WHERE id=7"));
            var migration = File.ReadAllText(HabbiconPacketTests.Repo("Database/Migrations/44_WidenOnlineTime.sql"));
            connection.Execute(migration);
            connection.Execute(migration);
            Assert.Equal((long)int.MaxValue, connection.ExecuteScalar<long>("SELECT OnlineTime FROM user_statistics WHERE id=7"));
            var reads = clock.Reads;
            persistence.Save(user);

            Assert.Equal(reads + 1, clock.Reads);
            Assert.Equal((long)int.MaxValue + 12, connection.ExecuteScalar<long>("SELECT OnlineTime FROM user_statistics WHERE id=7"));
            Assert.Equal(25, connection.ExecuteScalar<int>("SELECT credits FROM users WHERE id=7"));
            Assert.Equal(now.UtcDateTime, connection.ExecuteScalar<DateTime>("SELECT last_online FROM users WHERE id=7"));
            Assert.Equal("bigint", connection.ExecuteScalar<string>(
                "SELECT DATA_TYPE FROM information_schema.columns WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='user_statistics' AND COLUMN_NAME='OnlineTime'"));
        }
        finally {
            server.Execute($"DROP DATABASE `{schema}`");
        }
    }

    private sealed class SaveClock(DateTimeOffset now) : TimeProvider
    {
        public int Reads { get; private set; }
        public override DateTimeOffset GetUtcNow()
        {
            Reads++;

            return now;
        }
    }

    [Theory]
    [InlineData("0,50,100", 0, 50, 100)]
    [InlineData("-1,200,garbage", 100, 100, 100)]
    [InlineData("75", 75, 100, 100)]
    [InlineData(null, 100, 100, 100)]
    public void LoadedVolumesAreExactlyThreeValidatedChannels(string? stored, int system, int furni, int music) =>
        Assert.Equal(new[] { system, furni, music }, UserDataFactory.ParseVolumes(stored));

    private sealed class Styles(ChatStyle style) : IChatStyleManager
    {
        public void Init() { }
        public IReadOnlyList<int> GetAllowedStyleIds(UserAccess access) => [];
        public bool TryGetStyle(int id, [NotNullWhen(true)] out ChatStyle? found)
        {
            found = id == style.Id ? style : null;

            return found != null;
        }
    }

    private sealed class Rooms : IRoomDataLoader
    {
        public bool TryGetData(uint roomId, [NotNullWhen(true)] out RoomData? data)
        {
            data = new RoomData { Id = roomId };

            return true;
        }
        public List<RoomData> GetRoomsDataByOwnerSortByName(int ownerId) => [];
    }
    private sealed class FailingDatabase : IDatabase
    {
        public bool IsConnected() => true;
        public IDbConnection Connection() => throw new InvalidOperationException("Write failed");
    }
    private sealed class ProbeDatabase(string connectionString) : IDatabase
    {
        public bool IsConnected() => true;
        public IDbConnection Connection() => new MySqlConnection(connectionString);
    }
}
