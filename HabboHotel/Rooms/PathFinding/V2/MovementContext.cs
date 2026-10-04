namespace Plus.HabboHotel.Rooms.PathFinding;

internal sealed class MovementContext(Room room, RoomNavigation navigation, LandingEffects landingEffects, FloorEffectService floorEffects, MovementProfileService profiles)
{
    internal Room Room { get; } = room;
    internal RoomNavigation Navigation { get; } = navigation;
    internal NavGrid Grid => Navigation.Grid;
    internal RouteGraph Graph { get; } = new(navigation.Grid);
    internal GuildGateExecution GuildGates { get; } = new(room, navigation.Grid);
    internal LandingEffects LandingEffects { get; } = landingEffects;
    internal FloorEffectService FloorEffects { get; } = floorEffects;
    internal MovementProfileService Profiles { get; } = profiles;
    internal TransportLandingService TransportLandings { get; } = new(room);
    internal GeometryPublicationService Geometry { get; set; } = null!;
    internal ClaimLedger Claims { get; } = new(navigation.Grid);
    internal SearchScheduler<RoomUser> Scheduler { get; } = new();
    internal ApproachIntentRegistry Approaches { get; } = new();
    internal ApproachGoalResolver ApproachGoals => _approachGoals ??= new(this);
    private ApproachGoalResolver? _approachGoals;
    private PlanningOccupancy _executionOccupancy = new(navigation.Grid.SlotCapacity);
    internal void RefreshMembership(RoomUser actor)
    {
        var grid = Navigation.Grid;
        if (!grid.InBounds(actor.X, actor.Y)) { Claims.Remove(actor); return; }
        var tile = grid.Tile(actor.X, actor.Y);
        Claims.EnsureCapacity(grid.SlotCapacity);
        Claims.Move(actor, MembershipSlot(actor.Movement.CurrentRef), tile, IsWalking(actor),
            Group(actor));
    }
    // K=1 keeps the tile slot; a layered reference without a live surface is off-graph.
    private int? MembershipSlot(SurfaceRef? current)
    {
        if (current is not { } surface) return null;
        if (!Grid.Layered) return surface.Tile;
        var slot = Grid.SlotOf(surface);
        return slot >= 0 && Grid.Active(slot) ? slot : null;
    }
    private bool IsWalking(RoomUser actor)
    {
        if (actor.IsBot && actor.RidingHorse)
        {
            var rider = Room.GetRoomUserManager().GetRoomUserByVirtualId(actor.HorseId);
            if (rider is { RidingHorse: true }) return rider.Movement.HasIntent;
        }
        return actor.Movement.HasIntent;
    }
    internal long Group(RoomUser actor)
    {
        if (actor.RidingHorse && actor.IsBot)
        {
            var rider = Room.GetRoomUserManager().GetUserList().FirstOrDefault(a => !a.IsBot && a.RidingHorse && a.HorseId == actor.VirtualId);
            if (rider != null) return rider.Movement.LifetimeId;
        }
        return actor.Movement.LifetimeId;
    }
    internal void Replan(RoomUser actor)
    {
        var state = actor.Movement;
        if (state.HasIntent) Scheduler.Enqueue(actor, state.LifetimeId, state.GoalRevision);
    }
    internal PlanningOccupancy Occupancy(RoomUser actor)
    {
        Claims.EnsureCapacity(Grid.SlotCapacity);
        return Claims.Snapshot(Group(actor));
    }
    internal PlanningOccupancy OccupancyAt(RoomUser actor, int slot, IReadOnlySet<RoomUser>? departing = null)
    {
        Claims.EnsureCapacity(Grid.SlotCapacity);
        if (_executionOccupancy.Targets.Length < Grid.SlotCapacity) _executionOccupancy = new(Grid.SlotCapacity);
        _executionOccupancy.Targets[slot] = Claims.OccupancyAt(slot, Group(actor), departing);
        return _executionOccupancy;
    }
}
