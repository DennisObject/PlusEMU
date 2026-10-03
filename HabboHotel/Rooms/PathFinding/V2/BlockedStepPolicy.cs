namespace Plus.HabboHotel.Rooms.PathFinding;

internal sealed class BlockedStepPolicy(PathfindingSettings settings, MovementContext context, MovementCancellation cancellation)
{
    public void Handle(RoomUser actor)
    {
        var state = actor.Movement;
        actor.RemoveStatus("mv"); actor.UpdateNeeded = true;
        state.WaitTicks++; state.StallTicks++;
        if (state.StallTicks >= settings.MaxWalkStallTicks || state.BlockReplans >= settings.MaxBlockReplans)
        { cancellation.Cancel(actor); return; }
        if (state.WaitTicks <= settings.BlockWaitTicks) return;
        state.Route.Clear(); state.Cursor = 0; state.BlockReplans++; state.GoalRevision++;
        context.Replan(actor);
    }}
