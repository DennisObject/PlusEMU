using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items.DataFormat;
using Plus.HabboHotel.Achievements;

namespace Plus.HabboHotel.Items.Interactor;

internal sealed class InteractorCrackable(IAchievementManager achievements) : IFurniInteractor
{
    private const int RequiredEffect = 158;

    public void OnTrigger(GameClient? session, Item item, int request, bool hasRights)
    {
        if (session?.GetHabbo()?.Effects == null || session.GetHabbo().Effects.CurrentEffect != RequiredEffect)
        {
            return;
        }

        if (item.ExtraData is not CrackableDataFormat data || data.Target == 0 || data.Hits >= data.Target)
        {
            return;
        }

        achievements.ProgressAchievement(session, "ACH_PinataWhacker", 1);

        if (data.TryCrack())
        {
            achievements.ProgressAchievement(session, "ACH_PinataBreaker", 1);
        }

        item.UpdateState();
    }
}
