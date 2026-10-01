using System.Data;
using Dapper;
using Microsoft.Data.Sqlite;
using Plus.Database;
using Plus.Database.Interfaces;
using Plus.HabboHotel.Users;
using Xunit;

namespace Plus.Users.Tests;

public class HabboStatsMappingTests
{
    [Fact]
    public void DeserializerMapsMySqlColumnTypes()
    {
        using var table = new DataTable();
        table.Columns.Add("RoomVisits", typeof(int));
        table.Columns.Add("OnlineTime", typeof(int));
        table.Columns.Add("Respect", typeof(int));
        table.Columns.Add("RespectGiven", typeof(int));
        table.Columns.Add("GiftsGiven", typeof(int));
        table.Columns.Add("GiftsReceived", typeof(int));
        table.Columns.Add("DailyRespectPoints", typeof(int));
        table.Columns.Add("DailyPetRespectPoints", typeof(int));
        table.Columns.Add("AchievementPoints", typeof(int));
        table.Columns.Add("QuestId", typeof(uint));
        table.Columns.Add("QuestProgress", typeof(int));
        table.Columns.Add("FavouriteGroupId", typeof(int));
        table.Columns.Add("RespectsTimestamp", typeof(string));
        table.Columns.Add("ForumPosts", typeof(int));
        table.Rows.Add(12, 3600, 3, 4, 5, 6, 2, 1, 900, 42u, 7, 123, "10/01", 8);
        using var reader = table.CreateDataReader();

        var deserialize = SqlMapper.GetTypeDeserializer(typeof(HabboStats), reader);
        Assert.True(reader.Read());
        var stats = Assert.IsType<HabboStats>(deserialize(reader));

        AssertStats(stats);
        Assert.Equal(3, stats.Respect);
        Assert.Equal(4, stats.RespectGiven);
        Assert.Equal(5, stats.GiftsGiven);
        Assert.Equal(6, stats.GiftsReceived);
        Assert.Equal(2, stats.DailyRespectPoints);
        Assert.Equal(1, stats.DailyPetRespectPoints);
        Assert.Equal(900, stats.AchievementPoints);
        Assert.Equal(7, stats.QuestProgress);
    }

    [Fact]
    public async Task DapperQueryAndServiceLoadStatisticsRow()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await connection.ExecuteAsync(@"
            CREATE TABLE user_statistics (
                id INTEGER PRIMARY KEY, RoomVisits INTEGER, OnlineTime INTEGER,
                Respect INTEGER, RespectGiven INTEGER, GiftsGiven INTEGER,
                GiftsReceived INTEGER, DailyRespectPoints INTEGER,
                DailyPetRespectPoints INTEGER, AchievementScore INTEGER,
                quest_id INTEGER, quest_progress INTEGER, groupid INTEGER,
                respectsTimestamp TEXT, forum_posts INTEGER);
            INSERT INTO user_statistics VALUES
                (1, 12, 3600, 3, 4, 5, 6, 2, 1, 900, 42, 7, 123, '10/01', 8);");

        var stats = await connection.QueryFirstOrDefaultAsync<HabboStats>(
            @"SELECT RoomVisits, OnlineTime, Respect, RespectGiven, GiftsGiven, GiftsReceived,
              DailyRespectPoints, DailyPetRespectPoints, `AchievementScore` AS AchievementPoints,
              quest_id AS QuestId, quest_progress AS QuestProgress, groupid AS FavouriteGroupId,
              respectsTimestamp AS RespectsTimestamp, forum_posts AS ForumPosts
              FROM `user_statistics` WHERE `id` = @id LIMIT 1",
            new { id = 1 });

        Assert.NotNull(stats);
        AssertStats(stats);

        var service = new HabboStatsService(new TestDatabase(connection));
        AssertStats(await service.LoadHabboStats(1));
    }

    private static void AssertStats(HabboStats stats)
    {
        Assert.Equal(12, stats.RoomVisits);
        Assert.Equal(3600d, stats.OnlineTime);
        Assert.Equal(123, stats.FavouriteGroupId);
        Assert.Equal(42, stats.QuestId);
        Assert.Equal("10/01", stats.RespectsTimestamp);
        Assert.Equal(8, stats.ForumPosts);
    }

    private sealed class TestDatabase : IDatabase
    {
        private readonly IDbConnection _connection;

        public TestDatabase(IDbConnection connection) => _connection = connection;

        public IDbConnection Connection() => _connection;
        public bool IsConnected() => throw new NotSupportedException();
        public IQueryAdapter GetQueryReactor() => throw new NotSupportedException();
    }
}
