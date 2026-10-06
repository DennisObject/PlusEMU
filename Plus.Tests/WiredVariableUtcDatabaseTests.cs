using System.Data;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Dapper;
using MySqlConnector;
using Plus.Database;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.Wired.Configuration;
using Plus.HabboHotel.Items.Wired.Variables;
using Plus.HabboHotel.Rooms;
using Xunit;

namespace Plus.Tests;

public sealed class WiredVariableUtcDatabaseTests
{
    [RoomComponentDatabaseFact]
    public void Migration39PreservesKeysAndMaterializesUtcTimesInStrictAndPermissiveModes()
    {
        var root = Environment.GetEnvironmentVariable("ROOM_COMPONENT_DATABASE")!;

        foreach (var sqlMode in new[] { "", "STRICT_ALL_TABLES" }) {
            RunMigrationProbe(root, sqlMode);
        }
    }

    [RoomComponentDatabaseFact]
    public void PristineRewardStateDefinitionMatchesCanonicalMigration17()
    {
        var root = Environment.GetEnvironmentVariable("ROOM_COMPONENT_DATABASE")!;
        using var server = new MySqlConnection(root);
        server.Open();
        var schema = "task_wired_reward_pristine_" + Guid.NewGuid().ToString("N");
        server.Execute($"CREATE DATABASE `{schema}`");

        try {
            using var connection = new MySqlConnection(new MySqlConnectionStringBuilder(root) { Database = schema }.ConnectionString);
            connection.Open();
            var pristine = File.ReadAllText(Path.GetFullPath(Path.Join(AppContext.BaseDirectory,
                "../../../../Resources/SQLs/Original Database.sql")));
            const string marker = "CREATE TABLE IF NOT EXISTS `wired_reward_state`";
            var start = pristine.LastIndexOf(marker, StringComparison.Ordinal);
            Assert.True(start >= 0);
            var end = pristine.IndexOf(';', start);
            Assert.True(end > start);
            connection.Execute(pristine[start..(end + 1)]);

            var columns = connection.Query<ColumnMetadata>("""
                SELECT COLUMN_NAME AS Name,DATA_TYPE AS DataType,IS_NULLABLE AS IsNullable,ORDINAL_POSITION AS Ordinal
                FROM information_schema.columns
                WHERE table_schema=DATABASE() AND table_name='wired_reward_state'
                ORDER BY ORDINAL_POSITION
                """).ToArray();
            Assert.Collection(columns,
                item => { Assert.Equal("item_id", item.Name); Assert.Equal("int", item.DataType); Assert.Equal("NO", item.IsNullable); Assert.Equal(1, item.Ordinal); },
                claims => { Assert.Equal("claims", claims.Name); Assert.Equal("longtext", claims.DataType); Assert.Equal("NO", claims.IsNullable); Assert.Equal(2, claims.Ordinal); });
            Assert.Equal("item_id", connection.QuerySingle<string>("""
                SELECT COLUMN_NAME FROM information_schema.statistics
                WHERE table_schema=DATABASE() AND table_name='wired_reward_state' AND index_name='PRIMARY'
                """));
        }
        finally {
            server.Execute($"DROP DATABASE `{schema}`");
        }
    }

    [RoomComponentDatabaseFact]
    public void ConfigurationSaveKeepsLegacyUnknownCreationAndNoOpDoesNotReadClock()
    {
        var root = Environment.GetEnvironmentVariable("ROOM_COMPONENT_DATABASE")!;
        using var server = new MySqlConnection(root);
        server.Open();
        var schema = "task_wired_variable_config_" + Guid.NewGuid().ToString("N");
        server.Execute($"CREATE DATABASE `{schema}`");

        try {
            var connectionString = new MySqlConnectionStringBuilder(root)
            {
                Database = schema,
                AllowZeroDateTime = true,
                ConvertZeroDateTime = true
            }.ConnectionString;
            using var connection = new MySqlConnection(connectionString);
            connection.Open();
            var current = new WiredConfiguration { IntParams = [10, 5], Text = "legacy" };
            connection.Execute("""
                CREATE TABLE rooms (id INT UNSIGNED PRIMARY KEY,owner VARCHAR(32) NOT NULL) ENGINE=InnoDB;
                CREATE TABLE items (id INT UNSIGNED PRIMARY KEY,room_id INT UNSIGNED NOT NULL) ENGINE=InnoDB;
                CREATE TABLE wired_item_configurations (
                    item_id INT UNSIGNED PRIMARY KEY,box_name VARCHAR(64) NOT NULL,schema_version INT NOT NULL,configuration LONGTEXT NOT NULL) ENGINE=InnoDB;
                CREATE TABLE wired_variable_locks (
                    definition_id INT UNSIGNED PRIMARY KEY,retired TINYINT UNSIGNED NOT NULL DEFAULT 0) ENGINE=InnoDB;
                CREATE TABLE wired_variable_values (
                    definition_id INT UNSIGNED NOT NULL,target_kind TINYINT UNSIGNED NOT NULL,holder_id BIGINT NOT NULL,value INT NOT NULL,
                    created_at DATETIME(6) NULL,updated_at DATETIME(6) NULL,PRIMARY KEY(definition_id,target_kind,holder_id)) ENGINE=InnoDB;
                INSERT INTO rooms VALUES (1,'5');
                INSERT INTO items VALUES (10,1);
                INSERT INTO wired_item_configurations VALUES (10,'wf_var_room',1,@configuration);
                INSERT INTO wired_variable_values VALUES (10,3,0,5,NULL,'1970-01-01 00:00:01.000000');
                """, new { configuration = JsonSerializer.Serialize(current) });
            var database = new ProbeDatabase(connectionString);
            var clock = new CountingTimeProvider(DateTimeOffset.FromUnixTimeMilliseconds(3000));
            var module = new WiredVariableModule(1, new DatabaseWiredVariableDirectory(database),
                new DatabaseWiredVariableStore(database), clock);
            var room = (Room)RuntimeHelpers.GetUninitializedObject(typeof(Room));
            room.Id = 1;
            room.OwnerId = 5;
            Assert.True(WiredBoxRegistry.TryGet("wf_var_room", out var descriptor));
            var box = new WiredVariableDefinitionBox(room, new Item { Id = 10 }, descriptor,
                new WiredVariableConfigurationPersistence(database, module, clock), new(module));
            box.ApplyConfiguration(current);
            var changed = current with { IntParams = [10, 6] };

            Assert.True(WiredConfigurationSave.TrySave(box, changed, new RejectConfigurationStore(), out var error), error);
            var value = new DatabaseWiredVariableStore(database).Read(new(10, WiredVariableTarget.Global, 0))!;
            Assert.Null(value.CreatedAt);
            Assert.Equal(clock.Now, value.UpdatedAt);
            Assert.Equal(1, clock.Reads);
            Assert.Single(module.DrainChanges());
            Assert.True(WiredConfigurationSave.TrySave(box, changed, new RejectConfigurationStore(), out error), error);
            Assert.Equal(1, clock.Reads);
            Assert.Empty(module.DrainChanges());
        }
        finally {
            server.Execute($"DROP DATABASE `{schema}`");
        }
    }

    private static void RunMigrationProbe(string root, string sqlMode)
    {
        using var server = new MySqlConnection(root);
        server.Open();
        var schema = "task_wired_variable_utc_" + Guid.NewGuid().ToString("N");
        server.Execute($"CREATE DATABASE `{schema}`");

        try {
            var connectionString = new MySqlConnectionStringBuilder(root)
            {
                Database = schema,
                AllowZeroDateTime = true,
                ConvertZeroDateTime = true
            }.ConnectionString;
            using var connection = new MySqlConnection(connectionString);
            connection.Open();
            connection.Execute("SET SESSION sql_mode=@sqlMode", new { sqlMode });
            connection.Execute("""
                CREATE TABLE wired_variable_values (
                    definition_id INT UNSIGNED NOT NULL,target_kind TINYINT UNSIGNED NOT NULL,holder_id BIGINT NOT NULL,
                    value INT NOT NULL,created_at_ms BIGINT NOT NULL,updated_at_ms BIGINT NOT NULL,
                    PRIMARY KEY(definition_id,target_kind,holder_id)) ENGINE=InnoDB;
                INSERT INTO wired_variable_values VALUES
                    (1,0,10,1,0,-1),
                    (1,0,11,2,2208988800123,2208988800456),
                    (1,0,12,3,9223372036854775807,9223372036854775807),
                    (1,0,13,4,253402300799999,253402300799999);
                """);
            var migration = File.ReadAllText(Path.GetFullPath(Path.Join(AppContext.BaseDirectory,
                "../../../../Database/Migrations/39_UseUtcWiredVariableTimes.sql")));
            connection.Execute(migration);
            connection.Execute("INSERT INTO wired_variable_values VALUES (1,0,14,5,@created,@updated)", new
            {
                created = new DateTime(2039, 1, 2, 3, 4, 5, 123, DateTimeKind.Utc).AddTicks(4560),
                updated = new DateTime(2039, 1, 2, 3, 4, 6, 654, DateTimeKind.Utc).AddTicks(3210)
            });

            var store = new DatabaseWiredVariableStore(new ProbeDatabase(connectionString));
            var unknown = store.Read(new(1, WiredVariableTarget.User, 10))!;
            Assert.Null(unknown.CreatedAt);
            Assert.Null(unknown.UpdatedAt);
            var future = store.Read(new(1, WiredVariableTarget.User, 11))!;
            Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(2208988800123), future.CreatedAt);
            Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(2208988800456), future.UpdatedAt);
            var outOfRange = store.Read(new(1, WiredVariableTarget.User, 12))!;
            Assert.Null(outOfRange.CreatedAt);
            Assert.Null(outOfRange.UpdatedAt);
            var last = DateTimeOffset.FromUnixTimeMilliseconds(253402300799999);
            Assert.Equal(last, store.Read(new(1, WiredVariableTarget.User, 13))!.CreatedAt);
            var precise = store.Read(new(1, WiredVariableTarget.User, 14))!;
            Assert.Equal(new DateTimeOffset(2039, 1, 2, 3, 4, 5, 123, TimeSpan.Zero).AddTicks(4560), precise.CreatedAt);
            Assert.Equal(new DateTimeOffset(2039, 1, 2, 3, 4, 6, 654, TimeSpan.Zero).AddTicks(3210), precise.UpdatedAt);

            Assert.Equal(new[] { "definition_id", "target_kind", "holder_id" }, connection.Query<string>("""
                SELECT COLUMN_NAME FROM information_schema.statistics
                WHERE table_schema=DATABASE() AND table_name='wired_variable_values' AND index_name='PRIMARY'
                ORDER BY SEQ_IN_INDEX
                """));
            var columns = connection.Query<ColumnMetadata>("""
                SELECT COLUMN_NAME AS Name,DATA_TYPE AS DataType,IS_NULLABLE AS IsNullable,
                       DATETIME_PRECISION AS DateTimePrecision,ORDINAL_POSITION AS Ordinal
                FROM information_schema.columns
                WHERE table_schema=DATABASE() AND table_name='wired_variable_values' AND COLUMN_NAME IN ('created_at','updated_at')
                ORDER BY ORDINAL_POSITION
                """).ToArray();
            Assert.Collection(columns,
                created => AssertTimestampColumn(created, "created_at", 5),
                updated => AssertTimestampColumn(updated, "updated_at", 6));
        }
        finally {
            server.Execute($"DROP DATABASE `{schema}`");
        }
    }

    private static void AssertTimestampColumn(ColumnMetadata column, string name, int ordinal)
    {
        Assert.Equal(name, column.Name);
        Assert.Equal("datetime", column.DataType);
        Assert.Equal("YES", column.IsNullable);
        Assert.Equal(6, column.DateTimePrecision);
        Assert.Equal(ordinal, column.Ordinal);
    }

    private sealed class ColumnMetadata
    {
        public string Name { get; set; } = "";
        public string DataType { get; set; } = "";
        public string IsNullable { get; set; } = "";
        public int? DateTimePrecision { get; set; }
        public int Ordinal { get; set; }
    }

    private sealed class ProbeDatabase(string connectionString) : IDatabase
    {
        public bool IsConnected() => true;
        public IDbConnection Connection() => new MySqlConnection(connectionString);
    }

    private sealed class CountingTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; } = now;
        public int Reads { get; private set; }
        public override DateTimeOffset GetUtcNow()
        {
            Reads++;

            return Now;
        }
    }

    private sealed class RejectConfigurationStore : IWiredConfigurationStore
    {
        public WiredConfiguration? Load(uint itemId, WiredBoxDescriptor descriptor) => throw new NotSupportedException();
        public void Reset(IReadOnlyCollection<uint> itemIds) => throw new NotSupportedException();
        public void Save(uint itemId, WiredBoxDescriptor descriptor, WiredConfiguration configuration) => throw new NotSupportedException();
    }
}
