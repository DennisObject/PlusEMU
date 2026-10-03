using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items.Wired;
using Plus.HabboHotel.Rooms.PathFinding;

namespace Plus.HabboHotel.Items.Interactor;

public class InteractorGate : IFurniInteractor
{
    public void OnPlace(GameClient session, Item item) { }

    public void OnRemove(GameClient session, Item item) { }

    public void OnTrigger(GameClient session, Item item, int request, bool hasRights)
    {
        if (!hasRights)
            return;
        Toggle(item, GateCloseReason.Click,
            changed => changed.GetRoom().GetWired().TriggerEvent(WiredBoxType.TriggerStateChanges, session.GetHabbo(), changed));
    }

    public void OnWiredTrigger(Item item) => Toggle(item, GateCloseReason.Wired, null);

    // Closing goes through the owner-task operation; the follow-up runs only once the new state is written.
    private static void Toggle(Item item, GateCloseReason reason, Action<Item>? afterChange)
    {
        if (GateTransitionService.CancelQueuedClose(item)) return;
        var modes = item.Definition.Modes - 1;
        if (modes <= 0) item.UpdateState(false, true);
        var newMode = NextMode(item, modes);
        if (newMode == 0)
            if (!item.GetRoom().GetGameMap().ItemCanBePlaced(item.GetX, item.GetY))
                return;
        GateTransitionService.Apply(item, newMode.ToString(), reason, afterWrite: changed =>
        {
            changed.GetRoom().GetGameMap().UpdateMapForItem(changed);
            afterChange?.Invoke(changed);
        });
    }

    private static int NextMode(Item item, int modes)
    {
        if (!int.TryParse(item.LegacyDataString, out var currentMode)) { }
        if (currentMode <= 0)
            return 1;
        if (currentMode >= modes)
            return 0;
        return currentMode + 1;
    }
}