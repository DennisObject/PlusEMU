using Plus.HabboHotel.Rooms.AI;

namespace Plus.HabboHotel.Rooms;

public sealed record AvatarChangeSnapshot(int VirtualId, string Look, string Gender, string Motto, int AchievementPoints)
{
    public static AvatarChangeSnapshot Capture(RoomUser user, bool self)
    {
        var habbo = user.GetClient()?.GetHabbo() ?? throw new InvalidOperationException("Room user has no active account.");

        return new(self ? -1 : user.VirtualId, habbo.Look, habbo.Gender, habbo.Motto, habbo.HabboStats?.AchievementPoints ?? 0);
    }

    public static AvatarChangeSnapshot Capture(RoomBot bot) => new(bot.VirtualId, bot.Look, bot.Gender, bot.Motto, 0);
}
