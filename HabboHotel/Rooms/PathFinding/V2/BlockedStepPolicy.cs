namespace Plus.HabboHotel.Rooms.PathFinding;

internal sealed class BlockedStepPolicy(PathfindingSettings settings, MovementCancellation cancellation, RouteFallbackService fallback)
{
    public void TickStall(RoomUser actor, bool committed)
    {
        var state = actor.Movement;
        if (!state.HasIntent || committed || actor.Freezed || !actor.CanWalk)
        { state.StallTicks = 0; return; }
        // FIFO guarantees the search starts; waiting for it is not a stall.
        if (fallback.AwaitsUnstartedSearch(actor)) return;
        if (++state.StallTicks >= settings.MaxWalkStallTicks) cancellation.Cancel(actor);
    }
    public void Handle(RoomUser actor, bool temporaryBlock)
    {
        var state = actor.Movement;
        actor.RemoveStatus("mv"); actor.UpdateNeeded = true;
        state.WaitTicks++;
        if (temporaryBlock && state.WaitTicks <= settings.BlockWaitTicks) return;
        // A truncated route never searches again, so the replan limit cannot end its prefix.
        if (state.Fallback.State != RouteState.Truncated && state.BlockReplans >= settings.MaxBlockReplans)
        { cancellation.Cancel(actor); return; }
        fallback.OnRouteBlocked(actor);
    }
}
