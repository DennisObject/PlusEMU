using System.Data;
using System.Text.RegularExpressions;
using Dapper;
using MySqlConnector;
using Plus.Database;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Rooms.AI;
using Xunit;

namespace Plus.Tests;

public sealed class PristineBotClothingDatabaseTests
{
    [RoomComponentDatabaseFact]
    public void BotSpeechAndPlacementPreserveLegacyEnumLabelsInBothSqlModes()
    {
        Probe((connection, database) =>
        {
            CreateShippedTable(connection, "bots");
            CreateShippedTable(connection, "bots_speech");
            connection.Execute("INSERT INTO bots (id,user_id,room_id,name,motto,look) VALUES (10,7,42,'guide','hello','hd-180-1')");
            var store = new BotManagementStore(database);

            foreach (var mode in new[] { "", "STRICT_ALL_TABLES" })
            {
                connection.Execute("SET SESSION sql_mode=@mode", new
                {
                    mode
                });
                // Each store call opens its own connection. Set its session mode explicitly.
                database.SqlMode = mode;

                foreach (var automatic in new[] { true, false })
                {
                    Assert.Equal(["hello", "world"], store.SaveSpeech(10, 42, ["hello", "world"], automatic, 12, true));
                    Assert.Equal(automatic ? "true" : "false", connection.QuerySingle<string>("SELECT automatic_chat FROM bots WHERE id=10"));
                    connection.Execute("UPDATE bots SET room_id=0 WHERE id=10");
                    var placed = store.Place(10, 7, 42, 2, 3);
                    Assert.Equal(automatic, placed.AutomaticChat);
                    Assert.True(placed.MixSentences);
                    Assert.Equal(["hello", "world"], placed.Speech);
                    Assert.Equal((2, 3), connection.QuerySingle<(int, int)>("SELECT x,y FROM bots WHERE id=10"));
                }

                Assert.Throws<InvalidOperationException>(() => store.SaveSpeech(10, 99, ["replacement"], true, 15, false));
                Assert.Equal(["hello", "world"], connection.Query<string>("SELECT text FROM bots_speech WHERE bot_id=10").ToArray());
                Assert.Equal("false", connection.QuerySingle<string>("SELECT automatic_chat FROM bots WHERE id=10"));
            }
        });
    }

    [RoomComponentDatabaseFact]
    public void ClothingRedemptionMaterializesExistingVarcharPartAndCommitsAtomically()
    {
        Probe((connection, database) =>
        {
            CreateShippedTable(connection, "user_clothing");
            connection.Execute("""
                CREATE TABLE users (id INT PRIMARY KEY);
                CREATE TABLE items (id INT UNSIGNED PRIMARY KEY, user_id INT NOT NULL, room_id INT UNSIGNED NOT NULL);
                INSERT INTO users VALUES (7);
                INSERT INTO items VALUES (10,7,42),(11,7,42);
                INSERT INTO user_clothing (user_id,part_id,part) VALUES (7,'100','existing');
                """);
            var store = new ItemRedemptionStore(database);
            var parts = store.ConsumeClothing(10, 7, 42, "new", [100, 200, 100]);
            Assert.Equal([100, 200], parts.Select(part => part.PartId).ToArray());
            Assert.Equal(["existing", "new"], parts.Select(part => part.Part).ToArray());
            Assert.Equal(2, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM user_clothing"));
            Assert.Equal(0, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM items WHERE id=10"));

            connection.Execute("CREATE TRIGGER reject_part BEFORE INSERT ON user_clothing FOR EACH ROW SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='forced'");
            Assert.Throws<MySqlException>(() => store.ConsumeClothing(11, 7, 42, "new", [300]));
            Assert.Equal(1, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM items WHERE id=11"));
            Assert.Equal(2, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM user_clothing"));
        });
    }

    private static void CreateShippedTable(MySqlConnection connection, string name)
    {
        var sql = File.ReadAllText(Path.GetFullPath(Path.Join(AppContext.BaseDirectory, "../../../../Resources/SQLs/Original Database.sql")));
        var definition = Regex.Match(sql, $@"CREATE TABLE `{name}` \(.*?;", RegexOptions.Singleline);
        Assert.True(definition.Success);
        connection.Execute(definition.Value);
    }

    private static void Probe(Action<MySqlConnection, ProbeDatabase> run)
    {
        var options = new MySqlConnectionStringBuilder(Environment.GetEnvironmentVariable("ROOM_COMPONENT_DATABASE")!)
        {
            Database = "mysql",
            Pooling = false,
            AllowZeroDateTime = true,
            ConvertZeroDateTime = true
        };
        using var admin = new MySqlConnection(options.ConnectionString);
        admin.Open();
        var schema = "task_bot_clothing_" + Guid.NewGuid().ToString("N");
        admin.Execute($"CREATE DATABASE `{schema}`");

        try
        {
            options.Database = schema;
            using var connection = new MySqlConnection(options.ConnectionString);
            connection.Open();
            run(connection, new ProbeDatabase(options.ConnectionString));
        }
        finally { admin.Execute($"DROP DATABASE IF EXISTS `{schema}`"); }
    }

    private sealed class ProbeDatabase(string connectionString) : IDatabase
    {
        public string SqlMode { get; set; } = "STRICT_ALL_TABLES";
        public bool IsConnected() => true;
        public IDbConnection Connection()
        {
            return new ModeConnection(connectionString, SqlMode);
        }
    }

    private sealed class ModeConnection(string connectionString, string mode) : IDbConnection
    {
        private readonly MySqlConnection _connection = new(connectionString);
        public string ConnectionString
        {
            get => _connection.ConnectionString; set => _connection.ConnectionString = value;
        }
        public int ConnectionTimeout => _connection.ConnectionTimeout;
        public string Database => _connection.Database;
        public ConnectionState State => _connection.State;
        public IDbTransaction BeginTransaction() => _connection.BeginTransaction();
        public IDbTransaction BeginTransaction(IsolationLevel isolation) => _connection.BeginTransaction(isolation);
        public void ChangeDatabase(string database) => _connection.ChangeDatabase(database);
        public void Close() => _connection.Close();
        public IDbCommand CreateCommand() => _connection.CreateCommand();
        public void Open()
        {
            _connection.Open();
            _connection.Execute("SET SESSION sql_mode=@mode", new
            {
                mode
            });
        }
        public void Dispose() => _connection.Dispose();
    }
}
