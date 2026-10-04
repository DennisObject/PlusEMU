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
        // Sequenced like every gate write; the user and timer apply at the commit boundary.
        GateTransitionService.Apply(gate, "1", GateCloseReason.Walk, persist: false, afterWrite: opened =>
        {
            opened.InteractingUser = habbo.Id;
            opened.RequestUpdate(4, true);
        });
    }
}
