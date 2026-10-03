using Plus.Utilities;

namespace Plus.HabboHotel.Rooms.PathFinding;

internal sealed class AnnounceService(Room room, RoomNavigation navigation, MovementContext context, MovementCancellation cancellation)
{
    private readonly BlockedStepPolicy _blocked = new(navigation.Settings, context, cancellation);
    private readonly MovementRules _rules = new(navigation.Grid, navigation.Settings);
    public void Announce(RoomUser actor, bool committed)
    {
        var state = actor.Movement;
        _blocked.TickStall(actor, committed);
        if (!state.HasIntent || state.Route.Count == 0 || state.PendingCount != 0)
        { ClearCompletedAnnouncement(actor); ApplyIdleEffects(actor, committed); return; }
        if (actor.Freezed)
        { actor.RemoveStatus("mv"); actor.UpdateNeeded = true; ApplyIdleEffects(actor, committed); return; }
        var target = BuildBatch(actor, out var temporaryBlock);
        if (state.PendingCount == 0) { _blocked.Handle(actor, temporaryBlock); ApplyIdleEffects(actor, committed); return; }
        Publish(actor, target);
    }
    private static void ClearCompletedAnnouncement(RoomUser actor)
    {
        if (actor.IsBot && actor.RidingHorse) return;
        if (actor.Movement.PendingCount != 0 || !actor.HasStatus("mv")) return;
        actor.RemoveStatus("mv"); actor.UpdateNeeded = true;
    }
    private NavPosition BuildBatch(RoomUser actor, out bool temporaryBlock)
    {
        temporaryBlock = false;
        var state = actor.Movement;
        var profile = context.Profiles.Refresh(actor);
        var from = new NavPosition(actor.X, actor.Y, state.SupportZ);
        var limit = actor.SuperFastWalking ? 3 : actor.FastWalking ? 2 : 1;
        for (var i = state.Cursor; i < state.Route.Count && state.PendingCount < limit; i++)
        {
            var surface = state.Route.Steps[i];
            if (navigation.Grid.Reference(surface.Tile) != surface) break;
            var target = navigation.Grid.Position(surface.Tile);
            var purpose = state.Origin == MoveOrigin.Interaction ? StepPurpose.Interaction
                : i == state.Route.Count - 1 ? StepPurpose.Goal : StepPurpose.Transit;
            if (!ClaimStep(actor, profile, from, target, surface, purpose, out temporaryBlock)) break;
            state.Pending[state.PendingCount++] = surface; from = target;
        }
        return from;
    }
    private bool ClaimStep(RoomUser actor, ActorProfile profile, NavPosition from, NavPosition target,
        SurfaceRef surface, StepPurpose purpose, out bool temporaryBlock)
    {
        temporaryBlock = false;
        var occupancy = context.OccupancyAt(actor, surface.Tile);
        var mask = ClaimMatrix.BlockingMask(profile, navigation.Grid.Flags[surface.Tile], purpose, OccupancyView.Execution);
        var result = _rules.CanStep(profile, from, target, purpose, OccupancyView.Execution, occupancy);
        if (!result.Ok)
        {
            var blockers = occupancy.Targets[surface.Tile] & mask;
            var transient = TargetOccupancy.Walking | TargetOccupancy.ExclusiveClaim | TargetOccupancy.GoalClaim | TargetOccupancy.SharedClaim;
            temporaryBlock = result.Reason == StepReason.Occupied && (blockers & ~transient) == 0;
            return false;
        }
        return context.Claims.TryClaim(actor, surface.Tile, ClaimKindFor(profile, surface, purpose), mask);
    }
    private void ApplyIdleEffects(RoomUser actor, bool committed)
    {
        if (!committed && actor.Movement.PendingCount == 0)
            context.FloorEffects.Apply(actor, actor.X, actor.Y);
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
        context.FloorEffects.Apply(actor, target.X, target.Y);
    }
    public void SynchronizeHorse(RoomUser actor)
    {
        if (!actor.RidingHorse || actor.IsBot) return;
        var horse = room.GetRoomUserManager().GetRoomUserByVirtualId(actor.HorseId);
        if (horse == null) return;
        if (actor.HasStatus("mv"))
        {
            horse.SetStatus("mv", $"{actor.SetX},{actor.SetY},{TextHandling.GetString(actor.SetZ)}");
            horse.SetStep = actor.SetStep;
            horse.SetX = actor.SetX; horse.SetY = actor.SetY; horse.SetZ = actor.SetZ;
            horse.RotBody = actor.RotBody; horse.RotHead = actor.RotHead;
            horse.IsWalking = actor.IsWalking;
        }
        else
        {
            horse.RemoveStatus("mv"); horse.SetStep = false; horse.IsWalking = false;
        }
        horse.UpdateNeeded = true;
        context.RefreshMembership(horse);
    }
}
