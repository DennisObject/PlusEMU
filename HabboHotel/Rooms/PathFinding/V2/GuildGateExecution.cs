namespace Plus.HabboHotel.Rooms.PathFinding;

// Pure rules authorize the step; accepted execution keeps the legacy gate animation/timer.
internal sealed class GuildGateExecution(Room room, NavGrid grid)
{
    internal void Accept(RoomUser actor, ActorProfile profile, int tile, StepPurpose purpose)
    {
        if ((grid.Flags[tile] & NavFlags.GuildGate) == 0 || profile.LegacyOverride
            || profile.IgnoreUsers || purpose == StepPurpose.Interaction) return;
        var habbo = actor.GetClient()?.GetHabbo();
        var gate = room.GetRoomItemHandler().GetItem(grid.SupportItem[tile]);
        if (habbo == null || gate == null) return;
        gate.InteractingUser = habbo.Id;
        gate.LegacyDataString = "1";
        gate.UpdateState(false, true);
        gate.RequestUpdate(4, true);
    }
}
