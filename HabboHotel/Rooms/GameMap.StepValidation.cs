using Plus.HabboHotel.Items;
using Plus.HabboHotel.Rooms.PathFinding;

namespace Plus.HabboHotel.Rooms;

public partial class Gamemap
{
    // Side-effect-free legacy step validation; IsValidStep2 applies the effects afterwards.
    public LegacyStepCheck IsValidStepPure(RoomUser user, Vector2D from, Vector2D to, bool endOfPath, bool @override,
        LegacyStepView view = LegacyStepView.Execution)
    {
        if (!ValidTile(to.X, to.Y)) return new(LegacyStepRejection.InvalidTile);
        // Temporary stress bots keep walls and height; only occupancy is ignored.
        if (@override && user.BotData?.IsTemporary == true)
            return IsValidBotStep(from, to, endOfPath) ? default : new(LegacyStepRejection.BotStep);
        if (@override) return default;
        if (!ValidTile(from.X, from.Y) || !TilesTouching(from.X, from.Y, to.X, to.Y)) return new(LegacyStepRejection.NotAdjacent);
        if (!ValidCorner(from, to)) return new(LegacyStepRejection.Corner);
        var items = GetAllRoomItemForSquare(to.X, to.Y);
        if (items.Count > 0 && WalkMagicAt(to.X, to.Y) == null
            && items.FirstOrDefault(x => x.Definition.InteractionType == InteractionType.GuildGate) is { } gate)
            return GuildGateAccess(user, gate);
        if (IsBlockedState(to, endOfPath, items, view)) return new(LegacyStepRejection.BlockedState);
        if (SqAbsoluteHeight(to.X, to.Y) - SqAbsoluteHeight(from.X, from.Y) > 1.5 && !user.RidingHorse)
            return new(LegacyStepRejection.Height);
        return view == LegacyStepView.Prefix ? PrefixOccupancy(user, to, endOfPath) : ExecutionOccupancy(to, endOfPath);
    }

    internal void ApplyStepEffects(RoomUser user, LegacyStepCheck check)
    {
        if (check.Gate != null) { OpenGuildGate(user, check.Gate); return; }
        if (check.Rejection is not (LegacyStepRejection.GuildGateDenied or LegacyStepRejection.BlockedState)) return;
        if (user.Path.Count > 0)
            user.Path.Clear();
        user.PathRecalcNeeded = check.Rejection == LegacyStepRejection.BlockedState;
    }

    private static LegacyStepCheck GuildGateAccess(RoomUser user, Item gate)
    {
        if (!PlusEnvironment.Game.GroupManager.TryGetGroup(gate.GroupId, out var group))
            return new(LegacyStepRejection.GuildGateUnavailable);
        if (user.GetClient() == null || user.GetClient().GetHabbo() == null)
            return new(LegacyStepRejection.GuildGateUnavailable);
        return group.IsMember(user.GetClient().GetHabbo().Id) ? new(LegacyStepRejection.None, gate)
            : new(LegacyStepRejection.GuildGateDenied);
    }

    private static void OpenGuildGate(RoomUser user, Item gate)
    {
        gate.InteractingUser = user.GetClient().GetHabbo().Id;
        gate.LegacyDataString = "1";
        gate.UpdateState(false, true);
        gate.RequestUpdate(4, true);
    }

    /*
     * 0 = blocked
     * 1 = open
     * 2 = last step
     * 3 = door
     * */
    private bool IsBlockedState(Vector2D to, bool endOfPath, List<Item> items, LegacyStepView view)
    {
        var state = view == LegacyStepView.Prefix ? StructuralTile(to.X, to.Y) : GameMap[to.X, to.Y];
        return state == 3 && !endOfPath && !TopIsSeat(items) || state == 0 || state == 2 && !endOfPath;
    }

    private static bool TopIsSeat(List<Item> items)
    {
        var chair = false;
        double highestZ = -1;
        foreach (var item in items.ToList())
        {
            if (item == null)
                continue;
            if (item.GetZ < highestZ)
            {
                chair = false;
                continue;
            }
            highestZ = item.GetZ;
            if (item.Definition.IsSeat)
                chair = true;
        }
        return chair;
    }

    private LegacyStepCheck ExecutionOccupancy(Vector2D to, bool endOfPath)
    {
        var occupant = _room.GetRoomUserManager().GetUserForSquare(to.X, to.Y);
        return occupant != null && !occupant.IsWalking && endOfPath ? new(LegacyStepRejection.OccupiedGoal) : default;
    }

    // Stationary users block per the claim matrix; walkthrough transit and the door pass them.
    private LegacyStepCheck PrefixOccupancy(RoomUser user, Vector2D to, bool endOfPath)
    {
        if (_room.RoomBlockingEnabled && !endOfPath || to.X == Model.DoorX && to.Y == Model.DoorY) return default;
        foreach (var other in GetRoomUsers(new(to.X, to.Y)))
        {
            if (ReferenceEquals(other, user) || other.IsWalking || SameMovementGroup(user, other)) continue;
            return new(endOfPath ? LegacyStepRejection.OccupiedGoal : LegacyStepRejection.Occupied);
        }
        return default;
    }

    private static bool SameMovementGroup(RoomUser user, RoomUser other)
        => user.RidingHorse && other.VirtualId == user.HorseId || other.RidingHorse && other.HorseId == user.VirtualId;
}
