namespace Plus.HabboHotel.Rooms.PathFinding;

internal sealed class CommandIntake(MovementContext context, ForcePlacementService placement)
{
    public void Consume(RoomUser actor)
    {
        var state = actor.Movement;
        var command = state.Commands.Read();
        if (command == null || command.Sequence <= state.ConsumedSequence) return;
        state.ConsumedSequence = command.Sequence;
        if (actor.Frozen || !actor.CanWalk && command.Origin == MoveOrigin.User) return;
        if ((command.Flags & MoveFlags.Teleport) != 0) { placement.Teleport(actor, command); return; }
        state.Fallback.Begin(command.Sequence);
        context.Approaches.Bind(actor, command, ApproachSurface(command));
        state.GoalRevision++; state.Origin = command.Origin; state.Flags = command.Flags;
        state.Route.Clear(); state.Cursor = 0; state.HasIntent = true;
        state.WaitTicks = state.BlockReplans = state.StallTicks = 0;
        actor.GoalX = command.X; actor.GoalY = command.Y;
        state.Profile.Interaction = command.Origin == MoveOrigin.Interaction
            ? new(actor.X, actor.Y, command.X, command.Y) : null;
        actor.UnIdle(); actor.FreezeInteracting = false;
        ResolveGoal(actor);
        context.Replan(actor); context.RefreshMembership(actor);
    }
    // Resolved here, on the room task after dirty tiles were applied, never from the click thread.
    private SurfaceRef? ApproachSurface(MoveCommand command)
    {
        var grid = context.Grid;
        if (command.Approach == null || !grid.InBounds(command.X, command.Y)) return null;
        var tile = grid.Tile(command.X, command.Y);
        if (!grid.Layered) return grid.Active(tile) ? grid.Reference(tile) : null;
        // Layered: the approach surface is the one the item stands level with, not the tile's own slot.
        var itemZ = context.Navigation.Inputs.Read(command.Approach.ItemId)?.Z ?? grid.BaseZ[tile];
        var slot = SurfaceSelection.Resting(grid, tile, itemZ);
        return slot >= 0 ? grid.Reference(slot) : null;
    }
    private void ResolveGoal(RoomUser actor)
    {
        var state = actor.Movement; var grid = context.Grid;
        var profile = context.Profiles.Refresh(actor);
        var goal = state.Origin == MoveOrigin.Interaction && grid.InBounds(actor.GoalX, actor.GoalY)
            ? new AcceptedGoal(actor.GoalX, actor.GoalY, grid.TopSlot(grid.Tile(actor.GoalX, actor.GoalY)))
            : GoalResolver.ResolveClick(grid, profile, new(actor.X, actor.Y, state.SupportZ),
                actor.GoalX, actor.GoalY, context.Occupancy(actor));
        goal = ApproachGoal(actor, goal);
        state.AcceptedGoal = GoalIdentity.Capture(grid, goal);
        actor.GoalX = goal.X; actor.GoalY = goal.Y;
    }
    // A layered approach walks to its own surface when that surface is an accepted goal.
    private AcceptedGoal ApproachGoal(RoomUser actor, AcceptedGoal goal)
    {
        if (!context.Grid.Layered || context.Approaches.Peek(actor) is not { } intent) return goal;
        var slot = context.Grid.SlotOf(intent.Surface);
        return goal.Contains(slot) ? new(goal.X, goal.Y, slot) : goal;
    }
}
