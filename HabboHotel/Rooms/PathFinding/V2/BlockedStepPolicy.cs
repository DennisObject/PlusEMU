namespace Plus.HabboHotel.Rooms.PathFinding;

internal sealed class BlockedStepPolicy(PathfindingSettings settings, MovementContext context, MovementCancellation cancellation)
{
    public void TickStall(RoomUser actor, bool committed)
    {
        var state = actor.Movement;
        if (!state.HasIntent || committed || actor.Freezed || !actor.CanWalk)
        { state.StallTicks = 0; return; }
        if (++state.StallTicks >= settings.MaxWalkStallTicks) cancellation.Cancel(actor);
    }
    public void Handle(RoomUser actor, bool temporaryBlock)
    {
        var state = actor.Movement;
        actor.RemoveStatus("mv"); actor.UpdateNeeded = true;
        state.WaitTicks++;
        if (state.BlockReplans >= settings.MaxBlockReplans)
        { cancellation.Cancel(actor); return; }
        if (temporaryBlock && state.WaitTicks <= settings.BlockWaitTicks) return;
        state.Route.Clear(); state.Cursor = 0; state.BlockReplans++; state.GoalRevision++; state.AcceptedGoal = null;
        context.Replan(actor);
    }
}
