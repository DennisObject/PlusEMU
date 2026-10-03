using Plus.Utilities;

namespace Plus.HabboHotel.Rooms.PathFinding;

internal sealed class AnnounceService(Room room, RoomNavigation navigation, MovementContext context, MovementCancellation cancellation)
{
    private readonly BlockedStepPolicy _blocked = new(navigation.Settings, context, cancellation);
    private readonly MovementRules _rules = new(navigation.Grid, navigation.Settings);
    public void Announce(RoomUser actor)
    {
        var state = actor.Movement;
        if (!state.HasIntent || state.Route.Count == 0 || state.PendingCount != 0) return;
        if (actor.Freezed || !actor.CanWalk && state.Origin != MoveOrigin.Interaction) { actor.RemoveStatus("mv"); state.StallTicks = 0; actor.UpdateNeeded = true; return; }
        var profile = MovementProfiles.Refresh(room, navigation.Grid, navigation.Settings, actor);
        var from = new NavPosition(actor.X, actor.Y, state.SupportZ);
        var limit = actor.SuperFastWalking ? 3 : actor.FastWalking ? 2 : 1;
        for (var i = state.Cursor; i < state.Route.Count && state.PendingCount < limit; i++)
        {
            var surface = state.Route.Steps[i];
            if (navigation.Grid.Reference(surface.Tile) != surface) break;
            var target = navigation.Grid.Position(surface.Tile);
            var purpose = state.Origin == MoveOrigin.Interaction ? StepPurpose.Interaction
                : i == state.Route.Count - 1 ? StepPurpose.Goal : StepPurpose.Transit;
            if (!_rules.CanStep(profile, from, target, purpose, OccupancyView.Execution, context.OccupancyAt(actor, surface.Tile)).Ok) break;
            var kind = ClaimKindFor(profile, surface, purpose);
            var mask = ClaimMatrix.BlockingMask(profile, navigation.Grid.Flags[surface.Tile], purpose, OccupancyView.Execution);
            if (!context.Claims.TryClaim(actor, surface.Tile, kind, mask)) break;
            state.Pending[state.PendingCount++] = surface; from = target;
        }
        if (state.PendingCount == 0) { _blocked.Handle(actor); return; }
        Publish(actor, from);
    }
    private ClaimKind ClaimKindFor(ActorProfile profile, SurfaceRef surface, StepPurpose purpose)
    {
        if (profile.LegacyOverride || profile.IgnoreUsers || (navigation.Grid.Flags[surface.Tile] & NavFlags.Door) != 0) return ClaimKind.Shared;
        return !profile.Walkthrough ? ClaimKind.Exclusive : purpose == StepPurpose.Goal ? ClaimKind.Goal : ClaimKind.Shared;
    }
    private void Publish(RoomUser actor, NavPosition target)
    {
        var state = actor.Movement;
        actor.Statusses.Remove("sit"); actor.Statusses.Remove("lay"); actor.IsSitting = actor.IsLying = false;
        var z = target.Z + PostureService.RiderOffset(actor, state.Pending[state.PendingCount - 1]);
        actor.SetStatus("mv", $"{target.X},{target.Y},{TextHandling.GetString(z)}");
        actor.RotBody = actor.RotHead = Rotation.Calculate(actor.X, actor.Y, target.X, target.Y, actor.MoonwalkEnabled);
        actor.SetStep = true; actor.SetX = target.X; actor.SetY = target.Y; actor.SetZ = target.Z;
        actor.IsWalking = true; actor.UpdateNeeded = true;
    }
}
