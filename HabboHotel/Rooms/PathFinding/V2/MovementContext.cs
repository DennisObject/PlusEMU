namespace Plus.HabboHotel.Rooms.PathFinding;

internal sealed class MovementContext(Room room, RoomNavigation navigation, LandingEffects landingEffects, FloorEffectService floorEffects, MovementProfileService profiles)
{
    internal Room Room { get; } = room;
    internal RoomNavigation Navigation { get; } = navigation;
    internal NavGrid Grid => Navigation.Grid;
    internal LandingEffects LandingEffects { get; } = landingEffects;
    internal FloorEffectService FloorEffects { get; } = floorEffects;
    internal MovementProfileService Profiles { get; } = profiles;
    internal TransportLandingService TransportLandings { get; } = new(room);
    internal GeometryPublicationService Geometry { get; set; } = null!;
    internal ClaimLedger Claims { get; } = new(navigation.Grid.SlotCapacity, navigation.Grid.SlotCapacity);
    internal SearchScheduler<RoomUser> Scheduler { get; } = new();
    private PlanningOccupancy ExecutionOccupancy { get; } = new(navigation.Grid.SlotCapacity);
    internal void RefreshMembership(RoomUser actor)
    {
        var grid = Navigation.Grid;
        if (!grid.InBounds(actor.X, actor.Y)) { Claims.Remove(actor); return; }
        var tile = grid.Tile(actor.X, actor.Y);
        Claims.Move(actor, actor.Movement.CurrentRef?.Tile, tile, IsWalking(actor),
            Group(actor));
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
    internal PlanningOccupancy Occupancy(RoomUser actor) => Claims.Snapshot(Group(actor));
    internal PlanningOccupancy OccupancyAt(RoomUser actor, int slot)
    {
        ExecutionOccupancy.Targets[slot] = Claims.OccupancyAt(slot, Group(actor));
        return ExecutionOccupancy;
    }
}
