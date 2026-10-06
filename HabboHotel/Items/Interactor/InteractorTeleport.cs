using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.PathFinding;

namespace Plus.HabboHotel.Items.Interactor;

public class InteractorTeleport(TimeProvider clock) : IFurniInteractor, IApproachInteractor
{
    public int ActionKind => ApproachActionKind.Teleporter;

    public void OnPlace(GameClient? session, Item item)
    {
        var itemRoom = item.GetRoom();

        if (itemRoom == null) {
            return;
        }

        item.LegacyDataString = "0";

        if (item.InteractingUser != 0) {
            var user = itemRoom.GetRoomUserManager().GetRoomUserByHabbo(item.InteractingUser);

            if (user != null) {
                user.ClearMovement(true);
                user.AllowOverride = false;
                user.CanWalk = true;
            }

            item.InteractingUser = 0;
        }

        if (item.InteractingUser2 != 0) {
            var user = itemRoom.GetRoomUserManager().GetRoomUserByHabbo(item.InteractingUser2);

            if (user != null) {
                user.ClearMovement(true);
                user.AllowOverride = false;
                user.CanWalk = true;
            }

            item.InteractingUser2 = 0;
        }
    }

    public void OnRemove(GameClient? session, Item item)
    {
        var itemRoom = item.GetRoom();

        if (itemRoom == null) {
            return;
        }

        item.LegacyDataString = "0";

        if (item.InteractingUser != 0) {
            var user = itemRoom.GetRoomUserManager().GetRoomUserByHabbo(item.InteractingUser);

            if (user != null) {
                user.UnlockWalking();
            }

            item.InteractingUser = 0;
        }

        if (item.InteractingUser2 != 0) {
            var user = itemRoom.GetRoomUserManager().GetRoomUserByHabbo(item.InteractingUser2);

            if (user != null) {
                user.UnlockWalking();
            }

            item.InteractingUser2 = 0;
        }
    }

    public void OnTrigger(GameClient? session, Item item, int request, bool hasRights)
    {
        var itemRoom = item.GetRoom();

        if (itemRoom == null) {
            return;
        }

        if (item == null || itemRoom == null || session == null || session.GetHabbo() == null) {
            return;
        }

        var user = itemRoom.GetRoomUserManager().GetRoomUserByHabbo(session.GetHabbo().Id);

        if (user == null) {
            return;
        }

        var now = clock.GetUtcNow();
        user.LastInteractionAt = now;

        // Alright. But is this user in the right position?
        if (AtEntry(user, item)) {
            TryEnter(item, user, session.GetHabbo(), now);
        }
        else if (user.CanWalk) {
            user.ApproachItem(item, ActionKind);
        }
    }

    public bool StartFromApproach(Item item, RoomUser user)
    {
        if (user.GetClient()?.GetHabbo() is not { } habbo) {
            return false;
        }

        var now = clock.GetUtcNow();
        user.LastInteractionAt = now;

        return AtEntry(user, item) && TryEnter(item, user, habbo, now);
    }

    private static bool AtEntry(RoomUser user, Item item)
        => user.Coordinate == item.Coordinate || user.Coordinate == item.SquareInFront;

    // Fine. But is this tele even free?
    private static bool TryEnter(Item item, RoomUser user, Plus.HabboHotel.Users.Habbo habbo, DateTimeOffset now)
    {
        if (item.InteractingUser != 0) {
            return false;
        }

        if (!user.CanWalk || habbo.IsTeleporting || habbo.TeleporterId != 0 ||
            !IsInteractionCurrent(user.LastInteractionAt, now)) {
            return false;
        }

        user.TeleDelay = 2;
        item.InteractingUser = user.GetClient().GetHabbo().Id;

        return true;
    }

    internal static bool IsInteractionCurrent(DateTimeOffset? interactionAt, DateTimeOffset now)
        => interactionAt is { } timestamp && now - timestamp <= TimeSpan.FromSeconds(2);

    public void OnWiredTrigger(Item item)
    {
    }
}
