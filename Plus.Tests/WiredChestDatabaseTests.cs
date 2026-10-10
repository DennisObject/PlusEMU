using Dapper;
using MySqlConnector;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.Wired.Chests;
using Plus.HabboHotel.Users.Inventory.Furniture;
using Xunit;

namespace Plus.Tests;

public sealed class WiredChestDatabaseFactAttribute : FactAttribute
{
    public WiredChestDatabaseFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("WIRED_CHEST_DATABASE") == null) {
            Skip = "Set WIRED_CHEST_DATABASE to a disposable loopback MariaDB server.";
        }
    }
}

public sealed class WiredChestDatabaseTests
{
    [WiredChestDatabaseFact]
    public void DepositRequiresOwnedUnplacedUnstoredInventoryAndReloadDoesNotDuplicate()
    {
        using var fixture = new Fixture();
        var store = fixture.Store;
        var chest = fixture.Chest(100, 1, WiredChestKind.Furni);
        Assert.True(store.Transfer(fixture.Request(1, 100) with { PaymentIds = [201] }).Succeeded);
        Assert.Equal(0, fixture.Connection.QuerySingle<int>("SELECT user_id FROM items WHERE id=201"));
        Assert.Equal(100, fixture.Connection.QuerySingle<int>("SELECT chest_item_id FROM items WHERE id=201"));
        Assert.Equal(new uint[] { 201 }, new DatabaseWiredChestStore(fixture.Database, fixture.Definitions).Load(chest)!.Items.Select(item => item.Id));
        Assert.True(store.SaveSettings(chest, 1, false, false, old => old with { EveryoneCanDonate = true }));
        Assert.Equal(WiredChestFailure.Invalid, store.Transfer(fixture.Request(2, 100) with { PaymentIds = [201] }).Failure);
        Assert.Equal(WiredChestFailure.Invalid, store.Transfer(fixture.Request(1, 100) with { PaymentIds = [202] }).Failure);
        Assert.Equal(WiredChestFailure.Invalid, store.Transfer(fixture.Request(1, 100) with { PaymentIds = [999] }).Failure); // Borrowed BC stock has no owned inventory row.
        Assert.Equal(WiredChestFailure.Invalid, store.Transfer(fixture.Request(1, 100) with { PaymentIds = [203] }).Failure);
        Assert.Equal(WiredChestFailure.Invalid, store.Transfer(fixture.Request(1, 100) with { PaymentIds = [201, 201] }).Failure);
        Assert.Equal(1, fixture.Connection.QuerySingle<int>("SELECT COUNT(*) FROM items WHERE chest_item_id=100"));
        Assert.Equal(1, fixture.Connection.QuerySingle<int>("SELECT COUNT(*) FROM wired_chest_transactions"));
    }

    [WiredChestDatabaseFact]
    public void MissingRewardAndFinalWriteFailureRollBackBothSidesAndOperationRetriesOnce()
    {
        using var fixture = new Fixture();
        fixture.Chest(100, 1, WiredChestKind.Furni);
        fixture.Chest(101, 1, WiredChestKind.Coins);
        fixture.Enable(100);
        fixture.Enable(101);
        var request = fixture.Request(1, 100, 101) with { Wired = true, PaymentIds = [201], Reward = [new(5)] };
        Assert.Equal(WiredChestFailure.FundsGone, fixture.Store.Transfer(request).Failure);
        Assert.Equal(1, fixture.Connection.QuerySingle<int>("SELECT user_id FROM items WHERE id=201"));
        Assert.Equal(0, fixture.Connection.QuerySingle<int>("SELECT COUNT(*) FROM wired_chest_transactions"));
        fixture.Connection.Execute("UPDATE wired_chests SET coins=10 WHERE item_id=101; CREATE TRIGGER reject_chest_commit BEFORE UPDATE ON users FOR EACH ROW SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='forced chest final failure';");
        Assert.Throws<MySqlException>(() => fixture.Store.Transfer(request));
        Assert.Equal(1, fixture.Connection.QuerySingle<int>("SELECT user_id FROM items WHERE id=201"));
        Assert.Equal(10, fixture.Connection.QuerySingle<int>("SELECT coins FROM wired_chests WHERE item_id=101"));
        Assert.Equal(0, fixture.Connection.QuerySingle<int>("SELECT COUNT(*) FROM wired_chest_transactions"));
        fixture.Connection.Execute("DROP TRIGGER reject_chest_commit");
        var result = fixture.Store.Transfer(request);
        Assert.True(result.Succeeded);
        Assert.Equal(new WiredChestFigures(1, 1, 0, 0, 5), result.Figures);
        Assert.True(fixture.Store.Transfer(request).Replayed);
        Assert.Equal(105, fixture.Connection.QuerySingle<int>("SELECT credits FROM users WHERE id=1"));
        Assert.Equal(5, fixture.Connection.QuerySingle<int>("SELECT coins FROM wired_chests WHERE item_id=101"));
        Assert.Equal(1, fixture.Connection.QuerySingle<int>("SELECT COUNT(*) FROM wired_chest_transactions"));
    }

    [WiredChestDatabaseFact]
    public async Task ConcurrentWithdrawalsCannotDuplicateItemsOrOverdrawCredits()
    {
        using var fixture = new Fixture();
        fixture.Chest(100, 1, WiredChestKind.Furni);
        fixture.Chest(101, 1, WiredChestKind.Coins);
        fixture.Enable(100);
        fixture.Enable(101);
        Assert.True(fixture.Store.Transfer(fixture.Request(1, 100) with { PaymentIds = [201] }).Succeeded);
        fixture.Connection.Execute("UPDATE wired_chests SET coins=10 WHERE item_id=101");
        var requests = new[] { 1, 2 }.Select(user => fixture.Request(user, 100, 101) with
        { Wired = true, Reward = [new(10), new(1, new(false, 44))] }).ToArray();
        var results = await Task.WhenAll(requests.Select(request => Task.Run(() => fixture.Store.Transfer(request))));
        Assert.Single(results.Where(result => result.Succeeded));
        Assert.Single(results.Where(result => result.Failure == WiredChestFailure.FundsGone));
        Assert.Equal(0, fixture.Connection.QuerySingle<int>("SELECT coins FROM wired_chests WHERE item_id=101"));
        Assert.Equal(210, fixture.Connection.QuerySingle<int>("SELECT SUM(credits) FROM users"));
        Assert.Equal(0, fixture.Connection.QuerySingle<int>("SELECT COUNT(*) FROM items WHERE chest_item_id=100"));
        Assert.Equal(1, fixture.Connection.QuerySingle<int>("SELECT COUNT(*) FROM items WHERE id=201 AND user_id IN (1,2)"));
    }

    [WiredChestDatabaseFact]
    public void LocksDonationAndOwnerOnlyUnlockArePersistedAndWalletDepositsConserveValue()
    {
        using var fixture = new Fixture();
        var chest = fixture.Chest(101, 1, WiredChestKind.Coins);
        Assert.False(fixture.Store.SaveSettings(chest, 2, true, true, settings => settings with { Locked = false }));
        Assert.Equal(WiredChestFailure.NoOrLockedChests, fixture.Store.Transfer(fixture.Request(2, 101) with { CanModify = true, WalletDepositCredits = 10 }).Failure);
        Assert.True(fixture.Store.SaveSettings(chest, 1, false, false, settings => settings with { EveryoneCanDonate = true }));
        Assert.True(fixture.Store.Transfer(fixture.Request(2, 101) with { WalletDepositCredits = 10 }).Succeeded);
        Assert.Equal(90, fixture.Connection.QuerySingle<int>("SELECT credits FROM users WHERE id=2"));
        Assert.Equal(10, fixture.Store.Load(chest)!.Coins);
        Assert.Equal(WiredChestFailure.NoOrLockedChests, fixture.Store.Transfer(fixture.Request(2, 101) with { Reward = [new(10)], CanModify = true }).Failure);
        Assert.Equal(WiredChestFailure.NoOrLockedChests, fixture.Store.Transfer(fixture.Request(2, 101) with { Reward = [new(10)], Wired = true }).Failure);
        Assert.True(fixture.Store.Transfer(fixture.Request(1, 101) with { Reward = [new(10)] }).Succeeded);
        Assert.Equal(200, fixture.Connection.QuerySingle<int>("SELECT SUM(credits)+COALESCE((SELECT SUM(coins) FROM wired_chests),0) FROM users"));
        Assert.Equal(WiredChestFailure.InsufficientFunds, fixture.Store.Transfer(fixture.Request(2, 101) with { WalletDepositCredits = 91 }).Failure);
        Assert.Equal(WiredChestFailure.Invalid, fixture.Store.Transfer(fixture.Request(2, 101) with { WalletDepositCredits = -1 }).Failure);
    }

    [WiredChestDatabaseFact]
    public void SettingsSentinelsMigrationReloadAndPaidUpgradePreserveOwnershipAndBalances()
    {
        using var fixture = new Fixture();
        fixture.Connection.Execute(File.ReadAllText(HabbiconPacketTests.Repo("Database/Migrations/65_WiredChests.sql")));
        var coins = fixture.Chest(101, 1, WiredChestKind.Coins);
        Assert.True(fixture.Store.SaveSettings(coins, 1, false, false, settings => settings with { Name = "saved", PreviewMode = -1, PreviewAmount = 0 }));
        Assert.Equal("saved", fixture.Store.Load(coins)!.Settings.Name);
        Assert.Equal(1, fixture.Store.Load(coins)!.Settings.PreviewAmount);
        var furni = fixture.Chest(100, 1, WiredChestKind.Furni);
        Assert.True(fixture.Store.SaveSettings(furni, 1, false, false, settings => settings with { PreviewMode = 4, PreviewAmount = 3 }));
        Assert.True(fixture.Store.SaveSettings(furni, 1, false, false, settings => settings with { PreviewAmount = 0 }));
        Assert.Equal(3, fixture.Store.Load(furni)!.Settings.PreviewAmount);
        Assert.Equal(7, fixture.Store.Upgrade(coins, 2, 1).Code);
        Assert.Equal(2, fixture.Store.Upgrade(coins, 1, 20).Code);
        Assert.Equal(0, fixture.Store.Upgrade(coins, 1, 2).Code);
        Assert.Equal(2, fixture.Store.Load(coins)!.Level);
        Assert.Equal(15000, fixture.Store.Load(coins)!.MaximumCapacity);
        Assert.Equal(80, fixture.Connection.QuerySingle<int>("SELECT credits FROM users WHERE id=1"));
        Assert.Equal(80, fixture.Connection.QuerySingle<int>("SELECT amount FROM user_currencies WHERE user_id=1 AND type=5"));
        fixture.Connection.Execute("CREATE TRIGGER reject_chest_upgrade BEFORE UPDATE ON wired_chests FOR EACH ROW SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='forced upgrade failure'");
        Assert.Throws<MySqlException>(() => fixture.Store.Upgrade(coins, 1, 1));
        Assert.Equal(80, fixture.Connection.QuerySingle<int>("SELECT credits FROM users WHERE id=1"));
        Assert.Equal(80, fixture.Connection.QuerySingle<int>("SELECT amount FROM user_currencies WHERE user_id=1 AND type=5"));
    }

    [WiredChestDatabaseFact]
    public void StarterChestCannotEnableWiredExpandCapacityOrUpgrade()
    {
        using var fixture = new Fixture();
        var starter = fixture.Chest(100, 1, WiredChestKind.Furni, starter: true);
        Assert.Equal(100, fixture.Store.Load(starter)!.Settings.Capacity);
        Assert.False(fixture.Store.SaveSettings(starter, 1, false, false, settings => settings with { Capacity = 101 }));
        Assert.False(fixture.Store.SaveSettings(starter, 1, false, false, settings => settings with { WiredEnabled = true }));
        Assert.Equal(10, fixture.Store.Upgrade(starter, 1, 1).Code);
        Assert.Equal(100, fixture.Connection.QuerySingle<int>("SELECT credits FROM users WHERE id=1"));
    }

    [WiredChestDatabaseFact]
    public void PhysicalContractReloadPreservesTermsAndMetadataAndRejectsStaleOwner()
    {
        using var fixture = new Fixture();
        fixture.Connection.Execute("INSERT INTO items(id,user_id,room_id,base_item,extra_data) VALUES (80,1,42,99,'')");
        var item = new Item
        {
            Id = 80,
            OwnerId = 1,
            RoomId = 42,
            Definition = new()
            {
                Id = 99,
                ItemName = "wf_contract_trade",
                InteractionType = InteractionType.WiredContractTrade
            }
        };
        var contract = new WiredChestContract
        {
            Kind = WiredContractKind.Trade,
            Payment = [[new(2, new(false, 44))]],
            Reward = [new(7)],
            PaymentMode = 1,
            ReceiveText = "Owned stock only",
            Layout = "games",
            RewardCategory = 13,
            ShowDialog = true,
            RewardText = "Thank you"
        };
        Assert.True(fixture.Store.SaveContract(item, contract));
        var reloaded = new DatabaseWiredChestStore(fixture.Database, fixture.Definitions).LoadContract(item)!;
        Assert.Equal((uint)80, reloaded.Id);
        Assert.Equal(contract.Payment![0], reloaded.Payment![0]);
        Assert.Equal(contract.Reward, reloaded.Reward);
        Assert.Equal(contract.ReceiveText, reloaded.ReceiveText);
        Assert.Equal(contract.Layout, reloaded.Layout);
        Assert.Equal(contract.RewardCategory, reloaded.RewardCategory);
        Assert.True(reloaded.ShowDialog);
        Assert.Equal(contract.RewardText, reloaded.RewardText);
        Assert.False(fixture.Store.SaveContract(item, contract with { Kind = WiredContractKind.Reward }));
        item.OwnerId = 2;
        Assert.False(fixture.Store.SaveContract(item, contract with { RewardText = "stale" }));
        item.OwnerId = 1;
        Assert.Equal("Thank you", fixture.Store.LoadContract(item)!.RewardText);
    }

    internal sealed class Fixture : IDisposable
    {
        private readonly MySqlConnection _admin;
        private readonly string _schema = "chest_" + Guid.NewGuid().ToString("N");
        public MySqlConnection Connection { get; }
        public HabbiconDatabaseTests.TestDatabase Database { get; }
        public TestWiredDefinitions Definitions { get; }
        public DatabaseWiredChestStore Store { get; }
        public Fixture()
        {
            var options = new MySqlConnectionStringBuilder(Environment.GetEnvironmentVariable("WIRED_CHEST_DATABASE"));
            Assert.True(options.Server is "127.0.0.1" or "localhost" || Path.IsPathRooted(options.Server));
            options.AllowUserVariables = true;
            options.Database = "mysql";
            _admin = new(options.ConnectionString);
            _admin.Open();
            _admin.Execute($"CREATE DATABASE `{_schema}`");
            options.Database = _schema;
            Database = new(options.ConnectionString);
            Connection = new(options.ConnectionString);
            Connection.Open();
            Connection.Execute("""
                CREATE TABLE users(id INT PRIMARY KEY,credits INT NOT NULL) ENGINE=InnoDB;
                CREATE TABLE rooms(id INT UNSIGNED PRIMARY KEY,owner INT);
                INSERT INTO rooms VALUES(42,1);
                CREATE TABLE user_currencies(user_id INT,type INT,amount INT,PRIMARY KEY(user_id,type)) ENGINE=InnoDB;
                INSERT INTO user_currencies VALUES(1,5,100),(2,5,100);
                CREATE TABLE items(id INT UNSIGNED PRIMARY KEY,user_id INT UNSIGNED,room_id INT UNSIGNED,base_item INT UNSIGNED,
                    extra_data TEXT,limited_number INT UNSIGNED DEFAULT 0,limited_stack INT UNSIGNED DEFAULT 0) ENGINE=InnoDB;
                CREATE TABLE furniture(id INT PRIMARY KEY,item_name VARCHAR(100),interaction_type VARCHAR(25));
                CREATE TABLE wired_item_configurations(item_id INT UNSIGNED PRIMARY KEY,box_name VARCHAR(100),configuration LONGTEXT);
                CREATE TABLE room_music_playlist(disc_id INT UNSIGNED PRIMARY KEY);
                INSERT INTO users VALUES (1,100),(2,100),(7,0),(8,0);
                INSERT INTO items(id,user_id,room_id,base_item,extra_data) VALUES (201,1,0,1,'kept'),(202,2,0,1,''),(203,1,42,1,'');
                """);
            var sql = File.ReadAllText(HabbiconPacketTests.Repo("Database/Migrations/65_WiredChests.sql"));
            Connection.Execute(sql);
            Definitions = new(() => new() { [1] = new() { Id = 1, SpriteId = 44, AllowTrade = true, Type = ItemType.Floor } });
            Store = new(Database, Definitions);
        }
        public Item Chest(uint id, uint owner, WiredChestKind kind, bool starter = false)
        {
            Connection.Execute("INSERT INTO items(id,user_id,room_id,base_item,extra_data) VALUES (@id,@owner,42,99,'')", new { id, owner });
            var item = new Item
            {
                Id = id,
                OwnerId = owner,
                RoomId = 42,
                Definition = new()
                {
                    Id = 99,
                    ItemName = starter ? "wf_storage_furni_starter" : kind == WiredChestKind.Coins ? "wf_storage_coins1" : "wf_storage_furni1",
                    InteractionType = kind == WiredChestKind.Coins ? InteractionType.WiredChestCoins : InteractionType.WiredChestFurni
                }
            };
            Assert.NotNull(Store.Load(item));

            return item;
        }
        public void Enable(uint id) => Connection.Execute("UPDATE wired_chests SET settings=JSON_SET(settings,'$.Locked',false,'$.WiredEnabled',true) WHERE item_id=@id", new { id });
        public WiredChestTransfer Request(int user, params uint[] chests) => new() { RoomId = 42, UserId = user, ChestIds = chests };
        public void Dispose()
        {
            Connection.Dispose();
            _admin.Execute($"DROP DATABASE `{_schema}`");
            _admin.Dispose();
        }
    }
}
