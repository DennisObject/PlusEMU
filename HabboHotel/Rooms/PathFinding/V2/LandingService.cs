namespace Plus.HabboHotel.Rooms.PathFinding;

internal sealed class LandingService(Room room, RoomNavigation navigation, MovementContext context, MovementCancellation cancellation)
{
    public void Land(RoomUser actor, SurfaceRef surface, GraphView view)
    {
        var state = actor.Movement;
        var revision = state.LocationRevision;
        var wasLaying = actor.HasStatus("lay");
        var initial = context.Graph.Position(surface, view);
        room.GetGameMap().UpdateUserMovement(new(actor.X, actor.Y), new(initial.X, initial.Y), actor);

        if (!LeaveOrigin(actor, revision))
        {
            return;
        }

        navigation.ApplyDirty();
        var landing = context.Graph.ResolveLanding(surface, view, initial.Z);
        SetLandingPosition(actor, landing);

        if (surface.Tile == navigation.Grid.DoorTile && !actor.IsBot)
        {
            room.GetRoomUserManager().RemoveUserFromRoom(actor.GetClient(), true);

            return;
        }

        Arrive(actor, revision, wasLaying);
    }
    private bool LeaveOrigin(RoomUser actor, long revision)
    {
        foreach (var item in SurfaceContacts.Of(room, actor, room.GetGameMap().GetCoordinatedItems(new(actor.X, actor.Y))))
        {
            item.UserWalksOffFurni(actor);

            if (actor.Movement.LocationRevision != revision || actor.Movement.State != NavState.Active)
            {
                return false;
            }
        }

        return actor.Movement.LocationRevision == revision && actor.Movement.State == NavState.Active;
    }
    private void SetLandingPosition(RoomUser actor, LandingSurface landing)
    {
        var target = landing.Position;
        var state = actor.Movement;
        actor.InitializePosition(target.X, target.Y, target.Z + context.Graph.RiderOffset(actor, target.Slot));
        state.CurrentRef = landing.Support;
        state.SupportZ = target.Z;
        state.BoundVersion = navigation.Grid.Version;
        MirrorHorse(actor, target);
        context.RefreshMembership(actor);
    }
    private void Arrive(RoomUser actor, long revision, bool wasLaying)
    {
        var items = SurfaceContacts.Of(room, actor, room.GetGameMap().GetCoordinatedItems(new(actor.X, actor.Y)));

        foreach (var item in items)
        {
            item.Interactor.OnWalkOn(actor);

            if (actor.Movement.LocationRevision != revision || actor.Movement.State != NavState.Active)
            {
                return;
            }
        }

        foreach (var item in items)
        {
            item.UserWalksOnFurni(actor);

            if (actor.Movement.LocationRevision != revision || actor.Movement.State != NavState.Active)
            {
                return;
            }
        }

        PostureService.Apply(room, navigation.Grid, actor);
        context.LandingEffects.Apply(actor, wasLaying);
        actor.UpdateNeeded = true;
    }
    private void MirrorHorse(RoomUser actor, NavPosition target)
    {
        if (!actor.RidingHorse || actor.IsBot)
        {
            return;
        }

        var horse = room.GetRoomUserManager().GetRoomUserByVirtualId(actor.HorseId);

        if (horse == null)
        {
            return;
        }

        room.GetGameMap().UpdateUserMovement(new(horse.X, horse.Y), new(target.X, target.Y), horse);
        horse.InitializePosition(target.X, target.Y, target.Z);
        horse.Movement.SupportZ = target.Z;
        horse.Movement.CurrentRef = actor.Movement.CurrentRef;
        context.RefreshMembership(horse);
        horse.UpdateNeeded = true;
    }
}
