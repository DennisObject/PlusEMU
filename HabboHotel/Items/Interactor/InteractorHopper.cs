using Dapper;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.PathFinding;

namespace Plus.HabboHotel.Items.Interactor;

public class InteractorHopper : IFurniInteractor, IApproachInteractor
{
    public int ActionKind => ApproachActionKind.Hopper;

    public void OnPlace(GameClient? session, Item item)
    {
        var itemRoom = item.GetRoom();
        if (itemRoom == null) return;

        itemRoom.GetRoomItemHandler().HopperCount++;
        using var connection = PlusEnvironment.DatabaseManager.Connection();
        connection.Execute("INSERT INTO items_hopper (hopper_id,room_id) VALUES (@id,@roomId)", new { id = item.Id, roomId = item.RoomId });
        if (item.InteractingUser != 0)
        {
            var user = itemRoom.GetRoomUserManager().GetRoomUserByHabbo(item.InteractingUser);
            if (user != null)
            {
                user.ClearMovement(true);
                user.AllowOverride = false;
                user.CanWalk = true;
            }
            item.InteractingUser = 0;
        }
    }

    public void OnRemove(GameClient? session, Item item)
    {
        var itemRoom = item.GetRoom();
        if (itemRoom == null) return;

        itemRoom.GetRoomItemHandler().HopperCount--;
        using var connection = PlusEnvironment.DatabaseManager.Connection();
        connection.Execute("DELETE FROM items_hopper WHERE hopper_id=@id AND room_id=@roomId LIMIT 1", new { id = item.Id, roomId = itemRoom.RoomId });
        if (item.InteractingUser != 0)
        {
            var user = itemRoom.GetRoomUserManager().GetRoomUserByHabbo(item.InteractingUser);
            if (user != null) user.UnlockWalking();
            item.InteractingUser = 0;
        }
    }

    public void OnTrigger(GameClient? session, Item item, int request, bool hasRights)
    {
        var itemRoom = item.GetRoom();
        if (itemRoom == null) return;

        if (item == null || itemRoom == null || session == null || session.GetHabbo() == null)
            return;
        var user = itemRoom.GetRoomUserManager().GetRoomUserByHabbo(session.GetHabbo().Id);
        if (user == null) return;

        // Alright. But is this user in the right position?
        if (AtEntry(user, item)) TryEnter(item, user);
        else if (user.CanWalk) user.ApproachItem(item, ActionKind);
    }

    public bool StartFromApproach(Item item, RoomUser user)
        => user.GetClient()?.GetHabbo() != null && AtEntry(user, item) && TryEnter(item, user);

    private static bool AtEntry(RoomUser user, Item item)
        => user.Coordinate == item.Coordinate || user.Coordinate == item.SquareInFront;

    // Fine. But is this tele even free?
    private static bool TryEnter(Item item, RoomUser user)
    {
        if (item.InteractingUser != 0) return false;
        user.TeleDelay = 2;
        item.InteractingUser = user.GetClient().GetHabbo().Id;
        return true;
    }

    public void OnWiredTrigger(Item item) { }
}
