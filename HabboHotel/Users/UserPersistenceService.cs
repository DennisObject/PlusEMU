using Dapper;
using Plus.Database;

namespace Plus.HabboHotel.Users;

public interface IUserPersistenceService
{
    void Save(Habbo habbo, bool reopenModerationTickets = false);
    void SetProfileValue(int userId, string column, object? value);
}

public sealed class UserPersistenceService(IDatabase database, TimeProvider clock) : IUserPersistenceService
{
    public void Save(Habbo habbo, bool reopenModerationTickets = false)
    {
        var now = clock.GetUtcNow();
        using var connection = database.Connection();
        connection.Open();
        using var transaction = connection.BeginTransaction();
        connection.Execute(
            "UPDATE `users` SET `online` = false, `last_online` = @now, `credits` = @Credits, " +
            "`time_muted` = @TimeMuted, `bubble_id` = @CustomBubbleId WHERE `id` = @Id; " +
            "UPDATE `users_settings` SET `home_room` = @HomeRoom, `friend_bar_state` = @FriendbarState WHERE `user_id` = @Id; " +
            "UPDATE `user_statistics` SET `roomvisits` = @RoomVisits, `onlineTime` = `onlineTime` + @SessionSeconds, " +
            "`respect` = @Respect, `respectGiven` = @RespectGiven, `giftsGiven` = @GiftsGiven, `giftsReceived` = @GiftsReceived, " +
            "`dailyRespectPoints` = @DailyRespectPoints, `dailyPetRespectPoints` = @DailyPetRespectPoints, " +
            "`AchievementScore` = @AchievementPoints, `quest_id` = @QuestId, `quest_progress` = @QuestProgress, " +
            "`groupid` = @FavouriteGroupId, `forum_posts` = @ForumPosts WHERE `id` = @Id",
            new
            {
                habbo.Id,
                habbo.Credits,
                habbo.TimeMuted,
                habbo.CustomBubbleId,
                habbo.HomeRoom,
                FriendbarState = Messenger.FriendBar.FriendBarStateUtility.GetInt(habbo.FriendbarState),
                habbo.HabboStats.RoomVisits,
                SessionSeconds = Math.Max(0L, (long)(now - habbo.SessionStartedAt).TotalSeconds),
                habbo.HabboStats.Respect,
                habbo.HabboStats.RespectGiven,
                habbo.HabboStats.GiftsGiven,
                habbo.HabboStats.GiftsReceived,
                habbo.HabboStats.DailyRespectPoints,
                habbo.HabboStats.DailyPetRespectPoints,
                habbo.HabboStats.AchievementPoints,
                habbo.HabboStats.QuestId,
                habbo.HabboStats.QuestProgress,
                habbo.HabboStats.FavouriteGroupId,
                habbo.HabboStats.ForumPosts,
                now = now.UtcDateTime
            }, transaction);

        UserCurrencyStore.SetMany(connection, habbo.Id, habbo.Currencies.Snapshot(), transaction);

        if (reopenModerationTickets) {
            connection.Execute("UPDATE `moderation_tickets` SET `status` = 'open', `moderator_id` = 0 WHERE `status` = 'picked' AND `moderator_id` = @id",
                new { id = habbo.Id }, transaction);
        }

        transaction.Commit();
    }

    public void SetProfileValue(int userId, string column, object? value)
    {
        if (column is not ("username" or "last_change" or "bubble_id")) {
            throw new ArgumentOutOfRangeException(nameof(column));
        }

        using var connection = database.Connection();
        connection.Execute($"UPDATE `users` SET `{column}` = @value WHERE `id` = @userId", new { value, userId });
    }
}
