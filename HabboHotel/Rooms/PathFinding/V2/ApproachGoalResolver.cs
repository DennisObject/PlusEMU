namespace Plus.HabboHotel.Rooms.PathFinding;

// Layered approach goals (§16.4): the walk ends on the approach tile's surface at the item's level.
// With K=1 the approach surface is the tile's own surface and goals are never narrowed.
internal sealed class ApproachGoalResolver(MovementContext context)
{
    private NavGrid Grid => context.Grid;

    // Resolved on the room task after dirty tiles were applied, never from the click thread.
    internal SurfaceRef? Surface(MoveCommand command)
    {
        if (command.Approach == null || !Grid.InBounds(command.X, command.Y)) return null;
        var tile = Grid.Tile(command.X, command.Y);
        if (!Grid.Layered) return Grid.Active(tile) ? Grid.Reference(tile) : null;
        var itemZ = context.Navigation.Inputs.Read(command.Approach.ItemId)?.Z ?? Grid.BaseZ[tile];
        var slot = SurfaceSelection.Resting(Grid, tile, itemZ);
        return slot >= 0 ? Grid.Reference(slot) : null;
    }

    // At intake the walk is narrowed to the bound approach surface when it is an accepted goal.
    internal AcceptedGoal Narrow(RoomUser actor, AcceptedGoal goal)
    {
        if (!Grid.Layered || context.Approaches.Peek(actor) is not { } intent) return goal;
        var slot = Grid.SlotOf(intent.Surface);
        return goal.Contains(slot) ? new(goal.X, goal.Y, slot) : goal;
    }

    // A goal re-resolved after invalidation keeps the bound surface; if that surface is gone or no
    // longer accepted, the approach is dropped and the goal is invalid instead of widening.
    internal AcceptedGoal Reresolve(RoomUser actor, AcceptedGoal goal)
    {
        if (!Grid.Layered || context.Approaches.Peek(actor) is not { } intent) return goal;
        var slot = Grid.SlotOf(intent.Surface);
        if (slot >= 0 && goal.Contains(slot)) return new(goal.X, goal.Y, slot);
        context.Approaches.Cancel(actor);
        return new(goal.X, goal.Y, -1);
    }
}
