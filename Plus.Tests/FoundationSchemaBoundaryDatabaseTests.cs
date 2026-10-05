using System.Text.RegularExpressions;
using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using Plus.HabboHotel;
using Plus.HabboHotel.Navigator;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Permissions;
using Dapper;
using MySqlConnector;
using Plus.Database;
using Plus.HabboHotel.Rooms.Chat.Commands.Moderator;
using Plus.HabboHotel.Rooms.Chat.Commands.User;
using Plus.HabboHotel.Users;
using Xunit;

namespace Plus.Tests;

public sealed class FoundationSchemaDatabaseFactAttribute : FactAttribute
{
    public FoundationSchemaDatabaseFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("ROOM_COMPONENT_DATABASE") == null)
            Skip = "Opt-in GUID schema foundation boundary probe.";
    }
}

[Collection("HousekeepingDatabase")]
public sealed class FoundationSchemaBoundaryDatabaseTests
{
    [FoundationSchemaDatabaseFact]
    public void NativeDatesAndSettingsCommandsUseTheMigratedDatabaseBoundaries()
    {
        SqlMapper.AddTypeHandler(new UtcDateTimeOffsetHandler());
        var options = new MySqlConnectionStringBuilder(Environment.GetEnvironmentVariable("ROOM_COMPONENT_DATABASE")!)
        { Database = "mysql", Pooling = false, AllowZeroDateTime = true, ConvertZeroDateTime = true };
        using var admin = new MySqlConnection(options.ConnectionString);
        admin.Open();
        var schema = "task_foundation_schema_" + Guid.NewGuid().ToString("N");
        admin.Execute($"CREATE DATABASE `{schema}`");
        try
        {
            options.Database = schema;
            using var connection = new MySqlConnection(options.ConnectionString);
            connection.Open();
            var sql = File.ReadAllText(Path.GetFullPath(Path.Join(AppContext.BaseDirectory, "../../../../Resources/SQLs/Original Database.sql")));
            connection.Execute("CREATE TABLE users (id INT PRIMARY KEY); INSERT INTO users VALUES (7)");
            connection.Execute(Regex.Match(sql, @"CREATE TABLE `users_settings` \(.*?;", RegexOptions.Singleline).Value);
            connection.Execute("INSERT INTO users_settings (user_id,allow_mimic,disable_forced_effects) VALUES (7,FALSE,FALSE)");
            var database = new HabbiconDatabaseTests.TestDatabase(options.ConnectionString);
            var habbo = new Habbo { Id = 7 };
            var (client, sent) = HabbiconTestSupport.Client(habbo);
            var mimic = new DisableMimicCommand(database);
            var effects = new DisableForcedFxCommand(database);
            foreach (var expected in new[] { true, false })
            {
                mimic.Execute(client, null!, []);
                effects.Execute(client, null!, []);
                Assert.Equal(expected, habbo.AllowMimic);
                Assert.Equal(expected, habbo.DisableForcedEffects);
                Assert.Equal(expected, connection.ExecuteScalar<bool>("SELECT allow_mimic FROM users_settings WHERE user_id=7"));
                Assert.Equal(expected, connection.ExecuteScalar<bool>("SELECT disable_forced_effects FROM users_settings WHERE user_id=7"));
            }
            connection.Execute("CREATE TRIGGER reject_settings BEFORE UPDATE ON users_settings FOR EACH ROW SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='forced'");
            Assert.Throws<MySqlException>(() => mimic.Execute(client, null!, []));
            Assert.Throws<MySqlException>(() => effects.Execute(client, null!, []));
            Assert.False(habbo.AllowMimic);
            Assert.False(habbo.DisableForcedEffects);
            Assert.Empty(sent);
            connection.Execute("DROP TRIGGER reject_settings; DELETE FROM users_settings WHERE user_id=7");
            Assert.Throws<InvalidOperationException>(() => mimic.Execute(client, null!, []));
            Assert.Throws<InvalidOperationException>(() => effects.Execute(client, null!, []));
            Assert.False(habbo.AllowMimic);
            Assert.False(habbo.DisableForcedEffects);

            connection.Execute("CREATE TABLE dates (value DATETIME(6) NULL); INSERT INTO dates VALUES ('2039-01-02 03:04:05.123456'),(NULL),('1970-01-01 00:00:00')");
            var dates = connection.Query<DateRow>("SELECT value FROM dates").Select(row => row.Value).ToArray();
            Assert.Equal(new DateTimeOffset(2039, 1, 2, 3, 4, 5, TimeSpan.Zero).AddTicks(1234560), dates[0]);
            Assert.Null(dates[1]);
            Assert.Equal(DateTimeOffset.UnixEpoch, dates[2]);
        }
        finally { admin.Execute($"DROP DATABASE IF EXISTS `{schema}`"); }
    }
    [FoundationSchemaDatabaseFact]
    public async Task HomeRoomUsesTheSettingsKeyAndPublishesOnlyAfterPersistence()
    {
        var options = new MySqlConnectionStringBuilder(Environment.GetEnvironmentVariable("ROOM_COMPONENT_DATABASE")!)
        { Database = "mysql", Pooling = false, AllowZeroDateTime = true, ConvertZeroDateTime = true };
        using var admin = new MySqlConnection(options.ConnectionString);
        admin.Open();
        var schema = "task_foundation_home_" + Guid.NewGuid().ToString("N");
        admin.Execute($"CREATE DATABASE `{schema}`");
        var gameField = typeof(PlusEnvironment).GetField("_game", BindingFlags.NonPublic | BindingFlags.Static)!;
        var previousGame = gameField.GetValue(null);
        try
        {
            options.Database = schema;
            using var connection = new MySqlConnection(options.ConnectionString);
            connection.Open();
            connection.Execute("CREATE TABLE users_settings (user_id INT PRIMARY KEY, home_room INT UNSIGNED NOT NULL); INSERT INTO users_settings VALUES (7,0)");
            var room = new Room(new RoomData { Id = 42 }, [], TestLogging.Navigation, TestLogging.Logger);
            var rooms = DispatchProxy.Create<IRoomManager, LoadedRoom>();
            ((LoadedRoom)(object)rooms).Room = room;
            var game = DispatchProxy.Create<IGame, RoomGame>();
            ((RoomGame)(object)game).Rooms = rooms;
            gameField.SetValue(null, game);
            var navigator = new NavigatorManager(new HabbiconDatabaseTests.TestDatabase(options.ConnectionString), NullLogger<NavigatorManager>.Instance);
            var habbo = new Habbo { Id = 7 };
            await navigator.SaveHomeRoom(habbo, 42);
            Assert.Equal(42u, habbo.HomeRoom);
            Assert.Equal(42u, connection.ExecuteScalar<uint>("SELECT home_room FROM users_settings WHERE user_id=7"));
            connection.Execute("CREATE TRIGGER reject_home BEFORE UPDATE ON users_settings FOR EACH ROW SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='forced'");
            habbo.HomeRoom = 0;
            await Assert.ThrowsAsync<MySqlException>(() => navigator.SaveHomeRoom(habbo, 42));
            Assert.Equal(0u, habbo.HomeRoom);
            connection.Execute("DROP TRIGGER reject_home; DELETE FROM users_settings WHERE user_id=7");
            await Assert.ThrowsAsync<System.Data.DBConcurrencyException>(() => navigator.SaveHomeRoom(habbo, 42));
            Assert.Equal(0u, habbo.HomeRoom);

            connection.Execute("INSERT INTO users_settings VALUES (7,0)");
            using var entered = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            using var saving = new ManualResetEventSlim();
            var gatedDatabase = new HabbiconDatabaseTests.TestDatabase(options.ConnectionString);
            gatedDatabase.BeforeConnection = () =>
            {
                entered.Set();
                Assert.True(release.Wait(TimeSpan.FromSeconds(10)));
            };
            var gatedNavigator = new NavigatorManager(gatedDatabase, NullLogger<NavigatorManager>.Instance);
            habbo.Access = UserAccess.Empty;
            var persistence = DispatchProxy.Create<IUserPersistenceService, SaveHome>();
            ((SaveHome)(object)persistence).Save = () =>
            {
                using var saved = new MySqlConnection(options.ConnectionString);
                saved.Execute("UPDATE users_settings SET home_room=@HomeRoom WHERE user_id=7", new { habbo.HomeRoom });
            };
            habbo.Persistence = persistence;
            var update = Task.Run(() => gatedNavigator.SaveHomeRoom(habbo, 42));
            Task? save = null;
            try
            {
                Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
                save = Task.Run(() => { saving.Set(); habbo.Save(); });
                Assert.True(saving.Wait(TimeSpan.FromSeconds(5)));
                Assert.NotSame(save, await Task.WhenAny(save, Task.Delay(150)));
            }
            finally
            {
                release.Set();
                try { await update.WaitAsync(TimeSpan.FromSeconds(10)); } catch { }
                if (save != null) { try { await save.WaitAsync(TimeSpan.FromSeconds(10)); } catch { } }
            }
            await update;
            await save!;
            Assert.Equal(42u, habbo.HomeRoom);
            Assert.Equal(42u, connection.ExecuteScalar<uint>("SELECT home_room FROM users_settings WHERE user_id=7"));
            gatedDatabase.BeforeConnection = () => throw new InvalidOperationException("A closed wallet must not write.");
            await Assert.ThrowsAsync<InvalidOperationException>(() => gatedNavigator.SaveHomeRoom(habbo, 99));
            Assert.Equal(42u, habbo.HomeRoom);
        }
        finally
        {
            gameField.SetValue(null, previousGame);
            admin.Execute($"DROP DATABASE IF EXISTS `{schema}`");
        }
    }
    public class SaveHome : DispatchProxy
    {
        public Action Save = null!;
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method!.Name != nameof(IUserPersistenceService.Save)) throw new NotSupportedException(method.Name);
            Save();
            return null;
        }
    }
    public class LoadedRoom : DispatchProxy
    {
        public Room Room = null!;
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method!.Name != nameof(IRoomManager.TryGetRoom)) throw new NotSupportedException(method.Name);
            args![1] = Room;
            return true;
        }
    }
    public class RoomGame : DispatchProxy
    {
        public IRoomManager Rooms = null!;
        protected override object? Invoke(MethodInfo? method, object?[]? args) =>
            method!.Name == "get_RoomManager" ? Rooms : throw new NotSupportedException(method.Name);
    }
    private sealed class DateRow { public DateTimeOffset? Value { get; set; } }
}
