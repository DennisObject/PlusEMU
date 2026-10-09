using System.Data;
using Dapper;
using MySqlConnector;
using Plus.Database;
using Plus.HabboHotel.Rooms;
using Xunit;

namespace Plus.Tests;

public sealed class TradeCommitDatabaseTests
{
    [RoomComponentDatabaseFact]
    public void ACommittedTradeMovesItemsCreditsAndLogTogetherAndAnyFailureLeavesNothing()
    {
        var root = Environment.GetEnvironmentVariable("ROOM_COMPONENT_DATABASE")!;
        var schema = "trade_commit_" + Guid.NewGuid().ToString("N");
        using var server = new MySqlConnection(root);
        server.Execute($"CREATE DATABASE `{schema}`");

        try {
            var database = new ProbeDatabase(new MySqlConnectionStringBuilder(root) { Database = schema }.ConnectionString);
            using var connection = database.Connection();
            connection.Execute("""
                CREATE TABLE items (id INT UNSIGNED PRIMARY KEY, user_id INT NOT NULL, room_id INT UNSIGNED NOT NULL DEFAULT 0);
                CREATE TABLE users (id INT PRIMARY KEY, credits INT NOT NULL);
                CREATE TABLE logs_client_trade (id INT AUTO_INCREMENT PRIMARY KEY, `1id` INT, `2id` INT, `1items` TEXT, `2items` TEXT, `timestamp` DATETIME(6) NULL);
                INSERT INTO items (id, user_id) VALUES (1, 10), (2, 10), (3, 20);
                INSERT INTO users VALUES (10, 100), (20, 200);
                """);
            var store = (ITradeStore)new RoomTradingComponent(database, TimeProvider.System, TestRoomSettings.Empty);
            var swap = new TradeTransfer[] { new(1, 10, 20, false), new(3, 20, 10, false) };
            (int, int, int, int, int) State() => connection.QuerySingle<(int, int, int, int, int)>(
                "SELECT (SELECT user_id FROM items WHERE id=1),(SELECT user_id FROM items WHERE id=3),(SELECT credits FROM users WHERE id=10),(SELECT credits FROM users WHERE id=20),(SELECT COUNT(*) FROM logs_client_trade)");
            var applied = 0;

            // the in-memory step refuses: the rows, the credits and the log roll back together
            Assert.False(store.Commit(swap, 10, 20, 150, 250, "1;", "3;", () => { applied++; return false; }));
            Assert.Equal((10, 20, 100, 200, 0), State());

            // an item the sender no longer owns: nothing is written and the in-memory step never runs
            Assert.False(store.Commit([new(2, 20, 10, false)], 10, 20, 150, 250, "", "2;", () => { applied++; return true; }));
            Assert.Equal((10, 20, 100, 200, 0), State());
            Assert.Equal(1, applied);

            // a credit row that does not exist: the transfers roll back
            Assert.False(store.Commit(swap, 10, 99, 150, 250, "1;", "3;", () => true));
            Assert.Equal((10, 20, 100, 200, 0), State());

            // a redeemed item is deleted from the sender, a traded one changes owner, credits are set and the log is written
            Assert.True(store.Commit([new(1, 10, 20, false), new(3, 20, 10, true)], 10, 20, 130, 170, "1;", "3;", () => true));
            Assert.Equal((20, 130, 170, 1), connection.QuerySingle<(int, int, int, int)>(
                "SELECT (SELECT user_id FROM items WHERE id=1),(SELECT credits FROM users WHERE id=10),(SELECT credits FROM users WHERE id=20),(SELECT COUNT(*) FROM logs_client_trade)"));
            Assert.Equal(0, connection.QuerySingle<int>("SELECT COUNT(*) FROM items WHERE id=3"));
            Assert.Equal((10, 20, "1;", "3;"), connection.QuerySingle<(int, int, string, string)>("SELECT `1id`,`2id`,`1items`,`2items` FROM logs_client_trade"));
        }
        finally {
            server.Execute($"DROP DATABASE `{schema}`");
        }
    }

    private sealed class ProbeDatabase(string connectionString) : IDatabase
    {
        public bool IsConnected() => true;
        public IDbConnection Connection() => new MySqlConnection(connectionString);
    }
}
