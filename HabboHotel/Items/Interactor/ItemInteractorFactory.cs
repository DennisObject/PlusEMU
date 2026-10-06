using Plus.HabboHotel.Achievements;
using Plus.HabboHotel.Quests;
using Plus.HabboHotel.Users;

namespace Plus.HabboHotel.Items.Interactor;

public interface IItemInteractorFactory
{
    IFurniInteractor Create(Item item, TimeProvider timeProvider);
}

public sealed class ItemInteractorFactory(
    IItemTravelStore travelStore,
    IUserProfileService profiles,
    IQuestManager quests,
    IRewardTrackManager rewards,
    IAchievementManager achievements) : IItemInteractorFactory
{
    public IFurniInteractor Create(Item item, TimeProvider timeProvider)
    {
        if (item.IsWired)
        {
            return new InteractorWired();
        }

        return item.Definition.InteractionType switch
        {
            InteractionType.Gate => new InteractorGate(),
            InteractionType.Teleport => new InteractorTeleport(timeProvider),
            InteractionType.Hopper => new InteractorHopper(travelStore),
            InteractionType.Bottle => new InteractorSpinningBottle(),
            InteractionType.Dice => new InteractorDice(),
            InteractionType.HabboWheel => new InteractorHabboWheel(),
            InteractionType.LoveShuffler => new InteractorLoveShuffler(),
            InteractionType.OneWayGate => new InteractorOneWayGate(timeProvider),
            InteractionType.Alert => new InteractorAlert(),
            InteractionType.VendingMachine => new InteractorVendor(),
            InteractionType.Scoreboard => new InteractorScoreboard(),
            InteractionType.PuzzleBox => new InteractorPuzzleBox(),
            InteractionType.Mannequin => new InteractorMannequin(profiles),
            InteractionType.Banzaicounter => new InteractorBanzaiTimer(),
            InteractionType.Freezetimer => new InteractorFreezeTimer(),
            InteractionType.FreezeTileBlock or InteractionType.FreezeTile => new InteractorFreezeTile(),
            InteractionType.Footballcounterblue or InteractionType.Footballcountergreen or
                InteractionType.Footballcounterred or InteractionType.Footballcounteryellow => new InteractorScoreCounter(),
            InteractionType.Banzaiscoreblue or InteractionType.Banzaiscoregreen or
                InteractionType.Banzaiscorered or InteractionType.Banzaiscoreyellow => new InteractorBanzaiScoreCounter(),
            InteractionType.WfFloorSwitch1 or InteractionType.WfFloorSwitch2 => new InteractorSwitch(quests, rewards),
            InteractionType.Lovelock => new InteractorLoveLock(),
            InteractionType.Cannon => new InteractorCannon(),
            InteractionType.Counter => new InteractorCounter(),
            InteractionType.CrackableEgg => new InteractorCrackable(achievements),
            InteractionType.Skateboard => new InteractorSkateboard(achievements),
            _ => new InteractorGenericSwitch(quests, rewards)
        };
    }
}
