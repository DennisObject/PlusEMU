namespace Plus.HabboHotel.Rooms.PathFinding;

internal sealed class CommitService(Room room, RoomNavigation navigation, MovementContext context, MovementCancellation cancellation)
{
    private readonly MovementRules _rules = new(navigation.Grid, navigation.Settings);
    private readonly LandingService _landing = new(room, navigation, context, cancellation);
    public bool Commit(RoomUser actor)
    {
        var state = actor.Movement;
        if (state.PendingCount == 0) return false;
        if (ReplacedSeatGoal(actor)) { cancellation.Cancel(actor); return false; }
        var revision = state.LocationRevision;
        var count = state.PendingCount;
        var accepted = ValidPrefix(actor);
        state.PendingCount = 0; actor.SetStep = false;
        if (accepted > 0) _landing.Land(actor, state.Pending[accepted - 1]);
        context.Claims.ReleaseBatch(actor);
        if (state.State != NavState.Active || state.LocationRevision != revision) return accepted > 0;
        Advance(actor, accepted, count);
        return accepted > 0;
    }
    private static bool ReplacedSeatGoal(RoomUser actor)
    {
        var state = actor.Movement;
        var target = state.Pending[state.PendingCount - 1];
        var command = state.Commands.Read();
        return target.Kind == SurfaceKind.SeatBase && state.Route.GoalSurface == target
            && command != null && command.Sequence > state.ConsumedSequence && !actor.Frozen
            && (actor.CanWalk || command.Origin != MoveOrigin.User);
    }
    private void Advance(RoomUser actor, int accepted, int count)
    {
        var state = actor.Movement;
        state.Cursor += accepted;
        if (accepted > 0) state.WaitTicks = state.BlockReplans = state.StallTicks = 0;
        if (accepted != count)
        {
            actor.RemoveStatus("mv"); actor.UpdateNeeded = true;
            state.Route.Clear(); state.Cursor = 0; state.GoalRevision++;
            context.Replan(actor);
        }
        if (state.Cursor >= state.Route.Count && accepted == count) cancellation.Cancel(actor);
    }
    private int ValidPrefix(RoomUser actor)
    {
        var state = actor.Movement;
        var profile = MovementProfiles.Refresh(room, navigation.Grid, navigation.Settings, actor);
        var from = new NavPosition(actor.X, actor.Y, state.SupportZ);
        var accepted = 0;
        for (var i = 0; i < state.PendingCount; i++)
        {
            var step = state.Pending[i];
            if (navigation.Grid.Reference(step.Tile) != step) break;
            var to = navigation.Grid.Position(step.Tile);
            var purpose = state.Origin == MoveOrigin.Interaction ? StepPurpose.Interaction
                : to.X == actor.GoalX && to.Y == actor.GoalY ? StepPurpose.Goal : StepPurpose.Transit;
            if (!_rules.CanStep(profile, from, to, purpose, OccupancyView.Execution, context.OccupancyAt(actor, step.Tile)).Ok) break;
            accepted++; from = to;
        }
        return accepted;
    }
}
