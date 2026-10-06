using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms.Games.Teams;

namespace Plus.HabboHotel.Items.Interactor;

public class InteractorBanzaiScoreCounter : IFurniInteractor
{
    public void OnPlace(GameClient? session, Item item)
    {
        var itemRoom = item.GetRoom();

        if (itemRoom == null) {
            return;
        }

        if (item.Team == Team.None) {
            return;
        }

        item.LegacyDataString = itemRoom.GetGameManager().Points[Convert.ToInt32(item.Team)].ToString();
        item.UpdateState(false, true);
    }

    public void OnRemove(GameClient? session, Item item) { }

    public void OnTrigger(GameClient? session, Item item, int request, bool hasRights)
    {
        var itemRoom = item.GetRoom();

        if (itemRoom == null) {
            return;
        }

        if (hasRights) {
            itemRoom.GetGameManager().Points[Convert.ToInt32(item.Team)] = 0;
            item.LegacyDataString = "0";
            item.UpdateState();
        }
    }

    public void OnWiredTrigger(Item item) { }
}
