using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Plus.Tests")]

namespace Plus.HabboHotel.Users;

internal static class HabboStatsMaterializer
{
    public sealed class Row
    {
        public int RoomVisits { get; set; }
        public long OnlineTime { get; set; }
        public int Respect { get; set; }
        public int RespectGiven { get; set; }
        public int GiftsGiven { get; set; }
        public int GiftsReceived { get; set; }
        public int DailyRespectPoints { get; set; }
        public int DailyPetRespectPoints { get; set; }
        public int AchievementPoints { get; set; }
        public long QuestId { get; set; }
        public int QuestProgress { get; set; }
        public int FavouriteGroupId { get; set; }
        public string? RespectsTimestamp { get; set; }
        public int ForumPosts { get; set; }
    }

    public static HabboStats ToHabboStats(Row row) => new(
        row.RoomVisits,
        Convert.ToDouble(row.OnlineTime),
        row.Respect,
        row.RespectGiven,
        row.GiftsGiven,
        row.GiftsReceived,
        row.DailyRespectPoints,
        row.DailyPetRespectPoints,
        row.AchievementPoints,
        Convert.ToInt32(row.QuestId),
        row.QuestProgress,
        row.FavouriteGroupId,
        row.RespectsTimestamp ?? "",
        row.ForumPosts);
}
