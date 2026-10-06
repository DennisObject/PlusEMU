namespace Plus.HabboHotel.Users;

public sealed record UserObjectSnapshot(int Id, string Username, string Look, string Gender, string Motto,
    int Respect, int DailyRespectPoints, int DailyPetRespectPoints, DateTimeOffset? LastOnlineAt, bool ChangingName)
{
    public static UserObjectSnapshot Capture(Habbo habbo) => new(habbo.Id, habbo.Username, habbo.Look,
        habbo.Gender.ToUpperInvariant(), habbo.Motto, habbo.HabboStats.Respect, habbo.HabboStats.DailyRespectPoints,
        habbo.HabboStats.DailyPetRespectPoints, habbo.LastOnlineAt, habbo.ChangingName);
}
