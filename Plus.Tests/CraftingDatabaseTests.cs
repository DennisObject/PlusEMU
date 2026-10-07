using System.Data;
using Dapper;
using MySqlConnector;
using Plus.Database;
using Plus.HabboHotel.Crafting;
using Xunit;

namespace Plus.Tests;

public sealed class CraftingDatabaseTests
{
    [CraftingDatabaseFact]
    public void CraftingConsumesExactInventoryRowsAndAtomicallyCreatesTheRewardAndDiscovery()
    {
        using var fixture = new Fixture();
        var store = fixture.Store;
        var recipe = Assert.Single(store.Load(100, 7));
        Assert.True(recipe.Secret);
        Assert.False(recipe.Discovered);
        Assert.Equal([(101u, 2)], recipe.Ingredients.Select(row => (row.ItemId, row.Amount)));
        Assert.Null(store.Craft(7, 42, 10, recipe, [20, 20], true));
        Assert.Null(store.Craft(7, 42, 10, recipe, [20, 22], true)); // Foreign owner.
        Assert.Null(store.Craft(7, 42, 10, recipe, [20, 23], true)); // Already placed.
        Assert.Null(store.Craft(7, 43, 10, recipe, [20, 21], true)); // Foreign room.
        Assert.Null(store.Craft(8, 42, 10, recipe, [22, 24], true)); // Foreign altar.
        Assert.Null(store.Craft(7, 42, 10, recipe, [20, 21], false)); // Undiscovered secret.
        Assert.Equal(2, fixture.Connection.ExecuteScalar<int>("SELECT COUNT(*) FROM items WHERE id IN (20,21)"));
        Assert.Empty(fixture.Connection.Query<int>("SELECT recipe_id FROM user_crafting_recipes"));

        var crafted = Assert.IsType<CraftedItem>(store.Craft(7, 42, 10, recipe, [21, 20], true));
        Assert.True(crafted.Discovered);
        Assert.Equal((7, 0u, 102u), fixture.Connection.QuerySingle<(int, uint, uint)>(
            "SELECT user_id,room_id,base_item FROM items WHERE id=@id", new { id = crafted.ItemId }));
        Assert.Equal(0, fixture.Connection.ExecuteScalar<int>("SELECT COUNT(*) FROM items WHERE id IN (20,21)"));
        Assert.Equal(1, fixture.Connection.ExecuteScalar<int>("SELECT remaining FROM crafting_recipes WHERE id=1"));
        Assert.True(Assert.Single(store.Load(100, 7)).Discovered);
        Assert.Null(store.Craft(7, 42, 10, recipe, [20, 21], true));
        fixture.Connection.Execute("INSERT INTO items(id,user_id,room_id,base_item,extra_data) VALUES(30,7,0,101,''),(31,7,0,101,'')");
        var repeated = Assert.IsType<CraftedItem>(store.Craft(7, 42, 10, recipe, [30, 31], false));
        Assert.False(repeated.Discovered);
        Assert.Equal(1, fixture.Connection.ExecuteScalar<int>("SELECT COUNT(*) FROM user_crafting_recipes WHERE user_id=7 AND recipe_id=1"));
        Assert.Equal(0, fixture.Connection.ExecuteScalar<int>("SELECT remaining FROM crafting_recipes WHERE id=1"));
    }

    [CraftingDatabaseFact]
    public void PersistedLimitedIngredientsRejectStaleSelectionsWithoutConsumingStockOrDiscovery()
    {
        using var fixture = new Fixture();
        var recipe = Assert.Single(fixture.Store.Load(100, 7));

        foreach (var column in new[] { "limited_number", "limited_stack" }) {
            fixture.Connection.Execute($"UPDATE items SET {column}=1 WHERE id=20");
            Assert.Null(fixture.Store.Craft(7, 42, 10, recipe, [20, 21], true));
            Assert.Equal(2, fixture.Connection.ExecuteScalar<int>("SELECT COUNT(*) FROM items WHERE id IN (20,21)"));
            Assert.Equal(2, fixture.Connection.ExecuteScalar<int>("SELECT remaining FROM crafting_recipes WHERE id=1"));
            Assert.Equal(0, fixture.Connection.ExecuteScalar<int>("SELECT COUNT(*) FROM user_crafting_recipes"));
            Assert.Equal(0, fixture.Connection.ExecuteScalar<int>("SELECT COUNT(*) FROM items WHERE base_item=102"));
            fixture.Connection.Execute($"UPDATE items SET {column}=0 WHERE id=20");
        }
    }

    [CraftingDatabaseFact]
    public void FailedOutputInsertRollsBackIngredientsStockAndDiscovery()
    {
        using var fixture = new Fixture();
        fixture.Connection.Execute("""
            CREATE TRIGGER reject_crafting_reward BEFORE INSERT ON items FOR EACH ROW
            BEGIN
                IF NEW.base_item=102 THEN
                    SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='forced output failure';
                END IF;
            END
            """);
        var recipe = Assert.Single(fixture.Store.Load(100, 7));
        Assert.Throws<MySqlException>(() => fixture.Store.Craft(7, 42, 10, recipe, [20, 21], true));
        Assert.Equal(2, fixture.Connection.ExecuteScalar<int>("SELECT COUNT(*) FROM items WHERE id IN (20,21)"));
        Assert.Equal(2, fixture.Connection.ExecuteScalar<int>("SELECT remaining FROM crafting_recipes WHERE id=1"));
        Assert.Equal(0, fixture.Connection.ExecuteScalar<int>("SELECT COUNT(*) FROM user_crafting_recipes"));
        Assert.Equal(0, fixture.Connection.ExecuteScalar<int>("SELECT COUNT(*) FROM items WHERE base_item=102"));
    }

    [CraftingDatabaseFact]
    public async Task ConcurrentOwnersCannotBothConsumeTheLastLimitedRecipe()
    {
        using var fixture = new Fixture();
        fixture.Connection.Execute("UPDATE crafting_recipes SET remaining=1; INSERT INTO items(id,user_id,room_id,base_item,extra_data) VALUES(11,8,42,100,'')");
        var recipe = Assert.Single(fixture.Store.Load(100, 7));
        using var start = new Barrier(2);
        var results = await Task.WhenAll(Task.Run(() => { start.SignalAndWait(); return fixture.Store.Craft(7, 42, 10, recipe, [20, 21], true); }),
            Task.Run(() => { start.SignalAndWait(); return fixture.Store.Craft(8, 42, 11, recipe, [22, 24], true); }));
        Assert.Single(results, result => result is not null);
        Assert.Equal(1, fixture.Connection.ExecuteScalar<int>("SELECT COUNT(*) FROM items WHERE base_item=102"));
        Assert.Equal(2, fixture.Connection.ExecuteScalar<int>("SELECT COUNT(*) FROM items WHERE id IN (20,21,22,24)"));
        Assert.Equal(1, fixture.Connection.ExecuteScalar<int>("SELECT COUNT(*) FROM user_crafting_recipes"));
        Assert.Equal(0, fixture.Connection.ExecuteScalar<int>("SELECT remaining FROM crafting_recipes"));
    }

    [CraftingDatabaseFact]
    public void PortableSeedResolvesOnlyCompleteUniqueFurnitureAndPreservesConfiguredCodes()
    {
        using var fixture = new Fixture();
        fixture.Connection.Execute("""
            INSERT INTO furniture(id,item_name,sprite_id) VALUES
            (900,'hween_c15_altar',8388),(901,'clothing_firehelm',8342),(902,'hween_c15_purecrystal3',8373),
            (903,'hween_c15_purecrystal2',8363),(904,'hween_c15_purecrystal2',8363),(905,'clothing_airhelm',8340);
            INSERT INTO crafting_recipes(code,product_code,reward_item_id,secret,remaining) VALUES('clothing_airhelm','custom',999,TRUE,4);
            """);
        var migration = File.ReadAllText(HabbiconPacketTests.Repo("Database/Migrations/45_Crafting.sql"));
        fixture.Connection.Execute(migration);
        var seeded = Assert.Single(fixture.Store.Load(900, 7));
        Assert.Equal("clothing_firehelm", seeded.Code);
        Assert.Equal(901u, seeded.RewardId);
        Assert.Equal(new CraftingIngredient(902, 4), Assert.Single(seeded.Ingredients));
        Assert.False(seeded.Secret);
        Assert.Equal(("custom", 999u, 4), fixture.Connection.QuerySingle<(string, uint, int)>(
            "SELECT product_code,reward_item_id,remaining FROM crafting_recipes WHERE code='clothing_airhelm'"));
        fixture.Connection.Execute(migration);
        Assert.Equal(3, fixture.Connection.ExecuteScalar<int>("SELECT COUNT(*) FROM crafting_recipes"));
        Assert.Equal(2, fixture.Connection.ExecuteScalar<int>("SELECT COUNT(*) FROM crafting_recipes_ingredients"));
    }

    [CraftingDatabaseFact]
    public void InterruptedSeedDoesNotLeaveAPartialRecipeAndTheSameConnectionCanRetry()
    {
        using var fixture = new Fixture();
        fixture.Connection.Execute("""
            INSERT INTO furniture(id,item_name,sprite_id) VALUES
            (900,'hween_c15_altar',8388),(901,'clothing_firehelm',8342),(902,'hween_c15_purecrystal3',8373);
            CREATE TRIGGER reject_seed_ingredient BEFORE INSERT ON crafting_recipes_ingredients FOR EACH ROW
            SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='forced seed interruption';
            """);
        var migration = File.ReadAllText(HabbiconPacketTests.Repo("Database/Migrations/45_Crafting.sql"));
        Assert.Throws<MySqlException>(() => fixture.Connection.Execute(migration));
        fixture.Connection.Execute("ROLLBACK");
        Assert.Equal(1, fixture.Connection.ExecuteScalar<int>("SELECT COUNT(*) FROM crafting_recipes"));
        Assert.Equal(1, fixture.Connection.ExecuteScalar<int>("SELECT COUNT(*) FROM crafting_altars_recipes"));
        Assert.Equal(1, fixture.Connection.ExecuteScalar<int>("SELECT COUNT(*) FROM crafting_recipes_ingredients"));
        fixture.Connection.Execute("DROP TRIGGER reject_seed_ingredient");
        fixture.Connection.Execute(migration);
        Assert.Equal("clothing_firehelm", Assert.Single(fixture.Store.Load(900, 7)).Code);
        fixture.Connection.Execute(migration);
        Assert.Equal(2, fixture.Connection.ExecuteScalar<int>("SELECT COUNT(*) FROM crafting_recipes"));
    }

    private sealed class Fixture : IDisposable
    {
        private readonly MySqlConnection _admin;
        private readonly string _schema = "task_crafting_" + Guid.NewGuid().ToString("N");
        public MySqlConnection Connection { get; }
        public CraftingStore Store { get; }

        public Fixture()
        {
            var options = new MySqlConnectionStringBuilder(Environment.GetEnvironmentVariable("CRAFTING_DATABASE")!)
            { Database = "information_schema", Pooling = false, AllowZeroDateTime = true, ConvertZeroDateTime = true };
            _admin = new MySqlConnection(options.ConnectionString);
            _admin.Open();
            _admin.Execute($"CREATE DATABASE `{_schema}`");
            options.Database = _schema;
            Connection = new MySqlConnection(options.ConnectionString);
            Store = new CraftingStore(new Database(options.ConnectionString));

            try {
                Connection.Open();
                var dump = File.ReadAllText(HabbiconPacketTests.Repo("Resources/SQLs/Original Database.sql"));
                var start = dump.IndexOf("CREATE TABLE `items` (", StringComparison.Ordinal);
                Connection.Execute(dump[start..(dump.IndexOf(';', start) + 1)]);
                start = dump.IndexOf("CREATE TABLE `furniture` (", StringComparison.Ordinal);
                Connection.Execute(dump[start..(dump.IndexOf(';', start) + 1)]);
                Connection.Execute("CREATE TABLE users(id INT PRIMARY KEY); INSERT INTO users VALUES(7),(8)");
                Connection.Execute(File.ReadAllText(HabbiconPacketTests.Repo("Database/Migrations/45_Crafting.sql")));
                Connection.Execute("""
                    INSERT INTO crafting_recipes(id,code,product_code,reward_item_id,secret,remaining) VALUES(1,'test_recipe','test_product',102,TRUE,2);
                    INSERT INTO crafting_altars_recipes VALUES(100,1);
                    INSERT INTO crafting_recipes_ingredients VALUES(1,101,2);
                    INSERT INTO items(id,user_id,room_id,base_item,extra_data) VALUES
                    (10,7,42,100,''),(20,7,0,101,''),(21,7,0,101,''),(22,8,0,101,''),(23,7,42,101,''),(24,8,0,101,'');
                    """);
            }
            catch {
                Dispose();
                throw;
            }
        }

        public void Dispose()
        {
            Connection.Dispose();
            _admin.Execute($"DROP DATABASE IF EXISTS `{_schema}`");
            _admin.Dispose();
        }

        private sealed class Database(string connectionString) : IDatabase
        {
            public bool IsConnected() => true;
            public IDbConnection Connection() => new MySqlConnection(connectionString);
        }
    }
}

public sealed class CraftingDatabaseFactAttribute : FactAttribute
{
    public CraftingDatabaseFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CRAFTING_DATABASE")!)) {
            Skip = "Set CRAFTING_DATABASE to run the isolated crafting database regression.";
        }
    }
}
