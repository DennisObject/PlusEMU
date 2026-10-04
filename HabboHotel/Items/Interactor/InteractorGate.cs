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
        var modes = item.Definition.Modes - 1;
        if (modes <= 0) item.UpdateState(false, true);
        GateTransitionService.ToggleState(item, current => NextState(item, current, modes), reason, afterWrite: changed =>
        {
            changed.GetRoom().GetGameMap().UpdateMapForItem(changed);
            afterChange?.Invoke(changed);
        });
    }

    // Null keeps the gate as it is: closing is not possible while the legacy map says the tile is taken.
    private static string? NextState(Item item, string current, int modes)
    {
        var newMode = NextMode(current, modes);
        if (newMode == 0 && !item.GetRoom().GetGameMap().ItemCanBePlaced(item.GetX, item.GetY)) return null;
        return newMode.ToString();
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