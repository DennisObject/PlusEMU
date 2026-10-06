using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.PathFinding;

namespace Plus.HabboHotel.Items.Interactor;

public class InteractorVendor : IFurniInteractor, IApproachInteractor
{
    public int ActionKind => ApproachActionKind.VendingMachine;

    public void OnPlace(GameClient? session, Item item)
    {
        var itemRoom = item.GetRoom();

        if (itemRoom == null)
        {
            return;
        }

        item.LegacyDataString = "0";
        item.UpdateNeeded = true;

        if (item.InteractingUser > 0)
        {
            var user = itemRoom.GetRoomUserManager().GetRoomUserByHabbo(item.InteractingUser);

            if (user != null)
            {
                user.CanWalk = true;
            }
        }
    }

    public void OnRemove(GameClient? session, Item item)
    {
        var itemRoom = item.GetRoom();

        if (itemRoom == null)
        {
            return;
        }

        item.LegacyDataString = "0";

        if (item.InteractingUser > 0)
        {
            var user = itemRoom.GetRoomUserManager().GetRoomUserByHabbo(item.InteractingUser);

            if (user != null)
            {
                user.CanWalk = true;
            }
        }
    }

    public void OnTrigger(GameClient? session, Item item, int request, bool hasRights)
    {
        var itemRoom = item.GetRoom();

        if (itemRoom == null)
        {
            return;
        }

        if (!CanDispense(item) || session == null)
        {
            return;
        }

        var user = itemRoom.GetRoomUserManager().GetRoomUserByHabbo(session.GetHabbo().Id);

        if (user == null)
        {
            return;
        }

        if (!Gamemap.TilesTouching(user.X, user.Y, item.GetX, item.GetY))
        {
            user.ApproachItem(item, ActionKind);

            return;
        }

        StartDispensing(item, user, session.GetHabbo().Id);
    }

    public bool StartFromApproach(Item item, RoomUser user)
    {
        if (!CanDispense(item) || user.GetClient()?.GetHabbo() is not { } habbo)
        {
            return false;
        }

        if (!Gamemap.TilesTouching(user.X, user.Y, item.GetX, item.GetY))
        {
            return false;
        }

        StartDispensing(item, user, habbo.Id);

        return true;
    }

    private static bool CanDispense(Item item)
        => item.LegacyDataString != "1" && item.Definition.VendingIds.Count >= 1 && item.InteractingUser == 0;

    private static void StartDispensing(Item item, RoomUser user, int habboId)
    {
        item.InteractingUser = habboId;
        user.CanWalk = false;
        user.ClearMovement(true);
        user.SetRot(Rotation.Calculate(user.X, user.Y, item.GetX, item.GetY), false);
        item.RequestUpdate(2, true);
        item.LegacyDataString = "1";
        item.UpdateState(false, true);
    }

    public void OnWiredTrigger(Item item)
    {
    }
}
