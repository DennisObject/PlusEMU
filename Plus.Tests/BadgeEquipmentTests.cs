using System.Collections.Immutable;
using System.Data;
using System.Reflection;
using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using MySqlConnector;
using Plus.Communication.Packets.Incoming.Inventory.Badges;
using Plus.Communication.Packets.Incoming.Users;
using Plus.Communication.Packets.Outgoing.Users;
using Plus.Database;
using Plus.HabboHotel.Badges;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Quests;
using Plus.HabboHotel.Users;
using Plus.HabboHotel.Users.Badges;
using Plus.HabboHotel.Users.Inventory;
using Plus.HabboHotel.Users.Inventory.Badges;
using Xunit;

namespace Plus.Tests;

public sealed class BadgeEquipmentTests
{
    [Fact]
    public async Task HandlersDecodeAllFiveSlotsAndSelectedUserBeforeDelegation()
    {
        var badges = new RecordingEquipment();
        await new SetActivatedBadgesEvent(badges).Parse(null!, HabbiconTestSupport.Incoming(1, "a", 0, "", 3, "c", 4, "d", 6, "e"));
        Assert.Equal(new[] { new BadgeSlotSnapshot("a", 1), new("", 0), new("c", 3), new("d", 4), new("e", 6) }, badges.Requested);
        await new GetSelectedBadgesEvent(badges).Parse(null!, HabbiconTestSupport.Incoming(42));
        Assert.Equal(42, badges.UserId);
    }

    [Fact]
    public void EquippedBadgeWireFieldsRemainFrozenInSlotOrder()
    {
        var badge = new Badge("second", 2);
        var source = new List<Badge> { badge, new("first", 1) };
        var composer = new HabboUserBadgesComposer(42, BadgeInventorySnapshot.Capture(source).Equipped);
        var before = new HabbiconTestSupport.RecordingPacket();
        composer.Compose(before);
        Assert.Equal(new object[] { 42, 2, 1, "first", 2, "second" }, before.Writes);
        badge.Code = "changed";
        badge.Slot = 0;
        source.Clear();
        var after = new HabbiconTestSupport.RecordingPacket();
        composer.Compose(after);
        Assert.Equal(before.Writes, after.Writes);
    }

    [RoomComponentDatabaseFact]
    public async Task FailedBadgeUpdateRollsBackPersistenceAndLeavesMemoryRewardAndPacketsUntouched()
    {
        var connectionString = Environment.GetEnvironmentVariable("ROOM_COMPONENT_DATABASE")!;
        var schema = "task_refactor_tests_badges_" + Guid.NewGuid().ToString("N");
        using var server = new MySqlConnection(connectionString);
        server.Execute($"CREATE DATABASE `{schema}`");
        try
        {
            var database = new ProbeDatabase(new MySqlConnectionStringBuilder(connectionString) { Database = schema }.ConnectionString);
            using var connection = database.Connection();
            connection.Execute("CREATE TABLE user_badges(user_id INT,badge_id VARCHAR(100),badge_slot INT,PRIMARY KEY(user_id,badge_id)) ENGINE=InnoDB; INSERT INTO user_badges VALUES(42,'old',1),(42,'new',0)");
            connection.Execute("CREATE TRIGGER force_badge_failure BEFORE UPDATE ON user_badges FOR EACH ROW BEGIN IF NEW.badge_id='new' AND NEW.badge_slot=5 THEN SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='forced badge update failure'; END IF; END");
            var habbo = new Habbo { Id = 42, Inventory = new InventoryComponent { Badges = new(new() { ["old"] = new("old", 1), ["new"] = new("new", 0) }) } };
            var (client, packets) = HabbiconTestSupport.Client(habbo);
            var rewards = new Rewards();
            var manager = new BadgeManager(database, DispatchProxy.Create<IGameClientManager, UnusedClients>(), NullLogger<BadgeManager>.Instance);
            var service = new BadgeEquipmentService(manager, rewards);
            await Assert.ThrowsAsync<MySqlException>(() => service.Set(client, [new("new", 5)]));
            Assert.Equal(1, habbo.Inventory.Badges.GetBadge("old")!.Slot);
            Assert.Equal(0, habbo.Inventory.Badges.GetBadge("new")!.Slot);
            Assert.Equal(1, connection.ExecuteScalar<int>("SELECT badge_slot FROM user_badges WHERE badge_id='old'"));
            Assert.Equal(0, connection.ExecuteScalar<int>("SELECT badge_slot FROM user_badges WHERE badge_id='new'"));
            Assert.Equal(0, rewards.Progressed);
            Assert.Empty(packets);
            connection.Execute("DROP TRIGGER force_badge_failure");
            await service.Set(client, [new("new", 5), new("old", 9), new("unknown", 1)]);
            Assert.Equal(0, habbo.Inventory.Badges.GetBadge("old")!.Slot);
            Assert.Equal(5, habbo.Inventory.Badges.GetBadge("new")!.Slot);
            Assert.Equal(5, connection.ExecuteScalar<int>("SELECT badge_slot FROM user_badges WHERE badge_id='new'"));
            Assert.Equal(1, rewards.Progressed);
            Assert.Single(packets);
        }
        finally
        {
            server.Execute($"DROP DATABASE `{schema}`");
        }
    }

    private sealed class RecordingEquipment : IBadgeEquipmentService
    {
        public ImmutableArray<BadgeSlotSnapshot> Requested { get; private set; }
        public int UserId { get; private set; }
        public Task Set(GameClient session, ImmutableArray<BadgeSlotSnapshot> requested) { Requested = requested; return Task.CompletedTask; }
        public Task Show(GameClient session, int userId) { UserId = userId; return Task.CompletedTask; }
    }
    public class UnusedClients : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => throw new InvalidOperationException("Badge updates do not look up clients.");
    }
    private sealed class Rewards : IRewardTrackManager
    {
        public int Progressed { get; private set; }
        public void Progress(GameClient session, string actionType, int amount = 1) => Progressed += amount;
        public void SendTracks(GameClient session) => throw new NotSupportedException();
        public Task Claim(GameClient session, string trackId, string prizeId) => throw new NotSupportedException();
        public void PurchasePremium(GameClient session, string trackId) => throw new NotSupportedException();
    }
    private sealed class ProbeDatabase(string connectionString) : IDatabase
    {
        public IDbConnection Connection() => new MySqlConnection(connectionString);
        public bool IsConnected() => true;
    }
}
