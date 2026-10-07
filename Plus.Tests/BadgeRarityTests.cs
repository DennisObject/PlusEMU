using System.Data;
using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using MySqlConnector;
using Plus.Core.Settings;
using Plus.Database;
using Plus.HabboHotel.Badges.Rarity;
using Xunit;

namespace Plus.Tests;

public sealed class BadgeRarityTests
{
    [Theory]
    [InlineData(0, BadgeRarityTier.Common)]
    [InlineData(1, BadgeRarityTier.Unique)]
    [InlineData(3, BadgeRarityTier.Legendary)]
    [InlineData(4, BadgeRarityTier.Mythical)]
    [InlineData(8, BadgeRarityTier.Mythical)]
    [InlineData(20, BadgeRarityTier.Epic)]
    [InlineData(50, BadgeRarityTier.Rare)]
    [InlineData(51, BadgeRarityTier.Common)]
    public void SmallHotelsKeepTheMinimumBandsWithLegendaryAboveMythical(int owners, BadgeRarityTier expected) =>
        Assert.Equal(expected, BadgeRarityScale.For(14, null).Classify(owners));

    [Theory]
    [InlineData(1000, 5, 15, 50, 150)]
    [InlineData(100_000, 500, 1500, 5000, 15000)]
    public void CeilingsGrowWithTheActivePopulation(int population, int legendary, int mythical, int epic, int rare)
    {
        var scale = BadgeRarityScale.For(population, null);

        Assert.Equal(BadgeRarityTier.Legendary, scale.Classify(legendary));
        Assert.Equal(BadgeRarityTier.Mythical, scale.Classify(legendary + 1));
        Assert.Equal(BadgeRarityTier.Mythical, scale.Classify(mythical));
        Assert.Equal(BadgeRarityTier.Epic, scale.Classify(mythical + 1));
        Assert.Equal(BadgeRarityTier.Epic, scale.Classify(epic));
        Assert.Equal(BadgeRarityTier.Rare, scale.Classify(epic + 1));
        Assert.Equal(BadgeRarityTier.Rare, scale.Classify(rare));
        Assert.Equal(BadgeRarityTier.Common, scale.Classify(rare + 1));
        Assert.Equal(BadgeRarityTier.Unique, scale.Classify(1));
    }

    [Fact]
    public void SettingsOverrideBandsAndEnableUncommon()
    {
        var settings = new Settings(new()
        {
            ["badge.rarity.uncommon"] = "1",
            ["badge.rarity.rare.percent"] = "10",
            ["badge.rarity.epic.min_owners"] = "not a number"
        });
        var scale = BadgeRarityScale.For(1000, settings);

        Assert.Equal(BadgeRarityTier.Epic, scale.Classify(50));
        Assert.Equal(BadgeRarityTier.Rare, scale.Classify(100));
        Assert.Equal(BadgeRarityTier.Uncommon, scale.Classify(101));
        Assert.Equal(BadgeRarityTier.Uncommon, scale.Classify(350));
        Assert.Equal(BadgeRarityTier.Common, scale.Classify(351));
        Assert.Equal([BadgeRarityTier.Uncommon, BadgeRarityTier.Rare, BadgeRarityTier.Epic, BadgeRarityTier.Mythical, BadgeRarityTier.Legendary, BadgeRarityTier.Unique],
            scale.LeaderboardTiers());
        Assert.Equal([BadgeRarityTier.Rare, BadgeRarityTier.Epic, BadgeRarityTier.Mythical, BadgeRarityTier.Legendary, BadgeRarityTier.Unique],
            BadgeRarityScale.For(1000, null).LeaderboardTiers());
    }

    [Fact]
    public void ActiveDaysFallBackToTheDefault()
    {
        Assert.Equal(90, BadgeRarityScale.ActiveDays(new Settings(new())));
        Assert.Equal(30, BadgeRarityScale.ActiveDays(new Settings(new() { ["badge.rarity.active_days"] = "30" })));
    }

    [Fact]
    public void SnapshotCountsOwnersCaseInsensitivelyAndRanksEveryBoard()
    {
        BadgeOwnership[] ownership =
        [
            new(1, "SOLO"), new(1, "PAIR"), new(2, "pair"),
            new(3, "WIDE"), .. Enumerable.Range(10, 60).Select(id => new BadgeOwnership(id, "WIDE"))
        ];
        var snapshot = BadgeLeaderboardSnapshot.Build(ownership, [new(2, 40), new(1, 40), new(3, 0)], BadgeRarityScale.Empty);

        Assert.Equal(new BadgeRarity(2, BadgeRarityTier.Legendary), snapshot.Rarity.Get("Pair"));
        Assert.Equal(new BadgeRarity(61, BadgeRarityTier.Common), snapshot.Rarity.Get("WIDE"));
        Assert.Equal(new BadgeRarity(1, BadgeRarityTier.Unique), snapshot.Rarity.Get("NEWER_THAN_THE_REFRESH"));
        Assert.Equal((1, 2), snapshot.TotalBadges.Find(1));
        Assert.Equal((1, 1), snapshot.RarityBoards[BadgeRarityTier.Unique].Find(1));
        Assert.Equal(2, snapshot.RarityBoards[BadgeRarityTier.Legendary].Ranked.Length);
        Assert.Null(snapshot.RarityBoards[BadgeRarityTier.Legendary].Find(3));
        Assert.False(snapshot.RarityBoards.ContainsKey(BadgeRarityTier.Common));
        // Ties go to the lower user id; a zero score is not ranked.
        Assert.Equal([1, 2], snapshot.AchievementLevel.Ranked.Select(score => score.UserId));
        Assert.Equal(63, snapshot.TotalBadges.Ranked.Length);
    }

    [Fact]
    public void BoardsSendOnlyTheTopEntries()
    {
        var snapshot = BadgeLeaderboardSnapshot.Build(Enumerable.Range(1, 150).Select(id => new BadgeOwnership(id, "X")), [], BadgeRarityScale.Empty);

        Assert.Equal(BadgeLeaderboardSnapshot.EntryLimit, snapshot.TotalBadges.Top.Count());
        Assert.Equal((150, 1), snapshot.TotalBadges.Find(150));
    }

    [RoomComponentDatabaseFact]
    public async Task RefreshCountsActivePlayersFromTheDatabaseAndPublishesTheTable()
    {
        var connectionString = Environment.GetEnvironmentVariable("ROOM_COMPONENT_DATABASE")!;
        var schema = "badge_rarity_tests_" + Guid.NewGuid().ToString("N");
        using var server = new MySqlConnection(connectionString);
        server.Execute($"CREATE DATABASE `{schema}`");
        var previous = BadgeRarityTable.Current;

        try {
            var database = new ProbeDatabase(new MySqlConnectionStringBuilder(connectionString) { Database = schema }.ConnectionString);
            using var connection = database.Connection();
            connection.Execute("""
                CREATE TABLE users(id INT PRIMARY KEY, username VARCHAR(50), look VARCHAR(255), online TINYINT(1) DEFAULT 0, last_online DATETIME NULL);
                CREATE TABLE user_badges(id INT AUTO_INCREMENT PRIMARY KEY, user_id INT UNSIGNED NOT NULL, badge_id VARCHAR(100) NOT NULL, badge_slot INT NOT NULL DEFAULT 0,
                    UNIQUE KEY owner (user_id, badge_id)) DEFAULT CHARSET=latin1;
                CREATE TABLE user_statistics(id INT PRIMARY KEY, AchievementScore INT NOT NULL DEFAULT 0);
                INSERT INTO users VALUES (1,'Online','hd-1',1,NULL),(2,'Recent','hd-2',0,'2026-10-01 00:00:00'),(3,'Gone','hd-3',0,'2026-01-01 00:00:00'),(4,'Quiet','hd-4',0,NULL);
                INSERT INTO user_badges(user_id, badge_id) VALUES (1,'RARITY_SOLO'),(1,'RARITY_PAIR'),(2,'rarity_pair'),(3,'RARITY_PAIR'),(99,'RARITY_ORPHAN');
                INSERT INTO user_statistics VALUES (1,25),(2,0);
                """);
            var manager = new BadgeRarityManager(database, new Settings(new()), new FixedTimeProvider(new DateTimeOffset(2026, 10, 7, 0, 0, 0, TimeSpan.Zero)),
                NullLogger<BadgeRarityManager>.Instance);

            await manager.Refresh();

            var snapshot = manager.Snapshot;
            Assert.Equal(2, snapshot.Rarity.Scale.Population);
            Assert.Same(snapshot.Rarity, BadgeRarityTable.Current);
            Assert.Equal(new BadgeRarity(3, BadgeRarityTier.Legendary), BadgeRarityTable.Current.Get("RARITY_PAIR"));
            Assert.Equal(new BadgeRarity(1, BadgeRarityTier.Unique), BadgeRarityTable.Current.Get("RARITY_SOLO"));
            Assert.DoesNotContain(snapshot.Badges, badge => badge.Badge == "RARITY_ORPHAN");
            Assert.Equal((1, 25), snapshot.AchievementLevel.Find(1));
            Assert.Equal(new LeaderboardProfile("Online", "hd-1"), snapshot.Profiles[1]);
            Assert.Equal(new LeaderboardProfile("Quiet", "hd-4"), await manager.GetProfile(4));
            Assert.Null(await manager.GetProfile(42));
        }
        finally {
            BadgeRarityTable.Current = previous;
            server.Execute($"DROP DATABASE `{schema}`");
        }
    }

    private sealed class ProbeDatabase(string connectionString) : IDatabase
    {
        public IDbConnection Connection() => new MySqlConnection(connectionString);
        public bool IsConnected() => true;
    }

    private sealed class Settings(Dictionary<string, string> values) : ISettingsManager
    {
        public string TryGetValue(string value) => values.GetValueOrDefault(value, "0");
        public string? GetOptionalValue(string key) => values.GetValueOrDefault(key);
        public Task Reload() => Task.CompletedTask;
    }
}
