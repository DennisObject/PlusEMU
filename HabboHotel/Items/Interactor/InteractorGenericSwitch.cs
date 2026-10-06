using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Quests;
using Plus.HabboHotel.Rooms.PathFinding;

namespace Plus.HabboHotel.Items.Interactor;

public class InteractorGenericSwitch(IQuestManager quests, IRewardTrackManager rewards) : IFurniInteractor
{
    public void OnPlace(GameClient? session, Item item) { }

    public void OnRemove(GameClient? session, Item item) { }

    public void OnTrigger(GameClient? session, Item item, int request, bool hasRights)
    {
        var modes = item.Definition.Modes - 1;
        if (session == null || !hasRights || modes <= 0) return;
        quests.ProgressUserQuest(session, QuestType.FurniSwitch);
        if (GateTransitionService.For(item) != null) { ToggleSequenced(session, item, modes); return; }
        var before = item.LegacyDataString;
        var currentMode = 0;
        var newMode = 0;
        if (!int.TryParse(item.LegacyDataString, out currentMode)) { }
        if (currentMode <= 0)
            newMode = 1;
        else if (currentMode >= modes)
            newMode = 0;
        else
            newMode = currentMode + 1;
        item.LegacyDataString = newMode.ToString();
        item.UpdateState();
        if (!string.Equals(before, item.LegacyDataString, StringComparison.Ordinal))
            rewards.Progress(session, RewardTrackActions.SwitchItemState);
    }

    public void OnWiredTrigger(Item item)
    {
        var modes = item.Definition.Modes - 1;
        if (modes == 0) return;
        if (GateTransitionService.For(item) != null)
        {
            GateTransitionService.ToggleState(item, current => string.IsNullOrEmpty(current) ? NextMode("0", modes).ToString()
                : int.TryParse(current, out _) ? NextMode(current, modes).ToString() : null, GateCloseReason.Wired);
            return;
        }
        var currentMode = 0;
        var newMode = 0;
        if (string.IsNullOrEmpty(item.LegacyDataString))
            item.LegacyDataString = "0";
        if (!int.TryParse(item.LegacyDataString, out currentMode)) return;
        if (currentMode <= 0)
            newMode = 1;
        else if (currentMode >= modes)
            newMode = 0;
        else
            newMode = currentMode + 1;
        item.LegacyDataString = newMode.ToString();
        item.UpdateState();
    }

    // v2 only: gate states are written through the per-gate sequencer, which reports their state change on write.
    private void ToggleSequenced(GameClient session, Item item, int modes)
    {
        var before = item.LegacyDataString;
        var room = item.GetRoom();
        var actor = room == null ? null : FurnitureStateEvents.Actor(room, session);
        GateTransitionService.ToggleState(item, current => NextMode(current, modes).ToString(), GateCloseReason.Click, afterWrite: changed =>
        {
            if (!string.Equals(before, changed.LegacyDataString, StringComparison.Ordinal))
                rewards.Progress(session, RewardTrackActions.SwitchItemState);
            if (room != null && FurnitureStateEvents.IsSequenced(changed)) FurnitureStateEvents.Publish(room, actor, changed);
        });
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