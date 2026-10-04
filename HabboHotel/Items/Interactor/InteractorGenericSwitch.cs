using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Quests;
using Plus.HabboHotel.Rooms.PathFinding;

namespace Plus.HabboHotel.Items.Interactor;

public class InteractorGenericSwitch : IFurniInteractor
{
    public void OnPlace(GameClient session, Item item) { }

    public void OnRemove(GameClient session, Item item) { }

    public void OnTrigger(GameClient session, Item item, int request, bool hasRights)
    {
        var modes = item.Definition.Modes - 1;
        if (session == null || !hasRights || modes <= 0) return;
        PlusEnvironment.Game.QuestManager.ProgressUserQuest(session, QuestType.FurniSwitch);
        var before = item.LegacyDataString;
        GateTransitionService.ToggleState(item, current => NextMode(current, modes).ToString(), GateCloseReason.Click, afterWrite: changed =>
        {
            if (!string.Equals(before, changed.LegacyDataString, StringComparison.Ordinal))
                RewardTrackManager.Current?.Progress(session, RewardTrackActions.SwitchItemState);
        });
    }

    public void OnWiredTrigger(Item item)
    {
        var modes = item.Definition.Modes - 1;
        if (modes == 0) return;
        GateTransitionService.ToggleState(item, current => string.IsNullOrEmpty(current) ? NextMode("0", modes).ToString()
            : int.TryParse(current, out _) ? NextMode(current, modes).ToString() : null, GateCloseReason.Wired);
    }

    private static int NextMode(string current, int modes)
    {
        if (!int.TryParse(current, out var currentMode)) { }
        if (currentMode <= 0)
            return 1;
        if (currentMode >= modes)
            return 0;
        return currentMode + 1;
    }
}