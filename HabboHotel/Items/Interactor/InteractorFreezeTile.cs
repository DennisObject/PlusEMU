using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.Games.Teams;

namespace Plus.HabboHotel.Items.Interactor;

internal class InteractorFreezeTile : IFurniInteractor
{
    public void OnPlace(GameClient? session, Item item) { }

    public void OnRemove(GameClient? session, Item item) { }

    public void OnTrigger(GameClient? session, Item item, int request, bool hasRights)
    {
        var itemRoom = item.GetRoom();

        if (itemRoom == null) {
            return;
        }

        if (session == null || !session.GetHabbo().InRoom || item == null || item.InteractingUser > 0) {
            return;
        }

        var user = itemRoom.GetRoomUserManager().GetRoomUserByHabbo(session.GetHabbo().Id);

        if (user == null) {
            return;
        }

        if (user.Team != Team.None) {
            user.FreezeInteracting = true;
            item.InteractingUser = session.GetHabbo().Id;

            if (item.Definition.InteractionType == InteractionType.FreezeTileBlock) {
                if (Gamemap.TileDistance(user.X, user.Y, item.GetX, item.GetY) < 2) {
                    itemRoom.GetFreeze().OnFreezeTiles(item, item.FreezePowerUp);
                }
            }
        }
    }

    public void OnWiredTrigger(Item item) { }
}
