using Plus.HabboHotel.Users;

namespace Plus.HabboHotel.Games;

public sealed record WeeklyLeaderboardRow(int UserId, int Score, string Username, string Look, string Gender)
{
    public static WeeklyLeaderboardRow Capture(Habbo habbo) => new(habbo.Id, habbo.FastfoodScore, habbo.Username, habbo.Look, habbo.Gender.ToLower());
}
