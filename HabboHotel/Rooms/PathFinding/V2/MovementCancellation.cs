namespace Plus.HabboHotel.Rooms.PathFinding;

internal sealed class MovementCancellation(MovementContext context)
{
    internal void Cancel(RoomUser actor, long discardThrough = 0)
    {
        var state = actor.Movement;
        state.GoalRevision++; state.HasIntent = false; state.Route.Clear(); state.Cursor = 0;
        state.PendingCount = 0; state.WaitTicks = state.BlockReplans = state.StallTicks = 0;
        state.ConsumedSequence = Math.Max(state.ConsumedSequence, discardThrough);
        actor.GoalX = actor.X; actor.GoalY = actor.Y; actor.IsWalking = false;
        actor.SetStep = false; actor.PathRecalcNeeded = false; actor.Path.Clear();
        actor.RemoveStatus("mv"); actor.UpdateNeeded = true;
        context.Claims.ReleaseBatch(actor); context.Scheduler.Remove(actor);
        context.RefreshMembership(actor);
    }
    internal void Remove(RoomUser actor)
    {
        Cancel(actor); actor.Movement.State = NavState.Removing;
        actor.Movement.LocationRevision++;
        context.Claims.Remove(actor);
        context.Room.GetGameMap().RemoveUserFromMap(actor, new(actor.X, actor.Y));
    }

}
