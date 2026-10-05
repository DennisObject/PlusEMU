using System.Data;
using Dapper;
using MySqlConnector;
using Plus.Database;
using Plus.HabboHotel.Items.Wired.Variables;
using Xunit;

namespace Plus.Tests;

public sealed class WiredVariableUtcDatabaseTests
{
    [RoomComponentDatabaseFact]
    public void Migration39PreservesKeysAndMaterializesNullableUtcTimesWithProductionOptions()
    {
        var root = Environment.GetEnvironmentVariable("ROOM_COMPONENT_DATABASE")!;
        using var server = new MySqlConnection(root);
        server.Open();
        var schema = "task_wired_variable_utc_" + Guid.NewGuid().ToString("N");
        server.Execute($"CREATE DATABASE `{schema}`");
        try
        {
            var connectionString = new MySqlConnectionStringBuilder(root)
            {
                Database = schema, AllowZeroDateTime = true, ConvertZeroDateTime = true
            }.ConnectionString;
            using var connection = new MySqlConnection(connectionString);
            connection.Open();
            connection.Execute("""
                CREATE TABLE wired_variable_values (
                    definition_id INT UNSIGNED NOT NULL,target_kind TINYINT UNSIGNED NOT NULL,holder_id BIGINT NOT NULL,
                    value INT NOT NULL,created_at_ms BIGINT NOT NULL,updated_at_ms BIGINT NOT NULL,
                    PRIMARY KEY(definition_id,target_kind,holder_id)) ENGINE=InnoDB;
                INSERT INTO wired_variable_values VALUES
                    (1,0,10,1,0,-1),(1,0,11,2,2208988800123,2208988800456);
                """);
            var migration = File.ReadAllText(Path.GetFullPath(Path.Join(AppContext.BaseDirectory,
                "../../../../Database/Migrations/39_UseUtcWiredVariableTimes.sql")));
            connection.Execute(migration);
            connection.Execute("INSERT INTO wired_variable_values VALUES (1,0,12,3,@created,@updated)", new
            {
                created = new DateTime(2039, 1, 2, 3, 4, 5, 123, DateTimeKind.Utc).AddTicks(4560),
                updated = new DateTime(2039, 1, 2, 3, 4, 6, 654, DateTimeKind.Utc).AddTicks(3210)
            });

            var database = new ProbeDatabase(connectionString);
            var store = new DatabaseWiredVariableStore(database);
            var unknown = store.Read(new(1, WiredVariableTarget.User, 10))!;
            Assert.Null(unknown.CreatedAt); Assert.Null(unknown.UpdatedAt);
            var future = store.Read(new(1, WiredVariableTarget.User, 11))!;
            Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(2208988800123), future.CreatedAt);
            Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(2208988800456), future.UpdatedAt);
            var precise = store.Read(new(1, WiredVariableTarget.User, 12))!;
            Assert.Equal(new DateTimeOffset(2039, 1, 2, 3, 4, 5, 123, TimeSpan.Zero).AddTicks(4560), precise.CreatedAt);
            Assert.Equal(new DateTimeOffset(2039, 1, 2, 3, 4, 6, 654, TimeSpan.Zero).AddTicks(3210), precise.UpdatedAt);
            Assert.Equal(new[] { "definition_id", "target_kind", "holder_id" }, connection.Query<string>("""
                SELECT COLUMN_NAME FROM information_schema.statistics
                WHERE table_schema=DATABASE() AND table_name='wired_variable_values' AND index_name='PRIMARY'
                ORDER BY SEQ_IN_INDEX
                """));
            Assert.Equal(new[] { "created_at", "updated_at" }, connection.Query<string>("""
                SELECT COLUMN_NAME FROM information_schema.columns
                WHERE table_schema=DATABASE() AND table_name='wired_variable_values' AND DATA_TYPE='datetime'
                ORDER BY ORDINAL_POSITION
                """));
        }
        finally { server.Execute($"DROP DATABASE `{schema}`"); }
    }

    private sealed class ProbeDatabase(string connectionString) : IDatabase
    {
        public bool IsConnected() => true;
        public IDbConnection Connection() => new MySqlConnection(connectionString);
    }
}
