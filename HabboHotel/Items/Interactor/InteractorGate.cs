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
        if (GateTransitionService.For(item) != null)
        {
            if (hasRights)
                Toggle(item, GateCloseReason.Click,
                    changed => changed.GetRoom().GetWired().TriggerEvent(WiredBoxType.TriggerStateChanges, session.GetHabbo(), changed));
            return;
        }
        var modes = item.Definition.Modes - 1;
        if (!hasRights)
            return;
        if (modes <= 0) item.UpdateState(false, true);
        var currentMode = 0;
        var newMode = 0;
        if (!int.TryParse(item.LegacyDataString, out currentMode)) { }
        if (currentMode <= 0)
            newMode = 1;
        else if (currentMode >= modes)
            newMode = 0;
        else
            newMode = currentMode + 1;
        if (newMode == 0)
            if (!item.GetRoom().GetGameMap().ItemCanBePlaced(item.GetX, item.GetY))
                return;
        item.LegacyDataString = newMode.ToString();
        item.UpdateState();
        item.GetRoom().GetGameMap().UpdateMapForItem(item);
        item.GetRoom().GetWired().TriggerEvent(WiredBoxType.TriggerStateChanges, session.GetHabbo(), item);
        //Item.GetRoom().GenerateMaps();
    }

    public void OnWiredTrigger(Item item)
    {
        if (GateTransitionService.For(item) != null) { Toggle(item, GateCloseReason.Wired, null); return; }
        var modes = item.Definition.Modes - 1;
        if (modes <= 0) item.UpdateState(false, true);
        var currentMode = 0;
        var newMode = 0;
        if (!int.TryParse(item.LegacyDataString, out currentMode)) { }
        if (currentMode <= 0)
            newMode = 1;
        else if (currentMode >= modes)
            newMode = 0;
        else
            newMode = currentMode + 1;
        if (newMode == 0)
            if (!item.GetRoom().GetGameMap().ItemCanBePlaced(item.GetX, item.GetY))
                return;
        item.LegacyDataString = newMode.ToString();
        item.UpdateState();
        item.GetRoom().GetGameMap().UpdateMapForItem(item);
        //Item.GetRoom().GenerateMaps();
    }

    // v2 only: closing goes through the per-gate sequencer; the follow-up runs once the new state is written.
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