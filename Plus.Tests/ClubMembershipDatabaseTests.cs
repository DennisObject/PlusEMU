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
        if (Environment.GetEnvironmentVariable(Variable) == null) Skip = $"Set {Variable} to a disposable task_acl_tests_ database with update 23.";
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
    private readonly CatalogItem _gift;
    private readonly Clients _clients;

    public ClubMembershipDatabaseTests()
    {
        var connection = Environment.GetEnvironmentVariable(ClubDatabaseFactAttribute.Variable)!;
        if (!new MySqlConnectionStringBuilder(connection).Database.StartsWith("task_acl_tests_", StringComparison.Ordinal)) throw new InvalidOperationException("Disposable database required.");
        _database = new(connection);
        Clean();
        Sql("INSERT INTO users (id, username, auth_ticket, credits, activity_points, vip_points) VALUES (957001, 'club_test_member', '', 1000, 10, 10); " +
            "INSERT INTO catalog_club_offers (id, enabled, name, days, credits) VALUES (957101, 1, 'TEST_HC_MONTH', 31, 100)");
        _habbo = new Habbo { Id = User, Username = "club_test_member", Credits = 1000, Duckets = 10, Diamonds = 10,
            Inventory = new InventoryComponent { Furniture = new FurnitureInventoryComponent([], []) } };
        var (client, _) = HabbiconTestSupport.Client(_habbo);
        _habbo.Client = client;
        var clients = DispatchProxy.Create<IGameClientManager, Clients>();
        _clients = (Clients)clients; _clients.Client = client;
        _access = new(_database, clients, NullLogger<AccessControl>.Instance, _clock);
        _access.Init(); _habbo.Access = _access.Resolve(User);
        _memberships = new(_database, _access, _clock);
        _gift = new CatalogItem { Id = 65398, CatalogName = "hc_arab_chair", Amount = 1,
            Definition = new ItemDefinition { Id = 65398, ItemName = "hc_arab_chair", SpriteId = 6341, Type = ItemType.Floor, InteractionType = InteractionType.None } };
        var page = new CatalogPage { Id = 8, Enabled = true, Layout = "club_gift" }; page.Offers.Add(_gift.Id, _gift);
        var catalog = DispatchProxy.Create<ICatalogManager, Catalog>(); ((Catalog)catalog).Pages = [page];
        var settings = DispatchProxy.Create<ISettingsManager, ClubMembershipTests.SettingProxy>();
        _rewards = new(_database, catalog, clients, settings, new AccountSessionGate(), _clock, _access);
    }
    private ClubOffer Month => new() { Id = Offer, Days = 31, Credits = 100 };

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
        var now = _clock.Now.ToUnixTimeSeconds();
        Assert.Equal(now + 31 * ClubMembership.Day, _memberships.Purchase(_habbo, Month));
        Assert.Equal(900, _habbo.Credits); Assert.Equal(2, ClubAccess.LevelFor(_habbo.Access));
        Sql("UPDATE catalog_club_offers SET credits = 10000 WHERE id = 957101");
        Assert.Null(_memberships.Purchase(_habbo, Month));
        Assert.Equal(900, Scalar("SELECT credits FROM users WHERE id = 957001"));
        Assert.Equal(now + 31 * ClubMembership.Day, Scalar("SELECT expires_at FROM user_club_memberships WHERE user_id = 957001"));
        Assert.Equal(1, Scalar("SELECT COUNT(*) FROM acl_audit_log WHERE action = 'club.purchase' AND target_id = 957001"));
    }
    [ClubDatabaseFact]
    public async Task ConcurrentPurchasesSerializeWalletAndExtendWithoutLostTime()
    {
        var results = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => Task.Run(() => _memberships.Purchase(_habbo, Month))));
        Assert.All(results, result => Assert.NotNull(result));
        Assert.Equal(0, _habbo.Credits); Assert.Equal(0, Scalar("SELECT credits FROM users WHERE id = 957001"));
        Assert.Equal(_clock.Now.ToUnixTimeSeconds() + 310 * ClubMembership.Day, _memberships.GetExpiry(User));
        Assert.Null(_memberships.Purchase(_habbo, Month));
    }
    [ClubDatabaseFact]
    public void CatalogOfferMetadataComesFromTheServerAndDisabledOffersAreRefused()
    {
        var forged = new ClubOffer { Id = Offer, Days = 186, Credits = 0 };
        Assert.Equal(_clock.Now.ToUnixTimeSeconds() + 31 * ClubMembership.Day, _memberships.Purchase(_habbo, forged));
        Assert.Equal(900, _habbo.Credits);
        Sql("UPDATE catalog_club_offers SET enabled = 0 WHERE id = 957101");
        Assert.Null(_memberships.Purchase(_habbo, Month));
        Assert.Null(_memberships.Purchase(_habbo, new ClubOffer { Id = 999999, Days = 31 }));
        Assert.Null(_memberships.Purchase(_habbo, Month, User)); // offer is not giftable
    }
    [ClubDatabaseFact]
    public async Task GiftClaimsAreAtomicAndConcurrentDoubleClaimsGiveOneItem()
    {
        _memberships.Purchase(_habbo, Month); _clock.Now = _clock.Now.AddSeconds(1);
        Assert.Equal(1, _rewards.Gifts(_habbo).Available);
        Sql("CREATE TRIGGER club_test_gift_failure BEFORE INSERT ON items FOR EACH ROW BEGIN IF NEW.user_id = 957001 THEN SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'test gift rollback'; END IF; END");
        try { Assert.Throws<MySqlException>(() => _rewards.Claim(_habbo, "hc_arab_chair")); }
        finally { Sql("DROP TRIGGER club_test_gift_failure"); }
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
        _memberships.Purchase(_habbo, Month); _clock.Now = _clock.Now.AddSeconds(1);
        Assert.Null(_rewards.Claim(_habbo, "not_a_gift"));
        Sql("UPDATE club_gift_offers SET days_required = 100 WHERE catalog_item_id = 65398");
        try { Assert.Null(_rewards.Claim(_habbo, "hc_arab_chair")); }
        finally { Sql("UPDATE club_gift_offers SET days_required = 0 WHERE catalog_item_id = 65398"); }
        _gift.ClubLevel = 3; Assert.Null(_rewards.Claim(_habbo, "hc_arab_chair")); _gift.ClubLevel = 0;
        _clock.Now = DateTimeOffset.FromUnixTimeSeconds(_memberships.GetExpiry(User));
        Assert.Equal(0, ClubAccess.LevelFor(_habbo.Access));
        Assert.Null(_rewards.Claim(_habbo, "hc_arab_chair"));
        Assert.Equal(0, Scalar("SELECT COUNT(*) FROM items WHERE user_id = 957001"));
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
        try
        {
            Assert.False(_rewards.Charge(_habbo, 99, deliver: (connection, transaction) =>
            { Assert.Equal(1, CatalogLimitedStock.Reserve(connection, transaction, 65398)); return false; }));
            Assert.Equal(0, Scalar("SELECT limited_sells FROM catalog_items WHERE id = 65398"));
            Assert.Equal(1000, _habbo.Credits);
        }
        finally { Sql("UPDATE catalog_items SET limited_stack = 0, limited_sells = 0 WHERE id = 65398"); }
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
        _memberships.Purchase(_habbo, Month); Assert.True(_rewards.Charge(_habbo, 99));
        var due = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
        Sql($"UPDATE club_membership_intervals SET expires_at = {due.ToUnixTimeSeconds()} WHERE user_id = 957001");
        _clock.Now = due; _clients.Registered = false;
        _rewards.RunPaydays();
        Assert.Equal(801, Scalar("SELECT credits FROM users WHERE id = 957001"));
        Assert.Equal(9, _rewards.Kickback(_habbo).Missed);
    }
    private void Sql(string sql) { using var connection = _database.Connection(); connection.Execute(sql); }
    private long Scalar(string sql) { using var connection = _database.Connection(); return connection.ExecuteScalar<long>(sql); }
    private void Clean() => Sql("DROP TRIGGER IF EXISTS club_test_gift_failure; DELETE FROM user_club_memberships WHERE user_id = 957001; DELETE FROM club_membership_intervals WHERE user_id = 957001; " +
        "DELETE FROM club_credit_spending WHERE user_id = 957001; DELETE FROM club_paydays WHERE user_id = 957001; DELETE FROM club_gift_claims WHERE user_id = 957001; " +
        "DELETE FROM items WHERE user_id = 957001; DELETE FROM acl_audit_log WHERE target_id = 957001; DELETE FROM user_permissions WHERE user_id = 957001; DELETE FROM users WHERE id = 957001; DELETE FROM catalog_club_offers WHERE id = 957101");
    public void Dispose() { _access.Dispose(); Clean(); }
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
