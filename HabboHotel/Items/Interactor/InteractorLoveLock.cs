using System.Drawing;
using Plus.Communication.Packets.Outgoing.Rooms.Furni.LoveLocks;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms;

namespace Plus.HabboHotel.Items.Interactor;

public class InteractorLoveLock : IFurniInteractor
{
    public void OnPlace(GameClient? session, Item item) { }

    public void OnRemove(GameClient? session, Item item) { }

    public void OnTrigger(GameClient? session, Item item, int request, bool hasRights)
    {
        var itemRoom = item.GetRoom();
        if (itemRoom == null) return;

        if (session == null) return;
        var user = itemRoom.GetRoomUserManager().GetRoomUserByHabbo(session.GetHabbo().Id);
        if (user == null)
            return;
        if (Gamemap.TilesTouching(item.GetX, item.GetY, user.X, user.Y))
        {
            if (item.LegacyDataString == null || item.LegacyDataString.Length <= 1 || !item.LegacyDataString.Contains(Convert.ToChar(5).ToString()))
            {
                Point pointOne;
                Point pointTwo;
                switch (item.Rotation)
                {
                    case 2:
                        pointOne = new(item.GetX, item.GetY + 1);
                        pointTwo = new(item.GetX, item.GetY - 1);
                        break;
                    case 4:
                        pointOne = new(item.GetX - 1, item.GetY);
                        pointTwo = new(item.GetX + 1, item.GetY);
                        break;
                    default:
                        return;
                }
                var userOne = itemRoom.GetRoomUserManager().GetUserForSquare(pointOne.X, pointOne.Y);
                var userTwo = itemRoom.GetRoomUserManager().GetUserForSquare(pointTwo.X, pointTwo.Y);
                var clientOne = userOne?.GetClient();
                var clientTwo = userTwo?.GetClient();
                if (userOne == null || userTwo == null || clientOne == null || clientTwo == null)
                    session.SendNotification("We couldn't find a valid user to lock this love lock with.");
                else if (userOne.HabboId != item.UserId && userTwo.HabboId != item.UserId)
                    session.SendNotification("You can only use this item with the item owner.");
                else
                {
                    userOne.CanWalk = false;
                    userTwo.CanWalk = false;
                    item.InteractingUser = clientOne.GetHabbo().Id;
                    item.InteractingUser2 = clientTwo.GetHabbo().Id;
                    clientOne.Send(new LoveLockDialogueComposer(item.Id));
                    clientTwo.Send(new LoveLockDialogueComposer(item.Id));
                }
            }
            else
                return;
        }
        else
            user.MoveTo(item.SquareInFront);
    }

    public void OnWiredTrigger(Item item) { }
}