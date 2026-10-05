using System.Buffers.Binary;
using System.Text.RegularExpressions;
using System.Reflection;
using System.Text;
using Dapper;
using MySqlConnector;
using Plus.Communication.Flash;
using Plus.Communication.Packets.Incoming.Marketplace;
using Plus.HabboHotel.Catalog.Marketplace;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.DataFormat;
using Plus.HabboHotel.Users;
using Plus.HabboHotel.Users.Inventory;
using Plus.HabboHotel.Users.Inventory.Furniture;
using Xunit;

namespace Plus.Tests;

public sealed class MarketplaceDatabaseFactAttribute : FactAttribute
{
    public MarketplaceDatabaseFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("PLUS_MARKETPLACE_PROBE_CONNECTION_STRING")))
            Skip = "Set PLUS_MARKETPLACE_PROBE_CONNECTION_STRING to a disposable task_refactor_tests_marketplace_ schema with the catalog_marketplace_offers and items tables.";
    }
}

// Runs the marketplace store, search and redemption against a real MariaDB schema. Each test truncates the two tables it uses.
[Collection("MarketplaceDatabase")]
public sealed class MarketplaceDatabaseTests
{
    private const int SellerId = 7;
    private const int BuyerId = 8;
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);
    private readonly string _connectionString = Environment.GetEnvironmentVariable("PLUS_MARKETPLACE_PROBE_CONNECTION_STRING")!;

    public MarketplaceDatabaseTests()
    {
        if (!new MySqlConnectionStringBuilder(_connectionString).Database.StartsWith("task_refactor_tests_marketplace_", StringComparison.Ordinal))
            throw new InvalidOperationException("Marketplace probe tests require a disposable task_refactor_tests_marketplace_ schema.");
        Dapper.DefaultTypeMap.MatchNamesWithUnderscores = true;
        using var connection = new MySqlConnection(_connectionString);
        connection.Execute("DELETE FROM `catalog_marketplace_offers`");
        connection.Execute("DELETE FROM `items`");
        connection.Execute("DELETE FROM `catalog_marketplace_data`");
    }

    [MarketplaceDatabaseFact]
    public void SearchMaterializesTypedRowsInOrderWithinTheWindow()
    {
        Insert(1, sprite: 100, asking: 50, total: 55, state: "1", timestamp: Now.ToUnixTimeSeconds() - 1000);
        Insert(2, sprite: 200, asking: 30, total: 33, state: "1", timestamp: Now.ToUnixTimeSeconds() - 1000, itemType: "2");
        Insert(3, sprite: 300, asking: 10, total: 11, state: "1", timestamp: Now.ToUnixTimeSeconds() - 200_000);
        Insert(4, sprite: 400, asking: 20, total: 22, state: "2", timestamp: Now.ToUnixTimeSeconds() - 1000);
        var manager = new Manager();

        var snapshot = Search(manager, -1, -1, 0);

        Assert.Equal(new[] { 2, 1 }, manager.Keys);
        Assert.Equal(new uint[] { 2, 1 }, manager.Items.Select(item => item.OfferId).ToArray());
        Assert.Equal(2, manager.Items[0].ItemType);
        Assert.Equal(2, snapshot.Offers.Length);
    }

    [MarketplaceDatabaseFact]
    public void SearchIncludesAnOfferExactlyAtTheCutoffAndDropsOneSecondEarlier()
    {
        var cutoff = Now.ToUnixTimeSeconds() - 172800;
        Insert(1, sprite: 100, asking: 50, total: 55, state: "1", timestamp: cutoff);
        Insert(2, sprite: 200, asking: 30, total: 33, state: "1", timestamp: cutoff - 1);
        var manager = new Manager();

        var snapshot = Search(manager, -1, -1, 0);

        Assert.Equal(new uint[] { 1 }, snapshot.Offers.Select(offer => offer.OfferId).ToArray());
    }

    [MarketplaceDatabaseFact]
    public void FutureAndPost2038ListingsMaterializeAsUtc()
    {
        var future = DateTimeOffset.FromUnixTimeSeconds(2_200_000_000).UtcDateTime;
        Insert(1, sprite: 100, asking: 50, total: 55, state: "1", timestamp: 2_200_000_000);
        var manager = new Manager();

        var snapshot = Search(manager, -1, -1, 0);

        Assert.Single(snapshot.Offers);
        using var connection = new MySqlConnection(_connectionString);
        Assert.Equal(future, connection.ExecuteScalar<DateTime>("SELECT `listed_at` FROM `catalog_marketplace_offers` WHERE `offer_id` = 1"));
    }

    [MarketplaceDatabaseFact]
    public void OwnOffersReportMinutesRemainingAndUnknownListingsAsExpired()
    {
        Insert(1, sprite: 100, asking: 50, total: 55, state: "1", timestamp: Now.ToUnixTimeSeconds() - 172800 + 300, seller: SellerId);
        Insert(2, sprite: 200, asking: 30, total: 33, state: "1", timestamp: 0, seller: SellerId);
        var manager = new MarketplaceManager(new MySqlDatabase(_connectionString), ItemData(), CatalogSnapshotTestSupport.Proxy<IItemFactory>((method, _) => throw new InvalidOperationException(method)), new FixedClock(Now));

        var own = manager.OwnOffers(SellerId);

        Assert.Equal(new[] { (1, 5), (2, 0) }, own.Offers.Select(offer => (offer.OfferId, offer.MinutesRemaining)).ToArray());
        Assert.Equal(new[] { 1, 3 }, own.Offers.Select(offer => offer.State).ToArray());
    }

    [MarketplaceDatabaseFact]
    public void StagedMigrationConvertsLegacyEpochsAndKeepsZeroAndNegativeUnknown()
    {
        var table = "probe_legacy_offers";
        var ddl = Regex.Match(File.ReadAllText(Path.Combine(RepositoryRoot(), "Resources", "SQLs", "Original Database.sql")),
            "CREATE TABLE `catalog_marketplace_offers`.*?ENGINE=[^;]*;", RegexOptions.Singleline).Value.Replace("`catalog_marketplace_offers`", $"`{table}`");
        using (var connection = new MySqlConnection(_connectionString))
        {
            connection.Execute($"DROP TABLE IF EXISTS `{table}`");
            connection.Execute(ddl);
            connection.Execute($"INSERT INTO `{table}` (`offer_id`,`item_id`,`user_id`,`asking_price`,`total_price`,`public_name`,`sprite_id`,`item_type`,`timestamp`,`extra_data`,`limited_number`,`limited_stack`,`furni_id`,`state`) VALUES " +
                "(1,900,7,1,1,'p',1,'1',0,'',0,0,1,'1'), (2,900,7,1,1,'p',1,'1',-1,'',0,0,2,'1'), (3,900,7,1,1,'p',1,'1',1700000000,'',0,0,3,'1'), (4,900,7,1,1,'p',1,'1',2200000000,'',0,0,4,'1')");
            foreach (var statement in File.ReadAllText(Path.Combine(RepositoryRoot(), "Database", "Migrations", "25_UseUtcMarketplaceTimes.sql"))
                         .Replace("`catalog_marketplace_offers`", $"`{table}`").Split(";\n", StringSplitOptions.RemoveEmptyEntries))
                connection.Execute(statement);

            var rows = connection.Query<(uint Id, DateTime? ListedAt)>($"SELECT `offer_id` AS Id, `listed_at` AS ListedAt FROM `{table}` ORDER BY `offer_id`").ToArray();
            var dropped = connection.ExecuteScalar<int>("SELECT COUNT(*) FROM information_schema.columns WHERE table_schema = DATABASE() AND table_name = @table AND column_name = 'timestamp'", new { table });
            connection.Execute($"DROP TABLE `{table}`");

            Assert.Equal(new uint[] { 1, 2, 3, 4 }, rows.Select(row => row.Id).ToArray());
            Assert.Null(rows[0].ListedAt);
            Assert.Null(rows[1].ListedAt);
            Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_700_000_000).UtcDateTime, rows[2].ListedAt);
            Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(2_200_000_000).UtcDateTime, rows[3].ListedAt);
            Assert.Equal(0, dropped);
        }
    }

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Plus Emulator.csproj"))) dir = dir.Parent;
        return dir!.FullName;
    }

    private static IItemDataManager ItemData() => CatalogSnapshotTestSupport.Proxy<IItemDataManager>((method, _) => method == "get_Items" ? new Dictionary<uint, ItemDefinition>() : throw new InvalidOperationException(method));

    [MarketplaceDatabaseFact]
    public void SearchDescendingAndBoundsUseTheTotalPrice()
    {
        Insert(1, sprite: 100, asking: 50, total: 55, state: "1", timestamp: Now.ToUnixTimeSeconds() - 1000);
        Insert(2, sprite: 200, asking: 30, total: 33, state: "1", timestamp: Now.ToUnixTimeSeconds() - 1000);
        Insert(3, sprite: 300, asking: 5, total: 6, state: "1", timestamp: Now.ToUnixTimeSeconds() - 1000);
        var manager = new Manager();

        Search(manager, 10, 56, 1);

        Assert.Equal(new uint[] { 1, 2 }, manager.Items.Select(item => item.OfferId).ToArray());
    }

    [MarketplaceDatabaseFact]
    public void ListingCommitsOfferAndRemovesFurniTogether()
    {
        InsertFurni(41, SellerId, roomId: 0);
        var seller = Seller();

        Assert.True(Listing().TryList(seller, 41, 100));

        Assert.Equal(1, Count("catalog_marketplace_offers"));
        Assert.Equal(0, Count("items"));
    }

    [MarketplaceDatabaseFact]
    public void ListingRollsBackWhenTheFurniIsNoLongerTheSellersToRemove()
    {
        InsertFurni(41, SellerId, roomId: 5);
        var seller = Seller();

        Assert.Throws<InvalidOperationException>(() => Listing().TryList(seller, 41, 100));

        Assert.Equal(0, Count("catalog_marketplace_offers"));
        Assert.Equal(1, Count("items"));
    }

    [MarketplaceDatabaseFact]
    public void ListingRollsBackTheOfferWhenTheFurniDeleteFails()
    {
        InsertFurni(41, SellerId, roomId: 0);
        var seller = Seller();
        using (var admin = new MySqlConnection(_connectionString))
            admin.Execute("RENAME TABLE `items` TO `items_probe_hidden`");
        try
        {
            var error = Assert.Throws<MySqlException>(() => Listing().TryList(seller, 41, 100));
            Assert.Contains("doesn't exist", error.Message, StringComparison.Ordinal);
        }
        finally
        {
            using var admin = new MySqlConnection(_connectionString);
            admin.Execute("RENAME TABLE `items_probe_hidden` TO `items`");
        }

        Assert.Equal(0, Count("catalog_marketplace_offers"));
    }

    [MarketplaceDatabaseFact]
    public void ClaimRollsBackAndKeepsEverySaleWhenTheWalletCannotHoldIt()
    {
        Insert(1, sprite: 100, asking: 25, total: 26, state: "2", timestamp: 1, seller: SellerId);
        Insert(2, sprite: 200, asking: 5, total: 6, state: "2", timestamp: 1, seller: SellerId);

        var owed = Store().ClaimSold(SellerId, _ => false);

        Assert.Null(owed);
        Assert.Equal(2, CountWhere("state = '2' AND user_id = " + SellerId));
    }

    [MarketplaceDatabaseFact]
    public void ClaimPaysExactlyTheClaimedSalesAndKeepsALaterSale()
    {
        Insert(1, sprite: 100, asking: 25, total: 26, state: "2", timestamp: 1, seller: SellerId);
        Insert(2, sprite: 200, asking: 5, total: 6, state: "2", timestamp: 1, seller: SellerId);
        Task? concurrent = null;

        // A sale completed after the claim read its rows must survive the claim: the delete names only the claimed ids.
        var owed = Store().ClaimSold(SellerId, _ =>
        {
            concurrent = Task.Run(() => Insert(3, sprite: 300, asking: 7, total: 8, state: "2", timestamp: 1, seller: SellerId));
            return true;
        });
        concurrent!.Wait(TimeSpan.FromSeconds(30));

        Assert.Equal(30, owed);
        Assert.Equal(new uint[] { 3 }, OfferIds("state = '2' AND user_id = " + SellerId));
    }

    [MarketplaceDatabaseFact]
    public void ClaimRejectsNegativeSalesWithoutChangingAnything()
    {
        Insert(1, sprite: 100, asking: 25, total: 26, state: "2", timestamp: 1, seller: SellerId);
        Insert(2, sprite: 200, asking: -3, total: 6, state: "2", timestamp: 1, seller: SellerId);

        Assert.Null(Store().ClaimSold(SellerId, _ => true));

        Assert.Equal(2, CountWhere("state = '2' AND user_id = " + SellerId));
    }

    [MarketplaceDatabaseFact]
    public void RedemptionPaysOnceAcrossRepeatedCalls()
    {
        Insert(1, sprite: 100, asking: 25, total: 26, state: "2", timestamp: 1, seller: SellerId);
        Insert(2, sprite: 200, asking: 5, total: 6, state: "2", timestamp: 1, seller: SellerId);
        var (client, _) = HabbiconTestSupport.Client(new Habbo { Id = SellerId, Username = "seller", Credits = 1000 });
        var redeem = new MarketplaceRedemptionService(Store());

        redeem.Redeem(client);
        redeem.Redeem(client);

        Assert.Equal(1030, client.GetHabbo().Credits);
        Assert.Equal(0, CountWhere("state = '2' AND user_id = " + SellerId));
    }

    private (GameClient Client, int Id) Buyer(int credits)
    {
        var habbo = new Habbo { Id = BuyerId, Username = "buyer", Credits = credits, Inventory = new InventoryComponent { Furniture = new FurnitureInventoryComponent([], []) } };
        return (HabbiconTestSupport.Client(habbo).Client, BuyerId);
    }

    private MarketplacePurchaseService Purchase((GameClient Client, int Id) buyer)
    {
        var definitions = new Dictionary<uint, ItemDefinition> { [900] = new() { Id = 900, SpriteId = 55, PublicName = "Probe", ItemName = "probe", Type = ItemType.Floor } };
        var items = CatalogSnapshotTestSupport.Proxy<IItemDataManager>((method, _) => method == "get_Items" ? definitions : throw new InvalidOperationException(method));
        var averages = new Dictionary<int, int>();
        var counts = new Dictionary<int, int>();
        var marketplace = CatalogSnapshotTestSupport.Proxy<IMarketplaceManager>((method, _) => method switch
        {
            "get_MarketAverages" => averages,
            "get_MarketCounts" => counts,
            _ => throw new InvalidOperationException(method),
        });
        var search = CatalogSnapshotTestSupport.Proxy<IMarketplaceOfferSearchService>((method, _) => method == "Search" ? new MarketplaceOffersSnapshot([]) : throw new InvalidOperationException(method));
        return new MarketplacePurchaseService(new MarketplacePurchaseStore(new MySqlDatabase(_connectionString)), items, marketplace, search, new FixedClock(Now));
    }

    private string[] States()
    {
        using var connection = new MySqlConnection(_connectionString);
        return connection.Query<string>("SELECT `state` FROM `catalog_marketplace_offers` ORDER BY `offer_id`").ToArray();
    }

    private Habbo Seller(uint furniId = 41)
    {
        var item = new InventoryItem
        {
            Id = furniId, OwnerId = SellerId, ExtraData = FurniObjectData.Empty,
            Definition = new ItemDefinition { Id = 900, SpriteId = 55, PublicName = "Probe", ItemName = "probe", Type = ItemType.Floor, AllowTrade = true, AllowMarketplaceSell = true },
        };
        return new Habbo { Id = SellerId, Username = "seller", Inventory = new InventoryComponent { Furniture = new FurnitureInventoryComponent([item], []) } };
    }

    private MarketplaceListingService Listing() => new(Store(), new Manager(), new FixedClock(Now));

    private MarketplaceOffersSnapshot Search(Manager manager, int min, int max, int mode) =>
        new MarketplaceOfferSearchService(new MySqlDatabase(_connectionString), manager, new FixedClock(Now)).Search(min, max, "", mode);

    private MarketplaceOfferStore Store() => new(new MySqlDatabase(_connectionString));

    private void Insert(uint offerId, int sprite, int asking, int total, string state, long timestamp, string itemType = "1", int seller = BuyerId, uint itemId = 900, uint furniId = 1)
    {
        using var connection = new MySqlConnection(_connectionString);
        // Zero and negative epochs are the unknown time the migration keeps as NULL.
        DateTime? listedAt = timestamp <= 0 ? null : DateTimeOffset.FromUnixTimeSeconds(timestamp).UtcDateTime;
        connection.Execute("INSERT INTO `catalog_marketplace_offers` (`offer_id`,`item_id`,`user_id`,`asking_price`,`total_price`,`public_name`,`sprite_id`,`item_type`,`listed_at`,`extra_data`,`limited_number`,`limited_stack`,`furni_id`,`state`) " +
            "VALUES (@offerId,@itemId,@seller,@asking,@total,'probe',@sprite,@itemType,@listedAt,'',0,0,@furniId,@state)",
            new { offerId, itemId, seller, asking, total, sprite, itemType, listedAt, furniId, state });
    }

    [MarketplaceDatabaseFact]
    public void PurchaseDeliversOnceAndRecordsTheSale()
    {
        Insert(1, sprite: 55, asking: 100, total: 101, state: "1", timestamp: Now.ToUnixTimeSeconds() - 1000, seller: SellerId, furniId: 77);
        var buyer = Buyer(credits: 1000);
        var purchase = Purchase(buyer);

        Assert.Equal(MarketplacePurchaseOutcome.Bought, purchase.Buy(buyer.Client, 1));
        Assert.Equal(MarketplacePurchaseOutcome.Sold, purchase.Buy(buyer.Client, 1));

        Assert.Equal(899, buyer.Client.GetHabbo().Credits);
        Assert.Equal(new[] { "2" }, States());
        Assert.Equal(1, CountItems("`id` = 77 AND `user_id` = " + BuyerId + " AND `base_item` = 900"));
        using var connection = new MySqlConnection(_connectionString);
        Assert.Equal((1, 101), connection.QuerySingle<(int Sold, int Avg)>("SELECT `sold` AS Sold, `avgprice` AS Avg FROM `catalog_marketplace_data` WHERE `sprite` = 55"));
    }

    [MarketplaceDatabaseFact]
    public void PurchaseRefusalsChangeNothing()
    {
        Insert(1, sprite: 55, asking: 100, total: 101, state: "1", timestamp: Now.ToUnixTimeSeconds() - 1000, seller: SellerId, furniId: 77);
        Insert(2, sprite: 55, asking: 100, total: 101, state: "1", timestamp: Now.ToUnixTimeSeconds() - 200_000, seller: SellerId, furniId: 78);
        Insert(3, sprite: 55, asking: 100, total: 101, state: "1", timestamp: Now.ToUnixTimeSeconds() - 1000, seller: SellerId, itemId: 901, furniId: 79);
        Insert(4, sprite: 55, asking: 100, total: 101, state: "1", timestamp: Now.ToUnixTimeSeconds() - 1000, seller: BuyerId, furniId: 80);
        var poor = Buyer(credits: 50);
        var purchase = Purchase(poor);

        Assert.Equal(MarketplacePurchaseOutcome.InsufficientCredits, purchase.Buy(poor.Client, 1));
        var buyer = Buyer(credits: 1000);
        Assert.Equal(MarketplacePurchaseOutcome.Expired, Purchase(buyer).Buy(buyer.Client, 2));
        Assert.Equal(MarketplacePurchaseOutcome.UnknownItem, Purchase(buyer).Buy(buyer.Client, 3));
        Assert.Equal(MarketplacePurchaseOutcome.OwnOffer, Purchase(buyer).Buy(buyer.Client, 4));

        Assert.Equal(new[] { "1", "1", "1", "1" }, States());
        Assert.Equal(0, CountItems("`id` IN (77, 78, 79, 80)"));
        Assert.Equal(50, poor.Client.GetHabbo().Credits);
        Assert.Equal(1000, buyer.Client.GetHabbo().Credits);
    }

    [MarketplaceDatabaseFact]
    public void PurchaseRejectsInvalidPricesAndLimitedValuesBeforeAnyWrite()
    {
        Insert(1, sprite: 55, asking: 0, total: 0, state: "1", timestamp: Now.ToUnixTimeSeconds() - 1000, seller: SellerId, furniId: 77);
        Insert(2, sprite: 55, asking: -5, total: -5, state: "1", timestamp: Now.ToUnixTimeSeconds() - 1000, seller: SellerId, furniId: 78);
        Insert(3, sprite: 55, asking: 100, total: 101, state: "1", timestamp: Now.ToUnixTimeSeconds() - 1000, seller: SellerId, furniId: 79);
        using (var connection = new MySqlConnection(_connectionString))
            connection.Execute("UPDATE `catalog_marketplace_offers` SET `limited_number` = -1 WHERE `offer_id` = 3");
        var buyer = Buyer(credits: 1000);

        Assert.Equal(MarketplacePurchaseOutcome.InvalidOffer, Purchase(buyer).Buy(buyer.Client, 1));
        Assert.Equal(MarketplacePurchaseOutcome.InvalidOffer, Purchase(buyer).Buy(buyer.Client, 2));
        Assert.Equal(MarketplacePurchaseOutcome.InvalidOffer, Purchase(buyer).Buy(buyer.Client, 3));

        Assert.Equal(new[] { "1", "1", "1" }, States());
        Assert.Equal(0, CountItems("`id` IN (77, 78, 79)"));
        Assert.Equal(1000, buyer.Client.GetHabbo().Credits);
    }

    [MarketplaceDatabaseFact]
    public void PreparationFailureBeforeTheFirstWriteRollsBackWithNothingChanged()
    {
        Insert(1, sprite: 55, asking: 100, total: 101, state: "1", timestamp: Now.ToUnixTimeSeconds() - 1000, seller: SellerId, furniId: 77);
        var store = new MarketplacePurchaseStore(new MySqlDatabase(_connectionString));

        Assert.Throws<InvalidOperationException>(() => store.Claim(new MarketplacePurchaseRequest(1, BuyerId, 1000, Now.UtcDateTime.AddSeconds(-172800),
            _ => throw new InvalidOperationException("forced preparation failure"))));

        Assert.Equal(new[] { "1" }, States());
        Assert.Equal(0, CountItems("`id` = 77"));
        using var connection = new MySqlConnection(_connectionString);
        Assert.Equal(0, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM `catalog_marketplace_data`"));
    }

    [MarketplaceDatabaseFact]
    public void ForcedDeliveryFailureRollsBackTheClaimAndCharge()
    {
        Insert(1, sprite: 55, asking: 100, total: 101, state: "1", timestamp: Now.ToUnixTimeSeconds() - 1000, seller: SellerId, furniId: 77);
        using (var connection = new MySqlConnection(_connectionString))
            connection.Execute("INSERT INTO `items` (`id`,`user_id`,`room_id`,`base_item`,`extra_data`) VALUES (77, 9, 0, 1, '')");
        var buyer = Buyer(credits: 1000);

        var error = Assert.Throws<MySqlException>(() => Purchase(buyer).Buy(buyer.Client, 1));
        Assert.Contains("Duplicate entry", error.Message, StringComparison.Ordinal);

        Assert.Equal(new[] { "1" }, States());
        using (var connection = new MySqlConnection(_connectionString))
            Assert.Equal(0, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM `catalog_marketplace_data`"));
        Assert.Equal(1000, buyer.Client.GetHabbo().Credits);
    }

    private void InsertFurni(uint id, int owner, int roomId)
    {
        using var connection = new MySqlConnection(_connectionString);
        connection.Execute("INSERT INTO `items` (`id`,`user_id`,`room_id`,`base_item`,`extra_data`) VALUES (@id,@owner,@roomId,1,'')", new { id, owner, roomId });
    }

    private int Count(string table)
    {
        using var connection = new MySqlConnection(_connectionString);
        return connection.ExecuteScalar<int>($"SELECT COUNT(*) FROM `{table}`");
    }

    private int CountItems(string predicate)
    {
        using var connection = new MySqlConnection(_connectionString);
        return connection.ExecuteScalar<int>($"SELECT COUNT(*) FROM `items` WHERE {predicate}");
    }

    private int CountWhere(string predicate)
    {
        using var connection = new MySqlConnection(_connectionString);
        return connection.ExecuteScalar<int>($"SELECT COUNT(*) FROM `catalog_marketplace_offers` WHERE {predicate}");
    }

    private uint[] OfferIds(string predicate)
    {
        using var connection = new MySqlConnection(_connectionString);
        return connection.Query<uint>($"SELECT `offer_id` FROM `catalog_marketplace_offers` WHERE {predicate} ORDER BY `offer_id`").ToArray();
    }

    private sealed class MySqlDatabase(string connectionString) : Plus.Database.IDatabase
    {
        public bool IsConnected() => true;
        public System.Data.IDbConnection Connection() => new MySqlConnection(connectionString);
    }

    private sealed class Manager : IMarketplaceManager
    {
        public List<MarketOffer> Items { get; } = new();
        public List<int> Keys { get; } = new();
        public Dictionary<int, int> MarketAverages { get; } = new();
        public Dictionary<int, int> MarketCounts { get; } = new();
        List<int> IMarketplaceManager.MarketItemKeys => Keys;
        List<MarketOffer> IMarketplaceManager.MarketItems => Items;
        public int AvgPriceForSprite(int spriteId) => spriteId * 2;
        public int OfferCountForSprite(uint spriteId) => 0;
        public MarketplaceItemStats ItemStats(uint spriteId) => new(0, 0);
        public MarketplaceOwnOffers OwnOffers(int userId) => new(0, []);
        public int CalculateComissionPrice(float price) => Convert.ToInt32(Math.Ceiling(price / 100 * 1));
        public Task<bool> TryCancelOffer(Habbo habbo, uint offerId) => Task.FromResult(false);
        public Task<MarketOffer?> GetOffer(uint offerId) => Task.FromResult<MarketOffer?>(null);
        public Task DeleteOffer(uint offerId) => Task.CompletedTask;
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
