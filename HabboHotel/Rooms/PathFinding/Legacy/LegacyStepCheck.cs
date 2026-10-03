using Plus.HabboHotel.Items;

namespace Plus.HabboHotel.Rooms.PathFinding;

// Execution is today's IsValidStep2 rule. Prefix keeps geometry, gates, corners, height and
// stationary users, but reads the structural map so walking users' tile marks are ignored.
public enum LegacyStepView { Execution, Prefix }

public enum LegacyStepRejection
{
    None, InvalidTile, NotAdjacent, Corner, BotStep, GuildGateUnavailable, GuildGateDenied,
    BlockedState, Height, OccupiedGoal, Occupied
}

// Gate is the guild gate a member may open once the step is actually taken.
public readonly record struct LegacyStepCheck(LegacyStepRejection Rejection, Item? Gate = null)
{
    public bool Ok => Rejection == LegacyStepRejection.None;
}
