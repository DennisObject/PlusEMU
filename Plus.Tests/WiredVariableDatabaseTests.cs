using System.Data;
using System.Diagnostics;
using System.Text.Json;
using Dapper;
using MySqlConnector;
using Plus.Database;
using Plus.Database.Interfaces;
using Plus.HabboHotel.Items.Wired.Configuration;
using Plus.HabboHotel.Items.Wired.Variables;
using Xunit;
using Xunit.Abstractions;

namespace Plus.Tests;

public sealed class WiredVariableDatabaseFactAttribute : FactAttribute
{
    public WiredVariableDatabaseFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("WIRED_VARIABLE_PREVIEW_CONFIG") is null)
            Skip = "Opt-in isolated plus-wired-preview database probe.";
    }
}

public sealed class WiredVariableDatabaseTests(ITestOutputHelper output)
{
    [WiredVariableDatabaseFact]
    public async Task RealMySqlPersistenceAuthorizationConcurrencyAndBatchedReads()
    {
        var connectionString = GuardedConnectionString();
        using var admin = new MySqlConnection(connectionString); await admin.OpenAsync();
        Assert.Equal(2, admin.ExecuteScalar<int>("SELECT COUNT(*) FROM information_schema.tables WHERE table_schema=DATABASE() AND table_name IN ('wired_variable_locks','wired_variable_values')"));
        var users = new List<uint>(); var rooms = new List<uint>(); var items = new List<uint>();
        try
        {
            var suffix = Guid.NewGuid().ToString("N")[..10];
            var owner = Insert(admin, "users", new() { ["username"] = "wv_" + suffix, ["password"] = Guid.NewGuid().ToString("N"), ["mail"] = suffix + "@invalid" }); users.Add(owner);
            var room = Insert(admin, "rooms", new() { ["owner"] = owner.ToString(), ["caption"] = "Disposable wired variable probe", ["model_name"] = admin.QueryFirst<string>("SELECT id FROM room_models LIMIT 1") }); rooms.Add(room);
            var database = new ProbeDatabase(connectionString);
            var directory = new DatabaseWiredVariableDirectory(database);
            var store = new DatabaseWiredVariableStore(database);
            var baseItem = admin.QueryFirst<uint>("SELECT id FROM furniture LIMIT 1");
            for (var i = 0; i < 6; i++)
            {
                var item = Insert(admin, "items", new() { ["user_id"] = owner, ["room_id"] = room, ["base_item"] = baseItem, ["extra_data"] = "", ["wall_pos"] = "" }); items.Add(item);
                admin.Execute("INSERT INTO wired_item_configurations(item_id,box_name,schema_version,configuration) VALUES (@item,'wf_var_user',1,@configuration)",
                    new { item, configuration = JsonSerializer.Serialize(new WiredConfiguration { IntParams = [1, 10], Text = "probe" + i }) });
            }
            Assert.Equal(owner, directory.GetRoomOwner(room)); Assert.Equal(owner, directory.Find(items[0])!.OwnerId);
            var holders = Enumerable.Range(1, 200).Select(i =>
            {
                var id = Insert(admin, "users", new() { ["username"] = $"wv_{suffix}_{i}", ["password"] = Guid.NewGuid().ToString("N"), ["mail"] = $"{suffix}_{i}@invalid" });
                users.Add(id); return new WiredVariableHolder(WiredVariableTarget.User, id, i);
            }).ToArray();
            var frame = new WiredVariableFrame(room, holders);
            var module = new WiredVariableModule(room, directory, store, () => 1000);
            var reference = new WiredVariableReference(WiredVariableTarget.User, $"custom:{items[0]}");
            Assert.True(module.Mutate(reference, holders[0], WiredVariableMutation.Give, 1, frame));
            var reconnectedHolder = holders[0] with { EntityId = 999 };
            var reloaded = new WiredVariableModule(room, new DatabaseWiredVariableDirectory(database), new DatabaseWiredVariableStore(database), () => 2000);
            Assert.Equal(1, reloaded.Read(reference, reconnectedHolder, new(room, [reconnectedHolder]))!.Value);
            var modules = Enumerable.Range(0, 4).Select(_ => new WiredVariableModule(room, directory, store, () => 3000)).ToArray();
            await Task.WhenAll(modules.Select(m => Task.Run(() =>
            {
                for (var n = 0; n < 10; n++) Assert.True(m.Change(reference, holders[0], WiredVariableMutation.Set, value => value + 1, frame));
            })));
            Assert.Equal(41, module.Read(reference, holders[0], frame)!.Value);
            await Task.WhenAll(modules.Select(m => Task.Run(() => m.Mutate(reference, holders[1], WiredVariableMutation.Give, 5, frame))));
            Assert.Equal(1, admin.ExecuteScalar<int>("SELECT COUNT(*) FROM wired_variable_values WHERE definition_id=@id AND holder_id=@holder", new { id = items[0], holder = holders[1].StableId }));

            // Change the authoritative database after normal resolution, before the actual store transaction.
            var transactionDb = new ProbeDatabase(connectionString);
            var race = new WiredVariableModule(room, directory, new DatabaseWiredVariableStore(transactionDb), () => 4000);
            foreach (var change in new[] { "owner", "configuration", "placement" })
            {
                transactionDb.BeforeConnection = () =>
                {
                    if (change == "owner") admin.Execute("UPDATE rooms SET owner='malformed-owner' WHERE id=@room", new { room });
                    else if (change == "configuration") admin.Execute("UPDATE wired_item_configurations SET configuration=@config WHERE item_id=@id", new { id = items[0], config = JsonSerializer.Serialize(new WiredConfiguration { IntParams = [1,10], Text = "changed" }) });
                    else admin.Execute("UPDATE items SET room_id=0 WHERE id=@id", new { id = items[0] });
                };
                Assert.False(race.Mutate(reference, holders[0], WiredVariableMutation.Set, 999, frame)); Assert.Empty(race.DrainChanges());
                Assert.Equal(41, store.Read(new(items[0], WiredVariableTarget.User, holders[0].StableId))!.Value);
                admin.Execute("UPDATE rooms SET owner=@owner WHERE id=@room", new { owner = owner.ToString(), room });
                admin.Execute("UPDATE items SET room_id=@room WHERE id=@id", new { room, id = items[0] });
                admin.Execute("UPDATE wired_item_configurations SET configuration=@config WHERE item_id=@id", new { id = items[0], config = JsonSerializer.Serialize(new WiredConfiguration { IntParams = [1,10], Text = "probe0" }) });
            }
            admin.Execute("UPDATE rooms SET owner=@owner WHERE id=@room", new { owner = owner + "junk", room });
            Assert.Null(directory.GetRoomOwner(room)); Assert.Null(directory.Find(items[0]));
            admin.Execute("UPDATE rooms SET owner=@owner WHERE id=@room", new { owner = owner.ToString(), room });

            foreach (var item in items)
                foreach (var holder in holders)
                    admin.Execute("INSERT INTO wired_variable_values(definition_id,target_kind,holder_id,value,created_at_ms,updated_at_ms) VALUES (@item,0,@holder,25,1000,1000) ON DUPLICATE KEY UPDATE value=25", new { item, holder = holder.StableId });
            var references = items.Select(item => new WiredVariableReference(WiredVariableTarget.User, $"custom:{item}")).ToArray();
            database.Commands = 0;
            using (var snapshot = module.CaptureReads(references, frame))
                foreach (var variable in references)
                    foreach (var holder in holders) Assert.Equal(25, snapshot.Read(variable, holder, frame)!.Value);
            Assert.Equal(13, database.Commands);
            output.WriteLine("Actual MySQL 200 holders × 6 variables: 13 commands; 1200 populated values.");
            Assert.Equal(200, module.DeleteDefinition(items[0]));
            Assert.Empty(store.GetHolders(items[0]));
            Assert.Throws<InvalidOperationException>(() => module.Mutate(reference, holders[0], WiredVariableMutation.Give, 7, frame));
            Assert.DoesNotContain(module.DrainChanges(), x => x.After?.Value == 7);
            output.WriteLine("Persistence/reconnect, 40 concurrent increments, concurrent creation, transactional ownership/configuration/placement rejection and deletion tombstone passed.");
        }
        finally
        {
            foreach (var id in items)
            {
                admin.Execute("DELETE FROM wired_variable_values WHERE definition_id=@id", new { id });
                admin.Execute("DELETE FROM wired_variable_locks WHERE definition_id=@id", new { id });
                admin.Execute("DELETE FROM wired_item_configurations WHERE item_id=@id", new { id });
                admin.Execute("DELETE FROM items WHERE id=@id", new { id });
            }
            foreach (var id in rooms) admin.Execute("DELETE FROM rooms WHERE id=@id", new { id });
            foreach (var id in users) admin.Execute("DELETE FROM users WHERE id=@id", new { id });
            output.WriteLine("All disposable probe rows removed.");
        }
    }

    private static string GuardedConnectionString()
    {
        using var process = Process.Start(new ProcessStartInfo("docker")
        { ArgumentList = { "inspect", "plus-wired-preview-db-1" }, RedirectStandardOutput = true, RedirectStandardError = true })!;
        var inspect = process.StandardOutput.ReadToEnd(); process.WaitForExit();
        Assert.Equal(0, process.ExitCode);
        using var json = JsonDocument.Parse(inspect); var container = json.RootElement[0];
        Assert.Equal("plus-wired-preview", container.GetProperty("Config").GetProperty("Labels").GetProperty("com.docker.compose.project").GetString());
        var networks = container.GetProperty("NetworkSettings").GetProperty("Networks");
        Assert.Single(networks.EnumerateObject()); Assert.True(networks.TryGetProperty("plus-wired-preview_default", out var network));
        Assert.Contains(container.GetProperty("Mounts").EnumerateArray(), m => m.TryGetProperty("Name", out var name) && name.GetString() == "plus-wired-preview_wired-db-data" && m.GetProperty("Destination").GetString() == "/var/lib/mysql");
        using var config = JsonDocument.Parse(File.ReadAllText(Environment.GetEnvironmentVariable("WIRED_VARIABLE_PREVIEW_CONFIG")!));
        var db = config.RootElement.GetProperty("Database");
        return new MySqlConnectionStringBuilder { Server = network.GetProperty("IPAddress").GetString(), Port = 3306,
            Database = db.GetProperty("Name").GetString(), UserID = db.GetProperty("Username").GetString(), Password = db.GetProperty("Password").GetString(),
            MinimumPoolSize = 0, MaximumPoolSize = 8 }.ConnectionString;
    }

    private static uint Insert(MySqlConnection connection, string table, Dictionary<string, object> values)
    {
        var columns = connection.Query<Column>("SELECT COLUMN_NAME AS Name,DATA_TYPE AS Type,COLUMN_TYPE AS FullType FROM information_schema.columns WHERE table_schema=DATABASE() AND table_name=@table AND IS_NULLABLE='NO' AND COLUMN_DEFAULT IS NULL AND EXTRA NOT LIKE '%auto_increment%'", new { table });
        foreach (var column in columns.Where(x => !values.ContainsKey(x.Name)))
            values[column.Name] = column.Type switch
            {
                "enum" => column.FullType.Split('\'')[1], "datetime" or "timestamp" or "date" => DateTime.UtcNow,
                "varchar" or "char" or "text" or "mediumtext" or "longtext" => "", _ => 0
            };
        var parameters = new DynamicParameters(); foreach (var value in values) parameters.Add(value.Key, value.Value);
        connection.Execute($"INSERT INTO `{table}` ({string.Join(',', values.Keys.Select(x => $"`{x}`"))}) VALUES ({string.Join(',', values.Keys.Select(x => "@" + x))})", parameters);
        return connection.ExecuteScalar<uint>("SELECT LAST_INSERT_ID()");
    }
    private sealed class Column { public string Name { get; set; } = ""; public string Type { get; set; } = ""; public string FullType { get; set; } = ""; }
    private sealed class ProbeDatabase(string connectionString) : IDatabase
    {
        public int Commands;
        public Action? BeforeConnection;
        public bool IsConnected() => true;
        public IQueryAdapter GetQueryReactor() => throw new NotSupportedException();
        public IDbConnection Connection()
        {
            var action = BeforeConnection; BeforeConnection = null; action?.Invoke();
            return new CountedConnection(new MySqlConnection(connectionString), () => Interlocked.Increment(ref Commands));
        }
    }
    private sealed class CountedConnection(MySqlConnection inner, Action command) : IDbConnection
    {
        [System.Diagnostics.CodeAnalysis.AllowNull]
        public string ConnectionString { get => inner.ConnectionString; set => inner.ConnectionString = value ?? ""; }
        public int ConnectionTimeout => inner.ConnectionTimeout; public string Database => inner.Database; public ConnectionState State => inner.State;
        public IDbTransaction BeginTransaction() => inner.BeginTransaction(); public IDbTransaction BeginTransaction(IsolationLevel level) => inner.BeginTransaction(level);
        public void ChangeDatabase(string name) => inner.ChangeDatabase(name); public void Close() => inner.Close(); public void Open() => inner.Open(); public void Dispose() => inner.Dispose();
        public IDbCommand CreateCommand() { command(); return inner.CreateCommand(); }
    }
}
