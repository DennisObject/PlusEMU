using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Achievements;

namespace Plus.HabboHotel.Items.Interactor;

internal sealed class InteractorSkateboard(IAchievementManager achievements) : IFurniInteractor
{
    private const int SkateboardEffect = 71;

    public void OnWalkOn(RoomUser user)
    {
        var session = user?.GetClient();
        var habbo = session?.GetHabbo();

        if (user == null || session == null || habbo?.Effects == null)
        {
            return;
        }

        if (habbo.Effects.CurrentEffect != SkateboardEffect)
        {
            habbo.Effects.ApplyEffect(SkateboardEffect);
        }

        if (!TryTrick(user.LastItem, out var body, out var head, out var lift, out var achievement))
        {
            return;
        }

        user.RotBody = body;
        user.RotHead = head;
        user.Z += lift;
        achievements.ProgressAchievement(session, achievement, 1);
        user.UpdateNeeded = true;
    }

    internal static bool TryTrick(Item? previous, out int body, out int head, out double lift, out string achievement)
    {
        body = 0;
        head = 0;
        lift = 0;
        achievement = "";

        if (previous?.Definition?.ItemName != "sb_rail")
        {
            return false;
        }

        if (previous.Rotation == 2)
        {
            body = 3;
            head = 3;
            lift = 1;
            achievement = "ACH_SkateBoardJump";

            return true;
        }

        if (previous.Rotation == 0)
        {
            body = 2;
            head = 2;
            achievement = "ACH_SkateBoardSlide";

            return true;
        }

        return false;
    }
}
