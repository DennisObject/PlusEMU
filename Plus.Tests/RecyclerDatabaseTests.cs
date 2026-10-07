using System.Data;
using Dapper;
using MySqlConnector;
using Plus.Database;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Recycler;
using Xunit;

namespace Plus.Tests;

public sealed class RecyclerDatabaseTests
{
    private static readonly DateTimeOffset Now = new(2040, 2, 3, 4, 5, 6, TimeSpan.Zero);

    [RecyclerDatabaseFact]
    public void RecyclerConsumesOnlyExactUniqueOwnedEligibleUnplacedRowsAndPersistsBoxContentAndCooldownTogether()
    {
        using var fixture = new Fixture();
        var config = fixture.Store.Load();
        var prize = Assert.Single(Assert.Single(config.Levels).Prizes);
        Assert.Null(fixture.Store.Recycle(7, 100, config, prize, [new(20, 101), new(20, 101)], Now));
        Assert.Null(fixture.Store.Recycle(7, 100, config, prize, [new(20, 101), new(22, 101)], Now)); // Foreign owner.
        Assert.Null(fixture.Store.Recycle(7, 100, config, prize, [new(20, 101), new(23, 101)], Now)); // Placed.
        Assert.Null(fixture.Store.Recycle(7, 100, config, prize, [new(20, 101), new(24, 103)], Now)); // Ineligible.
        fixture.Connection.Execute("UPDATE items SET limited_number=1 WHERE id=21");
        Assert.Null(fixture.Store.Recycle(7, 100, config, prize, [new(20, 101), new(21, 101)], Now));
        fixture.Connection.Execute("UPDATE items SET limited_number=0 WHERE id=21");
        Assert.Equal(2, fixture.Connection.ExecuteScalar<int>("SELECT COUNT(*) FROM items WHERE id IN (20,21)"));
        Assert.Equal(0, fixture.Connection.ExecuteScalar<int>("SELECT COUNT(*) FROM user_presents"));
        Assert.Null(fixture.Store.NextAllowed(7));
        var box = Assert.IsType<RecycledBox>(fixture.Store.Recycle(7, 100, config, prize, [new(21, 101), new(20, 101)], Now));
        Assert.Equal("3-2-2040", box.ExtraData);
        Assert.Equal((7, 0u, 100u), fixture.Connection.QuerySingle<(int, uint, uint)>("SELECT user_id,room_id,base_item FROM items WHERE id=@id", new { id = box.Id }));
        Assert.Equal((102u, ""), fixture.Connection.QuerySingle<(uint, string)>("SELECT base_id,extra_data FROM user_presents WHERE item_id=@id", new { id = box.Id }));
        Assert.Equal(Now.AddSeconds(30), fixture.Store.NextAllowed(7));
        Assert.Equal(0, fixture.Connection.ExecuteScalar<int>("SELECT COUNT(*) FROM items WHERE id IN (20,21)"));
        fixture.Connection.Execute("INSERT INTO items(id,user_id,room_id,base_item,extra_data) VALUES(30,7,0,101,''),(31,7,0,101,'')");
        Assert.Null(fixture.Store.Recycle(7, 100, config, prize, [new(30, 101), new(31, 101)], Now.AddSeconds(29.999)));
        Assert.NotNull(fixture.Store.Recycle(7, 100, config, prize, [new(30, 101), new(31, 101)], Now.AddSeconds(30)));
    }

    [RecyclerDatabaseFact]
    public void BoxContentAndCooldownInsertFailuresRollBackEveryConsumedRow()
    {
        foreach (var table in new[] { "items", "user_presents", "user_recycler" }) {
            using var fixture = new Fixture();
            fixture.Connection.Execute($"CREATE TRIGGER reject_recycler_output BEFORE INSERT ON {table} FOR EACH ROW SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='forced recycler failure'");
            var config = fixture.Store.Load();
            var prize = Assert.Single(Assert.Single(config.Levels).Prizes);
            Assert.Throws<MySqlException>(() => fixture.Store.Recycle(7, 100, config, prize, [new(20, 101), new(21, 101)], Now));
            Assert.Equal(2, fixture.Connection.ExecuteScalar<int>("SELECT COUNT(*) FROM items WHERE id IN (20,21)"));
            Assert.Equal(0, fixture.Connection.ExecuteScalar<int>("SELECT COUNT(*) FROM items WHERE base_item=100"));
            Assert.Equal(0, fixture.Connection.ExecuteScalar<int>("SELECT COUNT(*) FROM user_presents"));
            Assert.Null(fixture.Store.NextAllowed(7));
        }
    }

    [RecyclerDatabaseFact]
    public async Task ConcurrentRepeatedSubmissionsPayOnlyOnceAndDoNotConsumeTheSecondSetDuringCooldown()
    {
        using var fixture = new Fixture();
        fixture.Connection.Execute("INSERT INTO items(id,user_id,room_id,base_item,extra_data) VALUES(30,7,0,101,''),(31,7,0,101,'')");
        var config = fixture.Store.Load();
        var prize = Assert.Single(Assert.Single(config.Levels).Prizes);
        using var start = new Barrier(2);
        var results = await Task.WhenAll(Task.Run(() => { start.SignalAndWait(); return fixture.Store.Recycle(7, 100, config, prize, [new(20, 101), new(21, 101)], Now); }),
            Task.Run(() => { start.SignalAndWait(); return fixture.Store.Recycle(7, 100, config, prize, [new(30, 101), new(31, 101)], Now); }));
        Assert.Single(results, result => result is not null);
        Assert.Equal(2, fixture.Connection.ExecuteScalar<int>("SELECT COUNT(*) FROM items WHERE id IN (20,21,30,31)"));
        Assert.Equal(1, fixture.Connection.ExecuteScalar<int>("SELECT COUNT(*) FROM user_presents"));
        Assert.Equal(1, fixture.Connection.ExecuteScalar<int>("SELECT COUNT(*) FROM items WHERE base_item=100"));
        Assert.Equal(Now.AddSeconds(30), fixture.Store.NextAllowed(7));
    }

    [RecyclerDatabaseFact]
    public void ConfigurationChangesOrOrphanPrizeRowsRefuseConsumptionAndMigrationRetainsOperatorData()
    {
        using var fixture = new Fixture();
        var config = fixture.Store.Load();
        var prize = Assert.Single(Assert.Single(config.Levels).Prizes);
        fixture.Connection.Execute("UPDATE recycler_settings SET cooldown_seconds=31");
        Assert.Null(fixture.Store.Recycle(7, 100, config, prize, [new(20, 101), new(21, 101)], Now));
        fixture.Connection.Execute("INSERT INTO recycler_prizes(level,item_id) VALUES(5,102)");
        Assert.False(fixture.Store.Load().Enabled);
        fixture.Connection.Execute(File.ReadAllText(HabbiconPacketTests.Repo("Database/Migrations/51_Recycler.sql")));
        Assert.Equal((true, 2, 31), fixture.Connection.QuerySingle<(bool, int, int)>("SELECT enabled,slots,cooldown_seconds FROM recycler_settings WHERE id=1"));
        Assert.Equal(2, fixture.Connection.ExecuteScalar<int>("SELECT COUNT(*) FROM recycler_prizes"));
        Assert.Equal(2, fixture.Connection.ExecuteScalar<int>("SELECT COUNT(*) FROM items WHERE id IN (20,21)"));
    }

    [RecyclerDatabaseFact]
    public void NativeEcotronContentsOpenOnceAsANewInventoryIdentityAndRejectForeignMalformedAndChangedContents()
    {
        using var fixture = new Fixture();
        var box = fixture.CreateBox();
        Assert.Null(fixture.Store.FindBox(box.Id, 8, 42, 100));
        Assert.Null(fixture.Store.FindBox(box.Id, 7, 43, 100));
        var content = Assert.IsType<GiftContent>(fixture.Store.FindBox(box.Id, 7, 42, 100));
        Assert.Null(fixture.Store.OpenBox(box.Id, 8, 42, 100, content));
        Assert.Null(fixture.Store.OpenBox(box.Id, 7, 43, 100, content));
        Assert.Null(fixture.Store.OpenBox(box.Id, 7, 42, 101, content));
        Assert.Null(fixture.Store.OpenBox(box.Id, 7, 42, 100, content with { ExtraData = "changed" }));
        fixture.Connection.Execute("INSERT INTO user_presents(item_id,base_id,extra_data) VALUES(@id,102,'duplicate')", new { id = box.Id });
        Assert.Null(fixture.Store.FindBox(box.Id, 7, 42, 100));
        Assert.Null(fixture.Store.OpenBox(box.Id, 7, 42, 100, content));
        fixture.Connection.Execute("DELETE FROM user_presents WHERE extra_data='duplicate'");
        fixture.Connection.Execute("UPDATE user_presents SET extra_data='blue' WHERE item_id=@id", new { id = box.Id });
        content = Assert.IsType<GiftContent>(fixture.Store.FindBox(box.Id, 7, 42, 100));
        var id = Assert.IsType<uint>(fixture.Store.OpenBox(box.Id, 7, 42, 100, content));
        Assert.NotEqual(box.Id, id);
        Assert.Equal((7, 0u, 102u, "blue"), fixture.Connection.QuerySingle<(int, uint, uint, string)>("SELECT user_id,room_id,base_item,extra_data FROM items WHERE id=@id", new { id }));
        Assert.Equal(0, fixture.Connection.ExecuteScalar<int>("SELECT COUNT(*) FROM items WHERE id=@id", new { id = box.Id }));
        Assert.Equal(0, fixture.Connection.ExecuteScalar<int>("SELECT COUNT(*) FROM user_presents"));
        Assert.Null(fixture.Store.OpenBox(box.Id, 7, 42, 100, content));
    }

    [RecyclerDatabaseFact]
    public void FailedRewardCreationRestoresThePlacedBoxAndItsExactPayload()
    {
        using var fixture = new Fixture();
        var box = fixture.CreateBox();
        var content = Assert.IsType<GiftContent>(fixture.Store.FindBox(box.Id, 7, 42, 100));
        fixture.Connection.Execute("CREATE TRIGGER reject_open_reward BEFORE INSERT ON items FOR EACH ROW SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='forced opening failure'");
        Assert.Throws<MySqlException>(() => fixture.Store.OpenBox(box.Id, 7, 42, 100, content));
        Assert.Equal(1, fixture.Connection.ExecuteScalar<int>("SELECT COUNT(*) FROM items WHERE id=@id AND user_id=7 AND room_id=42 AND base_item=100", new { id = box.Id }));
        Assert.Equal(content, fixture.Store.FindBox(box.Id, 7, 42, 100));
        fixture.Connection.Execute("DROP TRIGGER reject_open_reward");
        Assert.NotNull(fixture.Store.OpenBox(box.Id, 7, 42, 100, content));
    }

    [RecyclerDatabaseFact]
    public async Task ReloadedNativeInventoryPreservesEcotronDateAndCategoryWhileOtherDefaultDataStaysUnchanged()
    {
        using var fixture = new Fixture();
        var box = fixture.CreateBox();
        fixture.Connection.Execute("UPDATE items SET room_id=0 WHERE id=@id; UPDATE items SET extra_data='unused default state' WHERE id=24", new { id = box.Id });
        var definitions = new ItemDataManager(TestLogging.For<ItemDataManager>(), fixture.Source);
        definitions.Init();
        var inventory = await new Plus.HabboHotel.Users.Inventory.Furniture.FurnitureInventoryLoader(fixture.Source, definitions).Load(7);
        var loaded = Assert.Single(inventory.Where(item => item.Id == box.Id));
        Assert.Equal("3-2-2040", loaded.ExtraData.Serialize());
        Assert.Equal(Plus.HabboHotel.Users.Inventory.Furniture.FurniCategory.EcotronBox,
            Plus.HabboHotel.Users.Inventory.Furniture.InventoryItemSnapshot.Capture(loaded).Category);
        Assert.Equal("", Assert.Single(inventory.Where(item => item.Id == 24)).ExtraData.Serialize());
    }

    [RecyclerDatabaseFact]
    public async Task ConcurrentOpeningCreatesOnlyOneRewardAndDeletesOneExactBoxPayload()
    {
        using var fixture = new Fixture();
        var box = fixture.CreateBox();
        var content = Assert.IsType<GiftContent>(fixture.Store.FindBox(box.Id, 7, 42, 100));
        using var start = new Barrier(2);
        var results = await Task.WhenAll(Task.Run(() => { start.SignalAndWait(); return fixture.Store.OpenBox(box.Id, 7, 42, 100, content); }),
            Task.Run(() => { start.SignalAndWait(); return fixture.Store.OpenBox(box.Id, 7, 42, 100, content); }));
        Assert.Single(results, result => result is not null);
        Assert.Equal(1, fixture.Connection.ExecuteScalar<int>("SELECT COUNT(*) FROM items WHERE base_item=102 AND user_id=7 AND room_id=0"));
        Assert.Equal(0, fixture.Connection.ExecuteScalar<int>("SELECT COUNT(*) FROM user_presents"));
    }

    [RecyclerDatabaseFact]
    public async Task ABlockedOwnersInputDoesNotBlockAnotherOwnerUsingTheSameConfigurationAndFurniture()
    {
        using var fixture = new Fixture();
        fixture.Connection.Execute("INSERT INTO items(id,user_id,room_id,base_item,extra_data) VALUES(25,8,0,101,'')");
        var config = fixture.Store.Load();
        var prize = config.Levels[0].Prizes[0];
        using var holder = new MySqlConnection(fixture.Connection.ConnectionString);
        holder.Open();
        using var held = holder.BeginTransaction();
        holder.ExecuteScalar<uint>("SELECT id FROM items WHERE id=20 FOR UPDATE", transaction: held);
        Task<RecycledBox?>? first = null;
        Task<RecycledBox?>? second = null;
        var firstThread = 0;
        fixture.ConnectionOpened = thread => Interlocked.CompareExchange(ref firstThread, thread, 0);
        try {
            first = Task.Factory.StartNew(() => fixture.Store.Recycle(7, 100, config, prize, [new(20, 101), new(21, 101)], Now),
                CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            var waiting = false;
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (!waiting && DateTime.UtcNow < deadline) {
                waiting = fixture.Connection.ExecuteScalar<int>("""
                    SELECT COUNT(*) FROM information_schema.INNODB_LOCK_WAITS w
                    INNER JOIN information_schema.INNODB_TRX t ON t.trx_id=w.requesting_trx_id
                    INNER JOIN information_schema.INNODB_TRX blocker ON blocker.trx_id=w.blocking_trx_id
                    WHERE t.trx_mysql_thread_id=@thread AND blocker.trx_mysql_thread_id=@holder
                    """, new { thread = Volatile.Read(ref firstThread), holder = holder.ServerThread }) > 0;
                if (!waiting) {
                    await Task.Delay(250);
                }
            }
            if (!waiting) {
                var states = fixture.Connection.Query<string>("SELECT CONCAT(trx_state,':',COALESCE(trx_query,'')) FROM information_schema.INNODB_TRX WHERE trx_mysql_thread_id=@thread", new { thread = Volatile.Read(ref firstThread) });
                var result = first.IsCompleted ? (await first is null ? "refused" : "committed") : first.Status.ToString();
                Console.WriteLine($"Owned connection {firstThread}, task {result}: {string.Join(" | ", states)}");
            }
            Assert.True(waiting, $"First owner never reached the deliberately locked input (task: {first.Status}).");
            second = Task.Factory.StartNew(() => fixture.Store.Recycle(8, 100, config, prize, [new(22, 101), new(25, 101)], Now),
                CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            Assert.Same(second, await Task.WhenAny(second, Task.Delay(TimeSpan.FromSeconds(5))));
            Assert.NotNull(await second);
            Assert.False(first.IsCompleted);
        }
        finally {
            held.Rollback();
            if (first is not null) {
                await first;
            }
            if (second is not null) {
                await second;
            }
        }
        Assert.NotNull(await first!);
        Assert.Equal(2, fixture.Connection.ExecuteScalar<int>("SELECT COUNT(*) FROM user_presents"));
        Assert.Equal(2, fixture.Connection.ExecuteScalar<int>("SELECT COUNT(*) FROM user_recycler"));
    }

    private sealed class Fixture : IDisposable
    {
        private readonly MySqlConnection _admin;
        private readonly string _schema = "task_recycler_" + Guid.NewGuid().ToString("N");
        public MySqlConnection Connection { get; }
        public RecyclerStore Store { get; }
        public IDatabase Source { get; }
        public Action<int>? ConnectionOpened { get; set; }
        public Fixture()
        {
            SqlMapper.AddTypeHandler(new UtcDateTimeOffsetHandler());
            var options = new MySqlConnectionStringBuilder(Environment.GetEnvironmentVariable("RECYCLER_DATABASE")!)
            { Database = "information_schema", Pooling = false, AllowZeroDateTime = true, ConvertZeroDateTime = true };
            _admin = new MySqlConnection(options.ConnectionString);
            _admin.Open();
            _admin.Execute($"CREATE DATABASE `{_schema}`");
            options.Database = _schema;
            Connection = new MySqlConnection(options.ConnectionString);
            Source = new Database(options.ConnectionString, thread => ConnectionOpened?.Invoke(thread));
            Store = new RecyclerStore(Source);
            try {
                Connection.Open();
                var dump = File.ReadAllText(HabbiconPacketTests.Repo("Resources/SQLs/Original Database.sql"));
                foreach (var name in new[] { "items", "furniture", "user_presents", "items_groups" }) {
                    var start = dump.IndexOf($"CREATE TABLE `{name}` (", StringComparison.Ordinal);
                    Connection.Execute(dump[start..(dump.IndexOf(';', start) + 1)]);
                }
                Connection.Execute("CREATE TABLE users(id INT PRIMARY KEY); INSERT INTO users VALUES(7),(8)");
                Connection.Execute(File.ReadAllText(HabbiconPacketTests.Repo("Database/Migrations/51_Recycler.sql")));
                Assert.False(Store.Load().Enabled);
                Assert.Empty(Store.Load().Levels);
                Connection.Execute("""
                    UPDATE recycler_settings SET enabled=TRUE,slots=2,cooldown_seconds=30;
                    INSERT INTO recycler_levels(level,chance) VALUES(1,1);
                    INSERT INTO recycler_prizes(id,level,item_id) VALUES(1,1,102);
                    INSERT INTO furniture(id,item_name,sprite_id,allow_recycle) VALUES(100,'ecotron_box',3095,FALSE),(101,'input',41,TRUE),(102,'reward',42,TRUE),(103,'not_recyclable',43,FALSE);
                    INSERT INTO items(id,user_id,room_id,base_item,extra_data) VALUES(20,7,0,101,''),(21,7,0,101,''),(22,8,0,101,''),(23,7,42,101,''),(24,7,0,103,'');
                    """);
            }
            catch {
                Dispose();
                throw;
            }
        }
        public RecycledBox CreateBox()
        {
            var config = Store.Load();
            var box = Assert.IsType<RecycledBox>(Store.Recycle(7, 100, config, config.Levels[0].Prizes[0], [new(20, 101), new(21, 101)], Now));
            Connection.Execute("UPDATE items SET room_id=42 WHERE id=@id", new { id = box.Id });
            return box;
        }
        public void Dispose()
        {
            Connection.Dispose();
            _admin.Execute($"DROP DATABASE IF EXISTS `{_schema}`");
            _admin.Dispose();
        }
        private sealed class Database(string connectionString, Action<int> opened) : IDatabase
        {
            public bool IsConnected() => true;
            public IDbConnection Connection()
            {
                var connection = new MySqlConnection(connectionString);
                connection.StateChange += (_, state) => {
                    if (state.CurrentState == ConnectionState.Open) {
                        opened(connection.ServerThread);
                    }
                };
                return connection;
            }
        }
    }
}

public sealed class RecyclerDatabaseFactAttribute : FactAttribute
{
    public RecyclerDatabaseFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("RECYCLER_DATABASE"))) {
            Skip = "Set RECYCLER_DATABASE for isolated native-schema recycler regressions.";
        }
    }
}
