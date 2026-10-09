using System.Text.RegularExpressions;
using Dapper;
using MySqlConnector;
using Plus.HabboHotel.Users;
using Plus.HabboHotel.Users.UserData;
using Xunit;

namespace Plus.Tests;

// Database/Migrations/59_UserCurrencies.sql and the user_currencies store on a disposable schema.
public sealed class UserCurrencyDatabaseTests
{
    [RoomComponentDatabaseFact]
    public void MigrationMovesEveryBalanceToItsTypeAndDropsTheUserColumns()
    {
        InSchema((connection, _) =>
        {
            var pristine = File.ReadAllText(HabbiconPacketTests.Repo("Resources/SQLs/Original Database.sql"));
            connection.Execute(Regex.Match(pristine, @"CREATE TABLE `users` \([\s\S]*?\) ENGINE=[^;]+;").Value);
            connection.Execute("""
                INSERT INTO users (id, username, auth_ticket, credits, activity_points, vip_points, gotw_points) VALUES
                    (1, 'fresh', 'a', 50000, 5000, 0, 0),
                    (2, 'rich', 'b', 1, 0, 25, 3),
                    (3, 'empty', 'c', 2, NULL, NULL, NULL),
                    (4, 'odd', 'd', 3, -5, 7, 0);
                """);

            connection.Execute(File.ReadAllText(HabbiconPacketTests.Repo("Database/Migrations/59_UserCurrencies.sql")));

            Assert.Equal([(1, 0, 5000), (2, 5, 25), (2, 103, 3), (4, 0, -5), (4, 5, 7)],
                connection.Query<(int, int, int)>("SELECT user_id, type, amount FROM user_currencies ORDER BY user_id, type"));
            Assert.Equal([50000, 1, 2, 3], connection.Query<int>("SELECT credits FROM users ORDER BY id"));
            Assert.Equal(0, connection.QuerySingle<int>("SELECT COUNT(*) FROM information_schema.columns WHERE table_schema = DATABASE() AND table_name = 'users' " +
                "AND column_name IN ('activity_points', 'vip_points', 'gotw_points')"));

            // Rows belong to an existing user, are removed with it and never have a negative type.
            connection.Execute("DELETE FROM users WHERE id = 2");
            Assert.Equal(0, connection.QuerySingle<int>("SELECT COUNT(*) FROM user_currencies WHERE user_id = 2"));
            Assert.Throws<MySqlException>(() => connection.Execute("INSERT INTO user_currencies (user_id, type, amount) VALUES (99, 0, 1)"));
            Assert.Throws<MySqlException>(() => connection.Execute("INSERT INTO user_currencies (user_id, type, amount) VALUES (1, -1, 1)"));
        });
    }

    [RoomComponentDatabaseFact]
    public async Task PristineSchemaSavesAndLoadsEveryActivityPointType()
    {
        await InSchemaAsync(async (connection, database) =>
        {
            connection.Execute(File.ReadAllText(HabbiconPacketTests.Repo("Resources/SQLs/Original Database.sql")), commandTimeout: 900);
            Assert.Equal(0, connection.QuerySingle<int>("SELECT COUNT(*) FROM information_schema.columns WHERE table_schema = DATABASE() AND table_name = 'users' " +
                "AND column_name IN ('activity_points', 'vip_points', 'gotw_points')"));
            connection.Execute("INSERT INTO users (id, username, auth_ticket, credits) VALUES (7, 'wallet', '', 10); " +
                "INSERT INTO users_settings (user_id) VALUES (7); INSERT INTO user_statistics (id) VALUES (7); " +
                "INSERT INTO user_currencies (user_id, type, amount) VALUES (7, 0, 1), (7, 104, 2)");

            var habbo = new Habbo
            {
                Id = 7,
                Credits = 11,
                SessionStartedAt = DateTimeOffset.UtcNow,
                HabboStats = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, "", 0)
            };
            habbo.Currencies.Load(UserCurrencyStore.Load(connection, 7));
            habbo.Duckets += 4;
            habbo.Diamonds = 6;
            habbo.GotwPoints = 7;
            habbo.Currencies[101] = 8;
            new UserPersistenceService(database, TimeProvider.System, TestGameClientManager.Empty).Save(habbo);

            Assert.Equal([(0, 5), (5, 6), (101, 8), (103, 7), (104, 2)],
                connection.Query<(int, int)>("SELECT type, amount FROM user_currencies WHERE user_id = 7 ORDER BY type"));
            Assert.Equal(11, connection.QuerySingle<int>("SELECT credits FROM users WHERE id = 7"));

            var factory = new UserDataFactory(null!, database, [], null!, null!, null!, new Plus.HabboHotel.Rooms.RoomVisitRecorder(database, TimeProvider.System),
                TimeProvider.System, TestRoomAchievements.Unused, TestGameClientManager.Empty, TestRoomManager.Unused);
            var loaded = (await factory.GetUserDataByIdAsync(7))!;
            Assert.Equal((11, 5, 6, 7, 8, 2), (loaded.Credits, loaded.Duckets, loaded.Diamonds, loaded.GotwPoints, loaded.Currencies[101], loaded.Currencies[104]));

            // Balances change in the database by delta or by value; a missing row reads 0.
            UserCurrencyStore.Add(connection, 7, 105, 3);
            UserCurrencyStore.Add(connection, 7, 105, -1);
            UserCurrencyStore.Set(connection, 7, 0, 9);
            Assert.Equal((2, 9, 0), (UserCurrencyStore.Get(connection, 7, 105), UserCurrencyStore.Get(connection, 7, 0), UserCurrencyStore.Get(connection, 7, 102)));
            Assert.Throws<ArgumentOutOfRangeException>(() => UserCurrencyStore.Set(connection, 7, -1, 1));
        });
    }

    private static void InSchema(Action<MySqlConnection, HabbiconDatabaseTests.TestDatabase> run) =>
        InSchemaAsync((connection, database) =>
        {
            run(connection, database);

            return Task.CompletedTask;
        }).GetAwaiter().GetResult();

    private static async Task InSchemaAsync(Func<MySqlConnection, HabbiconDatabaseTests.TestDatabase, Task> run)
    {
        var options = new MySqlConnectionStringBuilder(Environment.GetEnvironmentVariable("ROOM_COMPONENT_DATABASE")!)
        {
            AllowUserVariables = true,
            AllowZeroDateTime = true,
            ConvertZeroDateTime = true
        };
        using var admin = new MySqlConnection(options.ConnectionString);
        admin.Open();
        var schema = "task_user_currencies_" + Guid.NewGuid().ToString("N");
        admin.Execute($"CREATE DATABASE `{schema}`");

        try {
            options.Database = schema;
            using var connection = new MySqlConnection(options.ConnectionString);
            connection.Open();
            await run(connection, new HabbiconDatabaseTests.TestDatabase(options.ConnectionString));
        }
        finally {
            admin.Execute($"DROP DATABASE `{schema}`");
        }
    }
}
