using System.Data;
using Dapper;
using MySqlConnector;
using Plus.Database;
using Plus.HabboHotel.Habbicons;
using Plus.HabboHotel.Users;
using Xunit;

namespace Plus.Tests;

public sealed class HabbiconDatabaseFactAttribute : FactAttribute
{
    public HabbiconDatabaseFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("PLUS_HABBICONS_TEST_CONNECTION_STRING"))) {
            Skip = "Set PLUS_HABBICONS_TEST_CONNECTION_STRING to a disposable task_habicons_tests_ database.";
        }
    }
}

// Tests are deliberately restricted to a disposable schema and run sequentially in this class.
public class HabbiconDatabaseTests
{
    private readonly TestDatabase _database;
    private readonly HabbiconService _service;
    private readonly CountingTimeProvider _clock = new(new DateTimeOffset(2040, 1, 2, 3, 4, 5, TimeSpan.Zero));
    private const int UserId = 910001;

    private static readonly object PristineImportLock = new();
    private static string? _importedConnectionString;

    // The pristine dump takes minutes to load, so each disposable schema gets it once per process before any fixture state is written.
    private static void ImportPristineSchemaOnce(string connectionString)
    {
        lock (PristineImportLock) {
            if (_importedConnectionString == connectionString) {
                return;
            }

            using var connection = new MySqlConnection(connectionString);
            connection.Open();
            connection.Execute(File.ReadAllText(HabbiconPacketTests.Repo("Resources/SQLs/Original Database.sql")), commandTimeout: 900);
            _importedConnectionString = connectionString;
        }
    }

    public HabbiconDatabaseTests()
    {
        string connectionString = Environment.GetEnvironmentVariable("PLUS_HABBICONS_TEST_CONNECTION_STRING")!;
        _database = new(connectionString);
        var builder = new MySqlConnectionStringBuilder(connectionString);

        if (!builder.Database.StartsWith("task_habicons_tests_", StringComparison.Ordinal)) {
            throw new InvalidOperationException("Habicon database tests require a disposable task_habicons_tests_ schema.");
        }

        ImportPristineSchemaOnce(connectionString);
        Execute(File.ReadAllText(HabbiconPacketTests.Repo("Resources/SQLs/Updates/14_Habbicons.sql")));
        Execute("DELETE FROM users_habbicons; DELETE FROM users WHERE id = 910001");

        using (var connection = _database.Connection()) {
            bool hasTicket = connection.QuerySingle<int>("SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'users' AND COLUMN_NAME = 'auth_ticket'") > 0;
            Execute(hasTicket
                ? "INSERT INTO users (id, username, auth_ticket, credits) VALUES (910001, 'habicon_tests', '', 100)"
                : "INSERT INTO users (id, username, credits) VALUES (910001, 'habicon_tests', 100)");
            Execute("INSERT INTO user_currencies (user_id, type, amount) VALUES (910001, 0, 20), (910001, 5, 20)");
        }

        Execute("UPDATE habbicons SET available = TRUE, default_owned = (id = 28), cost_credits = IF(id IN (28,38,49,60,71), 0, 5), cost_points = 0, points_type = 0");
        Execute("UPDATE habbicon_collections SET cost_credits = 40, cost_points = 0, points_type = 0");
        _service = new(_database, _clock);
    }

    [HabbiconDatabaseFact]
    public void StarterUnavailableFreeClaimAndOwnershipSurviveNewServiceInstance()
    {
        var snapshot = _service.Load(UserId);
        Assert.Equal(44, snapshot.Items.Count);
        Assert.Equal(new[] { 28 }, snapshot.Items.Values.Where(item => item.Owned).Select(item => item.Id));
        Assert.False(_service.Use(UserId, 61));
        Assert.False(_service.Use(UserId, int.MaxValue));
        Execute("UPDATE habbicons SET available = FALSE WHERE id = 61");
        Assert.Equal(HabbiconState.Unavailable, _service.Load(UserId).RequireItem(61).State);
        Assert.Equal(1, Assert.Throws<HabbiconRejected>(() => _service.Change(UserId, HabbiconAction.Buy, 61)).Code);
        Execute("UPDATE habbicons SET available = TRUE, cost_credits = 0 WHERE id = 61");
        Assert.Equal(HabbiconState.Claimable, _service.Load(UserId).RequireItem(61).State);
        _service.Change(UserId, HabbiconAction.Claim, 61);
        Assert.True(new HabbiconService(_database, _clock).Load(UserId).RequireItem(61).Owned);
        Assert.Equal(100, Scalar("SELECT credits FROM users WHERE id = 910001"));
        Assert.Equal(4, Assert.Throws<HabbiconRejected>(() => _service.Change(UserId, HabbiconAction.Claim, 61)).Code);
    }

    [HabbiconDatabaseFact]
    public void LiveWalletIsAuthoritativeAndTheChargedCurrencyPersistsOnlyAfterCommit()
    {
        var habbo = new Habbo { Id = UserId, Credits = 50, Duckets = 7, Diamonds = 9 };
        Execute("UPDATE habbicons SET cost_points = 2, points_type = 5 WHERE id = 61");
        _service.Change(habbo, HabbiconAction.Buy, 61);
        Assert.Equal((45, 7, 7), (habbo.Credits, habbo.Duckets, habbo.Diamonds));
        using var connection = _database.Connection();
        // Only the charged balance is written; duckets keep their stored value.
        Assert.Equal(45, Scalar("SELECT credits FROM users WHERE id = 910001"));
        Assert.Equal([(0, 20), (5, 7)], connection.Query<(int, int)>("SELECT type, amount FROM user_currencies WHERE user_id = 910001 ORDER BY type"));
        Execute("UPDATE habbicons SET cost_points = 99, points_type = 0 WHERE id = 62");
        Assert.Equal(3, Assert.Throws<HabbiconRejected>(() => _service.Change(habbo, HabbiconAction.Buy, 62)).Code);
        Assert.Equal(45, habbo.Credits);
        Assert.Equal(45, Scalar("SELECT credits FROM users WHERE id = 910001"));
        Assert.False(_service.Load(UserId).RequireItem(62).Owned);
        Execute("UPDATE habbicons SET cost_credits = 99 WHERE id = 62");
        Assert.Equal(2, Assert.Throws<HabbiconRejected>(() => _service.Change(habbo, HabbiconAction.Buy, 62)).Code);
        // Any activity point type can price a habbicon; type 9 is charged from its own balance.
        Execute("UPDATE habbicons SET cost_credits = 1, points_type = 9 WHERE id = 62");
        Assert.Equal(3, Assert.Throws<HabbiconRejected>(() => _service.Change(habbo, HabbiconAction.Buy, 62)).Code);
        habbo.Currencies[9] = 100;
        _service.Change(habbo, HabbiconAction.Buy, 62);
        Assert.Equal((44, 1, 7), (habbo.Credits, habbo.Currencies[9], habbo.Diamonds));
        Assert.Equal(1, Scalar("SELECT amount FROM user_currencies WHERE user_id = 910001 AND type = 9"));
    }

    [HabbiconDatabaseFact]
    public void CollectionPurchasePreservesFavoritesUnlocksUnseenRewardAndClaimsExactlyOnce()
    {
        _service.Change(UserId, HabbiconAction.Buy, 61);
        _service.Change(UserId, HabbiconAction.Favorite, 61);
        var change = _service.Change(UserId, HabbiconAction.BuyCollection, 6);
        Assert.True(change.Snapshot.Collections.Single(set => set.Id == 6).Completed);
        Assert.Equal(HabbiconState.Favorite, change.Snapshot.RequireItem(61).State);
        Assert.Equal(HabbiconState.Claimable, change.Snapshot.RequireItem(71).State);
        Assert.Contains(71, change.Snapshot.Unseen);
        Assert.False(_service.Use(UserId, 71));
        _service.Change(UserId, HabbiconAction.Claim, 71);
        Assert.True(_service.Use(UserId, 71));
        Assert.Equal(55, Scalar("SELECT credits FROM users WHERE id = 910001"));
        Assert.Equal(4, Assert.Throws<HabbiconRejected>(() => _service.Change(UserId, HabbiconAction.Claim, 71)).Code);
        Assert.Equal(4, Assert.Throws<HabbiconRejected>(() => _service.Change(UserId, HabbiconAction.BuyCollection, 6)).Code);
        _service.ClearUnseen(UserId, new[] { 71 });
        Assert.DoesNotContain(71, _service.Load(UserId).Unseen);
        Assert.Contains(61, _service.Load(UserId).Unseen);
        _service.ClearUnseen(UserId, Array.Empty<int>());
        Assert.Empty(_service.Load(UserId).Unseen);
    }

    [HabbiconDatabaseFact]
    public void RecentUseIsDistinctOrderedLimitedAndDoesNotDropFavorites()
    {
        _service.Change(UserId, HabbiconAction.BuyCollection, 6);
        _service.Change(UserId, HabbiconAction.Favorite, 61);

        for (int id = 61; id <= 70; id++) {
            Assert.True(_service.Use(UserId, id));
        }

        Assert.True(_service.Use(UserId, 28));
        Assert.True(_service.Use(UserId, 61));
        Assert.Equal(new[] { 61, 28, 70, 69, 68, 67, 66, 65, 64, 63 },
            new HabbiconService(_database, _clock).Load(UserId).Recent);
        Assert.Equal(HabbiconState.Favorite, _service.Load(UserId).RequireItem(61).State);
        _service.Change(UserId, HabbiconAction.Unfavorite, 61);
        Assert.Equal(HabbiconState.Owned, _service.Load(UserId).RequireItem(61).State);
    }

    [HabbiconDatabaseFact]
    public void UseStoresUtcFractionAndAdvancesFutureMaximumByOneMillisecond()
    {
        _service.Change(UserId, HabbiconAction.Buy, 61);
        var future = new DateTimeOffset(2041, 2, 3, 4, 5, 6, TimeSpan.Zero).AddTicks(1_234_560);
        Execute("UPDATE users_habbicons SET last_used = '2041-02-03 04:05:06.123456' WHERE user_id = 910001 AND habbicon_id = 61");

        Assert.True(_service.Use(UserId, 28));

        using var connection = _database.Connection();
        Assert.Equal(future.AddMilliseconds(1), connection.QuerySingle<DateTimeOffset>(
            "SELECT last_used FROM users_habbicons WHERE user_id = 910001 AND habbicon_id = 28"));
        Assert.Equal(new[] { 28, 61 }, _service.Load(UserId).Recent.Take(2));
    }

    [HabbiconDatabaseFact]
    public void MaximumUsageTimeRejectsBeforeUpdateAndRollsBackOwnershipRow()
    {
        _service.Change(UserId, HabbiconAction.Buy, 61);
        Execute("UPDATE users_habbicons SET last_used = '9999-12-31 23:59:59.999999' WHERE user_id = 910001 AND habbicon_id = 61");

        Assert.Equal(1, Assert.Throws<HabbiconRejected>(() => _service.Use(UserId, 28)).Code);

        Assert.Equal(0, Scalar("SELECT COUNT(*) FROM users_habbicons WHERE user_id = 910001 AND habbicon_id = 28"));
        using var connection = _database.Connection();
        Assert.Equal(new DateTimeOffset(9999, 12, 31, 23, 59, 59, TimeSpan.Zero).AddTicks(9_999_990),
            connection.QuerySingle<DateTimeOffset>(
                "SELECT last_used FROM users_habbicons WHERE user_id = 910001 AND habbicon_id = 61"));
    }

    [HabbiconDatabaseFact]
    public void PurchaseSamplesClockOnceAndReusesItForClubSpending()
    {
        var now = _clock.GetUtcNow();
        _clock.ResetCalls();
        var membership = new Plus.HabboHotel.Subscriptions.ClubMembership(
            now.AddDays(1), now.AddDays(-1),
            now.AddDays(-1));

        _service.Change(UserId, HabbiconAction.Buy, 61, membership: membership);

        Assert.Equal(1, _clock.Calls);
        using var connection = _database.Connection();
        Assert.Equal(now, connection.QuerySingle<DateTimeOffset>(
            "SELECT spent_at FROM club_credit_spending WHERE user_id = 910001"));
    }

    [HabbiconDatabaseFact]
    public void MigrationPreservesUtcMillisecondsNullsFutureValuesAndRecentIndex()
    {
        Execute("""
            DELETE FROM users_habbicons WHERE user_id = 910001;
            ALTER TABLE users_habbicons DROP INDEX recent;
            ALTER TABLE users_habbicons MODIFY last_used BIGINT NULL DEFAULT 0;
            ALTER TABLE users_habbicons ADD KEY recent (user_id, last_used);
            INSERT INTO users_habbicons (user_id, habbicon_id, state, last_used) VALUES
                (910001, 61, 2, NULL),
                (910001, 62, 2, 0),
                (910001, 63, 2, -1),
                (910001, 64, 2, 1700000000123),
                (910001, 65, 2, 2200000000456);
            """);

        Execute(File.ReadAllText(HabbiconPacketTests.Repo(
            "Database/Migrations/37_UseUtcHabbiconUsageTimes.sql")));

        using var connection = _database.Connection();
        var values = connection.Query<(int Id, DateTimeOffset? Used)>(
            "SELECT habbicon_id AS Id, last_used AS Used FROM users_habbicons WHERE user_id = 910001 ORDER BY habbicon_id")
            .ToArray();
        Assert.Null(values[0].Used);
        Assert.Null(values[1].Used);
        Assert.Null(values[2].Used);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_123), values[3].Used);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(2_200_000_000_456), values[4].Used);
        Assert.Equal("datetime:6:YES", connection.QuerySingle<string>("""
            SELECT CONCAT(DATA_TYPE, ':', DATETIME_PRECISION, ':', IS_NULLABLE)
            FROM information_schema.columns
            WHERE table_schema = DATABASE() AND table_name = 'users_habbicons' AND column_name = 'last_used'
            """));
        Assert.Equal("user_id,last_used", connection.QuerySingle<string>("""
            SELECT GROUP_CONCAT(column_name ORDER BY seq_in_index)
            FROM information_schema.statistics
            WHERE table_schema = DATABASE() AND table_name = 'users_habbicons' AND index_name = 'recent'
            """));
        Assert.Contains("last_used DATETIME(6) NULL DEFAULT NULL",
            File.ReadAllText(HabbiconPacketTests.Repo("Resources/SQLs/Original Database.sql")));
    }

    [HabbiconDatabaseFact]
    public async Task ConcurrentIndividualCatalogAndCollectionRequestsCannotChargeTheSameOwnershipTwice()
    {
        var habbo = new Habbo { Id = UserId, Credits = 100, Duckets = 20, Diamonds = 20 };
        var outcomes = await Task.WhenAll(Enumerable.Range(0, 12).Select(index => Task.Run(() =>
        {
            try {
                if (index % 2 == 0) {
                    _service.Change(habbo, HabbiconAction.Buy, 61);
                }
                else {
                    _service.BuyCatalog(habbo, 61, 5, 0, 0);
                }

                return true;
            }
            catch (HabbiconRejected rejected) {
                Assert.Equal(4, rejected.Code);

                return false;
            }
        })));
        Assert.Equal(1, outcomes.Count(success => success));
        Assert.Equal(95, habbo.Credits);
        Assert.Equal(95, Scalar("SELECT credits FROM users WHERE id = 910001"));
        _service.Change(habbo, HabbiconAction.BuyCollection, 6);
        Assert.Equal(55, habbo.Credits);
        Assert.Equal(4, Assert.Throws<HabbiconRejected>(() => _service.BuyCatalog(habbo, 62, 5, 0, 0)).Code);
    }

    [HabbiconDatabaseFact]
    public void SqlFailureAfterChargeRollsBackOwnershipAndLiveWallet()
    {
        var habbo = new Habbo { Id = UserId, Credits = 100, Duckets = 20, Diamonds = 20 };
        Execute("CREATE TRIGGER habicon_test_failure BEFORE INSERT ON users_habbicons FOR EACH ROW SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'injected ownership failure'");

        try {
            Assert.Throws<MySqlException>(() => _service.Change(habbo, HabbiconAction.Buy, 61));
            Assert.Equal(100, habbo.Credits);
            Assert.Equal(100, Scalar("SELECT credits FROM users WHERE id = 910001"));
            Assert.False(_service.Load(UserId).RequireItem(61).Owned);
        }
        finally {
            Execute("DROP TRIGGER habicon_test_failure");
        }
    }

    [HabbiconDatabaseFact]
    public void MigrationRerunPreservesCustomPricesHoldingsAndCatalogOffers()
    {
        _service.Change(UserId, HabbiconAction.Buy, 61);
        _service.Change(UserId, HabbiconAction.Favorite, 61);
        Execute("UPDATE habbicons SET cost_credits = 19 WHERE id = 62; UPDATE habbicon_collections SET cost_credits = 93 WHERE id = 6; UPDATE catalog_offers o JOIN catalog_offer_products p ON p.offer_id = o.id SET o.cost_credits = 27 WHERE p.habbicon_id = 62");
        int offers = Scalar("SELECT COUNT(*) FROM catalog_offer_products WHERE habbicon_id IS NOT NULL");
        Execute(File.ReadAllText(HabbiconPacketTests.Repo("Resources/SQLs/Updates/14_Habbicons.sql")));
        Assert.Equal(19, _service.Load(UserId).RequireItem(62).Credits);
        Assert.Equal(93, _service.Load(UserId).Collections.Single(set => set.Id == 6).Credits);
        Assert.Equal(HabbiconState.Favorite, _service.Load(UserId).RequireItem(61).State);
        Assert.Equal(27, Scalar("SELECT o.cost_credits FROM catalog_offers o JOIN catalog_offer_products p ON p.offer_id = o.id WHERE p.habbicon_id = 62 LIMIT 1"));
        Assert.Equal(offers, Scalar("SELECT COUNT(*) FROM catalog_offer_products WHERE habbicon_id IS NOT NULL"));
    }

    [HabbiconDatabaseFact]
    public async Task RconAwardWaitsForCommittedPurchaseAndShutdownRejectsFurtherWalletChanges()
    {
        var habbo = new Habbo
        {
            Id = UserId,
            Credits = 100,
            Duckets = 20,
            Diamonds = 20,
            HabboStats = new HabboStats(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, "", 0)
        };
        var (client, sent) = HabbiconTestSupport.Client(habbo);
        var clients = new Plus.HabboHotel.GameClients.GameClientManager(null!, null!);
        clients.RegisterClient(client, habbo.Id, "habicon_tests");
        var give = new Plus.Communication.RCON.Commands.User.GiveUserCurrencyCommand(new Plus.HabboHotel.Users.UserMaintenanceService(new Plus.HabboHotel.Users.UserMaintenanceStore(_database), new Plus.HabboHotel.Users.Authentication.AccountSessionGate(), clients));
        using var enteredPurchase = new ManualResetEventSlim();
        using var releasePurchase = new ManualResetEventSlim();
        using var startedAward = new ManualResetEventSlim();
        _database.BeforeConnection = () => { enteredPurchase.Set(); Assert.True(releasePurchase.Wait(TimeSpan.FromSeconds(5))); };
        var purchase = Task.Run(() => _service.Change(habbo, HabbiconAction.Buy, 61));
        Assert.True(enteredPurchase.Wait(TimeSpan.FromSeconds(5)));
        var award = Task.Run(async () => { startedAward.Set(); return await give.TryExecute(new[] { UserId.ToString(), "credits", "10" }); });

        try {
            Assert.True(startedAward.Wait(TimeSpan.FromSeconds(5)));
            Assert.False(award.Wait(TimeSpan.FromMilliseconds(100))); // It cannot read the pre-purchase wallet.
        }
        finally {
            releasePurchase.Set();
        }

        await purchase;
        Assert.True(await award);
        _database.BeforeConnection = null;
        Assert.Equal(105, habbo.Credits);
        Assert.Equal(105, Scalar("SELECT credits FROM users WHERE id = 910001"));
        sent.Clear();
        HabbiconMessagesForTest(client, await purchase);
        var balance = new Plus.Communication.Flash.FlashIncomingPacket { Buffer = sent.Single(p => p.Header == Plus.Communication.Packets.Outgoing.ServerPacketHeader.CreditBalanceComposer).Payload };
        Assert.Equal("105.0", balance.ReadString());
        SaveWallet(habbo);
        Assert.Equal(105, Scalar("SELECT credits FROM users WHERE id = 910001"));
        Assert.Equal(1, Assert.Throws<HabbiconRejected>(() => _service.Change(habbo, HabbiconAction.Buy, 62)).Code);
        Assert.False(await give.TryExecute(new[] { UserId.ToString(), "credits", "10" }));
        Assert.Equal(105, habbo.Credits);
    }

    private static void HabbiconMessagesForTest(Plus.HabboHotel.GameClients.GameClient client, HabbiconChange change) =>
        Plus.HabboHotel.Habbicons.HabbiconMessages.Publish(client, change);

    [HabbiconDatabaseFact]
    public async Task DirectFriendHabiconSendsOneTypedOnlineMessageAndUsesExistingOfflineFallback()
    {
        Execute("CREATE TABLE IF NOT EXISTS chatlogs_console (id INT PRIMARY KEY AUTO_INCREMENT, from_id INT NOT NULL, to_id INT NOT NULL, message TEXT NOT NULL, timestamp DATETIME(6) NOT NULL) ENGINE=InnoDB");
        Execute("CREATE TABLE IF NOT EXISTS messenger_offline_messages (id INT PRIMARY KEY AUTO_INCREMENT, from_id INT NOT NULL, to_id INT NOT NULL, message VARCHAR(255) NOT NULL, timestamp DATETIME(6) NOT NULL) ENGINE=InnoDB");
        Execute("DELETE FROM chatlogs_console WHERE from_id = 910001; DELETE FROM messenger_offline_messages WHERE from_id = 910001");
        var sender = new Habbo
        {
            Id = UserId,
            Messenger = new Plus.HabboHotel.Users.Messenger.HabboMessenger(
            new() { [910002] = new() { Id = 910002 } }, new(), new(), new FixedTimeProvider(FixedTimeProvider.Epoch))
        };
        var recipient = new Habbo
        {
            Id = 910002,
            AllowConsoleMessages = true,
            IgnoresComponent = new Plus.HabboHotel.Users.Ignores.IgnoresComponent(new()),
            Messenger = new Plus.HabboHotel.Users.Messenger.HabboMessenger(new() { [UserId] = new() { Id = UserId } }, new(), new(), new FixedTimeProvider(FixedTimeProvider.Epoch))
        };
        var (client, sent) = HabbiconTestSupport.Client(sender);
        var (target, received) = HabbiconTestSupport.Client(recipient);
        var clients = new Plus.HabboHotel.GameClients.GameClientManager(null!, null!);
        clients.RegisterClient(target, recipient.Id, "habicon_recipient");
        var handler = new Plus.Communication.Packets.Incoming.FriendList.SendMessengerMessageEvent(new Plus.HabboHotel.Friends.HabbiconMessengerService(_service, clients,
            new Plus.HabboHotel.Friends.HabbiconMessengerStore(_database),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<Plus.HabboHotel.Friends.HabbiconMessengerService>.Instance,
            new FixedTimeProvider(FixedTimeProvider.Epoch)));
        await handler.Parse(client, HabbiconTestSupport.Incoming(0, 910002, 7, 4, "28", ""));
        Assert.Equal(Plus.Communication.Packets.Outgoing.ServerPacketHeader.MessengerMessageComposer, Assert.Single(received).Header);
        Assert.Single(sent, p => p.Header == Plus.Communication.Packets.Outgoing.ServerPacketHeader.MessengerMessageAckComposer);
        Assert.DoesNotContain(received, p => p.Header == Plus.Communication.Packets.Outgoing.ServerPacketHeader.NewConsoleMessageComposer);
        Assert.Equal(new[] { 28 }, _service.Load(UserId).Recent);
        Assert.Equal(1, Scalar("SELECT COUNT(*) FROM chatlogs_console WHERE from_id = 910001"));
        Assert.Equal(0, Scalar("SELECT COUNT(*) FROM messenger_offline_messages WHERE from_id = 910001"));
        sent.Clear();
        received.Clear();
        await handler.Parse(client, HabbiconTestSupport.Incoming(0, 910002, 8, 4, "61", ""));
        Assert.Equal(Plus.Communication.Packets.Outgoing.ServerPacketHeader.MessengerMessageFailedComposer, Assert.Single(sent).Header);
        Assert.Empty(received);
        Assert.Equal(1, Scalar("SELECT COUNT(*) FROM chatlogs_console WHERE from_id = 910001"));
        clients.UnregisterClient(target, recipient.Id, "habicon_recipient");
        sent.Clear();
        await handler.Parse(client, HabbiconTestSupport.Incoming(0, 910002, 9, 4, "28", ""));
        Assert.Single(sent, p => p.Header == Plus.Communication.Packets.Outgoing.ServerPacketHeader.MessengerMessageAckComposer);
        using var connection = _database.Connection();
        Assert.Equal(":duck_duck:", connection.QuerySingle<string>("SELECT message FROM messenger_offline_messages WHERE from_id = 910001"));
    }

    [HabbiconDatabaseFact]
    public async Task RconSyncCannotPersistThePrePurchaseBalanceAfterCommit()
    {
        var habbo = new Habbo
        {
            Id = UserId,
            Credits = 100,
            Duckets = 20,
            Diamonds = 20,
            HabboStats = new HabboStats(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, "", 0)
        };
        var (client, _) = HabbiconTestSupport.Client(habbo);
        var clients = new Plus.HabboHotel.GameClients.GameClientManager(null!, null!);
        clients.RegisterClient(client, habbo.Id, "habicon_tests");
        var sync = new Plus.Communication.RCON.Commands.User.SyncUserCurrencyCommand(new Plus.HabboHotel.Users.UserMaintenanceService(new Plus.HabboHotel.Users.UserMaintenanceStore(_database), new Plus.HabboHotel.Users.Authentication.AccountSessionGate(), clients));
        using var enteredPurchase = new ManualResetEventSlim();
        using var releasePurchase = new ManualResetEventSlim();
        using var startedSync = new ManualResetEventSlim();
        _database.BeforeConnection = () => { enteredPurchase.Set(); Assert.True(releasePurchase.Wait(TimeSpan.FromSeconds(5))); };
        var purchase = Task.Run(() => _service.Change(habbo, HabbiconAction.Buy, 61));
        Assert.True(enteredPurchase.Wait(TimeSpan.FromSeconds(5)));
        var syncing = Task.Run(async () => { startedSync.Set(); return await sync.TryExecute(new[] { UserId.ToString(), "credits" }); });

        try {
            Assert.True(startedSync.Wait(TimeSpan.FromSeconds(5)));
            Assert.False(syncing.Wait(TimeSpan.FromMilliseconds(100)));
        }
        finally {
            releasePurchase.Set();
        }

        await purchase;
        Assert.True(await syncing);
        _database.BeforeConnection = null;
        Assert.Equal(95, habbo.Credits);
        Assert.Equal(95, Scalar("SELECT credits FROM users WHERE id = 910001"));
        SaveWallet(habbo);
        Assert.False(await sync.TryExecute(new[] { UserId.ToString(), "credits" }));
        Assert.False(await sync.TryExecute(new[] { UserId.ToString(), "duckets" }));
        Assert.False(await sync.TryExecute(new[] { UserId.ToString(), "diamonds" }));
    }

    [HabbiconDatabaseFact]
    public void ClosedWalletTransfersTradedVoucherIntactInsteadOfDeletingItsValue()
    {
        Execute("CREATE TABLE IF NOT EXISTS items (id INT PRIMARY KEY, user_id INT NOT NULL, base_item INT NOT NULL, extra_data TEXT NOT NULL) ENGINE=InnoDB");
        Execute("DELETE FROM items WHERE id IN (910005,910006); INSERT INTO items (id,user_id,base_item,extra_data) VALUES (910005,910002,1,''),(910006,910002,1,'')");
        var habbo = new Habbo
        {
            Id = UserId,
            Credits = 100,
            HabboStats = new HabboStats(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, "", 0),
            Inventory = new Plus.HabboHotel.Users.Inventory.InventoryComponent
            {
                Furniture = new Plus.HabboHotel.Users.Inventory.Furniture.FurnitureInventoryComponent(Array.Empty<Plus.HabboHotel.Users.Inventory.Furniture.InventoryItem>(), Array.Empty<Plus.HabboHotel.Users.Inventory.Furniture.InventoryItem>())
            }
        };
        var (client, sent) = HabbiconTestSupport.Client(habbo);
        var voucher = new Plus.HabboHotel.Users.Inventory.Furniture.InventoryItem
        {
            Id = 910005,
            Definition = new Plus.HabboHotel.Items.ItemDefinition
            {
                InteractionType = Plus.HabboHotel.Items.InteractionType.Exchange,
                BehaviourData = 10,
                Type = Plus.HabboHotel.Users.Inventory.Furniture.ItemType.Floor
            }
        };
        var store = (Plus.HabboHotel.Rooms.ITradeStore)new Plus.HabboHotel.Rooms.RoomTradingComponent(_database, _clock, TestRoomSettings.Empty);
        // Live wallet redeems exactly once and consumes the voucher.
        Plus.HabboHotel.Rooms.Trading.Trade.ReceiveTradedItem(client, voucher, true, store);
        Assert.Equal(110, habbo.Credits);
        Assert.Equal(0, Scalar("SELECT COUNT(*) FROM items WHERE id = 910005"));
        Assert.Empty(habbo.Inventory.Furniture.AllItems);
        sent.Clear();
        SaveWallet(habbo);
        voucher.Id = 910006;
        Plus.HabboHotel.Rooms.Trading.Trade.ReceiveTradedItem(client, voucher, true, store);
        Assert.Equal(110, habbo.Credits);
        Assert.Same(voucher, habbo.Inventory.Furniture.GetItem(910006));
        Assert.Equal(UserId, Scalar("SELECT user_id FROM items WHERE id = 910006"));
        Assert.DoesNotContain(sent, packet => packet.Header == Plus.Communication.Packets.Outgoing.ServerPacketHeader.CreditBalanceComposer);
    }

    private void SaveWallet(Habbo habbo)
    {
        Execute("INSERT IGNORE INTO users_settings (user_id) VALUES (910001); INSERT IGNORE INTO user_statistics (id) VALUES (910001)");
        habbo.SessionStartedAt = DateTimeOffset.UtcNow;
        habbo.Persistence = new UserPersistenceService(_database, TimeProvider.System);
        habbo.Save();
    }

    private void Execute(string sql)
    {
        using var connection = _database.Connection();
        connection.Execute(sql);
    }
    private int Scalar(string sql)
    {
        using var connection = _database.Connection();

        return connection.QuerySingle<int>(sql);
    }

    internal sealed class TestDatabase(string connectionString) : IDatabase
    {
        public bool IsConnected() => true;
        public Action? BeforeConnection { get; set; }
        public IDbConnection Connection()
        {
            BeforeConnection?.Invoke();

            return new MySqlConnection(connectionString);
        }
    }

    private sealed class CountingTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public int Calls { get; private set; }
        public override DateTimeOffset GetUtcNow()
        {
            Calls++;

            return now;
        }

        public void ResetCalls() => Calls = 0;
    }
}
