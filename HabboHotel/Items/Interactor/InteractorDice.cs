using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms;

namespace Plus.HabboHotel.Items.Interactor;

public class InteractorDice : IFurniInteractor
{
    public void OnPlace(GameClient? session, Item item)
    {
        if (item.LegacyDataString == "-1") {
            item.LegacyDataString = "0";
            item.UpdateNeeded = true;
        }
    }

    public void OnRemove(GameClient? session, Item item)
    {
        if (item.LegacyDataString == "-1") {
            item.LegacyDataString = "0";
        }
    }

    public void OnTrigger(GameClient? session, Item item, int request, bool hasRights)
    {
        var itemRoom = item.GetRoom();

        if (itemRoom == null) {
            return;
        }

        RoomUser? user = null;

        if (session != null) {
            user = itemRoom.GetRoomUserManager().GetRoomUserByHabbo(session.GetHabbo().Id);
        }

        if (user == null) {
            return;
        }

        if (Gamemap.TilesTouching(item.GetX, item.GetY, user.X, user.Y)) {
            if (item.LegacyDataString != "-1") {
                if (request == -1) {
                    item.LegacyDataString = "0";
                    item.UpdateState();
                }
                else {
                    item.LegacyDataString = "-1";
                    item.UpdateState(false, true);
                    item.RequestUpdate(3, true);
                }
            }
        }
        else {
            user.MoveTo(item.SquareInFront);
        }
    }

    public void OnWiredTrigger(Item item)
    {
        item.LegacyDataString = "-1";
        item.UpdateState(false, true);
        item.RequestUpdate(4, true);
    }
}
