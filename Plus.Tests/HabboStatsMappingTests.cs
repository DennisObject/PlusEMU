using System.Data;
using Dapper;
using Plus.HabboHotel.Users;
using Xunit;

namespace Plus.Tests;

public class HabboStatsMappingTests
{
    [Fact]
    public void Maps_user_statistics_row_and_rejects_direct_HabboStats_materialization()
    {
        const int onlineTime = 12;
        const uint questId = 99;

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
        table.Rows.Add(0, onlineTime, 0, 0, 0, 0, 0, 0, 0, questId, 0, 7, DBNull.Value, 0);

        using var reader = table.CreateDataReader();

        Assert.Throws<InvalidOperationException>(() => SqlMapper.GetTypeDeserializer(typeof(HabboStats), reader));

        Assert.True(reader.Read());
        var deserialize = SqlMapper.GetTypeDeserializer(typeof(HabboStatsMaterializer.Row), reader);
        var row = (HabboStatsMaterializer.Row)deserialize(reader);
        var stats = HabboStatsMaterializer.ToHabboStats(row);

        Assert.Equal((double)onlineTime, stats.OnlineTime);
        Assert.Equal(questId, (uint)stats.QuestId);
        Assert.Equal(7, stats.FavouriteGroupId);
        Assert.Equal("", stats.RespectsTimestamp);
    }
}
