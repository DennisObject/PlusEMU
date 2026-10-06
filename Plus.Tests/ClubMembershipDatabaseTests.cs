using System.Reflection;
using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using MySqlConnector;
using Plus.Core.Settings;
using Plus.HabboHotel.Catalog;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Permissions;
using Plus.HabboHotel.Subscriptions;
using Plus.HabboHotel.Users;
using Plus.HabboHotel.Users.Authentication;
using Plus.HabboHotel.Users.Inventory;
using Plus.HabboHotel.Users.Inventory.Furniture;
using Xunit;

namespace Plus.Tests;

public sealed class ClubDatabaseFactAttribute : FactAttribute
{
    public const string Variable = "PLUS_CLUB_TEST_CONNECTION_STRING";
    public ClubDatabaseFactAttribute()
    {
        if (Environment.GetEnvironmentVariable(Variable) == null) {
            Skip = $"Set {Variable} to a disposable task_acl_tests_ database with update 23.";
        }
    }
}
[CollectionDefinition("ClubDatabase", DisableParallelization = true)]
public sealed class ClubDatabaseCollection;

[Collection("ClubDatabase")]
public class ClubMembershipDatabaseTests : IDisposable
{
    private const int User = 957001, Offer = 957101;
    private readonly HabbiconDatabaseTests.TestDatabase _database;
    private readonly AccessControl _access;
    private readonly ClubMembershipService _memberships;
    private readonly ClubRewards _rewards;
    private readonly ClubMembershipTests.Clock _clock = new();
    private readonly Habbo _habbo;
    private readonly List<(uint Header, byte[] Body)> _sent;
    private readonly CatalogItem _gift;
    private readonly Clients _clients;

    public ClubMembershipDatabaseTests()
    {
        var connection = Environment.GetEnvironmentVariable(ClubDatabaseFactAttribute.Variable)!;

        if (!new MySqlConnectionStringBuilder(connection).Database.StartsWith("task_acl_tests_", StringComparison.Ordinal)) {
            throw new InvalidOperationException("Disposable database required.");
        }

        _database = new(connection);
        Clean();
        Sql("INSERT INTO users (id, username, auth_ticket, credits, activity_points, vip_points) VALUES (957001, 'club_test_member', '', 1000, 10, 10); " +
            "INSERT INTO catalog_club_offers (id, enabled, name, days, credits) VALUES (957101, 1, 'TEST_HC_MONTH', 31, 100)");
        _habbo = new Habbo
        {
            Id = User,
            Username = "club_test_member",
            Credits = 1000,
            Duckets = 10,
            Diamonds = 10,
            Inventory = new InventoryComponent { Furniture = new FurnitureInventoryComponent([], []) }
        };
        var (client, sent) = HabbiconTestSupport.Client(_habbo);
        _sent = sent;
        _habbo.Client = client;
        var clients = DispatchProxy.Create<IGameClientManager, Clients>();
        _clients = (Clients)clients;
        _clients.Client = client;
        _access = new(_database, clients, NullLogger<AccessControl>.Instance, _clock);
        _access.Init();
        _habbo.Access = _access.Resolve(User);
        _memberships = new(_database, _access, _clock);
        _gift = new CatalogItem
        {
            Id = 65398,
            CatalogName = "hc_arab_chair",
            Amount = 1,
            Definition = new ItemDefinition { Id = 65398, ItemName = "hc_arab_chair", SpriteId = 6341, Type = ItemType.Floor, InteractionType = InteractionType.None }
        };
        var page = new CatalogPage { Id = 8, Enabled = true, Layout = "club_gift" };
        page.Offers.Add(_gift.Id, _gift);
        var catalog = DispatchProxy.Create<ICatalogManager, Catalog>();
        ((Catalog)catalog).Pages = [page];
        var settings = DispatchProxy.Create<ISettingsManager, ClubMembershipTests.SettingProxy>();
        _rewards = new(_database, catalog, clients, settings, new AccountSessionGate(), _clock, _access);
    }
    private ClubOffer Month => new() { Id = Offer, Days = 31, Credits = 100 };

    [ClubDatabaseFact]
    public void KickbackCarriesFirstPurchaseDateAndStreakDaysAcrossReloadAndRenewal()
    {
        var first = _clock.Now;
        Assert.NotNull(_memberships.Purchase(_habbo, Month));
        Assert.Equal(first.UtcDateTime, ScalarTime("SELECT first_started_at FROM user_club_memberships WHERE user_id = 957001"));
        var initial = _rewards.Kickback(_habbo);
        Assert.Equal("04-10-2026", initial.FirstDate);
        Assert.Equal(0, initial.Streak);

        _clock.Now = _clock.Now.AddDays(12).AddHours(23);
        _habbo.Access = _access.Resolve(User);
        var continued = _rewards.Kickback(_habbo);
        Assert.Equal("04-10-2026", continued.FirstDate);
        Assert.Equal(12, continued.Streak);
        var packet = new HabbiconTestSupport.RecordingPacket();
        new Plus.Communication.Packets.Outgoing.Users.KickbackInfoComposer(continued).Compose(packet);
        Assert.Equal(12, packet.Writes[0]);
        Assert.Equal("04-10-2026", packet.Writes[1]);

        Assert.NotNull(_memberships.Purchase(_habbo, Month));
        Assert.Equal(12, _rewards.Kickback(_habbo).Streak);
        _clock.Now = _memberships.GetExpiry(User)!.Value;
        Assert.Equal(0, _rewards.Kickback(_habbo).Streak);
        Assert.Equal("04-10-2026", _rewards.Kickback(_habbo).FirstDate);
        _clock.Now = _clock.Now.AddDays(7);
        Assert.NotNull(_memberships.Purchase(_habbo, Month));
        _clock.Now = _clock.Now.AddDays(3);
        Assert.Equal(3, _rewards.Kickback(_habbo).Streak);
        Assert.Equal("04-10-2026", _rewards.Kickback(_habbo).FirstDate);
        Assert.Equal(first, _habbo.Access.Membership.FirstStartedAt);
    }

    [ClubDatabaseFact]
    public void ComplimentaryAccessHasNoPurchasedTenureGiftsOrPayday()
    {
        Sql("INSERT INTO user_permissions (user_id, permission_key, effect) VALUES (957001, 'club.access', 'grant')");
        _access.Refresh(User);
        Assert.Equal(2, ClubAccess.LevelFor(_habbo.Access));
        Assert.Equal(0, _rewards.Gifts(_habbo).Available);
        Assert.Null(_rewards.Claim(_habbo, "hc_arab_chair"));
        Assert.Equal(0, _rewards.Kickback(_habbo).Streak);
        Assert.Equal("", _rewards.Kickback(_habbo).FirstDate);
        Assert.True(_rewards.Charge(_habbo, 99));
        _clock.Now = new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
        _rewards.RunPaydays();
        Assert.Equal(0, Scalar("SELECT COUNT(*) FROM club_credit_spending WHERE user_id = 957001"));
        Assert.Equal(0, Scalar("SELECT COUNT(*) FROM club_paydays WHERE user_id = 957001"));
        Assert.Equal(901, _habbo.Credits);
    }
    [ClubDatabaseFact]
    public void PurchaseLoadsTheSnapshotAndInsufficientFundsCommitNothing()
    {
        var now = _clock.Now;
        Assert.Equal(now.AddDays(31), _memberships.Purchase(_habbo, Month));
        Assert.Equal(900, _habbo.Credits);
        Assert.Equal(2, ClubAccess.LevelFor(_habbo.Access));
        Sql("UPDATE catalog_club_offers SET credits = 10000 WHERE id = 957101");
        Assert.Null(_memberships.Purchase(_habbo, Month));
        Assert.Equal(900, Scalar("SELECT credits FROM users WHERE id = 957001"));
        Assert.Equal(now.AddDays(31).UtcDateTime, ScalarTime("SELECT expires_at FROM user_club_memberships WHERE user_id = 957001"));
        Assert.Equal(1, Scalar("SELECT COUNT(*) FROM acl_audit_log WHERE action = 'club.purchase' AND target_id = 957001"));
    }
    [ClubDatabaseFact]
    public async Task PurchasingAndExpiringMembershipResendBothLists()
    {
        using var lists = new ClientAccessLists(_access, ClientAccessListTests.Styles(), ClientAccessListTests.Models());
        await lists.Start();
        Assert.NotNull(_memberships.Purchase(_habbo, Month));
        AssertLists(2, 3);
        _clock.Now = _habbo.Access.Membership.ExpiresAt!.Value;
        _clock.Tick();
        AssertLists(1, 1);

        void AssertLists(int styleCount, int modelCount)
        {
            Assert.Equal(new[] {
                Plus.Communication.Packets.Outgoing.ServerPacketHeader.UserRightsComposer,
                Plus.Communication.Packets.Outgoing.ServerPacketHeader.AllowedChatStylesComposer,
                Plus.Communication.Packets.Outgoing.ServerPacketHeader.CreatableRoomModelsComposer
            }, _sent.Select(packet => packet.Header));
            Assert.Equal(styleCount, System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(_sent[1].Body));
            Assert.Equal(modelCount, System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(_sent[2].Body));
            _sent.Clear();
        }
    }

    [ClubDatabaseFact]
    public async Task PermissionRefreshAndWalletWriteAcquireAccountIndexesInTheSameOrder()
    {
        using var account = (MySqlConnection)_database.Connection();
        await account.OpenAsync();
        using var transaction = await account.BeginTransactionAsync();
        await account.ExecuteAsync("SELECT id FROM users FORCE INDEX(PRIMARY) WHERE id=@userId FOR UPDATE",
            new { userId = User }, transaction);
        var connectionRequests = 0;
        _database.BeforeConnection = () => Interlocked.Increment(ref connectionRequests);
        var workerEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var refresh = Task.Run(() => { workerEntered.SetResult(); _access.Refresh(User); });

        try {
            await workerEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            using var observer = new MySqlConnection(account.ConnectionString);
            var elapsed = System.Diagnostics.Stopwatch.StartNew();
            AccountLockWait? waiting = null;

            while (elapsed.Elapsed < TimeSpan.FromSeconds(5)) {
                waiting = await observer.QuerySingleOrDefaultAsync<AccountLockWait>("""
                    SELECT requesting.trx_query AS WaitingQuery FROM information_schema.INNODB_LOCK_WAITS w
                    JOIN information_schema.INNODB_TRX requesting ON requesting.trx_id=w.requesting_trx_id
                    JOIN information_schema.INNODB_TRX blocking ON blocking.trx_id=w.blocking_trx_id
                    WHERE blocking.trx_mysql_thread_id=@blocker LIMIT 1
                    """, new { blocker = account.ServerThread });

                if (waiting != null) {
                    break;
                }

                // MariaDB refreshes this shared metadata cache only after 100 ms without a read.
                await Task.Delay(200);
            }

            Assert.True(waiting != null, $"Permission refresh must reach its account row lock before the wallet write. Worker entered; connection requests={connectionRequests}; MaximumPoolSize={new MySqlConnectionStringBuilder(account.ConnectionString).MaximumPoolSize}.");
            Assert.NotNull(waiting!.WaitingQuery);
            Assert.StartsWith("UPDATE users FORCE INDEX(PRIMARY)", waiting.WaitingQuery, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("`rank`", waiting.WaitingQuery);
            Assert.Contains(User.ToString(System.Globalization.CultureInfo.InvariantCulture), waiting.WaitingQuery);
            // With the old secondary-index-first refresh, this write deadlocks while holding PRIMARY.
            await account.ExecuteAsync("UPDATE users SET credits=999 WHERE id=@userId",
                new { userId = User }, transaction);
            await transaction.CommitAsync();
            await refresh.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(999, Scalar("SELECT credits FROM users WHERE id=957001"));
        }
        finally {
            _database.BeforeConnection = null;

            if (transaction.Connection != null) {
                await transaction.RollbackAsync();
            }

            await refresh.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [ClubDatabaseFact]
    public async Task ConcurrentPurchasesSerializeWalletAndExtendWithoutLostTime()
    {
        var results = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => Task.Run(() => _memberships.Purchase(_habbo, Month))));
        Assert.All(results, result => Assert.NotNull(result));
        Assert.Equal(0, _habbo.Credits);
        Assert.Equal(0, Scalar("SELECT credits FROM users WHERE id = 957001"));
        Assert.Equal(_clock.Now.AddDays(310), _memberships.GetExpiry(User));
        Assert.Null(_memberships.Purchase(_habbo, Month));
    }
    [ClubDatabaseFact]
    public void CatalogOfferMetadataComesFromTheServerAndDisabledOffersAreRefused()
    {
        var forged = new ClubOffer { Id = Offer, Days = 186, Credits = 0 };
        Assert.Equal(_clock.Now.AddDays(31), _memberships.Purchase(_habbo, forged));
        Assert.Equal(900, _habbo.Credits);
        Sql("UPDATE catalog_club_offers SET enabled = 0 WHERE id = 957101");
        Assert.Null(_memberships.Purchase(_habbo, Month));
        Assert.Null(_memberships.Purchase(_habbo, new ClubOffer { Id = 999999, Days = 31 }));
        Assert.Null(_memberships.Purchase(_habbo, Month, User)); // offer is not giftable
    }
    [ClubDatabaseFact]
    public async Task GiftClaimsAreAtomicAndConcurrentDoubleClaimsGiveOneItem()
    {
        _memberships.Purchase(_habbo, Month);
        _clock.Now = _clock.Now.AddSeconds(1);
        Assert.Equal(1, _rewards.Gifts(_habbo).Available);
        Sql("CREATE TRIGGER club_test_gift_failure BEFORE INSERT ON items FOR EACH ROW BEGIN IF NEW.user_id = 957001 THEN SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'test gift rollback'; END IF; END");

        try {
            Assert.Throws<MySqlException>(() => _rewards.Claim(_habbo, "hc_arab_chair"));
        }
        finally {
            Sql("DROP TRIGGER club_test_gift_failure");
        }

        Assert.Equal(0, Scalar("SELECT COUNT(*) FROM club_gift_claims WHERE user_id = 957001"));
        Assert.Equal(0, Scalar("SELECT gifts_claimed FROM user_club_memberships WHERE user_id = 957001"));
        Assert.Equal(0, Scalar("SELECT COUNT(*) FROM items WHERE user_id = 957001"));
        var claims = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => Task.Run(() => _rewards.Claim(_habbo, "hc_arab_chair"))));
        Assert.Single(claims, claim => claim != null);
        Assert.Equal(1, Scalar("SELECT COUNT(*) FROM items WHERE user_id = 957001"));
        Assert.Equal(1, Scalar("SELECT COUNT(*) FROM club_gift_claims WHERE user_id = 957001"));
        Assert.Equal(0, _rewards.Gifts(_habbo).Available);
    }
    [ClubDatabaseFact]
    public void GiftSpoofingTenureRequirementsAndExactExpiryAreEnforced()
    {
        Assert.Null(_rewards.Claim(_habbo, "hc_arab_chair"));
        _memberships.Purchase(_habbo, Month);
        _clock.Now = _clock.Now.AddSeconds(1);
        Assert.Null(_rewards.Claim(_habbo, "not_a_gift"));
        Sql("UPDATE club_gift_offers SET days_required = 100 WHERE catalog_item_id = 65398");

        try {
            Assert.Null(_rewards.Claim(_habbo, "hc_arab_chair"));
        }
        finally {
            Sql("UPDATE club_gift_offers SET days_required = 0 WHERE catalog_item_id = 65398");
        }

        _gift.ClubLevel = 3;
        Assert.Null(_rewards.Claim(_habbo, "hc_arab_chair"));
        _gift.ClubLevel = 0;
        _clock.Now = _memberships.GetExpiry(User)!.Value;
        Assert.Equal(0, ClubAccess.LevelFor(_habbo.Access));
        Assert.Null(_rewards.Claim(_habbo, "hc_arab_chair"));
        Assert.Equal(0, Scalar("SELECT COUNT(*) FROM items WHERE user_id = 957001"));
    }
    [ClubDatabaseFact]
    public void RedeemableCreditFurnitureDoesNotEarnKickback()
    {
        _memberships.Purchase(_habbo, Month);
        Assert.True(_rewards.Charge(_habbo, 99, kickbackEligible: ClubRewards.EligibleCatalogPurchase("CF_100")));
        Assert.True(_rewards.Charge(_habbo, 99, kickbackEligible: ClubRewards.EligibleCatalogPurchase("CFC_100")));
        Assert.Equal(702, _habbo.Credits);
        Assert.Equal(0, _rewards.Kickback(_habbo).Spent);
        Assert.Equal(0, Scalar("SELECT COUNT(*) FROM club_credit_spending WHERE user_id = 957001"));
    }
    [ClubDatabaseFact]
    public void DeliveryRefusalOrFailureRollsBackWalletSpendingAndProducts()
    {
        _memberships.Purchase(_habbo, Month);
        bool Refuse(System.Data.IDbConnection connection, System.Data.IDbTransaction transaction)
        {
            connection.Execute("INSERT INTO items (user_id, base_item, extra_data) VALUES (957001, 65398, '')", transaction: transaction);

            return false;
        }
        Assert.False(_rewards.Charge(_habbo, 99, deliver: Refuse));
        Assert.Throws<InvalidOperationException>(() => _rewards.Charge(_habbo, 99, deliver: (connection, transaction) =>
        { Refuse(connection, transaction); throw new InvalidOperationException("delivery failed"); }));
        Assert.Equal(900, _habbo.Credits);
        Assert.Equal(900, Scalar("SELECT credits FROM users WHERE id = 957001"));
        Assert.Equal(0, Scalar("SELECT COUNT(*) FROM items WHERE user_id = 957001"));
        Assert.Equal(0, Scalar("SELECT COUNT(*) FROM club_credit_spending WHERE user_id = 957001"));
    }
    [ClubDatabaseFact]
    public void FailedDeliveryReturnsReservedLimitedStock()
    {
        Sql("UPDATE catalog_items SET limited_stack = 1, limited_sells = 0 WHERE id = 65398");

        try {
            Assert.False(_rewards.Charge(_habbo, 99, deliver: (connection, transaction) =>
            { Assert.Equal(1, CatalogLimitedStock.Reserve(connection, transaction, 65398)); return false; }));
            Assert.Equal(0, Scalar("SELECT limited_sells FROM catalog_items WHERE id = 65398"));
            Assert.Equal(1000, _habbo.Credits);
        }
        finally {
            Sql("UPDATE catalog_items SET limited_stack = 0, limited_sells = 0 WHERE id = 65398");
        }
    }
    [ClubDatabaseFact]
    public async Task PaydaysPersistSpendingAndReplayCannotPayTwice()
    {
        _memberships.Purchase(_habbo, Month);
        Assert.True(_rewards.Charge(_habbo, 99));
        Assert.Equal(99, _rewards.Kickback(_habbo).Spent);
        _clock.Now = new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
        await Task.WhenAll(Task.Run(_rewards.RunPaydays), Task.Run(_rewards.RunPaydays));
        Assert.Equal(815, _habbo.Credits); // 801 + 5 (28-day streak) + floor(99 * .1)
        Assert.Equal(815, Scalar("SELECT credits FROM users WHERE id = 957001"));
        Assert.Equal(1, Scalar("SELECT COUNT(*) FROM club_paydays WHERE user_id = 957001"));
        Assert.Equal(14, _rewards.Kickback(_habbo).Rewarded);
    }
    [ClubDatabaseFact]
    public void ExpiredAtPaydayRecordsMissedCreditsAndOfflinePayoutsPersist()
    {
        _memberships.Purchase(_habbo, Month);
        Assert.True(_rewards.Charge(_habbo, 99));
        var due = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
        Sql($"UPDATE club_membership_intervals SET expires_at = '{due.UtcDateTime:yyyy-MM-dd HH:mm:ss}' WHERE user_id = 957001");
        _clock.Now = due;
        _clients.Registered = false;
        _rewards.RunPaydays();
        Assert.Equal(801, Scalar("SELECT credits FROM users WHERE id = 957001"));
        Assert.Equal(9, _rewards.Kickback(_habbo).Missed);
    }
    private sealed class AccountLockWait
    {
        public string? WaitingQuery { get; set; }
    }
    private void Sql(string sql)
    {
        using var connection = _database.Connection();
        connection.Execute(sql);
    }
    private long Scalar(string sql)
    {
        using var connection = _database.Connection();

        return connection.ExecuteScalar<long>(sql);
    }
    [ClubDatabaseFact]
    public void FractionalGiftClaimKeepsItsMicrosecondsThroughTheRealService()
    {
        var purchased = new DateTimeOffset(2026, 10, 4, 10, 0, 0, TimeSpan.Zero).AddTicks(2_000_000);
        _clock.Now = purchased;
        _memberships.Purchase(_habbo, Month);
        // Just before expiry, after a full month of tenure, the first gift is earned and the membership is still active.
        var claimed = purchased.AddDays(31).AddSeconds(-1).AddTicks(3_450);
        _clock.Now = claimed;
        _access.Refresh(User);
        _habbo.Access = _access.Resolve(User);
        Assert.Equal(1, _rewards.Gifts(_habbo).Available);
        Assert.NotNull(_rewards.Claim(_habbo, "hc_arab_chair"));
        using var connection = _database.Connection();
        Assert.Equal(claimed, connection.ExecuteScalar<DateTimeOffset?>("SELECT claimed_at FROM club_gift_claims WHERE user_id = 957001"));
    }

    [ClubDatabaseFact]
    public void RunPaydaysRoundTripsTheMonthlyKeyAsUtcDatetime6()
    {
        _memberships.Purchase(_habbo, Month);
        Assert.True(_rewards.Charge(_habbo, 99));
        _clock.Now = new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
        _rewards.RunPaydays();
        using var connection = _database.Connection();
        var payday = connection.ExecuteScalar<DateTimeOffset?>("SELECT payday FROM club_paydays WHERE user_id = 957001");
        Assert.Equal(new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero), payday);
        Assert.Equal(TimeSpan.Zero, payday!.Value.Offset);
        Assert.Equal("datetime(6)", connection.ExecuteScalar<string>("SELECT COLUMN_TYPE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'club_paydays' AND COLUMN_NAME = 'payday'"));
    }

    [ClubDatabaseFact]
    public void FractionalPurchaseAndSpendingKeepMicrosecondsThroughStorageAndAccessResolution()
    {
        var instant = new DateTimeOffset(2026, 10, 4, 10, 0, 0, TimeSpan.Zero).AddTicks(1_234_560);
        _clock.Now = instant;
        var expiry = _memberships.Purchase(_habbo, Month);
        Assert.Equal(instant.AddDays(31), expiry);
        Assert.Equal(instant.AddDays(31), _memberships.GetExpiry(User));

        _access.Refresh(User);
        _habbo.Access = _access.Resolve(User);
        Assert.Equal(instant.AddDays(31), _habbo.Access.Membership.ExpiresAt);
        Assert.Equal(instant, _habbo.Access.Membership.FirstStartedAt);

        var spent = instant.AddSeconds(5).AddTicks(670);
        _clock.Now = spent;
        Assert.True(_rewards.Charge(_habbo, 99));
        using var connection = _database.Connection();
        Assert.Equal(spent, connection.ExecuteScalar<DateTimeOffset?>("SELECT spent_at FROM club_credit_spending WHERE user_id = 957001 ORDER BY id DESC LIMIT 1"));
    }

    [ClubDatabaseFact]
    public void KickbackUsesTheUtcMonthForANonUtcCapturedInstant()
    {
        // 02:00 on 1 November at +05:00 is 21:00 UTC on 31 October; the spend below belongs to October.
        _clock.Now = new DateTimeOffset(2026, 10, 4, 10, 0, 0, TimeSpan.Zero);
        Assert.NotNull(_memberships.Purchase(_habbo, Month));
        _access.Refresh(User);
        _habbo.Access = _access.Resolve(User);

        _clock.Now = new DateTimeOffset(2026, 10, 31, 22, 0, 0, TimeSpan.Zero);
        Assert.True(_rewards.Charge(_habbo, 40));
        _clock.Now = new DateTimeOffset(2026, 11, 1, 2, 0, 0, TimeSpan.FromHours(5));
        Assert.Equal(40, _rewards.Kickback(_habbo).Spent);
    }

    private DateTime? ScalarTime(string sql)
    {
        using var connection = _database.Connection();

        return connection.ExecuteScalar<DateTime?>(sql);
    }
    private void Clean() => Sql("DROP TRIGGER IF EXISTS club_test_gift_failure; DELETE FROM user_club_memberships WHERE user_id = 957001; DELETE FROM club_membership_intervals WHERE user_id = 957001; " +
        "DELETE FROM club_credit_spending WHERE user_id = 957001; DELETE FROM club_paydays WHERE user_id = 957001; DELETE FROM club_gift_claims WHERE user_id = 957001; " +
        "DELETE FROM items WHERE user_id = 957001; DELETE FROM acl_audit_log WHERE target_id = 957001; DELETE FROM user_permissions WHERE user_id = 957001; DELETE FROM users WHERE id = 957001; DELETE FROM catalog_club_offers WHERE id = 957101");
    public void Dispose()
    {
        _access.Dispose();
        Clean();
    }
    public class Clients : DispatchProxy
    {
        public GameClient Client { get; set; } = null!;
        public bool Registered { get; set; } = true;
        protected override object? Invoke(MethodInfo? method, object?[]? args) => method!.Name switch
        {
            "get_GetClients" => Registered ? new List<GameClient> { Client } : new List<GameClient>(),
            "GetClientByUserId" => Registered && (int)args![0]! == Client.GetHabbo().Id ? Client : null,
            _ => throw new InvalidOperationException(method.Name)
        };
    }
    public class Catalog : DispatchProxy
    {
        public ICollection<CatalogPage> Pages { get; set; } = [];
        protected override object? Invoke(MethodInfo? method, object?[]? args) => method!.Name == "get_Pages" ? Pages : throw new InvalidOperationException(method.Name);
    }
}
