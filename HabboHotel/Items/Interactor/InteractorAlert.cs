using Plus.HabboHotel.GameClients;

namespace Plus.HabboHotel.Items.Interactor;

public class InteractorAlert : IFurniInteractor
{
    public void OnPlace(GameClient? session, Item item)
    {
        item.LegacyDataString = "0";
        item.UpdateNeeded = true;
    }

    public void OnRemove(GameClient? session, Item item)
    {
        item.LegacyDataString = "0";
    }

    public void OnTrigger(GameClient? session, Item item, int request, bool hasRights)
    {
        if (!hasRights) return;
        if (item.LegacyDataString == "0")
        {
            item.LegacyDataString = "1";
            item.UpdateState(false, true);
            item.RequestUpdate(4, true);
        }
    }

    public void OnWiredTrigger(Item item)
    {
        if (item.LegacyDataString == "0")
        {
            item.LegacyDataString = "1";
            item.UpdateState(false, true);
            item.RequestUpdate(4, true);
        }
    }
}