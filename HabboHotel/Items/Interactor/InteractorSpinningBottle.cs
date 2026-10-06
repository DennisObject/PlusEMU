using Plus.HabboHotel.GameClients;

namespace Plus.HabboHotel.Items.Interactor;

public class InteractorSpinningBottle : IFurniInteractor
{
    public void OnPlace(GameClient? session, Item item)
    {
        item.LegacyDataString = "0";
        item.UpdateState(true, false);
    }

    public void OnRemove(GameClient? session, Item item)
    {
        item.LegacyDataString = "0";
    }

    public void OnTrigger(GameClient? session, Item item, int request, bool hasRights)
    {
        if (item.LegacyDataString != "-1") {
            item.LegacyDataString = "-1";
            item.UpdateState(false, true);
            item.RequestUpdate(3, true);
        }
    }

    public void OnWiredTrigger(Item item)
    {
        if (item.LegacyDataString != "-1") {
            item.LegacyDataString = "-1";
            item.UpdateState(false, true);
            item.RequestUpdate(3, true);
        }
    }
}
