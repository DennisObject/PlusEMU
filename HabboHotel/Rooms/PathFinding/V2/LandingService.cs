namespace Plus.HabboHotel.Rooms.PathFinding;

internal sealed class LandingService(Room room, RoomNavigation navigation, MovementContext context, MovementCancellation cancellation)
{
    public void Land(RoomUser actor, SurfaceRef surface)
    {
        var state = actor.Movement; var revision = state.LocationRevision;
        var target = navigation.Grid.Position(surface.Tile);
        room.GetGameMap().UpdateUserMovement(new(actor.X, actor.Y), new(target.X, target.Y), actor);
        foreach (var item in room.GetGameMap().GetCoordinatedItems(new(actor.X, actor.Y)).ToList()) item.UserWalksOffFurni(actor);
        if (state.LocationRevision != revision || state.State != NavState.Active) return;
        actor.InitializePosition(target.X, target.Y, target.Z);
        state.CurrentRef = surface; state.SupportZ = target.Z;
        MirrorHorse(actor, target);
        context.RefreshMembership(actor);
        if (surface.Tile == navigation.Grid.DoorTile && !actor.IsBot)
        {
            room.GetRoomUserManager().RemoveUserFromRoom(actor.GetClient(), true);
            return;
        }
        Arrive(actor, revision);
    }
    private void Arrive(RoomUser actor, long revision)
    {
        var items = room.GetGameMap().GetCoordinatedItems(new(actor.X, actor.Y)).ToList();
        foreach (var item in items)
        {
            item.Interactor.OnWalkOn(actor);
            if (actor.Movement.LocationRevision != revision || actor.Movement.State != NavState.Active) return;
        }
        foreach (var item in items)
        {
            item.UserWalksOnFurni(actor);
            if (actor.Movement.LocationRevision != revision || actor.Movement.State != NavState.Active) return;
        }
        PostureService.Apply(room, navigation.Grid, actor);
        actor.UpdateNeeded = true;
    }
    private void MirrorHorse(RoomUser actor, NavPosition target)
    {
        if (!actor.RidingHorse || actor.IsBot) return;
        var horse = room.GetRoomUserManager().GetRoomUserByVirtualId(actor.HorseId);
        if (horse == null) return;
        horse.InitializePosition(target.X, target.Y, target.Z);
        horse.Movement.SupportZ = target.Z; horse.Movement.CurrentRef = actor.Movement.CurrentRef;
        context.RefreshMembership(horse); horse.UpdateNeeded = true;
    }
}
