using System.Data;
using System.Text.Json;
using Dapper;
using MySqlConnector;
using Plus.Database;
using Plus.HabboHotel.Items.Wired.Variables;
using Plus.HabboHotel.Items.Wired.Configuration;
using Xunit;

namespace Plus.Tests;

public sealed class WiredVariableWidthDatabaseTests
{
    [RoomComponentDatabaseFact]
    public void MigrationPreservesExistingValuesAndDatabaseReadsRemainExactAtBothSignedLimits()
    {
        SqlMapper.AddTypeHandler(new UtcDateTimeOffsetHandler());
        var root = Environment.GetEnvironmentVariable("ROOM_COMPONENT_DATABASE")!;
        using var server = new MySqlConnection(root);
        server.Open();
        var schema = "task_wired_variable64_" + Guid.NewGuid().ToString("N");
        server.Execute($"CREATE DATABASE `{schema}`");

        try {
            var connectionString = new MySqlConnectionStringBuilder(root) { Database = schema }.ConnectionString;
            using var connection = new MySqlConnection(connectionString);
            connection.Open();
            connection.Execute("""
                CREATE TABLE rooms (id INT UNSIGNED PRIMARY KEY,owner VARCHAR(20));
                INSERT INTO rooms VALUES (1,'5');
                CREATE TABLE items (id INT UNSIGNED PRIMARY KEY,room_id INT UNSIGNED);
                INSERT INTO items VALUES (10,1);
                CREATE TABLE wired_item_configurations (item_id INT UNSIGNED PRIMARY KEY,box_name VARCHAR(64),configuration LONGTEXT,schema_version INT DEFAULT 2);
                CREATE TABLE wired_variable_locks (definition_id INT UNSIGNED PRIMARY KEY,retired TINYINT NOT NULL DEFAULT 0);
                CREATE TABLE wired_variable_values (
                    definition_id INT UNSIGNED NOT NULL, target_kind TINYINT UNSIGNED NOT NULL,
                    holder_id BIGINT NOT NULL, value INT NOT NULL, created_at DATETIME(6), updated_at DATETIME(6),
                    PRIMARY KEY(definition_id,target_kind,holder_id)
                );
                INSERT INTO wired_variable_values VALUES (10,1,1,2147483647,NULL,NULL),(10,1,2,-2147483648,NULL,NULL);
                """);
            var migration = File.ReadAllText(Path.GetFullPath(Path.Join(AppContext.BaseDirectory, "../../../../Database/Migrations/66_WiredVariableValueWidth.sql")));
            connection.Execute(migration);
            connection.Execute(migration); // Repeatable without changing values or keys.
            Assert.Equal(new long[] { int.MaxValue, int.MinValue }, connection.Query<long>("SELECT value FROM wired_variable_values ORDER BY holder_id"));

            foreach (var (id, number) in new[] { (3L, long.MinValue), (4L, long.MaxValue), (5L, 9007199254740993L) }) {
                connection.Execute("INSERT INTO wired_variable_values VALUES (10,1,@id,@number,NULL,NULL)", new { id, number });
            }

            Assert.True(WiredBoxRegistry.TryGet("wf_var_furni", out var variableDescriptor));
            var native = WiredNativeEditorProjection.DefaultNative(variableDescriptor) with { OwnedIntParams = [1, 10], Text = "wide" };
            Assert.True(WiredNativeEditorProjection.TryCompile(10, variableDescriptor, native, out var configuration));
            connection.Execute("INSERT INTO wired_item_configurations (item_id,box_name,configuration) VALUES (10,'wf_var_furni',@json)", new { json = JsonSerializer.Serialize(native) });
            Assert.True(WiredVariableDefinitions.TryDecode("wf_var_furni", 10, 1, 5, configuration, out var definition, out _));
            var authorization = new WiredVariableAuthorization(1, 5, [definition!]);
            var store = new DatabaseWiredVariableStore(new Database(connectionString));
            var keys = Enumerable.Range(3, 3).Select(id => new WiredVariableKey(10, WiredVariableTarget.Furni, id)).ToArray();
            Assert.Equal(long.MinValue, store.Read(keys[0])!.Value);
            Assert.Equal(long.MaxValue, store.Read(keys[1])!.Value);
            Assert.Equal(9007199254740993L, store.Read(keys[2])!.Value);
            var captured = store.ReadMany(keys);
            Assert.Equal(new[] { long.MinValue, long.MaxValue, 9007199254740993L }, keys.Select(key => captured[key].Value));
            Assert.Equal(new[] { long.MinValue, long.MaxValue, 9007199254740993L }, keys.Select(key => store.GetHolders(10)[key].Value));
            var wrapped = store.Mutate(keys[0], previous => previous! with { Value = unchecked(previous.Value - 1) }, authorization);
            Assert.True(wrapped.Changed);
            Assert.Equal(long.MinValue, wrapped.Before!.Value);
            Assert.Equal(long.MaxValue, wrapped.After!.Value);
            Assert.Equal(long.MaxValue, store.Read(keys[0])!.Value);
            var negative = store.Mutate(keys[1], previous => previous! with { Value = unchecked(previous.Value + 1) }, authorization);
            Assert.Equal(long.MinValue, negative.After!.Value);
            Assert.Equal(long.MinValue, store.Read(keys[1])!.Value);
            var saved = new WiredConfigurationStore(new Database(connectionString));

            foreach (var number in new[] { long.MinValue, long.MaxValue, 9007199254740993L }) {
                foreach (var name in new[] { "wf_act_give_var", "wf_act_change_var_val", "wf_cnd_var_val_match" }) {
                    Assert.True(WiredBoxRegistry.TryGet(name, out var descriptor));
                    var high = unchecked((int)(number >> 32));
                    var low = unchecked((int)number);
                    var scalar = WiredNativeEditorProjection.DefaultNative(descriptor) with {
                        OwnedIntParams = name == "wf_act_give_var" ? [0, high, low, 0]
                            : [0, name == "wf_cnd_var_val_match" ? 1 : 0, 0, high, low, 0],
                        VariableIds = name == "wf_act_give_var" ? ["furni:10"] : ["furni:10", "n"]
                    };
                    Assert.True(WiredNativeEditorProjection.TryCompile(20, descriptor, scalar, out var compiled));
                    saved.Save(20, descriptor, compiled);
                    var loaded = new WiredConfigurationStore(new Database(connectionString)).Load(20, descriptor)!;
                    Assert.Equal(compiled.IntParams.ToArray(), loaded.IntParams.ToArray());
                    Assert.True(WiredVariableExecutors.TryValidate(name, loaded, out _));
                }
            }

        }
        finally {
            server.Execute($"DROP DATABASE `{schema}`");
        }
    }
    private sealed class Database(string connectionString) : IDatabase
    {
        public bool IsConnected() => true;
        public IDbConnection Connection() => new MySqlConnection(connectionString);
    }
}
