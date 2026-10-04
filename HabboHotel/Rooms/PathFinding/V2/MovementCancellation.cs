namespace Plus.HabboHotel.Rooms.PathFinding;

internal sealed class MovementCancellation(MovementContext context)
{
    internal void Cancel(RoomUser actor, long discardThrough = 0)
    {
        context.Approaches.Cancel(actor);
        Finish(actor, discardThrough);
    }
    // Movement ended at its goal: the approach intent, if any, is completed by the caller instead of dropped.
    internal void Finish(RoomUser actor, long discardThrough = 0)
    {
        var state = actor.Movement;
        state.GoalRevision++; state.AcceptedGoal = null; state.RouteInvalidated = false; state.HasIntent = false; state.Route.Clear(); state.Cursor = 0;
        state.PendingCount = 0; state.WaitTicks = state.BlockReplans = state.StallTicks = 0;
        state.ConsumedSequence = Math.Max(state.ConsumedSequence, discardThrough);
        state.Fallback.Reset();
        actor.GoalX = actor.X; actor.GoalY = actor.Y; actor.IsWalking = false;
        actor.SetStep = false; actor.PathRecalcNeeded = false; actor.Path.Clear();
        actor.RemoveStatus("mv"); actor.UpdateNeeded = true;
        StopHorse(actor);
        context.Claims.ReleaseBatch(actor); context.Scheduler.Remove(actor);
        context.RefreshMembership(actor);
    }
    private void StopHorse(RoomUser actor)
    {
        if (actor.IsBot || !actor.RidingHorse) return;
        var horse = context.Room.GetRoomUserManager().GetRoomUserByVirtualId(actor.HorseId);
        if (horse == null) return;
        horse.RemoveStatus("mv"); horse.IsWalking = false; horse.SetStep = false; horse.UpdateNeeded = true;
    }
    internal void Remove(RoomUser actor)
    {
        Cancel(actor); actor.Movement.State = NavState.Removing;
        actor.Movement.LocationRevision++;
        context.Claims.Remove(actor);
        context.Room.GetGameMap().RemoveUserFromMap(actor, new(actor.X, actor.Y));
    }

}
