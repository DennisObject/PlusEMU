using System.Text.RegularExpressions;
using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using MySqlConnector;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Achievements;
using Plus.HabboHotel.Badges;
using Plus.HabboHotel.Users;
using Plus.HabboHotel.Users.Inventory;
using Plus.Communication.Packets.Outgoing;
using Plus.HabboHotel.Talents;
using Plus.HabboHotel.Users.Inventory.Furniture;
using Xunit;

namespace Plus.Tests;

public sealed class TalentTrackDatabaseFactAttribute : FactAttribute
{
    public TalentTrackDatabaseFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("TALENT_TRACK_DATABASE") == null) {
            Skip = "Opt-in isolated talent track MariaDB probe.";
        }
    }
}

public sealed class TalentTrackDatabaseTests
{
    [TalentTrackDatabaseFact]
    public async Task NativeDefinitionsKeepCategoriesSeparateAndRewardsAreAtomicAndIdempotent()
    {
        var options = new MySqlConnectionStringBuilder(Environment.GetEnvironmentVariable("TALENT_TRACK_DATABASE")!) {
            Pooling = false, AllowZeroDateTime = true, ConvertZeroDateTime = true, AllowUserVariables = true
        };
        using var connection = new MySqlConnection(options.ConnectionString);
        connection.Open();
        var schema = "task_talents_" + Guid.NewGuid().ToString("N");
        connection.Execute($"CREATE DATABASE `{schema}`");
        try {
            connection.Execute($"USE `{schema}`");
            var pristine = File.ReadAllText(HabbiconPacketTests.Repo("Resources/SQLs/Original Database.sql"));
            foreach (var table in new[] { "users", "items", "talents", "talents_sub_levels", "user_achievements" }) {
                var ddl = Regex.Match(pristine, $@"CREATE TABLE `{table}` \([\s\S]*?\) ENGINE=[^;]+;").Value;
                Assert.NotEmpty(ddl);
                connection.Execute(ddl);
            }
            connection.Execute(File.ReadAllText(HabbiconPacketTests.Repo("Database/Migrations/47_TalentTrackRewards.sql")));
            connection.Execute(File.ReadAllText(HabbiconPacketTests.Repo("Database/Migrations/47_TalentTrackRewards.sql")));
            connection.Execute("INSERT INTO users(id,username,auth_ticket) VALUES(7,'talent','ticket'); " +
                "INSERT INTO talents(type,level,data_actions,data_gifts) VALUES('citizenship',0,'TRADE','gift'),('helper',0,'HELP',''); " +
                "INSERT INTO talents_sub_levels(talent_type,talent_level,sub_level,badge_code,required_progress) VALUES('citizenship',0,1,'ACH_A1',5),('helper',0,1,'ACH_A2',10)");
            var database = new HabbiconDatabaseTests.TestDatabase(new MySqlConnectionStringBuilder(options.ConnectionString) { Database = schema, AllowUserVariables = false }.ConnectionString);
            var definitions = new TalentTrackManager(NullLogger<TalentTrackManager>.Instance, database);
            await definitions.Start();
            Assert.Equal("ACH_A1", Assert.Single(Assert.Single(definitions.GetLevels(), level => level.Type == "citizenship").GetSubLevels()).Badge);
            Assert.Equal("ACH_A2", Assert.Single(Assert.Single(definitions.GetLevels(), level => level.Type == "helper").GetSubLevels()).Badge);
            var store = new TalentTrackRewardStore(database);
            var gifts = new[] { new ItemDefinition { Id = 50, ItemName = "gift", Type = ItemType.Floor } };
            connection.Execute("CREATE TRIGGER fail_talent_item BEFORE INSERT ON items FOR EACH ROW SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='forced talent item failure'");
            Assert.Throws<MySqlException>(() => store.Claim(7, "citizenship", 0, gifts));
            Assert.Equal(0, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM user_talent_rewards"));
            Assert.Equal(0, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM items"));
            connection.Execute("DROP TRIGGER fail_talent_item");
            var claims = await Task.WhenAll(Task.Run(() => store.Claim(7, "citizenship", 0, gifts)), Task.Run(() => store.Claim(7, "citizenship", 0, gifts)));
            var awarded = Assert.Single(claims, claim => claim != null);
            var item = Assert.Single(awarded!);
            Assert.True(item.Id > 0);
            Assert.Equal(7u, item.OwnerId);
            Assert.Equal((7, 50, 0), connection.QuerySingle<(int, int, int)>("SELECT user_id,base_item,room_id FROM items"));
            Assert.Null(new TalentTrackRewardStore(database).Claim(7, "citizenship", 0, gifts));
            Assert.Single(store.Claim(7, "helper", 0, gifts)!);
            Assert.Equal(2, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM user_talent_rewards"));
            Assert.Equal(2, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM items"));
            // Drive the actual achievement-to-talent boundary with an independently eligible owner.
            connection.Execute("INSERT INTO users(id,username,auth_ticket) VALUES(8,'achiever','ticket2')");
            var habbo = new Habbo {
                Id = 8,
                HabboStats = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, "old", 0),
                Inventory = new InventoryComponent { Furniture = new([], []) }
            };
            var (client, packets) = HabbiconTestSupport.Client(habbo);
            habbo.Client = client;
            var itemDefinitions = CatalogSnapshotTestSupport.Proxy<IItemDataManager>((method, _) =>
                method == "GetItemByName" ? gifts[0] : throw new InvalidOperationException(method));
            var progression = new TalentTrackProgressionService(definitions, itemDefinitions, store, NullLogger<TalentTrackProgressionService>.Instance);
            var badges = CatalogSnapshotTestSupport.Proxy<IBadgeManager>((method, _) =>
                method == "GiveBadge" ? Task.CompletedTask : throw new InvalidOperationException(method));
            var achievements = new AchievementManager(null!, database, badges, progression);
            achievements.Achievements.Add("ACH_A", TalentTrackPresentationTests.Achievements()["ACH_A"]);
            Assert.True(achievements.ProgressAchievement(client, "ACH_A", 5));
            Assert.Equal(1, habbo.GetAchievementData("ACH_A")!.Level);
            Assert.Single(habbo.Inventory.Furniture.AllItems);
            Assert.Single(packets, packet => packet.Header == ServerPacketHeader.TalentLevelUpComposer);
            Assert.Equal(1, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM user_talent_rewards WHERE user_id=8 AND type='citizenship' AND level=0"));
            progression.Progress(habbo, achievements.Achievements);
            Assert.Single(habbo.Inventory.Furniture.AllItems);
            Assert.Single(packets, packet => packet.Header == ServerPacketHeader.TalentLevelUpComposer);

        }
        finally { connection.Execute($"DROP DATABASE `{schema}`"); }
    }
}
