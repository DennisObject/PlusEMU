using System.Data;
using Dapper;
using MySqlConnector;
using Plus.Database;
using Plus.HabboHotel.Users;
using Xunit;

namespace Plus.Tests;

public sealed class UserMaintenanceDatabaseTests
{
    [RoomComponentDatabaseFact]
    public void MaintenanceStoreWritesExactRowsRejectsMissingRowsAndRollsBackAFailure()
    {
        using var connection = new MySqlConnection(Environment.GetEnvironmentVariable("ROOM_COMPONENT_DATABASE"));
        connection.Open();
        var schema = "user_maintenance_" + Guid.NewGuid().ToString("N");
        connection.Execute($"CREATE DATABASE `{schema}`");

        try {
            connection.Execute($"USE `{schema}`");
            connection.Execute("""
                CREATE TABLE users (id INT PRIMARY KEY, credits INT NOT NULL, activity_points INT NOT NULL, vip_points INT NOT NULL, gotw_points INT NOT NULL, motto VARCHAR(100) NULL);
                INSERT INTO users VALUES (9, 100, 20, 30, 40, 'hello'), (10, 1, 2, 3, 4, NULL);
                """);
            connection.Execute(File.ReadAllText(HabbiconPacketTests.Repo("Database/Migrations/59_UserCurrencies.sql")));
            connection.Execute("""
                CREATE TRIGGER user_maintenance_fail BEFORE UPDATE ON user_currencies FOR EACH ROW BEGIN IF NEW.user_id = 10 AND NEW.amount = 77 THEN SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'forced failure'; END IF; END;
                """);
            var options = new MySqlConnectionStringBuilder(connection.ConnectionString) { Database = schema, Pooling = false, AllowZeroDateTime = true, ConvertZeroDateTime = true };
            var store = new UserMaintenanceStore(new ProbeDatabase(options.ConnectionString));

            Assert.Equal(100, store.ReadCurrency(9, UserCurrency.Credits));
            Assert.True(store.TryWriteCurrency(9, UserCurrency.Credits, 150));
            Assert.Equal(150, store.ReadCurrency(9, UserCurrency.Credits));
            Assert.Equal(20, store.ReadCurrency(9, UserCurrency.Duckets));
            Assert.True(store.TryWriteCurrency(9, UserCurrency.Credits, 150));
            Assert.True(store.TryWriteCurrency(9, UserCurrency.Diamonds, 31));
            Assert.True(store.TryWriteCurrency(9, UserCurrency.Gotw, 41));
            Assert.Equal(150, connection.QuerySingle<int>("SELECT credits FROM users WHERE id = 9"));
            Assert.Equal([(0, 20), (5, 31), (103, 41)], connection.Query<(int, int)>("SELECT type, amount FROM user_currencies WHERE user_id = 9 ORDER BY type"));

            // A balance the user never had reads 0 and is created on write.
            connection.Execute("DELETE FROM user_currencies WHERE user_id = 9 AND type = 103");
            Assert.Equal(0, store.ReadCurrency(9, UserCurrency.Gotw));
            Assert.True(store.TryWriteCurrency(9, UserCurrency.Gotw, 5));
            Assert.Equal(5, store.ReadCurrency(9, UserCurrency.Gotw));

            Assert.False(store.TryWriteCurrency(99, UserCurrency.Credits, 1));
            Assert.Null(store.ReadCurrency(99, UserCurrency.Credits));
            Assert.Null(store.ReadCurrency(99, UserCurrency.Duckets));
            Assert.False(store.TryWriteCurrency(99, UserCurrency.Duckets, 1));
            Assert.Equal(0, connection.QuerySingle<int>("SELECT COUNT(*) FROM user_currencies WHERE user_id = 99"));
            Assert.Equal(0, connection.QuerySingle<int>("SELECT COUNT(*) FROM users WHERE id = 99"));

            Assert.Equal("hello", store.ReadMotto(9));
            Assert.Null(store.ReadMotto(10));
            Assert.Null(store.ReadMotto(99));

            Assert.Throws<MySqlException>(() => store.TryWriteCurrency(10, UserCurrency.Duckets, 77));
            Assert.Equal(2, connection.QuerySingle<int>("SELECT amount FROM user_currencies WHERE user_id = 10 AND type = 0"));
            Assert.Equal(1, connection.QuerySingle<int>("SELECT credits FROM users WHERE id = 10"));
        }
        finally {
            connection.Execute($"DROP DATABASE IF EXISTS `{schema}`");
        }
    }

    private sealed class ProbeDatabase(string connectionString) : IDatabase
    {
        public bool IsConnected() => true;
        public IDbConnection Connection() => new MySqlConnection(connectionString);
    }
}
